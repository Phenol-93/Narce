using System.Security.Cryptography;
using System.Text;
using Nacre.Core.Models;
using Nacre.Core.Services;

namespace Nacre.Tests;

public sealed class VaultStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Nacre-tests", Guid.NewGuid().ToString("N"));
    private string VaultPath => Path.Combine(_directory, "vault.nacre");
    public VaultStoreTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task CrudPersistsAcrossLockAndRestartAndBackupsStayEncrypted()
    {
        var password = Fixtures.Password();
        var backup = Path.Combine(_directory, "backup.nacre");
        var id = Guid.Empty;
        using (var store = new VaultStore(VaultPath))
        {
            using (var session = await store.CreateAsync(password))
            {
                await session.SaveAsync(Fixtures.Data());
                var edit = VaultJson.Clone(session.Data);
                id = edit.Credentials.Single().Id;
                edit.Credentials.Single().Title = "Edited fictional record";
                edit.Credentials.Single().Fields.Add(new CredentialField { Name = "CLIENT_ID", Value = "fictional-public-id", IsSensitive = false });
                await session.SaveAsync(edit);
                await session.BackupAsync(backup);
                Assert.DoesNotContain("fictional-key-for-tests", await File.ReadAllTextAsync(backup));
            }
            using (var session = await store.UnlockAsync(password))
            {
                Assert.Equal(id, session.Data.Credentials.Single().Id);
                Assert.Equal("Edited fictional record", session.Data.Credentials.Single().Title);
                Assert.Equal(2, session.Data.Credentials.Single().Fields.Count);
                var deletion = VaultJson.Clone(session.Data);
                deletion.Credentials.Clear();
                await session.SaveAsync(deletion);
            }
        }
        using (var reopened = new VaultStore(VaultPath))
        {
            using (var session = await reopened.UnlockAsync(password)) Assert.Empty(session.Data.Credentials);
            using (var restored = await reopened.RestoreAsync(backup, password))
            {
                Assert.Equal(id, restored.Data.Credentials.Single().Id);
                Assert.Single(Directory.GetFiles(_directory, "*.before-restore-*"));
            }
            using var final = await reopened.UnlockAsync(password);
            Assert.Equal(2, final.Data.Credentials.Single().Fields.Count);
        }
        Assert.True(File.Exists(VaultPath + ".previous"));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        CryptographicOperations.ZeroMemory(password);
    }

    [Fact]
    public async Task WrongPasswordAndInvalidRestoreNeverOverwriteCurrentData()
    {
        using var store = new VaultStore(VaultPath);
        using var session = await store.CreateAsync(Fixtures.Password());
        await session.SaveAsync(Fixtures.Data());
        var before = await File.ReadAllBytesAsync(VaultPath);
        await Assert.ThrowsAsync<VaultAuthenticationException>(() => store.UnlockAsync(Encoding.UTF8.GetBytes("fictional-wrong-password")));
        var backup = Path.Combine(_directory, "backup.nacre"); await session.BackupAsync(backup);
        await Assert.ThrowsAsync<VaultAuthenticationException>(() => store.RestoreAsync(backup, Encoding.UTF8.GetBytes("another-fictional-password")));
        Assert.Equal(before, await File.ReadAllBytesAsync(VaultPath));
        await File.WriteAllTextAsync(backup, "corrupted backup");
        await Assert.ThrowsAsync<VaultFormatException>(() => store.RestoreAsync(backup, Fixtures.Password()));
        Assert.Equal(before, await File.ReadAllBytesAsync(VaultPath));
    }

    [Fact]
    public async Task PasswordChangeUsesNewSaltAndLeavesOldPasswordOnlyOnOldBackup()
    {
        using var store = new VaultStore(VaultPath);
        using var session = await store.CreateAsync(Fixtures.Password());
        await session.SaveAsync(Fixtures.Data());
        var before = VaultCryptography.Parse(await File.ReadAllBytesAsync(VaultPath));
        var next = Encoding.UTF8.GetBytes("fictional-new-master-password");
        await Assert.ThrowsAsync<VaultAuthenticationException>(() => session.ChangePasswordAsync(Encoding.UTF8.GetBytes("fictional-wrong-password"), next));
        await session.ChangePasswordAsync(Fixtures.Password(), next);
        var after = VaultCryptography.Parse(await File.ReadAllBytesAsync(VaultPath));
        Assert.NotEqual(Convert.ToHexString(before.Header.Salt), Convert.ToHexString(after.Header.Salt));
        await Assert.ThrowsAsync<VaultAuthenticationException>(() => store.UnlockAsync(Fixtures.Password()));
        using var unlocked = await store.UnlockAsync(next);
        Assert.Equal("fictional-key-for-tests", unlocked.Data.Credentials.Single().Fields.Single().Value);
        var previousStore = new VaultStore(VaultPath + ".previous");
        using (previousStore) using (var previous = await previousStore.UnlockAsync(Fixtures.Password())) Assert.Single(previous.Data.Credentials);
        CryptographicOperations.ZeroMemory(next);
    }

    [Fact]
    public async Task PreviousFileRecoversCorruptionAndExternalEditsAreDetected()
    {
        using var store = new VaultStore(VaultPath);
        using var session = await store.CreateAsync(Fixtures.Password());
        await session.SaveAsync(Fixtures.Data());
        await session.SaveAsync(VaultJson.Clone(session.Data));
        await File.WriteAllTextAsync(VaultPath, "damaged");
        await Assert.ThrowsAsync<VaultConflictException>(() => session.SaveAsync(VaultJson.Clone(session.Data)));
        await Assert.ThrowsAsync<VaultFormatException>(() => store.UnlockAsync(Fixtures.Password()));
        using var recovery = await store.RestoreAsync(VaultPath + ".previous", Fixtures.Password());
        Assert.Single(recovery.Data.Credentials);
        Assert.Equal("damaged", await File.ReadAllTextAsync(Directory.GetFiles(_directory, "*.before-restore-*").Single()));
    }

    [Fact]
    public async Task WriteFailureLeavesVaultIntactAndProcessLockPreventsSecondWriter()
    {
        using var store = new VaultStore(VaultPath);
        using var session = await store.CreateAsync(Fixtures.Password());
        Assert.Throws<IOException>(() => new VaultStore(VaultPath));
        var before = await File.ReadAllBytesAsync(VaultPath);
        using (var locked = new FileStream(VaultPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAnyAsync<IOException>(() => session.SaveAsync(Fixtures.Data()));
        Assert.Equal(before, await File.ReadAllBytesAsync(VaultPath));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        await Assert.ThrowsAsync<IOException>(() => session.BackupAsync(VaultPath));
        await Assert.ThrowsAsync<IOException>(() => store.RestoreAsync(VaultPath, Fixtures.Password()));
    }

    [Fact]
    public async Task LockDropsControlledDecryptedReferences()
    {
        using var store = new VaultStore(VaultPath);
        var session = await store.CreateAsync(Fixtures.Password());
        await session.SaveAsync(Fixtures.Data());
        var data = session.Data;
        var field = data.Credentials.Single().Fields.Single();
        var text = data.Texts.Single();
        session.Dispose();
        Assert.Empty(field.Value); Assert.Empty(text.Body); Assert.Empty(data.Credentials); Assert.Empty(data.Texts); Assert.Empty(data.Categories);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.SaveAsync(Fixtures.Data()));
    }

    [Fact]
    public async Task SixCharacterPasswordsSupportCreationChangeAndReopen()
    {
        var initial = Encoding.UTF8.GetBytes("fake06");
        var next = Encoding.UTF8.GetBytes("fake07");
        try
        {
            using var store = new VaultStore(VaultPath);
            using (var session = await store.CreateAsync(initial))
            {
                await session.SaveAsync(Fixtures.Data());
                await session.ChangePasswordAsync(initial, next);
            }
            await Assert.ThrowsAsync<VaultAuthenticationException>(() => store.UnlockAsync(initial));
            using var reopened = await store.UnlockAsync(next);
            Assert.Single(reopened.Data.Credentials);
        }
        finally { CryptographicOperations.ZeroMemory(initial); CryptographicOperations.ZeroMemory(next); }
    }

    [Fact]
    public async Task ReadOnlyDestinationNeverDestroysTheExistingVault()
    {
        using var store = new VaultStore(VaultPath);
        using var session = await store.CreateAsync(Fixtures.Password());
        var before = await File.ReadAllBytesAsync(VaultPath);
        File.SetAttributes(VaultPath, FileAttributes.ReadOnly);
        try { await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.SaveAsync(Fixtures.Data())); }
        finally { File.SetAttributes(VaultPath, FileAttributes.Normal); }
        Assert.Equal(before, await File.ReadAllBytesAsync(VaultPath));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void StorageModeNeverFallsBackFromPortable()
    {
        Assert.False(StorageLocation.Detect(_directory, _directory).IsPortable);
        File.WriteAllText(Path.Combine(_directory, "portable.flag"), "");
        var portable = StorageLocation.Detect(_directory, "unused");
        Assert.True(portable.IsPortable); Assert.Equal(Path.Combine(_directory, "Data"), portable.DirectoryPath);
        var blocked = Path.Combine(_directory, "not-directory"); File.WriteAllText(blocked, "");
        Assert.ThrowsAny<IOException>(() => new VaultStore(Path.Combine(blocked, "vault.nacre")));
    }

    public void Dispose()
    {
        // This path is exclusively a random test directory created by this fixture.
        Directory.Delete(_directory, true);
    }
}
