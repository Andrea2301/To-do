using DoItNow.Services;
using DoItNow.ViewModels;
using System;
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

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            SourceInitialized += MainWindow_SourceInitialized;
            Loaded += MainWindow_Loaded;

            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.4) };
            _toastTimer.Tick += (_, _) => HideToast();

            ToastService.ToastRequested += ShowToast;
            Closed += (_, _) =>
            {
                ToastService.ToastRequested -= ShowToast;
            };
        }

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
        }

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