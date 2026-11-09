using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Servers;

namespace EquipmentIsEternal.Patches;

[Injectable]
public class SaveProfilePatch : AbstractPatch
{
    private static ISptLogger<SaveProfilePatch> _logger = default!;
    private static ProfileHelper _profileHelper = default!;
    private static ItemHelper _itemHelper = default!;

    private static readonly string[] Tier1Slots =
    [
        "FirstPrimaryWeapon", "SecondPrimaryWeapon", "Holster",
        "Backpack", "Pockets", "TacticalVest", "ArmorVest",
        "Eyewear", "FaceCover", "Headwear", "Earpiece"
    ];

    // Standard EFT BaseClass MongoIds from items.json
    private static readonly MongoId BaseClassMeds = new("543be5664bdc2dd4348b4569");
    private static readonly MongoId BaseClassFoodDrink = new("543be6674bdc2df1348b4569");
    private static readonly MongoId BaseClassAmmo = new("5485a8684bdc2da71d8b4567");
    private static readonly MongoId BaseClassAmmoBox = new("543be5cb4bdc2deb348b4568");
    private static readonly MongoId BaseClassMagazine = new("5448bc234bdc2d3c308b4569");
    private static readonly MongoId BaseClassThrowWeap = new("543be6564bdc2df4348b4568");
    private static readonly MongoId BaseClassKey = new("543be5e94bdc2df1348b4568");
    private static readonly MongoId BaseClassMoney = new("543be5dd4bdc2deb348b4569");
    private static readonly MongoId BaseClassBarterItem = new("5448eb774bdc2d0a728b4567");
    private static readonly MongoId BaseClassArmorPlate = new("644120aa86ffbe10ee032b6f");

    public SaveProfilePatch(
        ISptLogger<SaveProfilePatch> logger,
        ProfileHelper profileHelper,
        ItemHelper itemHelper)
    {
        _logger = logger;
        _profileHelper = profileHelper;
        _itemHelper = itemHelper;
    }

    protected override MethodBase GetTargetMethod()
    {
        return typeof(SaveServer).GetMethod(
            "SaveProfileAsync",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            [typeof(MongoId), typeof(CancellationToken)],
            null)!;
    }

