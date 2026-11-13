using System;
using System.Security.Cryptography;
using System.Text;

namespace DoItNow.Services.Drive
{
    /// <summary>
    /// Cifra secretos de configuración (client secret y refresh token) con DPAPI bajo el
    /// usuario actual de Windows. Nunca se guardan en claro ni se commitean al repo.
    /// </summary>
    public static class SecretProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DoItNow.Drive.v1");

        public static string Protect(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var blob = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);

            return Convert.ToBase64String(blob);
        }

        public static string Unprotect(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            try
            {
                var blob = Convert.FromBase64String(value);
                var plain = ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                // Otro usuario de Windows o blob corrupto: se trata como "sin configurar".
                return string.Empty;
            }
        }
    }
}
