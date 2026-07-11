using System.Windows;

namespace FatalisOverlay;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        new SettingsWindow().Show();
    }
}
