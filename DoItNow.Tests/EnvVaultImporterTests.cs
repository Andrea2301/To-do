using DoItNow.Models;
using DoItNow.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace DoItNow.Tests;

public class EnvVaultImporterTests
{
    private static async Task<VaultService> CreateUnlockedVaultAsync()
    {
        var vault = new VaultService();
        await vault.InitializeAsync();
        Assert.True(vault.IsUnlocked, "La bóveda debería arrancar desbloqueada en modo DPAPI.");
        return vault;
    }

    [Fact]
    public async Task ImportAsync_CreatesVariableEntriesWithEncryptedSecrets()
    {
        using var env = new TestEnvironment();
        var vault = await CreateUnlockedVaultAsync();

        var variables = EnvFileImporter.Parse(
            "GROQ_API_KEY=gsk_local_demo\nDATABASE_URL=postgres://localhost/dev\n");

        var result = await EnvVaultImporter.ImportAsync(
            vault, variables, "dev.env", new DateTime(2026, 11, 12));

        Assert.Equal(2, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.Equal("GROQ_API_KEY", result.First?.Title);

        var stored = await vault.GetEntriesAsync();
        Assert.Equal(2, stored.Count);
        Assert.All(stored, e => Assert.Equal("Variable", e.EntryType));
        Assert.Contains(stored, e => e.Title == "DATABASE_URL" && e.Secret == "postgres://localhost/dev");
        Assert.All(stored, e => Assert.Contains("dev.env", e.Notes));
    }

    [Fact]
    public async Task ImportAsync_UpdatesExistingEntryByTitleIgnoringCase()
    {
        using var env = new TestEnvironment();
        var vault = await CreateUnlockedVaultAsync();

        var existing = new VaultEntry
        {
            Title = "GROQ_API_KEY",
            EntryType = "API Key",
            Secret = "valor-antiguo"
        };
        await vault.SaveEntryAsync(existing);

        var variables = EnvFileImporter.Parse("groq_api_key=valor-nuevo\n");

        var result = await EnvVaultImporter.ImportAsync(
            vault, variables, "dev.env", new DateTime(2026, 11, 12));

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Updated);

        var stored = Assert.Single(await vault.GetEntriesAsync());
        Assert.Equal(existing.Id, stored.Id);
        Assert.Equal("Variable", stored.EntryType);
        Assert.Equal("valor-nuevo", stored.Secret);
    }

    [Fact]
    public async Task ImportAsync_EmptyList_DoesNothing()
    {
        using var env = new TestEnvironment();
        var vault = await CreateUnlockedVaultAsync();

        var result = await EnvVaultImporter.ImportAsync(
            vault, EnvFileImporter.Parse(string.Empty), "empty.env", DateTime.Now);

        Assert.Equal(0, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.Null(result.First);
        Assert.Empty(await vault.GetEntriesAsync());
    }

    [Fact]
    public async Task ImportAsync_KeepsOneEntryPerKey()
    {
        using var env = new TestEnvironment();
        var vault = await CreateUnlockedVaultAsync();

        var variables = EnvFileImporter.Parse("TOKEN=primero\nOTRA=valor\nTOKEN=segundo\n");

        var result = await EnvVaultImporter.ImportAsync(
            vault, variables, "dev.env", DateTime.Now);

        Assert.Equal(2, result.Created);
        var stored = await vault.GetEntriesAsync();
        Assert.Equal(2, stored.Count);
        Assert.Equal("segundo", stored.Single(e => e.Title == "TOKEN").Secret);
    }
}
