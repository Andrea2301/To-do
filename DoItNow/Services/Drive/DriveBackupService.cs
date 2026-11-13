using DoItNow.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DoItNow.Services.Drive
{
    /// <summary>
    /// Orquesta el respaldo del vault cifrado en Google Drive: exporta a un archivo temporal,
    /// lo sube a la carpeta oculta de la app y conserva solo los últimos respaldos.
    /// El contenido sigue cifrado con la contraseña de exportación; Drive solo ve bytes.
    /// </summary>
    public sealed class DriveBackupService
    {
        public const int MaxBackups = 5;

        private readonly IDriveBackupClient _client;
        private readonly VaultService _vault;

        public DriveBackupService(IDriveBackupClient client, VaultService vault)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        }

        public static string BuildFileName(DateTime now) =>
            $"doitnow-vault-{now:yyyyMMdd-HHmmss}.ditvault";

        public async Task<DriveBackupFile> BackupAsync(
            string password,
            DateTime now,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("La contraseña del respaldo es obligatoria.", nameof(password));

            var tempPath = Path.Combine(
                Path.GetTempPath(), $"doitnow-backup-{Guid.NewGuid():N}.ditvault");

            try
            {
                await _vault.ExportAsync(tempPath, password);
                var content = await File.ReadAllBytesAsync(tempPath, cancellationToken);

                var uploaded = await _client.UploadAsync(
                    BuildFileName(now), content, cancellationToken);

                await TrimAsync(cancellationToken);
                return uploaded;
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        /// <summary>Descarga un respaldo y lo importa en la bóveda abierta. Devuelve las entradas importadas.</summary>
        public async Task<int> RestoreAsync(
            string fileId,
            string password,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("La contraseña del respaldo es obligatoria.", nameof(password));

            var content = await _client.DownloadAsync(fileId, cancellationToken);

            var tempPath = Path.Combine(
                Path.GetTempPath(), $"doitnow-restore-{Guid.NewGuid():N}.ditvault");

            try
            {
                await File.WriteAllBytesAsync(tempPath, content, cancellationToken);
                return await _vault.ImportAsync(tempPath, password);
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        public Task<IReadOnlyList<DriveBackupFile>> ListAsync(
            CancellationToken cancellationToken = default) =>
            _client.ListAsync(cancellationToken);

        public Task DeleteAsync(string fileId, CancellationToken cancellationToken = default) =>
            _client.DeleteAsync(fileId, cancellationToken);

        /// <summary>Elimina los respaldos más antiguos y conserva los <see cref="MaxBackups"/> más recientes.</summary>
        public async Task TrimAsync(CancellationToken cancellationToken = default)
        {
            var files = await _client.ListAsync(cancellationToken);

            var expired = files
                .OrderByDescending(f => f.ModifiedAt)
                .Skip(MaxBackups)
                .ToList();

            foreach (var file in expired)
            {
                await _client.DeleteAsync(file.Id, cancellationToken);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Un temporal que no se puede borrar no debe romper el respaldo.
            }
        }
    }
}
