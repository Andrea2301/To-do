using DoItNow.Data;
using DoItNow.Models;
using DoItNow.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace DoItNow.Tests;

public class VaultServiceTests
{
    private static async Task<VaultService> CreateUnlockedVaultAsync()
    {
        var vault = new VaultService();
        await vault.InitializeAsync();
        Assert.True(vault.IsUnlocked, "La bóveda debería arrancar desbloqueada en modo DPAPI.");
        return vault;
    }

    private static VaultEntry NewEntry(string title = "AWS Producción") => new()
    {
        Title = title,
        EntryType = "API Key",
        Username = "AKIAIOSFODNN7EXAMPLE",
        Url = "https://console.aws.amazon.com/iam/",
        Secret = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
        Notes = "region = eu-west-1"
    };

    [Fact]
    public async Task SaveEntry_EncryptsSecretAndNotesAtRest()
    {
        using var env = new TestEnvironment();
        var vault = await CreateUnlockedVaultAsync();

        var entry = NewEntry();
        await vault.SaveEntryAsync(entry);

        Assert.True(entry.Id > 0);

        await using (var db = new AppDbContext())
        {
            var stored = await db.VaultEntries.SingleAsync();
            Assert.DoesNotContain(entry.Secret, stored.EncryptedSecret);
            Assert.DoesNotContain(entry.Notes, stored.EncryptedNotes);
            Assert.NotEmpty(stored.EncryptedSecret);
            Assert.NotEmpty(stored.EncryptedNotes);
        }

        var loaded = Assert.Single(await vault.GetEntriesAsync());
        Assert.Equal(entry.Secret, loaded.Secret);
        Assert.Equal(entry.Notes, loaded.Notes);
    }

    [Fact]
    public async Task SaveEntry_RequiresTitle()
    {
        using var env = new TestEnvironment();
        var vault = await CreateUnlockedVaultAsync();

        var entry = NewEntry();
        entry.Title = "   ";

        await Assert.ThrowsAsync<InvalidOperationException>(() => vault.SaveEntryAsync(entry));
    }

    [Fact]
    public async Task AddAttachment_RoundTripsContentAndMetadata()
    {
        using var env = new TestEnvironment();
        var vault = await CreateUnlockedVaultAsync();

        var entry = NewEntry();
        await vault.SaveEntryAsync(entry);

        var content = Encoding.UTF8.GetBytes("Access key ID,Secret access key\nAKIA...,secreto...\n");
        var attachment = await vault.AddAttachmentAsync(
            entry.Id, @"C:\Users\demo\credentials.csv", "text/csv", content);

        Assert.True(attachment.Id > 0);
        Assert.Equal("credentials.csv", attachment.FileName);
        Assert.Equal(content.Length, attachment.SizeBytes);
        Assert.Empty(attachment.EncryptedContent);

        var restored = await vault.ReadAttachmentAsync(attachment.Id);
        Assert.Equal(content, restored);

        var loaded = Assert.Single(await vault.GetEntriesAsync());
        var metadata = Assert.Single(loaded.Attachments);
        Assert.Equal("credentials.csv", metadata.FileName);
        Assert.Equal("text/csv", metadata.ContentType);
        Assert.Empty(metadata.EncryptedContent);
    }

    [Fact]
    public async Task AddAttachment_StoresEncryptedBlob()
    {
        using var env = new TestEnvironment();
        var vault = await CreateUnlockedVaultAsync();

        var entry = NewEntry();
        await vault.SaveEntryAsync(entry);

        var content = Encoding.UTF8.GetBytes("contenido sensible del archivo");
        await vault.AddAttachmentAsync(entry.Id, "notas.txt", "text/plain", content);

        await using var db = new AppDbContext();
        var stored = await db.VaultAttachments.SingleAsync();
        Assert.NotEmpty(stored.EncryptedContent);
        Assert.False(stored.EncryptedContent.SequenceEqual(content));
        Assert.DoesNotContain("sensible", Encoding.UTF8.GetString(stored.EncryptedContent));
    }

