#!/usr/bin/env python3
"""Construit les livrables dans dist/ :
  mars_horizon.apworld          l'APWorld (à mettre dans custom_worlds/ d'Archipelago)
  MarsHorizonAP-<version>.zip   le plugin BepInEx + YAML d'exemple + guide
Prérequis : dotnet build plugin/MarsHorizonAP/MarsHorizonAP.csproj -c Release
"""
import re
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DIST = ROOT / "dist"
WORLD = ROOT / "apworld" / "mars_horizon"
BIN = ROOT / "plugin" / "MarsHorizonAP" / "bin" / "Release"
PKG = Path.home() / ".nuget" / "packages" / "archipelago.multiclient.net" / "6.7.1" / "lib" / "net35"
SKIP_DIRS = {"__pycache__", "test"}


def add_tree(z: zipfile.ZipFile, src: Path, arc_root: str) -> None:
    for path in sorted(src.rglob("*")):
        if path.is_dir() or SKIP_DIRS & set(path.relative_to(src).parts) or path.suffix == ".pyc":
            continue
        z.write(path, f"{arc_root}/{path.relative_to(src).as_posix()}")


def main() -> int:
    DIST.mkdir(exist_ok=True)
    manifest = (WORLD / "archipelago.json").read_text(encoding="utf-8")
    version = re.search(r'"world_version":\s*"([^"]+)"', manifest).group(1)

    apworld = DIST / "mars_horizon.apworld"
    with zipfile.ZipFile(apworld, "w", zipfile.ZIP_DEFLATED) as z:
        add_tree(z, WORLD, "mars_horizon")
    print("écrit", apworld.relative_to(ROOT))

    needed = [BIN / "MarsHorizonAP.dll", PKG / "Archipelago.MultiClient.Net.dll", PKG / "Newtonsoft.Json.dll", PKG / "websocket-sharp.dll"]
    missing = [p for p in needed if not p.exists()]
    if missing:
        print("fichiers manquants (compiler le plugin d'abord) :", *missing, sep="\n  ")
        return 1
    plugin = DIST / f"MarsHorizonAP-{version}.zip"
    with zipfile.ZipFile(plugin, "w", zipfile.ZIP_DEFLATED) as z:
        for p in needed:
            z.write(p, f"BepInEx/plugins/MarsHorizonAP/{p.name}")
        z.write(ROOT / "yaml" / "MarsHorizon.yaml", "MarsHorizon.yaml")
        z.write(ROOT / "docs" / "INSTALL.md", "INSTALL.md")
        z.write(apworld, "mars_horizon.apworld")
    print("écrit", plugin.relative_to(ROOT))
    return 0


if __name__ == "__main__":
    sys.exit(main())
