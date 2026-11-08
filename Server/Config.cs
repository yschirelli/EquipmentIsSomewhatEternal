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
    // Web UI settings
    public bool EnableWebUi { get; init; } = true;
    public int WebUiPort { get; init; } = 6970;

    // Preset selector
    public DifficultyPreset Preset { get; init; } = DifficultyPreset.Easy;

    // Durability & resource penalties
    public float DurabilityLossPercent { get; init; } = 0f;
    public float MinDurabilityFloorPercent { get; init; } = 10f;
    public bool AffectMaxDurability { get; init; } = false;
    public float ConsumableResourceLossPercent { get; init; } = 0f;

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

    // Original EquipmentIsEternal toggles
    public bool RestoreArmBand { get; init; } = false;
    public bool RestoreSpecialSlots { get; init; } = false;
    public bool RestoreScabbard { get; init; } = false;
    public bool RestoreSecuredContainer { get; init; } = false;
    public bool OnlyRestoreInsured { get; init; } = false;
    public bool KeepQuestItems { get; init; } = false;
    public bool DeduplicateItems { get; init; } = true;

    public ModConfig NormalizePreset() => Preset switch
    {
        DifficultyPreset.Easy => this with
        {
            DurabilityLossPercent = 0f,
            KeepBackpackItems = true,
            KeepPocketItems = true,
            KeepRigItems = true
        },
        DifficultyPreset.Medium => this with
        {
            DurabilityLossPercent = DurabilityLossPercent > 0f ? DurabilityLossPercent : 15f,
            KeepBackpackItems = true,
            KeepPocketItems = true,
            KeepRigItems = true
        },
        DifficultyPreset.Hard => this with
        {
            DurabilityLossPercent = DurabilityLossPercent > 0f ? DurabilityLossPercent : 30f,
            KeepBackpackItems = false,
            KeepPocketItems = false,
            KeepRigItems = false
        },
        _ => this
    };
}

public static class ConfigStore
{
    private static readonly Lock SyncLock = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string? _configFilePath;
    private static ModConfig _cached = new();

    public static void Initialize(string modPath, ModConfig initial)
    {
        lock (SyncLock)
        {
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
        if (hard.DurabilityLossPercent != 30f || hard.KeepBackpackItems || hard.KeepPocketItems || hard.KeepRigItems)
            throw new InvalidOperationException("Self-check failed: Hard preset normalization.");

        var item = new SPTarkov.Server.Core.Models.Eft.Common.Tables.Item
        {
            Id = new SPTarkov.Server.Core.Models.Common.MongoId(),
            Template = new SPTarkov.Server.Core.Models.Common.MongoId("5447a9cd4bdc2dbd208b4567"),
            Upd = new SPTarkov.Server.Core.Models.Eft.Common.Tables.Upd
            {
                Repairable = new SPTarkov.Server.Core.Models.Eft.Common.Tables.UpdRepairable
                {
                    Durability = 20.0,
                    MaxDurability = 100.0
                }
            }
        };

        Patches.SaveProfilePatch.ApplyPenalties([item], hard);
        if (item.Upd.Repairable.Durability != 10.0)
            throw new InvalidOperationException($"Self-check failed: Expected durability floor 10.0, got {item.Upd.Repairable.Durability}.");
    }
}
