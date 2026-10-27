using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nacre.Core.Models;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class CredentialField : ObservableObject
{
    private string _name = "", _value = "", _environmentVariable = "";
    private bool _isSensitive = true;
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Value { get => _value; set => Set(ref _value, value); }
    public bool IsSensitive { get => _isSensitive; set => Set(ref _isSensitive, value); }
    public string EnvironmentVariable { get => _environmentVariable; set => Set(ref _environmentVariable, value); }
}

public abstract class SavedRecord : ObservableObject
{
    private string _title = "", _category = "", _notes = "";
    private bool _favorite;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Category { get => _category; set => Set(ref _category, value); }
    public List<string> Tags { get; set; } = [];
    public string Notes { get => _notes; set => Set(ref _notes, value); }
    public bool Favorite { get => _favorite; set => Set(ref _favorite, value); }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CredentialRecord : SavedRecord
{
    private string _baseUrl = "", _environmentVariable = "", _usage = "";
    private DateTime? _expiresOn;
    public ObservableCollection<CredentialField> Fields { get; set; } = [];
    public string BaseUrl { get => _baseUrl; set => Set(ref _baseUrl, value); }
    public string EnvironmentVariable { get => _environmentVariable; set => Set(ref _environmentVariable, value); }
    public List<string> Links { get; set; } = [];
    public string Usage { get => _usage; set => Set(ref _usage, value); }
    public DateTime? ExpiresOn { get => _expiresOn; set => Set(ref _expiresOn, value); }
}

public enum TextKind { Prompt, Code }

public sealed class TextRecord : SavedRecord
{
    private string _body = "";
    private TextKind _kind;
    public string Body { get => _body; set => Set(ref _body, value); }
    public TextKind Kind { get => _kind; set => Set(ref _kind, value); }
}

public sealed class Preferences : ObservableObject
{
    private string _theme = "System";
    private int _autoLockMinutes = 5, _clipboardSeconds = 30;
    public string Theme { get => _theme; set => Set(ref _theme, value); }
    public int AutoLockMinutes { get => _autoLockMinutes; set => Set(ref _autoLockMinutes, value); }
    public int ClipboardSeconds { get => _clipboardSeconds; set => Set(ref _clipboardSeconds, value); }
}

public sealed class VaultData
{
    public int SchemaVersion { get; set; } = 1;
    public List<CredentialRecord> Credentials { get; set; } = [];
    public List<TextRecord> Texts { get; set; } = [];
    public List<string> Categories { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public Preferences Settings { get; set; } = new();

    public IEnumerable<SavedRecord> AllRecords() => Credentials.Cast<SavedRecord>().Concat(Texts);

    public void ClearSensitiveReferences()
    {
        foreach (var record in Credentials)
        {
            foreach (var field in record.Fields) { field.Value = ""; field.Name = ""; field.EnvironmentVariable = ""; }
            record.Fields.Clear(); record.BaseUrl = ""; record.EnvironmentVariable = "";
            record.Usage = ""; record.Links.Clear();
        }
        foreach (var record in Texts) record.Body = "";
        foreach (var record in AllRecords())
        { record.Title = ""; record.Category = ""; record.Notes = ""; record.Tags.Clear(); }
        Credentials.Clear(); Texts.Clear(); Categories.Clear(); Tags.Clear();
    }
}

public static class VaultJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };
    public static T Clone<T>(T source)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(source, Options);
        try { return JsonSerializer.Deserialize<T>(bytes, Options)!; }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
    }
}
