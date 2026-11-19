namespace UE5DumpUI.Models;

/// <summary>
/// Which Windows DLL we hijack to inject the dumper into a game's process.
/// User picks one of these via the Proxy Deploy panel.
/// </summary>
public enum ProxyType
{
    /// <summary>
    /// Hijack version.dll. Loaded by virtually every Windows process via
    /// GetFileVersionInfo / COM init / manifest parsing — most reliable
    /// activation timing for the broadest set of games.
    /// </summary>
    Version,

    /// <summary>
    /// Hijack dinput8.dll. Loaded by UE games that use gamepad / DirectInput
    /// support. Useful when version.dll hijacking conflicts with the game's
    /// installer or anti-tamper shim.
    /// </summary>
    Dinput8,

    /// <summary>
    /// Hijack dxgi.dll. Statically imported by every D3D11/D3D12 Unreal
    /// Engine game on Windows (CreateDXGIFactory1/2). The reliable target
    /// for EXEs that import neither version.dll nor dinput8.dll (e.g. some
    /// SQUARE ENIX UE4 builds). Does not apply to Vulkan/OpenGL-only games.
    /// </summary>
    Dxgi,

    /// <summary>
    /// Hijack winmm.dll. It reaches the same UE games as <see cref="Dxgi"/>, so
    /// it reaches nothing dxgi misses.
    ///
    /// <para>It exists as a second free <b>slot</b>, not for coverage: a proxy
    /// only works if its filename is unused, and <c>dxgi.dll</c> is the name
    /// ReShade and many mod loaders take, while <c>version.dll</c> is likewise
    /// a common ASI/mod-loader name (e.g. Ultimate ASI Loader). When both are
    /// taken, winmm is the remaining universally-importable choice — few UE games
    /// import dinput8.</para>
    /// </summary>
    Winmm,
}

/// <summary>
/// Helpers for mapping ProxyType to file names and source DLL paths.
/// AOT-safe: pure switch expressions, no reflection.
/// </summary>
public static class ProxyTypeExtensions
{
    /// <summary>
    /// Return the on-disk file name (e.g. "version.dll") for the proxy type.
    /// </summary>
    public static string GetDllName(this ProxyType type) => type switch
    {
        ProxyType.Version => Constants.ProxyDllName,
        ProxyType.Dinput8 => Constants.ProxyDllNameDinput8,
        ProxyType.Dxgi    => Constants.ProxyDllNameDxgi,
        ProxyType.Winmm   => Constants.ProxyDllNameWinmm,
        _                 => Constants.ProxyDllName,
    };

    /// <summary>
    /// Return a short human-readable label (used in error messages, logs).
    /// </summary>
    public static string GetDisplayName(this ProxyType type) => type switch
    {
        ProxyType.Version => "version.dll",
        ProxyType.Dinput8 => "dinput8.dll",
        ProxyType.Dxgi    => "dxgi.dll",
        ProxyType.Winmm   => "winmm.dll",
        _                 => "version.dll",
    };

    /// <summary>
    /// Map a proxy DLL file name (e.g. from the DLL's self-reported load_mode
    /// "proxy:dxgi.dll") back to the enum. Case-insensitive; returns null for a
    /// non-proxy name (UE5Dumper.dll / anything else). AOT-safe: pure comparison.
    /// </summary>
    public static ProxyType? FromDllName(string? dllName)
    {
        if (string.IsNullOrEmpty(dllName)) return null;
        if (string.Equals(dllName, Constants.ProxyDllName, StringComparison.OrdinalIgnoreCase)) return ProxyType.Version;
        if (string.Equals(dllName, Constants.ProxyDllNameDinput8, StringComparison.OrdinalIgnoreCase)) return ProxyType.Dinput8;
        if (string.Equals(dllName, Constants.ProxyDllNameDxgi, StringComparison.OrdinalIgnoreCase)) return ProxyType.Dxgi;
        if (string.Equals(dllName, Constants.ProxyDllNameWinmm, StringComparison.OrdinalIgnoreCase)) return ProxyType.Winmm;
        return null;
    }
}
