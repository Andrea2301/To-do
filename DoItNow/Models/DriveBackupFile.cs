using System;

namespace DoItNow.Models
{
    /// <summary>Archivo de respaldo del vault que vive en la carpeta oculta de Drive.</summary>
    public sealed class DriveBackupFile
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public long SizeBytes { get; init; }
        public DateTimeOffset ModifiedAt { get; init; }

        public string DisplayDate => ModifiedAt.ToLocalTime().ToString("dd MMM yyyy · HH:mm");
        public string DisplaySize => FormatSize(SizeBytes);

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
            return $"{bytes / (1024.0 * 1024.0):0.##} MB";
        }
    }
}
