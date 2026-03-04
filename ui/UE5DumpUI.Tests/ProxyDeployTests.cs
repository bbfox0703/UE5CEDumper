using UE5DumpUI.Models;
using UE5DumpUI.Services;
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

        Assert.Equal(6, values.Length);
        Assert.Contains(ProxyDeployStatus.NotDeployed, values);
        Assert.Contains(ProxyDeployStatus.DeployedCurrent, values);
        Assert.Contains(ProxyDeployStatus.DeployedOutdated, values);
        Assert.Contains(ProxyDeployStatus.OtherProxy, values);
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
        Assert.Equal("UE5CEDumper", Constants.ProxyProductName);
        Assert.False(string.IsNullOrEmpty(Constants.SteamRegistryPath));
        Assert.False(string.IsNullOrEmpty(Constants.SteamRegistryKey));
        Assert.False(string.IsNullOrEmpty(Constants.SteamDefaultPath));
        Assert.False(string.IsNullOrEmpty(Constants.SteamLibraryFoldersVdf));
        Assert.False(string.IsNullOrEmpty(Constants.SteamAppsCommon));
    }
}
