using System.Security.Cryptography;
using Nacre.Core.Models;

namespace Nacre.Core.Services;

/// <summary>Holds a per-vault process lock. All temporary and backup files are encrypted.</summary>
public sealed class VaultStore : IDisposable
{
    private readonly FileStream _processLock;
    public string FilePath { get; }
    public bool Exists => File.Exists(FilePath);
    public VaultStore(string path)
    {
        FilePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        _processLock = new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public async Task<VaultSession> CreateAsync(ReadOnlyMemory<byte> password)
    {
        if (Exists) throw new IOException("保险库已存在，请使用主密码解锁。");
        VaultCryptography.ValidateNewPassword(password.Span);
        var header = VaultCryptography.NewHeader();
        var key = await VaultCryptography.DeriveKeyAsync(password, header).ConfigureAwait(false);
        var session = new VaultSession(this, key, header, new VaultData(), null);
        try { await session.SaveAsync(session.Data).ConfigureAwait(false); return session; }
        catch { session.Dispose(); throw; }
    }

    public async Task<VaultSession> UnlockAsync(ReadOnlyMemory<byte> password)
    {
        var file = await ReadBoundedAsync(FilePath).ConfigureAwait(false);
        var envelope = VaultCryptography.Parse(file);
        var key = await VaultCryptography.DeriveKeyAsync(password, envelope.Header).ConfigureAwait(false);
        try { return new VaultSession(this, key, envelope.Header, VaultCryptography.Decrypt(envelope, key), SHA256.HashData(file)); }
        catch { CryptographicOperations.ZeroMemory(key); throw; }
    }

    public async Task<VaultSession> RestoreAsync(string backup, ReadOnlyMemory<byte> password)
    {
        if (Path.GetFullPath(backup).Equals(FilePath, StringComparison.OrdinalIgnoreCase)) throw new IOException("请选择其他备份文件，不能将当前保险库恢复到自身。");
        var file = await ReadBoundedAsync(backup).ConfigureAwait(false);
        var envelope = VaultCryptography.Parse(file);
        var key = await VaultCryptography.DeriveKeyAsync(password, envelope.Header).ConfigureAwait(false);
        VaultData? data = null;
        try
        {
            data = VaultCryptography.Decrypt(envelope, key);
            string? retained = null;
            if (Exists)
            {
                retained = FilePath + $".before-restore-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
                await CopyEncryptedAsync(await ReadBoundedAsync(FilePath).ConfigureAwait(false), retained).ConfigureAwait(false);
            }
            await WriteAtomicAsync(file, key, retainedOldFile: false).ConfigureAwait(false);
            return new VaultSession(this, key, envelope.Header, data, SHA256.HashData(file));
        }
        catch { data?.ClearSensitiveReferences(); CryptographicOperations.ZeroMemory(key); throw; }
    }

    internal async Task WriteAtomicAsync(byte[] encrypted, byte[] key, bool retainedOldFile = true)
    {
        var temporary = FilePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await WriteFlushedAsync(temporary, encrypted).ConfigureAwait(false);
            var verified = VaultCryptography.Decrypt(VaultCryptography.Parse(await ReadBoundedAsync(temporary).ConfigureAwait(false)), key);
            verified.ClearSensitiveReferences();
            if (Exists) File.Replace(temporary, FilePath, retainedOldFile ? FilePath + ".previous" : null, ignoreMetadataErrors: false);
            else File.Move(temporary, FilePath, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static async Task<byte[]> ReadBoundedAsync(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        if (stream.Length > VaultCryptography.MaxFileBytes) throw new VaultFormatException("保险库文件超过大小限制。");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes).ConfigureAwait(false);
        return bytes;
    }
    private static async Task WriteFlushedAsync(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }
    internal static async Task CopyEncryptedAsync(byte[] bytes, string destination)
    {
        var target = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await WriteFlushedAsync(temp, bytes).ConfigureAwait(false);
            if (!(await ReadBoundedAsync(temp).ConfigureAwait(false)).AsSpan().SequenceEqual(bytes)) throw new IOException("备份写入校验失败。");
            File.Move(temp, target, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Dispose() => _processLock.Dispose();
}

public sealed class VaultSession : IDisposable
{
    private readonly VaultStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private byte[] _key;
    private VaultHeader _header;
    private byte[]? _fileHash;
    private bool _disposed;
    public VaultData Data { get; private set; }
    internal VaultSession(VaultStore store, byte[] key, VaultHeader header, VaultData data, byte[]? hash)
    { _store = store; _key = key; _header = header; Data = data; _fileHash = hash; }

    private async Task VerifyUnchangedAsync()
    {
        if (_fileHash is null) { if (_store.Exists) throw new VaultConflictException(); return; }
        if (!_store.Exists || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(await VaultStore.ReadBoundedAsync(_store.FilePath).ConfigureAwait(false)), _fileHash))
            throw new VaultConflictException();
    }

    public async Task SaveAsync(VaultData data)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await VerifyUnchangedAsync().ConfigureAwait(false);
            var bytes = VaultCryptography.Encrypt(data, _key, _header);
            await _store.WriteAtomicAsync(bytes, _key).ConfigureAwait(false);
            _fileHash = SHA256.HashData(bytes);
            if (!ReferenceEquals(Data, data)) { Data.ClearSensitiveReferences(); Data = data; }
        }
        finally { _gate.Release(); }
    }

    public async Task BackupAsync(string path)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Path.GetFullPath(path).Equals(_store.FilePath, StringComparison.OrdinalIgnoreCase) || Path.GetFullPath(path).Equals(_store.FilePath + ".previous", StringComparison.OrdinalIgnoreCase))
                throw new IOException("备份目标不能是当前保险库或自动备份文件。");
            await VerifyUnchangedAsync().ConfigureAwait(false);
            await VaultStore.CopyEncryptedAsync(await VaultStore.ReadBoundedAsync(_store.FilePath).ConfigureAwait(false), path).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task ChangePasswordAsync(ReadOnlyMemory<byte> currentPassword, ReadOnlyMemory<byte> newPassword)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        byte[]? replacement = null, check = null;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            VaultCryptography.ValidateNewPassword(newPassword.Span);
            await VerifyUnchangedAsync().ConfigureAwait(false);
            check = await VaultCryptography.DeriveKeyAsync(currentPassword, _header).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(check, _key)) throw new VaultAuthenticationException();
            var header = VaultCryptography.NewHeader();
            replacement = await VaultCryptography.DeriveKeyAsync(newPassword, header).ConfigureAwait(false);
            var file = VaultCryptography.Encrypt(Data, replacement, header);
            await _store.WriteAtomicAsync(file, replacement).ConfigureAwait(false);
            CryptographicOperations.ZeroMemory(_key);
            _key = replacement; replacement = null; _header = header; _fileHash = SHA256.HashData(file);
        }
        finally
        {
            if (check is not null) CryptographicOperations.ZeroMemory(check);
            if (replacement is not null) CryptographicOperations.ZeroMemory(replacement);
            _gate.Release();
        }
    }
    public void Dispose()
    {
        _gate.Wait();
        try { if (_disposed) return; _disposed = true; CryptographicOperations.ZeroMemory(_key); Data.ClearSensitiveReferences(); }
        finally { _gate.Release(); }
    }
}
