using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoItNow.Models;
using DoItNow.Services;
using System.Collections.ObjectModel;

namespace DoItNow.ViewModels
{
    public partial class DashboardViewModel : ObservableObject
    {
        private readonly TaskService _taskService;

        [ObservableProperty] private int _completedToday;
        [ObservableProperty] private int _pendingCount;
        [ObservableProperty] private int _overdueCount;
        [ObservableProperty] private int _totalCount;
        [ObservableProperty] private double _completionPercentage;
        [ObservableProperty] private string _greeting = string.Empty;
        [ObservableProperty] private string _motivationText = string.Empty;

        public ObservableCollection<TaskItem> UpcomingTasks { get; } = new();
        public ObservableCollection<TaskItem> OverdueTasks { get; } = new();

        public DashboardViewModel(TaskService taskService)
        {
            _taskService = taskService;
            SetGreeting();
            _ = LoadStatsAsync();
        }

        private void SetGreeting()
        {
            var hour = DateTime.Now.Hour;
            Greeting = hour switch
            {
                < 12 => "¡Buenos días! 🌅",
                < 18 => "¡Buenas tardes! ☀️",
                _ => "¡Buenas noches! 🌙"
            };
        }

        private void SetMotivation(int completed, int pending)
        {
            MotivationText = (completed, pending) switch
            {
                ( > 0, 0) => "¡Increíble! Completaste todo 🎉",
                ( > 5, _) => "¡Gran avance hoy! Sigue así 💪",
                ( > 0, _) => $"{completed} completadas, ¡tú puedes! 🚀",
                (0, 0)    => "Sin tareas — ¡agrega tu primera! ✨",
                _         => $"{pending} tarea(s) esperando tu atención"
            };
        }

        [RelayCommand]
        public async Task LoadStatsAsync()
        {
            CompletedToday = await _taskService.GetCompletedTodayCountAsync();
            PendingCount   = await _taskService.GetPendingCountAsync();
            OverdueCount   = await _taskService.GetOverdueCountAsync();
            TotalCount     = await _taskService.GetTotalCountAsync();

            CompletionPercentage = TotalCount > 0
                ? Math.Round((double)CompletedToday / TotalCount * 100, 1)
                : 0;

            SetMotivation(CompletedToday, PendingCount);

            var pending = await _taskService.GetPendingTasksAsync();
            OverdueTasks.Clear();
            UpcomingTasks.Clear();
            foreach (var t in pending)
            {
                if (t.DueDate.Date < DateTime.Today)
                    OverdueTasks.Add(t);
                else if (UpcomingTasks.Count < 6)
                    UpcomingTasks.Add(t);
            }
        }
    }
}
