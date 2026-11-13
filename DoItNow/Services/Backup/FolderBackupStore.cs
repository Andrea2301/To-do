using DoItNow.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DoItNow.Services.Backup
{
    /// <summary>
    /// Respaldo en una carpeta local. Pensado para carpetas que ya sincroniza un cliente
    /// (OneDrive, Dropbox, Google Drive de escritorio): DoItNow solo escribe el
    /// <c>.ditvault</c> cifrado y el cliente lo sube por su cuenta.
    /// </summary>
    public sealed class FolderBackupStore : IBackupStore
    {
        private const string Extension = ".ditvault";

        public FolderBackupStore(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                throw new ArgumentException("La carpeta de respaldo es obligatoria.", nameof(folderPath));

            RootPath = Path.GetFullPath(folderPath.Trim());
        }

        public string RootPath { get; }

        public Task<IReadOnlyList<VaultBackupFile>> ListAsync(
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(RootPath);

            var files = new DirectoryInfo(RootPath)
                .EnumerateFiles("*" + Extension, SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => new VaultBackupFile
                {
                    Id = f.Name,
                    Name = f.Name,
                    SizeBytes = f.Length,
                    ModifiedAt = new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero)
                })
                .ToList();

            return Task.FromResult<IReadOnlyList<VaultBackupFile>>(files);
        }

        public async Task<VaultBackupFile> UploadAsync(
            string fileName, byte[] content, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(RootPath);

            var safeName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(safeName))
                throw new ArgumentException("El nombre del respaldo es obligatorio.", nameof(fileName));

            var path = EnsureUniquePath(Path.Combine(RootPath, safeName));

            await File.WriteAllBytesAsync(path, content, cancellationToken);

            var info = new FileInfo(path);
            return new VaultBackupFile
            {
                Id = info.Name,
                Name = info.Name,
                SizeBytes = info.Length,
                ModifiedAt = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)
            };
        }

        public async Task<byte[]> DownloadAsync(
            string fileId, CancellationToken cancellationToken = default) =>
            await File.ReadAllBytesAsync(ResolvePath(fileId), cancellationToken);

        public Task DeleteAsync(string fileId, CancellationToken cancellationToken = default)
        {
            var path = ResolvePath(fileId);
            if (File.Exists(path))
                File.Delete(path);

            return Task.CompletedTask;
        }

        private string ResolvePath(string fileId)
        {
            if (string.IsNullOrWhiteSpace(fileId))
                throw new ArgumentException("Falta el identificador del respaldo.", nameof(fileId));

            // Solo nombres simples dentro de la carpeta: un id manipulado no puede salir de ella.
            var name = Path.GetFileName(fileId);
            if (!string.Equals(name, fileId, StringComparison.Ordinal) ||
                !name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Identificador de respaldo no válido.", nameof(fileId));
            }

            return Path.Combine(RootPath, name);
        }

        private static string EnsureUniquePath(string path)
        {
            if (!File.Exists(path))
                return path;

            var directory = Path.GetDirectoryName(path)!;
            var name = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);

            for (var index = 1; index < 1000; index++)
            {
                var candidate = Path.Combine(directory, $"{name}-{index}{extension}");
                if (!File.Exists(candidate))
                    return candidate;
            }

            throw new InvalidOperationException("No se pudo generar un nombre único para el respaldo.");
        }
    }
}
