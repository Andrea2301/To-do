using System;
using System.Text.Json.Serialization;

namespace DoItNow.Models
{
    /// <summary>
    /// Perfil local de la aplicación. Cada perfil tiene su propia base SQLite,
    /// ajustes, historial de chat y bóveda. La contraseña (opcional) se guarda
    /// solo como verificador PBKDF2; nunca en claro.
    /// </summary>
    public class AppProfile
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Sal PBKDF2 en Base64. Vacío si el perfil no tiene contraseña.</summary>
        public string? PasswordSalt { get; set; }

        /// <summary>Verificador PBKDF2 en Base64. Nunca se guarda la contraseña.</summary>
        public string? PasswordVerifier { get; set; }

        public int PasswordIterations { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? LastLoginAt { get; set; }

        [JsonIgnore]
        public bool HasPassword =>
            !string.IsNullOrWhiteSpace(PasswordSalt) && !string.IsNullOrWhiteSpace(PasswordVerifier);

        [JsonIgnore]
        public string Initial =>
            string.IsNullOrWhiteSpace(DisplayName)
                ? "?"
                : DisplayName.Trim()[..1].ToUpperInvariant();

        [JsonIgnore]
        public string AccessLabel => HasPassword ? "Protegido con contraseña" : "Sin contraseña";

        [JsonIgnore]
        public string LastLoginLabel =>
            LastLoginAt is { } last
                ? $"Última sesión: {last:dd MMM yyyy, HH:mm}"
                : "Aún no has entrado con este perfil";
    }
}