    [PatchPrefix]
    public static void Prefix(MongoId sessionID)
    {
        var pending = EquipmentSnapshot.ConsumePendingRestore(sessionID.ToString()!);
        if (pending == null) return;

        var config = ConfigStore.Load();
        if (!config.ModEnabled)
        {
            _logger.Info($"EquipmentIsSomewhatEternal: Mod is disabled via config (ModEnabled=false); skipping restore for session {sessionID}.");
            return;
        }

        var profile = _profileHelper.GetFullProfile(sessionID);
        var pmcData = profile?.CharacterData?.PmcData;
        if (pmcData?.Inventory?.Items == null || pmcData.Inventory.Equipment == null)
        {
            _logger.Error($"EquipmentIsSomewhatEternal: Cannot restore — profile/inventory is null for session {sessionID}");
            return;
        }

        var equipmentId = pmcData.Inventory.Equipment.ToString()!;
        var inventory = pmcData.Inventory.Items;
        var snapshot = pending.Snapshot;
        var restoredCount = 0;

        foreach (var slot in Tier1Slots)
        {
            if (!IsSlotRestorationAllowed(slot, config))
                continue;

            var slotItems = snapshot.EquipmentItems
                .Where(i => i.ParentId == equipmentId && i.SlotId == slot)
                .ToList();

            if (slotItems.Count == 0) continue;

            var itemsToRestore = new List<Item>();
            foreach (var rootItem in slotItems)
            {
                if (config.OnlyRestoreInsured &&
                    !snapshot.InsuredItemIds.Contains(rootItem.Id!.ToString()!))
                    continue;

                if (ShouldStripContainerCargo(slot, config))
                {
                    itemsToRestore.AddRange(GetFilteredContainerTree(snapshot.EquipmentItems, rootItem, config));
                }
                else
                {
                    itemsToRestore.AddRange(GetItemTree(snapshot.EquipmentItems, rootItem));
                }
            }

            if (itemsToRestore.Count == 0) continue;

            ApplyPenalties(itemsToRestore, config);

            RemoveExistingSlotItems(inventory, equipmentId, slot);
            inventory.AddRange(itemsToRestore);
            restoredCount += itemsToRestore.Count;
        }

        RestoreTier2Slot(inventory, snapshot, equipmentId, "ArmBand", config.RestoreArmBand, config, ref restoredCount);
        RestoreTier2Slot(inventory, snapshot, equipmentId, "Scabbard", config.RestoreScabbard, config, ref restoredCount);
        RestoreTier2Slot(inventory, snapshot, equipmentId, "SecuredContainer", config.RestoreSecuredContainer, config, ref restoredCount);

        if (config.RestoreSpecialSlots)
        {
            foreach (var rootItem in snapshot.EquipmentItems
                .Where(i => i.ParentId == equipmentId && i.SlotId != null && i.SlotId.StartsWith("SpecialSlot")))
            {
                if (config.OnlyRestoreInsured &&
                    !snapshot.InsuredItemIds.Contains(rootItem.Id!.ToString()!))
                    continue;

                var itemTree = GetItemTree(snapshot.EquipmentItems, rootItem);
                ApplyPenalties(itemTree, config);
                RemoveExistingSlotItems(inventory, equipmentId, rootItem.SlotId!);
                inventory.AddRange(itemTree);
                restoredCount += itemTree.Count;
            }
        }

        if (config.KeepQuestItems &&
            pending.QuestItems is { Count: > 0 } questItems &&
            pmcData.Inventory.QuestRaidItems != null)
        {
            ApplyPenalties(questItems, config);
            var questContainerId = pmcData.Inventory.QuestRaidItems.ToString()!;
            var existingIds = EquipmentSnapshot.GetAllChildrenOf(inventory, questContainerId)
                .Select(i => i.Id)
                .ToHashSet();
            inventory.RemoveAll(i => existingIds.Contains(i.Id));
            inventory.AddRange(questItems);
            restoredCount += questItems.Count;
        }

        if (restoredCount == 0 && config.OnlyRestoreInsured)
            _logger.Warning($"EquipmentIsSomewhatEternal: Restored 0 items — OnlyRestoreInsured is enabled and no equipment items matched {snapshot.InsuredItemIds.Count} insured item IDs");
        else
            _logger.Success($"EquipmentIsSomewhatEternal: Restored {restoredCount} items for session {sessionID} (Preset={config.Preset}, DurabilityLoss={config.DurabilityLossPercent}%)");

        ResolveDuplicateIds(inventory, sessionID);
    }

    public static bool IsSlotRestorationAllowed(string slot, ModConfig config) => slot switch
    {
        "FirstPrimaryWeapon" or "SecondPrimaryWeapon" => config.RestorePrimaryWeapons,
        "Holster" => config.RestoreHolster,
        "ArmorVest" => config.RestoreArmorVest,
        "TacticalVest" => config.RestoreTacticalVest,
        "Backpack" => config.RestoreBackpack,
        "Headwear" => config.RestoreHeadwear,
        "Earpiece" => config.RestoreEarpiece,
        "FaceCover" => config.RestoreFaceCover,
        "Eyewear" => config.RestoreEyewear,
        _ => true
    };

    private static bool ShouldStripContainerCargo(string slot, ModConfig config) => slot switch
    {
        "Backpack" => !config.KeepBackpackItems,
        "Pockets" => !config.KeepPocketItems,
        "TacticalVest" => !config.KeepRigItems,
        _ => false
    };

