using System;
using System.Linq;
using System.Windows;

namespace ReminderSetup;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        bool uninstall = e.Args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));
        new SetupWindow(uninstall).Show();
    }
}
