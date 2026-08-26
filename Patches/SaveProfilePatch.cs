using System.Reflection;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
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
    private static ModConfig _config = default!;

    private static readonly string[] Tier1Slots =
    [
        "FirstPrimaryWeapon", "SecondPrimaryWeapon", "Holster",
        "Backpack", "Pockets", "TacticalVest", "ArmorVest",
        "Eyewear", "FaceCover", "Headwear", "Earpiece"
    ];

    public SaveProfilePatch(ISptLogger<SaveProfilePatch> logger, ProfileHelper profileHelper)
    {
        _logger = logger;
        _profileHelper = profileHelper;
    }

    public static void SetConfig(ModConfig config) => _config = config;

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

        var profile = _profileHelper.GetFullProfile(sessionID);
        var pmcData = profile?.CharacterData?.PmcData;
        if (pmcData?.Inventory?.Items == null || pmcData.Inventory.Equipment == null)
        {
            _logger.Error($"EquipmentIsEternal: Cannot restore — profile/inventory is null for session {sessionID}");
            return;
        }

        var equipmentId = pmcData.Inventory.Equipment.ToString()!;
        var inventory = pmcData.Inventory.Items;
        var snapshot = pending.Snapshot;
        var restoredCount = 0;

        foreach (var slot in Tier1Slots)
        {
            var slotItems = snapshot.EquipmentItems
                .Where(i => i.ParentId == equipmentId && i.SlotId == slot)
                .ToList();

            if (slotItems.Count == 0) continue;

            var itemsToRestore = new List<Item>();
            foreach (var rootItem in slotItems)
            {
                if (_config.OnlyRestoreInsured &&
                    !snapshot.InsuredItemIds.Contains(rootItem.Id!.ToString()!))
                    continue;

                itemsToRestore.AddRange(GetItemTree(snapshot.EquipmentItems, rootItem));
            }

            if (itemsToRestore.Count == 0) continue;

            RemoveExistingSlotItems(inventory, equipmentId, slot);
            inventory.AddRange(itemsToRestore);
            restoredCount += itemsToRestore.Count;
        }

        RestoreTier2Slot(inventory, snapshot, equipmentId, "ArmBand", _config.RestoreArmBand, ref restoredCount);
        RestoreTier2Slot(inventory, snapshot, equipmentId, "Scabbard", _config.RestoreScabbard, ref restoredCount);
        RestoreTier2Slot(inventory, snapshot, equipmentId, "SecuredContainer", _config.RestoreSecuredContainer, ref restoredCount);

        if (_config.RestoreSpecialSlots)
        {
            foreach (var rootItem in snapshot.EquipmentItems
                .Where(i => i.ParentId == equipmentId && i.SlotId != null && i.SlotId.StartsWith("SpecialSlot")))
            {
                var itemTree = GetItemTree(snapshot.EquipmentItems, rootItem);
                RemoveExistingSlotItems(inventory, equipmentId, rootItem.SlotId!);
                inventory.AddRange(itemTree);
                restoredCount += itemTree.Count;
            }
        }

        if (_config.KeepQuestItems &&
            pending.QuestItems is { Count: > 0 } questItems &&
            pmcData.Inventory.QuestRaidItems != null)
        {
            var questContainerId = pmcData.Inventory.QuestRaidItems.ToString()!;
            var existingIds = EquipmentSnapshot.GetAllChildrenOf(inventory, questContainerId)
                .Select(i => i.Id)
                .ToHashSet();
            inventory.RemoveAll(i => existingIds.Contains(i.Id));
            inventory.AddRange(questItems);
            restoredCount += questItems.Count;
        }

        if (restoredCount == 0 && _config.OnlyRestoreInsured)
            _logger.Warning($"EquipmentIsEternal: Restored 0 items — OnlyRestoreInsured is enabled and no equipment items matched {snapshot.InsuredItemIds.Count} insured item IDs");
        else
            _logger.Success($"EquipmentIsEternal: Restored {restoredCount} items for session {sessionID} (pre-save)");

        // Final verification: the restore above re-adds snapshot items by their original ID.
        // If the same item was moved to a different slot in-raid (e.g. Pockets -> SecuredContainer),
        // RemoveExistingSlotItems only cleared the original slot, so the in-raid copy survives
        // under a different parent and the ID now appears twice. Left alone this throws
        // "An item with the same key already exists" when the profile is indexed by ID.
        ResolveDuplicateIds(inventory, sessionID);
    }

    /// <summary>
    /// Ensures every item ID in the inventory is unique.
    /// The first occurrence of an ID is authoritative (restore appends, so this is the
    /// pre-existing in-raid copy); later occurrences are the conflicts.
    /// DeduplicateItems=true drops them, false re-keys them onto a fresh ID.
    /// </summary>
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

        if (_config.DeduplicateItems)
        {
            // Remove by reference so only the duplicate entries go, not every item sharing the ID.
            var doomed = new HashSet<Item>(conflicts, ReferenceEqualityComparer.Instance);
            var removed = inventory.RemoveAll(doomed.Contains);
            _logger.Warning(
                $"EquipmentIsEternal: Removed {removed} duplicate item(s) for session {sessionID} " +
                $"(DeduplicateItems=true)");
            return;
        }

        // Keep both copies: give each conflict a fresh ID.
        //
        // Children are linked by ParentId, which is ambiguous while two items share an ID —
        // there is no way to tell from the data which instance a given child belongs to.
        // Position disambiguates it: the restore appends snapshot items to the end, so any
        // child already present BEFORE the conflicting entry belongs to the earlier (in-raid)
        // instance and must keep pointing at it. Only children that arrived with the restored
        // copy — i.e. after it in the list — follow the new ID.
        var rekeyed = 0;
        foreach (var item in conflicts)
        {
            var oldId = item.Id.ToString()!;
            var newId = new MongoId();
            var conflictIndex = inventory.IndexOf(item);

            for (var i = conflictIndex + 1; i < inventory.Count; i++)
            {
                if (inventory[i].ParentId?.ToString() == oldId)
                    inventory[i].ParentId = newId;
            }

            item.Id = newId;
            rekeyed++;
        }

        _logger.Warning(
            $"EquipmentIsEternal: Re-keyed {rekeyed} conflicting item(s) for session {sessionID} " +
            $"(DeduplicateItems=false)");
    }

    private static void RestoreTier2Slot(
        List<Item> inventory,
        EquipmentSnapshot.SnapshotData snapshot,
        string equipmentId,
        string slotId,
        bool enabled,
        ref int restoredCount)
    {
        if (!enabled) return;

        var rootItem = snapshot.EquipmentItems
            .FirstOrDefault(i => i.ParentId == equipmentId && i.SlotId == slotId);
        if (rootItem == null) return;

        var itemTree = GetItemTree(snapshot.EquipmentItems, rootItem);
        RemoveExistingSlotItems(inventory, equipmentId, slotId);
        inventory.AddRange(itemTree);
        restoredCount += itemTree.Count;
    }

    private static List<Item> GetItemTree(List<Item> allItems, Item root)
    {
        var result = new List<Item> { root };
        EquipmentSnapshot.CollectChildrenPublic(allItems, root.Id!.ToString()!, result);
        return result;
    }

    private static void RemoveExistingSlotItems(List<Item> inventory, string equipmentId, string slotId)
    {
        var existing = EquipmentSnapshot.GetItemAndChildren(inventory, equipmentId, slotId);
        var idsToRemove = existing.Select(i => i.Id).ToHashSet();
        inventory.RemoveAll(i => idsToRemove.Contains(i.Id));
    }
}
