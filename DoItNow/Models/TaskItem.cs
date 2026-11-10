using System;
using System.Collections.Generic;

namespace DoItNow.Models
{
    public enum Priority { Low, Medium, High }
    public enum TaskCategory { Personal, Work, Health, Study, Other }
    public enum State { Active, Completed, Pending, Overdue }

    public class TaskItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        // Estado y completado sincronizados
        private bool _isCompleted;
        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                _isCompleted = value;
                if (value && State != State.Completed)
                {
                    State = State.Completed;
                    CompletedAt = DateTime.Now;
                }
                else if (!value && State == State.Completed)
                {
                    State = State.Pending;
                    CompletedAt = null;
                }
            }
        }

        private State _state = State.Pending;
        public State State
        {
            get => _state;
            set
            {
                _state = value;
                _isCompleted = (value == State.Completed);
                if (value == State.Completed && CompletedAt == null)
                {
                    CompletedAt = DateTime.Now;
                }
                else if (value != State.Completed)
                {
                    CompletedAt = null;
                }
            }
        }

        public Priority Priority { get; set; } = Priority.Medium;
        public TaskCategory Category { get; set; } = TaskCategory.Personal;
        public DateTime DueDate { get; set; } = DateTime.Today;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        // Métricas de tiempo y productividad
        public int? EstimatedMinutes { get; set; }
        public int? ActualMinutes { get; set; }

        public string DurationLabel => EstimatedMinutes.HasValue
            ? $"{EstimatedMinutes} min"
            : "Sin duración";

        // Enriquecimiento por IA (Ollama)
        public string? AiSummary { get; set; }

        // Subtareas / Checklist
        public List<SubTaskItem> SubTasks { get; set; } = new();
    }
}
