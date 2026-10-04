# Mars Horizon × Archipelago

Randomizer [Archipelago](https://archipelago.gg) pour *Mars Horizon* (Auroch Digital, 2020).

| Dossier | Contenu |
|---|---|
| `apworld/mars_horizon/` | APWorld Python (items, locations, régions, règles, options) — phase 2 |
| `plugin/MarsHorizonAP/` | Plugin BepInEx 5 (C#, Harmony) qui fait le lien jeu ↔ serveur AP |
| `tools/` | Scripts : décompilation, extraction des données (phase 1) |
| `docs/` | Notes techniques, dont [`game-internals.md`](docs/game-internals.md) |

Roadmap et prompt : `Mars Horizon × Archipelago — Prompt & Roadmap.pdf`.
Version visée : **Mars Horizon v1.4.2.1** (Steam, build Mono), **BepInEx 5.4.23.5 x64**.

## Mise en place (dev)

1. Installer BepInEx 5 x64 dans le dossier du jeu, lancer le jeu une fois.
2. Copier `plugin/GameDir.props.example` en `plugin/GameDir.props` et y mettre le chemin du jeu
   (ou définir la variable d'environnement `MARS_HORIZON_DIR`).
3. Compiler le plugin (copié automatiquement dans `BepInEx/plugins/MarsHorizonAP/`) :

   ```bash
   dotnet build plugin/MarsHorizonAP/MarsHorizonAP.csproj -c Release
   ```

4. Vérifier dans `BepInEx/LogOutput.log` : `Hello from MarsHorizonAP ...`.

## Décompiler le jeu

Le code décompilé va dans `decompiled/` (ignoré par git : c'est le code d'Auroch Digital).

```bash
powershell -File tools/decompile.ps1 -GameDir "D:\SteamApp\steamapps\common\Mars Horizon"
```

Prérequis : `dotnet tool install -g ilspycmd`.
