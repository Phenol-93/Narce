using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Konscious.Security.Cryptography;
using Nacre.Core.Models;
using Nacre.Core.Services;

namespace Nacre.Tests;

public sealed class CryptographyTests
{
    [Fact]
    public void Argon2idMatchesRfc9106Section53()
    {
        using var argon = new Argon2id(Enumerable.Repeat((byte)1, 32).ToArray())
        {
            Salt = Enumerable.Repeat((byte)2, 16).ToArray(),
            KnownSecret = Enumerable.Repeat((byte)3, 8).ToArray(),
            AssociatedData = Enumerable.Repeat((byte)4, 12).ToArray(),
            MemorySize = 32, Iterations = 3, DegreeOfParallelism = 4
        };
        Assert.Equal("0D640DF58D78766C08C037A34A8B53C9D01EF0452D75B65EB52520E96B01E659", Convert.ToHexString(argon.GetBytes(32)));
    }

    [Fact]
    public async Task PayloadIsAuthenticatedAndCompletelyEncrypted()
    {
        var header = VaultCryptography.NewHeader();
        var password = Encoding.UTF8.GetBytes("fictional-master-password");
        var key = await VaultCryptography.DeriveKeyAsync(password, header);
        try
        {
            var bytes = VaultCryptography.Encrypt(Fixtures.Data(), key, header);
            var file = Encoding.UTF8.GetString(bytes);
            foreach (var secret in new[] { "fictional-key-for-tests", "私有分类", "测试备注", "fictional prompt", "private-tag" }) Assert.DoesNotContain(secret, file);
            var envelope = VaultCryptography.Parse(bytes);
            Assert.Equal(12, envelope.Nonce.Length); Assert.Equal(16, envelope.Tag.Length); Assert.Equal(32, envelope.Header.Salt.Length);
            Assert.Equal(Fixtures.Data().Credentials[0].Fields[0].Value, VaultCryptography.Decrypt(envelope, key).Credentials[0].Fields[0].Value);
            Assert.Equal("fictional prompt\r\n第二行", VaultCryptography.Decrypt(envelope, key).Texts[0].Body);
            Assert.Equal("私有分类", VaultCryptography.Decrypt(envelope, key).Categories.Single());
            foreach (var part in new[] { "ciphertext", "tag", "nonce", "salt", "parameters" })
            {
                var changed = VaultCryptography.Parse(bytes);
                changed = part switch
                {
                    "ciphertext" => changed with { Ciphertext = Flip(changed.Ciphertext) },
                    "tag" => changed with { Tag = Flip(changed.Tag) },
                    "nonce" => changed with { Nonce = Flip(changed.Nonce) },
                    "salt" => changed with { Header = changed.Header with { Salt = Flip(changed.Header.Salt) } },
                    _ => changed with { Header = changed.Header with { Parameters = changed.Header.Parameters with { Iterations = 4 } } }
                };
                Assert.Throws<VaultAuthenticationException>(() => VaultCryptography.Decrypt(changed, key));
            }
            var nonces = Enumerable.Range(0, 100).Select(_ => Convert.ToHexString(VaultCryptography.Parse(VaultCryptography.Encrypt(Fixtures.Data(), key, header)).Nonce)).ToArray();
            Assert.Equal(nonces.Length, nonces.Distinct().Count());
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(password); }
    }

    [Fact]
    public void RejectsMalformedAndUnsupportedFilesBeforeKdfAllocation()
    {
        Assert.Throws<VaultFormatException>(() => VaultCryptography.Parse("not json"u8));
        Assert.Throws<VaultFormatException>(() => VaultCryptography.Parse("{}"u8));
        var header = VaultCryptography.NewHeader();
        foreach (var altered in new[] { header with { Version = 2 }, header with { Kdf = "unknown" }, header with { Parameters = new(int.MaxValue) }, header with { Salt = new byte[1] } })
            Assert.Throws<VaultFormatException>(() => VaultCryptography.ValidateHeader(altered));
        var bad = new VaultEnvelope(header, new byte[8], new byte[16], []);
        Assert.Throws<VaultFormatException>(() => VaultCryptography.Parse(JsonSerializer.SerializeToUtf8Bytes(bad, VaultJson.Options)));
    }
    private static byte[] Flip(byte[] value) { var copy = value.ToArray(); copy[0] ^= 0x80; return copy; }

    [Theory]
    [InlineData("fake5", false)]
    [InlineData("fake06", true)]
    [InlineData("fictional-long-password", true)]
    [InlineData("虚构短密码", false)]
    [InlineData("虚构测试密码", true)]
    public void PasswordMinimumIsSixCharacters(string password, bool accepted)
    {
        var bytes = Encoding.UTF8.GetBytes(password);
        try
        {
            if (accepted) VaultCryptography.ValidateNewPassword(bytes);
            else Assert.Throws<ArgumentException>(() => VaultCryptography.ValidateNewPassword(bytes));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    [Fact]
    public void RejectsInvalidPayloadShapesAndCountsPasswordCharacters()
    {
        Assert.Throws<ArgumentException>(() => VaultCryptography.ValidateNewPassword(Encoding.UTF8.GetBytes("虚构短码")));
        Assert.Throws<ArgumentException>(() => VaultCryptography.ValidateNewPassword([0xff, 0xfe]));
        VaultCryptography.ValidateNewPassword(Encoding.UTF8.GetBytes("fictional-long-password"));
        var data = Fixtures.Data();
        data.Credentials[0].Fields[0].Value = null!;
        Assert.Throws<VaultFormatException>(() => VaultValidation.ValidatePayload(data));
        data = Fixtures.Data(); data.Credentials.Add(VaultJson.Clone(data.Credentials[0]));
        Assert.Throws<VaultFormatException>(() => VaultValidation.ValidatePayload(data));
        data = Fixtures.Data(); data.SchemaVersion = 2;
        Assert.Throws<VaultFormatException>(() => VaultValidation.ValidatePayload(data));
    }
}

internal static class Fixtures
{
    internal static byte[] Password() => Encoding.UTF8.GetBytes("fictional-master-password");
    internal static VaultData Data() => new()
    {
        Categories = ["私有分类"], Tags = ["private-tag"],
        Credentials = [new CredentialRecord { Title = "Fictional service", Category = "私有分类", Tags = ["private-tag"], Notes = "测试备注", Fields = [new CredentialField { Name = "TOKEN", Value = "fictional-key-for-tests" }], BaseUrl = "https://example.invalid", Links = ["https://example.invalid/docs"], EnvironmentVariable = "TEST_TOKEN", Usage = "fictional project", ExpiresOn = new DateTime(2027, 1, 2), Favorite = true }],
        Texts = [new TextRecord { Title = "Fictional text", Body = "fictional prompt\r\n第二行", Kind = TextKind.Prompt }]
    };
}
