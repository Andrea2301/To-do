using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoItNow.Models
{
    /// <summary>
    /// Archivo adjunto a una entrada de la bóveda. El contenido NUNCA se guarda en claro:
    /// se cifra con AES-GCM (misma clave que la bóveda) y se persiste como BLOB.
    /// </summary>
    public class VaultAttachment
    {
        public int Id { get; set; }

        public int VaultEntryId { get; set; }

        /// <summary>Solo el nombre del archivo, sin rutas.</summary>
        public string FileName { get; set; } = string.Empty;

        public string ContentType { get; set; } = "application/octet-stream";

        /// <summary>Tamaño del archivo original (antes de cifrar).</summary>
        public long SizeBytes { get; set; }

        /// <summary>Contenido cifrado: versión + nonce + tag + ciphertext.</summary>
        public byte[] EncryptedContent { get; set; } = Array.Empty<byte>();

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public VaultEntry? Entry { get; set; }

        /// <summary>Tamaño legible para la interfaz. No se persiste.</summary>
        [NotMapped]
        public string SizeLabel
        {
            get
            {
                if (SizeBytes < 1024)
                    return $"{SizeBytes} B";
                if (SizeBytes < 1024 * 1024)
                    return $"{SizeBytes / 1024.0:0.#} KB";
                return $"{SizeBytes / (1024.0 * 1024.0):0.#} MB";
            }
        }
    }
}
