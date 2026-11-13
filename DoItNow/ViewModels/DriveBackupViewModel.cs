using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoItNow.Models;
using DoItNow.Services;
using DoItNow.Services.Drive;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace DoItNow.ViewModels
{
    /// <summary>
    /// Ventana de respaldos en Google Drive: credenciales OAuth, conexión de la cuenta,
    /// subida del vault cifrado y restauración de versiones anteriores.
    /// </summary>
    public partial class DriveBackupViewModel : ObservableObject
    {
        private readonly DriveBackupCoordinator _coordinator;
        private readonly VaultService _vault;

        public ObservableCollection<DriveBackupFile> Backups { get; } = new();

        /// <summary>Se dispara cuando una restauración modificó la bóveda abierta.</summary>
        public event Action? VaultRestored;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowCredentials))]
        private bool _hasCredentials;

        [ObservableProperty]
        private bool _isEditingCredentials;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ConnectionLabel))]
        [NotifyPropertyChangedFor(nameof(CanInteract))]
        private bool _isConnected;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanInteract))]
        [NotifyPropertyChangedFor(nameof(IsNotBusy))]
        private bool _isBusy;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasBackups))]
        private int _backupCount;

        [ObservableProperty]
        private string _clientId = string.Empty;

        [ObservableProperty]
        private string _clientSecret = string.Empty;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        public bool ShowCredentials => !HasCredentials || IsEditingCredentials;

        public bool HasBackups => BackupCount > 0;

        public bool IsNotBusy => !IsBusy;

        /// <summary>Habilita las acciones que necesitan conexión y no están en curso.</summary>
        public bool CanInteract => IsConnected && !IsBusy;

        public string ConnectionLabel => IsConnected
            ? "Conectado a Google Drive"
            : "Sin conectar";

        public DriveBackupViewModel(DriveBackupCoordinator coordinator, VaultService vault)
        {
            _coordinator = coordinator;
            _vault = vault;
        }

        public async Task InitializeAsync()
        {
            HasCredentials = await _coordinator.HasCredentialsAsync();
            IsConnected = await _coordinator.IsConnectedAsync();
            IsEditingCredentials = !HasCredentials;

            if (HasCredentials)
            {
                var (clientId, _) = await _coordinator.GetCredentialsAsync();
                ClientId = clientId;
            }

            if (IsConnected)
                await RefreshAsync();
        }

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
                await _coordinator.SaveCredentialsAsync(ClientId, ClientSecret);

                HasCredentials = true;
                IsConnected = await _coordinator.IsConnectedAsync();
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
        public async Task ConnectAsync()
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

                await _coordinator.ConnectAsync(url =>
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));

                IsConnected = true;
                StatusMessage = "Cuenta conectada. Ya puedes crear respaldos.";
                ToastService.Success("Cuenta de Google conectada", "Google Drive");

                await RefreshAsync();
            }
            catch (Exception ex)
            {
                IsConnected = await _coordinator.IsConnectedAsync();
                StatusMessage = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task DisconnectAsync()
        {
            if (IsBusy) return;

            try
            {
                IsBusy = true;
                await _coordinator.DisconnectAsync();
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
        public async Task RefreshAsync()
        {
            if (!IsConnected)
            {
                StatusMessage = "Conecta tu cuenta para ver los respaldos.";
                return;
            }

            try
            {
                IsBusy = true;
                var files = await _coordinator.ListAsync();

                Backups.Clear();
                foreach (var file in files.OrderByDescending(f => f.ModifiedAt))
                    Backups.Add(file);

                BackupCount = Backups.Count;
                StatusMessage = BackupCount == 0
                    ? "Todavía no hay respaldos en Drive."
                    : $"{BackupCount} respaldo(s) en Drive.";
            }
            catch (Exception ex)
            {
                IsConnected = await _coordinator.IsConnectedAsync();
                StatusMessage = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task BackupNowAsync()
        {
            if (IsBusy) return;

            if (!IsConnected)
            {
                StatusMessage = "Conecta tu cuenta de Google primero.";
                return;
            }

            if (!_vault.IsUnlocked)
            {
                StatusMessage = "Desbloquea la bóveda antes de respaldarla.";
                return;
            }

            var password = DialogService.AskPassword(
                "Contraseña del respaldo",
                "El respaldo se cifra con esta contraseña antes de subirlo. La necesitarás para restaurarlo.",
                confirm: true,
                requireMinLength: true);
            if (password == null) return;

            try
            {
                IsBusy = true;
                StatusMessage = "Cifrando y subiendo el respaldo…";

                var uploaded = await _coordinator.BackupAsync(password, DateTime.Now);

                StatusMessage = $"Respaldo subido: {uploaded.Name} ({uploaded.DisplaySize}).";
                ToastService.Success("Respaldo cifrado subido a Drive", "Google Drive");

                await RefreshAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"No se pudo subir el respaldo: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task RestoreAsync(DriveBackupFile? file)
        {
            if (file == null || IsBusy) return;

            if (!_vault.IsUnlocked)
            {
                StatusMessage = "Desbloquea la bóveda antes de restaurar.";
                return;
            }

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

                var imported = await _coordinator.RestoreAsync(file.Id, password);

                VaultRestored?.Invoke();

                StatusMessage = $"{imported} entrada(s) restauradas desde {file.Name}.";
                ToastService.Success($"{imported} entradas restauradas", "Google Drive");
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
        public async Task DeleteAsync(DriveBackupFile? file)
        {
            if (file == null || IsBusy) return;

            var confirmed = DialogService.Confirm(
                "Eliminar respaldo",
                $"¿Eliminar «{file.Name}» de Google Drive? Esta acción no se puede deshacer.");
            if (!confirmed) return;

            try
            {
                IsBusy = true;
                await _coordinator.DeleteAsync(file.Id);
                Backups.Remove(file);
                BackupCount = Backups.Count;
                StatusMessage = "Respaldo eliminado de Drive.";
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

        [RelayCommand]
        public void EditCredentials()
        {
            IsEditingCredentials = true;
            StatusMessage = "Cambiar el Client ID desconecta la cuenta actual.";
        }

        [RelayCommand]
        public void OpenConsole()
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
    }
}
