using System;
using System.Windows;

namespace Reminder;

internal static class IntegrationConsent
{
    public static bool Ensure(Window owner, TaskService service)
    {
        if (service.State.SchedulerConsent) return true;
        var answer = MessageBox.Show(owner, Loc.T("ConsentText"), Loc.T("ConsentTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return false;
        try { service.EnableIntegration(); return true; }
        catch (Exception ex) { MessageBox.Show(owner, Loc.T("ConsentFailed") + "\n" + ex.Message, "Reminder"); return false; }
    }
}