    [Fact]
    public async Task AddAttachment_RejectsFilesOverLimit()
    {
        using var env = new TestEnvironment();
        var vault = await CreateUnlockedVaultAsync();

        var entry = NewEntry();
        await vault.SaveEntryAsync(entry);

        var tooBig = new byte[VaultService.MaxAttachmentBytes + 1];
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => vault.AddAttachmentAsync(entry.Id, "grande.bin", "application/octet-stream", tooBig));
    }

    [Fact]
    public async Task AddAttachment_RejectsUnknownEntry()
    {
        using var env = new TestEnvironment();
        var vault = await CreateUnlockedVaultAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => vault.AddAttachmentAsync(9999, "x.txt", "text/plain", new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public async Task DeleteAttachment_RemovesOnlyThatFile()
    {
        using var env = new TestEnvironment();
        var vault = await CreateUnlockedVaultAsync();

        var entry = NewEntry();
        await vault.SaveEntryAsync(entry);

        var first = await vault.AddAttachmentAsync(entry.Id, "uno.txt", "text/plain", Encoding.UTF8.GetBytes("uno"));
        var second = await vault.AddAttachmentAsync(entry.Id, "dos.txt", "text/plain", Encoding.UTF8.GetBytes("dos"));

        await vault.DeleteAttachmentAsync(first.Id);

        var loaded = Assert.Single(await vault.GetEntriesAsync());
        var remaining = Assert.Single(loaded.Attachments);
        Assert.Equal(second.Id, remaining.Id);
    }

    [Fact]
    public async Task DeleteEntry_RemovesItsAttachments()
    {
        using var env = new TestEnvironment();
        var vault = await CreateUnlockedVaultAsync();

        var entry = NewEntry();
        await vault.SaveEntryAsync(entry);
        await vault.AddAttachmentAsync(entry.Id, "uno.txt", "text/plain", Encoding.UTF8.GetBytes("uno"));
        await vault.AddAttachmentAsync(entry.Id, "dos.txt", "text/plain", Encoding.UTF8.GetBytes("dos"));

        await vault.DeleteEntryAsync(entry.Id);

        await using var db = new AppDbContext();
        Assert.Equal(0, await db.VaultEntries.CountAsync());
        Assert.Equal(0, await db.VaultAttachments.CountAsync());
    }

    [Fact]
    public async Task SetupMasterPassword_LocksAndUnlocksTheVault()
    {
        using var env = new TestEnvironment();
        var vault = await CreateUnlockedVaultAsync();

        var entry = NewEntry();
        await vault.SaveEntryAsync(entry);
        var content = Encoding.UTF8.GetBytes("adjunto cifrado");
        var attachment = await vault.AddAttachmentAsync(entry.Id, "dato.bin", "application/octet-stream", content);

        await vault.SetupMasterPasswordAsync("master-uno");

        Assert.True(vault.HasMasterPassword);
        Assert.Equal(content, await vault.ReadAttachmentAsync(attachment.Id));

        vault.Lock();
        Assert.False(vault.IsUnlocked);

        Assert.False(await vault.UnlockAsync("incorrecta"));
        Assert.True(await vault.UnlockAsync("master-uno"));
        Assert.Equal(content, await vault.ReadAttachmentAsync(attachment.Id));
    }

    [Fact]
    public async Task ChangeMasterPassword_ReencryptsAttachments()
    {
        using var env = new TestEnvironment();
        var vault = await CreateUnlockedVaultAsync();

        var entry = NewEntry();
        await vault.SaveEntryAsync(entry);
        var content = Encoding.UTF8.GetBytes("contenido a re-cifrar");
        var attachment = await vault.AddAttachmentAsync(entry.Id, "cert.pem", "application/x-pem-file", content);

        await vault.SetupMasterPasswordAsync("master-uno");

        Assert.False(await vault.ChangeMasterPasswordAsync("incorrecta", "otra"));
        Assert.True(await vault.ChangeMasterPasswordAsync("master-uno", "master-dos"));

        vault.Lock();
        Assert.False(await vault.UnlockAsync("master-uno"));
        Assert.True(await vault.UnlockAsync("master-dos"));
        Assert.Equal(content, await vault.ReadAttachmentAsync(attachment.Id));
    }

    [Fact]
    public async Task RemoveMasterPassword_ReturnsToDpapiMode()
    {
        using var env = new TestEnvironment();
        var vault = await CreateUnlockedVaultAsync();

        var entry = NewEntry();
        await vault.SaveEntryAsync(entry);
        var content = Encoding.UTF8.GetBytes("sigue accesible tras quitar la contraseña");
        var attachment = await vault.AddAttachmentAsync(entry.Id, "dato.txt", "text/plain", content);

        await vault.SetupMasterPasswordAsync("master-uno");
        await vault.RemoveMasterPasswordAsync();

        Assert.False(vault.HasMasterPassword);

        // Una instancia nueva debe desbloquearse sola con DPAPI y descifrar el adjunto.
        var reopened = new VaultService();
        await reopened.InitializeAsync();

        Assert.True(reopened.IsUnlocked);
        Assert.Equal(content, await reopened.ReadAttachmentAsync(attachment.Id));
    }

    [Fact]
    public async Task ExportImport_RoundTripsEntriesAndAttachments()
    {
        using var env = new TestEnvironment();
        env.Use("source.db");

        var source = await CreateUnlockedVaultAsync();
        var entry = NewEntry("AWS Staging");
        await source.SaveEntryAsync(entry);

        var content = Encoding.UTF8.GetBytes("credenciales de staging");
        await source.AddAttachmentAsync(entry.Id, "staging.csv", "text/csv", content);

        var exportPath = env.File("backup.ditvault");
        await source.ExportAsync(exportPath, "clave-export");

        var envelope = await File.ReadAllTextAsync(exportPath);
        Assert.DoesNotContain(entry.Secret, envelope);
        Assert.DoesNotContain("AWS Staging", envelope);
        Assert.Contains("\"version\": 2", envelope);

        env.Use("target.db");
        var target = await CreateUnlockedVaultAsync();

        var imported = await target.ImportAsync(exportPath, "clave-export");
        Assert.Equal(1, imported);

        var importedEntry = Assert.Single(await target.GetEntriesAsync());
        Assert.Equal(entry.Secret, importedEntry.Secret);
        Assert.Equal(entry.Notes, importedEntry.Notes);

        var importedAttachment = Assert.Single(importedEntry.Attachments);
        Assert.Equal("staging.csv", importedAttachment.FileName);
        Assert.Equal(content, await target.ReadAttachmentAsync(importedAttachment.Id));
    }

    [Fact]
    public async Task Import_WithWrongPassword_Throws()
    {
        using var env = new TestEnvironment();
        env.Use("source.db");

        var source = await CreateUnlockedVaultAsync();
        await source.SaveEntryAsync(NewEntry());

        var exportPath = env.File("backup.ditvault");
        await source.ExportAsync(exportPath, "correcta");

        env.Use("target.db");
        var target = await CreateUnlockedVaultAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => target.ImportAsync(exportPath, "incorrecta"));

        Assert.Contains("Contraseña incorrecta", error.Message);
    }

    [Fact]
    public async Task ExportImport_IsIdempotentForSameEntryAndDoesNotDuplicateAttachments()
    {
        using var env = new TestEnvironment();
        var source = await CreateUnlockedVaultAsync();
        var entry = NewEntry("Repetida");
        await source.SaveEntryAsync(entry);
        await source.AddAttachmentAsync(entry.Id, "dato.txt", "text/plain", Encoding.UTF8.GetBytes("dato"));

        var exportPath = env.File("backup.ditvault");
        await source.ExportAsync(exportPath, "clave-export");

        var imported = await source.ImportAsync(exportPath, "clave-export");
        Assert.Equal(1, imported);

        var reloaded = Assert.Single(await source.GetEntriesAsync());
        Assert.Single(reloaded.Attachments);
    }
}
