using DoItNow.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace DoItNow.Views
{
    /// <summary>
    /// Selecciona el DataTemplate correcto según el ViewModel activo.
    /// Permite que la vista central cambie dinámicamente según el módulo seleccionado.
    /// </summary>
    public class ViewTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? DashboardTemplate { get; set; }
        public DataTemplate? TasksTemplate { get; set; }
        public DataTemplate? CalendarTemplate { get; set; }
        public DataTemplate? AiChatTemplate { get; set; }
        public DataTemplate? DocToolsTemplate { get; set; }
        public DataTemplate? SettingsTemplate { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            return item switch
            {
                DashboardViewModel => DashboardTemplate,
                TasksViewModel => TasksTemplate,
                CalendarViewModel => CalendarTemplate,
                AiChatViewModel => AiChatTemplate,
                DocToolsViewModel => DocToolsTemplate,
                SettingsViewModel => SettingsTemplate,
                _ => base.SelectTemplate(item, container)
            };
        }
    }
}
