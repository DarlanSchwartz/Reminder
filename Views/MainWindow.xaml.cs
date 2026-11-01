using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;

namespace Reminder;

public partial class MainWindow : Window
{
    private readonly TaskService service;
    private bool completed;
    // Ids already on screen: only cards that newly appear animate in.
    private readonly System.Collections.Generic.HashSet<Guid> shown = new();
    private int slideFrom;
    private bool tabPlaced;
    private ContextMenu? settingsMenu;
    private bool settingsClosedOnGear;
    public MainWindow(TaskService service)
    {
        InitializeComponent(); this.service = service;
        Topmost = service.State.Pinned;
        var area = SystemParameters.WorkArea;
        Left = Math.Clamp(service.State.Left ?? area.Right - Width - 25, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(service.State.Top ?? area.Top + 45, area.Top, Math.Max(area.Top, area.Bottom - Height));
        SourceInitialized += (_, _) => NativeWindow.Round(this);
        service.Changed += Refresh;
        Language = Loc.Xml;
        // Tab labels change width with the language, so the highlight is re-measured after layout.
        Loc.Changed += () => { Language = Loc.Xml; Refresh(); Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, PlaceTabPill); };
        Closing += OnClosing;
        // Reopening from the tray replays the entrance.
        IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) { shown.Clear(); Motion.Enter(Root, y: 10, ms: 240); Refresh(); } };
        Refresh();
    }
    public void Refresh()
    {
        var tasks = service.State.Tasks.Where(t => t.Completed == completed).OrderBy(t => t.DueDate ?? DateTime.MaxValue).ThenBy(t => t.AlarmAt).ToList();
        TaskList.ItemsSource = tasks;
        bool wasEmpty = EmptyState.Visibility == Visibility.Visible;
        EmptyState.Visibility = tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (tasks.Count == 0 && !wasEmpty) Motion.Zoom(EmptyState, 0.9);
        EmptyIcon.Text = completed ? "\uE73E" : "\uE8FB";
        EmptyTitle.Text = Loc.T(completed ? "EmptyDoneTitle" : "EmptyTitle");
        EmptyText.Text = Loc.T(completed ? "EmptyDoneText" : "EmptyText");
        PendingTab.Content = Loc.T("TabPending");
        DoneTab.Content = Loc.T("TabDone");
        StyleTab(PendingTab, !completed);
        StyleTab(DoneTab, completed);
        PinButton.Foreground = Topmost ? Brushes.White : new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));
        PinButton.Content = Topmost ? "\uE840" : "\uE718";
        if (tabPlaced) PlaceTabPill();
        // Containers exist after layout; stagger the cards that just appeared.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            int order = 0;
            for (int i = 0; i < tasks.Count; i++)
            {
                if (shown.Contains(tasks[i].Id) || TaskList.ItemContainerGenerator.ContainerFromIndex(i) is not UIElement card) continue;
                Motion.Enter(card, x: slideFrom * 24, y: slideFrom == 0 ? 14 : 0, ms: 280, delay: Math.Min(order++, 6) * 45);
            }
            shown.Clear(); foreach (var t in tasks) shown.Add(t.Id);
            slideFrom = 0;
            if (!tabPlaced) PlaceTabPill();
        });
    }
    private void PlaceTabPill()
    {
        var active = completed ? DoneTab : PendingTab;
        if (active.ActualWidth == 0) return;
        double x = active.TranslatePoint(new Point(0, 0), TabPill.Parent as UIElement).X;
        Motion.Glide(TabPill, x, active.ActualWidth, tabPlaced);
        tabPlaced = true;
    }
    private void StyleTab(Button tab, bool active)
    {
        var target = active ? ((SolidColorBrush)FindResource("Ink")).Color : Color.FromArgb(215, 255, 255, 255);
        // Cross-fade the label colour while the highlight glides underneath.
        if (tabPlaced && Motion.Enabled && tab.Foreground is SolidColorBrush current && current.Color != target)
        {
            var brush = new SolidColorBrush(current.Color); tab.Foreground = brush;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new System.Windows.Media.Animation.ColorAnimation(target, TimeSpan.FromMilliseconds(220)));
        }
        else if (tab.Foreground is not SolidColorBrush same || same.Color != target) tab.Foreground = new SolidColorBrush(target);
        tab.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
    }
    private void DragHeader(object sender, MouseButtonEventArgs e)
    {
        // The whole header, including its padding, is a drag surface. Ignore
        // button template children as well as the buttons themselves.
        for (var source = e.OriginalSource as DependencyObject; source != null; source = VisualTreeHelper.GetParent(source))
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase) return;
            if (ReferenceEquals(source, sender)) break;
        }
        if (e.LeftButton == MouseButtonState.Pressed) { e.Handled = true; DragMove(); service.State.Left = Left; service.State.Top = Top; Try(service.SaveSettings); }
    }
    // Switching tabs slides the new list in from the side of the chosen tab.
    private void ShowPending(object sender, RoutedEventArgs e) { if (!completed) return; completed = false; shown.Clear(); slideFrom = -1; Refresh(); }
    private void ShowDone(object sender, RoutedEventArgs e) { if (completed) return; completed = true; shown.Clear(); slideFrom = 1; Refresh(); }
    private void TogglePin(object sender, RoutedEventArgs e) { Topmost = !Topmost; service.State.Pinned = Topmost; Try(service.SaveSettings); Refresh(); }
    private void HideWidget(object sender, RoutedEventArgs e) => Motion.Exit(Root, () => { Hide(); Root.BeginAnimation(OpacityProperty, null); Root.RenderTransform = Transform.Identity; }, y: 8, ms: 140);
    private void AddTask(object sender, RoutedEventArgs e) => OpenEditor(null);
    private void EditTask(object sender, MouseButtonEventArgs e) => Edit((TaskItem)((FrameworkElement)sender).DataContext);
    private void EditTaskButton(object sender, RoutedEventArgs e) => Edit((TaskItem)((FrameworkElement)sender).DataContext);
    private void Edit(TaskItem task) => OpenEditor(task);
    // The editor replaces the list inside the widget instead of opening a window.
    public void OpenEditor(TaskItem? task)
    {
        if (EditorHost.Content is EditorPanel) return;
        var editor = new EditorPanel(service, task);
        editor.Done += () => Motion.Exit(EditorHost, () =>
        {
            EditorHost.Content = null; EditorHost.BeginAnimation(OpacityProperty, null);
            Tabs.Visibility = Visibility.Visible; Motion.Enter(Tabs, y: -4, ms: 200);
            shown.Clear(); Refresh();
            // The backdrop dissolves while the cards stagger back in.
            Motion.Exit(EditorShade, () => { if (EditorHost.Content == null) EditorShade.Visibility = Visibility.Collapsed; EditorShade.BeginAnimation(OpacityProperty, null); }, y: 0, ms: 160);
        }, y: 18, ms: 150);
        EditorHost.Content = editor;
        EditorShade.Visibility = Visibility.Visible;
        Motion.Enter(EditorShade, y: 0, ms: 100);
        Motion.Enter(EditorHost, y: 26, ms: 320, delay: 80);
        Tabs.Visibility = Visibility.Hidden;
    }
    private void CompleteTask(object sender, RoutedEventArgs e)
    {
        var task = (TaskItem)((FrameworkElement)sender).DataContext;
        var card = TaskList.ItemContainerGenerator.ContainerFromItem(task) as FrameworkElement;
        // A repeating task stays and moves to its next occurrence; others slide away first.
        if (card == null || (task.Repeat != null && !task.Completed)) { Try(() => service.Complete(task)); Refresh(); if (card != null) Motion.Zoom(card, 0.97); return; }
        card.IsHitTestVisible = false;
        Motion.Exit(card, () => Motion.Collapse(card, () => { Try(() => service.Complete(task)); Refresh(); }), x: completed ? -40 : 40, y: 0, ms: 200);
    }
    private void Settings(object sender, RoutedEventArgs e)
    {
        // The gear toggles the menu. Clicking it while open closes the menu on mouse-down,
        // so a click that arrives right after that close must not reopen it.
        if (settingsMenu?.IsOpen == true) { settingsMenu.IsOpen = false; return; }
        if (settingsClosedOnGear) { settingsClosedOnGear = false; return; }
        Try(service.RefreshStartup);
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, HorizontalOffset = -210 };
        MenuItem Item(string glyph, string header, string? hint = null)
        {
            var item = new MenuItem { Header = header, Icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 14 } };
            if (hint != null) item.InputGestureText = hint;
            menu.Items.Add(item);
            return item;
        }
        menu.Items.Add(new MenuItem { Header = Loc.T("Preferences"), IsEnabled = false, Style = (Style)FindResource("MenuHeading") });
        var startup = Item("", Loc.T("StartWithWindows"));
        startup.IsCheckable = true; startup.IsChecked = service.State.StartupEnabled;
        startup.Click += (_, _) => {
            if (!service.State.StartupEnabled && !IntegrationConsent.Ensure(this, service)) return;
            Try(() => service.SetStartup(!service.State.StartupEnabled));
        };
        var integration = Item("", Loc.T(service.State.SchedulerConsent ? "DisableIntegration" : "AllowScheduled"));
        integration.Click += (_, _) => { if (service.State.SchedulerConsent) Try(service.DisableIntegration); else IntegrationConsent.Ensure(this, service); };
        var folder = Item("", Loc.T("MusicFolder"), service.State.MusicFolder is { } f ? System.IO.Path.GetFileName(f.TrimEnd('\\')) : null);
        folder.ToolTip = service.State.MusicFolder ?? Loc.T("MusicFolderTip");
        folder.Click += (_, _) => {
            var picker = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("MusicFolderTitle"), InitialDirectory = service.State.MusicFolder ?? "" };
            if (picker.ShowDialog(this) == true) { service.State.MusicFolder = picker.FolderName; Try(service.SaveSettings); }
        };
        var command = Item("", Loc.T("AddToPath"));
        command.ToolTip = Loc.T("AddToPathTip");
        command.IsCheckable = true; command.IsChecked = CommandPath.IsEnabled();
        command.Click += (_, _) => Try(() => CommandPath.Set(command.IsChecked));
        // Language picker: a small segmented control inside one menu row.
        var languages = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0) };
        foreach (var (code, name) in Loc.Languages)
        {
            bool active = code == Loc.Language;
            var choice = new Button { Content = code.ToUpperInvariant(), ToolTip = name, FontSize = 11, Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(2, 0, 0, 0), Focusable = false,
                Background = active ? (Brush)FindResource("Accent") : Brushes.Transparent, Foreground = active ? Brushes.White : (Brush)FindResource("Ink"), FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal };
            AutomationProperties.SetName(choice, name);
            choice.Click += (_, _) => { menu.IsOpen = false; if (active) return; service.State.Language = code; Try(service.SaveSettings); Loc.Set(code); };
            languages.Children.Add(choice);
        }
        var languageRow = new DockPanel();
        DockPanel.SetDock(languages, Dock.Right);
        languageRow.Children.Add(languages);
        languageRow.Children.Add(new TextBlock { Text = Loc.T("Language"), VerticalAlignment = VerticalAlignment.Center });
        var language = Item("", "");
        language.Header = languageRow; language.StaysOpenOnClick = true;
        menu.Items.Add(new Separator());
        var test = Item("", Loc.T("TryAlert"));
        test.Click += (_, _) => new AlarmWindow(new TaskItem { Title = Loc.T("SampleTitle"), Description = Loc.T("SampleText"), AlarmAt = DateTimeOffset.Now }, service, true).Show();
        menu.Items.Add(new Separator());
        var quit = Item("", Loc.T("Quit")); quit.Click += (_, _) => Application.Current.Shutdown();
        menu.Opened += (_, _) => Motion.Zoom(menu, 0.92, new Point(0.85, 0), 260);
        var gear = (FrameworkElement)sender;
        menu.Closed += (_, _) => { settingsClosedOnGear = Mouse.LeftButton == MouseButtonState.Pressed && new Rect(gear.RenderSize).Contains(Mouse.GetPosition(gear)); settingsMenu = null;
            // Forget the flag once that click has been handled, even if released elsewhere.
            if (settingsClosedOnGear) { MouseButtonEventHandler? clear = null; clear = (_, _) => { PreviewMouseLeftButtonUp -= clear; Dispatcher.BeginInvoke(() => settingsClosedOnGear = false, System.Windows.Threading.DispatcherPriority.Background); }; PreviewMouseLeftButtonUp += clear; }
        };
        settingsMenu = menu;
        menu.IsOpen = true;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        service.State.Left = Left; service.State.Top = Top; Try(service.SaveSettings);
        // Closing the widget keeps it available through the tray. Application.Shutdown
        // ignores cancellation, so the explicit Exit action still ends the process.
        e.Cancel = true; Hide();
    }
    public static void Try(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(Loc.T("SaveChangeFailed") + "\n\n" + ex.Message, "Reminder", MessageBoxButton.OK, MessageBoxImage.Warning); } }
}
