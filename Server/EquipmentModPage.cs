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

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "iframe");
        builder.AddAttribute(1, "src", "/equipment-is-somewhat-eternal/ui");
        builder.AddAttribute(2, "title", "Equipment Is Somewhat Eternal Configuration");
        builder.AddAttribute(3, "style", "position:fixed;inset:0;width:100vw;height:100vh;border:none;margin:0;padding:0;background:#111315;z-index:9999;");
        builder.CloseElement();
    }
}
