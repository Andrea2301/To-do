using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DoItNow.Services.Drive
{
    /// <summary>Token OAuth de Google con su fecha de expiración.</summary>
    public sealed class GoogleToken
    {
        public string AccessToken { get; init; } = string.Empty;
        public string RefreshToken { get; init; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; init; }
        public string Scope { get; init; } = string.Empty;

        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt.AddSeconds(-60);
    }

    /// <summary>
    /// Error devuelto por el endpoint de tokens. Conserva el código OAuth
    /// (<c>invalid_grant</c>, <c>invalid_client</c>…) para que la capa superior decida
    /// si debe desconectar la cuenta.
    /// </summary>
    public sealed class GoogleAuthException : InvalidOperationException
    {
        public string ErrorCode { get; }

        public GoogleAuthException(string message, string errorCode) : base(message)
        {
            ErrorCode = errorCode ?? string.Empty;
        }
    }

    /// <summary>
    /// OAuth 2.0 para aplicaciones de escritorio: Authorization Code + PKCE con redirect
    /// a loopback (127.0.0.1). No usa un servidor propio ni guarda secretos en el repo.
    /// </summary>
    public sealed class GoogleDriveAuth
    {
        public const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
        public const string DriveAppDataScope = "https://www.googleapis.com/auth/drive.appdata";

        private readonly HttpClient _http;

        public GoogleDriveAuth(HttpClient? http = null)
        {
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        }

        public static string CreateCodeVerifier() =>
            Base64Url(RandomNumberGenerator.GetBytes(32));

        public static string CreateCodeChallenge(string verifier) =>
            Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        public static string BuildAuthorizeUrl(
            string clientId, string redirectUri, string codeChallenge, string state)
        {
            var query = new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["scope"] = DriveAppDataScope,
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256",
                ["access_type"] = "offline",
                ["prompt"] = "consent",
                ["state"] = state
            };

            var builder = new StringBuilder(AuthorizeEndpoint);
            builder.Append('?');
            var first = true;
            foreach (var pair in query)
            {
                if (!first) builder.Append('&');
                builder.Append(Uri.EscapeDataString(pair.Key));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(pair.Value));
                first = false;
            }

            return builder.ToString();
        }

        /// <summary>
        /// Abre el navegador, espera el callback en 127.0.0.1 y canjea el código por tokens.
        /// </summary>
        public async Task<GoogleToken> AuthorizeInteractiveAsync(
            string clientId,
            string clientSecret,
            Action<string> openBrowser,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException("Falta el Client ID de Google.", nameof(clientId));

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var redirectUri = $"http://127.0.0.1:{port}";
                var verifier = CreateCodeVerifier();
                var challenge = CreateCodeChallenge(verifier);
                var state = Base64Url(RandomNumberGenerator.GetBytes(16));

                openBrowser(BuildAuthorizeUrl(clientId, redirectUri, challenge, state));

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMinutes(3));

                string code;
                try
                {
                    code = await WaitForCodeAsync(listener, state, timeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        "La autorización no se completó en 3 minutos. Inténtalo de nuevo.");
                }

                return await ExchangeCodeAsync(
                    clientId, clientSecret, code, redirectUri, verifier, timeout.Token);
            }
            finally
            {
                listener.Stop();
            }
        }

        public async Task<GoogleToken> ExchangeCodeAsync(
            string clientId,
            string clientSecret,
            string code,
            string redirectUri,
            string codeVerifier,
            CancellationToken cancellationToken = default)
        {
            var form = new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["code"] = code,
                ["code_verifier"] = codeVerifier,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri
            };

            return await PostTokenAsync(form, cancellationToken);
        }

        public async Task<GoogleToken> RefreshAsync(
            string clientId,
            string clientSecret,
            string refreshToken,
            CancellationToken cancellationToken = default)
        {
            var form = new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token"
            };

            return await PostTokenAsync(form, cancellationToken);
        }

        private async Task<GoogleToken> PostTokenAsync(
            Dictionary<string, string> form, CancellationToken cancellationToken)
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await _http.PostAsync(TokenEndpoint, content, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var (code, message) = DescribeError(json);
                throw new GoogleAuthException(message, code);
            }

            return ParseToken(json, DateTimeOffset.UtcNow);
        }

        public static GoogleToken ParseToken(string json, DateTimeOffset now)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("access_token", out var accessToken) ||
                string.IsNullOrWhiteSpace(accessToken.GetString()))
            {
                var (code, message) = DescribeError(json);
                throw new GoogleAuthException(message, code);
            }

            var expiresIn = root.TryGetProperty("expires_in", out var expires) &&
                            expires.TryGetInt32(out var seconds)
                ? seconds
                : 3600;

            return new GoogleToken
            {
                AccessToken = accessToken.GetString()!,
                RefreshToken = root.TryGetProperty("refresh_token", out var refresh)
                    ? refresh.GetString() ?? string.Empty
                    : string.Empty,
                Scope = root.TryGetProperty("scope", out var scope)
                    ? scope.GetString() ?? string.Empty
                    : string.Empty,
                ExpiresAt = now.AddSeconds(expiresIn)
            };
        }

        private static (string Code, string Message) DescribeError(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var code = string.Empty;
                if (root.TryGetProperty("error", out var error))
                {
                    code = error.ValueKind == JsonValueKind.String
                        ? error.GetString() ?? string.Empty
                        : string.Empty;
                }

                if (root.TryGetProperty("error_description", out var description) &&
                    !string.IsNullOrWhiteSpace(description.GetString()))
                {
                    return (code, description.GetString()!);
                }

                if (!string.IsNullOrWhiteSpace(code))
                    return (code, code);

                if (root.TryGetProperty("error", out var errorObject) &&
                    errorObject.ValueKind != JsonValueKind.String)
                {
                    return (string.Empty, errorObject.ToString());
                }
            }
            catch
            {
                // Respuesta no JSON: se usa el mensaje genérico.
            }

            return (string.Empty, "Google rechazó la solicitud de autorización.");
        }

        private static async Task<string> WaitForCodeAsync(
            TcpListener listener, string expectedState, CancellationToken cancellationToken)
        {
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken);
                using var stream = client.GetStream();

                var request = await ReadRequestAsync(stream, cancellationToken);
                var target = ParseTarget(request);

                if (target == null)
                {
                    await WriteResponseAsync(stream, 400, ErrorPage, cancellationToken);
                    continue;
                }

                var query = ParseQuery(target);
                var hasCode = query.TryGetValue("code", out var code) &&
                              !string.IsNullOrWhiteSpace(code);
                var hasError = query.TryGetValue("error", out var error) &&
                               !string.IsNullOrWhiteSpace(error);

                if (!hasCode && !hasError)
                {
                    // Suele ser /favicon.ico: se responde y se sigue esperando.
                    await WriteResponseAsync(stream, 404, ErrorPage, cancellationToken);
                    continue;
                }

                await WriteResponseAsync(
                    stream,
                    hasCode ? 200 : 400,
                    hasCode ? SuccessPage : ErrorPage,
                    cancellationToken);

                if (hasError)
                {
                    throw new InvalidOperationException(
                        query.TryGetValue("error_description", out var description)
                            ? description
                            : $"Google devolvió el error '{error}'.");
                }

                if (!query.TryGetValue("state", out var state) ||
                    !string.Equals(state, expectedState, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "El parámetro state no coincide; la autorización se canceló por seguridad.");
                }

                return code!;
            }
        }

        private static async Task<string> ReadRequestAsync(
            NetworkStream stream, CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            var builder = new StringBuilder();

            while (builder.Length < 16_384)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read <= 0)
                    break;

                builder.Append(Encoding.ASCII.GetString(buffer, 0, read));
                if (builder.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                    break;
            }

            return builder.ToString();
        }

        private static string? ParseTarget(string request)
        {
            // Primera línea: "GET /?code=... HTTP/1.1"
            var endOfLine = request.IndexOf('\r');
            if (endOfLine <= 0)
                return null;

            var line = request[..endOfLine];
            var parts = line.Split(' ');
            return parts.Length >= 2 ? parts[1] : null;
        }

        public static Dictionary<string, string> ParseQuery(string target)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var questionMark = target.IndexOf('?');
            if (questionMark < 0 || questionMark == target.Length - 1)
                return result;

            foreach (var pair in target[(questionMark + 1)..].Split('&'))
            {
                if (pair.Length == 0)
                    continue;

                var separator = pair.IndexOf('=');
                if (separator < 0)
                {
                    result[Uri.UnescapeDataString(pair)] = string.Empty;
                    continue;
                }

                var key = Uri.UnescapeDataString(pair[..separator].Replace('+', ' '));
                var value = Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
                result[key] = value;
            }

            return result;
        }

        private static async Task WriteResponseAsync(
            NetworkStream stream, int status, string html, CancellationToken cancellationToken)
        {
            var body = Encoding.UTF8.GetBytes(html);
            var header =
                $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Bad Request")}\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                "Connection: close\r\n" +
                $"Content-Length: {body.Length}\r\n\r\n";

            var headerBytes = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(headerBytes, cancellationToken);
            await stream.WriteAsync(body, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        public static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        private const string SuccessPage =
            "<!doctype html><html lang=\"es\"><meta charset=\"utf-8\">" +
            "<title>DoItNow</title><body style=\"font-family:Segoe UI,sans-serif;" +
            "background:#20213A;color:#F5F6FF;display:flex;align-items:center;" +
            "justify-content:center;height:100vh;margin:0\">" +
            "<div style=\"text-align:center\"><h2>Cuenta conectada</h2>" +
            "<p>Ya puedes cerrar esta pestaña y volver a DoItNow.</p></div></body></html>";

        private const string ErrorPage =
            "<!doctype html><html lang=\"es\"><meta charset=\"utf-8\">" +
            "<title>DoItNow</title><body style=\"font-family:Segoe UI,sans-serif;" +
            "background:#20213A;color:#F5F6FF;display:flex;align-items:center;" +
            "justify-content:center;height:100vh;margin:0\">" +
            "<div style=\"text-align:center\"><h2>No se pudo conectar</h2>" +
            "<p>Vuelve a DoItNow e inténtalo de nuevo.</p></div></body></html>";
    }
}
