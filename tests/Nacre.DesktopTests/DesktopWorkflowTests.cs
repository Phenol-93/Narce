using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nacre.Core.Models;
using Nacre.Core.Services;
using Nacre.Services;
using Nacre.ViewModels;
using Nacre.Views;

namespace Nacre.DesktopTests;

public sealed class DesktopWorkflowTests
{
    [Fact]
    public async Task RealWpfBindingsCommandsThemesClipboardAndLockWorkTogether()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var directory = Path.Combine(Path.GetTempPath(), "Nacre-desktop-tests", Guid.NewGuid().ToString("N"));
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var app = new App();
            app.InitializeComponent();
            // Isolate the synthetic activity clock from the human user's real desktop input.
            var model = new MainViewModel(new StorageLocation(directory, true), observeNativeInput: () => null);
            var window = new MainWindow(model);
            app.MainWindow = window;
            window.Loaded += async (_, _) =>
            {
                try
                {
                    Assert.True(model.IsFirstRun); Assert.False(model.IsUnlocked);
                    await Layout();
                    Capture(window, "01-create");
                    Find<PasswordBox>(window, "MasterPassword").Password = "fake06";
                    Find<PasswordBox>(window, "ConfirmPassword").Password = "fake06";
                    Find<CheckBox>(window, "Acknowledge").IsChecked = true;
                    Invoke(Find<Button>(window, "Unlock"));
                    await Until(() => model.IsUnlocked && !model.IsBusy);

                    await model.NewCommand.ExecuteAsync();
                    await Layout();
                    Find<TextBox>(window, "RecordTitle").Text = "Fictional desktop credential";
                    Find<TextBox>(window, "FieldName").Text = "TOKEN";
                    var secretControl = Descendants(window).OfType<SecretFieldControl>().First(c => c.IsVisible);
                    secretControl.SetCurrentValue(SecretFieldControl.ValueProperty, "fictional-ui-key");
                    Assert.Equal("Fictional desktop credential", model.Editor!.Record.Title);
                    Assert.Equal("fictional-ui-key", model.Editor.Credential!.Fields.Single().Value);
                    model.Editor.Record.Category = "全部分类";
                    model.Editor.TagsText = "fictional-tag, 第二个标签";
                    model.Editor.Record.Favorite = true;
                    Invoke(Find<Button>(window, "Save"));
                    await Until(() => model.VisibleRecords.Count == 1 && !model.IsBusy);
                    Assert.False(model.Editor!.IsDirty);
                    Assert.Equal(2, model.Editor.Record.Tags.Count);
                    model.CategoryFilter = model.CategoryFilters.Single(c => c.Value == "全部分类");
                    Assert.Single(model.VisibleRecords);
                    model.CategoryFilter = MainViewModel.AllCategories;
                    Assert.Equal("全部分类", model.CategoryFilter.ToString());
                    model.Search = "does-not-exist"; Assert.Empty(model.VisibleRecords);
                    model.Search = ""; Assert.Single(model.VisibleRecords);
                    model.OnlyFavorites = true; Assert.Single(model.VisibleRecords);
                    model.OnlyFavorites = false;
                    await Layout();
                    var visibleSecret = Descendants(window).OfType<SecretFieldControl>().First(c => c.IsVisible);
                    Assert.Equal(Visibility.Visible, ((PasswordBox)visibleSecret.FindName("Masked")).Visibility);
                    Assert.Equal(Visibility.Collapsed, ((TextBox)visibleSecret.FindName("Revealed")).Visibility);
                    Capture(window, "02-credentials-light");

                    model.AddFieldCommand.Execute(null);
                    var second = model.Editor.Credential!.Fields.Last();
                    second.Name = "CLIENT_ID"; second.Value = "fictional-client-id"; second.IsSensitive = false;
                    await model.SaveCommand.ExecuteAsync();
                    await model.DuplicateCommand.ExecuteAsync();
                    Assert.True(model.Editor!.IsNew); Assert.True(model.Editor.IsDirty);
                    await model.SaveCommand.ExecuteAsync();
                    Assert.Equal(2, model.VisibleRecords.Count);
                    var cachedField = model.Editor!.Credential!.Fields.First();
                    model.CopyFieldCommand.Execute(cachedField);
                    Assert.Equal("fictional-ui-key", Clipboard.GetText());
                    Clipboard.SetText("fictional-content-from-another-app");
                    await model.LockCommand.ExecuteAsync();
                    Assert.False(model.IsUnlocked); Assert.Null(model.Editor); Assert.Empty(cachedField.Value);
                    Assert.Equal("fictional-content-from-another-app", Clipboard.GetText());
                    Clipboard.Clear();
                    await model.UnlockAsync(Encoding.UTF8.GetBytes("fake06"));
                    Assert.Equal(2, model.VisibleRecords.Count);
                    Assert.Equal(2, model.Editor!.Credential!.Fields.Count);
                    var config = new ConfigDialog(model.Editor.Credential) { Owner = window };
                    ShowAndAct(config, () => Invoke(Descendants(config).OfType<Button>().Single(b => b.Content as string == "复制明文配置")));
                    Assert.Contains("TOKEN=", config.Output);
                    Assert.Contains("CLIENT_ID=", config.Output);
                    config.ClearOutput(); Assert.Null(config.Output);

                    var preview = new EnvImportDialog(EnvFile.Parse("DEMO_TOKEN=fictional-preview\nDEMO_TOKEN=fictional-second\nINVALID"), ["DEMO_TOKEN"]) { Owner = window };
                    ShowAndAct(preview, () =>
                    {
                        Assert.Equal(2, preview.Rows.Count);
                        Assert.Contains("同名", preview.Rows[0].Description);
                        preview.Rows[1].Selected = false;
                        Invoke(Descendants(preview).OfType<Button>().Single(b => b.Content as string == "确认导入所选项"));
                    });
                    Assert.Equal("fictional-preview", preview.SelectedEntries.Single().Value);
                    preview.Clear(); Assert.Empty(preview.Rows);

                    await model.NavigateCommand.ExecuteAsync("Texts");
                    await model.NewCommand.ExecuteAsync();
                    await Layout();
                    Find<TextBox>(window, "RecordTitle").Text = "Fictional text title";
                    Find<TextBox>(window, "TextBody").Text = "fictional prompt\r\n第二行\nthird line";
                    model.Editor!.Text!.Kind = TextKind.Code;
                    await model.SaveCommand.ExecuteAsync();
                    model.Search = "第二行"; Assert.Single(model.VisibleRecords);
                    model.KindFilter = "Prompt 提示词"; Assert.Empty(model.VisibleRecords);
                    model.KindFilter = "代码 / 配置片段"; Assert.Single(model.VisibleRecords);
                    model.Search = "";
                    Capture(window, "03-texts-light");
                    var editor = model.Editor!;
                    editor.Text!.Body += "\nunsaved"; Assert.True(editor.IsDirty);
                    editor.Text.Body = "fictional prompt\r\n第二行\nthird line"; Assert.False(editor.IsDirty);

                    await model.NavigateCommand.ExecuteAsync("Settings");
                    model.Settings!.Theme = "Dark"; model.Settings.AutoLockMinutes = 1;
                    await model.SaveSettingsCommand.ExecuteAsync();
                    Assert.Equal(ColorConverter.ConvertFromString("#171C23"), ((SolidColorBrush)app.Resources["BackgroundBrush"]).Color);
                    await Layout(); Capture(window, "04-settings-dark");
                    window.Width = 1040; window.Height = 660;
                    await model.NavigateCommand.ExecuteAsync("Credentials"); await Layout();
                    Assert.All(Descendants(window).OfType<ComboBox>().Where(c => c.ItemsSource == model.CategoryFilters || c.ItemsSource == model.TagFilters), c => Assert.NotNull(c.SelectedItem));
                    Capture(window, "05-minimum-dark");
                    await model.LockCommand.ExecuteAsync();
                    await model.UnlockAsync(Encoding.UTF8.GetBytes("fake06"));
                    await model.NavigateCommand.ExecuteAsync("Settings");
                    Assert.Equal("Dark", model.Settings!.Theme); Assert.Equal(1, model.Settings.AutoLockMinutes);

                    using (var clipboard = new SensitiveClipboard())
                    {
                        clipboard.Copy("fictional-expiring-content", true, 1);
                        Assert.Equal("fictional-expiring-content", Clipboard.GetText());
                        await Until(() => !Clipboard.ContainsText(), 4000);
                        clipboard.Copy("fictional-owned-content", true, 1);
                        Clipboard.SetText("fictional-replacement");
                        await Task.Delay(2200);
                        Assert.Equal("fictional-replacement", Clipboard.GetText());
                        Clipboard.Clear();
                    }
                    var modal = new ConfigDialog(new CredentialRecord { Title = "Fictional modal", Fields = [new CredentialField { Name = "DEMO", Value = "fictional-modal" }] }) { Owner = window };
                    modal.Show();
                    // Advance the existing activity timestamp to exercise the real DispatcherTimer.
                    typeof(MainViewModel).GetField("_lastActivity", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(model, Environment.TickCount64 - 61000L);
                    await Until(() => !model.IsUnlocked, 4000);
                    Assert.False(modal.IsVisible); Assert.Null(model.Editor); Assert.Empty(model.VisibleRecords);
                    completion.SetResult();
                }
                catch (Exception error) { completion.SetException(error); }
                finally { model.Dispose(); app.Shutdown(); }
            };
            app.Run(window);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(60)); }
        finally { thread.Join(TimeSpan.FromSeconds(5)); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static T Find<T>(DependencyObject root, string id) where T : FrameworkElement =>
        Descendants(root).OfType<T>().First(x => AutomationProperties.GetAutomationId(x) == id && x.IsVisible);
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void ShowAndAct(Window dialog, Action action)
    {
        Exception? failure = null;
        _ = dialog.Dispatcher.BeginInvoke(() =>
        {
            try { dialog.UpdateLayout(); action(); }
            catch (Exception e) { failure = e; dialog.Close(); }
        }, DispatcherPriority.ApplicationIdle);
        var accepted = dialog.ShowDialog();
        if (failure is not null) throw failure;
        Assert.True(accepted);
    }
    private static async Task Layout() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    private static async Task Until(Func<bool> predicate, int timeout = 10000)
    {
        var end = Environment.TickCount64 + timeout;
        while (!predicate()) { if (Environment.TickCount64 >= end) throw new TimeoutException("WPF workflow did not complete."); await Task.Delay(50); }
        await Layout();
    }
    private static void Capture(Window window, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Nacre.sln"))) root = root.Parent;
        var folder = Path.Combine(root?.FullName ?? AppContext.BaseDirectory, "artifacts", "UiScreenshots"); Directory.CreateDirectory(folder);
        using var stream = File.Create(Path.Combine(folder, name + ".png")); png.Save(stream);
    }
}
