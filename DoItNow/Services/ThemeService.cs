using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using FontAwesome.Sharp;

namespace DoItNow.Services
{
    /// <summary>
    /// Describe un tema disponible: su identificador estable, el nombre visible,
    /// el diccionario de recursos y los colores de muestra para el selector.
    /// </summary>
    public sealed class ThemeInfo
    {
        public ThemeInfo(
            string id,
            string displayName,
            string source,
            bool isDark,
            IconChar icon,
            string previewBackground,
            string previewSurface,
            string previewAccent,
            string previewText)
        {
            Id = id;
            DisplayName = displayName;
            Source = source;
            IsDark = isDark;
            Icon = icon;
            PreviewBackground = previewBackground;
            PreviewSurface = previewSurface;
            PreviewAccent = previewAccent;
            PreviewText = previewText;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Source { get; }
        public bool IsDark { get; }
        public IconChar Icon { get; }
        public string PreviewBackground { get; }
        public string PreviewSurface { get; }
        public string PreviewAccent { get; }
        public string PreviewText { get; }

        /// <summary>Nombres alternativos aceptados por CLI y valores antiguos guardados.</summary>
        public string[] Aliases { get; init; } = Array.Empty<string>();

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Catálogo y aplicación de temas. Cada tema es un ResourceDictionary que define
    /// las mismas claves semánticas; al cambiarlo, las vistas se actualizan solas
    /// porque consumen los brushes mediante DynamicResource.
    /// </summary>
    public static class ThemeService
    {
        private const string ThemeFolder = "Styles/Themes/";

        public const string DefaultId = "light";

        public static IReadOnlyList<ThemeInfo> Themes { get; } = new[]
        {
            new ThemeInfo(
                "light", "Claro", "Styles/Themes/LightTheme.xaml", false, IconChar.Sun,
                "#AAACD8", "#FFFFFF", "#CCF658", "#1C1D36")
            {
                Aliases = new[] { "Light", "Claro" }
            },
            new ThemeInfo(
                "dark", "Oscuro", "Styles/Themes/DarkTheme.xaml", true, IconChar.Moon,
                "#1D1E33", "#1F2036", "#CCF658", "#F2F3FF")
            {
                Aliases = new[] { "Dark", "Oscuro" }
            },
            new ThemeInfo(
                "sepia", "Sepia", "Styles/Themes/SepiaTheme.xaml", false, IconChar.Leaf,
                "#C9B18B", "#FFFDF7", "#E8B84B", "#2F2618")
            {
                Aliases = new[] { "Sepia", "Sépia", "Papel" }
            },
            new ThemeInfo(
                "midnight", "Medianoche", "Styles/Themes/MidnightTheme.xaml", true, IconChar.Star,
                "#0B1026", "#111A33", "#5EA8FF", "#EEF3FF")
            {
                Aliases = new[] { "Midnight", "Medianoche", "Noche" }
            },
            new ThemeInfo(
                "ocean", "Océano", "Styles/Themes/OceanTheme.xaml", true, IconChar.Water,
                "#062B2E", "#0E2F31", "#3CE0C4", "#E9FFFB")
            {
                Aliases = new[] { "Ocean", "Oceano", "Océano", "Mar" }
            },
            new ThemeInfo(
                "nord", "Nord", "Styles/Themes/NordTheme.xaml", true, IconChar.Snowflake,
                "#3B4252", "#2E3440", "#88C0D0", "#ECEFF4")
            {
                Aliases = new[] { "Nord", "Artico", "Ártico" }
            },
            new ThemeInfo(
                "rose", "Rosé", "Styles/Themes/RoseTheme.xaml", false, IconChar.Heart,
                "#F2C6D6", "#FFFCFD", "#FF7AA8", "#3A1F2A")
            {
                Aliases = new[] { "Rose", "Rosé", "Rosa" }
            },
            new ThemeInfo(
                "cinnamoroll", "Cinnamoroll", "Styles/Themes/CinnamorollTheme.xaml", false, IconChar.Cloud,
                "#8FC2EA", "#FFFFFF", "#6EC9FF", "#17324D")
            {
                Aliases = new[] { "Cinnamoroll", "Cinnamon", "Cinnamon Roll", "Nube", "Cloud", "Cielo", "Azulito" }
            },
            new ThemeInfo(
                "contrast", "Alto contraste", "Styles/Themes/HighContrastTheme.xaml", true, IconChar.Adjust,
                "#000000", "#000000", "#FFE500", "#FFFFFF")
            {
                Aliases = new[] { "Contrast", "HighContrast", "High Contrast", "Contraste", "Alto contraste" }
            }
        };

        /// <summary>Tema activo en este momento.</summary>
        public static ThemeInfo Current { get; private set; } = Themes[0];

        public static string CurrentId => Current.Id;

        public static bool IsDark => Current.IsDark;

        /// <summary>Fuerza un tema al arrancar (por ejemplo, desde --theme=ocean).</summary>
        public static string? StartupOverride { get; set; }

        public static event Action<ThemeInfo>? ThemeChanged;

        /// <summary>Busca un tema por id, nombre visible o alias.</summary>
        public static ThemeInfo? Find(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var normalized = Normalize(value);
            if (normalized.Length == 0) return null;

            return Themes.FirstOrDefault(t =>
                       Normalize(t.Id) == normalized ||
                       Normalize(t.DisplayName) == normalized ||
                       t.Aliases.Any(alias => Normalize(alias) == normalized));
        }

        /// <summary>Aplica un tema por id/nombre. Si no se reconoce, usa el predeterminado.</summary>
        public static ThemeInfo Apply(string? themeId)
        {
            var theme = Find(themeId) ?? Themes[0];
            ApplyResources(theme);
            return theme;
        }

        /// <summary>Aplica el tema forzado por argumentos, si existe.</summary>
        public static bool ApplyStartupOverride()
        {
            if (string.IsNullOrWhiteSpace(StartupOverride)) return false;

            Apply(StartupOverride);
            return true;
        }

        /// <summary>Lee --theme=&lt;id&gt; de los argumentos de línea de comandos.</summary>
        public static string? ReadThemeArgument(string[] args)
        {
            var arg = args.FirstOrDefault(a =>
                a.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(arg)) return null;

            return arg.Substring("--theme=".Length).Trim();
        }

        private static void ApplyResources(ThemeInfo theme)
        {
            Current = theme;

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

            var dictionary = new ResourceDictionary
            {
                Source = new Uri(theme.Source, UriKind.Relative)
            };

            // El tema debe cargarse antes que los estilos que consumen sus claves.
            merged.Insert(0, dictionary);
            ThemeChanged?.Invoke(theme);
        }

        /// <summary>
        /// Normaliza para comparar sin distinguir mayúsculas, espacios ni acentos.
        /// </summary>
        private static string Normalize(string value)
        {
            var buffer = new System.Text.StringBuilder(value.Length);

            foreach (var c in value.Trim().ToLowerInvariant())
            {
                if (char.IsWhiteSpace(c) || c == '-' || c == '_') continue;

                buffer.Append(c switch
                {
                    'á' or 'à' or 'ä' or 'â' => 'a',
                    'é' or 'è' or 'ë' or 'ê' => 'e',
                    'í' or 'ì' or 'ï' or 'î' => 'i',
                    'ó' or 'ò' or 'ö' or 'ô' => 'o',
                    'ú' or 'ù' or 'ü' or 'û' => 'u',
                    'ñ' => 'n',
                    _ => c
                });
            }

            return buffer.ToString();
        }
    }
}
