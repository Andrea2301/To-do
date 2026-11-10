using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoItNow.Models;
using DoItNow.Services;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Threading;

namespace DoItNow.ViewModels
{
    /// <summary>
    /// ViewModel raíz — orquesta la navegación general entre todos los módulos:
    /// Dashboard, Tareas, Calendario, AI Chat (Ollama), Doc Tools y Configuración.
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        private readonly TaskService _taskService;
        private readonly SettingsService _settingsService;
        private readonly OllamaService _ollamaService;
        private readonly ChatService _chatService;

        public DashboardViewModel Dashboard { get; }
        public TasksViewModel Tasks { get; }
        public CalendarViewModel Calendar { get; }
        public NotesViewModel Notes { get; }
        public VaultViewModel Vault { get; }
        public AiChatViewModel AiChat { get; }
        public DocToolsViewModel DocTools { get; }
        public SettingsViewModel Settings { get; }

        // Alias para compatibilidad de bindings en XAML
        public DashboardViewModel DashboardVM => Dashboard;
        public TasksViewModel TasksVM => Tasks;
        public CalendarViewModel CalendarVM => Calendar;
        public NotesViewModel NotesVM => Notes;
        public VaultViewModel VaultVM => Vault;
        public AiChatViewModel AiChatVM => AiChat;
        public DocToolsViewModel DocToolsVM => DocTools;
        public SettingsViewModel SettingsVM => Settings;

        [ObservableProperty] private ObservableObject _currentView;
        [ObservableProperty] private string _activeSection = "Dashboard";
        [ObservableProperty] private string _currentTime = string.Empty;
        [ObservableProperty] private string _currentDate = string.Empty;
        [ObservableProperty] private string _currentSectionTitle = "Dashboard";
        [ObservableProperty] private string _activeViewMode = "Daily";
        [ObservableProperty] private string _aiMode = "Chat";
        [ObservableProperty] private string _formattedSelectedDate = string.Empty;
        [ObservableProperty] private ThemeInfo _selectedTheme = ThemeService.Current;
        [ObservableProperty] private string _weekRangeLabel = string.Empty;

        public ObservableCollection<WeekDayItem> WeekDays { get; } = new();

        public IReadOnlyList<ThemeInfo> AvailableThemes => ThemeService.Themes;

        private readonly DispatcherTimer _clockTimer;
        private bool _syncingTheme;

