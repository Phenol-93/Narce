using System.Text;
using System.Text.Json;

namespace Nacre.Core.Services;

public sealed record EnvEntry(string Name, string Value, int Line);
public sealed record EnvIssue(int Line, string Message);
public sealed record EnvParseResult(IReadOnlyList<EnvEntry> Entries, IReadOnlyList<EnvIssue> Issues);

/// <summary>Conservative dotenv parser. No interpolation, expansion, execution or file loading.</summary>
public static class EnvFile
{
    public static EnvParseResult Parse(string input)
    {
        input = input.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n');
        var entries = new List<EnvEntry>();
        var issues = new List<EnvIssue>();
        var i = 0;
        var line = 1;
        while (i < input.Length)
        {
            SkipSpaces(input, ref i);
            if (i == input.Length) break;
            if (input[i] == '\n') { i++; line++; continue; }
            if (input[i] == '#') { SkipLine(input, ref i); continue; }
            var startLine = line;
            if (input.AsSpan(i).StartsWith("export ") || input.AsSpan(i).StartsWith("export\t")) { i += 6; SkipSpaces(input, ref i); }
            var keyStart = i;
            while (i < input.Length && input[i] is not '=' and not '\n') i++;
            if (i == input.Length || input[i] != '=') { issues.Add(new(startLine, "缺少等号，已跳过该行。")); SkipLine(input, ref i); continue; }
            var name = input[keyStart..i].Trim(); i++;
            if (!VaultValidation.IsEnvironmentName(name)) { issues.Add(new(startLine, "环境变量名称无效，已跳过该行。")); SkipLine(input, ref i); continue; }
            SkipSpaces(input, ref i);
            string value;
            if (i < input.Length && input[i] is '\'' or '"')
            {
                var quote = input[i++];
                var builder = new StringBuilder();
                var closed = false;
                while (i < input.Length)
                {
                    var c = input[i++];
                    if (c == quote) { closed = true; break; }
                    if (c == '\n') line++;
                    if (c == '\\' && quote == '"' && i < input.Length)
                    {
                        var escaped = input[i++];
                        switch (escaped)
                        {
                            case 'n': builder.Append('\n'); break;
                            case 'r': builder.Append('\r'); break;
                            case 't': builder.Append('\t'); break;
                            case '"': builder.Append('"'); break;
                            case '\\': builder.Append('\\'); break;
                            case '$': builder.Append('$'); break;
                            case '\n': line++; break;
                            default: builder.Append('\\').Append(escaped); break;
                        }
                    }
                    else builder.Append(c);
                }
                if (!closed) { issues.Add(new(startLine, "引号未闭合，已跳过该项及未闭合内容。")); break; }
                value = builder.ToString(); builder.Clear();
                SkipSpaces(input, ref i);
                if (i < input.Length && input[i] != '\n' && input[i] != '#')
                { issues.Add(new(startLine, "引号结束后存在多余内容，已跳过该项。")); SkipLine(input, ref i); continue; }
                SkipLine(input, ref i);
            }
            else
            {
                var begin = i;
                while (i < input.Length && input[i] != '\n') i++;
                value = input[begin..i];
                for (var j = 0; j < value.Length; j++)
                    if (value[j] == '#' && (j == 0 || char.IsWhiteSpace(value[j - 1]))) { value = value[..j]; break; }
                value = value.TrimEnd();
            }
            entries.Add(new(name, value, startLine));
        }
        return new(entries, issues);
    }

    public static string Export(IEnumerable<KeyValuePair<string, string>> fields, bool json)
    {
        var pairs = fields.ToArray();
        if (pairs.Length == 0) throw new ArgumentException("请至少选择一个字段。");
        if (pairs.Any(p => string.IsNullOrWhiteSpace(p.Key))) throw new ArgumentException("请填写每个输出字段的名称。");
        if (pairs.Select(p => p.Key).Distinct(StringComparer.Ordinal).Count() != pairs.Length) throw new ArgumentException("输出字段名重复，请修改后重试。");
        if (json) return JsonSerializer.Serialize(pairs.ToDictionary(p => p.Key, p => p.Value), new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        if (pairs.Any(p => !VaultValidation.IsEnvironmentName(p.Key))) throw new ArgumentException(".env 字段名必须为合法的环境变量名称。");
        return string.Join(Environment.NewLine, pairs.Select(p => p.Key + "=\"" + Escape(p.Value) + "\"")) + Environment.NewLine;
    }
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t").Replace("$", "\\$");
    private static void SkipSpaces(string s, ref int i) { while (i < s.Length && s[i] is ' ' or '\t') i++; }
    private static void SkipLine(string s, ref int i) { while (i < s.Length && s[i] != '\n') i++; }
}
