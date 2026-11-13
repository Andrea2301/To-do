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
            "API Key", "Token", "Contraseña", "Conexión", "Variable", "Otro"
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
        [ObservableProperty] private bool _hasAttachments;

        /// <summary>Archivos adjuntos de la entrada en edición (metadatos; el contenido vive cifrado).</summary>
        public ObservableCollection<VaultAttachment> EditAttachments { get; } = new();

        /// <summary>Adjuntos de una entrada nueva que aún no tiene Id en la base.</summary>
        private readonly List<PendingAttachment> _pendingAttachments = new();

        private sealed record PendingAttachment(
            string FileName, string ContentType, byte[] Content, VaultAttachment Preview);

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
            EditAttachments.CollectionChanged += (_, _) => HasAttachments = EditAttachments.Count > 0;
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

            FillAttachments(value);
        }

        private void FillAttachments(VaultEntry? entry)
        {
            EditAttachments.Clear();
            _pendingAttachments.Clear();

            if (entry != null)
            {
                foreach (var attachment in entry.Attachments)
                    EditAttachments.Add(attachment);
            }
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
            FillAttachments(null);
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

            if (string.IsNullOrWhiteSpace(EditSecret) &&
                string.IsNullOrWhiteSpace(EditNotes) &&
                EditAttachments.Count == 0)
            {
                StatusMessage = "⚠️ Añade al menos el secreto, una nota o un archivo.";
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

                // Los adjuntos de una entrada nueva se suben ahora que ya existe su Id.
                foreach (var pending in _pendingAttachments)
                {
                    await _vaultService.AddAttachmentAsync(
                        entry.Id, pending.FileName, pending.ContentType, pending.Content);
                }
                _pendingAttachments.Clear();

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

        // --- Archivos adjuntos cifrados ---

        [RelayCommand]
        public async Task AddAttachmentAsync()
        {
            if (!IsUnlocked) return;

            var dialog = new OpenFileDialog
            {
                Title = "Adjuntar archivo a la bóveda",
                Filter = "Todos los archivos (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var info = new FileInfo(dialog.FileName);
                var limitMb = VaultService.MaxAttachmentBytes / (1024 * 1024);

                if (info.Length > VaultService.MaxAttachmentBytes)
                {
                    StatusMessage = $"⚠️ El archivo supera el límite de {limitMb} MB.";
                    DialogService.Error("Archivo demasiado grande",
                        $"El límite por archivo es de {limitMb} MB.");
                    return;
                }

                var content = await File.ReadAllBytesAsync(dialog.FileName);
                var contentType = GuessContentType(dialog.FileName);

                if (SelectedEntry == null || SelectedEntry.Id <= 0)
                {
                    var preview = new VaultAttachment
                    {
                        FileName = info.Name,
                        ContentType = contentType,
                        SizeBytes = content.Length,
                        CreatedAt = DateTime.Now
                    };

                    _pendingAttachments.Add(new PendingAttachment(preview.FileName, contentType, content, preview));
                    EditAttachments.Add(preview);
                    StatusMessage = $"📎 '{info.Name}' listo. Se cifrará al guardar la clave.";
                }
                else
                {
                    var added = await _vaultService.AddAttachmentAsync(
                        SelectedEntry.Id, info.Name, contentType, content);

                    SelectedEntry.Attachments.Add(added);
                    EditAttachments.Add(added);
                    StatusMessage = $"📎 '{added.FileName}' cifrado y adjuntado.";
                    ToastService.Success("Archivo cifrado y adjuntado", "Bóveda");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ No se pudo adjuntar el archivo: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task SaveAttachmentAsync(VaultAttachment? attachment)
        {
            if (attachment == null || !IsUnlocked) return;

            try
            {
                byte[] content;

                if (attachment.Id <= 0)
                {
                    var pending = _pendingAttachments.FirstOrDefault(p => ReferenceEquals(p.Preview, attachment));
                    if (pending == null) return;
                    content = pending.Content;
                }
                else
                {
                    content = await _vaultService.ReadAttachmentAsync(attachment.Id);
                }

                var dialog = new SaveFileDialog
                {
                    Title = "Guardar archivo descifrado",
                    FileName = attachment.FileName,
                    Filter = "Todos los archivos (*.*)|*.*"
                };

                if (dialog.ShowDialog() != true) return;

                await File.WriteAllBytesAsync(dialog.FileName, content);
                StatusMessage = $"✓ Archivo guardado en {Path.GetFileName(dialog.FileName)}.";
                ToastService.Success("Archivo descifrado guardado", "Bóveda");
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ No se pudo guardar el archivo: {ex.Message}";
                DialogService.Error("No se pudo guardar", ex.Message);
            }
        }

        [RelayCommand]
        public async Task DeleteAttachmentAsync(VaultAttachment? attachment)
        {
            if (attachment == null || !IsUnlocked) return;

            if (attachment.Id <= 0)
            {
                var pending = _pendingAttachments.FirstOrDefault(p => ReferenceEquals(p.Preview, attachment));
                if (pending != null) _pendingAttachments.Remove(pending);
                EditAttachments.Remove(attachment);
                return;
            }

            if (!DialogService.Confirm("Eliminar archivo",
                    $"¿Eliminar '{attachment.FileName}' de la bóveda? Esta acción no se puede deshacer."))
                return;

            try
            {
                await _vaultService.DeleteAttachmentAsync(attachment.Id);
                EditAttachments.Remove(attachment);
                SelectedEntry?.Attachments.Remove(attachment);
                StatusMessage = "🗑 Archivo adjunto eliminado.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ No se pudo eliminar el archivo: {ex.Message}";
            }
        }

        // --- Importación de credenciales de AWS ---

        [RelayCommand]
        public async Task ImportAwsCredentialsAsync()
        {
            if (!IsUnlocked) return;

            var dialog = new OpenFileDialog
            {
                Title = "Importar credenciales de AWS",
                Filter = "Credenciales de AWS (*.csv;*.ini;*credentials)|*.csv;*.ini;*credentials|Todos los archivos (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                IsBusy = true;

                var text = await File.ReadAllTextAsync(dialog.FileName);
                var credentials = AwsCredentialImporter.Parse(text, dialog.FileName);

                if (credentials.Count == 0)
                {
                    StatusMessage = "⚠️ No se encontraron credenciales válidas en el archivo.";
                    DialogService.Error("Sin credenciales",
                        "El archivo no parece un CSV de claves de IAM ni un archivo credentials de AWS.");
                    return;
                }

                VaultEntry? first = null;

                foreach (var credential in credentials)
                {
                    var existing = Entries.FirstOrDefault(e =>
                        string.Equals(e.Username, credential.AccessKeyId, StringComparison.Ordinal));

                    var entry = new VaultEntry
                    {
                        Id = existing?.Id ?? 0,
                        Title = existing?.Title ?? $"AWS · {credential.Profile}",
                        EntryType = "API Key",
                        Username = credential.AccessKeyId,
                        Url = "https://console.aws.amazon.com/iam/",
                        Secret = credential.SecretAccessKey,
                        Notes = BuildAwsNotes(credential)
                    };

                    await _vaultService.SaveEntryAsync(entry);
                    first ??= entry;
                }

                // El archivo original se adjunta una sola vez, a la primera entrada.
                var fileName = Path.GetFileName(dialog.FileName);
                var fileContent = await File.ReadAllBytesAsync(dialog.FileName);
                var attached = false;

                if (first != null &&
                    fileContent.Length <= VaultService.MaxAttachmentBytes)
                {
                    var alreadyStored = await _vaultService.GetEntriesAsync();
                    var target = alreadyStored.FirstOrDefault(e => e.Id == first.Id);

                    if (target != null && !target.Attachments.Any(a => a.FileName == fileName))
                    {
                        await _vaultService.AddAttachmentAsync(
                            first.Id, fileName, GuessContentType(fileName), fileContent);
                        attached = true;
                    }
                }

                await LoadEntriesAsync();

                IsNewEntry = false;
                SelectedEntry = first == null ? null : Entries.FirstOrDefault(e => e.Id == first.Id);

                StatusMessage = attached
                    ? $"✓ {credentials.Count} clave(s) de AWS importadas y archivo original adjuntado."
                    : $"✓ {credentials.Count} clave(s) de AWS importadas.";

                ToastService.Success($"{credentials.Count} clave(s) de AWS importadas", "Bóveda");
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ No se pudo importar AWS: {ex.Message}";
                DialogService.Error("Importación de AWS fallida", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static string BuildAwsNotes(AwsCredentialImporter.AwsCredential credential)
        {
            var lines = new List<string>();

            if (!string.IsNullOrWhiteSpace(credential.Region))
                lines.Add($"region = {credential.Region}");

            if (!string.IsNullOrWhiteSpace(credential.SessionToken))
                lines.Add($"aws_session_token = {credential.SessionToken}");

            return string.Join(Environment.NewLine, lines);
        }

        // --- Respaldo en Google Drive ---

        [RelayCommand]
        public async Task OpenDriveBackupAsync()
        {
            if (!IsUnlocked) return;

            try
            {
                if (DialogService.ShowDriveBackup(_vaultService))
                {
                    await LoadEntriesAsync();
                    StatusMessage = "✓ Bóveda actualizada desde Google Drive.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ No se pudo abrir Google Drive: {ex.Message}";
            }
        }

        // --- Importación de variables .env ---
        [RelayCommand]
        public async Task ImportEnvFileAsync()
        {
            if (!IsUnlocked) return;

            var dialog = new OpenFileDialog
            {
                Title = "Importar variables de entorno",
                Filter = "Archivos .env (*.env;*.env.*)|*.env;*.env.*|Todos los archivos (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                IsBusy = true;

                var text = await File.ReadAllTextAsync(dialog.FileName);
                var variables = EnvFileImporter.Parse(text);

                if (variables.Count == 0)
                {
                    StatusMessage = "⚠️ No se encontraron variables válidas en el archivo.";
                    DialogService.Error("Sin variables",
                        "El archivo no contiene líneas con el formato CLAVE=valor.");
                    return;
                }

                var fileName = Path.GetFileName(dialog.FileName);
                var preview = string.Join(", ", variables.Take(6).Select(v => v.Key));
                if (variables.Count > 6)
                    preview += $" y {variables.Count - 6} más";

                var confirmed = DialogService.Confirm(
                    "Importar variables de entorno",
                    $"Se encontraron {variables.Count} variable(s) en {fileName}:\n\n{preview}\n\n" +
                    "Se guardarán cifradas en la bóveda. Las entradas con el mismo nombre se actualizan.");

                if (!confirmed)
                {
                    StatusMessage = "Importación cancelada.";
                    return;
                }

                var import = await EnvVaultImporter.ImportAsync(
                    _vaultService, variables, fileName, DateTime.Now);

                var first = import.First;

                // El archivo original se adjunta una sola vez, a la primera entrada.
                var fileContent = await File.ReadAllBytesAsync(dialog.FileName);
                var attached = false;

                if (first != null && fileContent.Length <= VaultService.MaxAttachmentBytes)
                {
                    var stored = await _vaultService.GetEntriesAsync();
                    var target = stored.FirstOrDefault(e => e.Id == first.Id);

                    if (target != null && !target.Attachments.Any(a => a.FileName == fileName))
                    {
                        await _vaultService.AddAttachmentAsync(
                            first.Id, fileName, GuessContentType(fileName), fileContent);
                        attached = true;
                    }
                }

                await LoadEntriesAsync();

                IsNewEntry = false;
                SelectedEntry = first == null ? null : Entries.FirstOrDefault(e => e.Id == first.Id);

                var summary = import.Updated > 0
                    ? $"{import.Created} nueva(s), {import.Updated} actualizada(s)"
                    : $"{import.Created} nueva(s)";

                StatusMessage = attached
                    ? $"✓ {summary} desde .env y archivo original adjuntado."
                    : $"✓ {summary} desde .env.";

                ToastService.Success($"{variables.Count} variable(s) importadas", "Bóveda");
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ No se pudo importar el archivo .env: {ex.Message}";
                DialogService.Error("Importación .env fallida", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static string GuessContentType(string path)
            => Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".csv" => "text/csv",
                ".ini" => "text/plain",
                ".txt" => "text/plain",
                ".json" => "application/json",
                ".pem" or ".crt" or ".cer" => "application/x-pem-file",
                ".pfx" or ".p12" => "application/x-pkcs12",
                ".zip" => "application/zip",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };
    }
}
