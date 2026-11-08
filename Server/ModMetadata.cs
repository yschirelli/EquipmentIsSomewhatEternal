using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Web;

namespace EquipmentIsEternal;

public record ModMetadata : IModMetadata, IModBlazorMetadata
{
    public string ModGuid { get; init; } = "com.yschirelli.equipmentissomewhateternal";
    public string Name { get; init; } = "Equipment Is Somewhat Eternal";
    public string Author { get; init; } = "yschirelli";
    public List<string>? Contributors { get; init; } = ["ScrewTSW"];
    public SemanticVersioning.Version Version { get; init; } = new("1.0.0");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; } = ["eu.thescrewcollab.equipmentiseternal"];
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "https://github.com/yschirelli/EquipmentIsSomewhatEternal";
    public string License { get; init; } = "MIT";

    public string? WWWRootUrl { get; set; } = "EquipmentIsSomewhatEternal";
    public string? HomePage { get; set; } = "/equipment-is-somewhat-eternal";
    public string? HomePageDescription { get; set; } = "Configure difficulty presets, durability penalties, and item retention filters.";
}
