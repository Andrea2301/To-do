using DoItNow.Models;

namespace DoItNow.Services
{
    /// <summary>
    /// Sesión activa del proceso. Guarda qué perfil está usando la app y qué base
    /// SQLite debe abrir <c>AppDbContext</c>. Se limpia al cerrar sesión.
    /// </summary>
    public static class AppSession
    {
        public static AppProfile? CurrentProfile { get; private set; }

        public static string? DatabasePath { get; private set; }

        public static bool IsSignedIn =>
            CurrentProfile != null && !string.IsNullOrWhiteSpace(DatabasePath);

        public static void SignIn(AppProfile profile, string databasePath)
        {
            CurrentProfile = profile;
            DatabasePath = databasePath;
        }

        public static void SignOut()
        {
            CurrentProfile = null;
            DatabasePath = null;
        }
    }
}
