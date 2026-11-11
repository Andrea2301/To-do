using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoItNow.Models;
using DoItNow.Services;
using DoItNow.Services.Conversion;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DoItNow.ViewModels
{
    public partial class DocToolsViewModel : ObservableObject
    {
        private readonly TaskService _taskService;
        private readonly OllamaService _ollamaService;

        // ---------- Modo Documentos: convertir / extraer ----------

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsExtractMode))]
        private bool _isConvertMode = true;

        public bool IsExtractMode => !IsConvertMode;

        // ---------- Convertidor ----------

        public ObservableCollection<ConversionJob> Jobs { get; } = new();

        [ObservableProperty]
        private ConversionJob? _selectedJob;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(OutputFolderDisplay))]
        private string _outputFolder = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(OutputFolderDisplay))]
        private bool _useSourceFolder = true;

        public string OutputFolderDisplay
            => UseSourceFolder || string.IsNullOrWhiteSpace(OutputFolder)
                ? "Junto al archivo original"
                : OutputFolder;

        [ObservableProperty]
        private string _converterStatus = "Añade archivos para empezar. Nada sale de tu equipo.";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConvertAllCommand))]
        private bool _isConverting;

        [ObservableProperty]
        private string _previewText = string.Empty;

        [ObservableProperty]
        private string _previewTitle = "Sin archivo seleccionado";

        [ObservableProperty]
        private string? _lastOutputFolder;

        public bool HasJobs => Jobs.Count > 0;
        public bool CanConvert => Jobs.Count > 0 && !IsConverting;

        private bool CanRunConversion() => CanConvert;

        // ---------- Modo extraer tareas (IA) ----------

        [ObservableProperty] private string _selectedFilePath = string.Empty;
        [ObservableProperty] private string _fileName = "Ningún archivo seleccionado";
        [ObservableProperty] private string _fileContent = string.Empty;
        [ObservableProperty] private string _extractedText = string.Empty;
        [ObservableProperty] private string _statusMessage = "Selecciona o arrastra un archivo de texto o notas para procesar.";
        [ObservableProperty] private bool _isProcessing;
        [ObservableProperty] private int _wordCount;

        public DocToolsViewModel(TaskService? taskService = null, OllamaService? ollamaService = null)
        {
            _taskService = taskService ?? new TaskService();
            _ollamaService = ollamaService ?? new OllamaService();
        }

        partial void OnSelectedJobChanged(ConversionJob? value)
        {
            PreviewTitle = value is null
                ? "Sin archivo seleccionado"
                : value.OutputPath is { Length: > 0 } output
                    ? System.IO.Path.GetFileName(output)
                    : value.FileName;

            PreviewText = value?.Preview ?? string.Empty;
        }

        #region Modo

        [RelayCommand]
        private void ShowConverter()
        {
            IsConvertMode = true;
        }

        [RelayCommand]
        private void ShowExtractor()
        {
            IsConvertMode = false;
        }

        #endregion

        #region Convertidor: comandos

        [RelayCommand]
        private void AddFiles()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Añadir documentos",
                Filter = DocumentFormats.FileFilter,
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
                AddFiles(dialog.FileNames);
        }

        public void AddFiles(IEnumerable<string> paths)
        {
            var added = 0;
            var skipped = new List<string>();

            foreach (var path in paths)
            {
                var kind = DocumentFormats.Detect(path);

                if (!DocumentFormats.IsSupported(kind) || !File.Exists(path))
                {
                    skipped.Add(Path.GetFileName(path));
                    continue;
                }

                if (Jobs.Any(j => string.Equals(j.FullPath, path, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var job = new ConversionJob(path);
                Jobs.Add(job);
                added++;
            }

            if (added > 0 && SelectedJob is null)
                SelectedJob = Jobs[0];

            ConverterStatus = Jobs.Count == 0
                ? "Añade archivos para empezar. Nada sale de tu equipo."
                : $"{Jobs.Count} archivo{(Jobs.Count == 1 ? "" : "s")} en cola.";

            if (skipped.Count > 0)
            {
                ConverterStatus += $" {skipped.Count} omitido{(skipped.Count == 1 ? "" : "s")} " +
                                   $"({string.Join(", ", skipped.Take(2))}{(skipped.Count > 2 ? "…" : "")}).";
            }

            OnPropertyChanged(nameof(HasJobs));
            ConvertAllCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void RemoveJob(ConversionJob? job)
        {
            if (job is null) return;

            Jobs.Remove(job);

            if (SelectedJob == job)
                SelectedJob = Jobs.FirstOrDefault();

            OnPropertyChanged(nameof(HasJobs));
            ConvertAllCommand.NotifyCanExecuteChanged();

            ConverterStatus = Jobs.Count == 0
                ? "Añade archivos para empezar. Nada sale de tu equipo."
                : $"{Jobs.Count} archivo{(Jobs.Count == 1 ? "" : "s")} en cola.";
        }

        [RelayCommand]
        private void ClearJobs()
        {
            Jobs.Clear();
            SelectedJob = null;
            PreviewText = string.Empty;
            PreviewTitle = "Sin archivo seleccionado";
            ConverterStatus = "Añade archivos para empezar. Nada sale de tu equipo.";

            OnPropertyChanged(nameof(HasJobs));
            ConvertAllCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void ChooseOutputFolder()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Carpeta de destino",
                Multiselect = false
            };

            if (UseSourceFolder && SelectedJob is not null)
            {
                var folder = Path.GetDirectoryName(SelectedJob.FullPath);
                if (!string.IsNullOrEmpty(folder)) dialog.InitialDirectory = folder;
            }

            if (dialog.ShowDialog() == true)
            {
                OutputFolder = dialog.FolderName;
                UseSourceFolder = false;
            }
        }

        [RelayCommand]
        private void ResetOutputFolder()
        {
            UseSourceFolder = true;
            OutputFolder = string.Empty;
        }

        [RelayCommand(CanExecute = nameof(CanRunConversion))]
        private async Task ConvertAllAsync()
        {
            if (Jobs.Count == 0 || IsConverting) return;

            IsConverting = true;
            var converted = 0;
            var failed = 0;

            try
            {
                foreach (var job in Jobs)
                {
                    if (job.SelectedTarget is null) continue;

                    job.IsBusy = true;
                    job.IsError = false;
                    job.IsDone = false;
                    job.Status = "Convirtiendo…";
                    ConverterStatus = $"Convirtiendo {job.FileName}…";

                    try
                    {
                        var result = await DocumentConverter.ConvertAsync(
                            job.FullPath,
                            job.SelectedTarget,
                            UseSourceFolder ? null : OutputFolder);

                        job.OutputPath = result.OutputPath;
                        job.Preview = result.Preview;
                        job.IsDone = true;
                        job.Status = "Listo";
                        LastOutputFolder = Path.GetDirectoryName(result.OutputPath);

                        if (SelectedJob == job)
                        {
                            PreviewText = result.Preview;
                            PreviewTitle = Path.GetFileName(result.OutputPath);
                        }

                        converted++;
                    }
                    catch (Exception ex)
                    {
                        job.IsError = true;
                        job.Status = ex.Message;
                        failed++;
                    }
                    finally
                    {
                        job.IsBusy = false;
                    }
                }

                ConverterStatus = failed == 0
                    ? $"{converted} archivo{(converted == 1 ? "" : "s")} convertido{(converted == 1 ? "" : "s")}."
                    : $"{converted} convertido{(converted == 1 ? "" : "s")} · {failed} con error.";

                if (converted > 0)
                    ToastService.Info(ConverterStatus, "Conversión completada");
            }
            finally
            {
                IsConverting = false;
                ConvertAllCommand.NotifyCanExecuteChanged();
            }
        }

        [RelayCommand]
        private void OpenOutputFolder()
        {
            var folder = LastOutputFolder
                         ?? (UseSourceFolder ? null : OutputFolder)
                         ?? (SelectedJob is not null ? Path.GetDirectoryName(SelectedJob.FullPath) : null);

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }

        [RelayCommand]
        private void OpenSelectedOutput()
        {
            var path = SelectedJob?.OutputPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }

        #endregion

        #region Extraer tareas (IA)

        [RelayCommand]
        public void SelectFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Seleccionar documento",
                Filter = "Documentos (*.txt;*.md;*.json;*.csv)|*.txt;*.md;*.json;*.csv|Todos los archivos (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                LoadFile(dialog.FileName);
            }
        }

        public void LoadFile(string path)
        {
            try
            {
                SelectedFilePath = path;
                FileName = Path.GetFileName(path);
                var text = File.ReadAllText(path, Encoding.UTF8);
                ExtractedText = text;

                var words = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                WordCount = words.Length;
                StatusMessage = $"✓ Archivo cargado ({WordCount} palabras). Listo para analizar.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"⚠ Error al leer archivo: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task ExtractTasksWithAiAsync()
        {
            if (string.IsNullOrWhiteSpace(ExtractedText))
            {
                StatusMessage = "⚠ Primero debes cargar un archivo o pegar texto.";
                return;
            }

            IsProcessing = true;
            StatusMessage = "La IA está analizando el documento y extrayendo tareas pendientes…";

            try
            {
                var prompt = $"Extrae hasta 3 tareas accionables concretas del siguiente documento. " +
                             $"Para cada tarea escribe solo una línea con el formato: [CATEGORIA] Titulo de la tarea\n" +
                             $"Categorías válidas: Personal, Work, Health, Study, Other.\n\n" +
                             $"Documento:\n{(ExtractedText.Length > 2000 ? ExtractedText.Substring(0, 2000) : ExtractedText)}";

                var summary = await _ollamaService.BreakdownTaskAsync("Extracción de documento: " + FileName, prompt);

                var countAdded = 0;
                foreach (var item in summary)
                {
                    if (string.IsNullOrWhiteSpace(item)) continue;

                    var cat = TaskCategory.Work;
                    var title = item;

                    if (item.StartsWith("[Personal]", StringComparison.OrdinalIgnoreCase)) { cat = TaskCategory.Personal; title = item.Substring(10).Trim(); }
                    else if (item.StartsWith("[Work]", StringComparison.OrdinalIgnoreCase)) { cat = TaskCategory.Work; title = item.Substring(6).Trim(); }
                    else if (item.StartsWith("[Health]", StringComparison.OrdinalIgnoreCase)) { cat = TaskCategory.Health; title = item.Substring(8).Trim(); }
                    else if (item.StartsWith("[Study]", StringComparison.OrdinalIgnoreCase)) { cat = TaskCategory.Study; title = item.Substring(7).Trim(); }

                    await _taskService.AddTaskAsync(new TaskItem
                    {
                        Title = title,
                        Description = $"Generada por IA desde el archivo '{FileName}'",
                        Category = cat,
                        Priority = Priority.High,
                        State = State.Pending,
                        DueDate = DateTime.Today.AddDays(1)
                    });
                    countAdded++;
                }

                StatusMessage = countAdded > 0
                    ? $"✓ Se crearon {countAdded} tareas automáticamente en tu lista."
                    : "La IA analizó el texto pero no encontró tareas explícitas.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"⚠ Error de IA: {ex.Message}";
            }
            finally
            {
                IsProcessing = false;
            }
        }

        [RelayCommand]
        public void Clear()
        {
            SelectedFilePath = string.Empty;
            FileName = "Ningún archivo seleccionado";
            ExtractedText = string.Empty;
            WordCount = 0;
            StatusMessage = "Selecciona o arrastra un archivo de texto o notas para procesar.";
        }

        #endregion
    }
}
