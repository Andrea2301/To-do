using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoItNow.Models;
using DoItNow.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace DoItNow.ViewModels
{
    /// <summary>
    /// ViewModel de la bóveda de claves sensibles. Orquesta el desbloqueo, el CRUD y la
    /// exportación/importación cifrada. Toda la criptografía vive en <see cref="VaultService"/>.
    /// </summary>
    public partial class VaultViewModel : ObservableObject
    {
        private readonly VaultService _vaultService;

        public ObservableCollection<VaultEntry> Entries { get; } = new();
        public ObservableCollection<VaultEntry> FilteredEntries { get; } = new();

        public ObservableCollection<string> EntryTypes { get; } = new()
        {
            "API Key", "Token", "Contraseña", "Conexión", "Otro"
        };

        [ObservableProperty] private bool _isUnlocked;
        [ObservableProperty] private bool _hasMasterPassword;
        [ObservableProperty] private bool _isBusy;

        [ObservableProperty] private string _searchText = string.Empty;
        [ObservableProperty] private VaultEntry? _selectedEntry;

        [ObservableProperty] private bool _isNewEntry;
        [ObservableProperty] private bool _isSecretVisible;
        [ObservableProperty] private bool _hasEntries;
        [ObservableProperty] private bool _hasFilteredEntries;

        [ObservableProperty] private string _editTitle = string.Empty;
        [ObservableProperty] private string _editEntryType = "API Key";
        [ObservableProperty] private string _editUsername = string.Empty;
        [ObservableProperty] private string _editUrl = string.Empty;
        [ObservableProperty] private string _editSecret = string.Empty;
        [ObservableProperty] private string _editNotes = string.Empty;

        [ObservableProperty] private string _statusMessage = "Cargando bóveda...";
        [ObservableProperty] private string _unlockPassword = string.Empty;
        [ObservableProperty] private string _unlockError = string.Empty;
        [ObservableProperty] private string _emptyListTitle = "Sin claves todavía";
        [ObservableProperty] private string _emptyListSubtitle = "Crea tu primera entrada y se cifrará al guardarla.";

        public bool IsLocked => !IsUnlocked;
        public bool UsesWindowsProtection => HasMasterPassword == false;
        public bool CanConfigurePassword => IsUnlocked && !HasMasterPassword;
        public bool CanManageMasterPassword => IsUnlocked && HasMasterPassword;
        public bool CanDelete => IsUnlocked && !IsNewEntry && SelectedEntry != null;
        public bool IsEditorEnabled => IsUnlocked && (IsNewEntry || SelectedEntry != null);
        public bool ShowDetailEmptyState => HasEntries && !IsEditorEnabled;
        public bool ShowListEmptyState => HasEntries && !HasFilteredEntries;
        public bool ShowOnboardingState => IsUnlocked && !HasEntries;
        public bool IsSecretConfigured => !string.IsNullOrEmpty(EditSecret);
        public string MaskedSecret => string.IsNullOrEmpty(EditSecret) ? "Sin secreto" : "••••••••••••••••••••";
        public string SecurityStatusText => HasMasterPassword
            ? (IsUnlocked ? "Desbloqueada · contraseña maestra" : "Bloqueada · contraseña maestra")
            : "Protegida por Windows (DPAPI)";
        public string SecurityBadge => HasMasterPassword ? "🔐 Contraseña maestra" : "🛡 DPAPI de Windows";

        public VaultViewModel(VaultService? vaultService = null)
        {
            _vaultService = vaultService ?? new VaultService();
            _ = InitializeAsync();
        }

        public async Task InitializeAsync()
        {
            try
            {
                await _vaultService.InitializeAsync();
                SyncSecurityState();

                if (_vaultService.IsUnlocked)
                {
                    await LoadEntriesAsync();
                    StatusMessage = HasMasterPassword
                        ? "✓ Bóveda desbloqueada. Tus claves están disponibles."
                        : "✓ Bóveda protegida por Windows. Configura una contraseña maestra cuando quieras.";
                }
                else
                {
                    StatusMessage = "Introduce tu contraseña maestra para desbloquear la bóveda.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ No se pudo inicializar la bóveda: {ex.Message}";
            }
        }

        partial void OnIsUnlockedChanged(bool value)
        {
            OnPropertyChanged(nameof(IsLocked));
            OnPropertyChanged(nameof(CanConfigurePassword));
            OnPropertyChanged(nameof(CanManageMasterPassword));
            OnPropertyChanged(nameof(IsEditorEnabled));
            OnPropertyChanged(nameof(CanDelete));
            OnPropertyChanged(nameof(ShowDetailEmptyState));
            OnPropertyChanged(nameof(ShowOnboardingState));
            OnPropertyChanged(nameof(ShowListEmptyState));
            OnPropertyChanged(nameof(SecurityStatusText));
        }

        partial void OnHasMasterPasswordChanged(bool value)
        {
            OnPropertyChanged(nameof(UsesWindowsProtection));
            OnPropertyChanged(nameof(CanConfigurePassword));
            OnPropertyChanged(nameof(CanManageMasterPassword));
            OnPropertyChanged(nameof(SecurityStatusText));
            OnPropertyChanged(nameof(SecurityBadge));
        }

        partial void OnSearchTextChanged(string value) => ApplyFilter();

        partial void OnSelectedEntryChanged(VaultEntry? value)
        {
            OnPropertyChanged(nameof(IsEditorEnabled));
            OnPropertyChanged(nameof(CanDelete));
            OnPropertyChanged(nameof(ShowDetailEmptyState));
            OnPropertyChanged(nameof(ShowOnboardingState));
            OnPropertyChanged(nameof(ShowListEmptyState));

            if (value == null) return;

            IsNewEntry = false;
            EditTitle = value.Title;
            EditEntryType = value.EntryType;
            EditUsername = value.Username;
            EditUrl = value.Url;
            EditSecret = value.Secret;
            EditNotes = value.Notes;
            IsSecretVisible = false;
            OnPropertyChanged(nameof(IsSecretConfigured));
            OnPropertyChanged(nameof(MaskedSecret));
        }

        partial void OnEditSecretChanged(string value)
        {
            OnPropertyChanged(nameof(IsSecretConfigured));
            OnPropertyChanged(nameof(MaskedSecret));
        }

        partial void OnIsNewEntryChanged(bool value)
        {
            OnPropertyChanged(nameof(IsEditorEnabled));
            OnPropertyChanged(nameof(CanDelete));
            OnPropertyChanged(nameof(ShowDetailEmptyState));
            OnPropertyChanged(nameof(ShowOnboardingState));
            OnPropertyChanged(nameof(ShowListEmptyState));
        }

        partial void OnHasEntriesChanged(bool value)
        {
            OnPropertyChanged(nameof(ShowDetailEmptyState));
            OnPropertyChanged(nameof(ShowOnboardingState));
            OnPropertyChanged(nameof(ShowListEmptyState));
        }

        partial void OnHasFilteredEntriesChanged(bool value)
            => OnPropertyChanged(nameof(ShowListEmptyState));

        private void SyncSecurityState()
        {
            HasMasterPassword = _vaultService.HasMasterPassword;
            IsUnlocked = _vaultService.IsUnlocked;
        }

        public async Task LoadEntriesAsync()
        {
            try
            {
                IsBusy = true;
                var entries = await _vaultService.GetEntriesAsync();

                Entries.Clear();
                foreach (var entry in entries)
                    Entries.Add(entry);

                HasEntries = Entries.Count > 0;
                ApplyFilter();
            }
            catch (Exception ex)
            {
                StatusMessage = $"⚠️ {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void ApplyFilter()
        {
            FilteredEntries.Clear();
            var term = SearchText?.Trim().ToLowerInvariant() ?? string.Empty;

            var query = Entries.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(term))
            {
                query = query.Where(e =>
                    e.Title.ToLowerInvariant().Contains(term) ||
                    e.EntryType.ToLowerInvariant().Contains(term) ||
                    e.Username.ToLowerInvariant().Contains(term) ||
                    e.Url.ToLowerInvariant().Contains(term));
            }

            foreach (var entry in query)
                FilteredEntries.Add(entry);

            HasFilteredEntries = FilteredEntries.Count > 0;

            if (Entries.Count == 0)
            {
                EmptyListTitle = "Sin claves todavía";
                EmptyListSubtitle = "Crea tu primera entrada y se cifrará al guardarla.";
            }
            else
            {
                EmptyListTitle = "Sin resultados";
                EmptyListSubtitle = "Prueba con otro término de búsqueda.";
            }
        }

        [RelayCommand]
        public void SelectEntry(VaultEntry? entry)
        {
            if (entry == null) return;
            SelectedEntry = entry;
        }

        [RelayCommand]
        public async Task UnlockAsync(string? password)
        {
            UnlockError = string.Empty;
            var candidate = password ?? UnlockPassword;

            if (string.IsNullOrWhiteSpace(candidate))
            {
                UnlockError = "Escribe la contraseña maestra.";
                return;
            }

            IsBusy = true;
            try
            {
                var ok = await _vaultService.UnlockAsync(candidate);
                if (!ok)
                {
                    UnlockError = "Contraseña incorrecta. Inténtalo de nuevo.";
                    StatusMessage = "⚠️ No se pudo desbloquear la bóveda.";
                    return;
                }

                SyncSecurityState();
                UnlockPassword = string.Empty;
                await LoadEntriesAsync();
                StatusMessage = "✓ Bóveda desbloqueada.";
                ToastService.Success("Bóveda desbloqueada", "Seguridad");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public void Lock()
        {
            _vaultService.Lock();
            SyncSecurityState();

            Entries.Clear();
            FilteredEntries.Clear();
            HasEntries = false;
            SelectedEntry = null;
            IsNewEntry = false;
            ClearEditor();
            IsSecretVisible = false;
            UnlockError = string.Empty;
            StatusMessage = "🔒 Bóveda bloqueada.";
            ToastService.Info("Bóveda bloqueada", "Seguridad");
        }

        [RelayCommand]
        public void NewEntry()
        {
            IsNewEntry = true;
            SelectedEntry = null;
            ClearEditor();
            IsSecretVisible = false;
            StatusMessage = "Nueva entrada. Completa los campos y guarda.";
        }

        private void ClearEditor()
        {
            EditTitle = string.Empty;
            EditEntryType = "API Key";
            EditUsername = string.Empty;
            EditUrl = string.Empty;
            EditSecret = string.Empty;
            EditNotes = string.Empty;
        }

        [RelayCommand]
        public async Task SaveEntryAsync()
        {
            if (!IsUnlocked) return;

            if (string.IsNullOrWhiteSpace(EditTitle))
            {
                StatusMessage = "⚠️ El título es obligatorio.";
                return;
            }

            if (string.IsNullOrWhiteSpace(EditSecret) && string.IsNullOrWhiteSpace(EditNotes))
            {
                StatusMessage = "⚠️ Añade al menos el secreto o una nota.";
                return;
            }

            try
            {
                IsBusy = true;
                var entry = new VaultEntry
                {
                    Id = IsNewEntry ? 0 : SelectedEntry?.Id ?? 0,
                    Title = EditTitle.Trim(),
                    EntryType = EditEntryType,
                    Username = EditUsername.Trim(),
                    Url = EditUrl.Trim(),
                    Secret = EditSecret,
                    Notes = EditNotes
                };

                await _vaultService.SaveEntryAsync(entry);
                await LoadEntriesAsync();

                var saved = Entries.FirstOrDefault(e => e.Id == entry.Id);
                IsNewEntry = false;
                SelectedEntry = saved;

                StatusMessage = "✓ Entrada cifrada y guardada correctamente.";
                ToastService.Success("Clave guardada", "Bóveda");
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ No se pudo guardar: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task DeleteEntryAsync()
        {
            if (SelectedEntry == null || !IsUnlocked) return;

            if (!DialogService.Confirm(
                    "Eliminar entrada",
                    $"¿Eliminar '{SelectedEntry.Title}' de la bóveda? Esta acción no se puede deshacer."))
                return;

            try
            {
                await _vaultService.DeleteEntryAsync(SelectedEntry.Id);
                SelectedEntry = null;
                IsNewEntry = false;
                ClearEditor();
                await LoadEntriesAsync();
                StatusMessage = "🗑 Entrada eliminada.";
                ToastService.Info("Entrada eliminada", "Bóveda");
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ No se pudo eliminar: {ex.Message}";
            }
        }

        [RelayCommand]
        public void CopySecret()
        {
            if (string.IsNullOrEmpty(EditSecret)) return;

            try
            {
                Clipboard.SetText(EditSecret);
                StatusMessage = "📋 Secreto copiado al portapapeles.";
                ToastService.Success("Secreto copiado", "Portapapeles");
            }
            catch (Exception ex)
            {
                StatusMessage = $"⚠️ No se pudo copiar: {ex.Message}";
            }
        }

        [RelayCommand]
        public void CopyUsername()
        {
            if (string.IsNullOrWhiteSpace(EditUsername)) return;

            try
            {
                Clipboard.SetText(EditUsername);
                StatusMessage = "📋 Usuario copiado al portapapeles.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"⚠️ No se pudo copiar: {ex.Message}";
            }
        }

        [RelayCommand]
        public void ToggleSecret() => IsSecretVisible = !IsSecretVisible;

        [RelayCommand]
        public async Task SetupMasterPasswordAsync()
        {
            if (!IsUnlocked || HasMasterPassword) return;

            var password = DialogService.AskPassword(
                "Configurar contraseña maestra",
                "Una sola contraseña desbloqueará toda la bóveda. No se puede recuperar: guárdala en un lugar seguro.",
                confirm: true,
                requireMinLength: true);

            if (password == null) return;

            try
            {
                await _vaultService.SetupMasterPasswordAsync(password);
                SyncSecurityState();
                StatusMessage = "🔐 Contraseña maestra configurada. La bóveda ya no depende solo de Windows.";
                ToastService.Success("Contraseña maestra activada", "Seguridad");
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ No se pudo configurar la contraseña: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task ChangeMasterPasswordAsync()
        {
            if (!IsUnlocked || !HasMasterPassword) return;

            var current = DialogService.AskPassword(
                "Contraseña actual",
                "Introduce tu contraseña maestra actual para cambiarla.");
            if (current == null) return;

            var next = DialogService.AskPassword(
                "Nueva contraseña maestra",
                "Elige una contraseña nueva. Se volverán a cifrar todas las entradas.",
                confirm: true,
                requireMinLength: true);
            if (next == null) return;

            try
            {
                var ok = await _vaultService.ChangeMasterPasswordAsync(current, next);
                if (!ok)
                {
                    StatusMessage = "⚠️ La contraseña actual es incorrecta.";
                    DialogService.Error("Contraseña incorrecta", "La contraseña actual no coincide.");
                    return;
                }

                SyncSecurityState();
                StatusMessage = "🔐 Contraseña maestra actualizada y entradas re-cifradas.";
                ToastService.Success("Contraseña actualizada", "Seguridad");
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ No se pudo cambiar la contraseña: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task RemoveMasterPasswordAsync()
        {
            if (!IsUnlocked || !HasMasterPassword) return;

            if (!DialogService.Confirm(
                    "Quitar contraseña maestra",
                    "La bóveda volverá a protegerse solo con DPAPI de Windows. ¿Continuar?"))
                return;

            try
            {
                await _vaultService.RemoveMasterPasswordAsync();
                SyncSecurityState();
                StatusMessage = "🛡 Contraseña eliminada. La bóveda usa DPAPI de Windows.";
                ToastService.Info("Contraseña maestra eliminada", "Seguridad");
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ No se pudo quitar la contraseña: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task ExportVaultAsync()
        {
            if (!IsUnlocked) return;

            var dialog = new SaveFileDialog
            {
                Title = "Exportar bóveda cifrada",
                Filter = "Bóveda DoItNow (*.ditvault)|*.ditvault|Todos los archivos (*.*)|*.*",
                FileName = $"doitnow-vault-{DateTime.Now:yyyyMMdd}.ditvault",
                DefaultExt = ".ditvault"
            };

            if (dialog.ShowDialog() != true) return;

            var password = DialogService.AskPassword(
                "Contraseña de exportación",
                "El archivo se cifrará con esta contraseña. La necesitarás para importarlo en otro equipo.",
                confirm: true,
                requireMinLength: true);
            if (password == null) return;

            try
            {
                await _vaultService.ExportAsync(dialog.FileName, password);
                StatusMessage = $"✓ Bóveda exportada a {Path.GetFileName(dialog.FileName)}.";
                ToastService.Success("Exportación cifrada creada", "Bóveda");
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ No se pudo exportar: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task ImportVaultAsync()
        {
            if (!IsUnlocked) return;

            var dialog = new OpenFileDialog
            {
                Title = "Importar bóveda cifrada",
                Filter = "Bóveda DoItNow (*.ditvault)|*.ditvault|Todos los archivos (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true) return;

            var password = DialogService.AskPassword(
                "Contraseña de importación",
                "Introduce la contraseña con la que se cifró el archivo.");
            if (password == null) return;

            try
            {
                var imported = await _vaultService.ImportAsync(dialog.FileName, password);
                await LoadEntriesAsync();
                StatusMessage = $"✓ {imported} entradas importadas y re-cifradas con esta bóveda.";
                ToastService.Success($"{imported} entradas importadas", "Bóveda");
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ No se pudo importar: {ex.Message}";
                DialogService.Error("Importación fallida", ex.Message);
            }
        }
    }
}
