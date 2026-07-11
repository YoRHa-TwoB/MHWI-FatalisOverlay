using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace FatalisOverlay;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _vm;
    private MainWindow? _overlay;

    public SettingsWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.StartPolling();

        // Auto-show overlay on startup
        if (_vm.OverlayVisible)
            CreateOverlay();
    }

    private void CreateOverlay()
    {
        if (_overlay != null) return;
        _overlay = new MainWindow(_vm);
        _overlay.Show();
    }

    private void DestroyOverlay()
    {
        if (_overlay == null) return;
        _overlay.Close();
        _overlay = null;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.OverlayVisible))
        {
            Dispatcher.Invoke(() =>
            {
                if (_vm.OverlayVisible) CreateOverlay();
                else DestroyOverlay();
            });
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _vm.SaveConfig();
        MessageBox.Show("配置已保存", "Fatalis Overlay", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        try { Process.Start("explorer.exe", dir); } catch { }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.StopPolling();
        _vm.SaveConfig();

        _overlay?.Close();
        _overlay = null;

        Application.Current.Shutdown();
    }
}
