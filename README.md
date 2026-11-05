# Reminder

A small desktop widget for Windows that keeps your to-do list in view and rings a full-screen alarm at the right time. It's a native WPF app (.NET 10) in English, Spanish and Portuguese, with a command line for scripts and AI agents.

<p>
  <img src="docs/screenshots/widget.png" width="300" alt="The widget with a list of tasks">
  <img src="docs/screenshots/editor.png" width="300" alt="Choosing a weekly repeat in the task editor">
</p>

![The full-screen alarm](docs/screenshots/alarm.png)

## Features

- Tasks with an optional due date, alarm and repeat rule: every N hours, days, weeks (you pick the weekdays), months or years, with an optional end date. A monthly alarm on the 31st rings on the last day of shorter months, and one on 29 February rings on the 28th in other years.
- A full-screen alarm that plays your own song, or a random one from a folder. From the alarm you can complete the task, snooze for 5, 10 or 30 minutes, skip the rest of today (repeating tasks only) or dismiss it.
- Six color themes, light and dark, and an optional picture or animated GIF behind the alarm.
- Alarms are registered with the Windows Task Scheduler (after you agree), so they ring even when the widget is closed.
- The widget can float on top of other windows or behave like a normal window. It hides to the tray.
- A command line so scripts and AI agents can manage tasks.

## Requirements

- Windows 10 or 11
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) to run the app. The installer tells you if it's missing.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build from source

## Install

Download `ReminderSetup-<version>.exe` from [Releases](https://github.com/DarlanSchwartz/Reminder/releases) and run it. It installs for the current user, with no admin rights needed, to `%LocalAppData%\Programs\Reminder`. To remove it, go to **Settings → Apps**. You can choose to keep your tasks and songs when you uninstall.

<img src="docs/screenshots/installer.png" width="300" alt="The installer">

Each release also has a portable zip. Unzip it anywhere and run `Reminder.exe`.

## Usage

- **New task** opens the editor: the name and description, then the due date and alarm, the repeat rule, and the sound. Without an alarm, only the first two steps are shown.
- Tick a task to complete it. A repeating task moves to its next occurrence instead, and completing the last occurrence finishes it. The **Done** tab lists completed tasks, and you can tick one again to reopen it.
- The settings (gear) menu has the language, theme, alarm background, default music folder, **Start with Windows**, Windows integration and the PATH command.
- The pin button keeps the widget above other windows. The hide button sends it to the tray, and you can reopen it from the tray icon.

## Command line

To put `reminder` on your PATH, use the installer's **Add the reminder command** option, or click **Add reminder command to PATH** in the settings menu (click it again to remove it). Terminals you open afterwards can run it. If the app is open, the command runs inside it, so the widget updates right away.

```powershell
reminder add "Pay rent" --at "2026-11-05 09:00" --every month --until 2027-06-05
reminder add "Gym" --at 07:00 --every week --days mon,wed,fri
reminder add "Drink water" --at +1h --every hour --interval 2 --until today
reminder list --json
reminder done 3fa2c1
reminder skip 3fa2c1   # no more alarms today for a repeating task
reminder help          # every command and option
```

- Put titles that contain spaces in quotes. Extra words are rejected so no text is lost.
- Ids can be shortened to their first 4 or more characters, as shown by `list`.
- Add `--json` to any command for machine-readable output.
- Exit codes: `0` success, `1` failure, `2` usage error.

## Data

Everything stays in the `data` folder next to `Reminder.exe`:

| Path | Contents |
| --- | --- |
| `data\tasks.json` | Tasks and settings. Each save keeps the previous version in `tasks.json.bak`. |
| `data\sounds\` | Copies of the songs you picked, so moving the originals doesn't break an alarm |
| `data\backgrounds\` | The alarm background picture |

If `tasks.json` can't be read, Reminder doesn't open and doesn't overwrite it. Fix the file or replace it with `tasks.json.bak`.

Windows integration adds entries named `Reminder-<id>-…` to the Task Scheduler. Turning it off from the settings menu, or uninstalling, removes them.

## Build

The app builds offline because `NuGet.Config` clears the package sources. Run these on Windows from the repository root:

| Command | What it does |
| --- | --- |
| `.\build.ps1` | Publishes the app to `app\` |
| `.\release.ps1` | Builds `dist\ReminderSetup-<version>.exe` and the portable zip |
| `dotnet run --project Reminder.csproj` | Builds and starts the app from `bin\` |

To release, set `<Version>` in `Reminder.csproj` and `Installer\ReminderSetup.csproj`. Then run `release.ps1` and upload `dist\*` with `gh release create v<version> dist\*`.

## Tests

```powershell
dotnet test Reminder.Tests
```

`Reminder.Tests` is an xUnit project for the core logic: recurrence (including daylight-saving changes and leap years), the task service and its rollback on errors, storage, the command line, time parsing and the translations. It compiles the app's platform-neutral source files directly, so it also runs on Linux and macOS. It is the only project that downloads packages, from nuget.org through `Reminder.Tests\NuGet.Config`.

The built app also has two self-checks for Windows:

| Command | What it checks |
| --- | --- |
| `app\Reminder.exe --self-test` | Storage, recurrence, alarms and sounds, using scratch data under `app\test-output\`. Writes `test-results.txt`. |
| `app\Reminder.exe --scheduler-test` | Creates and removes real, temporary Task Scheduler entries. Writes `scheduler-test-results.txt`. |

## Translations

The text lives in one JSON file per language: `Localization\<code>.json` for the app and `Installer\Localization\<code>.json` for the installer. `_language` is the name shown in the menu and `_culture` sets the date formats. To add a language, copy `en.json` to a new code, translate the values and rebuild. Any key you leave out falls back to English. Keep the `{0}`-style placeholders of each English text; the tests check them.

## Project layout

| Folder | Contents |
| --- | --- |
| `Models`, `Services` | Tasks, recurrence, storage, the Task Scheduler and PATH setup |
| `Views`, `Themes` | The widget, editor and alarm windows |
| `Audio` | Playback |
| `Cli` | Command line |
| `Localization` | Translations (JSON) |
| `Installer` | The setup program (.NET Framework 4.8, so it runs before .NET 10 is installed) |
| `Tests` | The app's built-in self-tests |
| `Reminder.Tests` | Unit tests (xUnit) |
