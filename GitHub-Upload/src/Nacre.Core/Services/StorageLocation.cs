namespace Nacre.Core.Services;

public sealed record StorageLocation(string DirectoryPath, bool IsPortable)
{
    public string VaultPath => Path.Combine(DirectoryPath, "vault.nacre");
    public static StorageLocation Detect(string? executableDirectory = null, string? localAppData = null)
    {
        var directory = executableDirectory ?? AppContext.BaseDirectory;
        return File.Exists(Path.Combine(directory, "portable.flag"))
            ? new(Path.Combine(directory, "Data"), true)
            : new(Path.Combine(localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nacre"), false);
    }
}
