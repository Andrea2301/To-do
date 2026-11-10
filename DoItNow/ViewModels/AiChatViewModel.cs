using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoItNow.Models;
using DoItNow.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace DoItNow.ViewModels
{
    public partial class AiChatViewModel : ObservableObject
    {
        private readonly ChatService _chatService;
        private readonly OllamaService _ollamaService;
        private readonly SettingsService _settingsService;
        private ChatConversation? _currentConversation;

        public ObservableCollection<ChatMessage> Messages { get; } = new();

        [ObservableProperty] private string _inputMessage = string.Empty;
        [ObservableProperty] private bool _isGenerating;
        [ObservableProperty] private string _statusText = "Comprobando conexión con Ollama...";
        [ObservableProperty] private bool _isOllamaOnline;
        [ObservableProperty] private string _selectedModel = "llama3.2";

        public AiChatViewModel(ChatService? chatService = null, OllamaService? ollamaService = null, SettingsService? settingsService = null)
        {
            _settingsService = settingsService ?? new SettingsService();
            _ollamaService = ollamaService ?? new OllamaService(_settingsService);
            _chatService = chatService ?? new ChatService(_ollamaService);

            _ = InitializeAsync();
        }

        public async Task InitializeAsync()
        {
            await CheckOllamaStatusAsync();

            // Modo demo (capturas): garantiza una conversación de ejemplo atractiva.
            if (Environment.GetCommandLineArgs()
                .Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase)))
            {
                await _chatService.EnsureDemoConversationAsync();
            }

            _currentConversation = await _chatService.GetOrCreateActiveConversationAsync();

            Messages.Clear();
            foreach (var msg in _currentConversation.Messages)
            {
                Messages.Add(msg);
            }

            if (Messages.Count == 0)
            {
                Messages.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = "¡Hola! Soy tu asistente de productividad con Ollama. Puedo ayudarte a planificar tu día, estructurar tareas grandes en subtareas o sugerirte cómo organizar tus prioridades. ¿En qué te gustaría enfocarte hoy?",
                    Timestamp = DateTime.Now
                });
            }
        }

        [RelayCommand]
        public async Task CheckOllamaStatusAsync()
        {
            var (isReady, provider, badge) = await _ollamaService.GetActiveProviderStatusAsync();
            IsOllamaOnline = isReady;
            StatusText = badge;
        }

        [RelayCommand]
        public async Task SendMessageAsync()
        {
            var text = InputMessage?.Trim();
            if (string.IsNullOrWhiteSpace(text) || IsGenerating) return;

            InputMessage = string.Empty;

            // Asegurar conversación
            if (_currentConversation == null)
            {
                _currentConversation = await _chatService.GetOrCreateActiveConversationAsync();
            }

            // Añadir mensaje visual del usuario
            var userMsg = new ChatMessage
            {
                ConversationId = _currentConversation.Id,
                Role = "user",
                Content = text,
                Timestamp = DateTime.Now
            };
            Messages.Add(userMsg);

            IsGenerating = true;
            try
            {
                var assistantReply = await _chatService.SendUserMessageAsync(_currentConversation.Id, text);
                Messages.Add(assistantReply);
            }
            catch (Exception ex)
            {
                Messages.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = $"❌ Error al procesar: {ex.Message}",
                    Timestamp = DateTime.Now
                });
            }
            finally
            {
                IsGenerating = false;
            }
        }

        [RelayCommand]
        public async Task ClearChatAsync()
        {
            if (_currentConversation != null)
            {
                await _chatService.ClearConversationMessagesAsync(_currentConversation.Id);
            }
            Messages.Clear();
            Messages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = "Chat reiniciado. ¿En qué te puedo ayudar hoy?",
                Timestamp = DateTime.Now
            });
        }

        [RelayCommand]
        public void SendQuickPrompt(string prompt)
        {
            InputMessage = prompt;
            _ = SendMessageAsync();
        }
    }
}
