namespace UE5DumpUI.Models;

/// <summary>
/// A running process offered as a DLL-injection target in the process picker.
/// <paramref name="IsUe"/> flags whether the executable looks like a UE4/UE5 game
/// (name / path heuristics — see <c>UeProcessDetector</c>).
///
/// The <c>Dumper*</c> fields report whether OUR dumper DLL is ALREADY loaded in
/// the target (via a proxy DLL, a prior inject, or a Cheat Engine .CT) and the
/// version of the DLL actually running — populated by <c>DumperModuleDetector</c>
/// from the process's loaded modules. When true, the UI connects to the copy that is
/// already serving instead of injecting: a second copy would take the game over from it,
/// and it would shut down. Left false/null when detection is unavailable (access denied, etc.).
/// </summary>
public sealed record GameProcessInfo(
    int Pid, string Name, string Path, bool IsUe,
    bool DumperLoaded = false, string? DumperLoadMode = null, string? DumperVersion = null)
{
    /// <summary>One-line label shown in the picker list.</summary>
    public string Display => $"{Name}   (PID {Pid})";

    /// <summary>Human-readable "already loaded" line for the picker, or "" when the
    /// dumper is not (known to be) loaded. E.g. "UE5Dumper loaded (proxy: dxgi.dll) v1.0.0.1982".</summary>
    public string DumperStatusLine
    {
        get
        {
            if (!DumperLoaded)
                return "";
            string mode = string.IsNullOrEmpty(DumperLoadMode) ? "" : $" ({DumperLoadMode})";
            string ver = string.IsNullOrEmpty(DumperVersion) ? "" : $" v{DumperVersion}";
            return $"UE5Dumper loaded{mode}{ver}";
        }
    }
}
