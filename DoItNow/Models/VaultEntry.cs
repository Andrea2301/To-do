using System;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoItNow.Models
{
    /// <summary>
    /// Entrada de la bóveda de claves. El secreto y las notas nunca se guardan en claro:
    /// se cifran con AES-GCM antes de persistirse (ver <c>VaultService</c>).
    /// </summary>
    public class VaultEntry
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        /// <summary>API Key, Token, Contraseña, Conexión, Otro...</summary>
        public string EntryType { get; set; } = "API Key";

        public string Username { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;

        /// <summary>Secreto cifrado (Base64: versión + nonce + tag + ciphertext).</summary>
        public string EncryptedSecret { get; set; } = string.Empty;

        /// <summary>Notas cifradas (Base64).</summary>
        public string EncryptedNotes { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        /// <summary>Valor descifrado en memoria. No se persiste.</summary>
        [NotMapped]
        public string Secret { get; set; } = string.Empty;

        /// <summary>Notas descifradas en memoria. No se persisten.</summary>
        [NotMapped]
        public string Notes { get; set; } = string.Empty;

        /// <summary>Metadatos de los archivos adjuntos (sin contenido). No se persiste aquí.</summary>
        [NotMapped]
        public ObservableCollection<VaultAttachment> Attachments { get; } = new();

        public string TypeIcon => EntryType switch
        {
            "API Key" => "🔑",
            "Token" => "🎫",
            "Contraseña" => "🔒",
            "Conexión" => "🌐",
            "Variable" => "⚙",
            _ => "🏷"
        };
    }
}
