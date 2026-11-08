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
        var initialConfig = modHelper.GetJsonDataFromFile<ModConfig>(modPath, "config.jsonc");

        ConfigStore.VerifySelfCheck();
        ConfigStore.Initialize(modPath, initialConfig);
        var config = ConfigStore.Load();

        EquipmentSnapshot.SetBackupDir(Path.Combine(modPath, "backups"));

        raidStartPatch.Enable();
        raidEndPatch.Enable();
        saveProfilePatch.Enable();

        if (config.EnableWebUi)
        {
            WebConfigServer.Start(modPath, config.WebUiPort, logger);
        }

        logger.Success($"EquipmentIsSomewhatEternal loaded (Preset: {config.Preset}, DurabilityLoss: {config.DurabilityLossPercent}%)");
        return Task.CompletedTask;
    }
}
