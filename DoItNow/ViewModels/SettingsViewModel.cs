using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoItNow.Data;
using DoItNow.Models;
using DoItNow.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DoItNow.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly SettingsService _settingsService;
        private readonly OllamaService _ollamaService;
        private readonly TaskService _taskService;
        private bool _isLoaded;

        /// <summary>Se dispara tras cargar datos de demostración para refrescar las vistas.</summary>
        public event Action? DemoDataLoaded;

        [ObservableProperty] private string _ollamaUrl = "http://localhost:11434";
        [ObservableProperty] private string _selectedModel = "llama3.2";
        [ObservableProperty] private string _groqApiKey = string.Empty;
        [ObservableProperty] private bool _isApiKeyVisible;
        [ObservableProperty] private string _aiProvider = "Auto";
        [ObservableProperty] private ThemeInfo? _selectedTheme;
        [ObservableProperty] private string _databasePath = AppDbContext.DatabasePath;
        [ObservableProperty] private string _statusMessage = string.Empty;
        [ObservableProperty] private bool _isSaving;
        [ObservableProperty] private bool _isLoading;

        public ObservableCollection<string> AvailableModels { get; } = new()
        {
            "llama3.2",
            "mistral",
            "deepseek-r1:1.5b",
            "phi3"
        };

        public ObservableCollection<string> AvailableProviders { get; } = new() { "Auto", "Ollama", "Groq" };
        public IReadOnlyList<ThemeInfo> AvailableThemes => ThemeService.Themes;

        private bool _syncingTheme;

        /// <summary>Representación segura de la clave: nunca se muestra completa en pantalla.</summary>
        public string MaskedGroqApiKey => string.IsNullOrWhiteSpace(GroqApiKey)
            ? "Sin clave configurada"
            : "••••••••••••••••••••••••";

        partial void OnGroqApiKeyChanged(string value)
            => OnPropertyChanged(nameof(MaskedGroqApiKey));

        partial void OnSelectedThemeChanged(ThemeInfo? value)
        {
            if (value == null || _syncingTheme) return;

            ThemeService.Apply(value.Id);
            _ = _settingsService.SetSettingAsync(
                SettingsService.KeyTheme,
                value.Id,
                "Tema de la aplicación");
        }

        private void OnThemeServiceChanged(ThemeInfo theme)
        {
            if (SelectedTheme?.Id == theme.Id) return;

            _syncingTheme = true;
            try
            {
                SelectedTheme = theme;
            }
            finally
            {
                _syncingTheme = false;
            }
        }

        [RelayCommand]
        private void ToggleApiKeyVisibility() => IsApiKeyVisible = !IsApiKeyVisible;

        public SettingsViewModel(SettingsService? settingsService = null, OllamaService? ollamaService = null)
        {
            _settingsService = settingsService ?? new SettingsService();
            _ollamaService = ollamaService ?? new OllamaService(_settingsService);
            _taskService = new TaskService();

            ThemeService.ThemeChanged += OnThemeServiceChanged;
            _ = LoadSettingsAsync();
        }

        public async Task LoadSettingsAsync(bool forceReload = false)
        {
            if (IsLoading) return;
            if (_isLoaded && !forceReload) return;

            IsLoading = true;
            try
            {
                OllamaUrl = await _settingsService.GetOllamaUrlAsync();
                
                var savedModel = await _settingsService.GetOllamaModelAsync();
                if (!string.IsNullOrWhiteSpace(savedModel))
                {
                    SelectedModel = savedModel;
                }

                GroqApiKey = await _settingsService.GetGroqApiKeyAsync();
                AiProvider = await _settingsService.GetAiProviderAsync();

                // El tema activo ya se resolvió al arrancar (CLI > base de datos > appsettings).
                // Reflejamos ThemeService.Current para que el selector no contradiga la UI.
                _syncingTheme = true;
                try
                {
                    SelectedTheme = ThemeService.Current;
                }
                finally
                {
                    _syncingTheme = false;
                }

                await RefreshModelsAsync();
                _isLoaded = true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"⚠️ Error al cargar configuración: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        public async Task RefreshModelsAsync()
        {
            try
            {
                StatusMessage = "Comprobando servicios de IA...";
                var (_, _, badge) = await _ollamaService.GetActiveProviderStatusAsync();

                var installed = await _ollamaService.GetInstalledModelsAsync();
                var currentSelection = SelectedModel;

                var targetList = new List<string>();
                if (installed != null && installed.Count > 0)
                {
                    targetList.AddRange(installed.Where(m => !string.IsNullOrWhiteSpace(m)));
                }
                else
                {
                    targetList.AddRange(new[] { "llama3.2", "mistral", "deepseek-r1:1.5b", "phi3" });
                }

                if (!string.IsNullOrWhiteSpace(currentSelection) && !targetList.Contains(currentSelection))
                {
                    targetList.Insert(0, currentSelection);
                }

                // Sincronizar AvailableModels sin vaciar bruscamente para no romper el ComboBox si está abierto
                for (int i = AvailableModels.Count - 1; i >= 0; i--)
                {
                    if (!targetList.Contains(AvailableModels[i]))
                    {
                        AvailableModels.RemoveAt(i);
                    }
                }
                foreach (var item in targetList)
                {
                    if (!AvailableModels.Contains(item))
                    {
                        AvailableModels.Add(item);
                    }
                }

                if (!string.IsNullOrWhiteSpace(currentSelection) && AvailableModels.Contains(currentSelection))
                {
                    SelectedModel = currentSelection;
                }
                else if (AvailableModels.Count > 0)
                {
                    SelectedModel = AvailableModels[0];
                }

                StatusMessage = $"Estado actual: {badge}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"⚠️ No se pudieron listar modelos: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task SaveSettingsAsync()
        {
            if (IsSaving) return;
            IsSaving = true;
            try
            {
                StatusMessage = "Guardando configuración...";
                var url = string.IsNullOrWhiteSpace(OllamaUrl) ? "http://localhost:11434" : OllamaUrl.Trim();
                var model = string.IsNullOrWhiteSpace(SelectedModel) ? "llama3.2" : SelectedModel;
                var groqKey = GroqApiKey?.Trim() ?? string.Empty;
                var provider = string.IsNullOrWhiteSpace(AiProvider) ? "Auto" : AiProvider;
                var themeId = SelectedTheme?.Id ?? ThemeService.CurrentId;

                await _settingsService.SetSettingAsync(SettingsService.KeyOllamaUrl, url, "URL local o remota de Ollama");
                await _settingsService.SetSettingAsync(SettingsService.KeyOllamaModel, model, "Modelo activo de Ollama");
                await _settingsService.SetSettingAsync(SettingsService.KeyGroqApiKey, groqKey, "API Key de Groq Cloud");
                await _settingsService.SetSettingAsync(SettingsService.KeyAiProvider, provider, "Proveedor activo de IA");
                await _settingsService.SetSettingAsync(SettingsService.KeyTheme, themeId, "Tema de la aplicación");

                var (_, _, badge) = await _ollamaService.GetActiveProviderStatusAsync();
                StatusMessage = $"✓ ¡Guardado exitosamente! {badge}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Error al guardar: {ex.Message}";
            }
            finally
            {
                IsSaving = false;
            }
        }

        /// <summary>
        /// Inserta un set curado de tareas (idempotente) para que la demo y las
        /// capturas del portafolio siempre luzcan completas.
        /// </summary>
        [RelayCommand]
        public async Task LoadDemoDataAsync()
        {
            try
            {
                StatusMessage = "Cargando datos de demostración...";
                var existing = await _taskService.GetAllTasksAsync();
                var existingTitles = existing
                    .Select(t => t.Title)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var today = DateTime.Today;
                var demo = BuildDemoTasks(today)
                    .Where(t => !existingTitles.Contains(t.Title))
                    .ToList();

                foreach (var task in demo)
                {
                    await _taskService.AddTaskAsync(task);
                }

                StatusMessage = demo.Count > 0
                    ? $"✓ Se añadieron {demo.Count} tareas de demostración."
                    : "ℹ️ Los datos de demostración ya estaban cargados.";

                ToastService.Success(
                    demo.Count > 0 ? $"{demo.Count} tareas de demo añadidas" : "La demo ya estaba cargada",
                    "Datos de demostración");

                DemoDataLoaded?.Invoke();
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ No se pudieron cargar los datos demo: {ex.Message}";
            }
        }

        private static List<TaskItem> BuildDemoTasks(DateTime today)
        {
            static SubTaskItem Sub(string title, bool done = false) => new()
            {
                Title = title,
                IsCompleted = done,
                CreatedAt = DateTime.Now
            };

            return new List<TaskItem>
            {
                new()
                {
                    Title = "Diseñar el sistema visual de DoItNow",
                    Description = "Definir paleta, tipografía, elevación y estados de interacción.",
                    Priority = Priority.High,
                    Category = TaskCategory.Work,
                    State = State.Active,
                    DueDate = today,
                    EstimatedMinutes = 90,
                    SubTasks = new()
                    {
                        Sub("Auditar la UI actual", true),
                        Sub("Definir tokens de color y tema oscuro"),
                        Sub("Documentar componentes en el README")
                    }
                },
                new()
                {
                    Title = "Revisar PR de integración con Groq",
                    Description = "Validar el fallback automático Ollama → Groq y los mensajes de error.",
                    Priority = Priority.High,
                    Category = TaskCategory.Work,
                    State = State.Active,
                    DueDate = today,
                    EstimatedMinutes = 45
                },
                new()
                {
                    Title = "Bloque de foco: refactor del parser",
                    Description = "Sin notificaciones: separar el parseo de credenciales de la UI.",
                    Priority = Priority.Medium,
                    Category = TaskCategory.Work,
                    State = State.Pending,
                    DueDate = today,
                    EstimatedMinutes = 120
                },
                new()
                {
                    Title = "Rotar claves de AWS en el vault",
                    Description = "Importar el CSV de IAM, actualizar las entradas y archivar el original.",
                    Priority = Priority.Medium,
                    Category = TaskCategory.Work,
                    State = State.Pending,
                    DueDate = today,
                    EstimatedMinutes = 25
                },
                new()
                {
                    Title = "Preparar demo para portafolio",
                    Description = "Grabar el flujo principal y capturar las pantallas clave.",
                    Priority = Priority.High,
                    Category = TaskCategory.Work,
                    State = State.Active,
                    DueDate = today.AddDays(1),
                    EstimatedMinutes = 75,
                    SubTasks = new()
                    {
                        Sub("Preparar datos de demostración"),
                        Sub("Capturar modo claro y oscuro"),
                        Sub("Escribir el README en inglés")
                    }
                },
                new()
                {
                    Title = "Estudiar patrones de MVVM en WPF",
                    Description = "Repasar CommunityToolkit.Mvvm y comandos asíncronos.",
                    Priority = Priority.Low,
                    Category = TaskCategory.Study,
                    State = State.Pending,
                    DueDate = today.AddDays(1),
                    EstimatedMinutes = 60
                },
                new()
                {
                    Title = "Actualizar README y capturas",
                    Description = "Añadir arquitectura, stack y guía de ejecución.",
                    Priority = Priority.Medium,
                    Category = TaskCategory.Work,
                    State = State.Pending,
                    DueDate = today.AddDays(2),
                    EstimatedMinutes = 40
                },
                new()
                {
                    Title = "Revisar logs del pipeline de CI",
                    Description = "Detectar el test intermitente y abrir un issue con la traza.",
                    Priority = Priority.Low,
                    Category = TaskCategory.Work,
                    State = State.Pending,
                    DueDate = today.AddDays(2),
                    EstimatedMinutes = 20
                },
                new()
                {
                    Title = "Planificar el sprint semanal",
                    Description = "Priorizar el backlog y definir los tres objetivos clave.",
                    Priority = Priority.High,
                    Category = TaskCategory.Work,
                    State = State.Pending,
                    DueDate = today.AddDays(-1),
                    EstimatedMinutes = 30
                },
                new()
                {
                    Title = "Configurar SQLite + EF Core",
                    Description = "Modelo de datos, índices y migraciones seguras.",
                    Priority = Priority.High,
                    Category = TaskCategory.Work,
                    State = State.Completed,
                    IsCompleted = true,
                    CompletedAt = DateTime.Now.AddHours(-2),
                    DueDate = today,
                    EstimatedMinutes = 60
                },
                new()
                {
                    Title = "Diseñar wireframes del dashboard",
                    Description = "Explorar la jerarquía visual y el timeline semanal.",
                    Priority = Priority.Medium,
                    Category = TaskCategory.Personal,
                    State = State.Completed,
                    IsCompleted = true,
                    CompletedAt = DateTime.Now.AddHours(-1),
                    DueDate = today,
                    EstimatedMinutes = 45
                }
            };
        }

        [RelayCommand]
        public void OpenGroqConsole()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://console.groq.com/keys",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"No se pudo abrir el navegador: {ex.Message}";
            }
        }

        [RelayCommand]
        public void OpenDatabaseFolder()
        {
            try
            {
                var dir = Path.GetDirectoryName(DatabasePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"No se pudo abrir la carpeta: {ex.Message}";
            }
        }
    }
}
