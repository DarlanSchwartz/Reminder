using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Reminder;

// Command-line interface, meant for scripts and AI agents. It returns text
// instead of printing so the same code runs in-process or inside the open app.
internal static class Cli
{
    private static readonly string[] Commands = ["add", "list", "show", "edit", "done", "reopen", "remove", "help", "--help", "-h", "/?"];
    public static bool IsCommand(string[] args) => args.Length > 0 && Commands.Contains(args[0].ToLowerInvariant());

    public const string Help = """
        Reminder CLI  ·  use reminder.cmd (or Reminder.exe) followed by a command.

          add <title> [options]         Create a task. Prints its id.
          list [--all | --done]         Pending tasks (default), every task, or completed ones.
          show <id>                     One task in detail.
          edit <id> [options]           Change a task. Only the options given are changed.
          done <id> [--series]          Complete. A repeating task moves to its next
                                        occurrence; --series ends the whole series.
          reopen <id>                   Mark a completed task as pending again.
          remove <id>                   Delete a task.

        Ids can be shortened to their first characters (as shown by list).
        Add --json to any command for machine-readable output.

        Options for add / edit
          --title <text>                (edit) New title.
          --desc <text>                 Description.
          --due <date>                  Due date (independent of the alarm).
          --at <when>                   Alarm time. Examples: 18:30 (next 18:30), "2026-11-05 09:00",
                                        "05/11/2026 09:00", "tomorrow 08:00", today, +30m, +2h, +1d.
          --every <unit>                Repeat: hour, day, week, month, year,
                                        weekday (Mon–Fri) or weekend. Needs an alarm.
          --interval <n>                Every n units (default 1), e.g. --every week --interval 2.
          --days <list>                 Weekdays for --every week: mon,tue,wed,thu,fri,sat,sun.
          --until <date>                Last day of the series (inclusive).
          --music <file>                MP3 or WAV played by the alarm.
          --music-folder <folder>       A random song from this folder on each alarm.
          --no-due  --no-alarm  --no-repeat  --no-until  --no-music   (edit) clear that field.

        Dates: 2026-11-30, 30/11/2026, 30/11, today, tomorrow, +3d.

        Examples
          reminder add "Pagar aluguel" --at "2026-11-05 09:00" --every month --until 2027-06-05
          reminder add "Academia" --at 07:00 --every week --days mon,wed,fri
          reminder add "Beber água" --at +1h --every hour --interval 2 --until today
          reminder list --json
          reminder done 3fa2c1
        """;

    private static readonly HashSet<string> Flags = ["--json", "--all", "--done", "--series", "--no-due", "--no-alarm", "--no-repeat", "--no-until", "--no-music"];

    public static (int code, string output) Execute(TaskService service, string[] args, string soundsDirectory)
    {
        try { return Run(service, args, soundsDirectory); }
        catch (CliError e) { return (2, "Erro: " + e.Message + "\n"); }
        catch (Exception e) { return (1, "Erro: " + e.Message + "\n"); }
    }

    private sealed class CliError(string message) : Exception(message);

    private static (int, string) Run(TaskService service, string[] args, string soundsDirectory)
    {
        string command = args[0].ToLowerInvariant();
        var options = new Dictionary<string, string>();
        var flags = new HashSet<string>();
        var positional = new List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            string a = args[i];
            if (Flags.Contains(a)) flags.Add(a);
            else if (a.StartsWith("--", StringComparison.Ordinal))
            {
                if (i + 1 >= args.Length) throw new CliError($"{a} precisa de um valor.");
                options[a] = args[++i];
            }
            else positional.Add(a);
        }
        bool json = flags.Contains("--json");
        var now = DateTime.Now;

