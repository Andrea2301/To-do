using DoItNow.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DoItNow.Views
{
    public partial class VaultView : UserControl
    {
        public VaultView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is VaultViewModel { IsLocked: true })
                UnlockPasswordBox.Focus();
        }

        private async void Unlock_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not VaultViewModel vm) return;

            var password = UnlockPasswordBox.Password;
            await vm.UnlockAsync(password);
            UnlockPasswordBox.Clear();

            if (vm.IsUnlocked)
                UnlockPasswordBox.Focus();
        }

        private void UnlockPassword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            e.Handled = true;
            Unlock_Click(sender, e);
        }
    }
}
