using DoItNow.Services;
using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace DoItNow
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ApplyStartupTheme(e.Args);

            // Evita que la app se cierre sola ante excepciones en el hilo principal / UI
            DispatcherUnhandledException += App_DispatcherUnhandledException;

            // Atrapa excepciones no controladas en el AppDomain
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // Atrapa excepciones no observadas en tareas en segundo plano (Tasks)
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        /// <summary>
        /// Aplica el tema antes de crear la ventana para evitar el "flash" de tema claro.
        /// Prioridad: argumento --theme > base de datos > appsettings.json.
        /// </summary>
        private static void ApplyStartupTheme(string[] args)
        {
            try
            {
                var forced = ThemeService.ReadThemeArgument(args);
                if (!string.IsNullOrWhiteSpace(forced))
                {
                    ThemeService.StartupOverride = forced;
                    ThemeService.ApplyStartupOverride();
                    return;
                }

                var stored = new SettingsService()
                    .GetSettingAsync(SettingsService.KeyTheme, ThemeService.DefaultId)
                    .GetAwaiter()
                    .GetResult();

                if (string.IsNullOrWhiteSpace(stored))
                {
                    stored = SettingsService.ReadSettingFromFile(SettingsService.KeyTheme) ?? ThemeService.DefaultId;
                }

                ThemeService.Apply(stored);
            }
            catch
            {
                // Si algo falla, el tema claro cargado por App.xaml sigue siendo válido.
            }
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogException("DispatcherUnhandledException", e.Exception);
            
            var msg = $"{e.Exception.Message}\n\n" +
                      $"Método: {e.Exception.TargetSite}\n\n" +
                      $"Pila de llamadas:\n{e.Exception.StackTrace}";

            MessageBox.Show(msg, "DoItNow - Error Detectado", MessageBoxButton.OK, MessageBoxImage.Warning);
            
            // Marca como manejado para evitar que el proceso finalice abruptamente
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogException("AppDomain.CurrentDomain.UnhandledException", ex);
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogException("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        }

        private static void LogException(string source, Exception ex)
        {
            try
            {
                var logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_error.txt");
                File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]\n{ex}\n----------------------------------------\n\n");
            }
            catch { }
        }
    }
}
