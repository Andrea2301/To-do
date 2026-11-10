using DoItNow.Data;
using DoItNow.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DoItNow.Services
{
    /// <summary>
    /// Servicio que maneja toda la lógica de acceso a datos para tareas y subtareas.
    /// Patrón Repository — los ViewModels no conocen EF Core directamente.
    /// </summary>
    public class TaskService
    {
        private readonly AppDbContext _context;

        public TaskService()
        {
            _context = new AppDbContext();
            _context.InitializeDatabase();
            SeedInitialDataIfEmpty();
        }

        private void SeedInitialDataIfEmpty()
        {
            if (!_context.Tasks.Any())
            {
                var today = DateTime.Today;

                var task1 = new TaskItem
                {
                    Title = "Morning Run",
                    Description = "Entrenamiento cardiovascular matutino al aire libre",
                    Priority = Priority.High,
                    Category = TaskCategory.Health,
                    State = State.Active,
                    DueDate = today,
                    EstimatedMinutes = 30,
                    CreatedAt = DateTime.Now,
                    SubTasks = new List<SubTaskItem>
                    {
                        new() { Title = "Calentamiento articular (5 min)", IsCompleted = true },
                        new() { Title = "Carrera continúa 4km", IsCompleted = false },
                        new() { Title = "Estiramiento y vuelta a la calma", IsCompleted = false }
                    }
                };

                var task2 = new TaskItem
                {
                    Title = "Yoga & Meditación",
                    Description = "Sesión de flexibilidad, postura y respiración",
                    Priority = Priority.Medium,
                    Category = TaskCategory.Health,
                    State = State.Pending,
                    DueDate = today,
                    EstimatedMinutes = 25,
                    CreatedAt = DateTime.Now
                };

                var task3 = new TaskItem
                {
                    Title = "Revisión Proyecto DoItNow",
                    Description = "Refactorización, arquitectura de BD e integración con Ollama",
                    Priority = Priority.High,
                    Category = TaskCategory.Work,
                    State = State.Active,
                    DueDate = today,
                    EstimatedMinutes = 90,
                    CreatedAt = DateTime.Now,
                    SubTasks = new List<SubTaskItem>
                    {
                        new() { Title = "Diseñar modelos y tablas en SQLite", IsCompleted = true },
                        new() { Title = "Crear servicio cliente HTTP para Ollama", IsCompleted = false },
                        new() { Title = "Probar módulo de Chat y sugerencias", IsCompleted = false }
                    }
                };

                var task4 = new TaskItem
                {
                    Title = "Lectura de Documentación",
                    Description = "Estudiar prompts y endpoints de Ollama para C#",
                    Priority = Priority.Low,
                    Category = TaskCategory.Study,
                    State = State.Pending,
                    DueDate = today.AddDays(1),
                    EstimatedMinutes = 45,
                    CreatedAt = DateTime.Now
                };

                _context.Tasks.AddRange(task1, task2, task3, task4);
                _context.SaveChanges();
            }
        }

        public async Task<List<TaskItem>> GetAllTasksAsync()
        {
            return await _context.Tasks
                .Include(t => t.SubTasks)
                .OrderBy(t => t.IsCompleted)
                .ThenByDescending(t => t.Priority)
                .ThenBy(t => t.DueDate)
                .ToListAsync();
        }

        public async Task<List<TaskItem>> GetTasksByDateAsync(DateTime date)
        {
            return await _context.Tasks
                .Include(t => t.SubTasks)
                .Where(t => t.DueDate.Date == date.Date)
                .OrderBy(t => t.IsCompleted)
                .ThenByDescending(t => t.Priority)
                .ToListAsync();
        }

        public async Task<List<TaskItem>> GetPendingTasksAsync()
        {
            return await _context.Tasks
                .Include(t => t.SubTasks)
                .Where(t => !t.IsCompleted)
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.DueDate)
                .ToListAsync();
        }

        public async Task<List<TaskItem>> GetTasksByStateAsync(State state)
        {
            return await _context.Tasks
                .Include(t => t.SubTasks)
                .Where(t => t.State == state)
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.DueDate)
                .ToListAsync();
        }

        public async Task<TaskItem> AddTaskAsync(TaskItem task)
        {
            task.CreatedAt = DateTime.Now;
            _context.Tasks.Add(task);
            await _context.SaveChangesAsync();
            return task;
        }

        public async Task UpdateTaskAsync(TaskItem task)
        {
            task.UpdatedAt = DateTime.Now;
            _context.Tasks.Update(task);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteTaskAsync(int taskId)
        {
            var task = await _context.Tasks.FindAsync(taskId);
            if (task != null)
            {
                _context.Tasks.Remove(task);
                await _context.SaveChangesAsync();
            }
        }

        public async Task ToggleCompleteAsync(int taskId)
        {
            var task = await _context.Tasks.FindAsync(taskId);
            if (task != null)
            {
                task.IsCompleted = !task.IsCompleted;
                task.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateTaskStateAsync(int taskId, State newState)
        {
            var task = await _context.Tasks.FindAsync(taskId);
            if (task != null)
            {
                task.State = newState;
                task.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync();
            }
        }

        // --- Gestión de Subtareas ---
        public async Task<SubTaskItem?> AddSubTaskAsync(int taskId, string title, bool isAi = false)
        {
            var task = await _context.Tasks.Include(t => t.SubTasks).FirstOrDefaultAsync(t => t.Id == taskId);
            if (task == null) return null;

            var subTask = new SubTaskItem
            {
                TaskId = taskId,
                Title = title,
                IsAiGenerated = isAi,
                OrderIndex = task.SubTasks.Count,
                CreatedAt = DateTime.Now
            };

            _context.SubTasks.Add(subTask);
            await _context.SaveChangesAsync();
            return subTask;
        }

        public async Task ToggleSubTaskAsync(int subTaskId)
        {
            var subTask = await _context.SubTasks.FindAsync(subTaskId);
            if (subTask != null)
            {
                subTask.IsCompleted = !subTask.IsCompleted;
                subTask.CompletedAt = subTask.IsCompleted ? DateTime.Now : null;
                await _context.SaveChangesAsync();
            }
        }

        public async Task DeleteSubTaskAsync(int subTaskId)
        {
            var subTask = await _context.SubTasks.FindAsync(subTaskId);
            if (subTask != null)
            {
                _context.SubTasks.Remove(subTask);
                await _context.SaveChangesAsync();
            }
        }

        // --- Estadísticas para el Dashboard ---
        public async Task<int> GetCompletedTodayCountAsync()
        {
            return await _context.Tasks.CountAsync(
                t => t.IsCompleted && t.CompletedAt.HasValue &&
                     t.CompletedAt.Value.Date == DateTime.Today);
        }

        public async Task<int> GetPendingCountAsync()
        {
            return await _context.Tasks.CountAsync(t => !t.IsCompleted);
        }

        public async Task<int> GetActiveCountAsync()
        {
            return await _context.Tasks.CountAsync(t => t.State == State.Active);
        }

        public async Task<int> GetOverdueCountAsync()
        {
            return await _context.Tasks.CountAsync(
                t => !t.IsCompleted && t.DueDate.Date < DateTime.Today);
        }

        public async Task<int> GetTotalCountAsync()
        {
            return await _context.Tasks.CountAsync();
        }
    }
}
