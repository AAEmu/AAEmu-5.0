// Keep the legacy global enum name used by packet and item task code.
#pragma warning disable CA1050
public enum ItemTaskLogType : byte
{
    UpdateOnly = 0,
    GainItem = 1,
    RemoveItem = 2,
    MoveItem = 3,
    SwapItem = 4,
    Place = 5
}