    /// <summary>
    /// Restores the container root item, its built-in/removable armor plates, and only direct cargo
    /// items that match enabled category retention toggles.
    /// shortcut: only inspects direct children of the container root, upgrade to re-parent deeply nested cargo if players stack rigs inside lost backpacks.
    /// </summary>
    public static List<Item> GetFilteredContainerTree(List<Item> allItems, Item rootContainer, ModConfig config)
    {
        var result = new List<Item> { rootContainer };
        var rootId = rootContainer.Id!.ToString()!;

        foreach (var child in allItems.Where(i => i.ParentId == rootId))
        {
            if (IsArmorInsertOrPlate(child))
            {
                result.Add(child);
                EquipmentSnapshot.CollectChildren(allItems, child.Id!.ToString()!, result);
                continue;
            }

            if (!IsCategoryKept(child.Template, config)) continue;

            result.Add(child);

            // If a magazine is kept but KeepAmmo is false, strip the cartridges inside it
            if (IsMagazine(child.Template) && !config.KeepAmmo)
            {
                foreach (var magChild in allItems.Where(i => i.ParentId == child.Id!.ToString() && i.SlotId != "cartridges"))
                {
                    result.Add(magChild);
                    EquipmentSnapshot.CollectChildren(allItems, magChild.Id!.ToString()!, result);
                }
            }
            else
            {
                EquipmentSnapshot.CollectChildren(allItems, child.Id!.ToString()!, result);
            }
        }

        return result;
    }

