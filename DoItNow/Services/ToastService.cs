using System;

namespace DoItNow.Services
{
    /// <summary>
    /// Bus de notificaciones ligeras (toasts) desacoplado de las vistas.
    /// Los ViewModels publican mensajes y MainWindow los anima.
    /// </summary>
    public static class ToastService
    {
        public static event Action<string, string>? ToastRequested;

        public static void Show(string message, string title = "DoItNow")
            => ToastRequested?.Invoke(title, message);

        public static void Success(string message, string title = "Listo")
            => ToastRequested?.Invoke(title, message);

        public static void Info(string message, string title = "DoItNow")
            => ToastRequested?.Invoke(title, message);
    }
}
