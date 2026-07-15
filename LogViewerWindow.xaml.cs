using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FatalisOverlay;

public partial class LogViewerWindow : Window
{
    private readonly LogViewerViewModel _vm;
    private static readonly BrushConverter _brushConv = new();

    public LogViewerWindow()
    {
        InitializeComponent();
        _vm = new LogViewerViewModel();
        DataContext = _vm;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LogViewerViewModel.SelectedSession))
                EmptyHint.Visibility = _vm.SelectedSession == null
                    ? Visibility.Visible : Visibility.Collapsed;
        };
        EmptyHint.Visibility = _vm.SelectedSession == null
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSession == null) return;

        var dialog = new Window
        {
            Title = "重命名对局",
            Width = 380, Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = B("#1E1E1E"),
            Foreground = B("#E0E0E0")
        };
        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var tb = new TextBox
        {
            Text = _vm.SelectedSession.DisplayName,
            Background = B("#2D2D2D"),
            Foreground = Brushes.White,
            BorderBrush = B("#555"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(tb, 0);
        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var okBtn = new Button { Content = "确定", Width = 60, Margin = new Thickness(0, 0, 6, 0) };
        var cancelBtn = new Button { Content = "取消", Width = 60 };
        okBtn.Click += (_, _) => { _vm.RenameSession(_vm.SelectedSession, tb.Text.Trim()); dialog.Close(); };
        cancelBtn.Click += (_, _) => dialog.Close();
        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        Grid.SetRow(btnPanel, 1);
        grid.Children.Add(tb);
        grid.Children.Add(btnPanel);
        dialog.Content = grid;
        dialog.ShowDialog();
    }

    private static Brush B(string hex) => (Brush)_brushConv.ConvertFrom(hex)!;

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSession == null) return;
        var result = MessageBox.Show(
            "确定删除此对局记录？\n\n此操作不可撤销。",
            "删除确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
            _vm.DeleteSession(_vm.SelectedSession);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        var prevSelected = _vm.SelectedSession?.FileName;
        _vm.LoadSessions();
        if (prevSelected != null)
        {
            foreach (var s in _vm.Sessions)
                if (s.FileName == prevSelected)
                { _vm.SelectedSession = s; break; }
        }
    }
}
