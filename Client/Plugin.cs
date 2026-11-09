using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using BepInEx;
using BepInEx.Configuration;

namespace EquipmentIsSomewhatEternal.Client;

public enum DifficultyPreset
{
    Easy,
    Medium,
    Hard,
    Custom
}

[BepInPlugin("com.yschirelli.equipmentissomewhateternal.client", "EquipmentIsSomewhatEternal", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    private ConfigEntry<bool> _modEnabled = null!;
    private ConfigEntry<DifficultyPreset> _preset = null!;
    private ConfigEntry<float> _durabilityLossPercent = null!;
    private ConfigEntry<float> _durabilityLossVariancePercent = null!;
    private ConfigEntry<float> _minDurabilityFloorPercent = null!;
    private ConfigEntry<bool> _affectMaxDurability = null!;
    private ConfigEntry<float> _consumableResourceLossPercent = null!;

    private ConfigEntry<bool> _restorePrimaryWeapons = null!;
    private ConfigEntry<bool> _restoreHolster = null!;
    private ConfigEntry<bool> _restoreArmorVest = null!;
    private ConfigEntry<bool> _restoreTacticalVest = null!;
    private ConfigEntry<bool> _restoreBackpack = null!;
    private ConfigEntry<bool> _restoreHeadwear = null!;
    private ConfigEntry<bool> _restoreEarpiece = null!;
    private ConfigEntry<bool> _restoreFaceCover = null!;
    private ConfigEntry<bool> _restoreEyewear = null!;

    private ConfigEntry<bool> _keepBackpackItems = null!;
    private ConfigEntry<bool> _keepPocketItems = null!;
    private ConfigEntry<bool> _keepRigItems = null!;

    private ConfigEntry<bool> _keepMeds = null!;
    private ConfigEntry<bool> _keepProvisions = null!;
    private ConfigEntry<bool> _keepMagazines = null!;
    private ConfigEntry<bool> _keepAmmo = null!;
    private ConfigEntry<bool> _keepGrenades = null!;
    private ConfigEntry<bool> _keepKeys = null!;
    private ConfigEntry<bool> _keepMoney = null!;
    private ConfigEntry<bool> _keepBarterItems = null!;

    private ConfigEntry<bool> _onlyRestoreInsured = null!;
    private ConfigEntry<bool> _keepQuestItems = null!;
    private ConfigEntry<bool> _stripFoundInRaid = null!;
    private ConfigEntry<bool> _restoreArmBand = null!;
    private ConfigEntry<bool> _restoreSpecialSlots = null!;
    private ConfigEntry<bool> _restoreScabbard = null!;
    private ConfigEntry<bool> _restoreSecuredContainer = null!;

    private bool _suppressEvents;
    private volatile bool _reloadPending;
    private long _reloadAtTicks;
    private string? _serverConfigPath;
    private FileSystemWatcher? _watcher;

    private void Awake()
    {
        _serverConfigPath = ResolveServerConfigPath();

        var order = 100;
        ConfigDescription Desc(string text, bool isPercent = false) =>
            new(text, isPercent ? new AcceptableValueRange<float>(0f, 100f) : null,
                new ConfigurationManagerAttributes { Order = order--, ShowRangeAsPercent = isPercent ? false : null });

        _modEnabled = Config.Bind("1. General", "Mod Enabled", true,
            Desc("Master toggle to enable or disable EquipmentIsSomewhatEternal entirely."));

        _preset = Config.Bind("1. General", "Difficulty Preset", DifficultyPreset.Easy,
            Desc("Easy: keep all pre-raid gear/items (0% durability loss). Medium: 15% durability loss. Hard: lose backpack & cargo + 30% durability loss + strip FIR. Custom: manual configuration."));

        _durabilityLossPercent = Config.Bind("2. Durability & Penalties", "Durability Loss %", 0f,
            Desc("Percentage of max durability subtracted from restored weapons, armor, helmets, and plates on death.", true));
        _durabilityLossVariancePercent = Config.Bind("2. Durability & Penalties", "Durability Loss Variance %", 0f,
            Desc("Random variance (+/- %) applied to durability loss to simulate combat damage unpredictability.", true));
        _minDurabilityFloorPercent = Config.Bind("2. Durability & Penalties", "Min Durability Floor %", 10f,
            Desc("Minimum durability floor (% of max durability) so repeated deaths never brick gear to 0.", true));
        _affectMaxDurability = Config.Bind("2. Durability & Penalties", "Affect Max Durability (Burn)", false,
            Desc("Also reduce max durability on death to simulate repair degradation."));
        _consumableResourceLossPercent = Config.Bind("2. Durability & Penalties", "Consumable Resource Drain %", 0f,
            Desc("Percentage drained from kept multi-use medkits and food/drink resources on death.", true));

        _restorePrimaryWeapons = Config.Bind("3. Equipped Slots", "Restore Primary Weapons", true,
            Desc("Restore equipped primary weapons (first & second slots)."));
        _restoreHolster = Config.Bind("3. Equipped Slots", "Restore Holster (Sidearm)", true,
            Desc("Restore equipped sidearm pistol."));
        _restoreArmorVest = Config.Bind("3. Equipped Slots", "Restore Armor Vest", true,
            Desc("Restore equipped body armor vest."));
        _restoreTacticalVest = Config.Bind("3. Equipped Slots", "Restore Tactical Rig", true,
            Desc("Restore equipped chest rig / tactical vest."));
        _restoreBackpack = Config.Bind("3. Equipped Slots", "Restore Backpack", true,
            Desc("Restore equipped backpack itself (separate from contents)."));
        _restoreHeadwear = Config.Bind("3. Equipped Slots", "Restore Headwear (Helmet/Hat)", true,
            Desc("Restore equipped helmet or cap."));
        _restoreEarpiece = Config.Bind("3. Equipped Slots", "Restore Headset / Earpiece", true,
            Desc("Restore equipped tactical headset."));
        _restoreFaceCover = Config.Bind("3. Equipped Slots", "Restore Face Cover", true,
            Desc("Restore equipped balaclava or mask."));
        _restoreEyewear = Config.Bind("3. Equipped Slots", "Restore Eyewear", true,
            Desc("Restore equipped glasses or goggles."));

        _keepBackpackItems = Config.Bind("4. Container Contents", "Keep Backpack Items", true,
            Desc("Keep pre-raid items stored inside your Backpack."));
        _keepPocketItems = Config.Bind("4. Container Contents", "Keep Pocket Items", true,
            Desc("Keep pre-raid items stored inside your Pockets."));
        _keepRigItems = Config.Bind("4. Container Contents", "Keep Tactical Rig Items", true,
            Desc("Keep pre-raid items stored inside your Tactical Rig."));

        _keepMeds = Config.Bind("5. Category Whitelist (When Container Lost)", "Keep Meds & Stims", false,
            Desc("Keep medical items and stimulators even if their container loses contents."));
        _keepProvisions = Config.Bind("5. Category Whitelist (When Container Lost)", "Keep Food & Drink", false,
            Desc("Keep provisions even if their container loses contents."));
        _keepMagazines = Config.Bind("5. Category Whitelist (When Container Lost)", "Keep Magazines", false,
            Desc("Keep weapon magazines even if their container loses contents."));
        _keepAmmo = Config.Bind("5. Category Whitelist (When Container Lost)", "Keep Loose & Mag Ammo", false,
            Desc("Keep loose ammo stacks, ammo boxes, and cartridges inside kept magazines."));
        _keepGrenades = Config.Bind("5. Category Whitelist (When Container Lost)", "Keep Grenades", false,
            Desc("Keep throwable grenades even if their container loses contents."));
        _keepKeys = Config.Bind("5. Category Whitelist (When Container Lost)", "Keep Keys & Keycards", true,
            Desc("Keep mechanical keys and keycards even if their container loses contents."));
        _keepMoney = Config.Bind("5. Category Whitelist (When Container Lost)", "Keep Money", false,
            Desc("Keep cash stacks even if their container loses contents."));
        _keepBarterItems = Config.Bind("5. Category Whitelist (When Container Lost)", "Keep Barter Items", false,
            Desc("Keep barter items even if their container loses contents."));

        _onlyRestoreInsured = Config.Bind("6. Special Rules & Extra Slots", "Only Restore Insured Gear", false,
            Desc("Only restore pre-raid equipment slots that were insured before entering the raid."));
        _keepQuestItems = Config.Bind("6. Special Rules & Extra Slots", "Keep In-Raid Quest Items", false,
            Desc("Preserve quest items collected during the raid on death."));
        _stripFoundInRaid = Config.Bind("6. Special Rules & Extra Slots", "Strip Found-in-Raid On Restore", false,
            Desc("Remove Found-in-Raid status on restored items upon death."));
        _restoreArmBand = Config.Bind("6. Special Rules & Extra Slots", "Restore ArmBand Snapshot", false,
            Desc("Restore pre-raid ArmBand on death."));
        _restoreSpecialSlots = Config.Bind("6. Special Rules & Extra Slots", "Restore Special Slots Snapshot", false,
            Desc("Restore pre-raid Special Slots on death."));
        _restoreScabbard = Config.Bind("6. Special Rules & Extra Slots", "Restore Scabbard Snapshot", false,
            Desc("Restore pre-raid melee Scabbard on death."));
        _restoreSecuredContainer = Config.Bind("6. Special Rules & Extra Slots", "Restore Secured Container Snapshot", false,
            Desc("Restore pre-raid Secure Container on death (warning: overwrites items placed into Secure Container during raid)."));

        LoadFromServerJson();

        _preset.SettingChanged += OnPresetChanged;
        _durabilityLossPercent.SettingChanged += OnPresetDependentSettingChanged;
        _durabilityLossVariancePercent.SettingChanged += OnPresetDependentSettingChanged;
        _restorePrimaryWeapons.SettingChanged += OnPresetDependentSettingChanged;
        _restoreHolster.SettingChanged += OnPresetDependentSettingChanged;
        _restoreArmorVest.SettingChanged += OnPresetDependentSettingChanged;
        _restoreTacticalVest.SettingChanged += OnPresetDependentSettingChanged;
        _restoreBackpack.SettingChanged += OnPresetDependentSettingChanged;
        _restoreHeadwear.SettingChanged += OnPresetDependentSettingChanged;
        _restoreEarpiece.SettingChanged += OnPresetDependentSettingChanged;
        _restoreFaceCover.SettingChanged += OnPresetDependentSettingChanged;
        _restoreEyewear.SettingChanged += OnPresetDependentSettingChanged;
        _keepBackpackItems.SettingChanged += OnPresetDependentSettingChanged;
        _keepPocketItems.SettingChanged += OnPresetDependentSettingChanged;
        _keepRigItems.SettingChanged += OnPresetDependentSettingChanged;
        _stripFoundInRaid.SettingChanged += OnPresetDependentSettingChanged;
        Config.SettingChanged += (_, _) =>
        {
            if (!_suppressEvents) SaveToServerJson();
        };

        StartWatchingServerConfig();
        Logger.LogInfo($"EquipmentIsSomewhatEternal F12 Plugin loaded (Server config: {_serverConfigPath ?? "not found"})");
    }

    private void Update()
    {
        if (_reloadPending && DateTime.UtcNow.Ticks >= _reloadAtTicks)
        {
            _reloadPending = false;
            LoadFromServerJson();
        }
    }

    private void OnDestroy()
    {
        _watcher?.Dispose();
    }

    private void OnPresetChanged(object sender, EventArgs e)
    {
        if (_suppressEvents) return;
        _suppressEvents = true;
        try
        {
            switch (_preset.Value)
            {
                case DifficultyPreset.Easy:
                    _durabilityLossPercent.Value = 0f;
                    _durabilityLossVariancePercent.Value = 0f;
                    _restorePrimaryWeapons.Value = true;
                    _restoreHolster.Value = true;
                    _restoreArmorVest.Value = true;
                    _restoreTacticalVest.Value = true;
                    _restoreBackpack.Value = true;
                    _restoreHeadwear.Value = true;
                    _restoreEarpiece.Value = true;
                    _restoreFaceCover.Value = true;
                    _restoreEyewear.Value = true;
                    _keepBackpackItems.Value = true;
                    _keepPocketItems.Value = true;
                    _keepRigItems.Value = true;
                    _stripFoundInRaid.Value = false;
                    break;
                case DifficultyPreset.Medium:
                    _durabilityLossPercent.Value = 15f;
                    _durabilityLossVariancePercent.Value = 5f;
                    _restorePrimaryWeapons.Value = true;
                    _restoreHolster.Value = true;
                    _restoreArmorVest.Value = true;
                    _restoreTacticalVest.Value = true;
                    _restoreBackpack.Value = true;
                    _restoreHeadwear.Value = true;
                    _restoreEarpiece.Value = true;
                    _restoreFaceCover.Value = true;
                    _restoreEyewear.Value = true;
                    _keepBackpackItems.Value = true;
                    _keepPocketItems.Value = true;
                    _keepRigItems.Value = true;
                    _stripFoundInRaid.Value = false;
                    break;
                case DifficultyPreset.Hard:
                    _durabilityLossPercent.Value = 30f;
                    _durabilityLossVariancePercent.Value = 10f;
                    _restorePrimaryWeapons.Value = true;
                    _restoreHolster.Value = true;
                    _restoreArmorVest.Value = true;
                    _restoreTacticalVest.Value = true;
                    _restoreBackpack.Value = false;
                    _restoreHeadwear.Value = true;
                    _restoreEarpiece.Value = true;
                    _restoreFaceCover.Value = true;
                    _restoreEyewear.Value = true;
                    _keepBackpackItems.Value = false;
                    _keepPocketItems.Value = false;
                    _keepRigItems.Value = false;
                    _stripFoundInRaid.Value = true;
                    break;
            }
            SaveToServerJson();
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void OnPresetDependentSettingChanged(object sender, EventArgs e)
    {
        if (_suppressEvents) return;
        _suppressEvents = true;
        try
        {
            _preset.Value = DifficultyPreset.Custom;
            SaveToServerJson();
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private static string? ResolveServerConfigPath()
    {
        var gameRoot = Path.GetFullPath(Path.Combine(Paths.BepInExRootPath, ".."));
        string[] candidates =
        [
            Path.Combine(gameRoot, "SPT", "user", "mods", "EquipmentIsSomewhatEternal", "config.jsonc"),
            Path.Combine(gameRoot, "user", "mods", "EquipmentIsSomewhatEternal", "config.jsonc"),
            Path.Combine(gameRoot, "SPT", "user", "mods", "EquipmentIsEternal", "config.jsonc")
        ];

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        return candidates[0];
    }

    private void StartWatchingServerConfig()
    {
        if (_serverConfigPath == null) return;
        var dir = Path.GetDirectoryName(_serverConfigPath);
        if (dir == null || !Directory.Exists(dir)) return;

        _watcher = new FileSystemWatcher(dir, Path.GetFileName(_serverConfigPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Changed += (_, _) =>
        {
            _reloadAtTicks = DateTime.UtcNow.AddMilliseconds(150).Ticks;
            _reloadPending = true;
        };
    }

    private void LoadFromServerJson()
    {
        if (_serverConfigPath == null || !File.Exists(_serverConfigPath)) return;

        try
        {
            var raw = File.ReadAllText(_serverConfigPath);
            var cleanJson = Regex.Replace(raw, @"^\s*//.*$", "", RegexOptions.Multiline);
            var serializer = new JavaScriptSerializer();
            if (serializer.DeserializeObject(cleanJson) is not Dictionary<string, object> dict) return;

            _suppressEvents = true;
            try
            {
                ReadBool(dict, "ModEnabled", _modEnabled);

                if (dict.TryGetValue("Preset", out var pObj) && pObj is string pStr && Enum.TryParse<DifficultyPreset>(pStr, true, out var parsedPreset))
                    _preset.Value = parsedPreset;

                ReadFloat(dict, "DurabilityLossPercent", _durabilityLossPercent);
                ReadFloat(dict, "DurabilityLossVariancePercent", _durabilityLossVariancePercent);
                ReadFloat(dict, "MinDurabilityFloorPercent", _minDurabilityFloorPercent);
                ReadBool(dict, "AffectMaxDurability", _affectMaxDurability);
                ReadFloat(dict, "ConsumableResourceLossPercent", _consumableResourceLossPercent);

                ReadBool(dict, "RestorePrimaryWeapons", _restorePrimaryWeapons);
                ReadBool(dict, "RestoreHolster", _restoreHolster);
                ReadBool(dict, "RestoreArmorVest", _restoreArmorVest);
                ReadBool(dict, "RestoreTacticalVest", _restoreTacticalVest);
                ReadBool(dict, "RestoreBackpack", _restoreBackpack);
                ReadBool(dict, "RestoreHeadwear", _restoreHeadwear);
                ReadBool(dict, "RestoreEarpiece", _restoreEarpiece);
                ReadBool(dict, "RestoreFaceCover", _restoreFaceCover);
                ReadBool(dict, "RestoreEyewear", _restoreEyewear);

                ReadBool(dict, "KeepBackpackItems", _keepBackpackItems);
                ReadBool(dict, "KeepPocketItems", _keepPocketItems);
                ReadBool(dict, "KeepRigItems", _keepRigItems);

                ReadBool(dict, "KeepMeds", _keepMeds);
                ReadBool(dict, "KeepProvisions", _keepProvisions);
                ReadBool(dict, "KeepAmmo", _keepAmmo);
                ReadBool(dict, "KeepMagazines", _keepMagazines);
                ReadBool(dict, "KeepGrenades", _keepGrenades);
                ReadBool(dict, "KeepKeys", _keepKeys);
                ReadBool(dict, "KeepMoney", _keepMoney);
                ReadBool(dict, "KeepBarterItems", _keepBarterItems);

                ReadBool(dict, "RestoreArmBand", _restoreArmBand);
                ReadBool(dict, "RestoreSpecialSlots", _restoreSpecialSlots);
                ReadBool(dict, "RestoreScabbard", _restoreScabbard);
                ReadBool(dict, "RestoreSecuredContainer", _restoreSecuredContainer);
                ReadBool(dict, "OnlyRestoreInsured", _onlyRestoreInsured);
                ReadBool(dict, "KeepQuestItems", _keepQuestItems);
                ReadBool(dict, "StripFoundInRaid", _stripFoundInRaid);
            }
            finally
            {
                _suppressEvents = false;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Could not read server config.jsonc: {ex.Message}");
        }
    }

    private static void ReadFloat(Dictionary<string, object> dict, string key, ConfigEntry<float> entry)
    {
        if (dict.TryGetValue(key, out var val) && val != null)
            entry.Value = Convert.ToSingle(val, CultureInfo.InvariantCulture);
    }

    private static void ReadBool(Dictionary<string, object> dict, string key, ConfigEntry<bool> entry)
    {
        if (dict.TryGetValue(key, out var val) && val is bool b)
            entry.Value = b;
    }

    private void SaveToServerJson()
    {
        if (_serverConfigPath == null) return;
        try
        {
            var dir = Path.GetDirectoryName(_serverConfigPath);
            if (dir != null) Directory.CreateDirectory(dir);

            static string FmtFloat(float v) => Math.Round(v, 1).ToString("0.0", CultureInfo.InvariantCulture);
            static string FmtBool(bool v) => v ? "true" : "false";

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"ModEnabled\": {FmtBool(_modEnabled.Value)},");
            sb.AppendLine($"  \"Preset\": \"{_preset.Value}\",");
            sb.AppendLine($"  \"DurabilityLossPercent\": {FmtFloat(_durabilityLossPercent.Value)},");
            sb.AppendLine($"  \"DurabilityLossVariancePercent\": {FmtFloat(_durabilityLossVariancePercent.Value)},");
            sb.AppendLine($"  \"MinDurabilityFloorPercent\": {FmtFloat(_minDurabilityFloorPercent.Value)},");
            sb.AppendLine($"  \"AffectMaxDurability\": {FmtBool(_affectMaxDurability.Value)},");
            sb.AppendLine($"  \"ConsumableResourceLossPercent\": {FmtFloat(_consumableResourceLossPercent.Value)},");
            sb.AppendLine($"  \"RestorePrimaryWeapons\": {FmtBool(_restorePrimaryWeapons.Value)},");
            sb.AppendLine($"  \"RestoreHolster\": {FmtBool(_restoreHolster.Value)},");
            sb.AppendLine($"  \"RestoreArmorVest\": {FmtBool(_restoreArmorVest.Value)},");
            sb.AppendLine($"  \"RestoreTacticalVest\": {FmtBool(_restoreTacticalVest.Value)},");
            sb.AppendLine($"  \"RestoreBackpack\": {FmtBool(_restoreBackpack.Value)},");
            sb.AppendLine($"  \"RestoreHeadwear\": {FmtBool(_restoreHeadwear.Value)},");
            sb.AppendLine($"  \"RestoreEarpiece\": {FmtBool(_restoreEarpiece.Value)},");
            sb.AppendLine($"  \"RestoreFaceCover\": {FmtBool(_restoreFaceCover.Value)},");
            sb.AppendLine($"  \"RestoreEyewear\": {FmtBool(_restoreEyewear.Value)},");
            sb.AppendLine($"  \"KeepBackpackItems\": {FmtBool(_keepBackpackItems.Value)},");
            sb.AppendLine($"  \"KeepPocketItems\": {FmtBool(_keepPocketItems.Value)},");
            sb.AppendLine($"  \"KeepRigItems\": {FmtBool(_keepRigItems.Value)},");
            sb.AppendLine($"  \"KeepMeds\": {FmtBool(_keepMeds.Value)},");
            sb.AppendLine($"  \"KeepProvisions\": {FmtBool(_keepProvisions.Value)},");
            sb.AppendLine($"  \"KeepAmmo\": {FmtBool(_keepAmmo.Value)},");
            sb.AppendLine($"  \"KeepMagazines\": {FmtBool(_keepMagazines.Value)},");
            sb.AppendLine($"  \"KeepGrenades\": {FmtBool(_keepGrenades.Value)},");
            sb.AppendLine($"  \"KeepKeys\": {FmtBool(_keepKeys.Value)},");
            sb.AppendLine($"  \"KeepMoney\": {FmtBool(_keepMoney.Value)},");
            sb.AppendLine($"  \"KeepBarterItems\": {FmtBool(_keepBarterItems.Value)},");
            sb.AppendLine($"  \"RestoreArmBand\": {FmtBool(_restoreArmBand.Value)},");
            sb.AppendLine($"  \"RestoreSpecialSlots\": {FmtBool(_restoreSpecialSlots.Value)},");
            sb.AppendLine($"  \"RestoreScabbard\": {FmtBool(_restoreScabbard.Value)},");
            sb.AppendLine($"  \"RestoreSecuredContainer\": {FmtBool(_restoreSecuredContainer.Value)},");
            sb.AppendLine($"  \"OnlyRestoreInsured\": {FmtBool(_onlyRestoreInsured.Value)},");
            sb.AppendLine($"  \"KeepQuestItems\": {FmtBool(_keepQuestItems.Value)},");
            sb.AppendLine($"  \"StripFoundInRaid\": {FmtBool(_stripFoundInRaid.Value)}");
            sb.AppendLine("}");

            if (_watcher != null) _watcher.EnableRaisingEvents = false;
            try
            {
                File.WriteAllText(_serverConfigPath, sb.ToString());
            }
            finally
            {
                if (_watcher != null) _watcher.EnableRaisingEvents = true;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Could not write server config.jsonc: {ex.Message}");
        }
    }
}
