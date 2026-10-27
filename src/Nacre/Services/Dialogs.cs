using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Nacre.Core.Services;
using Nacre.Views;

namespace Nacre.Services;

public static class Dialogs
{
    public static Window Owner => Application.Current.MainWindow;
    public static bool Confirm(string message, string title = "Nacre") => MessageBox.Show(Owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    public static MessageBoxResult Unsaved() => MessageBox.Show(Owner, "当前有未保存的修改。是否先保存？\n选择“否”将放弃修改。", "未保存的修改", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
    public static void Error(Exception error) => Error(error switch
    {
        VaultAuthenticationException or VaultFormatException or VaultConflictException or ArgumentException => error.Message,
        UnauthorizedAccessException => "没有写入数据目录或所选文件的权限。请检查权限、只读属性和安全软件设置；便携模式请移到可写目录。",
        IOException => "文件操作失败：文件可能正被占用、存储空间不足、路径不可用，或已有另一个 Nacre 实例正在使用此保险库。请检查后重试。",
        _ => "操作未完成。请检查数据文件和可用空间后重试。没有生成包含凭据的错误日志。"
    });
    public static void Error(string message) => MessageBox.Show(Owner, message, "Nacre · 操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
    public static string? OpenFile(string filter, string title)
    { var dialog = new OpenFileDialog { Filter = filter, Title = title, CheckFileExists = true }; return dialog.ShowDialog(Owner) == true ? dialog.FileName : null; }
    public static string? SaveFile(string filter, string title, string fileName)
    { var dialog = new SaveFileDialog { Filter = filter, Title = title, FileName = fileName, AddExtension = true, OverwritePrompt = true }; return dialog.ShowDialog(Owner) == true ? dialog.FileName : null; }
    public static byte[]? Password(string title)
    { var dialog = new PasswordDialog(title) { Owner = Owner }; return dialog.ShowDialog() == true ? dialog.TakePassword() : null; }
    public static void OpenLink(string link)
    {
        if (!VaultValidation.IsWebLink(link)) { Error("仅支持有效的 http / https 链接。"); return; }
        try { Process.Start(new ProcessStartInfo(link) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Error("无法打开系统默认浏览器，请检查 Windows 默认应用设置。"); }
    }
}
