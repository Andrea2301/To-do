using System;
using System.Linq;
using System.Windows;

namespace DoItNow.Services
{
    /// <summary>
    /// Aplica el tema visual (claro/oscuro) intercambiando el diccionario de recursos
    /// de la aplicación. Todos los brushes semánticos viven en Styles/Themes.
    /// </summary>
    public static class ThemeService
    {
        private const string ThemeFolder = "Styles/Themes/";
        public const string LightSource = "Styles/Themes/LightTheme.xaml";
        public const string DarkSource = "Styles/Themes/DarkTheme.xaml";

        public static bool IsDark { get; private set; }

        /// <summary>Fuerza un tema al arrancar (por ejemplo, desde --theme=dark).</summary>
        public static bool? StartupOverride { get; set; }

        public static event Action<bool>? ThemeChanged;

        public static void Apply(bool dark)
        {
            IsDark = dark;

            var app = Application.Current;
            if (app == null) return;

            var merged = app.Resources.MergedDictionaries;

            // Quita cualquier tema previo.
            for (var i = merged.Count - 1; i >= 0; i--)
            {
                var source = merged[i].Source?.OriginalString ?? string.Empty;
                if (source.Contains(ThemeFolder, StringComparison.OrdinalIgnoreCase))
                {
                    merged.RemoveAt(i);
                }
            }

            var theme = new ResourceDictionary
            {
                Source = new Uri(dark ? DarkSource : LightSource, UriKind.Relative)
            };

            // El tema debe cargarse antes que los estilos que consumen sus claves.
            merged.Insert(0, theme);
            ThemeChanged?.Invoke(dark);
        }

        /// <summary>Lee --theme=dark|light de los argumentos de línea de comandos.</summary>
        public static bool? ReadThemeArgument(string[] args)
        {
            var arg = args.FirstOrDefault(a =>
                a.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(arg)) return null;

            var value = arg.Substring("--theme=".Length).Trim();
            return value.Equals("dark", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("oscuro", StringComparison.OrdinalIgnoreCase);
        }
    }
}
