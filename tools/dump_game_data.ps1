# Lance Mars Horizon avec le dump du plugin activé, attend tools/output/dump/game_data.json,
# puis désactive le dump. Le jeu se ferme tout seul une fois le fichier écrit (Dump.QuitAfter).
# Prérequis : plugin compilé et déployé (dotnet build plugin/MarsHorizonAP/MarsHorizonAP.csproj -c Release).
# Usage : powershell -File tools/dump_game_data.ps1 [-GameDir "D:\...\Mars Horizon"] [-TimeoutSeconds 240]
param(
    [string]$GameDir = $(if ($env:MARS_HORIZON_DIR) { $env:MARS_HORIZON_DIR } else { "C:\Program Files (x86)\Steam\steamapps\common\Mars Horizon" }),
    [int]$TimeoutSeconds = 240
)

$ErrorActionPreference = "Stop"
$config = Join-Path $GameDir "BepInEx\config\archipelago.marshorizon.cfg"
$outDir = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path + "\tools\output\dump"
$outFile = Join-Path $outDir "game_data.json"

if (Get-Process "Mars Horizon" -ErrorAction SilentlyContinue) { throw "Mars Horizon est déjà lancé : le fermer d'abord." }

function Set-DumpConfig([bool]$enabled) {
    $value = if ($enabled) { "true" } else { "false" }
    $lines = if (Test-Path $config) { @(Get-Content $config) } else { @() }
    if (-not ($lines -match '^\[Dump\]')) { $lines += @("", "[Dump]", "") }
    $settings = [ordered]@{ "Enabled" = $value; "OutputDirectory" = $outDir; "QuitAfter" = "true" }
    foreach ($key in $settings.Keys) {
        $pattern = "^$key\s*="
        if ($lines -match $pattern) {
            $lines = $lines | ForEach-Object { if ($_ -match $pattern) { "$key = $($settings[$key])" } else { $_ } }
        } else {
            $index = [Array]::FindIndex([string[]]$lines, [Predicate[string]]{ param($l) $l -match '^\[Dump\]' })
            $before = $lines[0..$index]
            $after = if ($index + 1 -lt $lines.Count) { $lines[($index + 1)..($lines.Count - 1)] } else { @() }
            $lines = @($before) + "$key = $($settings[$key])" + @($after)
        }
    }
    New-Item -ItemType Directory -Force (Split-Path $config) | Out-Null
    Set-Content -Path $config -Value $lines -Encoding UTF8
}

Set-DumpConfig $true
$start = Get-Date
try {
    Start-Process "steam://rungameid/765810"
    $deadline = $start.AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ((Test-Path $outFile) -and (Get-Item $outFile).LastWriteTime -gt $start) { break }
        Start-Sleep -Seconds 3
    }
    if (-not ((Test-Path $outFile) -and (Get-Item $outFile).LastWriteTime -gt $start)) {
        throw "Pas de dump après $TimeoutSeconds s : voir BepInEx\LogOutput.log"
    }
    Write-Host "Dump écrit : $outFile ($([math]::Round((Get-Item $outFile).Length / 1KB)) Ko)"
} finally {
    Set-DumpConfig $false
}
