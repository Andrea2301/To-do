using System;
using System.Collections.Generic;

namespace DoItNow.Models
{
    public class ChatConversation
    {
        public int Id { get; set; }
        public string Title { get; set; } = "New Conversation";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public List<ChatMessage> Messages { get; set; } = new();
    }

    public class ChatMessage
    {
        public int Id { get; set; }
        public int ConversationId { get; set; }
        public ChatConversation? Conversation { get; set; }

        public string Role { get; set; } = "user"; // "user", "assistant", "system"
        public string Content { get; set; } = string.Empty;
        public string? ModelUsed { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
