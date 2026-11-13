using DoItNow.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DoItNow.Services.Backup
{
    /// <summary>
    /// Destino de respaldos del vault. La implementación solo mueve bytes: el cifrado
    /// ocurre antes, en <see cref="BackupService"/>.
    /// </summary>
    public interface IBackupStore
    {
        Task<IReadOnlyList<VaultBackupFile>> ListAsync(CancellationToken cancellationToken = default);

        Task<VaultBackupFile> UploadAsync(
            string fileName, byte[] content, CancellationToken cancellationToken = default);

        Task<byte[]> DownloadAsync(string fileId, CancellationToken cancellationToken = default);

        Task DeleteAsync(string fileId, CancellationToken cancellationToken = default);
    }
}
