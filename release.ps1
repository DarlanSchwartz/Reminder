# Builds dist/ReminderSetup-<version>.exe and a portable zip for a GitHub release.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build/dotnet'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.build/packages'
$env:TEMP = Join-Path $PSScriptRoot '.build/temp'
$env:TMP = $env:TEMP
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
New-Item -ItemType Directory -Force $env:TEMP, $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES | Out-Null

$version = ([xml](Get-Content Reminder.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$work = Join-Path $PSScriptRoot '.build/installer'
$payload = Join-Path $work 'payload'
$zip = Join-Path $work 'payload.zip'
$dist = Join-Path $PSScriptRoot 'dist'
Remove-Item -Recurse -Force $work, $dist -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $work, $dist | Out-Null

dotnet publish Reminder.csproj -c Release -o $payload --self-contained false -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'App build failed.' }
Remove-Item -Recurse -Force (Join-Path $payload 'data') -ErrorAction SilentlyContinue
Get-ChildItem $payload -Filter *.pdb | Remove-Item
Compress-Archive -Path "$payload/*" -DestinationPath $zip

dotnet build Installer/ReminderSetup.csproj -c Release -o (Join-Path $work 'setup') -p:PayloadZip=$zip -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }

Copy-Item (Join-Path $work 'setup/ReminderSetup.exe') (Join-Path $dist "ReminderSetup-$version.exe")
Copy-Item $zip (Join-Path $dist "Reminder-$version-portable.zip")
Get-ChildItem $dist | ForEach-Object { '{0}  {1:N1} MB' -f $_.Name, ($_.Length / 1MB) }
