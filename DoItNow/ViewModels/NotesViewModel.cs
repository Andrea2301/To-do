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
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace DoItNow.ViewModels
{
    /// <summary>
    /// Notas con código: reemplaza el bloc de notas de Windows con un editor real
    /// (números de línea, resaltado por lenguaje y exportación de archivos).
    /// </summary>
    public partial class NotesViewModel : ObservableObject
    {
        private readonly NoteService _noteService;
        private CodeNote? _activeNote;

        public ObservableCollection<CodeNote> Notes { get; } = new();
        public ObservableCollection<CodeNote> FilteredNotes { get; } = new();

        public IReadOnlyList<string> Languages => SyntaxHighlightingService.Languages;

        [ObservableProperty] private string _searchText = string.Empty;
        [ObservableProperty] private CodeNote? _selectedNote;
        [ObservableProperty] private bool _isNewNote;
        [ObservableProperty] private bool _hasNotes;
        [ObservableProperty] private bool _hasFilteredNotes;
        [ObservableProperty] private bool _hasSelection;
        [ObservableProperty] private bool _isDirty;
        [ObservableProperty] private bool _isBusy;

        [ObservableProperty] private string _editTitle = string.Empty;
        [ObservableProperty] private string _editContent = string.Empty;
        [ObservableProperty] private string _editLanguage = "C#";
        [ObservableProperty] private string _statusMessage = "Cargando notas...";
        [ObservableProperty] private int _lineCount;
        [ObservableProperty] private int _characterCount;
        [ObservableProperty] private string _emptyListTitle = "Sin notas";
        [ObservableProperty] private string _emptyListSubtitle = "Crea una nota y pega tu código.";

        public bool CanDelete => HasSelection && !IsNewNote && SelectedNote != null;
        public bool ShowEditorEmptyState => HasNotes && !HasSelection;
        public bool ShowListEmptyState => HasNotes && !HasFilteredNotes;
        public bool ShowOnboardingState => !HasNotes;
        public string DirtyLabel => IsDirty ? "● Sin guardar" : "✓ Guardado";
        public string SaveButtonText => IsNewNote ? "Crear nota" : "Guardar";
        public string EditorStatus => $"{EditLanguage} · {LineCount} líneas · {CharacterCount} caracteres";

        public NotesViewModel(NoteService? noteService = null)
        {
            _noteService = noteService ?? new NoteService();
            _ = LoadNotesAsync();
        }

        partial void OnSearchTextChanged(string value) => ApplyFilter();

        partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(DirtyLabel));

        partial void OnHasSelectionChanged(bool value)
        {
            OnPropertyChanged(nameof(CanDelete));
            OnPropertyChanged(nameof(ShowEditorEmptyState));
        }

        partial void OnHasNotesChanged(bool value)
        {
            OnPropertyChanged(nameof(ShowEditorEmptyState));
            OnPropertyChanged(nameof(ShowListEmptyState));
            OnPropertyChanged(nameof(ShowOnboardingState));
        }

        partial void OnHasFilteredNotesChanged(bool value)
            => OnPropertyChanged(nameof(ShowListEmptyState));

        partial void OnIsNewNoteChanged(bool value)
        {
            OnPropertyChanged(nameof(CanDelete));
            OnPropertyChanged(nameof(SaveButtonText));
        }

        partial void OnSelectedNoteChanged(CodeNote? value)
        {
            // Si había cambios sin guardar, los persistimos antes de cambiar de nota.
            if (IsDirty && _activeNote != null && _activeNote.Id > 0)
                PersistSnapshot(_activeNote);

            _activeNote = value;
            OnPropertyChanged(nameof(CanDelete));

            if (value == null) return;

            IsNewNote = false;
            HasSelection = true;

            EditTitle = value.Title;
            EditContent = value.Content;
            EditLanguage = string.IsNullOrWhiteSpace(value.Language) ? "Texto plano" : value.Language;

            IsDirty = false;
            UpdateCounters();
            StatusMessage = $"📝 {value.Title} · {value.Language}";
        }

        partial void OnEditContentChanged(string value)
        {
            UpdateCounters();
            OnPropertyChanged(nameof(EditorStatus));
            MarkDirty();
        }

        partial void OnEditTitleChanged(string value) => MarkDirty();

        partial void OnEditLanguageChanged(string value)
        {
            OnPropertyChanged(nameof(EditorStatus));
            MarkDirty();
        }

        private void MarkDirty()
        {
            if (!HasSelection) return;
            IsDirty = true;
        }

        private void UpdateCounters()
        {
            var content = EditContent ?? string.Empty;
            CharacterCount = content.Length;
            LineCount = content.Length == 0 ? 0 : content.Split('\n').Length;
        }

        /// <summary>Guarda una instantánea de la nota activa sin recargar la lista ni mover el cursor.</summary>
        private void PersistSnapshot(CodeNote note)
        {
            var snapshot = new CodeNote
            {
                Id = note.Id,
                Title = string.IsNullOrWhiteSpace(EditTitle) ? "Sin título" : EditTitle.Trim(),
                Content = EditContent ?? string.Empty,
                Language = EditLanguage,
                CreatedAt = note.CreatedAt
            };

            note.Title = snapshot.Title;
            note.Content = snapshot.Content;
            note.Language = snapshot.Language;

            _ = _noteService.UpdateAsync(snapshot);
        }

        public async Task LoadNotesAsync()
        {
            try
            {
                IsBusy = true;
                var notes = await _noteService.GetAllAsync();

                Notes.Clear();
                foreach (var note in notes)
                    Notes.Add(note);

                HasNotes = Notes.Count > 0;
                ApplyFilter();

                if (SelectedNote == null && !IsNewNote && FilteredNotes.Count > 0)
                    SelectedNote = FilteredNotes[0];

                if (Notes.Count == 0)
                    StatusMessage = "No hay notas todavía. Crea la primera con «Nueva nota».";
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ No se pudieron cargar las notas: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void ApplyFilter()
        {
            FilteredNotes.Clear();
            var term = SearchText?.Trim().ToLowerInvariant() ?? string.Empty;

            var query = Notes.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(term))
            {
                query = query.Where(n =>
                    n.Title.ToLowerInvariant().Contains(term) ||
                    n.Language.ToLowerInvariant().Contains(term) ||
                    n.Content.ToLowerInvariant().Contains(term));
            }

            foreach (var note in query)
                FilteredNotes.Add(note);

            HasFilteredNotes = FilteredNotes.Count > 0;

            if (Notes.Count == 0)
            {
                EmptyListTitle = "Sin notas";
                EmptyListSubtitle = "Crea una nota y pega tu código.";
            }
            else
            {
                EmptyListTitle = "Sin resultados";
                EmptyListSubtitle = "Prueba con otro término de búsqueda.";
            }
        }

        [RelayCommand]
        public void SelectNote(CodeNote? note)
        {
            if (note == null) return;
            SelectedNote = note;
        }

        [RelayCommand]
        public void NewNote()
        {
            if (IsDirty && _activeNote != null && _activeNote.Id > 0)
                PersistSnapshot(_activeNote);

            _activeNote = null;
            SelectedNote = null;
            IsNewNote = true;
            HasSelection = true;

            EditTitle = "Nueva nota";
            EditContent = string.Empty;
            EditLanguage = "C#";
            IsDirty = false;
            UpdateCounters();
            StatusMessage = "Nueva nota. Pega tu código o texto y guarda.";
        }

        [RelayCommand]
        public async Task SaveNoteAsync()
        {
            if (!HasSelection) return;

            try
            {
                IsBusy = true;
                var note = new CodeNote
                {
                    Id = IsNewNote ? 0 : SelectedNote?.Id ?? _activeNote?.Id ?? 0,
                    Title = string.IsNullOrWhiteSpace(EditTitle) ? "Sin título" : EditTitle.Trim(),
                    Content = EditContent ?? string.Empty,
                    Language = EditLanguage,
                    CreatedAt = SelectedNote?.CreatedAt ?? _activeNote?.CreatedAt ?? DateTime.Now
                };

                if (note.Id <= 0)
                    await _noteService.AddAsync(note);
                else
                    await _noteService.UpdateAsync(note);

                IsNewNote = false;
                IsDirty = false;

                await LoadNotesAsync();

                var saved = Notes.FirstOrDefault(n => n.Id == note.Id);
                if (saved != null)
                    SelectedNote = saved;

                IsDirty = false;
                StatusMessage = $"✓ Nota guardada · {note.Language} · {note.Content.Length} caracteres.";
                ToastService.Success("Nota guardada", "Notas de código");
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ No se pudo guardar la nota: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task DeleteNoteAsync()
        {
            if (SelectedNote == null) return;

            if (!DialogService.Confirm(
                    "Eliminar nota",
                    $"¿Eliminar '{SelectedNote.Title}'? Esta acción no se puede deshacer."))
                return;

            try
            {
                await _noteService.DeleteAsync(SelectedNote.Id);

                _activeNote = null;
                SelectedNote = null;
                IsNewNote = false;
                HasSelection = false;
                IsDirty = false;
                EditTitle = string.Empty;
                EditContent = string.Empty;
                UpdateCounters();

                await LoadNotesAsync();
                StatusMessage = "🗑 Nota eliminada.";
                ToastService.Info("Nota eliminada", "Notas de código");
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ No se pudo eliminar la nota: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task DuplicateNoteAsync()
        {
            if (SelectedNote == null) return;

            try
            {
                var copy = new CodeNote
                {
                    Title = $"{SelectedNote.Title} (copia)",
                    Content = EditContent,
                    Language = EditLanguage
                };

                await _noteService.AddAsync(copy);
                await LoadNotesAsync();

                var saved = Notes.FirstOrDefault(n => n.Id == copy.Id);
                if (saved != null)
                    SelectedNote = saved;

                StatusMessage = "📄 Nota duplicada.";
                ToastService.Success("Nota duplicada", "Notas de código");
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ No se pudo duplicar: {ex.Message}";
            }
        }

        [RelayCommand]
        public void CopyContent()
        {
            if (string.IsNullOrEmpty(EditContent)) return;

            try
            {
                Clipboard.SetText(EditContent);
                StatusMessage = "📋 Contenido copiado al portapapeles.";
                ToastService.Success("Código copiado", "Portapapeles");
            }
            catch (Exception ex)
            {
                StatusMessage = $"⚠️ No se pudo copiar: {ex.Message}";
            }
        }

        [RelayCommand]
        public void ExportNote()
        {
            if (!HasSelection) return;

            var extension = EditLanguage switch
            {
                "C#" => ".cs",
                "JSON" => ".json",
                "SQL" => ".sql",
                "JavaScript" => ".js",
                "Python" => ".py",
                "PowerShell" => ".ps1",
                "XML" => ".xml",
                "Markdown" => ".md",
                _ => ".txt"
            };

            var safeTitle = string.Join("_", (EditTitle ?? "nota")
                .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

            var dialog = new SaveFileDialog
            {
                Title = "Exportar nota",
                Filter = $"Archivo {EditLanguage} (*{extension})|*{extension}|Todos los archivos (*.*)|*.*",
                FileName = $"{safeTitle}{extension}",
                DefaultExt = extension
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                File.WriteAllText(dialog.FileName, EditContent ?? string.Empty, new UTF8Encoding(false));
                StatusMessage = $"✓ Nota exportada a {Path.GetFileName(dialog.FileName)}.";
                ToastService.Success("Nota exportada", "Archivo");
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ No se pudo exportar: {ex.Message}";
            }
        }
    }
}
