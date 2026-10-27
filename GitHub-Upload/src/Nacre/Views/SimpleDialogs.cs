using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Nacre.Core.Models;
using Nacre.Core.Services;
using Nacre.Services;

namespace Nacre.Views;

public class DialogWindow : Window
{
    protected StackPanel Form { get; } = new() { Margin = new Thickness(24) };
    protected StackPanel Actions { get; } = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 0, 24, 24) };
    protected DialogWindow(string title, double width = 480)
    {
        Title = title; Width = width; SizeToContent = SizeToContent.Height;
        SetResourceReference(StyleProperty, typeof(Window));
        MaxHeight = SystemParameters.WorkArea.Height - 60;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.CanResize;
        var root = new DockPanel();
        DockPanel.SetDock(Actions, Dock.Bottom); root.Children.Add(Actions);
        root.Children.Add(new ScrollViewer { Content = Form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }
    protected static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 14, 0, 6), TextWrapping = TextWrapping.Wrap };
    protected Button Action(string text, Action clicked, bool primary = false)
    {
        var button = new Button { Content = text, MinWidth = 84 };
        if (primary) button.SetResourceReference(StyleProperty, "Primary");
        button.Click += (_, _) => clicked(); Actions.Children.Add(button); return button;
    }
    protected void Cancel() { var b = Action("取消", () => DialogResult = false); b.IsCancel = true; }
}

public sealed class PasswordDialog : DialogWindow
{
    private readonly PasswordBox _password = new() { MaxLength = 1024 };
    private byte[]? _result;
    public PasswordDialog(string title) : base(title)
    {
        Form.Children.Add(Label("输入备份创建时使用的主密码。该密码可能与当前保险库不同。"));
        Form.Children.Add(_password);
        Cancel();
        var accept = Action("验证并恢复", () =>
        {
            using var secure = _password.SecurePassword;
            if (secure.Length == 0) { Dialogs.Error("请输入主密码。"); return; }
            _result = PasswordBytes.FromSecureString(secure); DialogResult = true;
        }, true); accept.IsDefault = true;
        Loaded += (_, _) => _password.Focus();
        Closed += (_, _) => { _password.Clear(); if (DialogResult != true && _result is not null) CryptographicOperations.ZeroMemory(_result); };
    }
    public byte[] TakePassword() { var result = _result ?? []; _result = null; return result; }
}

public sealed class ChangePasswordDialog : DialogWindow
{
    private readonly PasswordBox _current = new() { MaxLength = 1024 }, _next = new() { MaxLength = 1024 }, _confirm = new() { MaxLength = 1024 };
    private byte[]? _currentBytes, _newBytes;
    public ChangePasswordDialog() : base("修改主密码")
    {
        Form.Children.Add(Label("当前主密码")); Form.Children.Add(_current);
        Form.Children.Add(Label($"新主密码（至少 {VaultCryptography.MinimumPasswordLength} 个字符）")); Form.Children.Add(_next);
        Form.Children.Add(Label("再次输入新主密码")); Form.Children.Add(_confirm);
        Form.Children.Add(Label("忘记新密码无法恢复。旧备份仍需旧密码，请在修改后重新备份。"));
        Cancel();
        Action("重新加密并保存", () =>
        {
            using var current = _current.SecurePassword; using var next = _next.SecurePassword; using var confirm = _confirm.SecurePassword;
            if (current.Length == 0 || next.Length < VaultCryptography.MinimumPasswordLength) { Dialogs.Error($"请输入当前密码，新密码至少需要 {VaultCryptography.MinimumPasswordLength} 个字符。"); return; }
            var nextBytes = PasswordBytes.FromSecureString(next); var other = PasswordBytes.FromSecureString(confirm);
            var equal = CryptographicOperations.FixedTimeEquals(nextBytes, other); CryptographicOperations.ZeroMemory(other);
            if (!equal) { CryptographicOperations.ZeroMemory(nextBytes); Dialogs.Error("两次输入的新密码不一致。"); return; }
            _currentBytes = PasswordBytes.FromSecureString(current); _newBytes = nextBytes; DialogResult = true;
        }, true);
        Closed += (_, _) => { _current.Clear(); _next.Clear(); _confirm.Clear(); };
    }
    public (byte[] Current, byte[] New) TakePasswords()
    { var result = (_currentBytes ?? [], _newBytes ?? []); _currentBytes = null; _newBytes = null; return result; }
}

public sealed class TaxonomyDialog : DialogWindow
{
    private readonly VaultData _data;
    private readonly ComboBox _type = new() { ItemsSource = new[] { "分类", "标签" }, SelectedIndex = 0 };
    private readonly ListBox _list = new() { Height = 230 };
    private readonly TextBox _name = new();
    private bool IsTag => _type.SelectedIndex == 1;
    private List<string> Names => IsTag ? _data.Tags : _data.Categories;
    public TaxonomyDialog(VaultData data) : base("管理分类与标签", 560)
    {
        _data = data;
        Form.Children.Add(_type); Form.Children.Add(_list);
        Form.Children.Add(Label("名称（选择已有名称后可重命名或删除）")); Form.Children.Add(_name);
        var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) }; Form.Children.Add(row);
        void AddButton(string title, Action action) { var b = new Button { Content = title }; b.Click += (_, _) => { try { action(); Refresh(); } catch (ArgumentException e) { Dialogs.Error(e); } }; row.Children.Add(b); }
        AddButton("新建", () =>
        {
            var name = _name.Text.Trim();
            if (name.Length == 0) throw new ArgumentException("名称不能为空。");
            if (IsTag && !TaxonomyService.IsTagName(name)) throw new ArgumentException("标签名不能包含逗号、分号或换行。");
            if (Names.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("名称已存在。");
            Names.Add(name);
        });
        AddButton("重命名", () => { if (_list.SelectedItem is string old) TaxonomyService.Rename(_data, IsTag, old, _name.Text); else throw new ArgumentException("请先选择一个名称。"); });
        AddButton("删除", () =>
        {
            if (_list.SelectedItem is not string name) throw new ArgumentException("请先选择一个名称。");
            if (Dialogs.Confirm("删除此名称并从所有密钥与文本记录中移除关联？记录本身会保留。", "删除分类 / 标签"))
                TaxonomyService.Delete(_data, IsTag, name);
        });
        _type.SelectionChanged += (_, _) => Refresh();
        _list.SelectionChanged += (_, _) => { if (_list.SelectedItem is string name) _name.Text = name; };
        Form.Children.Add(Label("分类和标签由两个模块共用。点击“保存更改”后生效。"));
        Cancel(); Action("保存更改", () => DialogResult = true, true); Refresh();
    }
    private void Refresh() { _list.ItemsSource = Names.Order(StringComparer.CurrentCultureIgnoreCase).ToArray(); _name.Text = ""; }
}

public sealed class LicenseDialog : DialogWindow
{
    public LicenseDialog() : base("Nacre · 开源许可证", 660)
    {
        using var stream = typeof(LicenseDialog).Assembly.GetManifestResourceStream("Nacre.LICENSE");
        using var reader = stream is null ? null : new StreamReader(stream);
        Form.Children.Add(new TextBox { Text = reader?.ReadToEnd() ?? "MIT License — 请参阅程序旁的 LICENSE 文件。", IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 370, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 12 });
        Form.Children.Add(Label("密码派生使用 Konscious.Security.Cryptography.Argon2（MIT）。依赖许可证和来源见 THIRD-PARTY-NOTICES.md。"));
        Action("关闭", () => Close());
    }
}
