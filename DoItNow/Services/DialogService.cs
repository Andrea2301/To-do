using DoItNow.Views;
using System.Windows;

namespace DoItNow.Services
{
    /// <summary>Pequeña capa de diálogos para que los ViewModels no dependan de WPF directamente.</summary>
    public static class DialogService
    {
        /// <summary>
        /// Pide una contraseña. Devuelve null si el usuario cancela.
        /// </summary>
        public static string? AskPassword(
            string title,
            string message,
            bool confirm = false,
            bool requireMinLength = false)
        {
            var window = new PasswordPromptWindow(title, message, confirm, requireMinLength)
            {
                Owner = Application.Current?.MainWindow
            };

            return window.ShowDialog() == true ? window.Password : null;
        }

        public static bool Confirm(string title, string message)
        {
            var owner = Application.Current?.MainWindow;
            var result = owner != null
                ? MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning)
                : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning);

            return result == MessageBoxResult.Yes;
        }

        public static void Error(string title, string message)
        {
            var owner = Application.Current?.MainWindow;
            if (owner != null)
                MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
