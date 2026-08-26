namespace EquipmentIsEternal;

public record ModConfig
{
    public bool RestoreArmBand { get; init; }
    public bool RestoreSpecialSlots { get; init; }
    public bool RestoreScabbard { get; init; }
    public bool RestoreSecuredContainer { get; init; }
    public bool OnlyRestoreInsured { get; init; }
    public bool KeepQuestItems { get; init; }
    public bool DeduplicateItems { get; init; } = true;
}
