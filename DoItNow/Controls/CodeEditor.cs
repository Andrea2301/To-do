using DoItNow.Services;
using ICSharpCode.AvalonEdit;
using System;
using System.Windows;
using System.Windows.Media;

namespace DoItNow.Controls
{
    /// <summary>
    /// Editor de código basado en AvalonEdit con números de línea, resaltado de sintaxis
    /// y colores que siguen el tema activo (claro/oscuro) de la aplicación.
    /// </summary>
    public class CodeEditor : TextEditor
    {
        public static readonly DependencyProperty SyntaxLanguageProperty =
            DependencyProperty.Register(
                nameof(SyntaxLanguage),
                typeof(string),
                typeof(CodeEditor),
                new PropertyMetadata("Texto plano", OnLanguageChanged));

        public string SyntaxLanguage
        {
            get => (string)GetValue(SyntaxLanguageProperty);
            set => SetValue(SyntaxLanguageProperty, value);
        }

        /// <summary>
        /// Contenido enlazable. TextEditor.Text es una propiedad CLR normal, así que no se
        /// puede usar directamente en un Binding de WPF; esta DP sincroniza ambos sentidos.
        /// </summary>
        public static readonly DependencyProperty TextContentProperty =
            DependencyProperty.Register(
                nameof(TextContent),
                typeof(string),
                typeof(CodeEditor),
                new FrameworkPropertyMetadata(
                    string.Empty,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnTextContentChanged));

        public string TextContent
        {
            get => (string)GetValue(TextContentProperty);
            set => SetValue(TextContentProperty, value);
        }

        private bool _suppressTextSync;
        private bool _hooked;

        public CodeEditor()
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New");
            FontSize = 13;
            ShowLineNumbers = true;
            WordWrap = false;
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
            Padding = new Thickness(8, 6, 8, 6);

            Options.EnableHyperlinks = false;
            Options.EnableEmailHyperlinks = false;
            Options.HighlightCurrentLine = true;
            Options.IndentationSize = 4;
            Options.ConvertTabsToSpaces = true;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            TextChanged += OnTextChanged;
        }

        private static void OnLanguageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CodeEditor editor)
                editor.ApplyTheme();
        }

        private static void OnTextContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not CodeEditor editor) return;

            var incoming = e.NewValue as string ?? string.Empty;
            if (string.Equals(editor.Text, incoming, StringComparison.Ordinal)) return;

            editor._suppressTextSync = true;
            try
            {
                editor.Text = incoming;
            }
            finally
            {
                editor._suppressTextSync = false;
            }
        }

        private void OnTextChanged(object? sender, EventArgs e)
        {
            if (_suppressTextSync) return;

            var current = Text ?? string.Empty;
            if (!string.Equals(TextContent, current, StringComparison.Ordinal))
                SetCurrentValue(TextContentProperty, current);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!_hooked)
            {
                ThemeService.ThemeChanged += OnThemeChanged;
                _hooked = true;
            }

            ApplyTheme();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_hooked)
            {
                ThemeService.ThemeChanged -= OnThemeChanged;
                _hooked = false;
            }
        }

        private void OnThemeChanged(ThemeInfo theme) => ApplyTheme();

        private void ApplyTheme()
        {
            var dark = ThemeService.IsDark;

            Background = ThemeBrush("SurfaceInputBrush", dark ? "#262845" : "#F4F5FB");
            Foreground = ThemeBrush("TextPrimaryBrush", dark ? "#F2F3FF" : "#1C1D36");
            LineNumbersForeground = ThemeBrush("TextMutedBrush", dark ? "#9A9DC4" : "#8588AF");

            var accent = ThemeBrush("AccentBrush", "#CCF658");
            var accentColor = accent is SolidColorBrush solid ? solid.Color : Color.FromRgb(204, 246, 88);

            TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(dark ? (byte)90 : (byte)70, accentColor.R, accentColor.G, accentColor.B));
            TextArea.SelectionBorder = new Pen(new SolidColorBrush(Color.FromArgb(120, accentColor.R, accentColor.G, accentColor.B)), 1);
            TextArea.Caret.CaretBrush = Foreground;

            if (TextArea.TextView != null)
            {
                TextArea.TextView.CurrentLineBackground = new SolidColorBrush(
                    Color.FromArgb(dark ? (byte)26 : (byte)18, accentColor.R, accentColor.G, accentColor.B));
                TextArea.TextView.CurrentLineBorder = new Pen(Brushes.Transparent, 0);
            }

            SyntaxHighlighting = SyntaxHighlightingService.Get(SyntaxLanguage, dark);
        }

        private static Brush ThemeBrush(string key, string fallback)
        {
            if (Application.Current?.TryFindResource(key) is Brush brush)
                return brush;

            return (Brush)new BrushConverter().ConvertFromString(fallback)!;
        }
    }
}
