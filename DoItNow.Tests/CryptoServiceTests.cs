using DoItNow.Services;
using System;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace DoItNow.Tests;

public class CryptoServiceTests
{
    private static byte[] NewKey() => CryptoService.RandomBytes(CryptoService.KeySize);

    [Fact]
    public void Encrypt_Decrypt_RoundTripsUtf8Text()
    {
        var key = NewKey();
        const string plaintext = "clave secreta · ñandú · 日本語 · 🔐";

        var payload = CryptoService.Encrypt(plaintext, key);

        Assert.NotEqual(plaintext, payload);
        Assert.Equal(plaintext, CryptoService.Decrypt(payload, key));
    }

    [Fact]
    public void Encrypt_SameInputTwice_ProducesDifferentCiphertext()
    {
        var key = NewKey();

        var first = CryptoService.Encrypt("mismo texto", key);
        var second = CryptoService.Encrypt("mismo texto", key);

        Assert.NotEqual(first, second);
        Assert.Equal("mismo texto", CryptoService.Decrypt(first, key));
        Assert.Equal("mismo texto", CryptoService.Decrypt(second, key));
    }

    [Fact]
    public void EncryptToBlob_DecryptFromBlob_RoundTripsBinaryContent()
    {
        var key = NewKey();
        var content = CryptoService.RandomBytes(64 * 1024);

        var blob = CryptoService.EncryptToBlob(content, key);
        var restored = CryptoService.DecryptFromBlob(blob, key);

        Assert.Equal(content, restored);
    }

    [Fact]
    public void Decrypt_WithWrongKey_Throws()
    {
        var payload = CryptoService.Encrypt("secreto", NewKey());

        Assert.ThrowsAny<CryptographicException>(() => CryptoService.Decrypt(payload, NewKey()));
    }

    [Fact]
    public void Decrypt_TamperedBlob_Throws()
    {
        var key = NewKey();
        var blob = CryptoService.EncryptToBlob(Encoding.UTF8.GetBytes("contenido íntegro"), key);
        blob[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => CryptoService.DecryptFromBlob(blob, key));
    }

    [Fact]
    public void Decrypt_UnknownVersion_Throws()
    {
        var key = NewKey();
        var blob = CryptoService.EncryptToBlob(Encoding.UTF8.GetBytes("x"), key);
        blob[0] = 0x7F;

        Assert.Throws<CryptographicException>(() => CryptoService.DecryptFromBlob(blob, key));
    }

    [Fact]
    public void Decrypt_EmptyPayload_ReturnsEmptyBytes()
    {
        Assert.Empty(CryptoService.DecryptBytes(string.Empty, NewKey()));
    }

    [Fact]
    public void DeriveKeyMaterial_IsDeterministicForSamePasswordAndSalt()
    {
        var salt = CryptoService.RandomBytes(CryptoService.SaltSize);

        var first = CryptoService.DeriveKeyMaterial("contraseña", salt);
        var second = CryptoService.DeriveKeyMaterial("contraseña", salt);

        Assert.Equal(first.Verifier, second.Verifier);
        Assert.Equal(first.Key, second.Key);
        Assert.Equal(CryptoService.KeySize, first.Key.Length);
    }

    [Fact]
    public void DeriveKeyMaterial_ChangesWithSalt()
    {
        var first = CryptoService.DeriveKeyMaterial("contraseña", CryptoService.RandomBytes(CryptoService.SaltSize));
        var second = CryptoService.DeriveKeyMaterial("contraseña", CryptoService.RandomBytes(CryptoService.SaltSize));

        Assert.NotEqual(first.Key, second.Key);
    }

    [Fact]
    public void DeriveKeyMaterial_RejectsEmptyPassword()
    {
        var salt = CryptoService.RandomBytes(CryptoService.SaltSize);

        Assert.Throws<ArgumentException>(() => CryptoService.DeriveKeyMaterial(string.Empty, salt));
    }

    [Fact]
    public void Verify_ComparesVerifiersWithoutLeakingLengthMismatch()
    {
        var salt = CryptoService.RandomBytes(CryptoService.SaltSize);
        var (verifier, _) = CryptoService.DeriveKeyMaterial("correcta", salt);
        var (candidate, _) = CryptoService.DeriveKeyMaterial("correcta", salt);
        var (wrong, _) = CryptoService.DeriveKeyMaterial("incorrecta", salt);

        Assert.True(CryptoService.Verify(verifier, candidate));
        Assert.False(CryptoService.Verify(verifier, wrong));
        Assert.False(CryptoService.Verify(verifier, verifier[..8]));
    }
}
