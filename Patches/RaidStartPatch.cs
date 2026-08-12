using System.Reflection;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Services.InRaid;

namespace EquipmentIsEternal.Patches;

[Injectable]
public class RaidStartPatch : AbstractPatch
{
    private static ISptLogger<RaidStartPatch> _logger = default!;
    private static ProfileHelper _profileHelper = default!;

    private static readonly string[] SnapshotSlots =
    [
        "FirstPrimaryWeapon", "SecondPrimaryWeapon", "Holster",
        "Pockets", "TacticalVest", "ArmorVest",
        "Eyewear", "FaceCover", "Headwear", "Earpiece",
        "ArmBand", "Scabbard", "SecuredContainer"
    ];

    public RaidStartPatch(ISptLogger<RaidStartPatch> logger, ProfileHelper profileHelper)
    {
        _logger = logger;
        _profileHelper = profileHelper;
    }

    protected override MethodBase GetTargetMethod()
    {
        return typeof(LocationLifecycleService).GetMethod(
            "StartLocalRaidAsync",
            BindingFlags.Public | BindingFlags.Instance)!;
    }

    [PatchPrefix]
    public static void Prefix(MongoId sessionId, StartLocalRaidRequestData request)
    {
        var profile = _profileHelper.GetFullProfile(sessionId);
        var pmcData = profile?.CharacterData?.PmcData;
        if (pmcData?.Inventory?.Items == null || pmcData.Inventory.Equipment == null)
            return;

        var equipmentId = pmcData.Inventory.Equipment.ToString()!;
        var allItems = pmcData.Inventory.Items;

        var equipmentItems = new List<Item>();
        foreach (var slot in SnapshotSlots)
        {
            equipmentItems.AddRange(
                EquipmentSnapshot.GetItemAndChildren(allItems, equipmentId, slot));
        }

        equipmentItems.AddRange(
            EquipmentSnapshot.GetItemAndChildrenByPrefix(allItems, equipmentId, "SpecialSlot"));

        var insuredIds = pmcData.InsuredItems?
            .Where(i => i.ItemId != null)
            .Select(i => i.ItemId!.ToString()!)
            .ToList() ?? [];

        var snapshot = new EquipmentSnapshot.SnapshotData
        {
            EquipmentItems = EquipmentSnapshot.DeepCloneItems(equipmentItems),
            InsuredItemIds = insuredIds
        };

        EquipmentSnapshot.Store(sessionId.ToString()!, snapshot);

        var profileBackupPath = EquipmentSnapshot.WriteProfileBackup(sessionId.ToString()!, profile!);
        if (profileBackupPath != null)
            _logger.Info($"EquipmentIsEternal: Profile backup written to {profileBackupPath}");

        _logger.Info($"EquipmentIsEternal: Snapshotted {equipmentItems.Count} equipment items, {insuredIds.Count} insured item IDs for session {sessionId}");
    }
}
