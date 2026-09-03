param(
    [string]$Configuration = "Release",
    [string]$Project = ".\src\QuickProxy.Sdk\QuickProxy.Sdk.csproj",
    [string]$OutputDir = ".\.artifacts\nuget",
    [string]$Source = "https://api.nuget.org/v3/index.json",
    [string]$ApiKey = $env:NUGET_API_KEY,
    [string]$Version,
    [switch]$SkipDuplicate = $true
)

$ErrorActionPreference = "Stop"

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Script
    )

    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Script
}

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $repoRoot $Project
$packageOutput = Join-Path $repoRoot $OutputDir

if (-not (Test-Path $projectPath)) {
    throw "Could not find project file: $projectPath"
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    throw "NuGet API key is required. Pass -ApiKey or set NUGET_API_KEY."
}

New-Item -ItemType Directory -Force -Path $packageOutput | Out-Null

$packArguments = @(
    "pack", $projectPath,
    "-c", $Configuration,
    "-o", $packageOutput
)

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $packArguments += "-p:Version=$Version"
}

Invoke-Step -Name "Packing QuickProxy.Sdk" -Script {
    & dotnet @packArguments
}

$package = Get-ChildItem -Path $packageOutput -Filter "QuickProxy.Sdk.*.nupkg" |
    Where-Object { $_.Name -notlike "*.symbols.nupkg" } |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1

if (-not $package) {
    throw "No package was produced in $packageOutput"
}

$pushArguments = @(
    "nuget", "push", $package.FullName,
    "--api-key", $ApiKey,
    "--source", $Source
)

if ($SkipDuplicate) {
    $pushArguments += "--skip-duplicate"
}

Invoke-Step -Name "Pushing $($package.Name)" -Script {
    & dotnet @pushArguments
}

Write-Host ""
Write-Host "Package pushed: $($package.FullName)" -ForegroundColor Green
