# Install .NET 10 SDK for ReelWalk development on Windows, then open this repo in Cursor/VS Code.
# Run in PowerShell (may need: Set-ExecutionPolicy -Scope CurrentUser RemoteSigned):
#   .\scripts\setup-windows.ps1

$ErrorActionPreference = "Stop"

Write-Host "Checking for .NET 10 SDK..."
$sdkOk = $false
try {
    $sdks = & dotnet --list-sdks 2>$null
    if ($sdks -match '^10\.') { $sdkOk = $true }
} catch {}

if (-not $sdkOk) {
    Write-Host "Installing .NET 10 SDK via winget..."
    winget install --id Microsoft.DotNet.SDK.10 --exact --accept-package-agreements --accept-source-agreements
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
}

Write-Host "dotnet version:"
dotnet --version

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
Write-Host "Restoring and building Debug..."
dotnet restore .\ReelWalk.slnx
dotnet build .\src\ReelWalk\ReelWalk.csproj -c Debug

Write-Host ""
Write-Host "Done. In Cursor/VS Code:"
Write-Host "  1. Install recommended extensions when prompted (C# / Avalonia)."
Write-Host "  2. Open Run and Debug, choose 'ReelWalk (Windows)', press Play (F5)."
Write-Host "  3. First run creates ReelWalk.toml next to the Debug binary; add folder paths and F5 again."
