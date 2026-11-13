using DoItNow.Views;
using System.Linq;
using System.Windows;

namespace DoItNow.Services
{
    /// <summary>Pequeña capa de diálogos para que los ViewModels no dependan de WPF directamente.</summary>
    public static class DialogService
    {
        /// <summary>
        /// Dueño de los diálogos: la ventana activa (por ejemplo, una ventana modal abierta
        /// sobre la principal) o, si no hay, la ventana principal.
        /// </summary>
        private static Window? ActiveOwner() =>
            Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsActive)
            ?? Application.Current?.MainWindow;

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
                Owner = ActiveOwner()
            };

            return window.ShowDialog() == true ? window.Password : null;
        }

        public static bool Confirm(string title, string message)
        {
            var owner = ActiveOwner();
            var result = owner != null
                ? MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning)
                : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning);

            return result == MessageBoxResult.Yes;
        }

        public static void Error(string title, string message)
        {
            var owner = ActiveOwner();
            if (owner != null)
                MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>Abre la ventana de respaldos en Google Drive. Devuelve true si se restauró algo.</summary>
        public static bool ShowDriveBackup(VaultService vault)
        {
            var window = new DriveBackupWindow(vault)
            {
                Owner = Application.Current?.MainWindow
            };

            window.ShowDialog();
            return window.VaultChanged;
        }
    }
}
