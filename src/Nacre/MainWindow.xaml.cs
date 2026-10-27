using System.ComponentModel;
using System.Windows;
using Nacre.ViewModels;

namespace Nacre;

public partial class MainWindow : Window
{
    private bool _closing;
    private readonly MainViewModel _model;
    public MainWindow() : this(new MainViewModel()) { }
    public MainWindow(MainViewModel model) { InitializeComponent(); _model = model; DataContext = _model; }
    private void WindowLoaded(object sender, RoutedEventArgs e) => _model.Initialize();
    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        if (!await _model.TryCloseAsync()) return;
        _closing = true; _model.Dispose(); _ = Dispatcher.BeginInvoke(Close);
    }
}
