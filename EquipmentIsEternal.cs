using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using EquipmentIsEternal.Patches;

namespace EquipmentIsEternal;

[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public class EquipmentIsEternal(
    ISptLogger<EquipmentIsEternal> logger,
    ModHelper modHelper,
    RaidStartPatch raidStartPatch,
    RaidEndPatch raidEndPatch,
    SaveProfilePatch saveProfilePatch) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var config = modHelper.GetJsonDataFromFile<ModConfig>(modPath, "config.jsonc");

        RaidEndPatch.SetConfig(config);
        SaveProfilePatch.SetConfig(config);
        EquipmentSnapshot.SetBackupDir(Path.Combine(modPath, "backups"));

        raidStartPatch.Enable();
        raidEndPatch.Enable();
        saveProfilePatch.Enable();

        logger.Success("EquipmentIsEternal loaded");
        logger.Info($"  OnlyRestoreInsured: {config.OnlyRestoreInsured}");
        logger.Info($"  KeepQuestItems: {config.KeepQuestItems}");
        logger.Info($"  RestoreArmBand: {config.RestoreArmBand}");
        logger.Info($"  RestoreSpecialSlots: {config.RestoreSpecialSlots}");
        logger.Info($"  RestoreScabbard: {config.RestoreScabbard}");
        logger.Info($"  RestoreSecuredContainer: {config.RestoreSecuredContainer}");
        logger.Info($"  DeduplicateItems: {config.DeduplicateItems}");

        return Task.CompletedTask;
    }
}
