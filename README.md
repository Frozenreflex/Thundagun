# Thundagun

Thundagun is a lightning fast performance mod for Resonite. It improves performance by decoupling the FrooxEngine and Unity loops by running FrooxEngine and Unity on separate threads.

**Warning: This mod is a prerelease. It may contain bugs and other issues. Crashes and missing/buggy/jittery visuals are possible but are generally uncommon. Use at your own risk.**

## Key Features

- **Parallel Execution**: Maximizes CPU and GPU utilization by running Resonite and Unity on separate threads.
- **Sync Mode**: Ensures a consistent 1:1 ratio between game updates and frames for stable visuals.
- **Async Mode**: Allows Unity to update independently of Resonite, protecting against stalls.
- **Auto Switching**: Enables thresholds to be defined for switching dynamically between sync and async modes based on game performance.

## Installation

1. Install [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader).
1. Place [Thundagun.dll](https://github.com/Frozenreflex/Thundagun/releases/latest/download/ExampleModName.dll) into your `rml_mods` folder. This folder should be at `C:\Program Files (x86)\Steam\steamapps\common\Resonite\rml_mods` for a default install. You can create it if it's missing, or if you launch the game once with ResoniteModLoader installed it will create this folder for you.
1. (Recommended) Download and place [ResoniteModSettings.dll]() into your `rml_mods` folder. This will allow you to configure the mod in-game.
1. Start Resonite. If you want to verify that the mod is working you can check your Resonite logs.

## Contributions and Support

- **Issues**: Report bugs or request features via the repository's Issues section.
- **Pull Requests**: Submit code contributions through Pull Requests.
