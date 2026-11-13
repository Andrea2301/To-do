using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoItNow.Models;
using DoItNow.Services;
using DoItNow.Services.Backup;
using DoItNow.Services.Drive;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DoItNow.ViewModels
{
    /// <summary>
    /// Ventana de respaldos del vault. Dos destinos con el mismo cifrado y la misma
    /// retención: una carpeta local sincronizada (OneDrive, Dropbox, Drive de escritorio)
    /// o Google Drive con OAuth.
    /// </summary>
    public partial class BackupViewModel : ObservableObject
    {
        public const string KeyFolderPath = "Backup_FolderPath";
        public const string KeyDestination = "Backup_Destination"; // "Folder" | "Drive"

        private readonly VaultService _vault;
        private readonly SettingsService _settings;
        private readonly DriveBackupCoordinator _drive;

        public ObservableCollection<VaultBackupFile> Backups { get; } = new();

        /// <summary>Se dispara cuando una restauración modificó la bóveda abierta.</summary>
        public event Action? VaultRestored;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsFolderMode), nameof(IsDriveMode), nameof(HasDestination))]
        [NotifyPropertyChangedFor(nameof(CanInteract), nameof(ShowCredentials), nameof(BackupsHeader))]
        [NotifyPropertyChangedFor(nameof(DestinationHint))]
        private bool _useDrive;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowCredentials))]
        private bool _hasCredentials;

        [ObservableProperty]
        private bool _isEditingCredentials;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ConnectionLabel), nameof(CanInteract), nameof(HasDestination))]
        private bool _isConnected;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanInteract), nameof(IsNotBusy))]
        private bool _isBusy;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasBackups))]
        private int _backupCount;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasFolder), nameof(CanInteract), nameof(HasDestination))]
        [NotifyPropertyChangedFor(nameof(FolderLabel), nameof(DestinationHint))]
        private string _folderPath = string.Empty;

        [ObservableProperty]
        private string _clientId = string.Empty;

        [ObservableProperty]
        private string _clientSecret = string.Empty;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        public bool IsFolderMode => !UseDrive;

        public bool IsDriveMode => UseDrive;

        public bool HasFolder => !string.IsNullOrWhiteSpace(FolderPath);

        public bool HasBackups => BackupCount > 0;

        public bool IsNotBusy => !IsBusy;

        public bool HasDestination => UseDrive ? IsConnected : HasFolder;

        /// <summary>Habilita las acciones que necesitan un destino listo y no están en curso.</summary>
        public bool CanInteract => HasDestination && !IsBusy;

        public bool ShowCredentials => UseDrive && (!HasCredentials || IsEditingCredentials);

        public string ConnectionLabel => IsConnected
            ? "Conectado a Google Drive"
            : "Sin conectar";

        public string FolderLabel => HasFolder ? FolderPath : "Sin carpeta configurada";

        public string BackupsHeader => UseDrive ? "RESPALDOS EN DRIVE" : "RESPALDOS EN LA CARPETA";

        public string DestinationHint => UseDrive
            ? "Solo se accede a la carpeta oculta appDataFolder de DoItNow."
            : "Elige una carpeta de OneDrive, Dropbox o Drive de escritorio para que se sincronice sola.";

        public BackupViewModel(VaultService vault, SettingsService? settings = null)
        {
            _vault = vault ?? throw new ArgumentNullException(nameof(vault));
            _settings = settings ?? new SettingsService();
            _drive = new DriveBackupCoordinator(_settings);
        }

        public async Task InitializeAsync()
        {
            FolderPath = await _settings.GetSettingAsync(KeyFolderPath);
            if (string.IsNullOrWhiteSpace(FolderPath))
            {
                FolderPath = SuggestFolder();
                await _settings.SetSettingAsync(KeyFolderPath, FolderPath);
            }

            var destination = await _settings.GetSettingAsync(KeyDestination, "Folder");
            UseDrive = string.Equals(destination, "Drive", StringComparison.OrdinalIgnoreCase);

            HasCredentials = await _drive.HasCredentialsAsync();
            IsConnected = await _drive.IsConnectedAsync();
            IsEditingCredentials = !HasCredentials;

            if (HasCredentials)
            {
                var (clientId, _) = await _drive.GetCredentialsAsync();
                ClientId = clientId;
            }

            await RefreshListAsync();
        }

        // --- Destino ---

        [RelayCommand]
        private async Task ShowFolderAsync()
        {
            if (!UseDrive) return;

            UseDrive = false;
            await _settings.SetSettingAsync(KeyDestination, "Folder");
            await RefreshListAsync();
        }

        [RelayCommand]
        private async Task ShowDriveAsync()
        {
            if (UseDrive) return;

            UseDrive = true;
            await _settings.SetSettingAsync(KeyDestination, "Drive");
            await RefreshListAsync();
        }

        [RelayCommand]
        private async Task ChooseFolderAsync()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Elegir carpeta de respaldo",
                InitialDirectory = Directory.Exists(FolderPath) ? FolderPath : string.Empty
            };

            if (dialog.ShowDialog() != true) return;

            FolderPath = dialog.FolderName;
            await _settings.SetSettingAsync(KeyFolderPath, FolderPath);
            await RefreshListAsync();
        }

        [RelayCommand]
        private void OpenFolder()
        {
            try
            {
                if (!Directory.Exists(FolderPath))
                {
                    StatusMessage = "La carpeta todavía no existe.";
                    return;
                }

                Process.Start(new ProcessStartInfo(FolderPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusMessage = $"No se pudo abrir la carpeta: {ex.Message}";
            }
        }

        // --- Lista y respaldos ---

        [RelayCommand]
        private async Task RefreshAsync()
        {
            if (IsBusy) return;
            await RefreshListAsync();
        }

        private async Task RefreshListAsync()
        {
            var service = await ResolveServiceAsync();
            if (service == null)
            {
                Backups.Clear();
                BackupCount = 0;
                return;
            }

            try
            {
                IsBusy = true;
                var files = await service.ListAsync();

                Backups.Clear();
                foreach (var file in files.OrderByDescending(f => f.ModifiedAt))
                    Backups.Add(file);

                BackupCount = Backups.Count;
                StatusMessage = BackupCount == 0
                    ? (UseDrive
                        ? "Conecta la cuenta y pulsa «Respaldar ahora» para crear el primero."
                        : "Pulsa «Respaldar ahora» para crear el primer respaldo.")
                    : $"{BackupCount} respaldo(s) disponibles.";
            }
            catch (Exception ex)
            {
                if (UseDrive)
                    IsConnected = await _drive.IsConnectedAsync();

                StatusMessage = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task BackupNowAsync()
        {
            if (IsBusy) return;

            if (!_vault.IsUnlocked)
            {
                StatusMessage = "Desbloquea la bóveda antes de respaldarla.";
                return;
            }

            var service = await ResolveServiceAsync();
            if (service == null) return;

            var password = DialogService.AskPassword(
                "Contraseña del respaldo",
                "El respaldo se cifra con esta contraseña antes de guardarlo. La necesitarás para restaurarlo.",
                confirm: true,
                requireMinLength: true);
            if (password == null) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Cifrando y guardando el respaldo…";

                var uploaded = await service.BackupAsync(password, DateTime.Now);

                StatusMessage = $"Respaldo creado: {uploaded.Name} ({uploaded.DisplaySize}).";
                ToastService.Success("Respaldo cifrado creado", "Respaldos");

                await RefreshListAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"No se pudo crear el respaldo: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task RestoreAsync(VaultBackupFile? file)
        {
            if (file == null || IsBusy) return;

            if (!_vault.IsUnlocked)
            {
                StatusMessage = "Desbloquea la bóveda antes de restaurar.";
                return;
            }

            var service = await ResolveServiceAsync();
            if (service == null) return;

            var confirmed = DialogService.Confirm(
                "Restaurar respaldo",
                $"Se importarán las entradas de «{file.Name}» en la bóveda actual.\n\n" +
                "Las entradas con el mismo nombre se actualizan; el resto se conserva.");
            if (!confirmed) return;

            var password = DialogService.AskPassword(
                "Contraseña del respaldo",
                "Introduce la contraseña con la que se cifró este respaldo.");
            if (password == null) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Descargando y descifrando el respaldo…";

                var imported = await service.RestoreAsync(file.Id, password);

                VaultRestored?.Invoke();

                StatusMessage = $"{imported} entrada(s) restauradas desde {file.Name}.";
                ToastService.Success($"{imported} entradas restauradas", "Respaldos");
            }
            catch (Exception ex)
            {
                StatusMessage = $"No se pudo restaurar: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task DeleteAsync(VaultBackupFile? file)
        {
            if (file == null || IsBusy) return;

            var service = await ResolveServiceAsync();
            if (service == null) return;

            var confirmed = DialogService.Confirm(
                "Eliminar respaldo",
                $"¿Eliminar «{file.Name}»? Esta acción no se puede deshacer.");
            if (!confirmed) return;

            try
            {
                IsBusy = true;
                await service.DeleteAsync(file.Id);
                Backups.Remove(file);
                BackupCount = Backups.Count;
                StatusMessage = "Respaldo eliminado.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"No se pudo eliminar: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        // --- Google Drive ---

        [RelayCommand]
        public async Task SaveCredentialsAsync()
        {
            if (IsBusy) return;

            if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
            {
                StatusMessage = "Completa el Client ID y el Client Secret.";
                return;
            }

            try
            {
                IsBusy = true;
                await _drive.SaveCredentialsAsync(ClientId, ClientSecret);

                HasCredentials = true;
                IsConnected = await _drive.IsConnectedAsync();
                IsEditingCredentials = false;
                ClientSecret = string.Empty;
                StatusMessage = "Credenciales guardadas. Ahora conecta tu cuenta.";
                ToastService.Success("Credenciales de Google guardadas", "Google Drive");
            }
            catch (Exception ex)
            {
                StatusMessage = $"No se pudieron guardar las credenciales: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task ConnectAsync()
        {
            if (IsBusy) return;

            if (!HasCredentials)
            {
                IsEditingCredentials = true;
                StatusMessage = "Guarda primero las credenciales de Google.";
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Esperando la autorización en el navegador…";

                await _drive.ConnectAsync(url =>
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));

                IsConnected = true;
                StatusMessage = "Cuenta conectada. Ya puedes crear respaldos.";
                ToastService.Success("Cuenta de Google conectada", "Google Drive");

                await RefreshListAsync();
            }
            catch (Exception ex)
            {
                IsConnected = await _drive.IsConnectedAsync();
                StatusMessage = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task DisconnectAsync()
        {
            if (IsBusy) return;

            try
            {
                IsBusy = true;
                await _drive.DisconnectAsync();
                IsConnected = false;
                Backups.Clear();
                BackupCount = 0;
                StatusMessage = "Cuenta desconectada. Los respaldos siguen en tu Drive.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"No se pudo desconectar: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private void EditCredentials()
        {
            IsEditingCredentials = true;
            StatusMessage = "Cambiar el Client ID desconecta la cuenta actual.";
        }

        [RelayCommand]
        private void OpenConsole()
        {
            try
            {
                Process.Start(new ProcessStartInfo(
                    "https://console.cloud.google.com/apis/credentials") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusMessage = $"No se pudo abrir el navegador: {ex.Message}";
            }
        }

        // --- Utilidades ---

        private async Task<BackupService?> ResolveServiceAsync()
        {
            if (UseDrive)
            {
                if (!IsConnected)
                {
                    StatusMessage = "Conecta tu cuenta de Google primero.";
                    return null;
                }

                return new BackupService(await _drive.CreateStoreAsync(), _vault);
            }

            if (!HasFolder)
            {
                StatusMessage = "Elige una carpeta de respaldo.";
                return null;
            }

            return new BackupService(new FolderBackupStore(FolderPath), _vault);
        }

        private static string SuggestFolder()
        {
            var oneDrive = Environment.GetEnvironmentVariable("OneDrive")
                ?? Environment.GetEnvironmentVariable("OneDriveConsumer");

            var root = !string.IsNullOrWhiteSpace(oneDrive) && Directory.Exists(oneDrive)
                ? oneDrive
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            return Path.Combine(root, "DoItNow", "Backups");
        }
    }
}
