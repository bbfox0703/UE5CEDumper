using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Pure classification of a target process's loaded modules into "is OUR dumper
/// DLL already loaded, and how?". Kept IO-free (the caller supplies the module
/// list — file name + PE ProductName + FileVersion) so the rule is unit-testable
/// without a live process. The Windows platform service gathers the raw module
/// list (toolhelp / <c>Process.Modules</c>) and calls <see cref="Classify"/>.
///
/// A module is OURS when its PE ProductName equals <see cref="Constants.ProxyProductName"/>
/// ("UE5CEDumper") — the same identity check <c>ProxyDeployService.IsOurProxyDll</c>
/// uses. This deliberately ignores the real system <c>version.dll</c> / <c>dxgi.dll</c>
/// (Microsoft ProductName) that every process loads.
/// </summary>
internal static class DumperModuleDetector
{
    /// <summary>Our proxy DLL file names (lowercase). When one of these carries our
    /// ProductName it is a proxy load; otherwise the same ProductName on
    /// <c>ue5dumper.dll</c> is an inject / CE .CT load.</summary>
    /// <remarks>Derived from <see cref="ProxyType"/> rather than hardcoded, so a new
    /// proxy flavour cannot silently desync this list from the enum — a hand-kept
    /// list leaves the double-inject guards blind to every flavour it misses.</remarks>
    private static readonly string[] ProxyNames =
        Enum.GetValues<ProxyType>().Select(t => t.GetDllName().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Every module name this detector cares about: the proxies plus the
    /// directly-injected DLL. Exposed so the platform layer's module filter reads
    /// from the same source instead of repeating the literals.</summary>
    public static bool IsInterestingModuleName(string lowerName) =>
        lowerName == InjectedModuleName
        || ProxyNames.Contains(lowerName, StringComparer.OrdinalIgnoreCase);

    private const string InjectedModuleName = "ue5dumper.dll";

    /// <summary>A single loaded module as seen from the picker's point of view.</summary>
    public readonly record struct ModuleInfo(string Name, string? ProductName, string? Version);

    /// <summary>
    /// Decide whether our dumper DLL is present in <paramref name="modules"/> and,
    /// if so, how it was loaded and which version. Returns
    /// <c>(false, null, null)</c> when no module carries our ProductName.
    /// A proxy load is preferred over an inject load when both appear (the proxy is
    /// the one that blocks re-injection). Version comes from the winning module.
    /// </summary>
    public static (bool Loaded, string? Mode, string? Version) Classify(
        IReadOnlyList<ModuleInfo> modules)
    {
        ModuleInfo? proxy = null;
        ModuleInfo? injected = null;
        ModuleInfo? other = null;

        foreach (var m in modules)
        {
            if (!IsOurs(m.ProductName))
                continue;

            string lower = (m.Name ?? "").ToLowerInvariant();
            if (Array.IndexOf(ProxyNames, lower) >= 0)
                proxy ??= m;
            else if (string.Equals(lower, InjectedModuleName, StringComparison.Ordinal))
                injected ??= m;
            else
                other ??= m;
        }

        if (proxy is { } p)
            return (true, $"proxy: {p.Name}", NullIfEmpty(p.Version));
        if (injected is { } i)
            return (true, "injected", NullIfEmpty(i.Version));
        if (other is { } o)
            return (true, $"loaded: {o.Name}", NullIfEmpty(o.Version));
        return (false, null, null);
    }

    private static bool IsOurs(string? productName)
        => string.Equals(productName, Constants.ProxyProductName, StringComparison.OrdinalIgnoreCase);

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
