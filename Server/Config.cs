using System.Text.Json;
using System.Text.Json.Serialization;

namespace EquipmentIsEternal;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DifficultyPreset
{
    Easy,
    Medium,
    Hard,
    Custom
}

public record ModConfig
{
    // Universal Master Toggle
    public bool ModEnabled { get; init; } = true;

    // Preset selector
    public DifficultyPreset Preset { get; init; } = DifficultyPreset.Easy;

    // Durability & resource penalties
    public float DurabilityLossPercent { get; init; } = 0f;
    public float DurabilityLossVariancePercent { get; init; } = 0f;
    public float MinDurabilityFloorPercent { get; init; } = 10f;
    public bool AffectMaxDurability { get; init; } = false;
    public float ConsumableResourceLossPercent { get; init; } = 0f;

    // Equipped Slot Toggles (whether the equipped item slot itself is kept)
    public bool RestorePrimaryWeapons { get; init; } = true;
    public bool RestoreHolster { get; init; } = true;
    public bool RestoreArmorVest { get; init; } = true;
    public bool RestoreTacticalVest { get; init; } = true;
    public bool RestoreBackpack { get; init; } = true;
    public bool RestoreHeadwear { get; init; } = true;
    public bool RestoreEarpiece { get; init; } = true;
    public bool RestoreFaceCover { get; init; } = true;
    public bool RestoreEyewear { get; init; } = true;

    // Container content toggles (whether items INSIDE these containers are kept)
    public bool KeepBackpackItems { get; init; } = true;
    public bool KeepPocketItems { get; init; } = true;
    public bool KeepRigItems { get; init; } = true;

    // Category retention overrides (kept even when their container loses items)
    public bool KeepMeds { get; init; } = false;
    public bool KeepProvisions { get; init; } = false;
    public bool KeepAmmo { get; init; } = false;
    public bool KeepMagazines { get; init; } = false;
    public bool KeepGrenades { get; init; } = false;
    public bool KeepKeys { get; init; } = true;
    public bool KeepMoney { get; init; } = false;
    public bool KeepBarterItems { get; init; } = false;

    // Special slots, insurance & item rules
    public bool RestoreArmBand { get; init; } = false;
    public bool RestoreSpecialSlots { get; init; } = false;
    public bool RestoreScabbard { get; init; } = false;
    public bool RestoreSecuredContainer { get; init; } = false;
    public bool OnlyRestoreInsured { get; init; } = false;
    public bool KeepQuestItems { get; init; } = false;
    public bool StripFoundInRaid { get; init; } = false;

    public ModConfig NormalizePreset() => Preset switch
    {
        DifficultyPreset.Easy or DifficultyPreset.Medium or DifficultyPreset.Hard => this with
        {
            DurabilityLossPercent = Preset == DifficultyPreset.Easy ? 0f : Preset == DifficultyPreset.Medium ? 15f : 30f,
            DurabilityLossVariancePercent = Preset == DifficultyPreset.Easy ? 0f : Preset == DifficultyPreset.Medium ? 5f : 10f,
            RestorePrimaryWeapons = true,
            RestoreHolster = true,
            RestoreArmorVest = true,
            RestoreTacticalVest = true,
            RestoreBackpack = Preset != DifficultyPreset.Hard,
            RestoreHeadwear = true,
            RestoreEarpiece = true,
            RestoreFaceCover = true,
            RestoreEyewear = true,
            KeepBackpackItems = Preset != DifficultyPreset.Hard,
            KeepPocketItems = Preset != DifficultyPreset.Hard,
            KeepRigItems = Preset != DifficultyPreset.Hard,
            StripFoundInRaid = Preset == DifficultyPreset.Hard
        },
        _ => this
    };
}

public static class ConfigStore
{
    private static readonly Lock SyncLock = new();
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ModDirectory { get; private set; } = string.Empty;
    private static string? _configFilePath;
    private static ModConfig _cached = new();

    public static void Initialize(string modPath, ModConfig initial)
    {
        lock (SyncLock)
        {
            ModDirectory = modPath;
            _configFilePath = Path.Combine(modPath, "config.jsonc");
            _cached = initial.NormalizePreset();
        }
    }

    public static ModConfig Load()
    {
        lock (SyncLock)
        {
            if (_configFilePath != null && File.Exists(_configFilePath))
            {
                try
                {
                    var text = File.ReadAllText(_configFilePath);
                    var parsed = JsonSerializer.Deserialize<ModConfig>(text, JsonOptions);
                    if (parsed != null)
                        _cached = parsed.NormalizePreset();
                }
                catch
                {
                    // Keep last known valid config if file is mid-write
                }
            }
            return _cached;
        }
    }

