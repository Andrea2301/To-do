using DoItNow.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace DoItNow.Services
{
    /// <summary>
    /// Registro de perfiles locales. Vive en <c>%LOCALAPPDATA%\DoItNow\profiles.json</c>
    /// (solo metadatos y verificadores PBKDF2) y crea una base SQLite independiente
    /// por perfil en <c>%LOCALAPPDATA%\DoItNow\profiles\&lt;id&gt;\doitnow.db</c>.
    /// </summary>
    public class ProfileService
    {
        public const int MinPasswordLength = 8;
        public const int MaxDisplayNameLength = 32;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        /// <summary>Raíz de datos. DOITNOW_PROFILE_ROOT permite aislar los tests.</summary>
        public static string RootDirectory
        {
            get
            {
                var overridePath = Environment.GetEnvironmentVariable("DOITNOW_PROFILE_ROOT");
                if (!string.IsNullOrWhiteSpace(overridePath))
                    return Path.GetFullPath(overridePath);

                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DoItNow");
            }
        }

        private static string RegistryPath => Path.Combine(RootDirectory, "profiles.json");
        private static string ProfilesDirectory => Path.Combine(RootDirectory, "profiles");

        /// <summary>Base de datos anterior a los perfiles; se migra al crear el primero.</summary>
        public static string LegacyDatabasePath => Path.Combine(RootDirectory, "doitnow.db");

        public IReadOnlyList<AppProfile> GetProfiles() =>
            LoadStore().Profiles
                .OrderByDescending(p => p.LastLoginAt ?? DateTime.MinValue)
                .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        public AppProfile? GetLastProfile()
        {
            var store = LoadStore();
            return store.LastProfileId is { } id
                ? store.Profiles.FirstOrDefault(p => p.Id == id)
                : store.Profiles
                    .OrderByDescending(p => p.LastLoginAt ?? DateTime.MinValue)
                    .FirstOrDefault();
        }

        public AppProfile? FindByName(string displayName) =>
            LoadStore().Profiles.FirstOrDefault(p =>
                string.Equals(p.DisplayName, displayName?.Trim(), StringComparison.CurrentCultureIgnoreCase));

        public string GetDatabasePath(AppProfile profile) =>
            Path.Combine(ProfilesDirectory, profile.Id.ToString("N"), "doitnow.db");

        public AppProfile CreateProfile(string displayName, string? password = null)
        {
            var name = (displayName ?? string.Empty).Trim();

            if (name.Length < 2)
                throw new ArgumentException("El nombre del perfil debe tener al menos 2 caracteres.");
            if (name.Length > MaxDisplayNameLength)
                throw new ArgumentException($"El nombre no puede superar {MaxDisplayNameLength} caracteres.");
            if (!string.IsNullOrEmpty(password) && password.Length < MinPasswordLength)
                throw new ArgumentException($"La contraseña debe tener al menos {MinPasswordLength} caracteres.");

            var store = LoadStore();
            if (store.Profiles.Any(p =>
                    string.Equals(p.DisplayName, name, StringComparison.CurrentCultureIgnoreCase)))
                throw new InvalidOperationException("Ya existe un perfil con ese nombre.");

            var profile = new AppProfile
            {
                DisplayName = name,
                CreatedAt = DateTime.Now
            };

            if (!string.IsNullOrEmpty(password))
                SetPassword(profile, password);

            store.Profiles.Add(profile);
            SaveStore(store);

            Directory.CreateDirectory(Path.GetDirectoryName(GetDatabasePath(profile))!);
            TryMigrateLegacyDatabase(profile);

            return profile;
        }

        public bool VerifyPassword(AppProfile profile, string? password)
        {
            if (profile == null) return false;
            if (!profile.HasPassword) return true;
            if (string.IsNullOrEmpty(password)) return false;

            try
            {
                var salt = Convert.FromBase64String(profile.PasswordSalt!);
                var expected = Convert.FromBase64String(profile.PasswordVerifier!);
                var iterations = profile.PasswordIterations > 0
                    ? profile.PasswordIterations
                    : CryptoService.DefaultIterations;

                var (candidate, _) = CryptoService.DeriveKeyMaterial(password, salt, iterations);
                return CryptoService.Verify(expected, candidate);
            }
            catch (FormatException ex)
            {
                Debug.WriteLine($"[ProfileService] Verificador corrupto: {ex.Message}");
                return false;
            }
        }

        public void ChangePassword(AppProfile profile, string newPassword)
        {
            if (newPassword == null || newPassword.Length < MinPasswordLength)
                throw new ArgumentException($"La contraseña debe tener al menos {MinPasswordLength} caracteres.");

            var store = LoadStore();
            var target = store.Profiles.FirstOrDefault(p => p.Id == profile.Id)
                ?? throw new InvalidOperationException("El perfil ya no existe.");

            SetPassword(target, newPassword);
            SaveStore(store);

            // Mantén sincronizada la instancia que tiene la UI: si no, el objeto
            // en memoria seguiría validando la contraseña anterior.
            profile.PasswordSalt = target.PasswordSalt;
            profile.PasswordVerifier = target.PasswordVerifier;
            profile.PasswordIterations = target.PasswordIterations;
        }

        /// <summary>
        /// Quita la contraseña del perfil: vuelve a abrirse directamente desde la
        /// lista. No toca los datos del perfil.
        /// </summary>
        public void RemovePassword(AppProfile profile)
        {
            var store = LoadStore();
            var target = store.Profiles.FirstOrDefault(p => p.Id == profile.Id)
                ?? throw new InvalidOperationException("El perfil ya no existe.");

            target.PasswordSalt = null;
            target.PasswordVerifier = null;
            target.PasswordIterations = 0;
            SaveStore(store);

            profile.PasswordSalt = null;
            profile.PasswordVerifier = null;
            profile.PasswordIterations = 0;
        }

        public void MarkLogin(AppProfile profile)
        {
            var store = LoadStore();
            var target = store.Profiles.FirstOrDefault(p => p.Id == profile.Id);
            if (target == null) return;

            target.LastLoginAt = DateTime.Now;
            store.LastProfileId = target.Id;
            SaveStore(store);

            profile.LastLoginAt = target.LastLoginAt;
        }

        public void DeleteProfile(AppProfile profile, bool deleteData)
        {
            var store = LoadStore();
            var target = store.Profiles.FirstOrDefault(p => p.Id == profile.Id);
            if (target == null) return;

            if (deleteData)
            {
                var folder = Path.GetDirectoryName(GetDatabasePath(target))!;
                if (Directory.Exists(folder))
                {
                    // Los contextos ya están cerrados, pero EF/SQLite puede conservar
                    // el archivo en el pool de conexiones.
                    SqliteConnection.ClearAllPools();
                    Directory.Delete(folder, recursive: true);
                }
            }

            store.Profiles.Remove(target);
            if (store.LastProfileId == target.Id)
                store.LastProfileId = null;

            SaveStore(store);
        }

        private static void SetPassword(AppProfile profile, string password)
        {
            var salt = CryptoService.RandomBytes(CryptoService.SaltSize);
            var (verifier, _) = CryptoService.DeriveKeyMaterial(password, salt);

            profile.PasswordSalt = Convert.ToBase64String(salt);
            profile.PasswordVerifier = Convert.ToBase64String(verifier);
            profile.PasswordIterations = CryptoService.DefaultIterations;
        }

        private static void TryMigrateLegacyDatabase(AppProfile profile)
        {
            try
            {
                var target = new ProfileService().GetDatabasePath(profile);
                if (File.Exists(target) || !File.Exists(LegacyDatabasePath))
                    return;

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                File.Move(LegacyDatabasePath, target);
                foreach (var suffix in new[] { "-wal", "-shm" })
                {
                    if (File.Exists(LegacyDatabasePath + suffix))
                        File.Move(LegacyDatabasePath + suffix, target + suffix);
                }

                Debug.WriteLine($"[ProfileService] Base anterior migrada al perfil {profile.DisplayName}.");
            }
            catch (Exception ex)
            {
                // Si falla la migración el perfil sigue siendo válido con una base nueva.
                Debug.WriteLine($"[ProfileService] No se pudo migrar la base anterior: {ex.Message}");
            }
        }

        private static ProfileStore LoadStore()
        {
            try
            {
                if (!File.Exists(RegistryPath))
                    return new ProfileStore();

                var json = File.ReadAllText(RegistryPath, Encoding.UTF8);
                return JsonSerializer.Deserialize<ProfileStore>(json, JsonOptions) ?? new ProfileStore();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProfileService] Registro ilegible: {ex.Message}");
                return RecoverStore();
            }
        }

        private static void SaveStore(ProfileStore store)
        {
            Directory.CreateDirectory(RootDirectory);

            var json = JsonSerializer.Serialize(store, JsonOptions);
            var tempPath = RegistryPath + ".tmp";

            File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, RegistryPath, overwrite: true);
        }

        /// <summary>
        /// Si profiles.json se corrompe, reconstruye la lista a partir de las
        /// carpetas de datos para no perder el acceso a la base de cada perfil.
        /// </summary>
        private static ProfileStore RecoverStore()
        {
            var store = new ProfileStore();
            if (!Directory.Exists(ProfilesDirectory))
                return store;

            foreach (var folder in Directory.GetDirectories(ProfilesDirectory))
            {
                if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out var id))
                    continue;

                var name = $"Perfil {id.ToString("N")[..4]}";
                var created = Directory.GetCreationTime(folder);

                store.Profiles.Add(new AppProfile
                {
                    Id = id,
                    DisplayName = name,
                    CreatedAt = created == default ? DateTime.Now : created
                });
            }

            return store;
        }

        private sealed class ProfileStore
        {
            public List<AppProfile> Profiles { get; set; } = new();
            public Guid? LastProfileId { get; set; }
        }
    }
}
