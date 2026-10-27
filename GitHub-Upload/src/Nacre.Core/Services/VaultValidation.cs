using System.Text.RegularExpressions;
using Nacre.Core.Models;

namespace Nacre.Core.Services;

public static partial class VaultValidation
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex EnvironmentNamePattern();
    public static bool IsEnvironmentName(string name) => EnvironmentNamePattern().IsMatch(name);
    public static bool IsWebLink(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && !string.IsNullOrEmpty(uri.Host);

    public static string? RecordError(SavedRecord record)
    {
        if (record is null || record.Category is null || record.Notes is null || record.Tags is null || record.Tags.Any(t => t is null || !TaxonomyService.IsTagName(t)))
            return "记录的分类、标签或备注格式无效。";
        if (string.IsNullOrWhiteSpace(record.Title)) return "请填写名称或标题。";
        if (record.Title.Length > 300) return "名称或标题不能超过 300 个字符。";
        if (record is CredentialRecord key)
        {
            if (key.Fields is null || key.Links is null || key.BaseUrl is null || key.EnvironmentVariable is null || key.Usage is null
                || key.Fields.Any(f => f is null || f.Name is null || f.Value is null || f.EnvironmentVariable is null) || key.Links.Any(l => l is null))
                return "凭据字段或链接数据格式无效。";
            if (key.Fields.Count == 0) return "请至少添加一个凭据字段。";
            if (key.Fields.Any(f => string.IsNullOrWhiteSpace(f.Name))) return "每个凭据字段都需要名称。";
            if (key.Fields.Select(f => f.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != key.Fields.Count)
                return "同一记录内的字段名称不能重复。";
            if (key.BaseUrl.Length != 0 && !IsWebLink(key.BaseUrl)) return "Base URL 必须为完整的 http 或 https 地址。";
            if (key.Links.Any(l => !IsWebLink(l))) return "外部链接必须为完整的 http 或 https 地址（每行一个）。";
            if (key.EnvironmentVariable.Length > 0 && !IsEnvironmentName(key.EnvironmentVariable)) return "环境变量名只能包含英文字母、数字和下划线，且不能以数字开头。";
            if (key.Fields.Any(f => f.EnvironmentVariable.Length > 0 && !IsEnvironmentName(f.EnvironmentVariable)))
                return "字段的环境变量名无效。";
        }
        if (record is TextRecord text && (text.Body is null || !Enum.IsDefined(text.Kind))) return "文本内容或类型无效。";
        return null;
    }

    public static void ValidatePayload(VaultData data)
    {
        if (data.SchemaVersion != 1) throw new VaultFormatException("数据版本不受支持，请使用兼容版本的 Nacre。");
        if (data.Credentials is null || data.Texts is null || data.Categories is null || data.Tags is null || data.Settings is null)
            throw new VaultFormatException("保险库缺少必要的数据。");
        if (data.AllRecords().Any(r => r is null || r.Tags is null) || data.Credentials.Any(c => c.Fields is null || c.Links is null || c.Fields.Any(f => f is null)))
            throw new VaultFormatException("保险库包含无效记录。");
        if (data.Categories.Any(c => string.IsNullOrWhiteSpace(c)) || data.Tags.Any(t => !TaxonomyService.IsTagName(t)))
            throw new VaultFormatException("保险库的分类或标签名称无效。");
        if (data.AllRecords().Select(r => r.Id).Distinct().Count() != data.AllRecords().Count())
            throw new VaultFormatException("保险库包含重复的记录标识。");
        if (data.AllRecords().Any(r => RecordError(r) is not null) || data.Texts.Any(t => !Enum.IsDefined(t.Kind)))
            throw new VaultFormatException("保险库包含无效的记录内容。");
        if (data.Settings.Theme is not ("System" or "Light" or "Dark") || data.Settings.AutoLockMinutes is < 0 or > 1440 || data.Settings.ClipboardSeconds is < 0 or > 3600)
            throw new VaultFormatException("保险库设置无效。");
    }
}

public sealed class VaultFormatException(string message) : Exception(message);
public sealed class VaultAuthenticationException() : Exception("主密码不正确，或保险库已损坏、被篡改。请检查密码，或从加密备份恢复。");
public sealed class VaultConflictException() : IOException("数据文件已在其他位置被修改。为避免覆盖，请锁定后重新解锁，或备份当前数据后恢复。");
