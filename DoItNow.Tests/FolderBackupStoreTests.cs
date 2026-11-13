using DoItNow.Services;
using DoItNow.Services.Backup;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace DoItNow.Tests;

public class FolderBackupStoreTests
{
    private static FolderBackupStore CreateStore(TestEnvironment env) =>
        new(Path.Combine(env.Folder, "Backups"));

    [Fact]
    public async Task Upload_List_Download_Delete_RoundTrips()
    {
        using var env = new TestEnvironment();
        var store = CreateStore(env);
        var content = new byte[] { 1, 2, 3, 4, 5 };

        var uploaded = await store.UploadAsync("doitnow-vault-20260304-050607.ditvault", content);

        Assert.Equal("doitnow-vault-20260304-050607.ditvault", uploaded.Name);
        Assert.Equal(content.Length, uploaded.SizeBytes);

        var listed = Assert.Single(await store.ListAsync());
        Assert.Equal(uploaded.Id, listed.Id);

        Assert.Equal(content, await store.DownloadAsync(uploaded.Id));

        await store.DeleteAsync(uploaded.Id);
        Assert.Empty(await store.ListAsync());
        Assert.False(File.Exists(Path.Combine(store.RootPath, uploaded.Name)));
    }

    [Fact]
    public async Task ListAsync_IgnoresFilesThatAreNotVaultBackups()
    {
        using var env = new TestEnvironment();
        var store = CreateStore(env);
        Directory.CreateDirectory(store.RootPath);

        await File.WriteAllBytesAsync(
            Path.Combine(store.RootPath, "doitnow-vault-20260101-101010.ditvault"),
            new byte[] { 1 });
        await File.WriteAllBytesAsync(Path.Combine(store.RootPath, "notas.txt"), new byte[] { 2 });
        await File.WriteAllBytesAsync(Path.Combine(store.RootPath, "vault.ditvault.tmp"), new byte[] { 3 });

        var listed = Assert.Single(await store.ListAsync());
        Assert.Equal("doitnow-vault-20260101-101010.ditvault", listed.Name);
    }

    [Fact]
    public async Task UploadAsync_DuplicateName_GetsUniqueSuffix()
    {
        using var env = new TestEnvironment();
        var store = CreateStore(env);
        const string name = "doitnow-vault-20260304-050607.ditvault";

        await store.UploadAsync(name, new byte[] { 1 });
        var second = await store.UploadAsync(name, new byte[] { 2 });
        var third = await store.UploadAsync(name, new byte[] { 3 });

        Assert.Equal("doitnow-vault-20260304-050607-1.ditvault", second.Name);
        Assert.Equal("doitnow-vault-20260304-050607-2.ditvault", third.Name);
        Assert.Equal(3, (await store.ListAsync()).Count);
    }

    [Theory]
    [InlineData("..\\evil.ditvault")]
    [InlineData("../evil.ditvault")]
    [InlineData("sub/evil.ditvault")]
    [InlineData("evil.txt")]
    [InlineData("")]
    public async Task DownloadAsync_RejectsIdsOutsideTheFolder(string fileId)
    {
        using var env = new TestEnvironment();
        var store = CreateStore(env);

        await Assert.ThrowsAsync<ArgumentException>(() => store.DownloadAsync(fileId));
    }

    [Fact]
    public async Task DeleteAsync_RejectsIdsOutsideTheFolder()
    {
        using var env = new TestEnvironment();
        var store = CreateStore(env);
        var outside = Path.Combine(env.Folder, "outside.ditvault");
        await File.WriteAllBytesAsync(outside, new byte[] { 1 });

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.DeleteAsync("..\\outside.ditvault"));

        Assert.True(File.Exists(outside));
    }

    [Fact]
    public async Task BackupService_TrimAsync_KeepsNewestFilesInFolder()
    {
        using var env = new TestEnvironment();
        var store = CreateStore(env);
        Directory.CreateDirectory(store.RootPath);

        var start = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 8; i++)
        {
            var path = Path.Combine(store.RootPath, $"doitnow-vault-2026010{i + 1}-080000.ditvault");
            await File.WriteAllBytesAsync(path, new byte[] { (byte)i });
            File.SetLastWriteTimeUtc(path, start.AddDays(i));
        }

        var vault = new VaultService();
        await vault.InitializeAsync();

        var service = new BackupService(store, vault);
        await service.TrimAsync();

        var remaining = (await store.ListAsync()).OrderBy(f => f.ModifiedAt).ToList();

        Assert.Equal(BackupService.MaxBackups, remaining.Count);
        Assert.Equal("doitnow-vault-20260104-080000.ditvault", remaining[0].Name);
        Assert.Equal("doitnow-vault-20260108-080000.ditvault", remaining[^1].Name);
    }

    [Fact]
    public void Constructor_WithoutFolder_Throws()
    {
        Assert.Throws<ArgumentException>(() => new FolderBackupStore("   "));
    }
}