        switch (command)
        {
            case "help" or "--help" or "-h" or "/?":
                return (0, Help + "\n");
            case "list":
                var tasks = service.State.Tasks.Where(t => flags.Contains("--all") || t.Completed == flags.Contains("--done"))
                    .OrderBy(t => t.Completed).ThenBy(t => t.AlarmAt ?? DateTimeOffset.MaxValue).ThenBy(t => t.DueDate ?? DateTime.MaxValue).ToList();
                if (json) return (0, JsonSerializer.Serialize(tasks.Select(ToJson), JsonOptions) + "\n");
                return (0, tasks.Count == 0 ? "Nenhuma tarefa.\n" : string.Concat(tasks.Select(t => Line(t) + "\n")));
            case "add":
            {
                if (positional.Count == 0 || string.IsNullOrWhiteSpace(positional[0])) throw new CliError("informe o título: add \"Título\" [opções].");
                var task = Apply(new TaskItem { Title = positional[0].Trim() }, options, flags, now, soundsDirectory, isNew: true);
                service.Put(task);
                return Report(service, task, json, "Criada");
            }
            case "edit":
            {
                var task = Find(service, positional);
                var updated = Apply(task, options, flags, now, soundsDirectory, isNew: false);
                service.Put(updated);
                return Report(service, updated, json, "Atualizada");
            }
            case "show":
            {
                var task = Find(service, positional);
                return json ? (0, JsonSerializer.Serialize(ToJson(task), JsonOptions) + "\n") : (0, Detail(task));
            }
            case "done":
            {
                var task = Find(service, positional);
                if (task.Completed) return (0, json ? JsonSerializer.Serialize(ToJson(task), JsonOptions) + "\n" : "Já estava concluída.\n");
                if (flags.Contains("--series") && task.Repeat != null) service.Put(task with { Completed = true, AlarmAt = null });
                else service.Complete(task);
                var after = service.State.Tasks.First(t => t.Id == task.Id);
                return Report(service, after, json, after.Completed ? "Concluída" : "Ocorrência concluída, próxima");
            }
            case "reopen":
            {
                var task = Find(service, positional);
                service.Put(task with { Completed = false });
                return Report(service, task with { Completed = false }, json, "Reaberta");
            }
            case "remove":
            {
                var task = Find(service, positional);
                service.Delete(task);
                return (0, json ? JsonSerializer.Serialize(new { removed = task.Id }, JsonOptions) + "\n" : $"Removida: {task.Title}\n");
            }
        }
        throw new CliError("comando desconhecido. Use help.");
    }

    private static (int, string) Report(TaskService service, TaskItem task, bool json, string verb)
    {
        if (json) return (0, JsonSerializer.Serialize(ToJson(task), JsonOptions) + "\n");
        var text = new StringBuilder($"{verb}: {Line(task)}\n");
        if (task.HasAlarm && !service.State.SchedulerConsent)
            text.Append("Aviso: a integração com o Agendador do Windows está desativada; o alarme toca apenas com o Reminder aberto.\n");
        return (0, text.ToString());
    }

    private static TaskItem Find(TaskService service, List<string> positional)
    {
        if (positional.Count == 0) throw new CliError("informe o id da tarefa (veja list).");
        string id = positional[0].Trim().ToLowerInvariant();
        var matches = service.State.Tasks.Where(t => t.Id.ToString("N").StartsWith(id.Replace("-", ""), StringComparison.Ordinal)).ToList();
        if (id.Length < 4 || matches.Count == 0) throw new CliError($"nenhuma tarefa com id {positional[0]}.");
        if (matches.Count > 1) throw new CliError($"id {positional[0]} é ambíguo; use mais caracteres.");
        return matches[0];
    }

    // Applies add/edit options. The alarm and repeat rule are rebuilt only when one of their options is given.
    private static TaskItem Apply(TaskItem task, Dictionary<string, string> o, HashSet<string> flags, DateTime now, string soundsDirectory, bool isNew)
    {
        foreach (var key in o.Keys)
            if (key is not ("--title" or "--desc" or "--due" or "--at" or "--every" or "--interval" or "--days" or "--until" or "--music" or "--music-folder"))
                throw new CliError($"opção desconhecida {key}.");
        if (o.TryGetValue("--title", out var title)) task = task with { Title = string.IsNullOrWhiteSpace(title) ? throw new CliError("o título não pode ficar vazio.") : title.Trim() };
        if (o.TryGetValue("--desc", out var desc)) task = task with { Description = desc.Trim() };
        if (o.TryGetValue("--due", out var due)) task = task with { DueDate = ParseDate(due, now) };
        if (flags.Contains("--no-due")) task = task with { DueDate = null };
        if (o.TryGetValue("--music", out var music))
        {
            if (!File.Exists(music) || Path.GetExtension(music).ToLowerInvariant() is not (".mp3" or ".wav")) throw new CliError("--music precisa de um arquivo MP3 ou WAV existente.");
            task = task with { SoundPath = new SoundLibrary(soundsDirectory).Import(Path.GetFullPath(music)), SoundName = Path.GetFileName(music), SoundFolder = null };
        }
        if (o.TryGetValue("--music-folder", out var folder))
        {
            if (SoundLibrary.PickRandom(folder) == null) throw new CliError("--music-folder precisa de uma pasta com arquivos MP3 ou WAV.");
            task = task with { SoundFolder = Path.GetFullPath(folder), SoundPath = null, SoundName = null };
        }
        if (flags.Contains("--no-music")) task = task with { SoundPath = null, SoundName = null, SoundFolder = null };

        if (flags.Contains("--no-alarm"))
        {
            if (o.ContainsKey("--at") || o.ContainsKey("--every")) throw new CliError("--no-alarm não combina com --at ou --every.");
            return task with { AlarmAt = null, Repeat = null, AlertShown = false };
        }
        bool repeatOptions = o.ContainsKey("--every") || o.ContainsKey("--interval") || o.ContainsKey("--days") || o.ContainsKey("--until") || flags.Contains("--no-repeat") || flags.Contains("--no-until");
        if (!o.ContainsKey("--at") && !repeatOptions) return task;

        // Start from the given time, or keep the current series anchor / alarm time.
        DateTime start = o.TryGetValue("--at", out var at) ? ParseWhen(at, now)
            : task.Repeat?.Start ?? task.AlarmAt?.LocalDateTime ?? throw new CliError("informe --at para definir o alarme.");

        Recurrence? repeat = flags.Contains("--no-repeat") ? null : task.Repeat;
        if (o.TryGetValue("--every", out var every))
        {
            var (unit, preset) = ParseUnit(every);
            repeat = new Recurrence { Unit = unit, Interval = 1, Days = preset };
        }
        if (repeat == null && (o.ContainsKey("--interval") || o.ContainsKey("--days") || o.ContainsKey("--until")))
            throw new CliError("use --every para criar uma repetição.");
        if (repeat != null)
        {
            if (o.TryGetValue("--interval", out var n))
                repeat = repeat with { Interval = int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out int i) && i is >= 1 and <= 999 ? i : throw new CliError("--interval deve ser de 1 a 999.") };
            if (o.TryGetValue("--days", out var days))
            {
                if (repeat.Unit != RepeatUnit.Week) throw new CliError("--days só vale com --every week.");
                repeat = repeat with { Days = ParseDays(days) };
            }
            if (repeat.Unit == RepeatUnit.Week && repeat.Days == WeekDays.None) repeat = repeat with { Days = Recurrence.Flag(start.DayOfWeek) };
            if (repeat.Unit != RepeatUnit.Week) repeat = repeat with { Days = WeekDays.None };
            if (o.TryGetValue("--until", out var until)) repeat = repeat with { Until = ParseDate(until, now) };
            if (flags.Contains("--no-until")) repeat = repeat with { Until = null };
            repeat = repeat with { Start = start };
        }

        var local = DateTime.SpecifyKind(start, DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(local)) throw new CliError("esse horário não existe no fuso local.");
        var chosen = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        DateTimeOffset alarm;
        if (repeat != null)
        {
            var nowOffset = DateTimeOffset.Now;
            alarm = repeat.Next(chosen > nowOffset ? chosen.AddTicks(-1) : nowOffset) ?? throw new CliError("a repetição termina antes do próximo aviso.");
        }
        else
        {
            if (chosen <= DateTimeOffset.Now) throw new CliError("o alarme precisa estar no futuro.");
            alarm = chosen;
        }
        return task with { AlarmAt = alarm, Repeat = repeat, AlertShown = false, Completed = false };
    }

    private static (RepeatUnit, WeekDays) ParseUnit(string text) => text.Trim().ToLowerInvariant() switch
    {
        "hour" or "hours" or "hourly" or "hora" or "horas" => (RepeatUnit.Hour, WeekDays.None),
        "day" or "days" or "daily" or "dia" or "dias" or "diariamente" => (RepeatUnit.Day, WeekDays.None),
        "week" or "weeks" or "weekly" or "semana" or "semanas" or "semanalmente" => (RepeatUnit.Week, WeekDays.None),
        "weekday" or "weekdays" or "workday" or "workdays" or "dias-uteis" or "dias úteis" =>
            (RepeatUnit.Week, WeekDays.Monday | WeekDays.Tuesday | WeekDays.Wednesday | WeekDays.Thursday | WeekDays.Friday),
        "weekend" or "weekends" or "fim-de-semana" => (RepeatUnit.Week, WeekDays.Saturday | WeekDays.Sunday),
        "month" or "months" or "monthly" or "mes" or "mês" or "meses" or "mensalmente" => (RepeatUnit.Month, WeekDays.None),
        "year" or "years" or "yearly" or "annually" or "ano" or "anos" or "anualmente" => (RepeatUnit.Year, WeekDays.None),
        _ => throw new CliError($"repetição desconhecida \"{text}\". Use hour, day, week, month, year, weekday ou weekend."),
    };

    private static WeekDays ParseDays(string text)
    {
        var result = WeekDays.None;
        foreach (var part in text.ToLowerInvariant().Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            DayOfWeek day = part[..Math.Min(3, part.Length)] switch
            {
                "sun" or "dom" => DayOfWeek.Sunday, "mon" or "seg" => DayOfWeek.Monday, "tue" or "ter" => DayOfWeek.Tuesday,
                "wed" or "qua" => DayOfWeek.Wednesday, "thu" or "qui" => DayOfWeek.Thursday, "fri" or "sex" => DayOfWeek.Friday,
                "sat" or "sáb" or "sab" => DayOfWeek.Saturday,
                _ => throw new CliError($"dia desconhecido \"{part}\". Use mon,tue,wed,thu,fri,sat,sun."),
            };
            result |= Recurrence.Flag(day);
        }
        return result == WeekDays.None ? throw new CliError("--days está vazio.") : result;
    }

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd/MM", "d/M"];

    public static DateTime ParseDate(string text, DateTime now)
    {
        string s = text.Trim().ToLowerInvariant();
        if (s is "today" or "hoje") return now.Date;
        if (s is "tomorrow" or "amanhã" or "amanha") return now.Date.AddDays(1);
        if (s.StartsWith('+') && s.EndsWith('d') && int.TryParse(s[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out int days)) return now.Date.AddDays(days);
        if (DateTime.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return s.Count(c => c == '/') == 1 && date.Date < now.Date ? date.AddYears(1).Date : date.Date;
        throw new CliError($"data inválida \"{text}\". Use 2026-11-30, 30/11/2026, today ou tomorrow.");
    }

    // Accepts relative offsets, a date and/or a time. A time alone means its next occurrence.
    public static DateTime ParseWhen(string text, DateTime now)
    {
        string s = text.Trim().ToLowerInvariant();
        if (s is "now" or "agora") return now.AddMinutes(1);
        if (s.Length > 2 && s[0] == '+' && int.TryParse(s[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out int amount))
        {
            DateTime result = s[^1] switch { 'm' => now.AddMinutes(amount), 'h' => now.AddHours(amount), 'd' => now.AddDays(amount), 'w' => now.AddDays(7 * amount), _ => default };
            if (result != default) return new DateTime(result.Year, result.Month, result.Day, result.Hour, result.Minute, 0);
        }
        var parts = s.Replace('t', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (s.StartsWith("today", StringComparison.Ordinal) || s.StartsWith("tomorrow", StringComparison.Ordinal)) parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && AlarmTimeParser.TryParse(parts[0], out var only) && !parts[0].Contains('/') && !parts[0].Contains('-'))
        {
            var today = now.Date + only.ToTimeSpan();
            return today > now ? today : today.AddDays(1);
        }
        if (parts.Length is 1 or 2)
        {
            var date = ParseDate(parts[0], now);
            if (parts.Length == 1) return date.AddHours(9);
            if (AlarmTimeParser.TryParse(parts[1], out var time)) return date + time.ToTimeSpan();
        }
        throw new CliError($"horário inválido \"{text}\". Exemplos: 18:30, \"2026-11-05 09:00\", \"tomorrow 08:00\", +30m.");
    }

    private static string Line(TaskItem t)
    {
        var bits = new List<string>();
        if (t.HasAlarm) bits.Add("alarme " + t.AlarmAt!.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        if (t.Repeat != null) bits.Add(t.Repeat.ShortLabel);
        if (t.DueDate is { } d) bits.Add("prazo " + d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return $"{t.Id.ToString("N")[..8]}  [{(t.Completed ? "x" : " ")}] {t.Title}" + (bits.Count > 0 ? "  ·  " + string.Join("  ·  ", bits) : "");
    }

    private static string Detail(TaskItem t)
    {
        var text = new StringBuilder();
        text.Append($"Id:         {t.Id}\nTítulo:     {t.Title}\n");
        if (t.Description.Length > 0) text.Append($"Descrição:  {t.Description}\n");
        text.Append($"Situação:   {(t.Completed ? "concluída" : "pendente")}\n");
        if (t.DueDate is { } d) text.Append($"Prazo:      {d:yyyy-MM-dd}\n");
        if (t.HasAlarm) text.Append($"Alarme:     {t.AlarmAt!.Value.LocalDateTime:yyyy-MM-dd HH:mm}\n");
        if (t.Repeat != null) text.Append($"Repetição:  {t.Repeat.Describe()}\n");
        if (t.SoundFolder != null) text.Append($"Música:     aleatória de {t.SoundFolder}\n");
        else if (t.SoundName != null) text.Append($"Música:     {t.SoundName}\n");
        return text.ToString();
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static object ToJson(TaskItem t) => new
    {
        id = t.Id,
        shortId = t.Id.ToString("N")[..8],
        title = t.Title,
        description = t.Description,
        completed = t.Completed,
        due = t.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        alarm = t.HasAlarm ? t.AlarmAt!.Value.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture) : null,
        repeat = t.Repeat is not { } r ? null : new
        {
            every = r.Unit.ToString().ToLowerInvariant(),
            interval = r.Interval,
            days = r.Unit == RepeatUnit.Week ? Enum.GetValues<DayOfWeek>().Where(d => (r.Days & Recurrence.Flag(d)) != 0).Select(d => d.ToString()[..3].ToLowerInvariant()).ToArray() : null,
            until = r.Until?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            description = r.Describe(),
        },
        music = t.SoundFolder ?? t.SoundName,
    };
}

// The app is a Windows (GUI) executable: write to redirected handles when an agent
// captures the output, or attach to the calling console when run interactively.
internal static class ConsoleOutput
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool AttachConsole(int processId);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int handle);
    public static void Write(string text, bool error)
    {
        var handle = GetStdHandle(error ? -12 : -11);
        bool attached = (handle == IntPtr.Zero || handle == new IntPtr(-1)) && AttachConsole(-1);
        using var writer = new StreamWriter(error ? Console.OpenStandardError() : Console.OpenStandardOutput(), attached ? Console.OutputEncoding : new UTF8Encoding(false));
        writer.Write(attached ? text.Replace("\n", Environment.NewLine) : text);
    }
}
