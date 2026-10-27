using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Nacre.Core.Models;
using Nacre.Core.Services;
using Nacre.Infrastructure;
using Nacre.Services;
using Nacre.Views;

namespace Nacre.ViewModels;

public sealed record Choice<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private VaultStore? _store;
    private VaultSession? _session;
    private readonly ThemeService _theme = new();
    private readonly SensitiveClipboard _clipboard = new();
    private readonly Func<long?> _observeNativeInput;
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private long _lastActivity = Environment.TickCount64;
    private bool _busy, _unlocked, _isFirstRun, _refreshing;
    private int _storageWork;
    public static readonly Choice<string?> AllCategories = new("全部分类", null), AllTags = new("全部标签", null);
    private string _page = "Credentials", _search = "", _sort = "最近修改", _kindFilter = "全部类型", _status = "本地保险库 · 完全离线";
    private Choice<string?> _category = AllCategories, _tag = AllTags;
    private bool _onlyFavorites;
    private SavedRecord? _selected;
    private RecordEditor? _editor;
    private Preferences? _settings;
    public StorageLocation Storage { get; }
    public string StorageDescription => (Storage.IsPortable ? "便携模式" : "标准模式") + " · " + Storage.DirectoryPath;
    public string Version => "Nacre " + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
    public bool IsBusy { get => _busy; private set { Set(ref _busy, value); OnPropertyChanged(nameof(IsInteractive)); RaiseCommands(); } }
    public bool IsInteractive => !IsBusy;
    public bool IsUnlocked { get => _unlocked; private set => Set(ref _unlocked, value); }
    public bool IsFirstRun { get => _isFirstRun; private set { Set(ref _isFirstRun, value); OnPropertyChanged(nameof(UnlockTitle)); OnPropertyChanged(nameof(UnlockDescription)); } }
    public string UnlockTitle => IsFirstRun ? "创建你的本地保险库" : "欢迎回来";
    public string UnlockDescription => IsFirstRun ? "设置主密码，开始安全地保存开发凭据。" : "输入主密码，解锁你的密钥与文本收藏。";
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Page { get => _page; private set { Set(ref _page, value); OnPropertyChanged(nameof(PageTitle)); OnPropertyChanged(nameof(IsRecordsPage)); OnPropertyChanged(nameof(IsTextPage)); OnPropertyChanged(nameof(AvailableSorts)); } }
    public string PageTitle => Page switch { "Texts" => "文本收藏", "Settings" => "设置", _ => "密钥库" };
    public bool IsRecordsPage => Page != "Settings";
    public bool IsTextPage => Page == "Texts";
    public string Search { get => _search; set { if (Set(ref _search, value)) RefreshList(); } }
    public Choice<string?> CategoryFilter
    {
        get => _category;
        set
        {
            if (_refreshing) return;
            if (value is null) { _ = Application.Current.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(CategoryFilter))); return; }
            if (Set(ref _category, value)) RefreshList();
        }
    }
    public Choice<string?> TagFilter
    {
        get => _tag;
        set
        {
            if (_refreshing) return;
            if (value is null) { _ = Application.Current.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(TagFilter))); return; }
            if (Set(ref _tag, value)) RefreshList();
        }
    }
    public string Sort { get => _sort; set { if (Set(ref _sort, value)) RefreshList(); } }
    public string KindFilter { get => _kindFilter; set { if (Set(ref _kindFilter, value)) RefreshList(); } }
    public bool OnlyFavorites { get => _onlyFavorites; set { if (Set(ref _onlyFavorites, value)) RefreshList(); } }
    public ObservableCollection<SavedRecord> VisibleRecords { get; } = [];
    public ObservableCollection<string> Categories { get; } = [];
    public ObservableCollection<string> Tags { get; } = [];
    public ObservableCollection<Choice<string?>> CategoryFilters { get; } = [AllCategories];
    public ObservableCollection<Choice<string?>> TagFilters { get; } = [AllTags];
    public string[] Sorts { get; } = ["最近修改", "最近创建", "名称 A → Z", "名称 Z → A", "有效期最近"];
    public IEnumerable<string> AvailableSorts => IsTextPage ? Sorts.Take(4) : Sorts;
    public string[] KindFilters { get; } = ["全部类型", "Prompt 提示词", "代码 / 配置片段"];
    public string ListStatus => VisibleRecords.Count == 0 ? (HasFilters ? "没有符合条件的记录。" : "还没有记录，点击“新增”开始。") : $"{VisibleRecords.Count} 条记录";
    public bool HasRecords => VisibleRecords.Count > 0;
    private bool HasFilters => Search.Length > 0 || CategoryFilter.Value is not null || TagFilter.Value is not null || OnlyFavorites || KindFilter != "全部类型";
    public SavedRecord? SelectedRecord
    {
        get => _selected;
        set
        {
            if (_refreshing || value is null || value.Id == _selected?.Id || IsBusy) return;
            _ = SelectAsync(value);
        }
    }
    public RecordEditor? Editor { get => _editor; private set { Set(ref _editor, value); OnPropertyChanged(nameof(HasEditor)); RaiseCommands(); } }
    public bool HasEditor => Editor is not null;
    public Preferences? Settings { get => _settings; private set => Set(ref _settings, value); }
    public Choice<string>[] Themes { get; } = [new("跟随系统", "System"), new("浅色", "Light"), new("深色", "Dark")];
    public Choice<int>[] AutoLocks { get; } = [new("1 分钟", 1), new("5 分钟", 5), new("10 分钟", 10), new("15 分钟", 15), new("30 分钟", 30), new("60 分钟", 60), new("从不", 0)];
    public Choice<int>[] ClipboardTimes { get; } = [new("15 秒", 15), new("30 秒", 30), new("60 秒", 60), new("120 秒", 120), new("不定时清理", 0)];
    public Choice<TextKind>[] TextKinds { get; } = [new("Prompt 提示词", TextKind.Prompt), new("代码 / 配置片段", TextKind.Code)];

    public AsyncCommand NavigateCommand { get; }
    public AsyncCommand NewCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand DeleteCommand { get; }
    public AsyncCommand DuplicateCommand { get; }
    public AsyncCommand LockCommand { get; }
    public AsyncCommand ImportEnvCommand { get; }
    public AsyncCommand ImportTextCommand { get; }
    public AsyncCommand ExportTextCommand { get; }
    public AsyncCommand BackupCommand { get; }
    public AsyncCommand RestoreCommand { get; }
    public AsyncCommand ChangePasswordCommand { get; }
    public AsyncCommand SaveSettingsCommand { get; }
    public AsyncCommand TaxonomyCommand { get; }
    public RelayCommand AddFieldCommand { get; }
    public RelayCommand RemoveFieldCommand { get; }
    public RelayCommand CopyFieldCommand { get; }
    public RelayCommand CopyBaseUrlCommand { get; }
    public RelayCommand CopyEnvNameCommand { get; }
    public RelayCommand CopyBodyCommand { get; }
    public RelayCommand CopyConfigCommand { get; }
    public RelayCommand OpenLinkCommand { get; }
    public RelayCommand RetryCommand { get; }
    public RelayCommand LicenseCommand { get; }

    public MainViewModel(StorageLocation? storage = null, Func<long?>? observeNativeInput = null)
    {
        Storage = storage ?? StorageLocation.Detect();
        _observeNativeInput = observeNativeInput ?? NativeWindows.ObserveInputInThisProcess;
        NavigateCommand = Command(async p =>
        {
            if (!await LeaveEditorAsync()) return;
            ClearEditor(); Page = p as string ?? "Credentials";
            _selected = null; _search = ""; _category = AllCategories; _tag = AllTags; _kindFilter = "全部类型"; _onlyFavorites = false;
            _sort = "最近修改"; OnPropertyChanged(nameof(Sort));
            OnPropertyChanged(nameof(Search)); OnPropertyChanged(nameof(CategoryFilter)); OnPropertyChanged(nameof(TagFilter)); OnPropertyChanged(nameof(KindFilter)); OnPropertyChanged(nameof(OnlyFavorites));
            if (Page == "Settings") Settings = VaultJson.Clone(_session!.Data.Settings);
            RefreshList();
            if (Page != "Settings" && VisibleRecords.Count > 0) SetEditor(VisibleRecords[0]);
        });
        NewCommand = Command(async _ =>
        {
            if (!await LeaveEditorAsync()) return;
            SetEditor(Page == "Texts" ? new TextRecord() : new CredentialRecord { Fields = [new CredentialField()] }, true);
        });
        SaveCommand = Command(async _ => { await SaveEditorAsync(); }, () => HasEditor);
        DeleteCommand = Command(async _ =>
        {
            if (Editor is null || !Dialogs.Confirm("确定删除这条记录？删除后可通过先前的加密备份恢复。", "删除记录")) return;
            var data = VaultJson.Clone(_session!.Data);
            var id = Editor.Record.Id;
            data.Credentials.RemoveAll(x => x.Id == id); data.Texts.RemoveAll(x => x.Id == id);
            await CommitAsync(data); ClearEditor(); _selected = null; RefreshList(); Status = "记录已删除。";
        }, () => Editor is { IsNew: false });
        DuplicateCommand = Command(async _ =>
        {
            if (Editor is null || !await LeaveEditorAsync() || Editor is null) return;
            var copy = Editor.ToRecord();
            copy.Id = Guid.NewGuid(); copy.Title += "（副本）"; copy.CreatedAt = copy.UpdatedAt = DateTimeOffset.UtcNow;
            SetEditor(copy, true, true); RecordEditor.ClearRecord(copy); Status = "副本尚未保存，可以编辑后保存。";
        }, () => HasEditor);
        LockCommand = Command(async _ => { if (await LeaveEditorAsync()) Lock(false); });
        ImportEnvCommand = Command(async _ => await ImportEnvAsync());
        ImportTextCommand = Command(async _ => await ImportTextAsync());
        ExportTextCommand = Command(async _ => await ExportTextAsync(), () => Editor?.Text is not null);
        BackupCommand = Command(async _ =>
        {
            if (!await LeaveEditorAsync()) return;
            var path = Dialogs.SaveFile("Nacre 加密备份|*.nacre|所有文件|*.*", "保存加密备份", $"Nacre-{DateTime.Now:yyyyMMdd-HHmmss}.nacre");
            if (path is null) return;
            await StorageWorkAsync(() => _session!.BackupAsync(path)); Status = "加密备份已保存。请妥善保管备份和对应的主密码。";
        });
        RestoreCommand = new AsyncCommand(_ => RunAsync(RestoreAsync), () => !IsBusy);
        ChangePasswordCommand = Command(async _ =>
        {
            if (!await LeaveEditorAsync()) return;
            var dialog = new ChangePasswordDialog { Owner = Dialogs.Owner };
            if (dialog.ShowDialog() != true) return;
            var passwords = dialog.TakePasswords();
            try { await StorageWorkAsync(() => _session!.ChangePasswordAsync(passwords.Current, passwords.New)); Status = "主密码已修改。旧备份仍需要旧密码，请重新创建备份。"; }
            finally { CryptographicOperations.ZeroMemory(passwords.Current); CryptographicOperations.ZeroMemory(passwords.New); }
        });
        SaveSettingsCommand = Command(async _ => { await SaveSettingsAsync(); Status = "设置已保存。"; }, () => Settings is not null);
        TaxonomyCommand = Command(async _ =>
        {
            if (!await LeaveEditorAsync()) return;
            var data = VaultJson.Clone(_session!.Data);
            var dialog = new TaxonomyDialog(data) { Owner = Dialogs.Owner };
            if (dialog.ShowDialog() != true) { data.ClearSensitiveReferences(); return; }
            var id = Editor?.Record.Id;
            await CommitAsync(data); Reopen(id); Status = "分类与标签已更新。";
        });
        AddFieldCommand = new(_ => Editor?.Credential?.Fields.Add(new()), () => IsUnlocked && !IsBusy && Editor?.Credential is not null);
        RemoveFieldCommand = new(p => { if (p is CredentialField f && Dialogs.Confirm("确定移除此字段？保存记录后生效。", "移除凭据字段")) { Editor?.Credential?.Fields.Remove(f); f.Value = ""; } }, () => !IsBusy);
        CopyFieldCommand = new(p => { if (p is CredentialField f) Copy(f.Value, f.IsSensitive); }, () => !IsBusy);
        CopyBaseUrlCommand = new(_ => Copy(Editor?.Credential?.BaseUrl ?? "", false), () => !IsBusy);
        CopyEnvNameCommand = new(_ => Copy(Editor?.Credential?.EnvironmentVariable ?? "", false), () => !IsBusy);
        CopyBodyCommand = new(_ => Copy(Editor?.Text?.Body ?? "", true), () => !IsBusy);
        CopyConfigCommand = new(_ =>
        {
            if (Editor?.Credential is not { } key) return;
            var dialog = new ConfigDialog(key) { Owner = Dialogs.Owner };
            if (dialog.ShowDialog() == true && dialog.Output is { } output) { Copy(output, true); dialog.ClearOutput(); }
        }, () => !IsBusy && Editor?.Credential is not null);
        OpenLinkCommand = new(p => { if (p is string link) Dialogs.OpenLink(link); }, () => !IsBusy);
        RetryCommand = new(_ => Initialize(), () => !IsBusy);
        LicenseCommand = new(_ => new LicenseDialog { Owner = Dialogs.Owner }.ShowDialog());
        _idleTimer.Tick += IdleTick; _idleTimer.Start();
        _theme.Apply("System");
    }

    public void Initialize()
    {
        try { _store ??= new VaultStore(Storage.VaultPath); IsFirstRun = !_store.Exists; Status = "本地保险库 · 完全离线"; }
        catch (Exception e) { Status = "无法访问保险库目录。请解决权限或文件占用问题后重试。"; Dialogs.Error(e); }
    }
    private AsyncCommand Command(Func<object?, Task> action, Func<bool>? enabled = null) => new(p => RunAsync(() => action(p)), () => IsUnlocked && !IsBusy && (enabled?.Invoke() ?? true));
    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try { await action(); }
        catch (Exception e) { Dialogs.Error(e); Status = "操作未完成，当前数据未被静默覆盖。"; }
        finally { IsBusy = false; Activity(); }
    }
    private async Task StorageWorkAsync(Func<Task> action)
    { _storageWork++; try { await action(); } finally { _storageWork--; } }
    private async Task<T> StorageWorkAsync<T>(Func<Task<T>> action)
    { _storageWork++; try { return await action(); } finally { _storageWork--; } }
    public async Task UnlockAsync(byte[] password)
    {
        if (IsBusy || IsUnlocked) { CryptographicOperations.ZeroMemory(password); return; }
        await RunAsync(async () =>
        {
            try
            {
                if (_store is null) { Initialize(); if (_store is null) return; }
                _session = await StorageWorkAsync(() => IsFirstRun ? _store.CreateAsync(password) : _store.UnlockAsync(password));
                IsFirstRun = false; IsUnlocked = true; Page = "Credentials";
                _theme.Apply(_session.Data.Settings.Theme); RefreshTaxonomies(); RefreshList();
                if (VisibleRecords.Count > 0) SetEditor(VisibleRecords[0]);
                Status = "保险库已解锁。所有修改需点击保存。";
            }
            finally { CryptographicOperations.ZeroMemory(password); }
        });
    }
    private async Task SelectAsync(SavedRecord record)
    {
        await RunAsync(async () =>
        {
            if (await LeaveEditorAsync())
            {
                var current = _session!.Data.AllRecords().FirstOrDefault(r => r.Id == record.Id);
                if (current is not null) SetEditor(current);
            }
            else _ = Application.Current.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(SelectedRecord)));
        });
    }
    private void SetEditor(SavedRecord record, bool isNew = false, bool forceDirty = false)
    {
        ClearEditor(); Editor = new(record, isNew, forceDirty);
        _selected = isNew ? null : record; OnPropertyChanged(nameof(SelectedRecord));
    }
    private void ClearEditor() { Editor?.Clear(); Editor = null; }
    private void Reopen(Guid? id)
    {
        ClearEditor(); _selected = null;
        if (id is not null && _session?.Data.AllRecords().FirstOrDefault(r => r.Id == id) is { } record) SetEditor(record);
        RefreshList();
    }
    private async Task<bool> LeaveEditorAsync()
    {
        var dirty = Page == "Settings" ? SettingsDirty() : Editor?.IsDirty == true;
        if (!dirty) return true;
        switch (Dialogs.Unsaved())
        {
            case MessageBoxResult.Yes:
                return Page == "Settings" ? await SaveSettingsAsync() : await SaveEditorAsync();
            case MessageBoxResult.No:
                if (Page == "Settings") Settings = VaultJson.Clone(_session!.Data.Settings);
                else Reopen(Editor?.Record.Id);
                Status = "未保存的修改已放弃。";
                return true;
            default:
                return false;
        }
    }
    private bool SettingsDirty() => Settings is not null && _session is not null && (Settings.Theme != _session.Data.Settings.Theme || Settings.AutoLockMinutes != _session.Data.Settings.AutoLockMinutes || Settings.ClipboardSeconds != _session.Data.Settings.ClipboardSeconds);
    private async Task<bool> SaveEditorAsync()
    {
        if (Editor is null) return true;
        var record = Editor.ToRecord();
        if (VaultValidation.RecordError(record) is { } error) { RecordEditor.ClearRecord(record); Dialogs.Error(error); return false; }
        record.UpdatedAt = DateTimeOffset.UtcNow;
        var data = VaultJson.Clone(_session!.Data);
        if (record is CredentialRecord key)
        { var index = data.Credentials.FindIndex(x => x.Id == key.Id); if (index < 0) data.Credentials.Add(key); else data.Credentials[index] = key; }
        else if (record is TextRecord text)
        { var index = data.Texts.FindIndex(x => x.Id == text.Id); if (index < 0) data.Texts.Add(text); else data.Texts[index] = text; }
        TaxonomyService.Register(data, record);
        var id = record.Id;
        await CommitAsync(data); Reopen(id); Status = "记录已加密保存。"; return true;
    }
    private async Task<bool> SaveSettingsAsync()
    {
        if (Settings is null) return true;
        var data = VaultJson.Clone(_session!.Data); data.Settings = VaultJson.Clone(Settings);
        await CommitAsync(data); _theme.Apply(data.Settings.Theme); return true;
    }
    private async Task CommitAsync(VaultData data)
    {
        try { await StorageWorkAsync(() => _session!.SaveAsync(data)); }
        catch { data.ClearSensitiveReferences(); throw; }
        RefreshTaxonomies(); RefreshList();
    }
    private void RefreshTaxonomies()
    {
        if (_session is null) return;
        _refreshing = true;
        try
        {
            Categories.Clear(); Tags.Clear();
            foreach (var c in CategoryFilters.Where(c => c.Value is not null && !_session.Data.Categories.Contains(c.Value)).ToArray()) CategoryFilters.Remove(c);
            foreach (var t in TagFilters.Where(t => t.Value is not null && !_session.Data.Tags.Contains(t.Value)).ToArray()) TagFilters.Remove(t);
            foreach (var c in _session.Data.Categories.Order(StringComparer.CurrentCultureIgnoreCase))
            { Categories.Add(c); if (!CategoryFilters.Any(x => x.Value == c)) CategoryFilters.Add(new(c, c)); }
            foreach (var t in _session.Data.Tags.Order(StringComparer.CurrentCultureIgnoreCase))
            { Tags.Add(t); if (!TagFilters.Any(x => x.Value == t)) TagFilters.Add(new(t, t)); }
            _category = CategoryFilters.FirstOrDefault(c => c.Value == _category.Value) ?? AllCategories;
            _tag = TagFilters.FirstOrDefault(t => t.Value == _tag.Value) ?? AllTags;
        }
        finally { _refreshing = false; OnPropertyChanged(nameof(CategoryFilter)); OnPropertyChanged(nameof(TagFilter)); }
    }
    private void RefreshList()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            VisibleRecords.Clear();
            if (_session is null || Page == "Settings") return;
            IEnumerable<SavedRecord> records = Page == "Texts" ? _session.Data.Texts : _session.Data.Credentials;
            var query = Search.Trim();
            records = records.Where(r => (!OnlyFavorites || r.Favorite)
                && (CategoryFilter.Value is null || r.Category.Equals(CategoryFilter.Value, StringComparison.OrdinalIgnoreCase))
                && (TagFilter.Value is null || r.Tags.Contains(TagFilter.Value, StringComparer.OrdinalIgnoreCase))
                && (query.Length == 0 || SearchContent(r).Contains(query, StringComparison.CurrentCultureIgnoreCase))
                && (r is not TextRecord text || KindFilter == "全部类型" || text.Kind == (KindFilter == "Prompt 提示词" ? TextKind.Prompt : TextKind.Code)));
            records = Sort switch
            {
                "名称 A → Z" => records.OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase),
                "名称 Z → A" => records.OrderByDescending(r => r.Title, StringComparer.CurrentCultureIgnoreCase),
                "最近创建" => records.OrderByDescending(r => r.CreatedAt),
                "有效期最近" => records.OrderBy(r => (r as CredentialRecord)?.ExpiresOn ?? DateTime.MaxValue),
                _ => records.OrderByDescending(r => r.UpdatedAt)
            };
            foreach (var record in records) VisibleRecords.Add(record);
        }
        finally { _refreshing = false; OnPropertyChanged(nameof(SelectedRecord)); OnPropertyChanged(nameof(ListStatus)); OnPropertyChanged(nameof(HasRecords)); }
    }
    private static string SearchContent(SavedRecord r) => string.Join('\n', r.Title, r.Category, string.Join(' ', r.Tags), r.Notes,
        r is TextRecord t ? t.Body : r is CredentialRecord k ? string.Join(' ', k.BaseUrl, k.EnvironmentVariable, k.Usage, string.Join(' ', k.Links), string.Join(' ', k.Fields.Select(f => f.Name))) : "");
    private void Copy(string content, bool sensitive)
    {
        try { _clipboard.Copy(content, sensitive, _session?.Data.Settings.ClipboardSeconds ?? 30); Status = sensitive ? "已复制。剪贴板将按设置清理；锁定时也会尝试清理。" : "已复制。"; }
        catch (System.Runtime.InteropServices.ExternalException) { Dialogs.Error("剪贴板正被其他程序占用，请稍后重试。"); }
    }
    private static async Task<string> ReadTextAsync(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        if (stream.Length > 4 * 1024 * 1024) throw new ArgumentException("单个导入文件不能超过 4 MiB。");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync();
    }
    private async Task ImportEnvAsync()
    {
        if (!await LeaveEditorAsync()) return;
        var path = Dialogs.OpenFile("环境变量文件|*.env;.env;*.env.*|所有文件|*.*", "选择 .env 文件（UTF-8）");
        if (path is null) return;
        var active = _session;
        var content = await ReadTextAsync(path);
        if (!ReferenceEquals(active, _session)) return;
        var parsed = EnvFile.Parse(content);
        var dialog = new EnvImportDialog(parsed, _session!.Data.Credentials.Select(x => x.EnvironmentVariable)) { Owner = Dialogs.Owner };
        if (dialog.ShowDialog() != true) { dialog.Clear(); return; }
        var data = VaultJson.Clone(_session.Data);
        var count = 0;
        foreach (var entry in dialog.SelectedEntries)
        {
            data.Credentials.Add(new CredentialRecord { Title = entry.Name[..Math.Min(entry.Name.Length, 300)], EnvironmentVariable = entry.Name, Fields = [new CredentialField { Name = entry.Name, Value = entry.Value, EnvironmentVariable = entry.Name, IsSensitive = true }] }); count++;
        }
        dialog.Clear();
        if (count == 0) { data.ClearSensitiveReferences(); return; }
        await CommitAsync(data); ClearEditor(); _selected = null; Status = $"已导入 {count} 条新记录，未覆盖现有记录。";
    }
    private async Task ImportTextAsync()
    {
        if (!await LeaveEditorAsync()) return;
        var path = Dialogs.OpenFile("文本 / Markdown|*.txt;*.md|所有文件|*.*", "导入 UTF-8 文本");
        if (path is null) return;
        var active = _session;
        var body = await ReadTextAsync(path);
        if (!ReferenceEquals(active, _session)) return;
        var text = new TextRecord { Title = Path.GetFileNameWithoutExtension(path), Body = body };
        SetEditor(text, true, true); RecordEditor.ClearRecord(text); Status = "文本已载入编辑器，请选择类型并保存。";
    }
    private async Task ExportTextAsync()
    {
        if (Editor?.Text is not { } text || !Dialogs.Confirm("导出的文本文件未经加密，可能包含敏感信息。确定继续？", "导出明文文本")) return;
        var path = Dialogs.SaveFile("文本文件|*.txt|Markdown|*.md", "导出文本", "Nacre-text.txt");
        if (path is null) return;
        if (Path.GetFullPath(path).StartsWith(Path.GetFullPath(Storage.DirectoryPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请在保险库数据目录之外保存明文文本，避免混入加密备份。");
        await File.WriteAllTextAsync(path, text.Body, new UTF8Encoding(false)); Status = "文本已导出为明文 UTF-8 文件。";
    }
    private async Task RestoreAsync()
    {
        if (_store is null) { Initialize(); if (_store is null) return; }
        if (IsUnlocked && !await LeaveEditorAsync()) return;
        var path = Dialogs.OpenFile("Nacre 加密文件|*.nacre;*.previous;*.before-restore-*|所有文件|*.*", "选择加密备份");
        if (path is null || !Dialogs.Confirm("恢复将替换当前保险库的全部内容。现有文件会先保留为带 before-restore 标记的加密文件。\n\n请确认这是你要恢复的备份。", "确认恢复并覆盖")) return;
        var password = Dialogs.Password("输入此备份的主密码");
        if (password is null) return;
        try
        {
            var replacement = await StorageWorkAsync(() => _store.RestoreAsync(path, password));
            ClearEditor(); _session?.Dispose(); _session = replacement; _selected = null; Settings = null;
            IsUnlocked = true; IsFirstRun = false; Page = "Credentials";
            _theme.Apply(replacement.Data.Settings.Theme); RefreshTaxonomies(); RefreshList();
            Status = "加密备份已恢复；原文件已保留，可通过恢复功能再次打开。";
        }
        finally { CryptographicOperations.ZeroMemory(password); }
    }
    public void Activity() => _lastActivity = Environment.TickCount64;
    private void IdleTick(object? sender, EventArgs e)
    {
        if (_observeNativeInput() is long activity && activity > _lastActivity) _lastActivity = activity;
        if (!IsUnlocked || _session is null) return;
        if (_storageWork > 0) return;
        var minutes = _session.Data.Settings.AutoLockMinutes;
        if (minutes > 0 && Environment.TickCount64 - _lastActivity >= minutes * 60000L)
        {
            // Close sensitive modal previews before removing decrypted references.
            foreach (Window window in Application.Current.Windows.Cast<Window>().ToArray()) if (window != Application.Current.MainWindow) window.Close();
            NativeWindows.CloseOwnedNativeDialogs(Application.Current.MainWindow);
            Lock(true);
        }
    }
    private void Lock(bool automatic)
    {
        _clipboard.TryClearOwned(); ClearEditor(); Settings = null; _selected = null;
        _session?.Dispose(); _session = null;
        _search = ""; _category = AllCategories; _tag = AllTags; _kindFilter = "全部类型"; _onlyFavorites = false;
        Categories.Clear(); Tags.Clear(); CategoryFilters.Clear(); TagFilters.Clear();
        CategoryFilters.Add(AllCategories); TagFilters.Add(AllTags);
        IsUnlocked = false; RefreshList(); _theme.Apply("System");
        OnPropertyChanged(nameof(Search)); OnPropertyChanged(nameof(SelectedRecord));
        Status = automatic ? "因闲置已自动锁定。未保存的编辑已放弃。" : "保险库已锁定。";
        RaiseCommands();
    }
    private void RaiseCommands()
    {
        foreach (var property in GetType().GetProperties())
        {
            if (property.PropertyType == typeof(AsyncCommand)) (property.GetValue(this) as AsyncCommand)?.Raise();
            else if (property.PropertyType == typeof(RelayCommand)) (property.GetValue(this) as RelayCommand)?.Raise();
        }
    }
    public async Task<bool> TryCloseAsync()
    {
        if (IsBusy) return false;
        IsBusy = true;
        try { return !IsUnlocked || await LeaveEditorAsync(); }
        catch (Exception e) { Dialogs.Error(e); return false; }
        finally { IsBusy = false; }
    }
    public void Dispose()
    { _idleTimer.Stop(); _idleTimer.Tick -= IdleTick; ClearEditor(); _session?.Dispose(); _store?.Dispose(); _clipboard.Dispose(); _theme.Dispose(); }
}
