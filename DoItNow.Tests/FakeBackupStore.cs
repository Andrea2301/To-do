using DoItNow.Models;
using DoItNow.Services.Backup;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DoItNow.Tests;

/// <summary>Almacén de respaldos en memoria: guarda los archivos subidos y simula un destino remoto.</summary>
internal sealed class FakeBackupStore : IBackupStore
{
    private readonly Dictionary<string, byte[]> _content = new();

    public Dictionary<string, VaultBackupFile> Files { get; } = new();
    public List<string> Deleted { get; } = new();
    public List<string> UploadedNames { get; } = new();
    public byte[]? LastUploadedContent { get; private set; }

    public FakeBackupStore Seed(string id, string name, byte[] content, DateTimeOffset modifiedAt)
    {
        Files[id] = new VaultBackupFile
        {
            Id = id,
            Name = name,
            SizeBytes = content.Length,
            ModifiedAt = modifiedAt
        };
        _content[id] = content;
        return this;
    }

    public Task<IReadOnlyList<VaultBackupFile>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<VaultBackupFile>>(
            Files.Values.OrderByDescending(f => f.ModifiedAt).ToList());

    public Task<VaultBackupFile> UploadAsync(
        string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        var id = $"file-{Files.Count + 1}";
        var file = new VaultBackupFile
        {
            Id = id,
            Name = fileName,
            SizeBytes = content.Length,
            ModifiedAt = DateTimeOffset.UtcNow
        };

        Files[id] = file;
        _content[id] = content;
        UploadedNames.Add(fileName);
        LastUploadedContent = content;

        return Task.FromResult(file);
    }

    public Task<byte[]> DownloadAsync(string fileId, CancellationToken cancellationToken = default) =>
        _content.TryGetValue(fileId, out var content)
            ? Task.FromResult(content)
            : throw new InvalidOperationException($"No existe el respaldo '{fileId}'.");

    public Task DeleteAsync(string fileId, CancellationToken cancellationToken = default)
    {
        Files.Remove(fileId);
        _content.Remove(fileId);
        Deleted.Add(fileId);
        return Task.CompletedTask;
    }
}
