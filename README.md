# Quick Recall

![Quick Recall preview](assets/QuickRecall_Preview.png)

**Use your recall items with a single shortcut, straight from your inventory.**

[Download on Nexus Mods](https://www.nexusmods.com/terraria/mods/269)

Quick Recall lets you use a recall item from your inventory with a configurable hotkey, without selecting the item manually. It uses the item's normal Terraria behavior to teleport your character.

## Features

- Automatically chooses an available item in this order: **Shellphone** (all four modes) → **Cell Phone** → **Magic Mirror / Ice Mirror** → **Recall Potion** (optional).
- When multiple items have the same priority, uses the first eligible one in the inventory.
- Restores your previously selected item after use, unless you select an item manually.
- Shows a message if no eligible recall item is available.

## How it works

Press the assigned shortcut while playing. Quick Recall finds the highest-priority eligible recall item in your inventory, waits until it can select and use that item, and lets Terraria perform its normal item action. Recall Potions are considered only when their setting is enabled.

After use, the mod returns to your previous selection when the game allows it. If you manually select a hotbar slot during the action, including reselecting the current slot, your choice takes precedence over the mod's restoration. Further shortcut presses are ignored while a use or restoration is pending.

## Controls

The default hotkey is **Alt + Q**. To change it, find **Quick Recall** / **Use recall item** in the TerrariaModder keybind settings and assign your preferred shortcut.

## Settings

**Use Recall Potions** is **off by default**. Enable it in the Quick Recall settings to allow Recall Potions when no reusable recall item is available. With this setting disabled, the mod will not consume Recall Potions.

## Requirements

- **Terraria 1.4.5.8**
- **TerrariaModder Core 0.4.1 or newer**

## Installation

You need both `QuickRecall.dll` and `manifest.json` from the same Quick Recall package.

1. Install [TerrariaModder Core](https://www.nexusmods.com/terraria/mods/135) using its installation instructions. Close Terraria before copying mod files.
2. In your Terraria installation directory, create `TerrariaModder/mods/quick-recall/` if it does not already exist.
3. Place `QuickRecall.dll` and `manifest.json` directly in that folder:

   ```text
   Terraria/
   └── TerrariaModder/
       └── mods/
           └── quick-recall/
               ├── QuickRecall.dll
               └── manifest.json
   ```

4. Launch Terraria through `TerrariaInjector.exe` in your Terraria installation folder.

To update the mod, close Terraria and replace both files with the files from the updated package. To remove the mod, close Terraria and remove its `quick-recall` folder.

## Compatibility

Multiplayer compatibility has not been verified.

The mod uses existing inventory items and their normal Terraria actions. Item selection by another component can interrupt pending use or restoration, just as a manual hotbar selection can. This behavior does not establish compatibility with every other mod.

## Development

### Prerequisites

- Windows and the .NET Framework 4.8 runtime for running the `net48` tests.
- A .NET SDK capable of building the SDK-style projects and access to their NuGet packages. The projects use `Microsoft.NETFramework.ReferenceAssemblies` for the target framework reference assemblies; no SDK version is pinned by this repository.
- A local Terraria installation containing `Terraria.exe` and `Microsoft.Xna.Framework.Content.Pipeline.dll`.
- TerrariaModder Core and its Harmony dependency, `0Harmony.dll`.
- `Microsoft.Xna.Framework.Game.dll` from the XNA 4.0 framework, with its location supplied through local configuration.

Use the game and Core versions listed in Requirements when preparing an in-game candidate. The test project is independent of the production project and does not require the game assemblies to run its current smoke test.

### Local configuration

Copy [QuickRecall.Local.props.example](docs/QuickRecall.Local.props.example) to `src/QuickRecall.Local.props` and replace the placeholder paths. If a local file already exists, retain its machine-specific settings and update only the values that need changing.

| Property | Purpose |
| --- | --- |
| `TerrariaDirectory` | Directory containing `Terraria.exe` and the referenced Content Pipeline assembly |
| `CoreDllPath` | Path to `TerrariaModder.Core.dll`; defaults to `TerrariaModder/core/TerrariaModder.Core.dll` under the game directory |
| `XnaFrameworkGameDll` | Path to the XNA 4.0 `Microsoft.Xna.Framework.Game.dll` |
| `DeployToGame` | Enables copying the built DLL and manifest into the game when `true` |
| `TerrariaModDirectory` | Deployment destination; normally `TerrariaModder/mods/quick-recall` under the game directory |

`HarmonyDllPath` defaults to `deps/0Harmony.dll` relative to the directory containing the Core DLL. It can also be set in the local properties file if the dependency is installed elsewhere.

The supplied example sets `DeployToGame` to `true`. Set it to `false` for builds that should only produce local output, or use the explicit command-line override below. The local properties file is excluded from Git; the portable example remains public.

### Build

From the repository root, build the solution without copying files into the game:

```powershell
dotnet build src/QuickRecall.sln -c Release -p:DeployToGame=false
```

The production output is `src/bin/Release/QuickRecall.dll`. The solution also builds the test project. Use `-c Debug` for a Debug build.

To deploy intentionally, close Terraria, check `TerrariaModDirectory`, and build with `-p:DeployToGame=true`. The build target copies the production DLL and `mod/manifest.json` to that directory.

### Tests

From the repository root:

```powershell
dotnet test tests/QuickRecall.Tests/QuickRecall.Tests.csproj -c Release
```

The current suite contains a single NUnit smoke test that confirms test discovery and execution. It does not test recall priority, configuration, item use or selection restoration. Game-dependent behavior is covered by the procedures in the [testing policy](docs/testing.md).

## Repository structure

```text
docs/    Project documentation and the portable development configuration example
mod/     TerrariaModder package manifest
src/     Production source, solution and build configuration
tests/   NUnit test project
```

## Technical documentation

- [Architecture](docs/architecture.md)
- [Testing policy](docs/testing.md)