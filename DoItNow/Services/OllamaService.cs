using DoItNow.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DoItNow.Services
{
    /// <summary>
    /// Servicio de Inteligencia Artificial Híbrido:
    /// Soporta inferencia local privada con Ollama y fallback automático/directo a Groq Cloud (ultra-rápido y gratuito).
    /// </summary>
    public class OllamaService
    {
        private readonly HttpClient _httpClient;
        private readonly SettingsService _settingsService;

        public const string GroqEndpoint = "https://api.groq.com/openai/v1/chat/completions";
        // Groq retiró llama-3.3-70b-versatile del tier gratuito el 16/08/2026;
        // openai/gpt-oss-120b es el reemplazo recomendado por Groq.
        public const string GroqDefaultModel = "openai/gpt-oss-120b";

        public OllamaService(SettingsService? settingsService = null)
        {
            _settingsService = settingsService ?? new SettingsService();
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        private async Task<string> GetOllamaBaseUrlAsync()
        {
            var url = await _settingsService.GetOllamaUrlAsync();
            return string.IsNullOrWhiteSpace(url) ? "http://localhost:11434" : url.TrimEnd('/');
        }

        private async Task<string> GetOllamaModelNameAsync()
        {
            var model = await _settingsService.GetOllamaModelAsync();
            return string.IsNullOrWhiteSpace(model) ? "llama3.2" : model;
        }

        /// <summary>
        /// Comprueba si Ollama está en ejecución localmente en la máquina.
        /// </summary>
        public async Task<bool> IsOllamaRunningAsync()
        {
            try
            {
                var baseUrl = await GetOllamaBaseUrlAsync();
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));
                var response = await _httpClient.GetAsync($"{baseUrl}/api/tags", cts.Token);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Retorna si el servicio de IA está disponible (sea por Ollama local o por Groq Cloud).
        /// </summary>
        public async Task<bool> IsConnectedAsync()
        {
            var (isReady, _, _) = await GetActiveProviderStatusAsync();
            return isReady;
        }

        /// <summary>
        /// Obtiene el estado y nombre del proveedor que se utilizará en este momento.
        /// </summary>
        public async Task<(bool isReady, string providerName, string statusBadge)> GetActiveProviderStatusAsync()
        {
            var providerPref = await _settingsService.GetAiProviderAsync();
            var groqKey = await _settingsService.GetGroqApiKeyAsync();
            var ollamaOnline = await IsOllamaRunningAsync();

            if (providerPref == "Groq")
            {
                if (!string.IsNullOrWhiteSpace(groqKey))
                    return (true, "Groq", $"⚡ Groq Cloud Activo ({GroqDefaultModel})");
                return (false, "Groq", "⚠️ Ingresa tu API Key de Groq en Configuración");
            }

            if (providerPref == "Ollama")
            {
                if (ollamaOnline)
                {
                    var model = await GetOllamaModelNameAsync();
                    return (true, "Ollama", $"🟢 Ollama Local ({model})");
                }
                return (false, "Ollama", "🔴 Ollama desconectado. Ejecuta 'ollama serve'");
            }

            // Modo "Auto" (Híbrido)
            if (ollamaOnline)
            {
                var model = await GetOllamaModelNameAsync();
                return (true, "Ollama", $"🟢 Ollama Local ({model})");
            }

            if (!string.IsNullOrWhiteSpace(groqKey))
            {
                return (true, "Groq", $"⚡ Groq Cloud Fallback ({GroqDefaultModel})");
            }

            return (false, "None", "ℹ️ Inicia Ollama o ingresa API Key en Configuración");
        }

        /// <summary>
        /// Obtiene la lista de modelos de Ollama descargados localmente.
        /// </summary>
        public async Task<List<string>> GetInstalledModelsAsync()
        {
            var result = new List<string>();
            try
            {
                var baseUrl = await GetOllamaBaseUrlAsync();
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3));
                var response = await _httpClient.GetAsync($"{baseUrl}/api/tags", cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("models", out var modelsElement))
                    {
                        foreach (var m in modelsElement.EnumerateArray())
                        {
                            if (m.TryGetProperty("name", out var nameElement))
                            {
                                var name = nameElement.GetString();
                                if (!string.IsNullOrWhiteSpace(name))
                                    result.Add(name);
                            }
                        }
                    }
                }
            }
            catch { }

            return result;
        }

        /// <summary>
        /// Envía un mensaje a la IA (usando Ollama o Groq según la disponibilidad).
        /// </summary>
        public async Task<string> SendChatMessageAsync(IEnumerable<ChatMessage> history, string userMessage, string? systemPrompt = null)
        {
            var (isReady, provider, _) = await GetActiveProviderStatusAsync();

            if (!isReady)
            {
                return "⚠️ No se detectó conexión con la IA.\n\n" +
                       "• Opción 1: Ejecuta 'ollama serve' en tu PC si tienes Ollama instalado.\n" +
                       "• Opción 2: Ve a 'Configuración' e ingresa una clave gratuita de Groq (console.groq.com) para usar la IA en la nube sin instalar nada.";
            }

            if (provider == "Ollama")
            {
                return await SendViaOllamaAsync(history, userMessage, systemPrompt);
            }
            else
            {
                return await SendViaGroqAsync(history, userMessage, systemPrompt);
            }
        }

        private async Task<string> SendViaOllamaAsync(IEnumerable<ChatMessage> history, string userMessage, string? systemPrompt)
        {
            var baseUrl = await GetOllamaBaseUrlAsync();
            var model = await GetOllamaModelNameAsync();

            var messagesPayload = new List<object>
            {
                new
                {
                    role = "system",
                    content = systemPrompt ?? "Eres el asistente inteligente de productividad de DoItNow. Ayudas a organizar tareas, priorizar actividades y ser más eficiente de forma concisa y motivadora."
                }
            };

            foreach (var msg in history)
            {
                messagesPayload.Add(new { role = msg.Role, content = msg.Content });
            }
            messagesPayload.Add(new { role = "user", content = userMessage });

            var requestBody = new { model, messages = messagesPayload, stream = false };
            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync($"{baseUrl}/api/chat", jsonContent);
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseJson);
                    if (doc.RootElement.TryGetProperty("message", out var msgElement) &&
                        msgElement.TryGetProperty("content", out var contentElement))
                    {
                        return contentElement.GetString() ?? string.Empty;
                    }
                }
                return $"Error en Ollama (código {response.StatusCode}): {response.ReasonPhrase}";
            }
            catch (Exception ex)
            {
                return $"Error de conexión con Ollama: {ex.Message}";
            }
        }

        private async Task<string> SendViaGroqAsync(IEnumerable<ChatMessage> history, string userMessage, string? systemPrompt)
        {
            var apiKey = await _settingsService.GetGroqApiKeyAsync();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return "⚠️ Por favor ingresa tu API Key gratuita de Groq en Configuración.";
            }

            var messagesPayload = new List<object>
            {
                new
                {
                    role = "system",
                    content = systemPrompt ?? "Eres el asistente inteligente de productividad de DoItNow. Eres breve, directo, amable y ayudas al usuario a gestionar sus tareas."
                }
            };

            foreach (var msg in history)
            {
                messagesPayload.Add(new { role = msg.Role, content = msg.Content });
            }
            messagesPayload.Add(new { role = "user", content = userMessage });

            var requestBody = new
            {
                model = GroqDefaultModel,
                messages = messagesPayload,
                temperature = 0.7
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, GroqEndpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            req.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.SendAsync(req);
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseJson);
                    if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                        choices.GetArrayLength() > 0 &&
                        choices[0].TryGetProperty("message", out var msg) &&
                        msg.TryGetProperty("content", out var content))
                    {
                        return content.GetString() ?? string.Empty;
                    }
                }
                var errorBody = await response.Content.ReadAsStringAsync();
                return DescribeGroqFailure(response.StatusCode, errorBody);
            }
            catch (TaskCanceledException)
            {
                return "⌛ Groq tardó demasiado en responder. Inténtalo de nuevo.";
            }
            catch (Exception ex)
            {
                return $"🌐 No pude conectar con Groq Cloud: {ex.Message}";
            }
        }

        /// <summary>Traduce un fallo de Groq a un mensaje breve y accionable.</summary>
        private static string DescribeGroqFailure(HttpStatusCode status, string body)
        {
            var detail = ExtractGroqErrorMessage(body);

            return status switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    "🔑 Tu API Key de Groq no es válida o no tiene permisos. Revísala en Configuración.",
                HttpStatusCode.TooManyRequests =>
                    "⏳ Groq está limitando las solicitudes. Espera unos segundos e inténtalo de nuevo.",
                HttpStatusCode.NotFound =>
                    "🧩 El modelo de Groq configurado no está disponible. Revisa los modelos activos en console.groq.com/docs/models.",
                HttpStatusCode.BadRequest when detail.Contains("model", StringComparison.OrdinalIgnoreCase) =>
                    "🧩 Groq no reconoce el modelo solicitado. Revisa los modelos activos en console.groq.com/docs/models.",
                _ => string.IsNullOrWhiteSpace(detail)
                    ? $"⚠️ Groq no pudo completar la solicitud ({(int)status})."
                    : $"⚠️ Groq respondió con un error ({(int)status}): {detail}"
            };
        }

        /// <summary>Extrae el mensaje legible del JSON de error de Groq, si existe.</summary>
        private static string ExtractGroqErrorMessage(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var error) &&
                    error.TryGetProperty("message", out var message))
                    return message.GetString() ?? string.Empty;
            }
            catch (JsonException)
            {
                // El cuerpo no era JSON: se ignora y se usa el mensaje genérico.
            }

            return string.Empty;
        }

        /// <summary>
        /// Pide a la IA desglosar una tarea en una lista de subtareas accionables.
        /// </summary>
        public async Task<List<string>> BreakdownTaskAsync(string taskTitle, string taskDescription)
        {
            var prompt = $"Desglosa la siguiente tarea en entre 3 y 5 subtareas o pasos breves y concretos. " +
                         $"Responde ÚNICAMENTE con los pasos, uno por línea comenzando con un guion '-', sin introducciones ni despedidas.\n\n" +
                         $"Tarea: {taskTitle}\nDescripción: {taskDescription}";

            var reply = await SendChatMessageAsync(new List<ChatMessage>(), prompt);
            var steps = new List<string>();

            if (!string.IsNullOrWhiteSpace(reply))
            {
                var lines = reply.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var cleaned = line.Trim().TrimStart('-', '*', '•', '1', '2', '3', '4', '5', '.', ' ');
                    if (!string.IsNullOrWhiteSpace(cleaned) && !cleaned.StartsWith("Error") && !cleaned.StartsWith("⚠️"))
                        steps.Add(cleaned);
                }
            }

            return steps;
        }
    }
}
