# Mars Horizon Multiworld Setup Guide

## Required Software

- Mars Horizon (Steam, version 1.4.2.1)
- [BepInEx 5 (x64)](https://github.com/BepInEx/BepInEx/releases) installed in the game folder
- The `MarsHorizonAP` plugin (see the release page of the project)
- [Archipelago](https://github.com/ArchipelagoMW/Archipelago/releases) 0.6.4 or newer, with `mars_horizon.apworld`

## Setup

1. Install BepInEx 5 (x64) in the game folder and start the game once.
2. Copy the `MarsHorizonAP` folder to `BepInEx/plugins/`.
3. Put `mars_horizon.apworld` in the `custom_worlds` folder of Archipelago.
4. Generate a YAML from the options page (choose your agency) and give it to the host.
5. Start a **new game**, pick the same agency as in your YAML, with the default scenario.
6. Press F8 in the game to open the connection window: server, slot name, password.

(The plugin, its installation steps and troubleshooting are detailed in the project README.)
