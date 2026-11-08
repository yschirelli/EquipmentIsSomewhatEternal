using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Spt.Launcher;

namespace EquipmentIsEternal;

[Injectable]
public class EquipmentModPage : ModPage
{
    public EquipmentModPage()
    {
        Name = "Equipment Is Somewhat Eternal";
        HomePage = "/equipment-is-somewhat-eternal";
        Description = "Configure difficulty presets, durability penalties, and item retention filters.";
    }
}
