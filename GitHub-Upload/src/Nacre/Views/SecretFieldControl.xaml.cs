using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Nacre.Views;

public partial class SecretFieldControl : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(string), typeof(SecretFieldControl), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, ValueChanged));
    public static readonly DependencyProperty SensitiveProperty = DependencyProperty.Register(nameof(Sensitive), typeof(bool), typeof(SecretFieldControl), new PropertyMetadata(true, SensitivityChanged));
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool Sensitive { get => (bool)GetValue(SensitiveProperty); set => SetValue(SensitiveProperty, value); }
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool _syncing, _showing;
    public SecretFieldControl() { InitializeComponent(); _timer.Tick += (_, _) => Hide(); }
    private static void ValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (SecretFieldControl)d;
        if (control.Masked is null) return;
        control._syncing = true;
        try { control.Masked.Password = e.NewValue as string ?? ""; }
        finally { control._syncing = false; }
    }
    private static void SensitivityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((SecretFieldControl)d).Hide();
    private void MaskedChanged(object sender, RoutedEventArgs e)
    { if (!_syncing) SetCurrentValue(ValueProperty, Masked.Password); }
    private void RevealClick(object sender, RoutedEventArgs e)
    {
        if (_showing) { Hide(); return; }
        _showing = true; Masked.Visibility = Visibility.Collapsed; Revealed.Visibility = Visibility.Visible; Reveal.Content = "隐藏"; _timer.Start();
    }
    private void Hide()
    {
        _timer.Stop(); _showing = false;
        if (Masked is null) return;
        Masked.Visibility = Sensitive ? Visibility.Visible : Visibility.Collapsed;
        Revealed.Visibility = Sensitive ? Visibility.Collapsed : Visibility.Visible;
        Reveal.Visibility = Sensitive ? Visibility.Visible : Visibility.Collapsed; Reveal.Content = "显示";
    }
    private void Deactivated(object? sender, EventArgs e) => Hide();
    private void ControlLoaded(object sender, RoutedEventArgs e) { Application.Current.Deactivated += Deactivated; ValueChanged(this, new DependencyPropertyChangedEventArgs(ValueProperty, "", Value)); Hide(); }
    private void ControlUnloaded(object sender, RoutedEventArgs e)
    { Application.Current.Deactivated -= Deactivated; _timer.Stop(); _syncing = true; Masked.Clear(); _syncing = false; }
}
