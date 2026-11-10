using System;

namespace DoItNow.Models
{
    public class SubTaskItem
    {
        public int Id { get; set; }
        public int TaskId { get; set; }
        public TaskItem? Task { get; set; }

        public string Title { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public int OrderIndex { get; set; }
        public bool IsAiGenerated { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? CompletedAt { get; set; }
    }
}
