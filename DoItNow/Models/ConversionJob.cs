using CommunityToolkit.Mvvm.ComponentModel;
using DoItNow.Services.Conversion;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace DoItNow.Models
{
    /// <summary>Archivo en la cola del convertidor.</summary>
    public partial class ConversionJob : ObservableObject
    {
        public ConversionJob(string fullPath)
        {
            FullPath = fullPath;
            Kind = DocumentFormats.Detect(fullPath);
            ExtensionLabel = DocumentFormats.ExtensionLabel(fullPath);
            KindLabel = DocumentFormats.Label(Kind);
            SizeLabel = FormatSize(fullPath);
            Targets = new ObservableCollection<ConversionTarget>(DocumentFormats.Targets(Kind));
            SelectedTarget = Targets.FirstOrDefault();
        }

        public string FullPath { get; }
        public string FileName => Path.GetFileName(FullPath);
        public string ExtensionLabel { get; }
        public DocumentKind Kind { get; }
        public string KindLabel { get; }
        public string SizeLabel { get; }
        public ObservableCollection<ConversionTarget> Targets { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Subtitle))]
        private ConversionTarget? _selectedTarget;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Subtitle))]
        private string _status = "En cola";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Subtitle))]
        private bool _isBusy;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Subtitle))]
        private bool _isDone;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Subtitle))]
        private bool _isError;

        [ObservableProperty]
        private string? _outputPath;

        [ObservableProperty]
        private string _preview = string.Empty;

        public string Subtitle
        {
            get
            {
                if (IsBusy) return "Convirtiendo…";
                if (IsError) return Status;
                if (IsDone) return $"Listo · {Path.GetFileName(OutputPath)}";
                return $"{SizeLabel} · {KindLabel}";
            }
        }

        public bool IsNotBusy => !IsBusy;

        private static string FormatSize(string path)
        {
            try
            {
                var bytes = new FileInfo(path).Length;
                string[] units = { "B", "KB", "MB", "GB" };
                double size = bytes;
                var unit = 0;

                while (size >= 1024 && unit < units.Length - 1)
                {
                    size /= 1024;
                    unit++;
                }

                return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
            }
            catch
            {
                return "—";
            }
        }
    }
}
