using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Reminder;

public partial class EditorPanel : UserControl
{
    private readonly TaskService service;
    private readonly TaskItem? original;
    private readonly DateTime? initialDate;
    private readonly string initialTime;
    private bool formattingTime;
    private string? musicPath;
    private string? musicName;
    private string? musicFolder;
    private AlarmAudio? musicPreview;
    private int step = 1;
    public event Action? Done;
    private Window Owner => Window.GetWindow(this);
    public EditorPanel(TaskService service, TaskItem? task)
    {
        InitializeComponent(); this.service = service; original = task;
        musicPath = task?.SoundPath; musicName = task?.SoundName; musicFolder = task?.SoundFolder;
        UpdateMusicLabel();
        TitleInput.Text = task?.Title ?? ""; DescriptionInput.Text = task?.Description ?? "";
        DueInput.SelectedDate = task?.DueDate;
        AlarmDate.SelectedDate = initialDate = task?.AlarmAt?.LocalDateTime.Date ?? DateTime.Now.AddHours(1).Date;
        AlarmTime.Text = initialTime = (task?.AlarmAt?.LocalDateTime ?? DateTime.Now.AddHours(1)).ToString("HH:mm");
        AlarmEnabled.IsChecked = task?.AlarmAt != null && task?.AlertShown != true && task?.Completed != true;
        if (task?.Repeat is { } repeat)
        {
            RepeatMode.SelectedIndex = (int)repeat.Unit + 1;
            IntervalInput.Text = repeat.Interval.ToString(CultureInfo.InvariantCulture);
            foreach (var chip in DayChips.Children.OfType<ToggleButton>()) chip.IsChecked = (repeat.Days & Recurrence.Flag(DayOf(chip))) != 0;
            if (repeat.Until is { } until) { EndMode.SelectedIndex = 1; UntilDate.SelectedDate = until; }
        }
        DeleteButton.Visibility = task == null ? Visibility.Collapsed : Visibility.Visible;
        Loaded += (_, _) => TitleInput.Focus();
        DataObject.AddPastingHandler(AlarmTime, PasteAlarmTime);
        ValidateAlarm();
        ShowStep();
        Unloaded += (_, _) => StopMusic();
    }

