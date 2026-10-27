using System.Security.Cryptography;
using System.Text.Json;
using Nacre.Core.Models;
using Nacre.Core.Services;

namespace Nacre.ViewModels;

public sealed class RecordEditor : ObservableObject
{
    private string _tagsText, _linksText;
    private readonly byte[] _baseline;
    private readonly bool _forceDirty;
    public SavedRecord Record { get; }
    public CredentialRecord? Credential => Record as CredentialRecord;
    public TextRecord? Text => Record as TextRecord;
    public bool IsCredential => Credential is not null;
    public bool IsNew { get; }
    public string TagsText { get => _tagsText; set => Set(ref _tagsText, value); }
    public string LinksText { get => _linksText; set => Set(ref _linksText, value); }
    public string TimeDescription => $"创建：{Record.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}    修改：{Record.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm}";
    public RecordEditor(SavedRecord record, bool isNew = false, bool forceDirty = false)
    {
        Record = record is CredentialRecord key ? VaultJson.Clone(key) : VaultJson.Clone((TextRecord)record);
        IsNew = isNew; _forceDirty = forceDirty;
        _tagsText = string.Join(", ", Record.Tags);
        _linksText = string.Join(Environment.NewLine, Credential?.Links ?? []);
        _baseline = Fingerprint();
    }
    public SavedRecord ToRecord()
    {
        SavedRecord result = Credential is not null ? VaultJson.Clone(Credential) : VaultJson.Clone(Text!);
        result.Title = result.Title.Trim(); result.Category = result.Category.Trim();
        result.Tags = TaxonomyService.ParseTags(TagsText);
        if (result is CredentialRecord key)
        {
            key.Links = LinksText.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
            key.BaseUrl = key.BaseUrl.Trim(); key.EnvironmentVariable = key.EnvironmentVariable.Trim();
            foreach (var field in key.Fields) { field.Name = field.Name.Trim(); field.EnvironmentVariable = field.EnvironmentVariable.Trim(); }
        }
        return result;
    }
    private byte[] Fingerprint()
    {
        var record = ToRecord();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, record.GetType(), VaultJson.Options);
        try { return SHA256.HashData(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); ClearRecord(record); }
    }
    public bool IsDirty
    {
        get
        {
            if (_forceDirty) return true;
            var current = Fingerprint();
            try { return !CryptographicOperations.FixedTimeEquals(current, _baseline); }
            finally { CryptographicOperations.ZeroMemory(current); }
        }
    }
    public void Clear()
    { _tagsText = ""; _linksText = ""; ClearRecord(Record); CryptographicOperations.ZeroMemory(_baseline); }
    public static void ClearRecord(SavedRecord record)
    {
        if (record is CredentialRecord key)
        { foreach (var field in key.Fields) { field.Value = ""; field.Name = ""; field.EnvironmentVariable = ""; } key.Fields.Clear(); key.Links.Clear(); key.BaseUrl = ""; key.EnvironmentVariable = ""; key.Usage = ""; }
        if (record is TextRecord text) text.Body = "";
        record.Title = ""; record.Category = ""; record.Notes = ""; record.Tags.Clear();
    }
}
