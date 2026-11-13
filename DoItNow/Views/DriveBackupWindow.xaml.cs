using DoItNow.Services;
using DoItNow.Services.Drive;
using DoItNow.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace DoItNow.Views
{
    /// <summary>
    /// Ventana modal de respaldos en Google Drive. El cifrado del vault ocurre antes de
    /// subir el archivo, así que Drive nunca ve secretos en claro.
    /// </summary>
    public partial class DriveBackupWindow : Window
    {
        private readonly DriveBackupViewModel _viewModel;

        public bool VaultChanged { get; private set; }

        public DriveBackupWindow(VaultService vault)
        {
            InitializeComponent();

            _viewModel = new DriveBackupViewModel(new DriveBackupCoordinator(vault), vault);
            _viewModel.VaultRestored += () => VaultChanged = true;
            DataContext = _viewModel;

            Loaded += async (_, _) => await _viewModel.InitializeAsync();
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private async void SaveCredentials_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ClientSecret = SecretBox.Password;
            await _viewModel.SaveCredentialsAsync();

            if (_viewModel.HasCredentials)
                SecretBox.Clear();
        }

        private void CancelCredentials_Click(object sender, RoutedEventArgs e)
        {
            SecretBox.Clear();
            _viewModel.IsEditingCredentials = false;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }
}
