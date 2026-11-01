$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build/dotnet'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.build/packages'
$env:TEMP = Join-Path $PSScriptRoot '.build/temp'
$env:TMP = $env:TEMP
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
New-Item -ItemType Directory -Force $env:TEMP, $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES | Out-Null
dotnet publish Reminder.csproj -c Release -o "$PSScriptRoot/app" --self-contained false -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
