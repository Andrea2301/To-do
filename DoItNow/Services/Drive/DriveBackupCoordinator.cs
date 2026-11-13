using DoItNow.Services.Backup;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DoItNow.Services.Drive
{
    /// <summary>
    /// Punto único para conectar la cuenta de Google y crear el almacén de respaldos.
    /// Guarda el client secret y el refresh token cifrados con DPAPI y renueva el access
    /// token solo.
    /// </summary>
    public sealed class DriveBackupCoordinator
    {
        public const string KeyClientId = "Drive_ClientId";
        public const string KeyClientSecret = "Drive_ClientSecret";
        public const string KeyRefreshToken = "Drive_RefreshToken";

        private readonly SettingsService _settings;
        private readonly GoogleDriveAuth _auth;
        private readonly HttpClient _http;
        private GoogleToken? _token;

        public DriveBackupCoordinator(
            SettingsService? settings = null,
            GoogleDriveAuth? auth = null,
            HttpClient? http = null)
        {
            _settings = settings ?? new SettingsService();
            _auth = auth ?? new GoogleDriveAuth();
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        }

        public async Task<bool> HasCredentialsAsync()
        {
            var (clientId, clientSecret) = await GetCredentialsAsync();
            return !string.IsNullOrWhiteSpace(clientId) &&
                   !string.IsNullOrWhiteSpace(clientSecret);
        }

        public async Task<(string ClientId, string ClientSecret)> GetCredentialsAsync()
        {
            var clientId = await _settings.GetSettingAsync(KeyClientId);
            var protectedSecret = await _settings.GetSettingAsync(KeyClientSecret);
            return (clientId, SecretProtector.Unprotect(protectedSecret));
        }

        public async Task SaveCredentialsAsync(string clientId, string clientSecret)
        {
            var (previousId, _) = await GetCredentialsAsync();
            var changed = !string.Equals(previousId, clientId, StringComparison.Ordinal);

            await _settings.SetSettingAsync(KeyClientId, clientId?.Trim() ?? string.Empty);
            await _settings.SetSettingAsync(
                KeyClientSecret, SecretProtector.Protect(clientSecret?.Trim() ?? string.Empty));

            if (changed)
                await DisconnectAsync();
        }

        public async Task<bool> IsConnectedAsync()
        {
            var refreshToken = SecretProtector.Unprotect(
                await _settings.GetSettingAsync(KeyRefreshToken));

            return !string.IsNullOrWhiteSpace(refreshToken);
        }

        public async Task ConnectAsync(
            Action<string> openBrowser, CancellationToken cancellationToken = default)
        {
            var (clientId, clientSecret) = await GetCredentialsAsync();

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException(
                    "Primero guarda el Client ID y el Client Secret de Google.");
            }

            var token = await _auth.AuthorizeInteractiveAsync(
                clientId, clientSecret, openBrowser, cancellationToken);

            if (string.IsNullOrWhiteSpace(token.RefreshToken))
            {
                throw new InvalidOperationException(
                    "Google no devolvió un refresh token. Quita el acceso de la app en tu cuenta " +
                    "de Google y vuelve a conectar para forzar el consentimiento.");
            }

            await _settings.SetSettingAsync(
                KeyRefreshToken, SecretProtector.Protect(token.RefreshToken));

            _token = token;
        }

        public async Task DisconnectAsync()
        {
            _token = null;
            await _settings.SetSettingAsync(KeyRefreshToken, string.Empty);
        }

        /// <summary>Crea el almacén de Drive; exige una cuenta conectada.</summary>
        public async Task<IBackupStore> CreateStoreAsync()
        {
            if (!await IsConnectedAsync())
            {
                throw new InvalidOperationException(
                    "Conecta tu cuenta de Google antes de usar los respaldos.");
            }

            return new GoogleDriveClient(_http, GetAccessTokenAsync, () => _token = null);
        }

        private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            if (_token != null && !_token.IsExpired)
                return _token.AccessToken;

            var (clientId, clientSecret) = await GetCredentialsAsync();
            var refreshToken = SecretProtector.Unprotect(
                await _settings.GetSettingAsync(KeyRefreshToken));

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new InvalidOperationException(
                    "La sesión de Google no está activa. Vuelve a conectar la cuenta.");
            }

            GoogleToken token;
            try
            {
                token = await _auth.RefreshAsync(
                    clientId, clientSecret, refreshToken, cancellationToken);
            }
            catch (GoogleAuthException ex) when (ex.ErrorCode == "invalid_grant")
            {
                await DisconnectAsync();
                throw new InvalidOperationException(
                    "La sesión de Google expiró o fue revocada. Vuelve a conectar la cuenta.");
            }

            if (!string.IsNullOrWhiteSpace(token.RefreshToken))
            {
                await _settings.SetSettingAsync(
                    KeyRefreshToken, SecretProtector.Protect(token.RefreshToken));
            }

            _token = token;
            return token.AccessToken;
        }
    }
}
