using UE5DumpUI.Models;

namespace UE5DumpUI.Core;

/// <summary>
/// Business logic service for interacting with the UE5 Dumper DLL via pipe.
/// </summary>
public interface IDumpService
{
    Task<EngineState> InitAsync(CancellationToken ct = default);
    Task<EngineState> GetPointersAsync(CancellationToken ct = default);
    Task<int> GetObjectCountAsync(CancellationToken ct = default);
    Task<ObjectListResult> GetObjectListAsync(int offset, int limit, CancellationToken ct = default);
    Task<ObjectDetail> GetObjectAsync(string addr, CancellationToken ct = default);
    Task<ObjectDetail> FindObjectAsync(string path, CancellationToken ct = default);
    Task<ClassInfoModel> WalkClassAsync(string addr, CancellationToken ct = default);
    Task<byte[]> ReadMemAsync(string addr, int size, CancellationToken ct = default);
    Task WriteMemAsync(string addr, byte[] data, CancellationToken ct = default);
    Task WatchAsync(string addr, int size, int intervalMs, CancellationToken ct = default);
    Task UnwatchAsync(string addr, CancellationToken ct = default);
}
