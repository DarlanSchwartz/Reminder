using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Reminder;

public partial class MainWindow : Window
{
    private readonly TaskService service;
    private bool completed;
    public MainWindow(TaskService service)
    {
        InitializeComponent(); this.service = service;
        Topmost = service.State.Pinned;
        var area = SystemParameters.WorkArea;
        Left = Math.Clamp(service.State.Left ?? area.Right - Width - 25, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = Math.Clamp(service.State.Top ?? area.Top + 45, area.Top, Math.Max(area.Top, area.Bottom - Height));
        SourceInitialized += (_, _) => NativeWindow.Round(this);
        service.Changed += Refresh;
        Closing += OnClosing;
        Refresh();
    }
    public void Refresh()
    {
        var tasks = service.State.Tasks.Where(t => t.Completed == completed).OrderBy(t => t.DueDate ?? DateTime.MaxValue).ThenBy(t => t.AlarmAt).ToList();
        int pending = service.State.Tasks.Count(t => !t.Completed), done = service.State.Tasks.Count - pending;
        TaskList.ItemsSource = tasks;
        EmptyState.Visibility = tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyIcon.Text = completed ? "" : "";
        EmptyTitle.Text = completed ? "Nada concluído ainda" : "Tudo em dia";
        EmptyText.Text = completed ? "Tarefas marcadas como feitas aparecem aqui." : "Crie uma tarefa e, se quiser, um alarme que se repete.";
        PendingTab.Content = pending > 0 ? $"A fazer  ·  {pending}" : "A fazer";
        DoneTab.Content = done > 0 ? $"Concluídas  ·  {done}" : "Concluídas";
        StyleTab(PendingTab, !completed);
        StyleTab(DoneTab, completed);
        PinButton.Foreground = Topmost ? Brushes.White : new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));
        PinButton.Content = Topmost ? "" : "";
    }
    private void StyleTab(Button tab, bool active)
    {
        tab.Background = active ? Brushes.White : Brushes.Transparent;
        tab.Foreground = active ? (Brush)FindResource("Ink") : new SolidColorBrush(Color.FromArgb(215, 255, 255, 255));
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
    private void ShowPending(object sender, RoutedEventArgs e) { completed = false; Refresh(); }
    private void ShowDone(object sender, RoutedEventArgs e) { completed = true; Refresh(); }
    private void TogglePin(object sender, RoutedEventArgs e) { Topmost = !Topmost; service.State.Pinned = Topmost; Try(service.SaveSettings); Refresh(); }
    private void HideWidget(object sender, RoutedEventArgs e) => Hide();
    private void AddTask(object sender, RoutedEventArgs e) => OpenEditor(null);
    private void EditTask(object sender, MouseButtonEventArgs e) => Edit((TaskItem)((FrameworkElement)sender).DataContext);
    private void EditTaskButton(object sender, RoutedEventArgs e) => Edit((TaskItem)((FrameworkElement)sender).DataContext);
    private void Edit(TaskItem task) => OpenEditor(task);
    // The editor replaces the list inside the widget instead of opening a window.
    public void OpenEditor(TaskItem? task)
    {
        var editor = new EditorPanel(service, task);
        editor.Done += () => { EditorHost.Content = null; EditorHost.Visibility = Visibility.Collapsed; Tabs.Visibility = Visibility.Visible; Refresh(); };
        EditorHost.Content = editor;
        EditorHost.Visibility = Visibility.Visible;
        Tabs.Visibility = Visibility.Hidden;
    }
    private void CompleteTask(object sender, RoutedEventArgs e)
    {
        var task = (TaskItem)((FrameworkElement)sender).DataContext;
        Try(() => service.Complete(task)); Refresh();
    }
    private void Settings(object sender, RoutedEventArgs e)
    {
        Try(service.RefreshStartup);
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, HorizontalOffset = -210 };
        MenuItem Item(string glyph, string header, string? hint = null)
        {
            var item = new MenuItem { Header = header, Icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 14 } };
            if (hint != null) item.InputGestureText = hint;
            menu.Items.Add(item);
            return item;
        }
        menu.Items.Add(new MenuItem { Header = "PREFERÊNCIAS", IsEnabled = false, Style = (Style)FindResource("MenuHeading") });
        var startup = Item("", "Iniciar com o Windows");
        startup.IsCheckable = true; startup.IsChecked = service.State.StartupEnabled;
        startup.Click += (_, _) => {
            if (!service.State.StartupEnabled && !IntegrationConsent.Ensure(this, service)) return;
            Try(() => service.SetStartup(!service.State.StartupEnabled));
        };
        var integration = Item("", service.State.SchedulerConsent ? "Desativar integração com o Windows" : "Permitir alarmes agendados…");
        integration.Click += (_, _) => { if (service.State.SchedulerConsent) Try(service.DisableIntegration); else IntegrationConsent.Ensure(this, service); };
        var folder = Item("", "Pasta de músicas…", service.State.MusicFolder is { } f ? System.IO.Path.GetFileName(f.TrimEnd('\\')) : null);
        folder.ToolTip = service.State.MusicFolder ?? "Pasta onde o seletor de músicas abre por padrão";
        folder.Click += (_, _) => {
            var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Pasta de músicas do Reminder", InitialDirectory = service.State.MusicFolder ?? "" };
            if (picker.ShowDialog(this) == true) { service.State.MusicFolder = picker.FolderName; Try(service.SaveSettings); }
        };
        menu.Items.Add(new Separator());
        var test = Item("", "Experimentar o alerta");
        test.Click += (_, _) => new AlarmWindow(new TaskItem { Title = "Hora de cuidar do que importa.", Description = "Este é um teste. Seus alarmes aparecerão assim.", AlarmAt = DateTimeOffset.Now }, service, true).Show();
        menu.Items.Add(new Separator());
        var quit = Item("", "Sair do Reminder"); quit.Click += (_, _) => Application.Current.Shutdown();
        menu.IsOpen = true;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        service.State.Left = Left; service.State.Top = Top; Try(service.SaveSettings);
        // Closing the widget keeps it available through the tray. Application.Shutdown
        // ignores cancellation, so the explicit Exit action still ends the process.
        e.Cancel = true; Hide();
    }
    public static void Try(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show("Não foi possível salvar a alteração.\n\n" + ex.Message, "Reminder", MessageBoxButton.OK, MessageBoxImage.Warning); } }
}

internal static class IntegrationConsent
{
    public static bool Ensure(Window owner, TaskService service)
    {
        if (service.State.SchedulerConsent) return true;
        var answer = MessageBox.Show(owner, "Para iniciar com o Windows e avisar mesmo com o widget fechado, o Reminder precisa registrar tarefas no Agendador do Windows.\n\nO próprio Windows grava esses registros internos no disco do sistema (normalmente C:). O aplicativo e suas tarefas pessoais continuam nesta pasta no G:.\n\nVocê autoriza esses registros do Windows?", "Ativar integração com o Windows", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return false;
        try { service.EnableIntegration(); return true; }
        catch (Exception ex) { MessageBox.Show(owner, "Não foi possível ativar a integração.\n" + ex.Message, "Reminder"); return false; }
    }
}