    public static ModConfig Save(ModConfig updated)
    {
        lock (SyncLock)
        {
            _cached = updated.NormalizePreset();
            if (_configFilePath != null)
            {
                var json = JsonSerializer.Serialize(_cached, JsonOptions);
                File.WriteAllText(_configFilePath, json);
            }
            return _cached;
        }
    }

    public static void VerifySelfCheck()
    {
        var hard = new ModConfig { Preset = DifficultyPreset.Hard }.NormalizePreset();
        if (hard.DurabilityLossPercent != 30f || hard.KeepBackpackItems || hard.KeepPocketItems || hard.KeepRigItems || hard.RestoreBackpack || !hard.StripFoundInRaid)
            throw new InvalidOperationException("Self-check failed: Hard preset normalization.");

        var mediumFromHard = (hard with { Preset = DifficultyPreset.Medium }).NormalizePreset();
        if (mediumFromHard.DurabilityLossPercent != 15f || !mediumFromHard.KeepBackpackItems || !mediumFromHard.RestoreBackpack || mediumFromHard.StripFoundInRaid)
            throw new InvalidOperationException("Self-check failed: Hard -> Medium preset transition.");

        var item = new SPTarkov.Server.Core.Models.Eft.Common.Tables.Item
        {
            Id = new SPTarkov.Server.Core.Models.Common.MongoId(),
            Template = new SPTarkov.Server.Core.Models.Common.MongoId("5447a9cd4bdc2dbd208b4567"),
            Upd = new SPTarkov.Server.Core.Models.Eft.Common.Tables.Upd
            {
                SpawnedInSession = true,
                Repairable = new SPTarkov.Server.Core.Models.Eft.Common.Tables.UpdRepairable
                {
                    Durability = 20.0,
                    MaxDurability = 100.0
                }
            }
        };

        var fixedHard = hard with { DurabilityLossVariancePercent = 0f };
        Patches.SaveProfilePatch.ApplyPenalties([item], fixedHard);
        if (item.Upd.Repairable.Durability != 10.0)
            throw new InvalidOperationException($"Self-check failed: Expected durability floor 10.0, got {item.Upd.Repairable.Durability}.");
        if (item.Upd.SpawnedInSession == true)
            throw new InvalidOperationException("Self-check failed: StripFoundInRaid did not clear SpawnedInSession.");

        // Item below floor (5.0 / 100.0) must not heal up to 10.0 on death
        item.Upd.Repairable.Durability = 5.0;
        Patches.SaveProfilePatch.ApplyPenalties([item], fixedHard);
        if (item.Upd.Repairable.Durability != 5.0)
            throw new InvalidOperationException($"Self-check failed: Sub-floor item healed to {item.Upd.Repairable.Durability}.");

        // Variance bounds check: 50% loss +/- 10% on 100 durability must land in [40.0, 60.0]
        item.Upd.Repairable.Durability = 100.0;
        Patches.SaveProfilePatch.ApplyPenalties([item], fixedHard with { DurabilityLossPercent = 50f, DurabilityLossVariancePercent = 10f });
        if (item.Upd.Repairable.Durability is < 40.0 or > 60.0)
            throw new InvalidOperationException($"Self-check failed: Variance durability {item.Upd.Repairable.Durability} outside [40.0, 60.0].");

        // Loose armor plate in a backpack grid (Location != null) must not be treated as an installed plate
        var loosePlate = new SPTarkov.Server.Core.Models.Eft.Common.Tables.Item
        {
            Id = new SPTarkov.Server.Core.Models.Common.MongoId(),
            Template = new SPTarkov.Server.Core.Models.Common.MongoId("644120aa86ffbe10ee032b6f"),
            SlotId = "main",
            Location = new SPTarkov.Server.Core.Models.Eft.Common.Tables.ItemLocation()
        };
        if (Patches.SaveProfilePatch.IsArmorInsertOrPlate(loosePlate))
            throw new InvalidOperationException("Self-check failed: Loose grid plate treated as installed plate.");

        // Slot filter check: Backpack disabled on hard preset
        if (Patches.SaveProfilePatch.IsSlotRestorationAllowed("Backpack", hard))
            throw new InvalidOperationException("Self-check failed: Backpack slot should be disabled on Hard preset.");

        if (!Patches.SaveProfilePatch.IsSlotRestorationAllowed("FirstPrimaryWeapon", hard))
            throw new InvalidOperationException("Self-check failed: FirstPrimaryWeapon slot should be allowed on Hard preset.");
    }
}
