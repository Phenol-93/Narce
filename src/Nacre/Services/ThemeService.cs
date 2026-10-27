using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace Nacre.Services;

public sealed class ThemeService : IDisposable
{
    private string _choice = "System";
    public ThemeService() => SystemEvents.UserPreferenceChanged += Changed;
    private void Changed(object sender, UserPreferenceChangedEventArgs e)
    { if (_choice == "System") Application.Current.Dispatcher.Invoke(() => Apply(_choice)); }
    public void Apply(string choice)
    {
        _choice = choice;
        var dark = choice == "Dark";
        if (choice == "System")
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                dark = key?.GetValue("AppsUseLightTheme") is int i && i == 0;
            }
            catch (System.Security.SecurityException) { dark = false; }
        }
        var colors = dark
            ? new[] { "#171C23", "#12171D", "#202730", "#2B3540", "#E7EDF3", "#A4B1C0", "#8CC3CE", "#172B32", "#F09A9A", "#344754" }
            : new[] { "#F5F6F8", "#EBEEF2", "#FFFFFF", "#DCE2E8", "#202C38", "#617181", "#326B79", "#E4F0F3", "#AD3544", "#D4E6EB" };
        var keys = new[] { "BackgroundBrush", "SidebarBrush", "SurfaceBrush", "BorderBrush", "TextBrush", "MutedBrush", "AccentBrush", "AccentSoftBrush", "DangerBrush", "SelectionBrush" };
        for (var i = 0; i < keys.Length; i++)
        { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i])); brush.Freeze(); Application.Current.Resources[keys[i]] = brush; }
    }
    public void Dispose() => SystemEvents.UserPreferenceChanged -= Changed;
}