    // Without an alarm only the first two steps apply.
    private int LastStep => AlarmEnabled.IsChecked == true ? 4 : 2;
    private void ShowStep(int direction = 0)
    {
        if (SaveButton == null) return;
        step = Math.Min(step, LastStep);
        var panels = new FrameworkElement[] { TaskStep, WhenStep, RepeatStep, SoundStep };
        for (int i = 0; i < panels.Length; i++) panels[i].Visibility = i + 1 == step ? Visibility.Visible : Visibility.Collapsed;
        // Steps slide in from the side you are moving towards.
        if (direction != 0) Motion.Enter(panels[step - 1], x: 36 * direction, y: 0, ms: 280);
        Progress.Children.Clear();
        for (int i = 1; i <= LastStep; i++)
        {
            var bar = new System.Windows.Controls.Border { Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, i < LastStep ? 5 : 0, 0), Background = (System.Windows.Media.Brush)FindResource(i <= step ? "Accent" : "Line") };
            Progress.Children.Add(bar);
            if (direction > 0 && i == step) Motion.Fill(bar);
        }
        bool last = step == LastStep;
        NextButton.Visibility = last ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.Visibility = last ? Visibility.Visible : Visibility.Collapsed;
        NextButton.IsDefault = !last; SaveButton.IsDefault = last;
        BackButton.Content = step == 1 ? "Cancelar" : "Voltar";
        BackButton.IsCancel = step == 1;
        DeleteButton.Visibility = original != null && step == 1 ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Text = "";
        ValidateAlarm();
    }
    private void Next(object sender, RoutedEventArgs e)
    {
        if (step == 1 && string.IsNullOrWhiteSpace(TitleInput.Text)) { ErrorText.Text = "Dê um nome para esta tarefa."; Motion.Shake(ErrorText); TitleInput.Focus(); return; }
        if (step == 2 && (AlarmDate.SelectedDate == null || !AlarmTimeParser.TryParse(AlarmTime.Text, out _))) { ErrorText.Text = "Escolha a data e um horário como 14:30."; Motion.Shake(ErrorText); return; }
        if (step == 3 && ReadAlarm(out string error) == null) { ErrorText.Text = error; Motion.Shake(ErrorText); return; }
        step++; ShowStep(1);
    }
    private void Back(object sender, RoutedEventArgs e)
    {
        if (step == 1) { StopMusic(); Done?.Invoke(); return; }
        step--; ShowStep(-1);
    }
    private void SetAlarm(DateTime when) { AlarmDate.SelectedDate = when.Date; AlarmTime.Text = when.ToString("HH:mm", CultureInfo.InvariantCulture); }
    private void QuickInHour(object sender, RoutedEventArgs e) => SetAlarm(DateTime.Now.AddHours(1));
    private void QuickTonight(object sender, RoutedEventArgs e) => SetAlarm(DateTime.Today.AddHours(DateTime.Now.Hour >= 20 ? 44 : 20));
    private void QuickTomorrow(object sender, RoutedEventArgs e) => SetAlarm(DateTime.Today.AddDays(1).AddHours(9));
    private void UpdateMusicLabel()
    {
        if (musicFolder != null) { MusicIcon.Text = ""; MusicName.Text = "Aleatória · " + Path.GetFileName(musicFolder.TrimEnd('\\')); MusicHint.Text = musicFolder; }
        else if (musicPath != null) { MusicIcon.Text = ""; MusicName.Text = musicName ?? Path.GetFileName(musicPath); MusicHint.Text = "Repete enquanto o alarme toca"; }
        else { MusicIcon.Text = ""; MusicName.Text = "Som padrão"; MusicHint.Text = "Alerta do Windows, repetido"; }
        MusicName.ToolTip = MusicName.Text;
    }

    private static DayOfWeek DayOf(ToggleButton chip) => (DayOfWeek)int.Parse((string)chip.Tag, CultureInfo.InvariantCulture);
    private RepeatUnit? Unit => RepeatMode?.SelectedIndex is int i and > 0 ? (RepeatUnit)(i - 1) : null;
    private int? Interval => int.TryParse(IntervalInput?.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n is >= 1 and <= 999 ? n : null;
    private WeekDays Days => DayChips.Children.OfType<ToggleButton>().Where(c => c.IsChecked == true).Aggregate(WeekDays.None, (all, c) => all | Recurrence.Flag(DayOf(c)));

    // Builds the series from the form. An unchanged first occurrence keeps the
    // original anchor, so e.g. "day 31" survives editing while February is shown.
    private Recurrence? BuildRepeat(DateTime start)
    {
        if (Unit is not { } unit || Interval is not { } interval) return null;
        var days = unit == RepeatUnit.Week ? Days : WeekDays.None;
        var until = EndMode.SelectedIndex == 1 ? UntilDate.SelectedDate?.Date : null;
        if (original?.Repeat is { } old && old.Unit == unit && old.Interval == interval && old.Days == days && AlarmDate.SelectedDate == initialDate && AlarmTime.Text == initialTime)
            start = old.Start;
        return new Recurrence { Unit = unit, Interval = interval, Days = days, Start = start, Until = until };
    }

    private static DateTimeOffset? FirstOccurrence(Recurrence repeat, DateTimeOffset chosen)
    {
        var now = DateTimeOffset.Now;
        return repeat.Next(chosen > now ? chosen.AddTicks(-1) : now);
    }

    private void RepeatChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RepeatFields == null || UntilDate == null) return;
        Reveal(RepeatFields, Unit != null);
        Reveal(WeekFields, Unit == RepeatUnit.Week);
        Reveal(UntilDate, EndMode.SelectedIndex == 1);
        if (EndMode.SelectedIndex == 1 && UntilDate.SelectedDate == null) UntilDate.SelectedDate = (AlarmDate.SelectedDate ?? DateTime.Today).AddMonths(1);
        if (Unit == RepeatUnit.Week && Days == WeekDays.None && AlarmDate.SelectedDate is { } date)
            foreach (var chip in DayChips.Children.OfType<ToggleButton>()) chip.IsChecked = DayOf(chip) == date.DayOfWeek;
        ValidateAlarm();
    }
    private void RepeatInputChanged(object sender, TextChangedEventArgs e) => ValidateAlarm();
    private void DayToggled(object sender, RoutedEventArgs e) => ValidateAlarm();
    private void StepInterval(int delta) { IntervalInput.Text = Math.Clamp((Interval ?? 1) + delta, 1, 999).ToString(CultureInfo.InvariantCulture); }
    private void DecreaseInterval(object sender, RoutedEventArgs e) => StepInterval(-1);
    private void IncreaseInterval(object sender, RoutedEventArgs e) => StepInterval(1);
    private void PickDays(Func<DayOfWeek, bool> pick) { foreach (var chip in DayChips.Children.OfType<ToggleButton>()) chip.IsChecked = pick(DayOf(chip)); ValidateAlarm(); }
    private void PickWeekdays(object sender, RoutedEventArgs e) => PickDays(d => d is not (DayOfWeek.Saturday or DayOfWeek.Sunday));
    private void PickWeekend(object sender, RoutedEventArgs e) => PickDays(d => d is DayOfWeek.Saturday or DayOfWeek.Sunday);
    private void PickAllDays(object sender, RoutedEventArgs e) => PickDays(_ => true);
    private void FilterDigits(object sender, TextCompositionEventArgs e) => e.Handled = !e.Text.All(char.IsAsciiDigit);

    private void ChooseMusic(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Title = "Escolher música do lembrete", Filter = "Áudio (MP3, WAV)|*.mp3;*.wav", CheckFileExists = true, InitialDirectory = service.State.MusicFolder ?? "" };
        if (picker.ShowDialog(Owner) != true) return;
        StopMusic(); ErrorText.Text = ""; musicPath = picker.FileName; musicName = Path.GetFileName(picker.FileName); musicFolder = null; UpdateMusicLabel();
    }
    private void ChooseFolder(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Pasta com músicas (MP3 ou WAV)", InitialDirectory = musicFolder ?? service.State.MusicFolder ?? "" };
        if (picker.ShowDialog(Owner) != true) return;
        StopMusic();
        if (SoundLibrary.PickRandom(picker.FolderName) == null) { ErrorText.Text = "Essa pasta não tem arquivos MP3 ou WAV."; return; }
        ErrorText.Text = ""; musicFolder = picker.FolderName; UpdateMusicLabel(); ValidateAlarm();
    }
    private void StopMusic() { musicPreview?.Dispose(); musicPreview = null; PreviewMusicButton.Content = ""; }
    private void ResetMusic(object sender, RoutedEventArgs e) { StopMusic(); ErrorText.Text = ""; musicPath = null; musicName = null; musicFolder = null; UpdateMusicLabel(); ValidateAlarm(); }
    private void PreviewMusic(object sender, RoutedEventArgs e)
    {
        if (musicPreview != null) { StopMusic(); return; }
        ErrorText.Text = "";
        musicPreview = new AlarmAudio();
        musicPreview.PlaybackFailed += () => ErrorText.Text = "Falha ao reproduzir: " + musicPreview.LastError;
        PreviewMusicButton.Content = "";
        musicPreview.Play(musicFolder != null ? SoundLibrary.PickRandom(musicFolder) : musicPath);
    }
    private void ToggleAlarm(object sender, RoutedEventArgs e) { if (AlarmFields != null) Reveal(AlarmFields, AlarmEnabled.IsChecked == true); ShowStep(); }
    // Shows or hides an optional section; newly shown sections slide down into place.
    private static void Reveal(UIElement e, bool show)
    {
        bool appearing = show && e.Visibility != Visibility.Visible;
        e.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (appearing && e.IsVisible) Motion.Enter(e, y: -8, ms: 240);
    }
    private void FilterAlarmTime(object sender, TextCompositionEventArgs e) => e.Handled = !AlarmTimeParser.HasAllowedCharacters(e.Text);
    private void PasteAlarmTime(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText) || e.SourceDataObject.GetData(DataFormats.UnicodeText) is not string text || !AlarmTimeParser.TryParse(text, out var time))
        {
            e.CancelCommand();
            AlarmError.Text = "Cole um horário válido, como 09:30 ou 930.";
            AlarmError.Visibility = Visibility.Visible;
            return;
        }
        e.CancelCommand();
        AlarmTime.Text = time.ToString("HH:mm", CultureInfo.InvariantCulture);
        AlarmTime.CaretIndex = AlarmTime.Text.Length;
    }
    private void AlarmTimeChanged(object sender, TextChangedEventArgs e)
    {
        if (formattingTime) return;
        var input = (TextBox)sender;
        // Insert the separator once all four digits are present; keep partial input
        // editable so Backspace, selection and mid-string corrections behave normally.
        if (input.Text.Length == 4 && !input.Text.Contains(':') && AlarmTimeParser.TryParse(input.Text, out var time))
        {
            int caret = input.CaretIndex;
            formattingTime = true;
            try { input.Text = time.ToString("HH:mm", CultureInfo.InvariantCulture); input.CaretIndex = Math.Min(caret > 2 ? caret + 1 : caret, 5); }
            finally { formattingTime = false; }
        }
        ValidateAlarm();
    }
    private void NormalizeAlarmTime(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (AlarmTimeParser.TryParse(AlarmTime.Text, out var time)) AlarmTime.Text = time.ToString("HH:mm", CultureInfo.InvariantCulture);
        ValidateAlarm();
    }
    private void AlarmDateChanged(object? sender, SelectionChangedEventArgs e) => ValidateAlarm();

    // Validates the alarm section and returns the first occurrence and series.
    private (DateTimeOffset alarm, Recurrence? repeat)? ReadAlarm(out string error)
    {
        error = "";
        if (!AlarmTimeParser.TryParse(AlarmTime.Text, out var time)) { error = "Informe um horário entre 00:00 e 23:59."; return null; }
        if (AlarmDate.SelectedDate is not { } date) { error = "Escolha a data do alarme."; return null; }
        var local = DateTime.SpecifyKind(date.Date + time.ToTimeSpan(), DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(local)) { error = "Esse horário não existe no fuso local."; return null; }
        var chosen = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        if (Unit == null)
        {
            if (chosen <= DateTimeOffset.Now) { error = "Escolha uma data e um horário no futuro."; return null; }
            return (chosen, null);
        }
        if (Interval == null) { error = "Informe um intervalo de 1 a 999."; return null; }
        if (Unit == RepeatUnit.Week && Days == WeekDays.None) { error = "Escolha pelo menos um dia da semana."; return null; }
        if (EndMode.SelectedIndex == 1 && UntilDate.SelectedDate == null) { error = "Escolha até quando repetir."; return null; }
        var repeat = BuildRepeat(local)!;
        // Keep a pending snooze or the current occurrence when nothing relevant changed.
        var alarm = original?.Repeat == repeat && original.AlarmAt > DateTimeOffset.Now && !original.AlertShown ? original.AlarmAt : FirstOccurrence(repeat, chosen);
        if (alarm is not { } first) { error = "A repetição termina antes do próximo aviso."; return null; }
        return (first, repeat);
    }

    private bool ValidateAlarm()
    {
        if (AlarmError == null || SaveButton == null || RepeatSummary == null || EndMode == null) return false;
        if (IntervalUnit != null)
        {
            bool one = Interval == 1;
            IntervalUnit.Text = Unit switch { RepeatUnit.Hour => one ? "hora" : "horas", RepeatUnit.Day => one ? "dia" : "dias", RepeatUnit.Week => one ? "semana" : "semanas", RepeatUnit.Month => one ? "mês" : "meses", _ => one ? "ano" : "anos" };
        }
        string error = "";
        var read = AlarmEnabled.IsChecked == true ? ReadAlarm(out error) : null;
        Reveal(SummaryBox, Unit != null);
        if (read is { repeat: { } repeat } result)
        {
            RepeatSummary.Text = repeat.Describe();
            NextSummary.Text = "Próximo aviso: " + result.alarm.LocalDateTime.ToString("dddd, dd/MM 'às' HH:mm");
        }
        else if (Unit != null) { RepeatSummary.Text = "Complete os campos para ver a repetição."; NextSummary.Text = ""; }
        if (FinalSummary != null && read is { } r)
            FinalSummary.Text = $"{TitleInput.Text.Trim()}\nAviso: {r.alarm.LocalDateTime:dddd, dd/MM 'às' HH:mm}\n{(r.repeat?.Describe() ?? "Não repete.")}\nSom: {MusicName.Text}";
        AlarmError.Text = error;
        AlarmError.Visibility = error.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.IsEnabled = error.Length == 0;
        return error.Length == 0;
    }

    private void Save(object sender, RoutedEventArgs e)
    {
        if (!ValidateAlarm()) return;
        if (string.IsNullOrWhiteSpace(TitleInput.Text)) { ErrorText.Text = "Dê um nome para esta tarefa."; Motion.Shake(ErrorText); TitleInput.Focus(); return; }
        DateTimeOffset? alarm = null;
        Recurrence? repeat = null;
        if (AlarmEnabled.IsChecked == true)
        {
            if (ReadAlarm(out string error) is not { } result) { ErrorText.Text = error; return; }
            (alarm, repeat) = result;
            if (!IntegrationConsent.Ensure(Owner, service)) { ErrorText.Text = "Para salvar sem integração, desmarque o alarme."; return; }
        }
        try {
            StopMusic();
            string? storedMusic = musicPath == null ? null : new SoundLibrary(Path.Combine(AppContext.BaseDirectory, "data", "sounds")).Import(musicPath);
            service.Put((original ?? new TaskItem()) with { Title = TitleInput.Text.Trim(), Description = DescriptionInput.Text.Trim(), DueDate = DueInput.SelectedDate, AlarmAt = alarm, Repeat = repeat, SoundPath = storedMusic, SoundName = musicName, SoundFolder = musicFolder, AlertShown = false, Completed = alarm != null ? false : original?.Completed ?? false });
            Done?.Invoke();
        } catch (Exception ex) { ErrorText.Text = "Não foi possível salvar: " + ex.Message; }
    }
    private void Delete(object sender, RoutedEventArgs e)
    {
        if (original == null || MessageBox.Show(Owner, "Excluir esta tarefa?", "Reminder", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try { service.Delete(original); Done?.Invoke(); } catch (Exception ex) { ErrorText.Text = ex.Message; }
    }
}