    public static bool IsArmorInsertOrPlate(Item item)
    {
        // Loose items inside a storage grid always have grid coordinates (Location != null);
        // installed armor plates and soft inserts occupy named equipment slots (Location == null).
        if (item.Location != null) return false;

        if (item.SlotId != null &&
            (_itemHelper != null && _itemHelper.IsSoftInsertId(item.SlotId) ||
             item.SlotId.StartsWith("Armor_plate", StringComparison.OrdinalIgnoreCase) ||
             item.SlotId.StartsWith("Soft_armor", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return _itemHelper != null && _itemHelper.IsOfBaseclass(item.Template, BaseClassArmorPlate);
    }

    private static bool IsMagazine(MongoId tpl) =>
        _itemHelper != null && _itemHelper.IsOfBaseclass(tpl, BaseClassMagazine);

    private static bool IsCategoryKept(MongoId tpl, ModConfig config)
    {
        if (_itemHelper == null) return false;

        if (config.KeepMeds && _itemHelper.IsOfBaseclass(tpl, BaseClassMeds)) return true;
        if (config.KeepProvisions && _itemHelper.IsOfBaseclass(tpl, BaseClassFoodDrink)) return true;
        if (config.KeepAmmo && (_itemHelper.IsOfBaseclass(tpl, BaseClassAmmo) || _itemHelper.IsOfBaseclass(tpl, BaseClassAmmoBox))) return true;
        if (config.KeepMagazines && _itemHelper.IsOfBaseclass(tpl, BaseClassMagazine)) return true;
        if (config.KeepGrenades && _itemHelper.IsOfBaseclass(tpl, BaseClassThrowWeap)) return true;
        if (config.KeepKeys && _itemHelper.IsOfBaseclass(tpl, BaseClassKey)) return true;
        if (config.KeepMoney && _itemHelper.IsOfBaseclass(tpl, BaseClassMoney)) return true;
        if (config.KeepBarterItems && _itemHelper.IsOfBaseclass(tpl, BaseClassBarterItem)) return true;

        return false;
    }

    public static void ApplyPenalties(IEnumerable<Item> items, ModConfig config)
    {
        var lossPct = Math.Clamp(config.DurabilityLossPercent, 0f, 100f);
        var variancePct = Math.Clamp(config.DurabilityLossVariancePercent, 0f, 100f);
        var floorPct = Math.Clamp(config.MinDurabilityFloorPercent, 0f, 100f);
        var resourcePct = Math.Clamp(config.ConsumableResourceLossPercent, 0f, 100f);

        if (lossPct <= 0f && resourcePct <= 0f && !config.StripFoundInRaid) return;

        foreach (var item in items)
        {
            if (config.StripFoundInRaid && item.Upd?.SpawnedInSession == true)
            {
                item.Upd.SpawnedInSession = false;
            }

            if (lossPct > 0f &&
                item.Upd?.Repairable?.Durability is double cur &&
                item.Upd.Repairable.MaxDurability is double max &&
                max > 0)
            {
                var effectiveLossPct = lossPct;
                if (variancePct > 0f)
                {
                    var jitter = (Random.Shared.NextDouble() * 2.0 - 1.0) * variancePct;
                    effectiveLossPct = (float)Math.Clamp(lossPct + jitter, 0.0, 100.0);
                }

                var baseMax = GetTemplateMaxDurability(item.Template, max);
                var loss = baseMax * (effectiveLossPct / 100.0);
                var floor = baseMax * (floorPct / 100.0);

                if (config.AffectMaxDurability)
                {
                    max = Math.Round(Math.Min(max, Math.Max(floor, max - loss)), 2);
                    item.Upd.Repairable.MaxDurability = max;
                }

                var newCur = Math.Min(cur, Math.Max(floor, cur - loss));
                item.Upd.Repairable.Durability = Math.Round(Math.Min(max, newCur), 2);
            }

            if (resourcePct > 0f)
            {
                if (item.Upd?.MedKit?.HpResource is double hp && hp > 0)
                {
                    item.Upd.MedKit.HpResource = Math.Max(1.0, Math.Round(hp * (1.0 - resourcePct / 100.0), 1));
                }

                if (item.Upd?.FoodDrink?.HpPercent is double foodHp && foodHp > 0)
                {
                    item.Upd.FoodDrink.HpPercent = Math.Max(1.0, Math.Round(foodHp * (1.0 - resourcePct / 100.0), 1));
                }
            }
        }
    }

    private static double GetTemplateMaxDurability(MongoId tpl, double fallbackMax)
    {
        if (_itemHelper != null &&
            _itemHelper.GetItem(tpl) is { Key: true, Value.Properties.MaxDurability: double tplMax } &&
            tplMax > 0)
        {
            return tplMax;
        }

        return fallbackMax;
    }

    private static void ResolveDuplicateIds(List<Item> inventory, MongoId sessionID)
    {
        var seen = new HashSet<string>();
        var conflicts = new List<Item>();

        foreach (var item in inventory)
        {
            var id = item.Id.ToString();
            if (id == null) continue;
            if (!seen.Add(id)) conflicts.Add(item);
        }

        if (conflicts.Count == 0) return;

        var doomed = new HashSet<Item>(conflicts, ReferenceEqualityComparer.Instance);
        var removed = inventory.RemoveAll(doomed.Contains);
        _logger.Warning(
            $"EquipmentIsSomewhatEternal: Removed {removed} duplicate item(s) for session {sessionID}");
    }

    private static void RestoreTier2Slot(
        List<Item> inventory,
        EquipmentSnapshot.SnapshotData snapshot,
        string equipmentId,
        string slotId,
        bool enabled,
        ModConfig config,
        ref int restoredCount)
    {
        if (!enabled) return;

        var rootItem = snapshot.EquipmentItems
            .FirstOrDefault(i => i.ParentId == equipmentId && i.SlotId == slotId);
        if (rootItem == null) return;

        if (config.OnlyRestoreInsured &&
            !snapshot.InsuredItemIds.Contains(rootItem.Id!.ToString()!))
            return;

        var itemTree = GetItemTree(snapshot.EquipmentItems, rootItem);
        ApplyPenalties(itemTree, config);
        RemoveExistingSlotItems(inventory, equipmentId, slotId);
        inventory.AddRange(itemTree);
        restoredCount += itemTree.Count;
    }

    private static List<Item> GetItemTree(List<Item> allItems, Item root)
    {
        var result = new List<Item> { root };
        EquipmentSnapshot.CollectChildren(allItems, root.Id!.ToString()!, result);
        return result;
    }

    private static void RemoveExistingSlotItems(List<Item> inventory, string equipmentId, string slotId)
    {
        var existing = EquipmentSnapshot.GetItemAndChildren(inventory, equipmentId, slotId);
        var idsToRemove = existing.Select(i => i.Id).ToHashSet();
        inventory.RemoveAll(i => idsToRemove.Contains(i.Id));
    }
}
