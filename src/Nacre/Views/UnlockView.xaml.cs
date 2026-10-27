using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nacre.Services;
using Nacre.Core.Services;
using Nacre.ViewModels;

namespace Nacre.Views;

public partial class UnlockView : UserControl
{
    public UnlockView() { InitializeComponent(); IsVisibleChanged += (_, _) => { Password.Clear(); Confirmation.Clear(); Validation.Text = ""; Acknowledged.IsChecked = false; if (IsVisible) Dispatcher.BeginInvoke(() => Password.Focus()); }; }
    private void PasswordKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; UnlockClick(sender, e); } }
    private async void UnlockClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel model || model.IsBusy) return;
        Validation.Text = "";
        using var secure = Password.SecurePassword;
        if (secure.Length == 0) { Validation.Text = "请输入主密码。"; return; }
        if (model.IsFirstRun && (secure.Length < VaultCryptography.MinimumPasswordLength || Acknowledged.IsChecked != true))
        { Validation.Text = $"主密码至少 {VaultCryptography.MinimumPasswordLength} 个字符，并需确认无法找回密码。"; return; }
        var bytes = PasswordBytes.FromSecureString(secure);
        if (model.IsFirstRun)
        {
            using var confirmation = Confirmation.SecurePassword;
            var other = PasswordBytes.FromSecureString(confirmation);
            try { if (!CryptographicOperations.FixedTimeEquals(bytes, other)) { CryptographicOperations.ZeroMemory(bytes); Validation.Text = "两次输入的主密码不一致。"; return; } }
            finally { CryptographicOperations.ZeroMemory(other); }
        }
        Password.Clear(); Confirmation.Clear();
        await model.UnlockAsync(bytes);
    }
}
