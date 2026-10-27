using Nacre.Core.Models;

namespace Nacre.Core.Services;

public static class TaxonomyService
{
    public static bool IsTagName(string name) => !string.IsNullOrWhiteSpace(name) && name.IndexOfAny([',', '，', ';', '；', '\r', '\n']) < 0;
    public static List<string> ParseTags(string text) => text.Split([',', '，', ';', '；', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    public static void Register(VaultData data, SavedRecord record)
    {
        record.Category = record.Category.Trim();
        if (record.Category.Length > 0 && !data.Categories.Contains(record.Category, StringComparer.OrdinalIgnoreCase)) data.Categories.Add(record.Category);
        foreach (var tag in record.Tags)
            if (!data.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) data.Tags.Add(tag);
    }
    public static void Rename(VaultData data, bool tags, string oldName, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0) throw new ArgumentException("名称不能为空。");
        if (tags && !IsTagName(newName)) throw new ArgumentException("标签名不能包含分隔符。");
        var list = tags ? data.Tags : data.Categories;
        if (list.Any(x => x.Equals(newName, StringComparison.OrdinalIgnoreCase) && !x.Equals(oldName, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("名称已存在。");
        var index = list.FindIndex(x => x.Equals(oldName, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new ArgumentException("所选名称不存在。");
        list[index] = newName;
        foreach (var record in data.AllRecords())
        {
            if (tags) record.Tags = record.Tags.Select(t => t.Equals(oldName, StringComparison.OrdinalIgnoreCase) ? newName : t).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            else if (record.Category.Equals(oldName, StringComparison.OrdinalIgnoreCase)) record.Category = newName;
        }
    }
    public static void Delete(VaultData data, bool tags, string name)
    {
        (tags ? data.Tags : data.Categories).RemoveAll(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        foreach (var record in data.AllRecords())
        {
            if (tags) record.Tags.RemoveAll(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
            else if (record.Category.Equals(name, StringComparison.OrdinalIgnoreCase)) record.Category = "";
        }
    }
}
