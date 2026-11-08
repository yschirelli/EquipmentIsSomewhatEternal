using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace EquipmentIsEternal;

[Route("/equipment-is-somewhat-eternal")]
public class EquipmentModPage : ComponentBase
{
    [Inject]
    public NavigationManager? Navigation { get; set; }

    protected override void OnInitialized()
    {
        Navigation?.NavigateTo("/equipment-is-somewhat-eternal/ui", forceLoad: true);
    }
}
