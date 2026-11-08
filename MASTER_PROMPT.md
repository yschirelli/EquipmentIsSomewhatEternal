# Master Prompt — EquipmentIsSomewhatEternal (SPT 4.1.6)

Use this prompt to brief any coding session or subagent working on this project.

---

## Context & Goal
We are building **EquipmentIsSomewhatEternal** for **SPT 4.1.6** ([yschirelli/EquipmentIsSomewhatEternal](https://github.com/yschirelli/EquipmentIsSomewhatEternal)), a fork of [ScrewTSW/EquipmentIsEternal](https://github.com/ScrewTSW/EquipmentIsEternal).
The upstream mod is an SPT `.NET 10` server mod that snapshots PMC equipment at raid start (`StartLocalRaidAsync`) and restores it on death before profile save (`HandlePostRaidPmc` -> `SaveProfileAsync`).

Our goal is to add:
1. **Difficulty Presets (`Easy`, `Medium`, `Hard`, `Custom`)**
2. **Durability & Resource Penalties** (with minimum durability floor and optional max-durability burn)
3. **Container Content Stripping (`Backpack`, `Pockets`, `TacticalVest`)**
4. **Item-Category Retention Filters (`Meds`, `Provisions`, `Ammo`, `Magazines`, `Keys`, `Grenades`, `Money`, `Barter`)**
5. **Dual Live Configuration**:
   - **SPT Web Config Page** (`http://127.0.0.1:6970`) hosted directly by the server mod so players can configure everything in a browser without relying on the in-game F12 menu.
   - **BepInEx F12 ConfigurationManager Menu** (`Client/`) for quick in-game toggling.
   - **Scope note**: Fika / remote multi-machine sync is explicitly out of scope for the initial release; Server, Web UI, and Client share the local `config.jsonc` file.

---

## Core Behavior & Presets

| Feature / Setting | Easy (Original) | Medium | Hard | Custom |
| :--- | :--- | :--- | :--- | :--- |
| **Pre-raid Equipped Gear & Weapons** | Restored (100%) | Restored | Restored | User-defined |
| **In-raid Loot (brought FROM raid)** | Lost (except Secure/Quest) | Lost | Lost | Lost (except Secure/Quest) |
| **Durability Loss % on Restored Gear** | `0%` | `15%` | `30%` | `0%–100%` slider |
| **Min Durability Floor %** | `10%` | `10%` | `10%` | `0%–100%` slider |
| **Affect Max Durability (Burn)** | `false` | `false` | `false` | Toggle (`AffectMaxDurability`) |
| **Consumable Resource Loss %** | `0%` | `0%` | `0%` | `0%–100%` slider |
| **Items Inside `Backpack`** | Kept (pre-raid) | Kept (pre-raid) | **Lost** | Toggle (`KeepBackpackItems`) |
| **Items Inside `Pockets`** | Kept (pre-raid) | Kept (pre-raid) | **Lost** | Toggle (`KeepPocketItems`) |
| **Items Inside `TacticalVest` (Rig)** | Kept (pre-raid) | Kept (pre-raid) | **Lost** | Toggle (`KeepRigItems`) |
| **Category Overrides (in lost containers)** | N/A (all kept) | N/A (all kept) | Uses category toggles | Per-category toggles (`Meds`, `Provisions`, `Ammo`, `Magazines`, `Keys`, `Grenades`, `Money`, `Barter`) |

### Included Gameplay Features
1. **`KeepMagazines` (separate from `KeepAmmo`)**: Preserves magazines inside your rig/backpack/pockets in Hard/Custom mode so you don't have to rebuild magazines after every death. If `KeepAmmo` is `false`, cartridges inside kept magazines are stripped; if `KeepAmmo` is `true`, loaded rounds are kept too.
2. **`MinDurabilityFloorPercent` (Default `10%`)**: Prevents durability loss from bricking guns/armor to `0` durability on repeated deaths.
3. **`AffectMaxDurability` (Burn toggle)**: Choose whether durability penalty reduces current durability only (`Repairable.Durability`), or also burns `Repairable.MaxDurability` to simulate repair wear.
4. **`ConsumableResourceLossPercent` (`0%–100%`)**: Optional % penalty applied to multi-use medkits (`Upd.MedKit.HpResource`) and provisions (`Upd.FoodDrink.HpPercent`) when kept.

---

## Architecture Rules (Ponytail — Minimal & Maintainable)
1. **Single Source of Truth (`config.jsonc`)**:
   - **Server (`Server/`)**: `.NET 10` (`SPTarkov.Server.Core`). Owns all snapshot, container filtering, category classification (`ItemHelper.IsOfBaseclass`), and durability mutation inside `SaveProfilePatch`, plus the lightweight `HttpListener` Web Config Server.
   - **Web Config UI (`Server/wwwroot/index.html`)**: Served at `http://127.0.0.1:6970` (configurable port in `config.jsonc`). Uses `GET /api/config` and `POST /api/config` to read and write `config.jsonc` live.
   - **BepInEx F12 Client (`Client/`)**: `.NET Framework 4.7.2` (`BepInEx 5`). Single-file `Plugin.cs` that reads/writes the same `SPT/user/mods/EquipmentIsSomewhatEternal/config.jsonc` file (and reloads it when opening F12 so changes made in the Web UI are reflected in-game).
   - **Live Reload**: `SaveProfilePatch` and `RaidEndPatch` read the latest config from disk/memory on each raid end, so changes in either the Web UI or F12 apply immediately without restarting SPT or EFT.
2. **Container Stripping & Category Filtering**:
   - Root container items (`Backpack`, `Pockets`, `TacticalVest`) and their built-in armor/plates (`SlotId` starting with `Armor_plate` or `Soft_armor`, or listed in `PlateSlots`) are always restored with the equipment.
   - Cargo items inside a container whose `Keep<Container>Items` is `false` are dropped unless `ItemHelper.IsOfBaseclass(item.Tpl, baseClass)` matches an enabled `Keep<Category>` toggle.
3. **Durability & Resource Penalty**:
   - Applied to every restored item with `item.Upd?.Repairable?.Durability != null`.
   - Formula: `newDurability = Math.Max(maxDurability * (MinDurabilityFloorPercent / 100.0), currentDurability - (maxDurability * (DurabilityLossPercent / 100.0)))`.
