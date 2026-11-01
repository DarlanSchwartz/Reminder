using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace ReminderSetup;

public partial class SetupWindow : Window
{
    private enum Stage { Options, Confirm, Working, Done, Failed }

    private readonly bool uninstalling;
    private string folder;
    private Stage stage;
    private bool keptData;

    public SetupWindow(bool uninstall)
    {
        InitializeComponent();
        // The .ico's first frame is 16px; use the largest so the header logo stays sharp.
        var icon = BitmapDecoder.Create(new Uri("pack://application:,,,/Reminder.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Logo.Source = icon.Frames.OrderByDescending(f => f.PixelWidth).First();
        uninstalling = uninstall;
        string installed = Setup.InstalledFolder();
        folder = installed ?? (uninstall ? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\') : Setup.DefaultFolder);

        Subtitle.Text = string.Format(Text.T("Version"), Setup.Version);
        LocationLabel.Text = Text.T("Location").ToUpperInvariant();
        OptionsLabel.Text = Text.T("Options").ToUpperInvariant();
        ChangeButton.Content = Text.T("Change");
        DesktopText.Text = Text.T("Desktop");
        PathOptionText.Text = Text.T("PathOption");
        PathOptionHint.Text = Text.T("PathHint");
        LaunchText.Text = Text.T("Launch");
        RuntimeText.Text = Text.T("Runtime");
        RuntimeButton.Content = Text.T("GetRuntime");
        UninstallTitle.Text = Text.T("UninstallTitle");
        UninstallText.Text = Text.T("UninstallText");
        KeepText.Text = Text.T("Keep");
        KeepHint.Text = Text.T("KeepHint");
        PathText.Text = folder;
        PathText.ToolTip = folder;
        // Screen readers announce the switches by their labels.
        AutomationProperties.SetName(DesktopOption, DesktopText.Text);
        AutomationProperties.SetName(PathOption, PathOptionText.Text);
        AutomationProperties.SetName(LaunchOption, LaunchText.Text);
        AutomationProperties.SetName(KeepOption, KeepText.Text);
        AutomationProperties.SetName(CloseButton, Text.T("Close"));

        bool runtime = Setup.HasRuntime();
        RuntimeWarning.Visibility = runtime ? Visibility.Collapsed : Visibility.Visible;
        if (!runtime) LaunchOption.IsChecked = false;

        Show(uninstall ? Stage.Confirm : Stage.Options, animate: false);
        if (!uninstall && installed != null) PrimaryButton.Content = Text.T("Update");
    }

    private void Show(Stage next, bool animate = true)
    {
        stage = next;
        FrameworkElement page = next switch
        {
            Stage.Options => OptionsPage,
            Stage.Confirm => UninstallPage,
            Stage.Working => ProgressPage,
            _ => DonePage,
        };
        foreach (var p in new FrameworkElement[] { OptionsPage, UninstallPage, ProgressPage, DonePage }) p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;

        bool busy = next == Stage.Working;
        CloseButton.IsEnabled = !busy;
        SecondaryButton.Visibility = next is Stage.Options or Stage.Confirm or Stage.Failed ? Visibility.Visible : Visibility.Collapsed;
        PrimaryButton.Visibility = busy ? Visibility.Hidden : Visibility.Visible;
        SecondaryButton.Content = next == Stage.Failed ? Text.T("Close") : Text.T("Cancel");
        PrimaryButton.Content = next switch
        {
            Stage.Options => Text.T("Install"),
            Stage.Confirm => Text.T("Uninstall"),
            Stage.Failed => Text.T("Retry"),
            _ => !uninstalling && LaunchOption.IsChecked == true ? Text.T("Open") : Text.T("Close"),
        };
        if (next == Stage.Confirm) PrimaryButton.Background = (Brush)FindResource("Danger");
        else PrimaryButton.ClearValue(BackgroundProperty);

        if (!animate) return;
        var slide = new TranslateTransform(0, 14);
        page.RenderTransform = slide;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
    }

    private void SetProgress(double value, string detail)
    {
        double width = Math.Max(0, Track.ActualWidth * Math.Min(1, value));
        Fill.BeginAnimation(WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(220)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        DetailText.Text = detail;
    }

    private async void Run()
    {
        Show(Stage.Working);
        Fill.BeginAnimation(WidthProperty, null);
        Fill.Width = 0;
        StatusText.Text = Text.T(uninstalling ? "Uninstalling" : "Installing");
        bool desktop = DesktopOption.IsChecked == true, path = PathOption.IsChecked == true;
        keptData = KeepOption.IsChecked == true;
        var progress = new Progress<(double Value, string Detail)>(p => SetProgress(p.Value, p.Detail));
        IProgress<(double, string)> report = progress;
        string target = folder;
        var started = DateTime.UtcNow;
        try
        {
            await Task.Run(() =>
            {
                Setup.StopApp(target);
                if (uninstalling) Setup.Uninstall(target, keptData, (v, d) => report.Report((v, d)));
                else Setup.Install(target, desktop, path, (v, d) => report.Report((v, d)));
            });
            // Let the bar finish filling before the success page appears.
            var elapsed = DateTime.UtcNow - started;
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(350, 900 - elapsed.TotalMilliseconds)));
            Finish(true, uninstalling ? (keptData ? string.Format(Text.T("UninstalledKept"), Path.Combine(target, "data")) : Text.T("UninstalledText")) : Text.T("InstalledText"));
        }
        catch (Exception ex)
        {
            Finish(false, ex.Message);
        }
    }

    private void Finish(bool ok, string message)
    {
        Show(ok ? Stage.Done : Stage.Failed);
        DoneTitle.Text = ok ? Text.T(uninstalling ? "Uninstalled" : "Installed") : Text.T("Failed");
        DoneText.Text = message;
        DoneIcon.Text = ok ? "" : "";
        DoneIcon.Foreground = (Brush)FindResource(ok ? "Accent" : "Danger");
        DoneBadge.Background = (Brush)FindResource(ok ? "AccentSoft" : "DangerSoft");
        var scale = (ScaleTransform)DoneBadge.RenderTransform;
        var pop = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    private void PrimaryClicked(object sender, RoutedEventArgs e)
    {
        switch (stage)
        {
            case Stage.Options:
            case Stage.Confirm:
            case Stage.Failed:
                Run();
                break;
            case Stage.Done:
                if (!uninstalling && LaunchOption.IsChecked == true)
                {
                    try { Process.Start(new ProcessStartInfo(Path.Combine(folder, "Reminder.exe")) { UseShellExecute = true, WorkingDirectory = folder }); }
                    catch (Exception) { /* The Start menu shortcut still works. */ }
                }
                Close();
                break;
        }
    }

    private void SecondaryClicked(object sender, RoutedEventArgs e) => Close();

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void ChangeFolder(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = Text.T("FolderTitle"), SelectedPath = Directory.Exists(folder) ? folder : Path.GetDirectoryName(folder), ShowNewFolderButton = true };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        string chosen = dialog.SelectedPath;
        // Picking a parent folder installs into a Reminder subfolder, like most installers.
        if (!Path.GetFileName(chosen).Equals("Reminder", StringComparison.OrdinalIgnoreCase)) chosen = Path.Combine(chosen, "Reminder");
        folder = chosen;
        PathText.Text = folder;
        PathText.ToolTip = folder;
    }

    private void GetRuntime(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://dotnet.microsoft.com/download/dotnet/10.0") { UseShellExecute = true }); }
        catch (Exception) { }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (uninstalling && stage == Stage.Done) Setup.DeleteAfterExit(folder, keptData);
    }
}
