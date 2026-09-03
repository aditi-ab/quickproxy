param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = ".\artifacts\publish",
    [switch]$SkipClientBuild,
    [switch]$NoRestore
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
$serverProject = Join-Path $repoRoot "src\QuickProxy\QuickProxy.csproj"
$sharedUiDir = Join-Path $repoRoot "Packages\Aditify"
$publishDir = Resolve-Path -Path (Join-Path $repoRoot $OutputDir) -ErrorAction SilentlyContinue
if (-not $publishDir) {
    $publishDir = Join-Path $repoRoot $OutputDir
}
else {
    $publishDir = $publishDir.Path
}

if (-not (Test-Path $serverProject)) {
    throw "Could not find project file: $serverProject"
}

if (-not $SkipClientBuild) {
    if (-not (Test-Path $sharedUiDir)) {
        throw "Could not find shared UI directory: $sharedUiDir"
    }

    Push-Location $repoRoot
    try {
        Invoke-Step -Name "Installing shared UI dependencies" -Script {
            yarn --cwd $sharedUiDir install --frozen-lockfile
        }

        Invoke-Step -Name "Installing QuickProxy web dependencies" -Script {
            yarn install --frozen-lockfile
        }

        Invoke-Step -Name "Building QuickProxy web assets" -Script {
            yarn build
        }
    }
    finally {
        Pop-Location
    }
}

if (-not $NoRestore) {
    Invoke-Step -Name "Restoring .NET project" -Script {
        dotnet restore $serverProject -r $Runtime
    }
}

Invoke-Step -Name "Publishing backend" -Script {
    dotnet publish $serverProject `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -o $publishDir
}

Write-Host ""
Write-Host "Build completed." -ForegroundColor Green
Write-Host "Output: $publishDir"
