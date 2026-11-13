using DoItNow.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DoItNow.Services.Drive
{
    /// <summary>Operaciones de respaldo que usa <see cref="DriveBackupService"/>.</summary>
    public interface IDriveBackupClient
    {
        Task<IReadOnlyList<DriveBackupFile>> ListAsync(CancellationToken cancellationToken = default);
        Task<DriveBackupFile> UploadAsync(string fileName, byte[] content, CancellationToken cancellationToken = default);
        Task<byte[]> DownloadAsync(string fileId, CancellationToken cancellationToken = default);
        Task DeleteAsync(string fileId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Cliente mínimo de Google Drive v3 sobre la carpeta oculta <c>appDataFolder</c>:
    /// la app solo puede ver sus propios respaldos, nunca el resto del Drive del usuario.
    /// </summary>
    public sealed class GoogleDriveClient : IDriveBackupClient
    {
        public const string ApiBase = "https://www.googleapis.com/drive/v3";
        public const string UploadBase = "https://www.googleapis.com/upload/drive/v3";

        private readonly HttpClient _http;
        private readonly Func<CancellationToken, Task<string>> _accessToken;
        private readonly Action? _invalidateToken;

        public GoogleDriveClient(
            HttpClient http,
            Func<CancellationToken, Task<string>> accessToken,
            Action? invalidateToken = null)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
            _invalidateToken = invalidateToken;
        }

        public async Task<IReadOnlyList<DriveBackupFile>> ListAsync(
            CancellationToken cancellationToken = default)
        {
            var url = $"{ApiBase}/files?spaces=appDataFolder" +
                      "&fields=files(id,name,size,modifiedTime)&orderBy=modifiedTime%20desc&pageSize=100";

            using var response = await SendAsync(HttpMethod.Get, url, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            var files = new List<DriveBackupFile>();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("files", out var array) ||
                array.ValueKind != JsonValueKind.Array)
            {
                return files;
            }

            foreach (var item in array.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                if (!name.EndsWith(".ditvault", StringComparison.OrdinalIgnoreCase))
                    continue;

                files.Add(new DriveBackupFile
                {
                    Id = item.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                    Name = name,
                    SizeBytes = ParseLong(item, "size"),
                    ModifiedAt = ParseDate(item, "modifiedTime")
                });
            }

            return files;
        }

        public async Task<DriveBackupFile> UploadAsync(
            string fileName, byte[] content, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("El nombre del respaldo es obligatorio.", nameof(fileName));

            // Carga reanudable: el multipart simple de Drive está limitado a 5 MB y un vault
            // con adjuntos puede superarlo con facilidad.
            var metadata = JsonSerializer.Serialize(
                new { name = fileName, parents = new[] { "appDataFolder" } });

            var startUrl = $"{UploadBase}/files?uploadType=resumable" +
                           "&fields=id,name,size,modifiedTime";

            Uri? sessionUri;
            using (var metadataContent = new StringContent(metadata, Encoding.UTF8, "application/json"))
            using (var startResponse = await SendAsync(
                HttpMethod.Post,
                startUrl,
                cancellationToken,
                metadataContent,
                request =>
                {
                    request.Headers.Add("X-Upload-Content-Type", "application/octet-stream");
                    request.Headers.Add(
                        "X-Upload-Content-Length",
                        content.Length.ToString(CultureInfo.InvariantCulture));
                }))
            {
                sessionUri = startResponse.Headers.Location;
            }

            if (sessionUri == null)
                throw new InvalidOperationException(
                    "Google Drive no devolvió la URL de carga del respaldo.");

            using var uploadContent = new ByteArrayContent(content);
            uploadContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var response = await SendAsync(
                HttpMethod.Put, sessionUri.ToString(), cancellationToken, uploadContent);

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new DriveBackupFile
            {
                Id = root.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? fileName : fileName,
                SizeBytes = ParseLong(root, "size"),
                ModifiedAt = ParseDate(root, "modifiedTime")
            };
        }

        public async Task<byte[]> DownloadAsync(
            string fileId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileId))
                throw new ArgumentException("Falta el identificador del respaldo.", nameof(fileId));

            var url = $"{ApiBase}/files/{Uri.EscapeDataString(fileId)}?alt=media";
            using var response = await SendAsync(HttpMethod.Get, url, cancellationToken);
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        public async Task DeleteAsync(string fileId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileId))
                throw new ArgumentException("Falta el identificador del respaldo.", nameof(fileId));

            var url = $"{ApiBase}/files/{Uri.EscapeDataString(fileId)}";
            using var response = await SendAsync(HttpMethod.Delete, url, cancellationToken);
        }

        private async Task<HttpResponseMessage> SendAsync(
            HttpMethod method,
            string url,
            CancellationToken cancellationToken,
            HttpContent? content = null,
            Action<HttpRequestMessage>? configure = null)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var request = new HttpRequestMessage(method, url);
                if (content != null)
                    request.Content = content;

                configure?.Invoke(request);

                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer", await _accessToken(cancellationToken));

                var response = await _http.SendAsync(request, cancellationToken);

                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    // El token fue rechazado aunque aún no expiraba: se invalida y se pide otro.
                    response.Dispose();
                    _invalidateToken?.Invoke();
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var status = response.StatusCode;
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    response.Dispose();
                    throw new InvalidOperationException(DescribeError(status, body));
                }

                return response;
            }

            throw new InvalidOperationException(
                "Google Drive rechazó la petición incluso después de renovar el token.");
        }

        private static string DescribeError(HttpStatusCode status, string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var error))
                {
                    if (error.ValueKind == JsonValueKind.Object &&
                        error.TryGetProperty("message", out var message) &&
                        !string.IsNullOrWhiteSpace(message.GetString()))
                    {
                        return $"Google Drive: {message.GetString()}";
                    }

                    if (error.ValueKind == JsonValueKind.String)
                        return $"Google Drive: {error.GetString()}";
                }
            }
            catch
            {
                // Cuerpo no JSON: se usa el código HTTP.
            }

            return $"Google Drive respondió {(int)status} ({status}).";
        }

        private static long ParseLong(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value))
                return 0;

            if (value.ValueKind == JsonValueKind.String &&
                long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return value.TryGetInt64(out var number) ? number : 0;
        }

        private static DateTimeOffset ParseDate(JsonElement element, string property)
        {
            if (element.TryGetProperty(property, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var date))
            {
                return date;
            }

            return DateTimeOffset.MinValue;
        }
    }
}
