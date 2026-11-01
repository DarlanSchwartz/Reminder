@echo off
rem Runs Reminder CLI commands and waits for them, e.g.: reminder add "Pay rent" --at 2026-11-05T09:00 --every month
"%~dp0Reminder.exe" %*
exit /b %errorlevel%
