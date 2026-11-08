using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Services.InRaid;

namespace EquipmentIsEternal.Patches;

[Injectable]
public class RaidEndPatch : AbstractPatch
{
    private static ISptLogger<RaidEndPatch> _logger = default!;

    public RaidEndPatch(ISptLogger<RaidEndPatch> logger)
    {
        _logger = logger;
    }

    protected override MethodBase GetTargetMethod()
    {
        return typeof(LocationLifecycleService).GetMethod(
            "HandlePostRaidPmc",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
    }

    [PatchPrefix]
    public static void Prefix(
        MongoId sessionId,
        SptProfile fullServerProfile,
        bool isDead)
    {
        if (!isDead) return;

        var config = ConfigStore.Load();

        // Capture quest items before death processing removes them
        List<Item>? questItems = null;
        if (config.KeepQuestItems)
        {
            var pmcData = fullServerProfile.CharacterData?.PmcData;
            if (pmcData?.Inventory?.Items != null && pmcData.Inventory.QuestRaidItems != null)
            {
                var questContainerId = pmcData.Inventory.QuestRaidItems.ToString()!;
                var items = EquipmentSnapshot.GetAllChildrenOf(pmcData.Inventory.Items, questContainerId);
                questItems = EquipmentSnapshot.DeepCloneItems(items);
            }
        }

        var snapshot = EquipmentSnapshot.Retrieve(sessionId.ToString()!);
        if (snapshot == null)
        {
            _logger.Warning($"EquipmentIsSomewhatEternal: No snapshot found for session {sessionId}");
            return;
        }

        var backupPath = EquipmentSnapshot.WriteBackup(sessionId.ToString()!, snapshot);
        if (backupPath != null)
            _logger.Info($"EquipmentIsSomewhatEternal: Backup written to {backupPath}");

        EquipmentSnapshot.MarkPendingRestore(sessionId.ToString()!, new EquipmentSnapshot.PendingRestore
        {
            Snapshot = snapshot,
            QuestItems = questItems
        });

        _logger.Info($"EquipmentIsSomewhatEternal: Death detected, queued restoration of {snapshot.EquipmentItems.Count} items for session {sessionId}");
    }
}
