using DoItNow.Data;
using DoItNow.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace DoItNow.Services
{
    public class SettingsService
    {
        public const string KeyOllamaUrl = "Ollama_Endpoint";
        public const string KeyOllamaModel = "Ollama_Model";
        public const string KeyGroqApiKey = "Groq_ApiKey";
        public const string KeyAiProvider = "Ai_Provider"; // "Auto", "Ollama", "Groq"
        public const string KeyTheme = "App_Theme";
        public const string KeyLastSection = "App_LastSection";
        public const string KeyWindowPlacement = "App_WindowPlacement";

        private static readonly string AppSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        public SettingsService()
        {
            try
            {
                using var context = new AppDbContext();
                context.InitializeDatabase();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsService] Error inicializando BD: {ex.Message}");
            }
        }

        public async Task<string> GetSettingAsync(string key, string defaultValue = "")
        {
            try
            {
                using var context = new AppDbContext();
                var setting = await context.Settings.FindAsync(key);
                
                // Si ya existe en la base de datos y no está vacío, usamos ese
                if (setting != null && !string.IsNullOrWhiteSpace(setting.Value))
                {
                    return setting.Value;
                }

                // Si la BD no lo tiene o está en blanco, leemos de appsettings.json
                var fileValue = ReadSettingFromFile(key);
                var effectiveValue = !string.IsNullOrWhiteSpace(fileValue) ? fileValue : (defaultValue ?? string.Empty);

                if (setting == null)
                {
                    context.Settings.Add(new AppSetting
                    {
                        Key = key,
                        Value = effectiveValue,
                        UpdatedAt = DateTime.Now
                    });
                }
                else
                {
                    setting.Value = effectiveValue;
                    setting.UpdatedAt = DateTime.Now;
                }

                await context.SaveChangesAsync();
                return effectiveValue;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsService] GetSettingAsync({key}) error: {ex.Message}");
                var fileVal = ReadSettingFromFile(key);
                return !string.IsNullOrWhiteSpace(fileVal) ? fileVal : defaultValue;
            }
        }

        public async Task SetSettingAsync(string key, string value, string? description = null)
        {
            try
            {
                using var context = new AppDbContext();
                var safeValue = value ?? string.Empty;
                var setting = await context.Settings.FindAsync(key);
                if (setting != null)
                {
                    setting.Value = safeValue;
                    setting.UpdatedAt = DateTime.Now;
                    if (!string.IsNullOrWhiteSpace(description))
                        setting.Description = description;
                }
                else
                {
                    context.Settings.Add(new AppSetting
                    {
                        Key = key,
                        Value = safeValue,
                        Description = description,
                        UpdatedAt = DateTime.Now
                    });
                }
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsService] SetSettingAsync({key}) error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Lee la configuración desde el archivo appsettings.json si está presente.
        /// </summary>
        public static string? ReadSettingFromFile(string key)
        {
            try
            {
                var candidatePaths = new[]
                {
                    AppSettingsPath,
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "appsettings.json")
                };

                foreach (var p in candidatePaths)
                {
                    if (File.Exists(p))
                    {
                        var json = File.ReadAllText(p);
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        if (key == KeyGroqApiKey && root.TryGetProperty("AiSettings", out var ai) && ai.TryGetProperty("GroqApiKey", out var groqKey))
                            return groqKey.GetString();

                        if (key == KeyOllamaUrl && root.TryGetProperty("AiSettings", out var ai2) && ai2.TryGetProperty("OllamaEndpoint", out var url))
                            return url.GetString();

                        if (key == KeyOllamaModel && root.TryGetProperty("AiSettings", out var ai3) && ai3.TryGetProperty("OllamaModel", out var model))
                            return model.GetString();

                        if (key == KeyAiProvider && root.TryGetProperty("AiSettings", out var ai4) && ai4.TryGetProperty("Provider", out var provider))
                            return provider.GetString();

                        if (key == KeyTheme && root.TryGetProperty("AppSettings", out var app) && app.TryGetProperty("Theme", out var theme))
                            return theme.GetString();
                    }
                }
            }
            catch { }

            return null;
        }

        public async Task<string> GetOllamaUrlAsync() =>
            await GetSettingAsync(KeyOllamaUrl, "http://localhost:11434");

        public async Task<string> GetOllamaModelAsync() =>
            await GetSettingAsync(KeyOllamaModel, "llama3.2");

        public async Task<string> GetGroqApiKeyAsync() =>
            await GetSettingAsync(KeyGroqApiKey, "");

        public async Task<string> GetAiProviderAsync() =>
            await GetSettingAsync(KeyAiProvider, "Auto");
    }
}
