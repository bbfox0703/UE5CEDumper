namespace UE5DumpUI.Models;

/// <summary>
/// [LIVEFUNCS-UI-SNAP-MEMORY] What one Call Trace load keeps of the parameter copies it reads.
/// </summary>
public sealed class SnapCopyKeeper
{
    private readonly List<SnapSlot> _slots = new();

    public SnapCopyKeeper(long decodedBudgetBytes)
    {
        DecodedBudgetBytes = decodedBudgetBytes;
    }

    public long DecodedBudgetBytes { get; }
    public IReadOnlyList<SnapSlot> Slots => _slots;
    public long DecodedBytes { get; private set; }
    public long RawBytes { get; private set; }
    public int Dropped { get; private set; }

    public void Add(SnapSlot slot) => _slots.Add(slot);
}
