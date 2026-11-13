using DoItNow.Models;
using DoItNow.Services;
using DoItNow.Services.Backup;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace DoItNow.Tests;

public class BackupServiceTests
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
        var name = BackupService.BuildFileName(new DateTime(2026, 3, 4, 5, 6, 7));

        Assert.Equal("doitnow-vault-20260304-050607.ditvault", name);
    }

    [Fact]
    public async Task BackupAsync_UploadsEncryptedVault()
    {
        using var env = new TestEnvironment();
        var vault = await CreateVaultWithEntryAsync();
        var store = new FakeBackupStore();
        var service = new BackupService(store, vault);

        var uploaded = await service.BackupAsync(
            "clave-respaldo", new DateTime(2026, 3, 4, 5, 6, 7));

        Assert.Equal("doitnow-vault-20260304-050607.ditvault", uploaded.Name);
        Assert.Single(store.Files);

        var content = Assert.IsType<byte[]>(store.LastUploadedContent);
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
        var service = new BackupService(new FakeBackupStore(), vault);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.BackupAsync("   ", DateTime.Now));
    }

    [Fact]
    public async Task BackupAsync_TrimsOlderBackups()
    {
        using var env = new TestEnvironment();
        var vault = await CreateVaultWithEntryAsync();
        var store = new FakeBackupStore();

        for (var i = 0; i < BackupService.MaxBackups; i++)
        {
            store.Seed($"old-{i}", $"doitnow-vault-2025{i:00}.ditvault",
                Array.Empty<byte>(), DateTimeOffset.UtcNow.AddDays(-30 + i));
        }

        var service = new BackupService(store, vault);
        await service.BackupAsync("clave-respaldo", DateTime.Now);

        Assert.Equal(BackupService.MaxBackups, store.Files.Count);
        Assert.Single(store.Deleted);
        Assert.Equal("old-0", store.Deleted[0]);
    }

    [Fact]
    public async Task TrimAsync_KeepsNewestBackups()
    {
        using var env = new TestEnvironment();
        var vault = await CreateVaultWithEntryAsync();
        var store = new FakeBackupStore();

        for (var i = 0; i < 8; i++)
        {
            store.Seed($"f{i}", $"doitnow-vault-2025{i:00}.ditvault",
                Array.Empty<byte>(), DateTimeOffset.UtcNow.AddDays(i));
        }

        var service = new BackupService(store, vault);
        await service.TrimAsync();

        Assert.Equal(BackupService.MaxBackups, store.Files.Count);
        Assert.Equal(3, store.Deleted.Count);
        Assert.Equal(new[] { "f0", "f1", "f2" }, store.Deleted.OrderBy(x => x));
        Assert.All(store.Files.Keys, id => Assert.DoesNotContain(id, store.Deleted));
    }

    [Fact]
    public async Task RestoreAsync_ImportsEntriesIntoVault()
    {
        using var env = new TestEnvironment();
        env.Use("source.db");

        var source = await CreateVaultWithEntryAsync("AWS Staging");
        var store = new FakeBackupStore();
        var sourceService = new BackupService(store, source);

        var uploaded = await sourceService.BackupAsync(
            "clave-respaldo", new DateTime(2026, 3, 4, 5, 6, 7));

        env.Use("target.db");
        var target = await CreateUnlockedVaultAsync();
        var targetService = new BackupService(store, target);

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
        var store = new FakeBackupStore();
        var service = new BackupService(store, vault);

        var uploaded = await service.BackupAsync("correcta", DateTime.Now);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RestoreAsync(uploaded.Id, "incorrecta"));

        Assert.Contains("Contraseña incorrecta", error.Message);
    }

    [Fact]
    public async Task ListAsync_DelegatesToStore()
    {
        using var env = new TestEnvironment();
        var vault = await CreateVaultWithEntryAsync();
        var store = new FakeBackupStore()
            .Seed("b", "doitnow-vault-20260202.ditvault", Array.Empty<byte>(), DateTimeOffset.UtcNow)
            .Seed("a", "doitnow-vault-20260101.ditvault", Array.Empty<byte>(), DateTimeOffset.UtcNow.AddDays(-1));

        var service = new BackupService(store, vault);
        var files = await service.ListAsync();

        Assert.Equal(new[] { "b", "a" }, files.Select(f => f.Id));
    }
}
