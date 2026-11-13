using DoItNow.Models;
using DoItNow.Services;
using DoItNow.Views;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace DoItNow
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly ProfileService _profileService = new();

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

            // --profile=<nombre> entra directo cuando el perfil no tiene contraseña
            // (útil para capturas y demos reproducibles). Si la tiene, se abre su
            // pantalla de desbloqueo ya seleccionada.
            var profileName = ReadProfileArgument(e.Args);
            if (!string.IsNullOrWhiteSpace(profileName))
            {
                var profile = _profileService.FindByName(profileName);
                if (profile != null && !profile.HasPassword)
                {
                    _profileService.MarkLogin(profile);
                    StartSession(profile, e.Args);
                    return;
                }

                ShowLogin(profile);
                return;
            }

            ShowLogin();
        }

        /// <summary>
        /// Aplica el tema antes de crear la primera ventana para evitar el "flash"
        /// de tema claro. Antes de iniciar sesión no se toca la base de datos: solo
        /// el argumento o appsettings.json (el tema guardado es por perfil).
        /// Prioridad: --theme &gt; appsettings.json &gt; tema claro.
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

                var stored = SettingsService.ReadSettingFromFile(SettingsService.KeyTheme);

                ThemeService.Apply(
                    string.IsNullOrWhiteSpace(stored) ? ThemeService.DefaultId : stored);
            }
            catch
            {
                // Si algo falla, el tema claro cargado por App.xaml sigue siendo válido.
            }
        }

        /// <summary>
        /// Aplica el tema guardado en la base del perfil recién abierto. Un
        /// <c>--theme=</c> explícito siempre manda sobre lo guardado.
        /// </summary>
        private static void ApplyProfileTheme(string[] args)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(ThemeService.ReadThemeArgument(args)))
                    return;

                var stored = new SettingsService()
                    .GetSettingAsync(SettingsService.KeyTheme, string.Empty)
                    .GetAwaiter()
                    .GetResult();

                if (!string.IsNullOrWhiteSpace(stored))
                    ThemeService.Apply(stored);
            }
            catch
            {
                // El tema resuelto al arrancar sigue siendo válido.
            }
        }

        /// <summary>
        /// Abre la pantalla de perfiles. Si el usuario la cierra sin entrar,
        /// la aplicación termina (ShutdownMode = OnLastWindowClose).
        /// </summary>
        public void ShowLogin(AppProfile? preferredProfile = null)
        {
            var login = new LoginWindow(preferredProfile);
            MainWindow = login;
            login.Show();
        }

        /// <summary>
        /// Fija la sesión activa, aplica el tema del perfil y abre la ventana
        /// principal. La llama <see cref="LoginWindow"/> tras verificar la contraseña.
        /// </summary>
        public void StartSession(AppProfile profile, string[]? args = null)
        {
            AppSession.SignIn(profile, _profileService.GetDatabasePath(profile));
            ApplyProfileTheme(args ?? Array.Empty<string>());

            var main = new MainWindow();
            MainWindow = main;
            main.Show();
        }

        /// <summary>
        /// Cierra la sesión actual y vuelve a la pantalla de perfiles. La ventana
        /// principal se cierra antes de limpiar la sesión para que guarde su
        /// geometría en la base del perfil (no en la ruta antigua).
        /// </summary>
        public void SignOut()
        {
            var previous = MainWindow;

            var login = new LoginWindow();
            MainWindow = login;
            login.Show();

            previous?.Close();
            AppSession.SignOut();
        }

        /// <summary>Lee --profile=&lt;nombre&gt; de los argumentos de línea de comandos.</summary>
        private static string? ReadProfileArgument(string[] args)
        {
            var arg = args.FirstOrDefault(a =>
                a.StartsWith("--profile=", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(arg)) return null;

            var value = arg.Substring("--profile=".Length).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
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
