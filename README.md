# EquipmentIsSomewhatEternal

An SPT 4.x mod that restores your character's equipment upon death or MIA, with configurable presets, item-loss filtering, durability wear-and-tear, web UI configuration, and optional client-side BepInEx F12 menu sync.

---

## Architecture Overview

The mod consists of two components:
1. **Server Mod (`Server/`)**: SPT C# Server plugin (`net10.0`). Intercepts profile saves and raid transitions to back up your equipped items at raid start and restore them upon raid death or MIA according to rules in `config.jsonc`. Integrates natively with SPT's Server Information Center (SIC) / SP Tushonka at `https://127.0.0.1:6969/`.
2. **Client Mod (`Client/`)**: BepInEx 5 plugin (`net472`). Integrates with the in-game F12 configuration menu and automatically syncs changes to the server's `config.jsonc`.

---

## Prerequisites

- [.NET 9 or .NET 10 SDK](https://dotnet.microsoft.com/download)
- SPTarkov 4.1.2+ server & game installation
- BepInEx 5.4+ (client-side)

---

## Build Instructions

Build both the Server and Client components using the .NET CLI:

```bash
# Build Release binaries for both components
dotnet build -c Release
```

Or build individual projects:

```bash
# Server only
dotnet build Server/EquipmentIsSomewhatEternal.csproj -c Release

# Client only
dotnet build Client/EquipmentIsSomewhatEternal.Client.csproj -c Release
```

### Build Outputs

- **Server Mod**: `Server/bin/Release/EquipmentIsSomewhatEternal/`
  - `EquipmentIsSomewhatEternal.dll`
  - `config.jsonc`
  - `wwwroot/`
- **Client Mod**: `Client/bin/Release/net472/`
  - `EquipmentIsSomewhatEternal.Client.dll`

---

## Installation

### 1. Server Mod (Required)
Copy the `Server/bin/Release/EquipmentIsSomewhatEternal` directory into `<SPT-Server>/user/mods/`:

```
<SPT-Server>/user/mods/EquipmentIsSomewhatEternal/
├── EquipmentIsSomewhatEternal.dll
├── config.jsonc
└── wwwroot/
    └── index.html
```

### 2. Client Mod (Optional - F12 Menu Sync)
Copy `Client/bin/Release/net472/EquipmentIsSomewhatEternal.Client.dll` into your game's BepInEx plugins folder:

```
<SPT-Game>/BepInEx/plugins/EquipmentIsSomewhatEternal.Client.dll
```

---

## Configuration

Configuration can be changed via three methods:
1. **SPT Server Information Center (SIC)**: Open `https://127.0.0.1:6969/` in your browser and click on **Equipment Is Somewhat Eternal** under **MOD PAGES** (or navigate directly to `https://127.0.0.1:6969/equipment-is-somewhat-eternal`).
2. **In-Game F12 Menu**: If the Client mod is installed, press `F12` in-game and adjust settings under `EquipmentIsSomewhatEternal`.
3. **Direct Config File**: Edit `<SPT-Server>/user/mods/EquipmentIsSomewhatEternal/config.jsonc`.

### Difficulty Presets

- **Easy**: Keeps all backpack, pocket, and tactical rig items with 0% durability/resource loss.
- **Medium**: Keeps pocket and rig items, loses backpack items, 10% durability loss.
- **Hard**: Loses backpack, pocket, and rig contents, 25% durability loss.
- **Custom**: Fully user-defined parameters.

### Key Settings

| Setting | Type | Default | Description |
|---|---|---|---|
| `Preset` | string | `Medium` | Active preset (`Easy`, `Medium`, `Hard`, `Custom`). |
| `DurabilityLossPercent` | float | `10.0` | Percentage of max/current durability deducted on death. |
| `MinDurabilityFloorPercent` | float | `20.0` | Minimum durability percentage below which items won't degrade further. |
| `AffectMaxDurability` | bool | `true` | When true, lowers the item's maximum durability cap. |
| `ConsumableResourceLossPercent` | float | `0.0` | Percentage loss applied to medkits, repair kits, water/food. |
| `KeepBackpackItems` | bool | `false` | Retain loose items inside backpack. |
| `KeepPocketItems` | bool | `true` | Retain loose items inside pockets. |
| `KeepRigItems` | bool | `true` | Retain loose items inside tactical rig. |
| `OnlyRestoreInsured` | bool | `false` | Only restore items that were insured prior to raid. |
| `DeduplicateItems` | bool | `true` | Ensures no duplicate IDs occur when merging snapshot with post-raid profile. |
| `EnableWebUi` | bool | `true` | Enables embedded configuration server. |
| `WebUiPort` | int | `8088` | Web UI port. |
