using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace FatalisOverlay;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = _vm;

        Left = _vm.Config.WindowX;
        Top = _vm.Config.WindowY;

        if (double.IsNaN(Left) || Left < -1000 || Left > 5000) Left = 100;
        if (double.IsNaN(Top) || Top < -1000 || Top > 5000) Top = 100;

        Closing += OnClosing;
        Loaded += OnLoaded;
        IsVisibleChanged += OnVisibleChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm.Config.PropertyChanged += OnConfigChanged;
        SyncWidth();
    }

    private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
            SyncWidth();
    }

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_vm.Config.Scale))
            SyncWidth();
    }

    private void SyncWidth()
    {
        Width = 300 * _vm.Config.Scale;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _vm.Config.WindowX = Left;
        _vm.Config.WindowY = Top;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            var p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            try { System.Diagnostics.Process.Start("notepad.exe", p); } catch { }
        }
        else DragMove();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F5:
                // Refresh bindings
                var ctx = DataContext; DataContext = null; DataContext = ctx;
                break;
            case Key.Escape:
                Hide();
                break;
        }
    }
}
