using System.Windows;
using Nacre.Core.Models;
using Nacre.Core.Services;
using Nacre.Services;

namespace Nacre.Views;

public sealed class ConfigRow : ObservableObject
{
    private string _name = "";
    private bool _selected = true;
    public string Label { get; init; } = "";
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Value { get; set; } = "";
    public bool Selected { get => _selected; set => Set(ref _selected, value); }
}

public partial class ConfigDialog : Window
{
    private readonly List<ConfigRow> _rows;
    public string? Output { get; private set; }
    public ConfigDialog(CredentialRecord record)
    {
        InitializeComponent();
        _rows = record.Fields.Select((f, i) => new ConfigRow { Label = f.Name + (f.IsSensitive ? " · 敏感" : ""), Name = f.EnvironmentVariable.Length > 0 ? f.EnvironmentVariable : i == 0 && record.EnvironmentVariable.Length > 0 ? record.EnvironmentVariable : f.Name, Value = f.Value }).ToList();
        if (record.BaseUrl.Length > 0) _rows.Add(new ConfigRow { Label = "Base URL", Name = "BASE_URL", Value = record.BaseUrl, Selected = false });
        RowsList.ItemsSource = _rows;
        Closed += (_, _) => { foreach (var row in _rows) row.Value = ""; _rows.Clear(); RowsList.ItemsSource = null; if (DialogResult != true) ClearOutput(); };
    }
    private void CopyClick(object sender, RoutedEventArgs e)
    {
        try { Output = EnvFile.Export(_rows.Where(r => r.Selected).Select(r => new KeyValuePair<string, string>(r.Name.Trim(), r.Value)), Format.SelectedIndex == 1); DialogResult = true; }
        catch (ArgumentException error) { Dialogs.Error(error); }
    }
    public void ClearOutput() => Output = null;
}
