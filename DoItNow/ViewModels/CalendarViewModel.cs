using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace DoItNow.ViewModels
{
    /// <summary>
    /// Estado de fecha compartido por el Dashboard. La fecha seleccionada alimenta
    /// el mini calendario, la tira semanal, la agenda del día y el plan del día.
    /// </summary>
    public partial class CalendarViewModel : ObservableObject
    {
        [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    }
}
