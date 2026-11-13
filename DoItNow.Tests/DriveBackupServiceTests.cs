using DoItNow.Models;
using DoItNow.Services;
using DoItNow.Services.Drive;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace DoItNow.Tests;

public class DriveBackupServiceTests
{
    private const string Secret = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";

    private static async Task<VaultService> CreateUnlockedVaultAsync()
    {
        var vault = new VaultService();
        await vault.InitializeAsync();
        Assert.True(vault.IsUnlocked);
        return vault;
    }

    private static async Task<VaultService> CreateVaultWithEntryAsync(string title = "AWS Producción")
    {
        var vault = await CreateUnlockedVaultAsync();
        await vault.SaveEntryAsync(new VaultEntry
        {
            Title = title,
            EntryType = "API Key",
            Username = "AKIAIOSFODNN7EXAMPLE",
            Secret = Secret,
            Notes = "region = eu-west-1"
        });

        return vault;
    }

    [Fact]
    public void BuildFileName_UsesSortableTimestamp()
    {
        var name = DriveBackupService.BuildFileName(new DateTime(2026, 3, 4, 5, 6, 7));

        Assert.Equal("doitnow-vault-20260304-050607.ditvault", name);
    }

    [Fact]
    public async Task BackupAsync_UploadsEncryptedVault()
    {
        using var env = new TestEnvironment();
        var vault = await CreateVaultWithEntryAsync();
        var client = new FakeDriveClient();
        var service = new DriveBackupService(client, vault);

        var uploaded = await service.BackupAsync(
            "clave-respaldo", new DateTime(2026, 3, 4, 5, 6, 7));

        Assert.Equal("doitnow-vault-20260304-050607.ditvault", uploaded.Name);
        Assert.Single(client.Files);

        var content = Assert.IsType<byte[]>(client.LastUploadedContent);
        var text = Encoding.UTF8.GetString(content);
        Assert.DoesNotContain(Secret, text);
        Assert.DoesNotContain("AWS Producción", text);
        Assert.Contains("\"version\": 2", text);
    }

    [Fact]
    public async Task BackupAsync_WithoutPassword_Throws()
    {
        using var env = new TestEnvironment();
        var vault = await CreateVaultWithEntryAsync();
        var service = new DriveBackupService(new FakeDriveClient(), vault);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.BackupAsync("   ", DateTime.Now));
    }

    [Fact]
    public async Task BackupAsync_TrimsOlderBackups()
    {
        using var env = new TestEnvironment();
        var vault = await CreateVaultWithEntryAsync();
        var client = new FakeDriveClient();

        for (var i = 0; i < DriveBackupService.MaxBackups; i++)
        {
            client.Seed($"old-{i}", $"doitnow-vault-2025{i:00}.ditvault",
                Array.Empty<byte>(), DateTimeOffset.UtcNow.AddDays(-30 + i));
        }

        var service = new DriveBackupService(client, vault);
        await service.BackupAsync("clave-respaldo", DateTime.Now);

        Assert.Equal(DriveBackupService.MaxBackups, client.Files.Count);
        Assert.Single(client.Deleted);
        Assert.Equal("old-0", client.Deleted[0]);
    }

    [Fact]
    public async Task TrimAsync_KeepsNewestBackups()
    {
        using var env = new TestEnvironment();
        var vault = await CreateVaultWithEntryAsync();
        var client = new FakeDriveClient();

        for (var i = 0; i < 8; i++)
        {
            client.Seed($"f{i}", $"doitnow-vault-2025{i:00}.ditvault",
                Array.Empty<byte>(), DateTimeOffset.UtcNow.AddDays(i));
        }

        var service = new DriveBackupService(client, vault);
        await service.TrimAsync();

        Assert.Equal(DriveBackupService.MaxBackups, client.Files.Count);
        Assert.Equal(3, client.Deleted.Count);
        Assert.Equal(new[] { "f0", "f1", "f2" }, client.Deleted.OrderBy(x => x));
        Assert.All(client.Files.Keys, id => Assert.DoesNotContain(id, client.Deleted));
    }

    [Fact]
    public async Task RestoreAsync_ImportsEntriesIntoVault()
    {
        using var env = new TestEnvironment();
        env.Use("source.db");

        var source = await CreateVaultWithEntryAsync("AWS Staging");
        var client = new FakeDriveClient();
        var sourceService = new DriveBackupService(client, source);

        var uploaded = await sourceService.BackupAsync(
            "clave-respaldo", new DateTime(2026, 3, 4, 5, 6, 7));

        env.Use("target.db");
        var target = await CreateUnlockedVaultAsync();
        var targetService = new DriveBackupService(client, target);

        var imported = await targetService.RestoreAsync(uploaded.Id, "clave-respaldo");

        Assert.Equal(1, imported);
        var entry = Assert.Single(await target.GetEntriesAsync());
        Assert.Equal("AWS Staging", entry.Title);
        Assert.Equal(Secret, entry.Secret);
    }

    [Fact]
    public async Task RestoreAsync_WithWrongPassword_Throws()
    {
        using var env = new TestEnvironment();
        var vault = await CreateVaultWithEntryAsync();
        var client = new FakeDriveClient();
        var service = new DriveBackupService(client, vault);

        var uploaded = await service.BackupAsync("correcta", DateTime.Now);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RestoreAsync(uploaded.Id, "incorrecta"));

        Assert.Contains("Contraseña incorrecta", error.Message);
    }

    [Fact]
    public async Task ListAsync_DelegatesToClient()
    {
        using var env = new TestEnvironment();
        var vault = await CreateVaultWithEntryAsync();
        var client = new FakeDriveClient()
            .Seed("b", "doitnow-vault-20260202.ditvault", Array.Empty<byte>(), DateTimeOffset.UtcNow)
            .Seed("a", "doitnow-vault-20260101.ditvault", Array.Empty<byte>(), DateTimeOffset.UtcNow.AddDays(-1));

        var service = new DriveBackupService(client, vault);
        var files = await service.ListAsync();

        Assert.Equal(new[] { "b", "a" }, files.Select(f => f.Id));
    }
}
