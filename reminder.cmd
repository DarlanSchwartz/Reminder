@echo off
rem Runs Reminder CLI commands and waits for them, e.g.: reminder add "Pagar aluguel" --at 2026-11-05T09:00 --every month
"%~dp0Reminder.exe" %*
exit /b %errorlevel%
