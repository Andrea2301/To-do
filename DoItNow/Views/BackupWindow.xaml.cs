using DoItNow.Services;
using DoItNow.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace DoItNow.Views
{
    /// <summary>
    /// Ventana modal de respaldos del vault. El cifrado ocurre antes de guardar el archivo,
    /// así que la carpeta o Drive solo ven bytes.
    /// </summary>
    public partial class BackupWindow : Window
    {
        private readonly BackupViewModel _viewModel;

        public bool VaultChanged { get; private set; }

        public BackupWindow(VaultService vault)
        {
            InitializeComponent();

            _viewModel = new BackupViewModel(vault);
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
