using DoItNow.Services;
using System;
using System.Windows;
using System.Windows.Input;

namespace DoItNow.Views
{
    /// <summary>
    /// Diálogo modal para pedir una contraseña (desbloqueo de la bóveda, exportar/importar).
    /// </summary>
    public partial class PasswordPromptWindow : Window
    {
        private readonly bool _confirm;
        private readonly bool _requireMinLength;

        public string Password { get; private set; } = string.Empty;

        public PasswordPromptWindow(string title, string message, bool confirm, bool requireMinLength)
        {
            InitializeComponent();

            _confirm = confirm;
            _requireMinLength = requireMinLength;

            TitleText.Text = title;
            MessageText.Text = message;
            ConfirmPanel.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;

            Loaded += (_, _) => FirstPassword.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var password = FirstPassword.Password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(password))
            {
                ShowError("Escribe una contraseña.");
                return;
            }

            if (_requireMinLength && password.Length < 8)
            {
                ShowError("Usa al menos 8 caracteres para mayor seguridad.");
                return;
            }

            if (_confirm && !string.Equals(password, SecondPassword.Password, StringComparison.Ordinal))
            {
                ShowError("Las contraseñas no coinciden.");
                return;
            }

            Password = password;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Password_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Ok_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
