# Implementation Plan — EquipmentIsSomewhatEternal (SPT 4.1.6)

- **Repository**: [yschirelli/EquipmentIsSomewhatEternal](https://github.com/yschirelli/EquipmentIsSomewhatEternal)
- **Upstream**: [ScrewTSW/EquipmentIsEternal](https://github.com/ScrewTSW/EquipmentIsEternal)
- **Target**: SPT 4.1.6 (Local single-player; Fika out of scope for initial release)

---

## 1. Directory Structure

```text
EquipmentIsSomewhatEternal/
├── EquipmentIsSomewhatEternal.sln
├── MASTER_PROMPT.md
├── PLAN.md
├── Server/                                   # -> SPT/user/mods/EquipmentIsSomewhatEternal/
│   ├── EquipmentIsSomewhatEternal.csproj     # net10.0, SPTarkov.* 4.1.x packages
│   ├── ModMetadata.cs                        # ModGuid, Name, Author, Version, SptVersion (~4.1.0)
│   ├── Config.cs                             # DifficultyPreset enum + ModConfig + Preset helper
│   ├── config.jsonc                          # Shared config file (Server + Web UI + F12 Client)
│   ├── EquipmentIsEternal.cs                 # Entrypoint (IOnLoad) — starts patches + WebConfigServer
│   ├── EquipmentSnapshot.cs                  # Pre-raid snapshot + tree traversal helpers
│   ├── WebConfigServer.cs                    # Lightweight HttpListener serving Web UI + /api/config
│   ├── wwwroot/
│   │   └── index.html                        # Standalone Web Config Page (Presets, Sliders, Toggles)
│   └── Patches/
│       ├── RaidStartPatch.cs                 # Snapshots equipment & insured IDs on raid start
│       ├── RaidEndPatch.cs                   # Captures death state & quest items
│       └── SaveProfilePatch.cs               # Restores slots, filters container cargo, applies durability loss
└── Client/                                   # -> BepInEx/plugins/EquipmentIsSomewhatEternal.Client.dll
    ├── EquipmentIsSomewhatEternal.Client.csproj  # net472, BepInEx + UnityEngine refs
    ├── ConfigurationManagerAttributes.cs     # F12 UI ordering & percentage slider attributes
    └── Plugin.cs                             # F12 menu bindings synced with Server/config.jsonc
```

---

## 2. Config Schema (`Server/Config.cs` & `Server/config.jsonc`)

```csharp
public enum DifficultyPreset
{
    Easy,
    Medium,
    Hard,
    Custom
}

public record ModConfig
{
    // Web UI settings
    public bool EnableWebUi { get; init; } = true;
    public int WebUiPort { get; init; } = 6970;

    // Preset selector
    public DifficultyPreset Preset { get; init; } = DifficultyPreset.Easy;

    // Durability & resource penalties
    public float DurabilityLossPercent { get; init; } = 0f;         // Easy: 0, Medium: 15, Hard: 30
    public float MinDurabilityFloorPercent { get; init; } = 10f;    // Default: 10% floor so items never brick to 0
    public bool AffectMaxDurability { get; init; } = false;         // Optional max-durability burn
    public float ConsumableResourceLossPercent { get; init; } = 0f; // Optional % drain on medkits & provisions

    // Container content toggles (whether items INSIDE these containers are kept)
    public bool KeepBackpackItems { get; init; } = true;            // Easy/Med: true, Hard: false
    public bool KeepPocketItems { get; init; } = true;              // Easy/Med: true, Hard: false
    public bool KeepRigItems { get; init; } = true;                 // Easy/Med: true, Hard: false

    // Category retention overrides (kept even when their container loses items)
    public bool KeepMeds { get; init; } = false;
    public bool KeepProvisions { get; init; } = false;
    public bool KeepAmmo { get; init; } = false;
    public bool KeepMagazines { get; init; } = false;
    public bool KeepGrenades { get; init; } = false;
    public bool KeepKeys { get; init; } = true;
    public bool KeepMoney { get; init; } = false;
    public bool KeepBarterItems { get; init; } = false;

    // Original EquipmentIsEternal toggles
    public bool RestoreArmBand { get; init; } = false;
    public bool RestoreSpecialSlots { get; init; } = false;
    public bool RestoreScabbard { get; init; } = false;
    public bool RestoreSecuredContainer { get; init; } = false;
    public bool OnlyRestoreInsured { get; init; } = false;
    public bool KeepQuestItems { get; init; } = false;
    public bool DeduplicateItems { get; init; } = true;
}
```

---

## 3. Step-by-Step Implementation Plan

### Phase 1: Project Restructure & Shared Config Service (`Server/`)
1. Organize server files into `Server/` and update `ModMetadata.cs` (`ModGuid = "com.yschirelli.equipmentissomewhateternal"`, `Name = "EquipmentIsSomewhatEternal"`).
2. Implement `Config.cs` with `ModConfig`, `DifficultyPreset`, `ApplyPreset(DifficultyPreset)` helper, and thread-safe read/write methods to `config.jsonc` so the Server, Web UI, and F12 Client never clobber each other.

### Phase 2: Container Cargo Filtering & Category Retention (`Server/Patches/SaveProfilePatch.cs`)
1. Inject `ItemHelper` into `SaveProfilePatch`.
2. When restoring `Backpack`, `Pockets`, or `TacticalVest`:
   - Always keep the root container item itself and any armor plates/inserts attached to `TacticalVest`/`Backpack` (where child `SlotId` is an armor slot rather than a cargo grid).
   - When the container's `Keep*Items` toggle is `false`, filter cargo descendants using `ItemHelper.IsOfBaseclass(item.Tpl, ...)`:
     - `BaseClasses.MEDS` -> `KeepMeds`
     - `BaseClasses.FOOD_DRINK` -> `KeepProvisions`
     - `BaseClasses.AMMO` / `BaseClasses.AMMO_BOX` -> `KeepAmmo`
     - `BaseClasses.MAGAZINE` -> `KeepMagazines` (if `KeepMagazines && !KeepAmmo`, keep the magazine item and strip its child `cartridges`)
     - `BaseClasses.THROW_WEAPON` -> `KeepGrenades`
     - `BaseClasses.KEY` -> `KeepKeys`
     - `BaseClasses.MONEY` -> `KeepMoney`
     - `BaseClasses.BARTER_ITEM` -> `KeepBarterItems`
   - Keep any nested children belonging to a kept item (except ammo inside a kept magazine when `KeepAmmo == false`).

### Phase 3: Durability & Consumable Penalties (`Server/Patches/SaveProfilePatch.cs`)
1. **Gear & Weapon Durability (`DurabilityLossPercent > 0`)**:
   - For every restored item where `item.Upd?.Repairable?.Durability is double cur && item.Upd.Repairable.MaxDurability is double max`:
     - `loss = max * (config.DurabilityLossPercent / 100.0)`
     - `floor = max * (config.MinDurabilityFloorPercent / 100.0)`
     - `item.Upd.Repairable.Durability = Math.Round(Math.Max(floor, Math.Min(cur, cur - loss)), 2)`
     - If `config.AffectMaxDurability`, also reduce `item.Upd.Repairable.MaxDurability` by `loss` down to `floor`.
2. **Consumable Resource Drain (`ConsumableResourceLossPercent > 0`)**:
   - Reduce `item.Upd.MedKit.HpResource` and `item.Upd.FoodDrink.HpPercent` by `ConsumableResourceLossPercent%` (clamped to `>= 1`).

### Phase 4: SPT Web Config Page (`Server/WebConfigServer.cs` & `Server/wwwroot/index.html`)
1. **Why a dedicated `HttpListener` on `127.0.0.1:6970`?**
   - SPT 4.x's main port (`6969`) uses Unity-specific `zlib` payload compression and self-signed HTTPS certs, making standard browser access cumbersome.
   - A 60-line `HttpListener` bound to `http://127.0.0.1:6970/` prints a clickable URL in the SPT Server console on startup and opens cleanly in any browser.
2. **Endpoints**:
   - `GET /` -> Serves `wwwroot/index.html`.
   - `GET /api/config` -> Reads and returns the current `ModConfig` JSON from `config.jsonc`.
   - `POST /api/config` -> Applies preset logic if needed, writes `config.jsonc`, updates the in-memory config, and returns the saved `ModConfig`.
3. **Web UI Features (`index.html`)**:
   - One-click preset cards (`Easy`, `Medium`, `Hard`, `Custom`) that immediately populate sliders and toggles.
   - Automatic switch to `Custom` when any individual slider/toggle is modified.
   - Live status banner showing when changes are saved to `config.jsonc`.

### Phase 5: BepInEx F12 Client Plugin (`Client/Plugin.cs`)
1. Optional companion `.dll` in `BepInEx/plugins/EquipmentIsSomewhatEternal.Client.dll`.
2. Binds F12 ConfigurationManager settings mirroring `ModConfig`, watches `SPT/user/mods/EquipmentIsSomewhatEternal/config.jsonc` with `FileSystemWatcher` so edits made in the Web Config Page appear immediately in F12, and writes back to `config.jsonc` when changed in F12.

### Phase 6: Build & Verification
1. Verify `Server` and `Client` builds and test preset transitions, JSON persistence, container cargo stripping, magazine/ammo splitting, and durability floor clamping.
