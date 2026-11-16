namespace UE5DumpUI.Models;

// RED: the state type only.
public readonly record struct AiWriteBlockState(bool Blocked, long RevokeStamp, bool Unreadable);
