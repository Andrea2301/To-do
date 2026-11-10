using DoItNow.Models;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;

namespace DoItNow.Converters
{
    internal static class ThemeResources
    {
        /// <summary>
        /// Resuelve un brush semántico del tema activo. Si aún no existe (p. ej. en el
        /// diseñador de XAML), usa un color de respaldo para no romper el render.
        /// </summary>
        public static Brush Brush(string key, Color fallback)
        {
            if (Application.Current?.TryFindResource(key) is Brush brush)
                return brush;

            return new SolidColorBrush(fallback);
        }
    }

    /// <summary>Invierte un booleano — para mostrar/ocultar elementos.</summary>
    public class BoolToInverseVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Convierte bool a Visibility directa.</summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Mapea Priority a un color de acento.</summary>
    public class PriorityToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Priority p)
            {
                return p switch
                {
                    Priority.High => new SolidColorBrush(Color.FromRgb(239, 68, 68)),    // rojo
                    Priority.Medium => new SolidColorBrush(Color.FromRgb(234, 179, 8)),   // ámbar
                    Priority.Low => new SolidColorBrush(Color.FromRgb(34, 197, 94)),      // verde
                    _ => Brushes.Gray
                };
            }
            return Brushes.Gray;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Mapea TaskCategory a un emoji/icono de texto.</summary>
    public class CategoryToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is TaskCategory c ? c switch
            {
                TaskCategory.Work => "💼",
                TaskCategory.Health => "💪",
                TaskCategory.Study => "📚",
                TaskCategory.Personal => "👤",
                _ => "📌"
            } : "📌";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Mapea TaskCategory a su etiqueta en español.</summary>
    public class CategoryToLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is TaskCategory c ? c switch
            {
                TaskCategory.Work => "Trabajo",
                TaskCategory.Health => "Salud",
                TaskCategory.Study => "Estudio",
                TaskCategory.Personal => "Personal",
                _ => "Otros"
            } : string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Tachado de texto para tareas completadas.</summary>
    public class BoolToStrikethroughConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? TextDecorations.Strikethrough : null!;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Opacidad reducida para tareas completadas.</summary>
    public class BoolToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? 0.5 : 1.0;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Mapea Priority a su etiqueta en español.</summary>
    public class PriorityToLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Priority p ? p switch
            {
                Priority.High => "Alta",
                Priority.Medium => "Media",
                Priority.Low => "Baja",
                _ => p.ToString()
            } : value?.ToString() ?? "";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Compara el string de la sección activa para resaltar el botón del menú.</summary>
    public class StringEqualsToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value?.ToString() == parameter?.ToString();
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Muestra el elemento si el int es mayor que cero.</summary>
    public class IntToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is int i && i > 0 ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Muestra el elemento si el string NO está vacío.</summary>
    public class StringNotEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => !string.IsNullOrWhiteSpace(value?.ToString()) ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Fondo de tarjeta de tarea estilo TimeFrame (Lima vibrante, Púrpura pizarra, Lila pastel).</summary>
    public class PriorityToCardBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Priority p)
            {
                return p switch
                {
                    Priority.High => ThemeResources.Brush("TaskCardHighBrush", Color.FromRgb(204, 246, 88)),
                    Priority.Medium => ThemeResources.Brush("TaskCardMediumBrush", Color.FromRgb(136, 139, 183)),
                    Priority.Low => ThemeResources.Brush("TaskCardLowBrush", Color.FromRgb(228, 230, 248)),
                    _ => ThemeResources.Brush("TaskCardLowBrush", Color.FromRgb(228, 230, 248))
                };
            }
            return ThemeResources.Brush("TaskCardLowBrush", Color.FromRgb(228, 230, 248));
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Color de texto en tarjetas TimeFrame según el contraste del fondo.</summary>
    public class PriorityToCardForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Priority p
                ? p switch
                {
                    Priority.High => ThemeResources.Brush("TaskCardHighTextBrush", Color.FromRgb(28, 29, 54)),
                    Priority.Medium => ThemeResources.Brush("TaskCardMediumTextBrush", Colors.White),
                    Priority.Low => ThemeResources.Brush("TaskCardLowTextBrush", Color.FromRgb(28, 29, 54)),
                    _ => ThemeResources.Brush("TaskCardLowTextBrush", Color.FromRgb(28, 29, 54))
                }
                : ThemeResources.Brush("TaskCardLowTextBrush", Color.FromRgb(28, 29, 54));
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Alineación para mensajes de chat: user a la derecha, assistant a la izquierda.</summary>
    public class RoleToAlignmentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value?.ToString() == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Fondo de burbuja de chat.</summary>
    public class RoleToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value?.ToString() == "user"
                ? ThemeResources.Brush("PrimaryBrush", Color.FromRgb(32, 33, 58))
                : ThemeResources.Brush("SurfaceInputBrush", Color.FromRgb(244, 245, 251));
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Color de texto de burbuja de chat.</summary>
    public class RoleToForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value?.ToString() == "user"
                ? ThemeResources.Brush("TextOnDarkBrush", Colors.White)
                : ThemeResources.Brush("TextPrimaryBrush", Color.FromRgb(28, 29, 54));
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Color de badge según el estado de la tarea (State).</summary>
    public class StateToBadgeColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is State s)
            {
                return s switch
                {
                    State.Completed => ThemeResources.Brush("TaskCardHighBrush", Color.FromRgb(204, 246, 88)),
                    State.Active    => ThemeResources.Brush("TaskCardMediumBrush", Color.FromRgb(134, 137, 184)),
                    State.Pending   => ThemeResources.Brush("TaskCardLowBrush", Color.FromRgb(228, 230, 248)),
                    State.Overdue   => ThemeResources.Brush("DangerSoftBrush", Color.FromRgb(254, 202, 202)),
                    _ => Brushes.LightGray
                };
            }
            return Brushes.LightGray;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Compara una cadena con un parámetro y retorna Visible si coincide.</summary>
    public class StringEqualsToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value?.ToString() == parameter?.ToString() ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Compara una cadena con una lista separada por comas y retorna Visible si coincide con alguna.</summary>
    public class StringInListToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var str = value?.ToString();
            var list = parameter?.ToString()?.Split(new[] { ',' }, StringSplitOptions.TrimEntries);
            return (list != null && list.Contains(str)) ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Retorna un pincel activo (#20213A) si coincide con la sección activa, o Transparent si no.</summary>
    public class StringEqualsToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value?.ToString() == parameter?.ToString()
                ? ThemeResources.Brush("PrimaryBrush", Color.FromRgb(32, 33, 58))
                : Brushes.Transparent;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Indica si una categoría (o "All" para el filtro vacío) es la activa.</summary>
    internal static class CategoryFilterState
    {
        public static bool IsActive(object value, object parameter)
        {
            var filter = parameter?.ToString();
            if (string.Equals(filter, "All", StringComparison.OrdinalIgnoreCase))
                return value is null;

            return value is TaskCategory category
                && Enum.TryParse<TaskCategory>(filter, true, out var parsed)
                && category == parsed;
        }
    }

    /// <summary>Fondo de la pastilla de categoría según el filtro activo.</summary>
    public class CategoryFilterBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return CategoryFilterState.IsActive(value, parameter)
                ? ThemeResources.Brush("PrimaryBrush", Color.FromRgb(32, 33, 58))
                : ThemeResources.Brush("SurfaceHoverBrush", Color.FromRgb(232, 233, 244));
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Texto de la pastilla de categoría según el filtro activo.</summary>
    public class CategoryFilterForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return CategoryFilterState.IsActive(value, parameter)
                ? ThemeResources.Brush("TextOnDarkBrush", Colors.White)
                : ThemeResources.Brush("TextOnMutedBrush", Color.FromRgb(69, 100, 127));
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}


