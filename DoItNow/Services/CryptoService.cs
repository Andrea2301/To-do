using System;
using System.Security.Cryptography;
using System.Text;

namespace DoItNow.Services
{
    /// <summary>
    /// Criptografía simétrica para la bóveda: PBKDF2-SHA256 para derivar claves y
    /// AES-GCM (autenticado) para cifrar cada secreto. Formato del blob:
    /// [1 byte versión][12 bytes nonce][16 bytes tag][ciphertext] en Base64.
    /// </summary>
    public static class CryptoService
    {
        public const int DefaultIterations = 210_000;
        public const int SaltSize = 16;
        public const int KeySize = 32;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const byte BlobVersion = 1;

        private static readonly byte[] AssociatedData =
            Encoding.UTF8.GetBytes("DoItNow.Vault.v1");

        public static byte[] RandomBytes(int length) => RandomNumberGenerator.GetBytes(length);

        /// <summary>
        /// Deriva 64 bytes del password: los primeros 32 verifican la contraseña y
        /// los últimos 32 son la clave de cifrado. Así nunca se guarda la clave.
        /// </summary>
        public static (byte[] Verifier, byte[] Key) DeriveKeyMaterial(
            string password, byte[] salt, int iterations = DefaultIterations)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("La contraseña no puede estar vacía.", nameof(password));

            var material = Rfc2898DeriveBytes.Pbkdf2(
                password, salt, iterations, HashAlgorithmName.SHA256, KeySize * 2);

            var verifier = material[..KeySize];
            var key = material[KeySize..];
            return (verifier, key);
        }

        public static bool Verify(byte[] expectedVerifier, byte[] candidateVerifier)
            => expectedVerifier.Length == candidateVerifier.Length &&
               CryptographicOperations.FixedTimeEquals(expectedVerifier, candidateVerifier);

        public static string Encrypt(string plaintext, byte[] key)
            => EncryptBytes(Encoding.UTF8.GetBytes(plaintext ?? string.Empty), key);

        public static string Decrypt(string payload, byte[] key)
            => Encoding.UTF8.GetString(DecryptBytes(payload, key));

        public static string EncryptBytes(byte[] plaintext, byte[] key)
            => Convert.ToBase64String(EncryptToBlob(plaintext, key));

        /// <summary>Cifra bytes y devuelve el blob binario (sin Base64), útil para BLOBs.</summary>
        public static byte[] EncryptToBlob(byte[] plaintext, byte[] key)
        {
            var nonce = RandomBytes(NonceSize);
            var cipher = new byte[plaintext.Length];
            var tag = new byte[TagSize];

            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Encrypt(nonce, plaintext, cipher, tag, AssociatedData);
            }

            var blob = new byte[1 + NonceSize + TagSize + cipher.Length];
            blob[0] = BlobVersion;
            nonce.CopyTo(blob, 1);
            tag.CopyTo(blob, 1 + NonceSize);
            cipher.CopyTo(blob, 1 + NonceSize + TagSize);

            return blob;
        }

        public static byte[] DecryptBytes(string payload, byte[] key)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return Array.Empty<byte>();

            return DecryptFromBlob(Convert.FromBase64String(payload), key);
        }

        /// <summary>Descifra un blob binario generado por <see cref="EncryptToBlob"/>.</summary>
        public static byte[] DecryptFromBlob(byte[] blob, byte[] key)
        {
            if (blob == null || blob.Length < 1 + NonceSize + TagSize || blob[0] != BlobVersion)
                throw new CryptographicException("Formato de cifrado desconocido.");

            var nonce = blob.AsSpan(1, NonceSize);
            var tag = blob.AsSpan(1 + NonceSize, TagSize);
            var cipher = blob.AsSpan(1 + NonceSize + TagSize);
            var plaintext = new byte[cipher.Length];

            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Decrypt(nonce, cipher, tag, plaintext, AssociatedData);
            }

            return plaintext;
        }
    }
}
