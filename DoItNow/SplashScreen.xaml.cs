using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace DoItNow
{
    /// <summary>
    /// Lógica de interacción para SplashScreen.xaml
    /// </summary>
    public partial class SplashScreen : Window
    {
        public SplashScreen()
        {
            InitializeComponent();
        }

        void Window_ContentRendered(object? sender, EventArgs e)
        {
            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerReportsProgress = true;
            worker.DoWork += worker_DoWork;
            worker.ProgressChanged += worker_ProgressChanged;
            worker.RunWorkerAsync();
        }

        void worker_ProgressChanged(object? sender, ProgressChangedEventArgs e)
        {
            ProgressBar.Value = e.ProgressPercentage;

            if (ProgressBar.Value == 70)
            {
                MainWindow mainWindow = new MainWindow();
                Close();  // Cierra la ventana actual (SplashScreen)
                mainWindow.ShowDialog();  // Muestra la nueva ventana (MainWindow)
            }
        }

        void worker_DoWork(object? sender, DoWorkEventArgs e)
        {
            for (int i = 0; i <= 100; i++)
            {
                (sender as BackgroundWorker)?.ReportProgress(i);
                Thread.Sleep(65);
            }
        }

        private void ProgressBar_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<double> e)
        {
        }
    }
}
