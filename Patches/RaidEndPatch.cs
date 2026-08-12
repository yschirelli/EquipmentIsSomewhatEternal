using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Services.InRaid;

namespace EquipmentIsEternal.Patches;

[Injectable]
public class RaidEndPatch : AbstractPatch
{
    private static ISptLogger<RaidEndPatch> _logger = default!;
    private static ModConfig _config = default!;

    public RaidEndPatch(ISptLogger<RaidEndPatch> logger)
    {
        _logger = logger;
    }

    public static void SetConfig(ModConfig config) => _config = config;

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

        // Capture quest items before death processing removes them
        List<Item>? questItems = null;
        if (_config.KeepQuestItems)
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
            _logger.Warning($"EquipmentIsEternal: No snapshot found for session {sessionId}");
            return;
        }

        var backupPath = EquipmentSnapshot.WriteBackup(sessionId.ToString()!, snapshot);
        if (backupPath != null)
            _logger.Info($"EquipmentIsEternal: Backup written to {backupPath}");

        EquipmentSnapshot.MarkPendingRestore(sessionId.ToString()!, new EquipmentSnapshot.PendingRestore
        {
            Snapshot = snapshot,
            QuestItems = questItems
        });

        _logger.Info($"EquipmentIsEternal: Death detected, queued restoration of {snapshot.EquipmentItems.Count} items for session {sessionId}");
    }
}
