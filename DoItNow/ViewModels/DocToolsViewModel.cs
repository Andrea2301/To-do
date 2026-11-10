using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoItNow.Models;
using DoItNow.Services;
using Microsoft.Win32;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DoItNow.ViewModels
{
    public partial class DocToolsViewModel : ObservableObject
    {
        private readonly TaskService _taskService;
        private readonly OllamaService _ollamaService;

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
                StatusMessage = $"❌ Error al leer archivo: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task ExtractTasksWithAiAsync()
        {
            if (string.IsNullOrWhiteSpace(ExtractedText))
            {
                StatusMessage = "⚠️ Primero debes cargar un archivo o pegar texto.";
                return;
            }

            IsProcessing = true;
            StatusMessage = "🤖 Ollama está analizando el documento y extrayendo tareas pendientes...";

            try
            {
                var prompt = $"Extrae hasta 3 tareas accionables concretas del siguiente documento. " +
                             $"Para cada tarea escribe solo una línea con el formato: [CATEGORIA] Titulo de la tarea\n" +
                             $"Categorías válidas: Personal, Work, Health, Study, Other.\n\n" +
                             $"Documento:\n{(ExtractedText.Length > 2000 ? ExtractedText.Substring(0, 2000) : ExtractedText)}";

                var summary = await _ollamaService.BreakdownTaskAsync("Extracción de documento: " + FileName, prompt);

                int countAdded = 0;
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
                    ? $"🎉 ¡Éxito! Se crearon {countAdded} tareas automáticamente en tu lista."
                    : "ℹ️ Ollama analizó el texto pero no encontró tareas explícitas.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Error con Ollama: {ex.Message}";
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
    }
}
