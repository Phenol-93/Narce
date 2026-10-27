using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Konscious.Security.Cryptography;
using Nacre.Core.Models;

namespace Nacre.Core.Services;

public sealed record KdfParameters(int MemoryKiB = 65536, int Iterations = 3, int Parallelism = 4);
public sealed record VaultHeader(string Format, int Version, string Cipher, string Kdf, KdfParameters Parameters, byte[] Salt);
public sealed record VaultEnvelope(VaultHeader Header, byte[] Nonce, byte[] Tag, byte[] Ciphertext);

public static class VaultCryptography
{
    public const int MaxFileBytes = 48 * 1024 * 1024;
    public const int MaxPayloadBytes = 32 * 1024 * 1024;
    public const int MinimumPasswordLength = 6;
    public static void ValidateNewPassword(ReadOnlySpan<byte> password)
    {
        try
        {
            if (password.Length > 4096 || new UTF8Encoding(false, true).GetCharCount(password) < MinimumPasswordLength)
                throw new ArgumentException($"主密码至少需要 {MinimumPasswordLength} 个字符，且不能超过 4096 个 UTF-8 字节。");
        }
        catch (DecoderFallbackException) { throw new ArgumentException("主密码必须为有效的 UTF-8 文本。"); }
    }
    public static VaultHeader NewHeader() => new("Nacre", 1, "AES-256-GCM", "Argon2id", new(), RandomNumberGenerator.GetBytes(32));

    public static async Task<byte[]> DeriveKeyAsync(ReadOnlyMemory<byte> password, VaultHeader header)
    {
        ValidateHeader(header);
        var bytes = password.ToArray();
        try
        {
            using var argon = new Argon2id(bytes)
            {
                Salt = header.Salt, MemorySize = header.Parameters.MemoryKiB,
                Iterations = header.Parameters.Iterations, DegreeOfParallelism = header.Parameters.Parallelism
            };
            return await argon.GetBytesAsync(32).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static void ValidateHeader(VaultHeader? header)
    {
        if (header is null || header.Format != "Nacre" || header.Version != 1 || header.Cipher != "AES-256-GCM" || header.Kdf != "Argon2id")
            throw new VaultFormatException("这不是受支持的 Nacre 保险库（文件格式或版本不正确）。");
        var p = header.Parameters;
        // Bound untrusted parameters before allocating Argon2 memory.
        if (header.Salt is not { Length: 32 } || p is null || p.MemoryKiB is < 65536 or > 262144 || p.Iterations is < 3 or > 10 || p.Parallelism is < 1 or > 8)
            throw new VaultFormatException("保险库的密钥派生参数无效或超出安全资源限制。");
    }

    public static VaultEnvelope Parse(ReadOnlySpan<byte> file)
    {
        if (file.Length > MaxFileBytes) throw new VaultFormatException("保险库文件过大，无法安全打开。");
        try
        {
            var envelope = JsonSerializer.Deserialize<VaultEnvelope>(file, VaultJson.Options) ?? throw new JsonException();
            ValidateHeader(envelope.Header);
            if (envelope.Nonce is not { Length: 12 } || envelope.Tag is not { Length: 16 } || envelope.Ciphertext is null || envelope.Ciphertext.Length > MaxPayloadBytes)
                throw new VaultFormatException("保险库加密数据不完整或长度无效。");
            return envelope;
        }
        catch (JsonException) { throw new VaultFormatException("保险库不是有效的加密 JSON 文件，可能已损坏。"); }
    }

    public static byte[] Encrypt(VaultData data, ReadOnlySpan<byte> key, VaultHeader header, byte[]? previousNonce = null)
    {
        ValidateHeader(header);
        VaultValidation.ValidatePayload(data);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(data, VaultJson.Options);
        try
        {
            if (plaintext.Length > MaxPayloadBytes) throw new VaultFormatException("保险库内容超过 32 MiB，请减少过大的文本内容。");
            byte[] nonce;
            do { nonce = RandomNumberGenerator.GetBytes(12); } while (previousNonce is not null && nonce.AsSpan().SequenceEqual(previousNonce));
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[16];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, JsonSerializer.SerializeToUtf8Bytes(header, VaultJson.Options));
            return JsonSerializer.SerializeToUtf8Bytes(new VaultEnvelope(header, nonce, tag, ciphertext), VaultJson.Options);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public static VaultData Decrypt(VaultEnvelope envelope, ReadOnlySpan<byte> key)
    {
        ValidateHeader(envelope.Header);
        var plaintext = new byte[envelope.Ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, plaintext, JsonSerializer.SerializeToUtf8Bytes(envelope.Header, VaultJson.Options));
            var data = JsonSerializer.Deserialize<VaultData>(plaintext, VaultJson.Options) ?? throw new JsonException();
            VaultValidation.ValidatePayload(data);
            return data;
        }
        catch (CryptographicException) { throw new VaultAuthenticationException(); }
        catch (JsonException) { throw new VaultFormatException("解密后的数据格式无效或来自不兼容版本。"); }
        catch (NullReferenceException) { throw new VaultFormatException("解密后的记录数据不完整。"); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
}
