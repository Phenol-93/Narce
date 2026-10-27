using System.Windows;
using System.Windows.Input;
using Nacre.ViewModels;

namespace Nacre;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        InputManager.Current.PreProcessInput += InputReceived;
        DispatcherUnhandledException += (_, args) =>
        {
            Services.Dialogs.Error("发生未预期的界面错误。请保存可保存的修改，锁定后重新打开应用。没有生成错误日志。");
            args.Handled = true;
        };
        if (MainWindow is null) { MainWindow = new MainWindow(); MainWindow.Show(); }
    }
    private void InputReceived(object sender, PreProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is KeyEventArgs or MouseButtonEventArgs or MouseWheelEventArgs or TextCompositionEventArgs)
            (MainWindow?.DataContext as MainViewModel)?.Activity();
    }
    protected override void OnExit(ExitEventArgs e)
    { InputManager.Current.PreProcessInput -= InputReceived; base.OnExit(e); }
}
