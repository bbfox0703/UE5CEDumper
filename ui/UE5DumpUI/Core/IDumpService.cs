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
    Task<ObjectListResult> SearchObjectsAsync(string query, int limit = 200, CancellationToken ct = default);
    Task<ClassInfoModel> WalkClassAsync(string addr, CancellationToken ct = default);
    Task<byte[]> ReadMemAsync(string addr, int size, CancellationToken ct = default);
    Task WriteMemAsync(string addr, byte[] data, CancellationToken ct = default);
    Task WatchAsync(string addr, int size, int intervalMs, CancellationToken ct = default);
    Task UnwatchAsync(string addr, CancellationToken ct = default);

    // --- Live Data Walker ---
    Task<InstanceWalkResult> WalkInstanceAsync(string addr, string? classAddr = null, int arrayLimit = 64, int previewLimit = 2, CancellationToken ct = default);
    Task<WorldWalkResult> WalkWorldAsync(int actorLimit = 200, int arrayLimit = 64, CancellationToken ct = default);
    Task<FindInstancesResult> FindInstancesAsync(string className, bool exactMatch = false, int limit = 500, CancellationToken ct = default);
    Task<CePointerInfo> GetCePointerInfoAsync(string addr, int fieldOffset = 0, CancellationToken ct = default);

    // --- DataTable Row Browsing ---
    Task<DataTableWalkResult> WalkDataTableRowsAsync(string addr, int offset = 0, int limit = 64, CancellationToken ct = default);

    // --- Array Element Reading (Phase B) ---
    Task<ArrayElementsResult> ReadArrayElementsAsync(
        string instanceAddr, int fieldOffset,
        string innerAddr, string innerType, int elemSize,
        int offset = 0, int limit = 64, CancellationToken ct = default);

    // --- Address-to-Instance Reverse Lookup ---
    Task<AddressLookupResult> FindByAddressAsync(string addr, CancellationToken ct = default);

    // --- Enum Enumeration ---
    Task<List<EnumDefinition>> ListEnumsAsync(CancellationToken ct = default);

    // --- Function Walking (for SDK export) ---
    Task<List<FunctionInfoModel>> WalkFunctionsAsync(string addr, CancellationToken ct = default);

    // --- Property Keyword Search ---
    Task<PropertySearchResult> SearchPropertiesAsync(
        string query, string[]? types = null, bool gameOnly = true,
        int limit = 200, CancellationToken ct = default);

    // --- Game Class List ---
    Task<ClassListResult> ListClassesAsync(
        bool gameOnly = true, int limit = 5000, CancellationToken ct = default);

    // --- Extra Scan (user-triggered aggressive fallback) ---
    Task<RescanStartResult> StartRescanAsync(CancellationToken ct = default);
    Task<RescanStatusResult> GetRescanStatusAsync(CancellationToken ct = default);
    Task<EngineState> ApplyRescanAsync(CancellationToken ct = default);

    // --- Trigger Scan (proxy DLL deferred scan) ---
    /// <summary>
    /// Start async AOB scan. Used when proxy DLL starts without scanning.
    /// Returns immediately — poll progress with GetScanStatusAsync().
    /// Also safe to call in CE/manual mode — UE5_Init is idempotent.
    /// </summary>
    Task TriggerScanAsync(CancellationToken ct = default);

    /// <summary>
    /// Poll scan progress after TriggerScanAsync(). Returns phase, status text,
    /// and full EngineState when scan is complete (phase >= 7).
    /// </summary>
    Task<ScanStatusResult> GetScanStatusAsync(CancellationToken ct = default);

    // --- UFunction Invocation via Pipe ---

    /// <summary>
    /// Invoke a UFunction via ProcessEvent through the pipe.
    /// The DLL executes ProcessEvent in-process, bypassing CE's executeCodeEx.
    /// Works even when games block CreateRemoteThread.
    /// </summary>
    /// <param name="funcName">UFunction name to invoke.</param>
    /// <param name="instanceAddr">Hex address of target UObject instance (optional if className provided).</param>
    /// <param name="className">Class name to auto-resolve instance (optional if instanceAddr provided).</param>
    /// <param name="parmsSize">Total parameter buffer size from UFunction.</param>
    /// <param name="paramsHex">Hex-encoded param bytes (optional).</param>
    Task<InvokeFunctionResult> InvokeFunctionAsync(
        string funcName,
        string? instanceAddr = null,
        string? className = null,
        int parmsSize = 0,
        string? paramsHex = null,
        CancellationToken ct = default);
}
