# DifRUST — Rust Nights (source)

Client-side lighting mod for 7 Days to Die 3.4.x. Version **1.1.0**.

## What it does

- gradual dusk from 20:00 to 22:00;
- full darkness from 22:00 to 04:00;
- gradual dawn from 04:00 to 06:00;
- normal lighting for the rest of the day.

Only lighting is changed. Fog, weather, zombies, loot, progression, POIs and network
logic are untouched. There is no server component: the mod works on any server,
including vanilla ones.

## In-game menu

Press **NumPad6** or **F8**:

- enable / disable;
- darkness strength slider, 2–60% (default 6%);
- "Night now (30 s)" — preview without waiting for 22:00;
- reset to defaults.

The menu follows the game language: English by default, Russian when the game runs
in Russian. The language is detected at runtime, so no restart or manual switch is
needed.

Settings are stored in `DifRUST.cfg` next to the game's save data
(`Application.persistentDataPath`) and restored on launch:

```
enabled=1
night=0.06
```

## Harmony patches

- `SkyManager.GetMoonAmbientScale` — moon ambient light during the dark hours;
- `WorldEnvironment.AmbientSpectrumFrameUpdate` — ambient colour blending for dusk
  and dawn.

Both are postfix patches with a time-based factor; no other game systems are touched.

## Requirements

- .NET SDK 8/10 (for building)
- 7 Days to Die 3.4.x
- `0_TFP_Harmony`

## Build

From this folder:

```powershell
cd Source
.\build.ps1
```

Or with an explicit game path:

```powershell
dotnet build .\Source\DifRUST.csproj -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die"
```

Build output:

```text
Source\bin\Release\netstandard2.1\DifRUST.dll
```

## Install

```
%APPDATA%\7DaysToDie\Mods\DifRUST\ModInfo.xml
%APPDATA%\7DaysToDie\Mods\DifRUST\DifRUST.dll
```

Launch the game without EAC (DLL/Harmony mod). No EasyAntiCheat files are modified.

## Source map

- `Source/DifRUST.cs` — Harmony patches, schedule logic, settings, in-game menu.
- `Source/DifRUST.csproj` — build project.
- `Source/build.ps1` — build helper.
- `ModInfo.xml` — mod metadata.
- `CHANGELOG.md` — version history.

## Compatibility

- No known conflicts with server mods.
- If another client mod also darkens nights, effects stack. Use only one night
  lighting mod at a time.
- Pairs with Silent Fog: that mod softens night fog when DifRUST is installed.

## AI disclosure

Code and documentation were created with AI assistance. No AI-generated visual
assets, models, voices or textures are included. Testing and publishing are done
by hand.

## Version

`1.1.0` — see `CHANGELOG.md` for the full history.
