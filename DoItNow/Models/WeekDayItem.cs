using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Globalization;

namespace DoItNow.Models
{
    /// <summary>
    /// Día mostrado en la tira semanal del dashboard.
    /// </summary>
    public partial class WeekDayItem : ObservableObject
    {
        private static readonly CultureInfo Spanish = new("es-ES");

        public DateTime Date { get; init; }
        public bool IsToday { get; init; }

        public string DayNumber => Date.ToString("dd", Spanish);
        public string DayName => Date.ToString("ddd", Spanish).TrimEnd('.').ToUpperInvariant();

        [ObservableProperty] private bool _isSelected;
    }
}
