using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

public class ProxyDeployTests
{
    // ────────────────────────────────────────────────────────────────
    // VDF Parser
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void VdfParser_ValidContent_ExtractsPaths()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"        "C:\\Program Files (x86)\\Steam"
                    "label"       ""
                    "contentid"   "1234567890"
                }
                "1"
                {
                    "path"        "D:\\SteamLibrary"
                    "label"       ""
                }
            }
            """;

        var paths = VdfParser.ParseLibraryFolders(vdf);

        Assert.Equal(2, paths.Count);
        Assert.Equal(@"C:\Program Files (x86)\Steam", paths[0]);
        Assert.Equal(@"D:\SteamLibrary", paths[1]);
    }

    [Fact]
    public void VdfParser_EmptyContent_ReturnsEmptyList()
    {
        var paths = VdfParser.ParseLibraryFolders("");
        Assert.Empty(paths);
    }

    [Fact]
    public void VdfParser_NullContent_ReturnsEmptyList()
    {
        var paths = VdfParser.ParseLibraryFolders(null!);
        Assert.Empty(paths);
    }

    [Fact]
    public void VdfParser_WhitespaceOnly_ReturnsEmptyList()
    {
        var paths = VdfParser.ParseLibraryFolders("   \n\t  ");
        Assert.Empty(paths);
    }

    [Fact]
    public void VdfParser_MalformedContent_ReturnsEmptyGracefully()
    {
        var paths = VdfParser.ParseLibraryFolders("this is not vdf {{{{");
        // Should not throw, returns whatever it could parse (likely empty)
        Assert.NotNull(paths);
    }

    [Fact]
    public void VdfParser_EscapedBackslashes_UnescapesCorrectly()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"        "E:\\Games\\Steam Library"
                }
            }
            """;

        var paths = VdfParser.ParseLibraryFolders(vdf);

        Assert.Single(paths);
        Assert.Equal(@"E:\Games\Steam Library", paths[0]);
    }

    [Fact]
    public void VdfParser_NoPathKeys_ReturnsEmpty()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "label"        "main"
                    "contentid"    "99999"
                }
            }
            """;

        var paths = VdfParser.ParseLibraryFolders(vdf);
        Assert.Empty(paths);
    }

    [Fact]
    public void VdfParser_WithComments_IgnoresComments()
    {
        const string vdf = """
            // This is a comment
            "libraryfolders"
            {
                // Another comment
                "0"
                {
                    "path"        "C:\\Steam"
                    // "path"     "X:\\Fake"
                }
            }
            """;

        var paths = VdfParser.ParseLibraryFolders(vdf);
        Assert.Single(paths);
        Assert.Equal(@"C:\Steam", paths[0]);
    }

    [Fact]
    public void VdfParser_NestedDepth_OnlyExtractsDepth2()
    {
        // "path" at depth 3 (inside a sub-object) should be ignored
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"        "C:\\Steam"
                    "apps"
                    {
                        "path"    "should_be_ignored"
                    }
                }
            }
            """;

        var paths = VdfParser.ParseLibraryFolders(vdf);
        Assert.Single(paths);
        Assert.Equal(@"C:\Steam", paths[0]);
    }

    [Fact]
    public void VdfParser_RealWorldFormat_ParsesCorrectly()
    {
        // Closer to real Steam libraryfolders.vdf format
        const string vdf = """
            "libraryfolders"
            {
            	"0"
            	{
            		"path"		"C:\\Program Files (x86)\\Steam"
            		"label"		""
            		"contentid"		"8756120948756120948"
            		"totalsize"		"0"
            		"update_clean_bytes_tally"		"349829384752"
            		"time_last_update_corruption"		"0"
            		"apps"
            		{
            			"228980"		"597558935"
            			"250820"		"4493034958"
            		}
            	}
            	"1"
            	{
            		"path"		"D:\\SteamLibrary"
            		"label"		""
            		"contentid"		"1234567890123456789"
            		"totalsize"		"2000398934016"
            		"update_clean_bytes_tally"		"182739827139"
            		"time_last_update_corruption"		"0"
            		"apps"
            		{
            			"292030"		"46289375689"
            		}
            	}
            }
            """;

        var paths = VdfParser.ParseLibraryFolders(vdf);
        Assert.Equal(2, paths.Count);
        Assert.Equal(@"C:\Program Files (x86)\Steam", paths[0]);
        Assert.Equal(@"D:\SteamLibrary", paths[1]);
    }

    // ────────────────────────────────────────────────────────────────
    // VDF parser — key/value position and nesting validity (audit #5 AC12)
    //
    // The old walker tracked brace depth ONLY, so any depth-2 token reading "path"
    // was taken for a key and a brace imbalance was invisible.
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// THE FINDING. A library whose label is the word "path" made the walker read the
    /// NEXT key as a folder, injecting a directory Steam never named.
    ///
    /// NEGATIVE CONTROL: drop the key/value alternation and match on any depth-2 token
    /// equal to "path", and this returns TWO paths — the real one plus "contentid".
    /// </summary>
    [Fact]
    public void VdfParser_ValueSpelledPath_DoesNotMasqueradeAsAKey()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"        "D:\\SteamLibrary"
                    "label"       "path"
                    "contentid"   "1234567890"
                }
            }
            """;

        var paths = VdfParser.ParseLibraryFolders(vdf, out string? error);

        Assert.Null(error);
        Assert.Single(paths);
        Assert.Equal(@"D:\SteamLibrary", paths[0]);
    }

    [Fact]
    public void VdfParser_UnclosedBrace_IsReportedNotSilent()
    {
        // Truncated mid-file: everything read before the truncation is still usable,
        // but the caller must be able to say the file was broken.
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"        "D:\\SteamLibrary"
            """;

        var paths = VdfParser.ParseLibraryFolders(vdf, out string? error);

        Assert.Single(paths);
        Assert.Equal(@"D:\SteamLibrary", paths[0]);
        Assert.NotNull(error);
        Assert.Contains("unclosed", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VdfParser_ExtraClosingBrace_StopsAndReports()
    {
        // A stray '}' used to drive depth NEGATIVE, after which every later block was
        // read one level shallow and the file silently yielded zero libraries.
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"        "C:\\Steam"
                }
                }
                "1"
                {
                    "path"        "D:\\SteamLibrary"
                }
            }
            """;

        var paths = VdfParser.ParseLibraryFolders(vdf, out string? error);

        Assert.NotNull(error);
        Assert.Contains("unbalanced", error!, StringComparison.OrdinalIgnoreCase);
        Assert.Single(paths);                       // what was valid before the fault
        Assert.Equal(@"C:\Steam", paths[0]);
    }

    [Fact]
    public void VdfParser_BlockWithoutAKey_IsRejected()
    {
        var paths = VdfParser.ParseLibraryFolders("\"libraryfolders\" \"x\" { }", out string? error);
        Assert.Empty(paths);
        Assert.NotNull(error);
    }

    [Fact]
    public void VdfParser_TrailingKeyWithNoValue_IsReported()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"
                }
            }
            """;

        var paths = VdfParser.ParseLibraryFolders(vdf, out string? error);
        Assert.Empty(paths);
        Assert.NotNull(error);
    }

    [Fact]
    public void VdfParser_PlatformConditional_DoesNotDesyncTheAlternation()
    {
        // A bare [$WIN32] suffixes a statement — it is neither key nor value. Treating it
        // as one would shift every following token by one and turn "label" into a key's
        // value (and "contentid" into a key).
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"        "C:\\Steam"    [$WIN32]
                    "label"       ""
                    "contentid"   "42"
                }
            }
            """;

        var paths = VdfParser.ParseLibraryFolders(vdf, out string? error);

        Assert.Null(error);
        Assert.Single(paths);
        Assert.Equal(@"C:\Steam", paths[0]);
    }

    [Fact]
    public void VdfParser_QuotedBracketString_IsStillAValue()
    {
        // ...but a QUOTED "[$WIN32]" is an ordinary string and must keep its place.
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "label"       "[$WIN32]"
                    "path"        "C:\\Steam"
                }
            }
            """;

        var paths = VdfParser.ParseLibraryFolders(vdf, out string? error);

        Assert.Null(error);
        Assert.Single(paths);
        Assert.Equal(@"C:\Steam", paths[0]);
    }

    [Fact]
    public void VdfParser_CleanDocument_ReportsNoError()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"        "C:\\Steam"
                    "apps"
                    {
                        "228980"  "597558935"
                    }
                }
            }
            """;

        VdfParser.ParseLibraryFolders(vdf, out string? error);
        Assert.Null(error);
    }

    // ────────────────────────────────────────────────────────────────
    // Staged proxy publish (audit #5 AC11)
    // ────────────────────────────────────────────────────────────────

    [Theory]
    // faithful copy → publish
    [InlineData(1024L, 1024L, true,  true,  true)]
    [InlineData(1024L, 1024L, false, false, true)]   // a dev proxy with no ProductName still deploys
    // short write → refuse
    [InlineData(1024L,  512L, true,  true,  false)]
    // unmeasurable staged file (-1) → refuse, never "assume fine"
    [InlineData(1024L,   -1L, true,  true,  false)]
    // unmeasurable or empty SOURCE → refuse
    [InlineData(  -1L,   -1L, true,  true,  false)]
    [InlineData(   0L,    0L, true,  true,  false)]
    // right size but the version resource did not survive → refuse
    [InlineData(1024L, 1024L, true,  false, false)]
    public void ShouldPublishStagedProxy_TruthTable(
        long sourceBytes, long stagedBytes, bool sourceIsOurs, bool stagedIsOurs, bool expected)
    {
        Assert.Equal(expected, ProxyDeployService.ShouldPublishStagedProxy(
            sourceBytes, stagedBytes, sourceIsOurs, stagedIsOurs));
    }

    [Fact]
    public void CopyProxyStaged_FirstTimeDeploy_CreatesTargetAndLeavesNoResidue()
    {
        // The named regression risk of staging: a deploy onto a clean folder must still
        // work, because File.Move(overwrite: true) is also the create path.
        using var dir = new TempDir();
        string src = dir.Write("source.dll", "PROXY-BYTES");
        string dst = Path.Combine(dir.Path, "dxgi.dll");

        ProxyDeployService.CopyProxyStaged(src, dst);

        Assert.True(File.Exists(dst));
        Assert.Equal("PROXY-BYTES", File.ReadAllText(dst));
        Assert.False(File.Exists(dst + ProxyDeployService.StageSuffix));
    }

    [Fact]
    public void CopyProxyStaged_OverExistingTarget_ReplacesIt()
    {
        using var dir = new TempDir();
        string src = dir.Write("source.dll", "NEW-BYTES");
        string dst = dir.Write("dxgi.dll", "OLD");

        ProxyDeployService.CopyProxyStaged(src, dst);

        Assert.Equal("NEW-BYTES", File.ReadAllText(dst));
        Assert.False(File.Exists(dst + ProxyDeployService.StageSuffix));
    }

    /// <summary>
    /// A guard, NOT the negative control: measured, the direct
    /// <c>File.Copy(src, target, overwrite: true)</c> also leaves the target alone here,
    /// because Windows opens the source before it truncates the destination. Kept because
    /// it pins the no-residue half.
    /// </summary>
    [Fact]
    public void CopyProxyStaged_FailedSource_LeavesTheLiveProxyUntouched()
    {
        using var dir = new TempDir();
        string missing = Path.Combine(dir.Path, "does-not-exist.dll");
        string dst = dir.Write("dxgi.dll", "THE-WORKING-PROXY");

        Assert.ThrowsAny<IOException>(() => ProxyDeployService.CopyProxyStaged(missing, dst));

        Assert.Equal("THE-WORKING-PROXY", File.ReadAllText(dst));
        Assert.False(File.Exists(dst + ProxyDeployService.StageSuffix));
    }

    /// <summary>
    /// THE FINDING. A failing deploy used to TRUNCATE the live proxy: the game then fails
    /// to start, the grid reads the wreckage as another program's DLL (no version resource
    /// survives a truncated PE), and on that verdict BOTH removal paths refuse it — the
    /// user's own wreckage is unremovable from the panel that produced it.
    ///
    /// NEGATIVE CONTROL (run): put back the direct
    /// <c>File.Copy(src, target, overwrite: true)</c> and this fails together with
    /// <c>CopyProxyStaged_StaleStageFile_IsOverwrittenNotOrphaned</c> — 2 of 4284, and no
    /// others. A zero-byte source is maximal truncation, which is why it is the case that
    /// discriminates.
    /// </summary>
    [Fact]
    public void CopyProxyStaged_EmptySource_RefusesAndLeavesTheLiveProxyUntouched()
    {
        // A zero-byte source is a refusal, not a pass: publishing it would leave exactly
        // the unremovable-wreckage state staging exists to prevent.
        using var dir = new TempDir();
        string src = dir.Write("source.dll", "");
        string dst = dir.Write("dxgi.dll", "THE-WORKING-PROXY");

        Assert.ThrowsAny<IOException>(() => ProxyDeployService.CopyProxyStaged(src, dst));

        Assert.Equal("THE-WORKING-PROXY", File.ReadAllText(dst));
        Assert.False(File.Exists(dst + ProxyDeployService.StageSuffix));
    }

    [Fact]
    public void CopyProxyStaged_StaleStageFile_IsOverwrittenNotOrphaned()
    {
        using var dir = new TempDir();
        string src = dir.Write("source.dll", "NEW-BYTES");
        string dst = Path.Combine(dir.Path, "dxgi.dll");
        File.WriteAllText(dst + ProxyDeployService.StageSuffix, "leftover from a killed run");

        ProxyDeployService.CopyProxyStaged(src, dst);

        Assert.Equal("NEW-BYTES", File.ReadAllText(dst));
        Assert.False(File.Exists(dst + ProxyDeployService.StageSuffix));
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                          $"UE5DumpProxyStage_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Write(string name, string content)
        {
            var p = System.IO.Path.Combine(Path, name);
            File.WriteAllText(p, content);
            return p;
        }

        public void Dispose()
        {
            try { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    // ────────────────────────────────────────────────────────────────
    // DetectedGame Model
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void DetectedGame_DefaultValues()
    {
        var game = new DetectedGame();

        Assert.Equal("", game.Name);
        Assert.Equal("", game.ExePath);
        Assert.Equal("", game.BinariesDir);
        Assert.Null(game.UeVersion);
        Assert.Equal(ProxyDeployStatus.NotDeployed, game.Status);
        Assert.Null(game.InstalledVersion);
        Assert.Null(game.ErrorMessage);
        Assert.False(game.IsSelected);
    }

    [Fact]
    public void DetectedGame_InitProperties()
    {
        var game = new DetectedGame
        {
            Name = "Test Game",
            ExePath = @"C:\Games\Test\Binaries\Win64\Game-Win64-Shipping.exe",
            BinariesDir = @"C:\Games\Test\Binaries\Win64",
            UeVersion = "5.3",
        };

        Assert.Equal("Test Game", game.Name);
        Assert.Contains("Win64", game.ExePath);
        Assert.Equal("5.3", game.UeVersion);
    }

    [Fact]
    public void DetectedGame_StatusMutable()
    {
        var game = new DetectedGame();
        Assert.Equal(ProxyDeployStatus.NotDeployed, game.Status);

        game.Status = ProxyDeployStatus.DeployedCurrent;
        Assert.Equal(ProxyDeployStatus.DeployedCurrent, game.Status);

        game.Status = ProxyDeployStatus.OtherProxy;
        Assert.Equal(ProxyDeployStatus.OtherProxy, game.Status);
    }

    // ────────────────────────────────────────────────────────────────
    // ProxyDeployStatus Enum
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ProxyDeployStatus_AllValues()
    {
        var values = Enum.GetValues<ProxyDeployStatus>();

        Assert.Equal(7, values.Length);
        Assert.Contains(ProxyDeployStatus.NotDeployed, values);
        Assert.Contains(ProxyDeployStatus.DeployedCurrent, values);
        Assert.Contains(ProxyDeployStatus.DeployedOutdated, values);
        Assert.Contains(ProxyDeployStatus.OtherProxy, values);
        Assert.Contains(ProxyDeployStatus.DeployedOtherType, values);
        Assert.Contains(ProxyDeployStatus.ErrorLocked, values);
        Assert.Contains(ProxyDeployStatus.ErrorOther, values);
    }

    // ────────────────────────────────────────────────────────────────
    // Constants
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Constants_ProxyDeploy_Defined()
    {
        Assert.Equal("version.dll", Constants.ProxyDllName);
        Assert.Equal("dinput8.dll", Constants.ProxyDllNameDinput8);
        Assert.Equal("dxgi.dll", Constants.ProxyDllNameDxgi);
        Assert.Equal("UE5CEDumper", Constants.ProxyProductName);
        Assert.False(string.IsNullOrEmpty(Constants.SteamRegistryPath));
        Assert.False(string.IsNullOrEmpty(Constants.SteamRegistryKey));
        Assert.False(string.IsNullOrEmpty(Constants.SteamDefaultPath));
        Assert.False(string.IsNullOrEmpty(Constants.SteamLibraryFoldersVdf));
        Assert.False(string.IsNullOrEmpty(Constants.SteamAppsCommon));
    }

    // ────────────────────────────────────────────────────────────────
    // ProxyType
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ProxyType_Version_MapsToVersionDll()
    {
        Assert.Equal("version.dll", ProxyType.Version.GetDllName());
        Assert.Equal("version.dll", ProxyType.Version.GetDisplayName());
    }

    [Fact]
    public void ProxyType_Dinput8_MapsToDinput8Dll()
    {
        Assert.Equal("dinput8.dll", ProxyType.Dinput8.GetDllName());
        Assert.Equal("dinput8.dll", ProxyType.Dinput8.GetDisplayName());
    }

    [Fact]
    public void ProxyType_Dxgi_MapsToDxgiDll()
    {
        Assert.Equal("dxgi.dll", ProxyType.Dxgi.GetDllName());
        Assert.Equal("dxgi.dll", ProxyType.Dxgi.GetDisplayName());
    }

    [Fact]
    public void ProxyType_AllValuesHaveMappings()
    {
        // Guard against silently breaking the switch when a new enum value is
        // added — every defined ProxyType must yield a non-empty DLL name.
        foreach (ProxyType type in Enum.GetValues<ProxyType>())
        {
            Assert.False(string.IsNullOrEmpty(type.GetDllName()));
            Assert.False(string.IsNullOrEmpty(type.GetDisplayName()));
        }
    }

    [Fact]
    public void ProxyType_AllProxyTypes_HaveDistinctDllNames()
    {
        var names = Enum.GetValues<ProxyType>().Select(t => t.GetDllName()).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ────────────────────────────────────────────────────────────────
    // IsKnownStubExe
    // ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("CrashReportClient.exe", true)]
    [InlineData("crashreportclient.exe", true)]
    [InlineData("CRASHREPORTCLIENT.EXE", true)]
    [InlineData("FactoryGameSteam-Win64-Shipping.exe", false)]
    [InlineData("Game-Win64-Shipping.exe", false)]
    [InlineData("", false)]
    public void IsKnownStubExe_FiltersCrashReportClient(string exeName, bool expected)
    {
        Assert.Equal(expected, ProxyDeployService.IsKnownStubExe(exeName));
    }

    // ────────────────────────────────────────────────────────────────
    // BuildConflictMessage — redundancy warning only when 2+ coexist
    // (build 1165 fix: a single deployed proxy of any type is NOT a conflict,
    //  regardless of which proxy type the UI has selected)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildConflictMessage_NoProxies_ReturnsNull()
    {
        Assert.Null(ProxyDeployService.BuildConflictMessage(Array.Empty<string>()));
    }

    [Fact]
    public void BuildConflictMessage_SingleProxy_ReturnsNull()
    {
        // The exact false-positive the user hit: only dxgi.dll deployed, but
        // viewing the version.dll tab must NOT warn.
        Assert.Null(ProxyDeployService.BuildConflictMessage(new[] { "dxgi.dll" }));
        Assert.Null(ProxyDeployService.BuildConflictMessage(new[] { "version.dll" }));
    }

    [Fact]
    public void BuildConflictMessage_TwoProxies_WarnsAndListsBoth()
    {
        var msg = ProxyDeployService.BuildConflictMessage(new[] { "version.dll", "dxgi.dll" });
        Assert.NotNull(msg);
        Assert.Contains("version.dll", msg);
        Assert.Contains("dxgi.dll", msg);
        Assert.Contains("only one will activate", msg);
    }

    [Fact]
    public void BuildConflictMessage_ThreeProxies_ListsAll()
    {
        // N-proxy-safe: a future 4th type would extend this naturally.
        var msg = ProxyDeployService.BuildConflictMessage(new[] { "version.dll", "dinput8.dll", "dxgi.dll" });
        Assert.NotNull(msg);
        Assert.Contains("version.dll", msg);
        Assert.Contains("dinput8.dll", msg);
        Assert.Contains("dxgi.dll", msg);
    }

    // ────────────────────────────────────────────────────────────────
    // ClassifyAbsentSelected — selected proxy type absent (build: the user's
    // report: on the version.dll tab a game with dxgi.dll already deployed
    // wrongly read "NotDeployed"; it must read DeployedOtherType instead)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void ClassifyAbsentSelected_CleanFolder_IsNotDeployed()
    {
        var (status, message) = ProxyDeployService.ClassifyAbsentSelected(Array.Empty<string>());
        Assert.Equal(ProxyDeployStatus.NotDeployed, status);
        Assert.Null(message);
    }

    [Fact]
    public void ClassifyAbsentSelected_OneOtherProxy_IsDeployedOtherTypeAndNamesIt()
    {
        // Exactly the screenshot: version.dll selected (absent), dxgi.dll deployed.
        var (status, message) = ProxyDeployService.ClassifyAbsentSelected(new[] { "dxgi.dll" });
        Assert.Equal(ProxyDeployStatus.DeployedOtherType, status);
        Assert.NotNull(message);
        Assert.Contains("dxgi.dll", message);
    }

    [Fact]
    public void ClassifyAbsentSelected_TwoOtherProxies_IsDeployedOtherTypeWithNoMessage()
    {
        // 2+ coexisting is additionally surfaced by BuildConflictMessage, so this
        // returns no message to avoid listing the proxies twice.
        var (status, message) = ProxyDeployService.ClassifyAbsentSelected(new[] { "dxgi.dll", "dinput8.dll" });
        Assert.Equal(ProxyDeployStatus.DeployedOtherType, status);
        Assert.Null(message);
    }

    // ────────────────────────────────────────────────────────────────
    // FindUeGames — modular vs monolithic layouts (build 691 Satisfactory fix)
    // ────────────────────────────────────────────────────────────────

    private sealed class NoopLog : ILoggingService
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Error(string message, Exception ex) { }
        public void Debug(string message) { }
        public void Info(string category, string message) { }
        public void Warn(string category, string message) { }
        public void Error(string category, string message) { }
        public void Error(string category, string message, Exception ex) { }
        public void Debug(string category, string message) { }
        public void StartProcessMirror(string processName) { }
        public void StopProcessMirror() { }
    }

    /// <summary>
    /// Build a throwaway Steam-library tree under a temp folder. Returns
    /// the library root that should be fed to <c>FindUeGamesAsync</c>.
    /// The caller is responsible for deleting the returned path.
    /// </summary>
    private static string MakeTempLibrary()
    {
        string lib = Path.Combine(Path.GetTempPath(),
            "UE5DumpTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(lib, "steamapps", "common"));
        return lib;
    }

    private static void MakeEmptyFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Array.Empty<byte>());
    }

    [Fact]
    public async Task FindUeGames_ModularLayout_PicksEngineExeNotCrashReport()
    {
        string lib = MakeTempLibrary();
        try
        {
            // Satisfactory-shape: real .exe lives in <Game>/Engine/Binaries/Win64
            // alongside CrashReportClient.exe. <Game>/FactoryGame/Binaries/Win64
            // has only a .modules manifest + DLLs, no .exe at all.
            string game = Path.Combine(lib, "steamapps", "common", "Satisfactory");
            string engineBin = Path.Combine(game, "Engine", "Binaries", "Win64");
            string factoryBin = Path.Combine(game, "FactoryGame", "Binaries", "Win64");

            MakeEmptyFile(Path.Combine(engineBin, "FactoryGameSteam-Win64-Shipping.exe"));
            MakeEmptyFile(Path.Combine(engineBin, "CrashReportClient.exe"));
            MakeEmptyFile(Path.Combine(factoryBin, "FactoryGameSteam-Win64-Shipping.modules"));
            MakeEmptyFile(Path.Combine(factoryBin, "FactoryGameSteam-FactoryGame-Win64-Shipping.dll"));

            var svc = new ProxyDeployService(new NoopLog(), new NoopPlatform());
            var found = await svc.FindUeGamesAsync(new[] { lib }, TestContext.Current.CancellationToken);

            var sat = Assert.Single(found, g => g.Name == "Satisfactory");
            Assert.Equal(engineBin, sat.BinariesDir);
            Assert.EndsWith("FactoryGameSteam-Win64-Shipping.exe", sat.ExePath);
            Assert.DoesNotContain("CrashReport", sat.ExePath);
        }
        finally
        {
            Directory.Delete(lib, recursive: true);
        }
    }

    [Fact]
    public void ProxyImportInfo_Imports_CoversEveryProxyFlavour()
    {
        // The analyzer tracked version/dinput8/dxgi but NOT winmm, even though ProxyType has had
        // a Winmm member — so winmm could never be recommended, and it is the ONLY proxy that
        // works on Octopath Traveler. Same desync class the DumperModuleDetector comment warns
        // about. Assert every enum value is answerable, so a fifth flavour cannot repeat it.
        var all = new ProxyImportAnalyzer.ProxyImportInfo(true, true, true, true);
        var none = new ProxyImportAnalyzer.ProxyImportInfo(false, false, false, false);
        foreach (var t in Enum.GetValues<ProxyType>())
        {
            Assert.True(all.Imports(t));
            Assert.False(none.Imports(t));
        }
        Assert.True(none.ImportsNone);
        Assert.False(new ProxyImportAnalyzer.ProxyImportInfo(false, false, false, true).ImportsNone);
    }

    [Fact]
    public async Task FindUeGames_WrappedLayout_FindsGameTwoLevelsDown()
    {
        string lib = MakeTempLibrary();
        try
        {
            // NEKOPALIVE-shape: an extra "Package" folder between the Steam game
            // directory and the project, so Binaries\Win64 is TWO levels down —
            // and the exe carries no -Win64-Shipping suffix. A one-level scan
            // misses the whole game, and the Engine fallback misses it too
            // because Engine is not a direct child either.
            string game = Path.Combine(lib, "steamapps", "common", "NEKOPALIVE");
            string gameBin = Path.Combine(game, "Package", "Nekopara", "Binaries", "Win64");
            string engineBin = Path.Combine(game, "Package", "Engine", "Binaries", "Win64");

            MakeEmptyFile(Path.Combine(gameBin, "Nekopara.exe"));
            MakeEmptyFile(Path.Combine(engineBin, "CrashReportClient.exe"));

            var svc = new ProxyDeployService(new NoopLog(), new NoopPlatform());
            var found = await svc.FindUeGamesAsync(new[] { lib }, TestContext.Current.CancellationToken);

            var neko = Assert.Single(found, g => g.Name == "NEKOPALIVE");
            Assert.Equal(gameBin, neko.BinariesDir);
            Assert.EndsWith("Nekopara.exe", neko.ExePath);
            // The Engine tier holds only a stub, so it must not contribute a second row.
            Assert.DoesNotContain("CrashReport", neko.ExePath);
        }
        finally
        {
            Directory.Delete(lib, recursive: true);
        }
    }

    [Fact]
    public async Task FindUeGames_ShallowestDepthWins_BonusContentSuppressed()
    {
        string lib = MakeTempLibrary();
        try
        {
            // P3R-shape: the real game sits at depth 1, while Artbook and Soundtrack are
            // depth-2 bonus apps that are ALSO genuine UE builds with their own Engine folder —
            // so nothing about their contents separates them. Depth does.
            string game = Path.Combine(lib, "steamapps", "common", "P3R");
            string realBin = Path.Combine(game, "P3R", "Binaries", "Win64");
            string artBin = Path.Combine(game, "Artbook", "P3R_Artbook", "Binaries", "Win64");
            string ostBin = Path.Combine(game, "Soundtrack", "P3R_Soundtrack", "Binaries", "Win64");

            MakeEmptyFile(Path.Combine(realBin, "P3R.exe"));
            MakeEmptyFile(Path.Combine(artBin, "P3R_Artbook.exe"));
            MakeEmptyFile(Path.Combine(ostBin, "P3R_Soundtrack.exe"));
            MakeEmptyFile(Path.Combine(game, "Engine", "Binaries", "Win64", "CrashReportClient.exe"));

            var svc = new ProxyDeployService(new NoopLog(), new NoopPlatform());
            var found = await svc.FindUeGamesAsync(new[] { lib }, TestContext.Current.CancellationToken);

            var p3r = Assert.Single(found, g => g.Name == "P3R");
            Assert.Equal(realBin, p3r.BinariesDir);
            Assert.DoesNotContain("Artbook", p3r.ExePath);
            Assert.DoesNotContain("Soundtrack", p3r.ExePath);
        }
        finally
        {
            Directory.Delete(lib, recursive: true);
        }
    }

    [Fact]
    public async Task FindUeGames_DeepSearch_DoesNotDescendIntoContent()
    {
        string lib = MakeTempLibrary();
        try
        {
            // A shipped game's Content tree can hold thousands of folders. Descending it
            // would make every library scan pay for nothing, and a stray .exe parked under
            // Content\...\Binaries\Win64 must not be mistaken for the game.
            string game = Path.Combine(lib, "steamapps", "common", "DeepGame");
            string realBin = Path.Combine(game, "Proj", "Binaries", "Win64");
            string contentBin = Path.Combine(game, "Content", "Trap", "Binaries", "Win64");

            MakeEmptyFile(Path.Combine(realBin, "Proj-Win64-Shipping.exe"));
            MakeEmptyFile(Path.Combine(contentBin, "NotTheGame.exe"));

            var svc = new ProxyDeployService(new NoopLog(), new NoopPlatform());
            var found = await svc.FindUeGamesAsync(new[] { lib }, TestContext.Current.CancellationToken);

            var g = Assert.Single(found, x => x.Name == "DeepGame");
            Assert.Equal(realBin, g.BinariesDir);
            Assert.DoesNotContain("NotTheGame", g.ExePath);
        }
        finally
        {
            Directory.Delete(lib, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyProxySuggestions_RememberedPickWins_ElseVersionDefault_AndClearsWhenDisabled()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ue5lkg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Non-PE files → import parse returns null; that isolates the glue:
            // remembered-pick precedence + version fallback + enabled toggle.
            string exeA = Path.Combine(dir, "GameA.exe");
            string exeB = Path.Combine(dir, "GameB.exe");
            string exeC = Path.Combine(dir, "GameC.exe");
            string exeD = Path.Combine(dir, "GameD.exe");
            File.WriteAllBytes(exeA, new byte[] { 1, 2, 3 });
            File.WriteAllBytes(exeB, new byte[] { 1, 2, 3 });
            File.WriteAllBytes(exeC, new byte[] { 1, 2, 3 });
            File.WriteAllBytes(exeD, new byte[] { 1, 2, 3 });

            var gameA = new DetectedGame { Name = "GameA", ExePath = exeA, BinariesDir = dir };
            var gameB = new DetectedGame { Name = "GameB", ExePath = exeB, BinariesDir = dir };
            var gameC = new DetectedGame { Name = "GameC", ExePath = exeC, BinariesDir = dir };
            var gameD = new DetectedGame { Name = "GameD", ExePath = exeD, BinariesDir = dir };
            var games = new List<DetectedGame> { gameA, gameB, gameC, gameD };
            var remembered = new Dictionary<string, ProxyType>(StringComparer.OrdinalIgnoreCase)
            {
                ["GameA"] = ProxyType.Dxgi,
                ["GameD"] = ProxyType.Dinput8,   // will be beaten by GameD's confirmed pick
            };
            // GameC was injected (by .exe name); GameA also injected but a proxy pick wins.
            var injected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "GameC.exe", "GameA.exe",
            };
            // GameD confirmed-working via version.dll → beats its deployed dinput8 pick.
            var confirmed = new Dictionary<string, ProxyType>(StringComparer.OrdinalIgnoreCase)
            {
                ["GameD.exe"] = ProxyType.Version,
            };

            var svc = new ProxyDeployService(new NoopLog(), new NoopPlatform());

            await svc.ApplyProxySuggestionsAsync(games, confirmed, remembered, injected, enabled: true,
                TestContext.Current.CancellationToken);

            Assert.Equal(ProxyType.Dxgi, gameA.SuggestedProxyType);       // remembered proxy beats injection
            Assert.Equal("dxgi.dll · last used", gameA.SuggestedProxy);
            Assert.Equal(ProxyType.Version, gameB.SuggestedProxyType);    // no history → safe default
            Assert.Equal("version · default", gameB.SuggestedProxy);
            Assert.Null(gameC.SuggestedProxyType);                        // injection-only → no proxy type
            Assert.Equal("injection · no proxy deployed", gameC.SuggestedProxy);
            Assert.Equal(ProxyType.Version, gameD.SuggestedProxyType);    // confirmed beats deployed
            Assert.Equal("version.dll · confirmed working", gameD.SuggestedProxy);

            await svc.ApplyProxySuggestionsAsync(games, confirmed, remembered, injected, enabled: false,
                TestContext.Current.CancellationToken);

            Assert.Null(gameA.SuggestedProxyType);                        // disabled → cleared
            Assert.Null(gameA.SuggestedProxy);
            Assert.Null(gameB.SuggestedProxy);
            Assert.Null(gameC.SuggestedProxy);
            Assert.Null(gameD.SuggestedProxy);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task FindUeGames_MonolithicLayout_StillPicksGameExeNotEngineSide()
    {
        string lib = MakeTempLibrary();
        try
        {
            // Standard monolithic UE: real .exe under <Game>/<Sub>/Binaries/Win64.
            // <Game>/Engine/Binaries/Win64 contains only CrashReportClient.exe — must
            // NOT be surfaced as a phantom row.
            string game = Path.Combine(lib, "steamapps", "common", "MonoGame");
            string engineBin = Path.Combine(game, "Engine", "Binaries", "Win64");
            string gameBin = Path.Combine(game, "MonoGame", "Binaries", "Win64");

            MakeEmptyFile(Path.Combine(engineBin, "CrashReportClient.exe"));
            MakeEmptyFile(Path.Combine(gameBin, "MonoGame-Win64-Shipping.exe"));

            var svc = new ProxyDeployService(new NoopLog(), new NoopPlatform());
            var found = await svc.FindUeGamesAsync(new[] { lib }, TestContext.Current.CancellationToken);

            var mono = Assert.Single(found, g => g.Name == "MonoGame");
            Assert.Equal(gameBin, mono.BinariesDir);
            Assert.EndsWith("MonoGame-Win64-Shipping.exe", mono.ExePath);
        }
        finally
        {
            Directory.Delete(lib, recursive: true);
        }
    }

    [Fact]
    public async Task FindUeGames_OnlyCrashReportInEngineDir_ProducesNoRow()
    {
        string lib = MakeTempLibrary();
        try
        {
            // Edge case: a folder that contains an Engine/Binaries/Win64 with
            // only CrashReportClient.exe and no real game .exe anywhere.
            // Result: no DetectedGame row — we must not surface CrashReport.
            string game = Path.Combine(lib, "steamapps", "common", "OrphanEngine");
            string engineBin = Path.Combine(game, "Engine", "Binaries", "Win64");
            MakeEmptyFile(Path.Combine(engineBin, "CrashReportClient.exe"));

            var svc = new ProxyDeployService(new NoopLog(), new NoopPlatform());
            var found = await svc.FindUeGamesAsync(new[] { lib }, TestContext.Current.CancellationToken);

            Assert.DoesNotContain(found, g => g.Name == "OrphanEngine");
        }
        finally
        {
            Directory.Delete(lib, recursive: true);
        }
    }

    [Fact]
    public async Task FindUeGames_HybridLayout_PrefersGameSubdirOverEngineSide()
    {
        string lib = MakeTempLibrary();
        try
        {
            // StellarBlade / NMKART / Palworld / Titan Quest II shape: the real
            // game exe lives in <Game>\<Sub>\Binaries\Win64\ AND a stub
            // launcher exe lives in <Game>\Engine\Binaries\Win64\. Both look
            // like valid UE shipping exes. We must surface ONE row pointing at
            // the <Sub> path — the Engine-side row would lead the user to
            // deploy version.dll into the wrong folder for monolithic builds.
            string game = Path.Combine(lib, "steamapps", "common", "StellarBlade");
            string engineBin = Path.Combine(game, "Engine", "Binaries", "Win64");
            string sbBin     = Path.Combine(game, "SB", "Binaries", "Win64");

            MakeEmptyFile(Path.Combine(engineBin, "SB-Win64-Shipping.exe"));     // stub launcher
            MakeEmptyFile(Path.Combine(engineBin, "CrashReportClient.exe"));
            MakeEmptyFile(Path.Combine(sbBin,     "SB-Win64-Shipping.exe"));     // real exe

            var svc = new ProxyDeployService(new NoopLog(), new NoopPlatform());
            var found = await svc.FindUeGamesAsync(new[] { lib }, TestContext.Current.CancellationToken);

            var sb = Assert.Single(found, g => g.Name == "StellarBlade");
            Assert.Equal(sbBin, sb.BinariesDir);
        }
        finally
        {
            Directory.Delete(lib, recursive: true);
        }
    }

    [Fact]
    public async Task FindUeGames_PureModularLayout_FallsThroughToEngine()
    {
        string lib = MakeTempLibrary();
        try
        {
            // Repro of the Satisfactory exact shape: <Game>\<Sub>\Binaries\Win64\
            // has NO .exe at all (only the .modules manifest + game-side DLLs),
            // so Engine fallback MUST run and pick up the real launcher exe.
            // Regression-guard against the two-tier search accidentally
            // skipping Engine when <Sub>\Binaries\Win64\ existed but produced
            // no game row.
            string game = Path.Combine(lib, "steamapps", "common", "Satisfactory");
            string engineBin  = Path.Combine(game, "Engine",      "Binaries", "Win64");
            string factoryBin = Path.Combine(game, "FactoryGame", "Binaries", "Win64");

            MakeEmptyFile(Path.Combine(engineBin,  "FactoryGameSteam-Win64-Shipping.exe")); // real
            MakeEmptyFile(Path.Combine(engineBin,  "CrashReportClient.exe"));
            MakeEmptyFile(Path.Combine(factoryBin, "FactoryGameSteam-Win64-Shipping.modules"));
            MakeEmptyFile(Path.Combine(factoryBin, "FactoryGameSteam-FactoryGame-Win64-Shipping.dll"));

            var svc = new ProxyDeployService(new NoopLog(), new NoopPlatform());
            var found = await svc.FindUeGamesAsync(new[] { lib }, TestContext.Current.CancellationToken);

            var sat = Assert.Single(found, g => g.Name == "Satisfactory");
            Assert.Equal(engineBin, sat.BinariesDir);
        }
        finally
        {
            Directory.Delete(lib, recursive: true);
        }
    }

    // ────────────────────────────────────────────────────────────────
    // Generic (non-Steam) drive scan — pure helpers
    // ────────────────────────────────────────────────────────────────

    private sealed class NoopPlatform : IPlatformService
    {
        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => "";
        public string GetLogDirectoryPath() => "";
        public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(true);
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "test";
        public void CloseImeForWindow(IntPtr windowHandle) { }
        public Task<string?> ShowSaveFileDialogAsync(string defaultFileName,
            string filterName, string filterExtension) => Task.FromResult<string?>(null);
    }

    /// <summary>A platform whose %LOCALAPPDATA% points at a temp folder, so the
    /// [PROXYLOAD-2026-08-17] load-signal probe can be exercised end to end.</summary>
    private sealed class AppDataPlatform(string appData) : IPlatformService
    {
        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => appData;
        public string GetLogDirectoryPath() =>
            Path.Combine(appData, Constants.LogFolderName, Constants.LogSubFolder);
        public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(true);
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "test";
        public void CloseImeForWindow(IntPtr windowHandle) { }
        public Task<string?> ShowSaveFileDialogAsync(string defaultFileName,
            string filterName, string filterExtension) => Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task RefreshDeployStatus_SetsLoadObservation_FromPerProcessLogFolder()
    {
        // End-to-end proof of the [PROXYLOAD-2026-08-17] load signal: the join key
        // (ProcessLogFolderName) + the real folder lookup under
        // %LOCALAPPDATA%\UE5CEDumper\Logs. No exe is executed — a plain temp folder stands in
        // for a game that HAS run; a game with no folder is honest UNKNOWN, not a failure.
        string appData = MakeTempDir();
        try
        {
            // A game whose per-process folder was written just now → "loaded <today>".
            string ranDir = Path.Combine(appData, Constants.LogFolderName, Constants.LogSubFolder, "P3R");
            Directory.CreateDirectory(ranDir);
            File.WriteAllText(Path.Combine(ranDir, "scan-0.log"), "hello");

            string binRan = Path.Combine(appData, "binRan");
            string binNever = Path.Combine(appData, "binNever");
            Directory.CreateDirectory(binRan);
            Directory.CreateDirectory(binNever);

            var ran = new DetectedGame
            {
                Name = "Persona 3 Reload", ExePath = @"X:\g\P3R.exe", BinariesDir = binRan
            };
            var never = new DetectedGame
            {
                Name = "Octopath",
                ExePath = @"X:\g\Octopath-Win64-Shipping.exe",
                BinariesDir = binNever
            };

            var svc = new ProxyDeployService(new NoopLog(), new AppDataPlatform(appData));
            await svc.RefreshDeployStatusAsync(
                new List<DetectedGame> { ran, never }, @"X:\missing.dll", ProxyType.Version,
                ct: TestContext.Current.CancellationToken);

            Assert.StartsWith("loaded ", ran.LoadObservation);    // folder present + fresh
            Assert.Equal("not observed", never.LoadObservation);  // no folder → honest unknown
        }
        finally
        {
            Directory.Delete(appData, recursive: true);
        }
    }

    private static string MakeTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(),
            "UE5DumpGenTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static DriveDescriptor Drive(char letter, int? disk) =>
        new() { Root = $"{letter}:\\", Letter = letter, PhysicalDiskNumber = disk };

    // ---- IsHardSkipDir ----

    [Theory]
    [InlineData("Windows", true)]
    [InlineData("$Recycle.Bin", true)]
    [InlineData("steamapps", true)]
    [InlineData("STEAMAPPS", true)]      // case-insensitive
    [InlineData("node_modules", true)]
    [InlineData("MyCoolGame", false)]
    public void IsHardSkipDir_MatchesSystemAndSteamNames(string name, bool expected)
    {
        Assert.Equal(expected, ProxyDeployService.IsHardSkipDir(name));
    }

    // ---- IsExcludedBySteam ----

    [Fact]
    public void IsExcludedBySteam_UnderRoot_IsExcluded()
    {
        var roots = new[] { ProxyDeployService.NormalizeDir(@"D:\SteamLibrary") };
        Assert.True(ProxyDeployService.IsExcludedBySteam(@"D:\SteamLibrary\steamapps\common\Foo", roots));
    }

    [Fact]
    public void IsExcludedBySteam_TheRootItself_IsExcluded()
    {
        var roots = new[] { ProxyDeployService.NormalizeDir(@"D:\SteamLibrary") };
        Assert.True(ProxyDeployService.IsExcludedBySteam(@"D:\SteamLibrary", roots));
    }

    [Fact]
    public void IsExcludedBySteam_SiblingWithSharedPrefix_IsNotExcluded()
    {
        // 'D:\SteamLibrary' must NOT swallow 'D:\SteamLibraryBackup'.
        var roots = new[] { ProxyDeployService.NormalizeDir(@"D:\SteamLibrary") };
        Assert.False(ProxyDeployService.IsExcludedBySteam(@"D:\SteamLibraryBackup\Game", roots));
    }

    [Fact]
    public void IsExcludedBySteam_NoRoots_IsNotExcluded()
    {
        Assert.False(ProxyDeployService.IsExcludedBySteam(@"D:\Games\Foo", Array.Empty<string>()));
    }

    // ---- LooksLikeUeGameRoot ----

    [Fact]
    public void LooksLikeUeGameRoot_SiblingEngineTree_IsTrue()
    {
        string root = MakeTempDir();
        try
        {
            // <root>\Engine\Binaries\Win64 + <root>\Proj\Binaries\Win64 (tier 1)
            MakeEmptyFile(Path.Combine(root, "Engine", "Binaries", "Win64", "CrashReportClient.exe"));
            MakeEmptyFile(Path.Combine(root, "Proj", "Binaries", "Win64", "Proj-Win64-Shipping.exe"));
            Assert.True(ProxyDeployService.LooksLikeUeGameRoot(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void LooksLikeUeGameRoot_ContentPaks_IsTrue()
    {
        string root = MakeTempDir();
        try
        {
            // <root>\Proj\Content\Paks\x.pak (tier 2)
            MakeEmptyFile(Path.Combine(root, "Proj", "Content", "Paks", "pakchunk0.pak"));
            Assert.True(ProxyDeployService.LooksLikeUeGameRoot(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void LooksLikeUeGameRoot_PlainFolder_IsFalse()
    {
        string root = MakeTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Docs"));
            Directory.CreateDirectory(Path.Combine(root, "Images"));
            Assert.False(ProxyDeployService.LooksLikeUeGameRoot(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // ---- GroupDrivesByPhysicalDisk ----

    [Fact]
    public void GroupDrives_SamePhysicalDisk_OneGroup()
    {
        var groups = ProxyDeployService.GroupDrivesByPhysicalDisk(
            new[] { Drive('C', 0), Drive('D', 0) });
        var g = Assert.Single(groups);
        Assert.Equal(2, g.Count);
    }

    [Fact]
    public void GroupDrives_DifferentDisks_TwoGroups()
    {
        var groups = ProxyDeployService.GroupDrivesByPhysicalDisk(
            new[] { Drive('C', 0), Drive('E', 1) });
        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Single(g));
    }

    [Fact]
    public void GroupDrives_UnknownDisk_GetsOwnGroup()
    {
        // Two unknown-disk drives must NOT be serialized together — each its own group.
        var groups = ProxyDeployService.GroupDrivesByPhysicalDisk(
            new[] { Drive('C', 0), Drive('D', 0), Drive('X', null), Drive('Y', null) });
        // one group of {C,D} + two singleton unknown groups = 3 groups
        Assert.Equal(3, groups.Count);
        Assert.Contains(groups, g => g.Count == 2);
        Assert.Equal(2, groups.Count(g => g.Count == 1));
    }

    // ---- FindUeGamesOnDrivesAsync — Steam exclusion end-to-end ----

    [Fact]
    public async Task FindUeGamesOnDrives_SkipsSteamAppsSubtree()
    {
        string root = MakeTempDir();
        try
        {
            // A non-Steam game under <root>\Games\CoolGame (monolithic).
            string cool = Path.Combine(root, "Games", "CoolGame");
            MakeEmptyFile(Path.Combine(cool, "Engine", "Binaries", "Win64", "CrashReportClient.exe"));
            MakeEmptyFile(Path.Combine(cool, "CoolGame", "Binaries", "Win64", "CoolGame-Win64-Shipping.exe"));

            // A game inside a steamapps tree — must be SKIPPED by the generic walk.
            string steam = Path.Combine(root, "steamapps", "common", "SteamGame");
            MakeEmptyFile(Path.Combine(steam, "Engine", "Binaries", "Win64", "CrashReportClient.exe"));
            MakeEmptyFile(Path.Combine(steam, "SteamGame", "Binaries", "Win64", "SteamGame-Win64-Shipping.exe"));

            var svc = new ProxyDeployService(new NoopLog(), new NoopPlatform());
            var drive = new DriveDescriptor { Root = root, Letter = 'X', PhysicalDiskNumber = 0 };
            var found = await svc.FindUeGamesOnDrivesAsync(
                new[] { drive }, progress: null, ct: TestContext.Current.CancellationToken);

            Assert.Contains(found, g => g.Name == "CoolGame");
            Assert.DoesNotContain(found, g => g.Name == "SteamGame");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // ── Post-inject connect retry ────────────────────────────────────────────
    // The injected DLL scans BEFORE opening its pipe, so a single immediate connect
    // cannot succeed. These pin the two halves that matter: it keeps trying, and it
    // stops the moment the probe says connected (rather than burning the window).

    [Fact]
    public void PostInjectConnectWindow_IsLongEnoughForAFullScan()
    {
        // Measured on a stock UE 5.4 Development package: pipe appeared 8.8 s after
        // injection (1 s auto-start delay + 7.8 s AOB scan). A window that does not
        // clear that with margin re-creates the bug it was added for.
        Assert.True(ProxyDeployViewModel.PostInjectConnectWindow >= TimeSpan.FromSeconds(30),
            "window must comfortably exceed a full AOB scan");
    }

    [Fact]
    public void RetryDelay_IsShorterThanTheWindow()
    {
        Assert.True(ProxyDeployViewModel.PostInjectRetryDelay < ProxyDeployViewModel.PostInjectConnectWindow);
        Assert.True(ProxyDeployViewModel.PostInjectRetryDelay > TimeSpan.Zero,
            "a zero delay would spin the connect attempt as fast as the CPU allows");
    }

    // ── Post-operation refresh must not erase the operation's own result ──
    //
    // A refresh run as the TAIL of a deploy/undeploy recomputes status purely from disk.
    // For a game the deploy just FAILED on, the disk says "file absent", which classifies
    // honestly as (NotDeployed, null) — overwriting the failure status AND its reason, so
    // the row read "NotDeployed" with a blank Error beside a banner saying "1 failed".
    // The reason survived only in the log. These pin the preserve-set that fixes it.

    [Fact]
    public void ShouldApplyRefresh_NoPreserveSet_AppliesEverything()
    {
        // A standalone Refresh owns nothing and must recompute every row.
        Assert.True(ProxyDeployService.ShouldApplyRefresh(@"D:\Games\Foo\Binaries\Win64", null));
    }

    [Fact]
    public void ShouldApplyRefresh_PreservedGame_IsSkipped()
    {
        var preserve = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"D:\Games\Foo\Binaries\Win64",
        };
        Assert.False(ProxyDeployService.ShouldApplyRefresh(@"D:\Games\Foo\Binaries\Win64", preserve));
    }

    [Fact]
    public void ShouldApplyRefresh_OtherGames_StillRefreshed()
    {
        // Preserving the failed game must not freeze the rest of the grid.
        var preserve = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"D:\Games\Foo\Binaries\Win64",
        };
        Assert.True(ProxyDeployService.ShouldApplyRefresh(@"D:\Games\Bar\Binaries\Win64", preserve));
    }

    [Fact]
    public void ShouldApplyRefresh_PathComparison_IsCaseInsensitive()
    {
        // BinariesDir is a Windows path: it can differ in case between the scan that filled
        // the grid and the set built during the operation. A case-sensitive compare would
        // silently stop preserving and put the erasure bug straight back.
        var preserve = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"D:\Games\Foo\Binaries\Win64",
        };
        Assert.False(ProxyDeployService.ShouldApplyRefresh(@"d:\games\foo\binaries\win64", preserve));
    }

    // ────────────────────────────────────────────────────────────────
    // Reading a log the app itself has open (measured 2026-08-12)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The leftover scan mines our own <c>view-*.log</c> for "Deployed … : &lt;path&gt;" lines. The
    /// CURRENT run's <c>view-0.log</c> is held open by our own logger, and <c>File.ReadLines</c> —
    /// which opens with <c>FileShare.Read</c> — cannot open a file a writer already has. The caller's
    /// per-file <c>catch</c> swallowed the exception, so that whole file contributed zero candidates:
    /// anything deployed in the current session was invisible to "Find leftovers" until a restart
    /// rotated the log. Measured against a real deploy, then fixed with a shared-read open.
    ///
    /// <para>This test touches the filesystem on purpose. The bug IS the sharing mode, and a
    /// fake/in-memory file has no sharing mode to get wrong.</para>
    /// </summary>
    [Fact]
    public void ReadLinesShared_ReadsAFileThatIsStillOpenForWriting()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ue5cedumper-share-{Guid.NewGuid():N}.log");
        try
        {
            // Open it the way a logger does: we write, others may only read.
            using (var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var sw = new StreamWriter(writer))
            {
                sw.WriteLine("[INFO] Deployed version.dll to Light Maze: T:\\Light Maze\\LM\\Binaries\\Win64\\version.dll");
                sw.Flush();

                // NEGATIVE CONTROL — without it this test would pass against a file nothing holds
                // open, i.e. it would assert nothing. This is the exact call that was shipped.
                Assert.ThrowsAny<IOException>(() => File.ReadLines(path).ToList());

                var lines = ProxyDeployService.ReadLinesShared(path).ToList();
                Assert.Single(lines);
                Assert.Contains("Light Maze", lines[0], StringComparison.Ordinal);
            }
        }
        finally
        {
            try { File.Delete(path); } catch { /* temp file */ }
        }
    }

    /// <summary>The candidate directory really is recovered from such a line — the two halves of the
    /// defect joined up, so a future change to either the parser or the reader is caught.</summary>
    [Fact]
    public void ReadLinesShared_FeedsTheDeployLineParser()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ue5cedumper-share-{Guid.NewGuid():N}.log");
        try
        {
            using (var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var sw = new StreamWriter(writer))
            {
                sw.WriteLine("[2026-08-12 11:21:58.947] [INFO] Deployed version.dll to Light Maze: " +
                             @"T:\Light Maze\LightMaze\Binaries\Win64\version.dll");
                sw.Flush();

                var dirs = ProxyOrphanScanner.CandidateDirsFrom(
                    ProxyDeployService.ReadLinesShared(path), isDllLog: false);

                Assert.Equal(new[] { @"T:\Light Maze\LightMaze\Binaries\Win64" }, dirs);
            }
        }
        finally
        {
            try { File.Delete(path); } catch { /* temp file */ }
        }
    }
}