        public MainViewModel()
        {
            _taskService = new TaskService();
            _settingsService = new SettingsService();
            _ollamaService = new OllamaService(_settingsService);
            _chatService = new ChatService(_ollamaService);

            Dashboard = new DashboardViewModel(_taskService);
            Tasks     = new TasksViewModel(_taskService);
            Calendar  = new CalendarViewModel(_taskService);
            Notes     = new NotesViewModel();
            Vault     = new VaultViewModel();
            AiChat    = new AiChatViewModel(_chatService, _ollamaService, _settingsService);
            DocTools  = new DocToolsViewModel(_taskService, _ollamaService);
            Settings  = new SettingsViewModel(_settingsService, _ollamaService);

            _currentView = Dashboard;

            // Sincronización de eventos de tareas en tiempo real
            Tasks.TaskChanged += async () =>
            {
                await Dashboard.LoadStatsAsync();
            };

            // Al cargar datos demo desde Configuración, refrescamos agenda y métricas.
            Settings.DemoDataLoaded += async () =>
            {
                await Tasks.RefreshAsync();
                await Dashboard.LoadStatsAsync();
            };

            // El timeline del dashboard muestra el día seleccionado.
            Tasks.SelectedDateFilter = Calendar.SelectedDate;

            UpdateFormattedSelectedDate();
            RebuildWeekDays();
            Calendar.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Calendar.SelectedDate))
                {
                    UpdateFormattedSelectedDate();
                    RebuildWeekDays();
                    Tasks.SelectedDateFilter = Calendar.SelectedDate;
                }
            };

            // Inicializar reloj
            CurrentTime = DateTime.Now.ToString("HH:mm");
            CurrentDate = DateTime.Now.ToString("dddd, dd MMM", new CultureInfo("es-ES"));

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _clockTimer.Tick += (_, _) =>
            {
                CurrentTime = DateTime.Now.ToString("HH:mm");
                CurrentDate = DateTime.Now.ToString("dddd, dd MMM", new CultureInfo("es-ES"));
            };
            _clockTimer.Start();

            // El tema ya se resolvió en App.OnStartup (CLI > base de datos > appsettings).
            SelectedTheme = ThemeService.Current;
            ThemeService.ThemeChanged += OnThemeServiceChanged;

            // Navegación inicial para demos/capturas: --section=tasks|calendar|chat|doctools|settings
            var sectionArg = Environment.GetCommandLineArgs()
                .FirstOrDefault(a => a.StartsWith("--section=", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(sectionArg))
            {
                ApplyStartupSection(sectionArg.Substring("--section=".Length));
            }

            // Datos de demostración para capturas reproducibles: --demo
            if (Environment.GetCommandLineArgs()
                .Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase)))
            {
                Settings.LoadDemoDataCommand.Execute(null);
            }
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

        partial void OnSelectedThemeChanged(ThemeInfo value)
        {
            if (_syncingTheme) return;

            ThemeService.Apply(value.Id);

            _ = _settingsService.SetSettingAsync(
                SettingsService.KeyTheme,
                value.Id,
                "Tema de la aplicación");

            ToastService.Info(
                $"Tema {value.DisplayName} activado",
                "Tema actualizado");
        }

        private void UpdateFormattedSelectedDate()
        {
            FormattedSelectedDate = Calendar.SelectedDate
                .ToString("dd MMMM yyyy", new CultureInfo("es-ES"));
        }

        private void RebuildWeekDays()
        {
            var selected = Calendar.SelectedDate.Date;
            var offset = ((int)selected.DayOfWeek + 6) % 7; // lunes = 0
            var monday = selected.AddDays(-offset);

            WeekDays.Clear();
            for (var i = 0; i < 7; i++)
            {
                var date = monday.AddDays(i);
                WeekDays.Add(new WeekDayItem
                {
                    Date = date,
                    IsToday = date == DateTime.Today,
                    IsSelected = date == selected
                });
            }

            var sunday = monday.AddDays(6);
            WeekRangeLabel = $"{monday:dd MMM} — {sunday:dd MMM}";
        }

        [RelayCommand]
        public void SelectDate(DateTime date)
        {
            Calendar.SelectedDate = date;
        }

        [RelayCommand]
        public void ToggleTheme()
        {
            var themes = ThemeService.Themes;
            var index = 0;

            for (var i = 0; i < themes.Count; i++)
            {
                if (themes[i].Id == ThemeService.CurrentId)
                {
                    index = i;
                    break;
                }
            }

            SelectedTheme = themes[(index + 1) % themes.Count];
        }

        /// <summary>Navega a Tareas y abre el formulario de creación.</summary>
        [RelayCommand]
        public void NewTask()
        {
            NavigateToTasks();
            Tasks.ShowAddTaskCommand.Execute(null);
        }

        private void ApplyStartupSection(string section)
        {
            switch (section.Trim().ToLowerInvariant())
            {
                case "tasks":
                case "tareas":
                    NavigateToTasks();
                    break;
                case "calendar":
                case "calendario":
                    NavigateToCalendar();
                    break;
                case "notes":
                case "notas":
                case "code":
                    NavigateToNotes();
                    break;
                case "vault":
                case "boveda":
                case "keys":
                    NavigateToVault();
                    break;
                case "chat":
                case "ai":
                case "asistente":
                    NavigateToAi("Chat");
                    break;
                case "doctools":
                case "docs":
                case "doc":
                case "documentos":
                    NavigateToAi("Docs");
                    break;
                case "settings":
                case "config":
                    NavigateToSettings();
                    break;
                default:
                    NavigateToDashboard();
                    break;
            }
        }

        [RelayCommand]
        public void SetViewMode(string mode) => ActiveViewMode = mode;

        [RelayCommand]
        public void GoToToday() => Calendar.SelectedDate = DateTime.Today;

        [RelayCommand]
        public void PreviousDay() => Calendar.SelectedDate = Calendar.SelectedDate.AddDays(-1);

        [RelayCommand]
        public void NextDay() => Calendar.SelectedDate = Calendar.SelectedDate.AddDays(1);

        [RelayCommand]
        private void NavigateToDashboard()
        {
            CurrentView = Dashboard;
            ActiveSection = "Dashboard";
            CurrentSectionTitle = "🏠  Dashboard";
            Tasks.SelectedDateFilter = Calendar.SelectedDate;
            _ = Dashboard.LoadStatsAsync();
            _ = Tasks.RefreshAsync();
        }

        [RelayCommand]
        private void NavigateToTasks()
        {
            CurrentView = Tasks;
            ActiveSection = "Tasks";
            CurrentSectionTitle = "✅  Mis tareas";
            Tasks.SelectedDateFilter = null; // En Mis Tareas se ve todo el backlog
            _ = Tasks.RefreshAsync();
        }

        [RelayCommand]
        private void NavigateToCalendar()
        {
            CurrentView = Calendar;
            ActiveSection = "Calendar";
            CurrentSectionTitle = "📅  Calendario";
            _ = Calendar.LoadDayTasksAsync();
        }

        [RelayCommand]
        private void NavigateToNotes()
        {
            CurrentView = Notes;
            ActiveSection = "Notes";
            CurrentSectionTitle = "📝  Notas de código";
        }

        [RelayCommand]
        private void NavigateToVault()
        {
            CurrentView = Vault;
            ActiveSection = "Vault";
            CurrentSectionTitle = "🔐  Bóveda de claves";
        }

        /// <summary>Cambia entre los dos modos del workspace de IA.</summary>
        [RelayCommand]
        public void SetAiMode(string? mode)
        {
            if (string.IsNullOrWhiteSpace(mode)) return;

            AiMode = string.Equals(mode, "Docs", StringComparison.OrdinalIgnoreCase)
                ? "Docs"
                : "Chat";
        }

        /// <summary>Abre el workspace de IA en el modo indicado (o mantiene el actual).</summary>
        [RelayCommand]
        private void NavigateToAi(string? mode = null)
        {
            if (!string.IsNullOrWhiteSpace(mode))
                SetAiMode(mode);

            CurrentView = AiMode == "Docs" ? DocTools : AiChat;
            ActiveSection = "Ai";
            CurrentSectionTitle = "🤖  Asistente IA";

            if (AiMode == "Chat")
                _ = AiChat.CheckOllamaStatusAsync();
        }

        [RelayCommand]
        private void NavigateToSettings()
        {
            CurrentView = Settings;
            ActiveSection = "Settings";
            CurrentSectionTitle = "⚙️  Configuración";
            _ = Settings.LoadSettingsAsync();
        }
    }
}
