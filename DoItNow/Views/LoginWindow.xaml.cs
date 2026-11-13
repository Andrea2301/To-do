using DoItNow.Models;
using DoItNow.Services;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DoItNow.Views
{
    /// <summary>
    /// Pantalla de entrada: lista los perfiles locales, pide contraseña cuando el
    /// perfil la tiene y permite crear o eliminar perfiles. No toca la base de
    /// datos del perfil: solo el registro de perfiles (JSON).
    /// </summary>
    public partial class LoginWindow : Window
    {
        private readonly ProfileService _profileService = new();
        private AppProfile? _selectedProfile;

        public LoginWindow(AppProfile? preferredProfile = null)
        {
            InitializeComponent();

            ReloadProfiles();

            if (preferredProfile is { HasPassword: true })
                ShowUnlock(preferredProfile);

            Loaded += (_, _) => FocusDefault();
        }

        #region Navegación entre vistas

        private void ReloadProfiles()
        {
            var profiles = _profileService.GetProfiles();
            ProfileList.ItemsSource = profiles;

            if (profiles.Count == 0)
            {
                ShowCreate(clearFields: false);
                ProfilesHint.Text = "Crea tu primer perfil para empezar.";
            }
            else
            {
                ProfilesHint.Text = string.Empty;
                ShowProfiles();
            }
        }

        private void ShowProfiles()
        {
            if (ProfileList.Items.Count == 0)
            {
                ShowCreate(clearFields: false);
                return;
            }

            _selectedProfile = null;

            ProfilesView.Visibility = Visibility.Visible;
            UnlockView.Visibility = Visibility.Collapsed;
            CreateView.Visibility = Visibility.Collapsed;
            CreateBackButton.Visibility = Visibility.Visible;

            UnlockPassword.Clear();
            UnlockError.Visibility = Visibility.Collapsed;
        }

        private void ShowUnlock(AppProfile profile)
        {
            _selectedProfile = profile;

            UnlockInitial.Text = profile.Initial;
            UnlockName.Text = profile.DisplayName;
            UnlockAccess.Text = profile.AccessLabel;
            UnlockPassword.Clear();
            UnlockError.Visibility = Visibility.Collapsed;

            ProfilesView.Visibility = Visibility.Collapsed;
            UnlockView.Visibility = Visibility.Visible;
            CreateView.Visibility = Visibility.Collapsed;

            UnlockPassword.Focus();
        }

        private void ShowCreate(bool clearFields)
        {
            if (clearFields)
            {
                NewName.Clear();
                NewPassword.Clear();
                ConfirmPassword.Clear();
            }

            CreateError.Visibility = Visibility.Collapsed;

            CreateBackButton.Visibility = ProfileList.Items.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            ProfilesView.Visibility = Visibility.Collapsed;
            UnlockView.Visibility = Visibility.Collapsed;
            CreateView.Visibility = Visibility.Visible;

            Dispatcher.BeginInvoke(new Action(() => NewName.Focus()));
        }

        private void FocusDefault()
        {
            if (CreateView.Visibility == Visibility.Visible)
                NewName.Focus();
            else if (UnlockView.Visibility == Visibility.Visible)
                UnlockPassword.Focus();
        }

        #endregion

        #region Acciones

        private void ProfileCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: AppProfile profile }) return;

            if (profile.HasPassword)
                ShowUnlock(profile);
            else
                SignIn(profile);
        }

        private void Unlock_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile is null) return;

            if (!_profileService.VerifyPassword(_selectedProfile, UnlockPassword.Password))
            {
                ShowUnlockMessage("Contraseña incorrecta. Inténtalo de nuevo.", isError: true);
                UnlockPassword.SelectAll();
                UnlockPassword.Focus();
                return;
            }

            SignIn(_selectedProfile);
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            CreateError.Visibility = Visibility.Collapsed;

            var name = NewName.Text.Trim();
            var password = NewPassword.Password;
            var confirmation = ConfirmPassword.Password;

            if (name.Length < 2)
            {
                ShowCreateError("El nombre debe tener al menos 2 caracteres.");
                NewName.Focus();
                return;
            }

            var hasPassword = password.Length > 0 || confirmation.Length > 0;

            if (hasPassword)
            {
                if (password.Length < ProfileService.MinPasswordLength)
                {
                    ShowCreateError(
                        $"La contraseña debe tener al menos {ProfileService.MinPasswordLength} caracteres.");
                    NewPassword.Focus();
                    return;
                }

                if (!string.Equals(password, confirmation, StringComparison.Ordinal))
                {
                    ShowCreateError("Las contraseñas no coinciden.");
                    ConfirmPassword.SelectAll();
                    ConfirmPassword.Focus();
                    return;
                }
            }

            try
            {
                var profile = _profileService.CreateProfile(
                    name, hasPassword ? password : null);

                SignIn(profile);
            }
            catch (Exception ex)
            {
                ShowCreateError(ex.Message);
            }
        }

        private void ChangePassword_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile is null) return;

            var password = DialogService.AskPassword(
                "Cambiar contraseña",
                $"Nueva contraseña para {_selectedProfile.DisplayName}.",
                confirm: true,
                requireMinLength: true);

            if (password is null) return;

            try
            {
                _profileService.ChangePassword(_selectedProfile, password);
                ShowUnlockMessage("Contraseña actualizada.", isError: false);
            }
            catch (Exception ex)
            {
                ShowUnlockMessage(ex.Message, isError: true);
            }
        }

        private void RemovePassword_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile is null) return;

            var name = _selectedProfile.DisplayName;

            var confirmed = DialogService.Confirm(
                "Quitar contraseña",
                $"¿Quitar la contraseña de «{name}»?\n\n" +
                "El perfil se abrirá directamente desde la lista, sin pedir contraseña.");

            if (!confirmed) return;

            try
            {
                _profileService.RemovePassword(_selectedProfile);
                _selectedProfile = null;
                ReloadProfiles();
                FocusDefault();
            }
            catch (Exception ex)
            {
                ShowUnlockMessage($"No se pudo quitar la contraseña: {ex.Message}", isError: true);
            }
        }

        private void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile is null) return;

            var name = _selectedProfile.DisplayName;

            var confirmed = DialogService.Confirm(
                "Eliminar perfil",
                $"¿Eliminar el perfil «{name}» y todos sus datos?\n\n" +
                "Se borrarán sus tareas, notas, historial de chat y bóveda. " +
                "Esta acción no se puede deshacer.");

            if (!confirmed) return;

            try
            {
                _profileService.DeleteProfile(_selectedProfile, deleteData: true);
                _selectedProfile = null;
                ReloadProfiles();
                FocusDefault();
            }
            catch (Exception ex)
            {
                ShowUnlockMessage($"No se pudo eliminar el perfil: {ex.Message}", isError: true);
            }
        }

        private void ShowProfiles_Click(object sender, RoutedEventArgs e) => ShowProfiles();

        private void ShowCreate_Click(object sender, RoutedEventArgs e) => ShowCreate(clearFields: true);

        private void SignIn(AppProfile profile)
        {
            _profileService.MarkLogin(profile);

            if (Application.Current is not App app) return;

            try
            {
                app.StartSession(profile);
                Close();
            }
            catch (Exception ex)
            {
                AppSession.SignOut();
                DialogService.Error(
                    "No se pudo abrir el perfil",
                    $"No se pudo preparar la sesión de «{profile.DisplayName}».\n\n{ex.Message}");
            }
        }

        #endregion

        #region Mensajes y ventana

        private void ShowUnlockMessage(string message, bool isError)
        {
            UnlockError.SetResourceReference(
                TextBlock.ForegroundProperty, isError ? "DangerTextBrush" : "TextSecondaryBrush");

            UnlockError.Text = message;
            UnlockError.Visibility = Visibility.Visible;
        }

        private void ShowCreateError(string message)
        {
            CreateError.Text = message;
            CreateError.Visibility = Visibility.Visible;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (ProfilesView.Visibility == Visibility.Visible)
                    Close();
                else
                    ShowProfiles();

                e.Handled = true;
                return;
            }

            if (e.Key != Key.Enter) return;

            if (CreateView.Visibility == Visibility.Visible)
            {
                Create_Click(sender, e);
                e.Handled = true;
            }
            else if (UnlockView.Visibility == Visibility.Visible)
            {
                Unlock_Click(sender, e);
                e.Handled = true;
            }
        }

        private void Brand_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        #endregion
    }
}
