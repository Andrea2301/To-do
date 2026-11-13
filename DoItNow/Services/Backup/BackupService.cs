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
    /// Orquesta el respaldo del vault cifrado: exporta a un archivo temporal, lo sube al
    /// destino (carpeta sincronizada o Drive) y conserva solo los últimos respaldos.
    /// El contenido sigue cifrado con la contraseña de exportación; el destino solo ve bytes.
    /// </summary>
    public sealed class BackupService
    {
        public const int MaxBackups = 5;

        private readonly IBackupStore _store;
        private readonly VaultService _vault;

        public BackupService(IBackupStore store, VaultService vault)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        }

        public static string BuildFileName(DateTime now) =>
            $"doitnow-vault-{now:yyyyMMdd-HHmmss}.ditvault";

        public async Task<VaultBackupFile> BackupAsync(
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

                var uploaded = await _store.UploadAsync(
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

            var content = await _store.DownloadAsync(fileId, cancellationToken);

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

        public Task<IReadOnlyList<VaultBackupFile>> ListAsync(
            CancellationToken cancellationToken = default) =>
            _store.ListAsync(cancellationToken);

        public Task DeleteAsync(string fileId, CancellationToken cancellationToken = default) =>
            _store.DeleteAsync(fileId, cancellationToken);

        /// <summary>Elimina los respaldos más antiguos y conserva los <see cref="MaxBackups"/> más recientes.</summary>
        public async Task TrimAsync(CancellationToken cancellationToken = default)
        {
            var files = await _store.ListAsync(cancellationToken);

            var expired = files
                .OrderByDescending(f => f.ModifiedAt)
                .Skip(MaxBackups)
                .ToList();

            foreach (var file in expired)
            {
                await _store.DeleteAsync(file.Id, cancellationToken);
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
