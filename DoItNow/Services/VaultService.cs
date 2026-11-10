using DoItNow.Data;
using DoItNow.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DoItNow.Services
{
    /// <summary>
    /// Bóveda local-first de claves sensibles.
    ///
    /// Modos de protección:
    ///  • Contraseña maestra: clave derivada con PBKDF2-SHA256 y verificador en SQLite.
    ///  • Sin contraseña: clave aleatoria envuelta con DPAPI (ligada al usuario de Windows).
    ///
    /// En ambos modos cada entrada se cifra con AES-GCM. Al cambiar de contraseña se
    /// re-cifran todas las entradas para que la clave anterior deje de servir.
    /// </summary>
    public class VaultService
    {
        public const string KeyMasterSalt = "Vault_MasterSalt";
        public const string KeyMasterVerifier = "Vault_MasterVerifier";
        public const string KeyMasterIterations = "Vault_MasterIterations";
        public const string KeyDpapiWrapped = "Vault_DpapiWrappedKey";

        private const string ExportFormat = "doitnow-vault";
        private const int ExportVersion = 1;

        private static readonly byte[] DpapiEntropy =
            Encoding.UTF8.GetBytes("DoItNow.Vault.DPAPI.v1");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly SettingsService _settings;

        private byte[]? _key;
        private bool _initialized;

        public VaultService(SettingsService? settings = null)
        {
            _settings = settings ?? new SettingsService();
        }

        public bool IsUnlocked => _key != null;
        public bool HasMasterPassword { get; private set; }

        /// <summary>True cuando la bóveda se protege automáticamente con DPAPI (sin contraseña).</summary>
        public bool UsesWindowsProtection => !HasMasterPassword;

        public async Task InitializeAsync()
        {
            if (_initialized) return;
            _initialized = true;

            var salt = await _settings.GetSettingAsync(KeyMasterSalt, string.Empty);
            HasMasterPassword = !string.IsNullOrWhiteSpace(salt);

            if (HasMasterPassword)
            {
                // Bloqueada hasta que el usuario escriba su contraseña maestra.
                _key = null;
                return;
            }

            // Sin contraseña maestra: se usa una clave aleatoria protegida por DPAPI.
            try
            {
                var wrapped = await _settings.GetSettingAsync(KeyDpapiWrapped, string.Empty);
                if (string.IsNullOrWhiteSpace(wrapped))
                {
                    var fresh = CryptoService.RandomBytes(CryptoService.KeySize);
                    await StoreDpapiKeyAsync(fresh);
                    _key = fresh;
                }
                else
                {
                    _key = ProtectedData.Unprotect(
                        Convert.FromBase64String(wrapped),
                        DpapiEntropy,
                        DataProtectionScope.CurrentUser);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultService] DPAPI no disponible: {ex.Message}");
                _key = null;
            }
        }

        public async Task<bool> UnlockAsync(string password)
        {
            await InitializeAsync();

            if (!HasMasterPassword)
                return IsUnlocked;

            try
            {
                var salt = Convert.FromBase64String(await RequiredSettingAsync(KeyMasterSalt));
                var iterations = ParseIterations(await RequiredSettingAsync(KeyMasterIterations));
                var expected = Convert.FromBase64String(await RequiredSettingAsync(KeyMasterVerifier));

                var (verifier, key) = CryptoService.DeriveKeyMaterial(password, salt, iterations);
                if (!CryptoService.Verify(expected, verifier))
                    return false;

                _key = key;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultService] Unlock error: {ex.Message}");
                return false;
            }
        }

        /// <summary>Configura la contraseña maestra por primera vez (modo DPAPI → PBKDF2).</summary>
        public async Task SetupMasterPasswordAsync(string password)
        {
            await InitializeAsync();

            if (HasMasterPassword)
                throw new InvalidOperationException("La bóveda ya tiene una contraseña maestra.");
            if (_key == null)
                throw new InvalidOperationException("La bóveda no está desbloqueada.");

            var salt = CryptoService.RandomBytes(CryptoService.SaltSize);
            var (verifier, newKey) = CryptoService.DeriveKeyMaterial(password, salt);

            await ReencryptAllAsync(_key, newKey);

            await _settings.SetSettingAsync(KeyMasterSalt, Convert.ToBase64String(salt), "Sal de la contraseña maestra de la bóveda");
            await _settings.SetSettingAsync(KeyMasterIterations, CryptoService.DefaultIterations.ToString(), "Iteraciones PBKDF2 de la bóveda");
            await _settings.SetSettingAsync(KeyMasterVerifier, Convert.ToBase64String(verifier), "Verificador de la contraseña maestra");
            await _settings.SetSettingAsync(KeyDpapiWrapped, string.Empty, "Clave DPAPI de la bóveda");

            _key = newKey;
            HasMasterPassword = true;
        }

        public async Task<bool> ChangeMasterPasswordAsync(string currentPassword, string newPassword)
        {
            await InitializeAsync();
            if (!HasMasterPassword)
                throw new InvalidOperationException("La bóveda no tiene contraseña maestra.");

            var salt = Convert.FromBase64String(await RequiredSettingAsync(KeyMasterSalt));
            var iterations = ParseIterations(await RequiredSettingAsync(KeyMasterIterations));
            var expected = Convert.FromBase64String(await RequiredSettingAsync(KeyMasterVerifier));

            var (verifier, oldKey) = CryptoService.DeriveKeyMaterial(currentPassword, salt, iterations);
            if (!CryptoService.Verify(expected, verifier))
                return false;

            var newSalt = CryptoService.RandomBytes(CryptoService.SaltSize);
            var (newVerifier, newKey) = CryptoService.DeriveKeyMaterial(newPassword, newSalt);

            await ReencryptAllAsync(oldKey, newKey);

            await _settings.SetSettingAsync(KeyMasterSalt, Convert.ToBase64String(newSalt));
            await _settings.SetSettingAsync(KeyMasterIterations, CryptoService.DefaultIterations.ToString());
            await _settings.SetSettingAsync(KeyMasterVerifier, Convert.ToBase64String(newVerifier));

            _key = newKey;
            return true;
        }

        /// <summary>Quita la contraseña maestra y vuelve a proteger la bóveda con DPAPI.</summary>
        public async Task RemoveMasterPasswordAsync()
        {
            await InitializeAsync();
            if (!HasMasterPassword || _key == null)
                throw new InvalidOperationException("La bóveda no está desbloqueada con contraseña maestra.");

            var oldKey = _key;
            var newKey = CryptoService.RandomBytes(CryptoService.KeySize);

            await ReencryptAllAsync(oldKey, newKey);

            await StoreDpapiKeyAsync(newKey);
            await _settings.SetSettingAsync(KeyMasterSalt, string.Empty);
            await _settings.SetSettingAsync(KeyMasterVerifier, string.Empty);
            await _settings.SetSettingAsync(KeyMasterIterations, string.Empty);

            _key = newKey;
            HasMasterPassword = false;
        }

        public void Lock()
        {
            if (HasMasterPassword)
                _key = null;
        }

        public async Task<List<VaultEntry>> GetEntriesAsync()
        {
            EnsureUnlocked();

            using var db = new AppDbContext();
            var entries = await db.VaultEntries
                .AsNoTracking()
                .OrderBy(e => e.Title)
                .ToListAsync();

            foreach (var entry in entries)
            {
                entry.Secret = SafeDecrypt(entry.EncryptedSecret, _key!);
                entry.Notes = SafeDecrypt(entry.EncryptedNotes, _key!);
            }

            return entries;
        }

        public async Task SaveEntryAsync(VaultEntry entry)
        {
            EnsureUnlocked();
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (string.IsNullOrWhiteSpace(entry.Title))
                throw new InvalidOperationException("El título es obligatorio.");

            using var db = new AppDbContext();

            VaultEntry target;
            if (entry.Id <= 0)
            {
                target = new VaultEntry { CreatedAt = DateTime.Now };
                db.VaultEntries.Add(target);
            }
            else
            {
                target = await db.VaultEntries.FindAsync(entry.Id)
                    ?? throw new InvalidOperationException("La entrada ya no existe.");
            }

            target.Title = entry.Title.Trim();
            target.EntryType = string.IsNullOrWhiteSpace(entry.EntryType) ? "API Key" : entry.EntryType.Trim();
            target.Username = entry.Username?.Trim() ?? string.Empty;
            target.Url = entry.Url?.Trim() ?? string.Empty;
            target.EncryptedSecret = CryptoService.Encrypt(entry.Secret ?? string.Empty, _key!);
            target.EncryptedNotes = CryptoService.Encrypt(entry.Notes ?? string.Empty, _key!);
            target.UpdatedAt = DateTime.Now;

            await db.SaveChangesAsync();
            entry.Id = target.Id;
            entry.CreatedAt = target.CreatedAt;
            entry.UpdatedAt = target.UpdatedAt;
        }

        public async Task DeleteEntryAsync(int entryId)
        {
            EnsureUnlocked();

            using var db = new AppDbContext();
            var entry = await db.VaultEntries.FindAsync(entryId);
            if (entry == null) return;

            db.VaultEntries.Remove(entry);
            await db.SaveChangesAsync();
        }

        /// <summary>
        /// Exporta todas las entradas a un archivo cifrado con una contraseña independiente.
        /// El archivo se puede mover entre equipos y volver a importar aquí o en otra instalación.
        /// </summary>
        public async Task ExportAsync(string path, string password)
        {
            EnsureUnlocked();

            var entries = await GetEntriesAsync();
            var payload = new ExportPayload
            {
                App = "DoItNow",
                ExportedAt = DateTime.Now,
                Entries = entries.Select(e => new ExportEntry
                {
                    Title = e.Title,
                    EntryType = e.EntryType,
                    Username = e.Username,
                    Url = e.Url,
                    Secret = e.Secret,
                    Notes = e.Notes,
                    CreatedAt = e.CreatedAt
                }).ToList()
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var salt = CryptoService.RandomBytes(CryptoService.SaltSize);
            var (_, exportKey) = CryptoService.DeriveKeyMaterial(password, salt);
            var encrypted = CryptoService.EncryptBytes(Encoding.UTF8.GetBytes(json), exportKey);

            var envelope = new ExportEnvelope
            {
                Format = ExportFormat,
                Version = ExportVersion,
                Kdf = "PBKDF2-SHA256",
                Iterations = CryptoService.DefaultIterations,
                Salt = Convert.ToBase64String(salt),
                Data = encrypted
            };

            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(envelope, JsonOptions), Encoding.UTF8);
        }

        /// <summary>Importa un archivo cifrado. Devuelve cuántas entradas se añadieron o actualizaron.</summary>
        public async Task<int> ImportAsync(string path, string password)
        {
            EnsureUnlocked();

            var envelope = JsonSerializer.Deserialize<ExportEnvelope>(
                await File.ReadAllTextAsync(path, Encoding.UTF8), JsonOptions);

            if (envelope == null ||
                !string.Equals(envelope.Format, ExportFormat, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("El archivo no es una exportación válida de DoItNow.");

            var salt = Convert.FromBase64String(envelope.Salt);
            var iterations = envelope.Iterations > 0 ? envelope.Iterations : CryptoService.DefaultIterations;
            var (_, exportKey) = CryptoService.DeriveKeyMaterial(password, salt, iterations);

            byte[] plaintext;
            try
            {
                plaintext = CryptoService.DecryptBytes(envelope.Data, exportKey);
            }
            catch (CryptographicException)
            {
                throw new InvalidOperationException("Contraseña incorrecta o archivo dañado.");
            }

            var payload = JsonSerializer.Deserialize<ExportPayload>(
                Encoding.UTF8.GetString(plaintext), JsonOptions);

            if (payload?.Entries == null)
                throw new InvalidOperationException("La exportación no contiene entradas.");

            using var db = new AppDbContext();
            var existing = await db.VaultEntries.ToListAsync();
            var imported = 0;

            foreach (var item in payload.Entries)
            {
                if (string.IsNullOrWhiteSpace(item.Title)) continue;

                var target = existing.FirstOrDefault(e =>
                    string.Equals(e.Title, item.Title, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Username, item.Username ?? string.Empty, StringComparison.OrdinalIgnoreCase));

                if (target == null)
                {
                    target = new VaultEntry { CreatedAt = item.CreatedAt == default ? DateTime.Now : item.CreatedAt };
                    db.VaultEntries.Add(target);
                }

                target.Title = item.Title.Trim();
                target.EntryType = string.IsNullOrWhiteSpace(item.EntryType) ? "API Key" : item.EntryType;
                target.Username = item.Username ?? string.Empty;
                target.Url = item.Url ?? string.Empty;
                target.EncryptedSecret = CryptoService.Encrypt(item.Secret ?? string.Empty, _key!);
                target.EncryptedNotes = CryptoService.Encrypt(item.Notes ?? string.Empty, _key!);
                target.UpdatedAt = DateTime.Now;
                imported++;
            }

            await db.SaveChangesAsync();
            return imported;
        }

        // --- Helpers ---

        private void EnsureUnlocked()
        {
            if (_key == null)
                throw new InvalidOperationException("La bóveda está bloqueada. Introduce la contraseña maestra.");
        }

        private async Task ReencryptAllAsync(byte[] oldKey, byte[] newKey)
        {
            using var db = new AppDbContext();
            var entries = await db.VaultEntries.ToListAsync();

            foreach (var entry in entries)
            {
                var secret = SafeDecrypt(entry.EncryptedSecret, oldKey);
                var notes = SafeDecrypt(entry.EncryptedNotes, oldKey);
                entry.EncryptedSecret = CryptoService.Encrypt(secret, newKey);
                entry.EncryptedNotes = CryptoService.Encrypt(notes, newKey);
            }

            await db.SaveChangesAsync();
        }

        private async Task StoreDpapiKeyAsync(byte[] key)
        {
            var wrapped = ProtectedData.Protect(key, DpapiEntropy, DataProtectionScope.CurrentUser);
            await _settings.SetSettingAsync(
                KeyDpapiWrapped,
                Convert.ToBase64String(wrapped),
                "Clave de bóveda protegida por DPAPI (usuario de Windows)");
        }

        private async Task<string> RequiredSettingAsync(string key)
        {
            var value = await _settings.GetSettingAsync(key, string.Empty);
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Falta la configuración de la bóveda: {key}");
            return value;
        }

        private static int ParseIterations(string value)
            => int.TryParse(value, out var parsed) && parsed >= 10_000
                ? parsed
                : CryptoService.DefaultIterations;

        private static string SafeDecrypt(string payload, byte[] key)
        {
            try
            {
                return CryptoService.Decrypt(payload, key);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultService] No se pudo descifrar una entrada: {ex.Message}");
                return string.Empty;
            }
        }

        // --- Contratos del archivo de exportación ---

        private class ExportEnvelope
        {
            public string Format { get; set; } = ExportFormat;
            public int Version { get; set; } = ExportVersion;
            public string Kdf { get; set; } = "PBKDF2-SHA256";
            public int Iterations { get; set; } = CryptoService.DefaultIterations;
            public string Salt { get; set; } = string.Empty;
            public string Data { get; set; } = string.Empty;
        }

        private class ExportPayload
        {
            public string App { get; set; } = "DoItNow";
            public DateTime ExportedAt { get; set; }
            public List<ExportEntry> Entries { get; set; } = new();
        }

        private class ExportEntry
        {
            public string Title { get; set; } = string.Empty;
            public string EntryType { get; set; } = "API Key";
            public string Username { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public string Secret { get; set; } = string.Empty;
            public string Notes { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
        }
    }
}
