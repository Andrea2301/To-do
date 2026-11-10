using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoItNow.Models;
using DoItNow.Services;
using System.Collections.ObjectModel;

namespace DoItNow.ViewModels
{
    /// <summary>
    /// ViewModel del Calendario — muestra tareas por día seleccionado.
    /// </summary>
    public partial class CalendarViewModel : ObservableObject
    {
        private readonly TaskService _taskService;

        [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
        [ObservableProperty] private string _selectedDateLabel = string.Empty;

        public ObservableCollection<TaskItem> TasksForSelectedDay { get; } = new();

        public CalendarViewModel(TaskService taskService)
        {
            _taskService = taskService;
            UpdateDateLabel();
            _ = LoadDayTasksAsync();
        }

        partial void OnSelectedDateChanged(DateTime value)
        {
            UpdateDateLabel();
            _ = LoadDayTasksAsync();
        }

        private void UpdateDateLabel()
        {
            SelectedDateLabel = SelectedDate.ToString("dddd, dd 'de' MMMM yyyy",
                new System.Globalization.CultureInfo("es-ES"));
        }

        [RelayCommand]
        public async Task LoadDayTasksAsync()
        {
            var tasks = await _taskService.GetTasksByDateAsync(SelectedDate);
            TasksForSelectedDay.Clear();
            foreach (var t in tasks) TasksForSelectedDay.Add(t);
        }

        [RelayCommand]
        private void GoToToday()
        {
            SelectedDate = DateTime.Today;
        }
    }
}
