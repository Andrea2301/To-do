using DoItNow.Services;
using DoItNow.ViewModels;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DoItNow
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _toastTimer;
        private readonly SettingsService _settingsService;
        private bool _startMaximized;

        public MainWindow()
        {
            InitializeComponent();

            _settingsService = new SettingsService();
            RestorePlacement();

            DataContext = new MainViewModel();
            SourceInitialized += MainWindow_SourceInitialized;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;

            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.4) };
            _toastTimer.Tick += (_, _) => HideToast();

            ToastService.ToastRequested += ShowToast;
            Closed += (_, _) =>
            {
                ToastService.ToastRequested -= ShowToast;
            };
        }

        #region Window placement

        /// <summary>
        /// Limita el tamaño máximo al área de trabajo del monitor. El hook
        /// WM_GETMINMAXINFO ya hace lo mismo al maximizar, pero estos valores
        /// también evitan que la ventana restaurada supere la pantalla.
        /// </summary>
        private void ApplyWorkAreaLimits()
        {
            var work = SystemParameters.WorkArea;
            MaxWidth = Math.Max(900, work.Width);
            MaxHeight = Math.Max(600, work.Height);
        }

        /// <summary>
        /// Ajusta la ventana al área de trabajo del monitor: mantiene el tamaño de
        /// diseño cuando cabe y la reduce (con margen) en pantallas más pequeñas,
        /// para que nunca quede recortada por la barra de tareas ni por el borde.
        /// </summary>
        private void FitToWorkArea()
        {
            var work = SystemParameters.WorkArea;
            const double margin = 14;

            ApplyWorkAreaLimits();

            // Tamaño de diseño (1280x780) cuando hay espacio; si no, nos ajustamos
            // al área de trabajo para que la ventana nunca quede recortada.
            Width = Math.Clamp(work.Width - margin * 2, 900, 1280);
            Height = Math.Clamp(work.Height - margin * 2, 600, 780);

            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        /// <summary>
        /// Recupera el tamaño, la posición y el estado maximizado de la última
        /// sesión. Si no hay datos válidos (o quedaron fuera de pantalla), usa
        /// el tamaño de diseño centrado en el monitor actual.
        /// </summary>
        private void RestorePlacement()
        {
            ApplyWorkAreaLimits();

            string stored;
            try
            {
                stored = _settingsService
                    .GetSettingAsync(SettingsService.KeyWindowPlacement, string.Empty)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                FitToWorkArea();
                return;
            }

            if (!TryParsePlacement(stored, out var left, out var top,
                    out var width, out var height, out var maximized))
            {
                FitToWorkArea();
                return;
            }

            var work = SystemParameters.WorkArea;

            width = Math.Clamp(width, 900, Math.Max(900, work.Width));
            height = Math.Clamp(height, 600, Math.Max(600, work.Height));

            // Garantiza que siempre quede una franja visible en algún monitor,
            // incluso si la ventana se guardó en una pantalla ya desconectada.
            var virtualLeft = SystemParameters.VirtualScreenLeft;
            var virtualTop = SystemParameters.VirtualScreenTop;
            var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
            var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

            left = Math.Clamp(left, virtualLeft + 60 - width, virtualRight - 60);
            top = Math.Clamp(top, virtualTop, virtualBottom - 60);

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
            Width = width;
            Height = height;
            _startMaximized = maximized;
        }

        /// <summary>Formato invariante: left;top;width;height;maximized</summary>
        private static bool TryParsePlacement(string? raw, out double left, out double top,
            out double width, out double height, out bool maximized)
        {
            left = top = width = height = 0;
            maximized = false;

            if (string.IsNullOrWhiteSpace(raw))
                return false;

            var parts = raw.Split(';');
            if (parts.Length < 4)
                return false;

            var culture = CultureInfo.InvariantCulture;
            const NumberStyles style = NumberStyles.Float;

            if (!double.TryParse(parts[0], style, culture, out left) ||
                !double.TryParse(parts[1], style, culture, out top) ||
                !double.TryParse(parts[2], style, culture, out width) ||
                !double.TryParse(parts[3], style, culture, out height))
            {
                return false;
            }

            if (double.IsNaN(left) || double.IsNaN(top) ||
                double.IsNaN(width) || double.IsNaN(height) ||
                double.IsInfinity(left) || double.IsInfinity(top) ||
                double.IsInfinity(width) || double.IsInfinity(height) ||
                width < 400 || height < 300)
            {
                return false;
            }

            if (parts.Length > 4)
                bool.TryParse(parts[4], out maximized);

            return true;
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            SavePlacement();

            // Libera el reloj y la suscripción al tema del perfil que se cierra.
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.Dispose();
                DataContext = null;
            }
        }

        /// <summary>
        /// Guarda la geometría normal (la de RestoreBounds si está maximizada)
        /// de forma síncrona para que termine antes de que el proceso cierre.
        /// </summary>
        private void SavePlacement()
        {
            try
            {
                var bounds = WindowState == WindowState.Normal
                    ? new Rect(Left, Top, Width, Height)
                    : RestoreBounds;

                if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1)
                    return;

                var culture = CultureInfo.InvariantCulture;
                var value = string.Join(";",
                    bounds.Left.ToString("R", culture),
                    bounds.Top.ToString("R", culture),
                    bounds.Width.ToString("R", culture),
                    bounds.Height.ToString("R", culture),
                    (WindowState == WindowState.Maximized).ToString(culture));

                _settingsService
                    .SetSettingAsync(SettingsService.KeyWindowPlacement, value,
                        "Posición y tamaño de la ventana")
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                // El cierre de la aplicación nunca debe fallar por esto.
            }
        }

        #endregion

        #region Entrance animation

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Fade + scale suave al abrir la aplicación.
            Opacity = 0;

            var storyboard = new Storyboard();

            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fade, this);
            Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var scaleX = new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(360)) { EasingFunction = ease };
            var scaleY = new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(360)) { EasingFunction = ease };

            var transform = new System.Windows.Media.ScaleTransform(1, 1);
            RenderTransform = transform;
            RenderTransformOrigin = new Point(0.5, 0.5);

            Storyboard.SetTarget(scaleX, this);
            Storyboard.SetTargetProperty(scaleX,
                new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
            Storyboard.SetTarget(scaleY, this);
            Storyboard.SetTargetProperty(scaleY,
                new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));

            storyboard.Children.Add(fade);
            storyboard.Children.Add(scaleX);
            storyboard.Children.Add(scaleY);
            storyboard.Begin();

            if (_startMaximized)
            {
                _startMaximized = false;
                ToggleMaximize();
            }

            StartBootVeil();
        }

        #endregion

        #region Boot veil

        private DispatcherTimer? _bootVeilTimer;

        /// <summary>
        /// Velo temático de arranque: aparece al instante, anima tres puntos y se
        /// desvanece solo. No añade espera artificial a la carga real.
        /// </summary>
        private void StartBootVeil()
        {
            if (BootVeil == null)
                return;

            BootVeil.Visibility = Visibility.Visible;
            BootVeil.Opacity = 1;

            if (FindResource("BootDots") is Storyboard dots)
                dots.Begin(this, true);

            _bootVeilTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(520) };
            _bootVeilTimer.Tick += (_, _) =>
            {
                _bootVeilTimer?.Stop();
                HideBootVeil(false);
            };
            _bootVeilTimer.Start();
        }

        private void HideBootVeil(bool immediate)
        {
            _bootVeilTimer?.Stop();
            _bootVeilTimer = null;

            if (BootVeil == null || BootVeil.Visibility != Visibility.Visible)
                return;

            if (immediate || !IsVisible)
            {
                BootVeil.Visibility = Visibility.Collapsed;
                return;
            }

            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fade.Completed += (_, _) => BootVeil.Visibility = Visibility.Collapsed;
            BootVeil.BeginAnimation(OpacityProperty, fade);
        }

        private void BootVeil_MouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            HideBootVeil(true);
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e) => HideBootVeil(true);

        #endregion

        #region Toasts

        private void ShowToast(string title, string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => ShowToast(title, message));
                return;
            }

            ToastTitle.Text = title;
            ToastMessage.Text = message;
            ToastGlyph.Text = title.Contains("Tema", StringComparison.OrdinalIgnoreCase) ? "◐" : "✓";

            _toastTimer.Stop();
            ToastHost.Visibility = Visibility.Visible;
            ToastHost.BeginAnimation(OpacityProperty, null);

            var group = new System.Windows.Media.TransformGroup();
            var translate = new System.Windows.Media.TranslateTransform(0, 16);
            group.Children.Add(translate);
            ToastHost.RenderTransform = group;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var slideUp = new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            ToastHost.BeginAnimation(OpacityProperty, fadeIn);
            translate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slideUp);

            _toastTimer.Start();
        }

        private void HideToast()
        {
            _toastTimer.Stop();

            var fadeOut = new DoubleAnimation(ToastHost.Opacity, 0, TimeSpan.FromMilliseconds(180));
            fadeOut.Completed += (_, _) =>
            {
                ToastHost.Visibility = Visibility.Collapsed;
                ToastHost.BeginAnimation(OpacityProperty, null);
                ToastHost.Opacity = 0;
            };
            ToastHost.BeginAnimation(OpacityProperty, fadeOut);
        }

        private void ToastHost_Click(object sender, MouseButtonEventArgs e) => HideToast();

        #endregion

        #region Header actions

        private void BellButton_Click(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                var pending = vm.Dashboard.PendingCount;
                var overdue = vm.Dashboard.OverdueCount;

                var message = pending == 0
                    ? "Todo al día. Disfruta el momento. 🌿"
                    : $"Tienes {pending} tareas pendientes" +
                      (overdue > 0 ? $" y {overdue} vencidas." : ".");

                ToastService.Info(message, "Notificaciones");
            }
        }

        private void AvatarButton_Click(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.NavigateToSettingsCommand.Execute(null);
                ToastService.Info("Ajusta la IA, el tema y los datos de demostración.", "Configuración");
            }
        }

        #endregion

        #region Taskbar-Friendly Borderless Window Handling (Win32 Hook)

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        }

        // Intercepta WM_GETMINMAXINFO para que la ventana maximizada respete el área de trabajo
        // y NUNCA tape la barra de tareas de Windows.
        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_GETMINMAXINFO = 0x0024;
            if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            MINMAXINFO mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                MONITORINFO monitorInfo = new MONITORINFO();
                monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (GetMonitorInfo(monitor, ref monitorInfo))
                {
                    RECT rcWorkArea = monitorInfo.rcWork;
                    RECT rcMonitorArea = monitorInfo.rcMonitor;

                    mmi.ptMaxPosition.X = Math.Abs(rcWorkArea.Left - rcMonitorArea.Left);
                    mmi.ptMaxPosition.Y = Math.Abs(rcWorkArea.Top - rcMonitorArea.Top);
                    mmi.ptMaxSize.X = Math.Abs(rcWorkArea.Right - rcWorkArea.Left);
                    mmi.ptMaxSize.Y = Math.Abs(rcWorkArea.Bottom - rcWorkArea.Top);
                }
            }

            Marshal.StructureToPtr(mmi, lParam, true);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        #endregion

        #region Window Controls & TitleBar Interactions

        // Arrastrar ventana y doble clic para maximizar/restaurar
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }

            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Mantenemos compatibilidad con el evento original si fuera necesario
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
            => Application.Current.Shutdown();

        /// <summary>
        /// Cierra la sesión del perfil activo y vuelve a la pantalla de perfiles.
        /// Los datos quedan guardados en la base del perfil.
        /// </summary>
        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            var name = AppSession.CurrentProfile?.DisplayName ?? "tu perfil";

            var confirmed = DialogService.Confirm(
                "Cerrar sesión",
                $"¿Cerrar la sesión de «{name}»?\n\n" +
                "Tus datos se guardan y podrás volver a entrar desde la pantalla de perfiles.");

            if (!confirmed) return;

            if (Application.Current is App app)
                app.SignOut();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
            => ToggleMaximize();

        private void ToggleMaximize()
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                if (MaximizeIcon != null) MaximizeIcon.Text = "◻";
                if (MainBorder != null)
                {
                    MainBorder.CornerRadius = new CornerRadius(26);
                    MainBorder.Padding = new Thickness(16, 14, 18, 16);
                }
            }
            else
            {
                WindowState = WindowState.Maximized;
                if (MaximizeIcon != null) MaximizeIcon.Text = "🗗";
                if (MainBorder != null)
                {
                    MainBorder.CornerRadius = new CornerRadius(0);
                    MainBorder.Padding = new Thickness(12, 10, 12, 12);
                }
            }
        }

        #endregion
    }
}