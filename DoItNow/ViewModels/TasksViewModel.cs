using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoItNow.Models;
using DoItNow.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace DoItNow.ViewModels
{
    /// <summary>
    /// ViewModel interactivo para la gestión y filtrado en tiempo real de Tareas.
    /// </summary>
    public partial class TasksViewModel : ObservableObject
    {
        private readonly TaskService _taskService;

        // Notifica a MainViewModel / Dashboard cuando una tarea cambia para actualizar métricas en vivo
        public event Action? TaskChanged;

        // --- Colecciones ---
        public ObservableCollection<TaskItem> Tasks { get; } = new();
        public ObservableCollection<TaskItem> FilteredTasks { get; } = new();

        // --- Propiedades reactivas ---
        [ObservableProperty] private string _newTitle = string.Empty;
        [ObservableProperty] private string _newDescription = string.Empty;
        [ObservableProperty] private Priority _newPriority = Priority.High;
        [ObservableProperty] private TaskCategory _newCategory = TaskCategory.Health;
        [ObservableProperty] private DateTime _newDueDate = DateTime.Today;
        [ObservableProperty] private TaskItem? _selectedTask;
        [ObservableProperty] private bool _isEditing;
        [ObservableProperty] private bool _isFormVisible;
        [ObservableProperty] private bool _hasFilteredTasks;

        // Filtros reactivos
        [ObservableProperty] private string _searchText = string.Empty;
        [ObservableProperty] private TaskCategory? _selectedCategoryFilter;
        [ObservableProperty] private DateTime? _selectedDateFilter;

        /// <summary>Opción del selector de categorías (incluye "Todas").</summary>
        public sealed record CategoryFilterOption(string Label, TaskCategory? Value);

        public IReadOnlyList<CategoryFilterOption> CategoryFilterOptions { get; } = new[]
        {
            new CategoryFilterOption("Todas", null),
            new CategoryFilterOption("Trabajo", TaskCategory.Work),
            new CategoryFilterOption("Salud", TaskCategory.Health),
            new CategoryFilterOption("Estudio", TaskCategory.Study),
            new CategoryFilterOption("Personal", TaskCategory.Personal),
            new CategoryFilterOption("Otros", TaskCategory.Other)
        };

        [ObservableProperty] private CategoryFilterOption _selectedCategoryOption;

        private bool _syncingCategoryFilter;

        // Listas para los ComboBox
        public IEnumerable<Priority> Priorities => Enum.GetValues<Priority>();
        public IEnumerable<TaskCategory> Categories => Enum.GetValues<TaskCategory>();

        public TasksViewModel(TaskService taskService)
        {
            _taskService = taskService;
            _selectedCategoryOption = CategoryFilterOptions[0];
            _ = LoadTasksAsync();
        }

        partial void OnSearchTextChanged(string value) => ApplyFilter();
        partial void OnSelectedDateFilterChanged(DateTime? value) => ApplyFilter();

        partial void OnSelectedCategoryOptionChanged(CategoryFilterOption value)
        {
            if (_syncingCategoryFilter) return;

            _syncingCategoryFilter = true;
            SelectedCategoryFilter = value?.Value;
            _syncingCategoryFilter = false;
        }

        partial void OnSelectedCategoryFilterChanged(TaskCategory? value)
        {
            if (!_syncingCategoryFilter)
            {
                _syncingCategoryFilter = true;
                SelectedCategoryOption = CategoryFilterOptions
                    .FirstOrDefault(o => o.Value == value) ?? CategoryFilterOptions[0];
                _syncingCategoryFilter = false;
            }

            ApplyFilter();
        }

        public async Task LoadTasksAsync()
        {
            var tasks = await _taskService.GetAllTasksAsync();
            Tasks.Clear();
            foreach (var t in tasks) Tasks.Add(t);
            ApplyFilter();
        }

        public void ApplyFilter()
        {
            FilteredTasks.Clear();
            var query = Tasks.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var term = SearchText.Trim().ToLowerInvariant();
                query = query.Where(t => t.Title.ToLowerInvariant().Contains(term) ||
                                         t.Description.ToLowerInvariant().Contains(term));
            }

            if (SelectedCategoryFilter.HasValue)
            {
                query = query.Where(t => t.Category == SelectedCategoryFilter.Value);
            }

            if (SelectedDateFilter.HasValue)
            {
                query = query.Where(t => t.DueDate.Date == SelectedDateFilter.Value.Date);
            }

            foreach (var t in query)
            {
                FilteredTasks.Add(t);
            }

            HasFilteredTasks = FilteredTasks.Count > 0;
        }

        [RelayCommand]
        public void FilterByCategory(string categoryName)
        {
            if (string.Equals(categoryName, "All", StringComparison.OrdinalIgnoreCase))
            {
                SelectedCategoryFilter = null;
                return;
            }

            if (Enum.TryParse<TaskCategory>(categoryName, true, out var cat))
            {
                // Si hace clic en la misma categoría activa, quita el filtro (toggle)
                SelectedCategoryFilter = SelectedCategoryFilter == cat ? null : cat;
            }
        }

        [RelayCommand]
        private async Task AddTaskAsync()
        {
            if (string.IsNullOrWhiteSpace(NewTitle)) return;

            var task = new TaskItem
            {
                Title = NewTitle.Trim(),
                Description = NewDescription.Trim(),
                Priority = NewPriority,
                Category = NewCategory,
                DueDate = SelectedDateFilter ?? NewDueDate,
                CreatedAt = DateTime.Now
            };

            var added = await _taskService.AddTaskAsync(task);
            Tasks.Insert(0, added);
            ApplyFilter();
            ClearForm();
            IsFormVisible = false;

            ToastService.Success($"Tarea \"{added.Title}\" creada", "Nueva tarea");
            TaskChanged?.Invoke();
        }

        [RelayCommand]
        public async Task ToggleCompleteAsync(TaskItem task)
        {
            if (task == null) return;

            await _taskService.ToggleCompleteAsync(task.Id);
            task.IsCompleted = !task.IsCompleted;
            task.CompletedAt = task.IsCompleted ? DateTime.Now : null;

            ToastService.Success(
                task.IsCompleted ? $"¡Completaste \"{task.Title}\"! 🎉" : $"Reabriste \"{task.Title}\"",
                task.IsCompleted ? "Progreso" : "Tarea reabierta");

            ApplyFilter();
            TaskChanged?.Invoke();
        }

        [RelayCommand]
        public void EditTask(TaskItem task)
        {
            if (task == null) return;
            SelectedTask = task;
            NewTitle = task.Title;
            NewDescription = task.Description;
            NewPriority = task.Priority;
            NewCategory = task.Category;
            NewDueDate = task.DueDate;
            IsEditing = true;
            IsFormVisible = true;
        }

        [RelayCommand]
        public async Task SaveEditAsync()
        {
            if (SelectedTask == null || string.IsNullOrWhiteSpace(NewTitle)) return;

            SelectedTask.Title = NewTitle.Trim();
            SelectedTask.Description = NewDescription.Trim();
            SelectedTask.Priority = NewPriority;
            SelectedTask.Category = NewCategory;
            SelectedTask.DueDate = NewDueDate;

            var editedTitle = SelectedTask.Title;
            await _taskService.UpdateTaskAsync(SelectedTask);
            await LoadTasksAsync();
            ClearForm();
            IsFormVisible = false;

            ToastService.Success($"Cambios guardados en \"{editedTitle}\"", "Tarea actualizada");
            TaskChanged?.Invoke();
        }

        [RelayCommand]
        public async Task DeleteTaskAsync(TaskItem task)
        {
            if (task == null) return;

            var result = MessageBox.Show(
                $"¿Eliminar \"{task.Title}\"?",
                "Eliminar Tarea",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await _taskService.DeleteTaskAsync(task.Id);
                Tasks.Remove(task);
                ApplyFilter();
                ToastService.Info($"Tarea \"{task.Title}\" eliminada", "Eliminada");
                TaskChanged?.Invoke();
            }
        }

        [RelayCommand]
        public void ShowAddTask()
        {
            ClearForm();
            IsFormVisible = true;
        }

        [RelayCommand]
        public void CancelEdit()
        {
            ClearForm();
            IsFormVisible = false;
        }

        private void ClearForm()
        {
            NewTitle = string.Empty;
            NewDescription = string.Empty;
            NewPriority = Priority.High;
            NewCategory = TaskCategory.Health;
            NewDueDate = DateTime.Today;
            SelectedTask = null;
            IsEditing = false;
        }

        public async Task RefreshAsync() => await LoadTasksAsync();
    }
}
