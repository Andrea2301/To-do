using DoItNow.Data;
using DoItNow.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DoItNow.Services
{
    public class ChatService
    {
        private readonly OllamaService _ollamaService;

        public ChatService(OllamaService? ollamaService = null)
        {
            try
            {
                using var context = new AppDbContext();
                context.InitializeDatabase();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatService] Error inicializando BD: {ex.Message}");
            }
            _ollamaService = ollamaService ?? new OllamaService();
        }

        public async Task<List<ChatConversation>> GetAllConversationsAsync()
        {
            using var context = new AppDbContext();
            return await context.ChatConversations
                .Include(c => c.Messages)
                .OrderByDescending(c => c.UpdatedAt)
                .ToListAsync();
        }

        public async Task<ChatConversation> GetOrCreateActiveConversationAsync()
        {
            using var context = new AppDbContext();
            var conversation = await context.ChatConversations
                .Include(c => c.Messages)
                .OrderByDescending(c => c.UpdatedAt)
                .FirstOrDefaultAsync();

            if (conversation == null)
            {
                conversation = new ChatConversation
                {
                    Title = "Chat con Asistente DoItNow",
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                context.ChatConversations.Add(conversation);
                await context.SaveChangesAsync();
            }

            return conversation;
        }

        /// <summary>
        /// Crea (una sola vez) una conversación de ejemplo para demos y capturas.
        /// No borra el historial real del usuario.
        /// </summary>
        public async Task EnsureDemoConversationAsync()
        {
            const string title = "Demo — Planificación con IA";

            using var context = new AppDbContext();
            var existing = await context.ChatConversations
                .Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.Title == title);

            if (existing != null)
            {
                // Mantener la demo como conversación más reciente para capturas reproducibles.
                existing.UpdatedAt = DateTime.Now;
                await context.SaveChangesAsync();
                return;
            }

            var now = DateTime.Now;
            var conversation = new ChatConversation
            {
                Title = title,
                CreatedAt = now.AddMinutes(-6),
                UpdatedAt = now
            };

            conversation.Messages.Add(new ChatMessage
            {
                Role = "user",
                Content = "Tengo un examen el viernes y un proyecto que entregar el lunes. ¿Cómo organizo mi semana?",
                Timestamp = now.AddMinutes(-5)
            });

            conversation.Messages.Add(new ChatMessage
            {
                Role = "assistant",
                Content =
                    "Claro, vamos a estructurarlo en bloques realistas:\n\n" +
                    "1. Hoy y mañana → examen del viernes. Dedica 2 sesiones de 90 min con repaso activo y un simulacro corto.\n" +
                    "2. Jueves → cierre del temario y descanso temprano.\n" +
                    "3. Sábado → arranque del proyecto: define alcance, divide en 3 hitos y reserva un bloque profundo.\n" +
                    "4. Domingo → revisión e integración; deja el lunes solo para el pulido final.\n\n" +
                    "Consejo: protege las mañanas para lo difícil y usa la tarde para tareas mecánicas. ¿Quieres que convierta esto en tareas con duración?",
                Timestamp = now.AddMinutes(-4)
            });

            context.ChatConversations.Add(conversation);
            await context.SaveChangesAsync();
        }

        public async Task<ChatMessage> SendUserMessageAsync(int conversationId, string userPrompt, string? systemPrompt = null)
        {
            using var context = new AppDbContext();
            var conversation = await context.ChatConversations
                .Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.Id == conversationId);

            if (conversation == null)
                throw new ArgumentException("Conversación no encontrada");

            // 1. Guardar mensaje del usuario
            var userMessage = new ChatMessage
            {
                ConversationId = conversationId,
                Role = "user",
                Content = userPrompt,
                Timestamp = DateTime.Now
            };
            context.ChatMessages.Add(userMessage);
            conversation.UpdatedAt = DateTime.Now;
            await context.SaveChangesAsync();

            // 2. Llamar a Ollama
            var assistantReplyText = await _ollamaService.SendChatMessageAsync(conversation.Messages, userPrompt, systemPrompt);

            // 3. Guardar respuesta del asistente
            var assistantMessage = new ChatMessage
            {
                ConversationId = conversationId,
                Role = "assistant",
                Content = assistantReplyText,
                Timestamp = DateTime.Now
            };
            context.ChatMessages.Add(assistantMessage);
            conversation.UpdatedAt = DateTime.Now;
            await context.SaveChangesAsync();

            return assistantMessage;
        }

        public async Task ClearConversationMessagesAsync(int conversationId)
        {
            using var context = new AppDbContext();
            var messages = context.ChatMessages.Where(m => m.ConversationId == conversationId);
            context.ChatMessages.RemoveRange(messages);
            await context.SaveChangesAsync();
        }
    }
}
