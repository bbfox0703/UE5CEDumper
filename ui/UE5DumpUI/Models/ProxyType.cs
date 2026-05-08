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
        _                 => Constants.ProxyDllName,
    };

    /// <summary>
    /// Return a short human-readable label (used in error messages, logs).
    /// </summary>
    public static string GetDisplayName(this ProxyType type) => type switch
    {
        ProxyType.Version => "version.dll",
        ProxyType.Dinput8 => "dinput8.dll",
        _                 => "version.dll",
    };
}
