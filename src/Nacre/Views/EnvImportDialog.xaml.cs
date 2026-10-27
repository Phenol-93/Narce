using System.Windows;
using Nacre.Core.Models;
using Nacre.Core.Services;
using Nacre.Services;

namespace Nacre.Views;

public sealed class EnvImportRow : ObservableObject
{
    private bool _selected = true;
    private string _value = "";
    public string Name { get; init; } = "";
    public int Line { get; init; }
    public string Description { get; init; } = "";
    public bool Selected { get => _selected; set => Set(ref _selected, value); }
    public string Value { get => _value; set => Set(ref _value, value); }
}

public partial class EnvImportDialog : Window
{
    public List<EnvImportRow> Rows { get; }
    public IEnumerable<EnvEntry> SelectedEntries => Rows.Where(r => r.Selected).Select(r => new EnvEntry(r.Name, r.Value, r.Line));
    public EnvImportDialog(EnvParseResult parsed, IEnumerable<string> existing)
    {
        InitializeComponent();
        var used = existing.ToHashSet(StringComparer.Ordinal);
        var duplicates = parsed.Entries.GroupBy(e => e.Name, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        Rows = parsed.Entries.Select(e => new EnvImportRow { Name = e.Name, Value = e.Value, Line = e.Line, Description = $"{e.Name} · 第 {e.Line} 行" + (used.Contains(e.Name) || duplicates.Contains(e.Name) ? " · 同名变量将另存为新记录" : "") }).ToList();
        RowsList.ItemsSource = Rows;
        Summary.Text = $"可导入 {Rows.Count} 项；解析提示 {parsed.Issues.Count} 项。不会展开变量、执行命令或读取引用文件。";
        Issues.Text = string.Join(Environment.NewLine, parsed.Issues.Select(i => $"第 {i.Line} 行：{i.Message}"));
        Issues.Visibility = parsed.Issues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Closed += (_, _) => { if (DialogResult != true) Clear(); };
    }
    private void ImportClick(object sender, RoutedEventArgs e)
    { if (!Rows.Any(r => r.Selected)) { Dialogs.Error("请至少选择一项。"); return; } DialogResult = true; }
    private void SelectAllClick(object sender, RoutedEventArgs e) { foreach (var row in Rows) row.Selected = SelectAll.IsChecked == true; }
    public void Clear() { foreach (var row in Rows) row.Value = ""; Rows.Clear(); RowsList.ItemsSource = null; }
}
