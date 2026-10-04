# Décompile le code de Mars Horizon dans decompiled/ (ignoré par git).
#   decompiled/Assembly-CSharp/            : logique du jeu (namespace Astronautica)
#   decompiled/Assembly-CSharp-firstpass/  : SaveLoad, EventStream, Localisation, Newtonsoft.Json...
# Prérequis : dotnet tool install -g ilspycmd
# Usage : powershell -File tools/decompile.ps1 [-GameDir "D:\...\Mars Horizon"]
param(
    [string]$GameDir = $(if ($env:MARS_HORIZON_DIR) { $env:MARS_HORIZON_DIR } else { "C:\Program Files (x86)\Steam\steamapps\common\Mars Horizon" })
)

$ErrorActionPreference = "Stop"
$managed = Join-Path $GameDir "Mars Horizon_Data\Managed"

foreach ($asm in @("Assembly-CSharp", "Assembly-CSharp-firstpass")) {
    $dll = Join-Path $managed "$asm.dll"
    $out = Join-Path $PSScriptRoot "..\decompiled\$asm"
    if (-not (Test-Path $dll)) { throw "$asm.dll introuvable dans $managed (utiliser -GameDir)" }

    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    New-Item -ItemType Directory -Force $out | Out-Null

    ilspycmd -p -o $out -r $managed --nested-directories $dll
    if ($LASTEXITCODE -ne 0) { throw "ilspycmd a échoué sur $asm ($LASTEXITCODE)" }

    $count = (Get-ChildItem $out -Recurse -Filter *.cs | Measure-Object).Count
    Write-Host "$asm : $count fichiers .cs -> $out"
}
