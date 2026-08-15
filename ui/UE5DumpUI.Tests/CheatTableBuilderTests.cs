using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Coverage for the multi-row .CT batch generator (build 758+).
/// Three layers:
///
/// 1. <see cref="CheatTableBuilder"/> pure-XML output. Locks the
///    structural shape CE expects (CheatTable root, CheatEntries
///    nesting, GroupHeader markers, AssemblerScript bodies) and the
///    XML escaping rules so a description with &lt; / &amp; / quote
///    can't break a CT load.
///
/// 2. <see cref="InterestingPropertiesViewModel.BuildRowsFromSelection"/>
///    row mapping — verifies the StructProperty / ArrayProperty skip
///    + the defining-class targetClass choice. Static helper kept
///    addressable so we don't have to stand up the full VM lifecycle.
///
/// 3. <see cref="InterestingFunctionsViewModel.BuildRowsFromSelection"/>
///    row mapping — verifies the parmsSize / numParms shape and the
///    "edit baked PARAMS" hint in the description for parameterised
///    functions.
///
/// Wire-shape contract tests (RequestSaveCheatTable event payload
/// shape) live alongside the per-VM tests so future event-renames
/// surface as build failures rather than runtime regressions.
/// </summary>
public class CheatTableBuilderTests
{
    // ------------------------------------------------------------------
    // Helpers — synthetic rows so each test stays self-contained.
    // ------------------------------------------------------------------

    private static CtPropertyRow MakeFreezeRow(
        string category, string className, string propName,
        string ueType = "FloatProperty", int offset = 0x40, string value = "9999.0",
        int propSize = 4)
        => new()
        {
            Category    = category,
            Description = $"{className}::{propName}",
            FreezeParams = new FreezeScriptParams
            {
                ClassName      = className,
                PropertyName   = propName,
                PropertyOffset = offset,
                UeTypeName     = ueType,
                PropertySize   = propSize,
                ValueLiteral   = value,
            },
        };

    private static CtFunctionRow MakeFuncRow(
        string category, string className, string funcName,
        int parmsSize = 0)
        => new()
        {
            Category    = category,
            Description = $"{className}::{funcName}()",
            ClassName   = className,
            FuncName    = funcName,
            ParmsSize   = parmsSize,
            BakedValues = Array.Empty<BakedParamValue>(),
        };

    // ------------------------------------------------------------------
    // Builder layer — structural shape
    // ------------------------------------------------------------------

    [Fact]
    public void Build_ThreeProperties_ProducesValidShell()
    {
        var rows = new List<CheatTableRow>
        {
            MakeFreezeRow("Stats",  "BP_Player_C", "Health"),
            MakeFreezeRow("Stats",  "BP_Player_C", "Mana"),
            MakeFreezeRow("Combat", "BP_Player_C", "Damage"),
        };

        string ct = CheatTableBuilder.Build("title", rows);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", ct);
        Assert.Contains("<CheatTable CheatEngineTableVersion=\"46\">", ct);
        Assert.Contains("<UserdefinedSymbols/>", ct);
        Assert.EndsWith("</CheatTable>\r\n", ct);  // AppendLine on Windows
            // Cross-platform: trim CR before comparing so the line-ending
            // policy doesn't trip Linux test runs.
    }

    [Fact]
    public void Build_GroupsByCategoryAlphabetically()
    {
        var rows = new List<CheatTableRow>
        {
            MakeFreezeRow("Stats",   "C", "P1"),
            MakeFreezeRow("Combat",  "C", "P2"),
            MakeFreezeRow("Stats",   "C", "P3"),
        };
        string ct = CheatTableBuilder.Build("title", rows);

        // "Combat" must appear before "Stats" (alphabetical).
        int combatIdx = ct.IndexOf("--- Combat", StringComparison.Ordinal);
        int statsIdx  = ct.IndexOf("--- Stats",  StringComparison.Ordinal);
        Assert.True(combatIdx > 0, "Combat header missing");
        Assert.True(statsIdx > 0,  "Stats header missing");
        Assert.True(combatIdx < statsIdx,
            "Categories should be alphabetical (Combat < Stats)");
    }

    [Fact]
    public void Build_UncategorisedBucketComesLast()
    {
        var rows = new List<CheatTableRow>
        {
            MakeFreezeRow("",       "C", "P1"),
            MakeFreezeRow("Stats",  "C", "P2"),
        };
        string ct = CheatTableBuilder.Build("title", rows);

        int statsIdx = ct.IndexOf("--- Stats",        StringComparison.Ordinal);
        int uncatIdx = ct.IndexOf("--- Uncategorised",StringComparison.Ordinal);
        Assert.True(statsIdx > 0);
        Assert.True(uncatIdx > 0);
        Assert.True(statsIdx < uncatIdx,
            "Uncategorised bucket must trail the labelled categories.");
    }

    [Fact]
    public void Build_PreservesInputOrderWithinCategory()
    {
        var rows = new List<CheatTableRow>
        {
            MakeFreezeRow("Stats", "C", "ZLast",  ueType: "FloatProperty", offset: 0x10),
            MakeFreezeRow("Stats", "C", "AFirst", ueType: "FloatProperty", offset: 0x20),
        };
        string ct = CheatTableBuilder.Build("title", rows);

        // The first row inserted (ZLast) must appear first in the
        // output, NOT alphabetical — we sort buckets, not rows.
        int zIdx = ct.IndexOf("ZLast", StringComparison.Ordinal);
        int aIdx = ct.IndexOf("AFirst", StringComparison.Ordinal);
        Assert.True(zIdx > 0 && aIdx > 0);
        Assert.True(zIdx < aIdx,
            "Within a category, rows must keep input order (caller pre-sorts by relevance).");
    }

    [Fact]
    public void Build_TenMixedRows_HasOneEntryPerRow()
    {
        var rows = new List<CheatTableRow>();
        for (int i = 0; i < 5; i++)
            rows.Add(MakeFreezeRow("Stats",  "C", $"Prop{i}"));
        for (int i = 0; i < 3; i++)
            rows.Add(MakeFreezeRow("Combat", "C", $"DmgProp{i}"));
        for (int i = 0; i < 2; i++)
            rows.Add(MakeFuncRow("Inventory", "C", $"AddMoney{i}"));

        string ct = CheatTableBuilder.Build("title", rows);
        int entryCount = CountOccurrences(ct, "<VariableType>Auto Assembler Script</VariableType>");
        // 10 caller rows + the game-thread reminder Build injects into every table. It is
        // counted explicitly rather than folded into the number, so that if it ever stops
        // being emitted this reads as a missing REMINDER and not as a lost row.
        Assert.Equal(10 + 1, entryCount);
        Assert.Contains(CeInjectScriptGenerator.ReminderDescription, ct, StringComparison.Ordinal);

        // One category header per caller category (3 of them). The reminder's own bucket is
        // not asserted by header text -- its presence is already proven above, and pinning
        // the header format here would just duplicate what the header tests already cover.
        Assert.Contains("--- Combat (3 rows) ---",    ct);
        Assert.Contains("--- Inventory (2 rows) ---", ct);
        Assert.Contains("--- Stats (5 rows) ---",     ct);
    }

    [Fact]
    public void Build_MixedRowTypes_EmitsCorrectScripts()
    {
        var rows = new List<CheatTableRow>
        {
            MakeFreezeRow("Stats", "BP_Player_C", "Health"),
            MakeFuncRow ("Combat", "BP_Player_C", "TakeDamage"),
        };
        string ct = CheatTableBuilder.Build("title", rows);

        // FreezeScriptGenerator emits this header; presence proves the
        // property row went through the freeze path.
        Assert.Contains("Property Freeze: BP_Player_C::Health", ct);
        // BakedScriptGenerator emits this header.
        Assert.Contains("UFunction Invoker (baked args)", ct);
        Assert.Contains("BP_Player_C::TakeDamage", ct);
    }

    [Fact]
    public void Build_IdsAreUniqueAndSequential()
    {
        var rows = new List<CheatTableRow>
        {
            MakeFreezeRow("Stats", "C", "P1"),
            MakeFreezeRow("Stats", "C", "P2"),
        };
        string ct = CheatTableBuilder.Build("title", rows);

        var ids = ExtractIds(ct);
        // 1 root + 1 category + 2 rows = 4, plus the reminder's own category + entry that
        // Build injects into every table = 6.
        Assert.Equal(6, ids.Count);
        // Unique.
        Assert.Equal(ids.Count, new HashSet<int>(ids).Count);
        // Sequential starting at BaseId.
        for (int i = 0; i < ids.Count; i++)
            Assert.Equal(CheatTableBuilder.BaseId + i, ids[i]);
    }

    [Fact]
    public void Build_EmptyRowsThrows()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => CheatTableBuilder.Build("title", Array.Empty<CheatTableRow>()));
        Assert.Contains("zero rows", ex.Message);
    }

    [Fact]
    public void Build_NullRowsThrows()
    {
        Assert.Throws<ArgumentNullException>(
            () => CheatTableBuilder.Build("title", null!));
    }

    [Fact]
    public void EscapeXml_HandlesAllFiveCanonicalEntities()
    {
        // The five canonical XML entities must each round-trip.
        Assert.Equal("&amp;", CheatTableBuilder.EscapeXml("&"));
        Assert.Equal("&lt;",  CheatTableBuilder.EscapeXml("<"));
        Assert.Equal("&gt;",  CheatTableBuilder.EscapeXml(">"));
        Assert.Equal("&quot;",CheatTableBuilder.EscapeXml("\""));
        Assert.Equal("&apos;",CheatTableBuilder.EscapeXml("'"));

        Assert.Equal("a &amp; b &lt; c &gt; d",
            CheatTableBuilder.EscapeXml("a & b < c > d"));
    }

    [Fact]
    public void Build_DescriptionWithSpecialChars_IsEscaped()
    {
        // Descriptions can contain UE template syntax (`<T>`) and
        // operators (`<`, `&&`) — escaping is what keeps the CT loadable.
        var rows = new List<CheatTableRow>
        {
            MakeFreezeRow("Stats", "TArray<int>", "Health & Stamina"),
        };
        string ct = CheatTableBuilder.Build("title", rows);

        // Raw chars must NOT appear inside descriptions.
        Assert.Contains("TArray&lt;int&gt;", ct);
        Assert.Contains("Health &amp; Stamina", ct);
    }

    [Fact]
    public void DefaultFileName_IncludesProcessAndTimestamp()
    {
        var now = new DateTime(2026, 5, 27, 16, 42, 33);
        string name = CheatTableBuilder.DefaultFileName("ES2-Win64-Shipping", now);
        Assert.Equal("ES2-Win64-Shipping-batch-20260527-164233.CT", name);
    }

    [Fact]
    public void DefaultFileName_FallsBackToUE5CEDumperOnEmpty()
    {
        var now = new DateTime(2026, 5, 27, 16, 42, 33);
        string name = CheatTableBuilder.DefaultFileName(null, now);
        Assert.Equal("UE5CEDumper-batch-20260527-164233.CT", name);
    }

    [Fact]
    public void SanitizeFileName_ReplacesIllegalCharsWithUnderscore()
    {
        // Path-invalid chars on Windows: <>:"/\|?*
        Assert.Equal("foo_bar", CheatTableBuilder.SanitizeFileName("foo/bar"));
        Assert.Equal("foo_bar", CheatTableBuilder.SanitizeFileName("foo*bar"));
        Assert.Equal("foo_bar", CheatTableBuilder.SanitizeFileName("foo bar")); // space too
        // Trailing dots/spaces stripped.
        Assert.Equal("name", CheatTableBuilder.SanitizeFileName("name."));
    }

    // ------------------------------------------------------------------
    // VM layer — Interesting Properties row mapping
    // ------------------------------------------------------------------

    [Fact]
    public void PropsBuildRows_SkipsStructAndArrayTypes()
    {
        // Only types FreezeScriptGenerator supports should survive.
        var selection = new List<ScoredPropertyRow>
        {
            MakeScoredProp("Health",     "FloatProperty",  PropertyCategory.Stats),
            MakeScoredProp("Inventory",  "ArrayProperty",  PropertyCategory.Resources),
            MakeScoredProp("Pos",        "StructProperty", PropertyCategory.Movement),
            MakeScoredProp("IsDead",     "BoolProperty",   PropertyCategory.Stats),
        };
        var (rows, skippedUnsupported, _) =
            InterestingPropertiesViewModel.BuildRowsFromSelection(selection);

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, skippedUnsupported);
        // The two survivors should be Health (Float) and IsDead (Bool).
        var names = rows.OfType<CtPropertyRow>()
                        .Select(r => r.FreezeParams.PropertyName).ToList();
        Assert.Contains("Health", names);
        Assert.Contains("IsDead", names);
    }

    [Fact]
    public void PropsBuildRows_UsesDefiningClassWhenPresent()
    {
        var row = MakeScoredProp("Health", "FloatProperty",
                                 PropertyCategory.Stats);
        row.Match.DefiningClassName = "ACharacter";  // declared on engine super
        row.Match.ClassName         = "BP_Player_C"; // user picked the BP subclass

        var (rows, _, _) = InterestingPropertiesViewModel
            .BuildRowsFromSelection(new[] { row });

        Assert.Single(rows);
        var freeze = ((CtPropertyRow)rows[0]).FreezeParams;
        Assert.Equal("ACharacter", freeze.ClassName);
        // Description still surfaces the user-picked subclass + the
        // "defined on" hint so the CT entry is self-documenting.
        Assert.Contains("BP_Player_C::Health", rows[0].Description);
        Assert.Contains("ACharacter", rows[0].Description);
    }

    [Theory]
    [InlineData("FloatProperty",  "9999.0")]
    [InlineData("DoubleProperty", "9999.0")]
    [InlineData("IntProperty",    "99999")]
    [InlineData("BoolProperty",   "true")]
    [InlineData("ByteProperty",   "255")]
    public void PropsBuildRows_DefaultFreezeLiteralPerType(
        string ueType, string expectedLiteral)
    {
        var row = MakeScoredProp("X", ueType, PropertyCategory.Stats);
        var (rows, _, _) = InterestingPropertiesViewModel
            .BuildRowsFromSelection(new[] { row });
        Assert.Single(rows);
        Assert.Equal(expectedLiteral,
            ((CtPropertyRow)rows[0]).FreezeParams.ValueLiteral);
    }

    // ------------------------------------------------------------------
    // Audit #5 Y15 — the batch CT path must carry the engine-reported width.
    //
    // BuildRowsFromSelection is the second of the two places a FreezeScriptParams
    // is built. Asserting the SCRIPT (not just the params) is deliberate: it fails
    // whether the width is dropped at the row forward, at the params, or at the
    // mapping — anywhere along the chain the user's bytes travel.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(1, "uint8")]
    [InlineData(2, "uint16")]
    [InlineData(4, "int32")]
    [InlineData(8, "int64")]
    public void PropsBuildRows_EnumWidthReachesTheGeneratedScript(int propSize, string expected)
    {
        var row = MakeScoredProp("Stance", "EnumProperty", PropertyCategory.Stats,
                                 propSize: propSize);

        var (rows, _, _) = InterestingPropertiesViewModel
            .BuildRowsFromSelection(new[] { row });

        Assert.Single(rows);
        var freeze = ((CtPropertyRow)rows[0]).FreezeParams;
        Assert.Equal(propSize, freeze.PropertySize);
        Assert.Contains($"valueType          = '{expected}',", rows[0].GenerateScript());
    }

    // ------------------------------------------------------------------
    // VM layer — Interesting Functions row mapping
    // ------------------------------------------------------------------

    [Fact]
    public void FuncsBuildRows_MapsEveryRowOneToOne()
    {
        var sel = new List<ScoredFunctionRow>
        {
            MakeScoredFunc("BP_Player_C", "Jump",       FunctionCategory.Movement, parms: 0),
            MakeScoredFunc("BP_Player_C", "AddMoney",   FunctionCategory.Inventory, parms: 1),
            MakeScoredFunc("BP_Player_C", "TakeDamage", FunctionCategory.Combat,    parms: 4),
        };
        var rows = InterestingFunctionsViewModel.BuildRowsFromSelection(sel);
        Assert.Equal(3, rows.Count);
        // No-arg description matches the "()" form.
        Assert.Contains("Jump()", rows[0].Description);
        // Param-having description carries the editing hint.
        Assert.Contains("edit baked PARAMS in CE",
                        rows[2].Description);
    }

    [Fact]
    public void FuncsBuildRows_DropsRowsWithEmptyName()
    {
        var sel = new List<ScoredFunctionRow>
        {
            MakeScoredFunc("BP_Player_C", "",      FunctionCategory.Other),
            MakeScoredFunc("",            "Jump",  FunctionCategory.Movement),
            MakeScoredFunc("BP_Player_C", "Jump",  FunctionCategory.Movement),
        };
        var rows = InterestingFunctionsViewModel.BuildRowsFromSelection(sel);
        Assert.Single(rows);
    }

    [Fact]
    public void FuncsBuildRows_BakedValuesAlwaysEmpty()
    {
        // The batch path doesn't ask the user to fill baked values per
        // row — empty list + helper zero-fill is the contract.
        var sel = new List<ScoredFunctionRow>
        {
            MakeScoredFunc("BP_Player_C", "AddMoney", FunctionCategory.Inventory, parms: 1),
        };
        var rows = InterestingFunctionsViewModel.BuildRowsFromSelection(sel);
        Assert.Single(rows);
        var f = (CtFunctionRow)rows[0];
        Assert.Empty(f.BakedValues);
    }

    // ------------------------------------------------------------------
    // Synthetic constructors for ScoredFunctionRow / ScoredPropertyRow.
    // Both models are required-init records; tests need full ctors.
    // ------------------------------------------------------------------

    private static ScoredPropertyRow MakeScoredProp(
        string propName, string propType, PropertyCategory cat,
        string className = "BP_Player_C", int offset = 0x40, int propSize = 4)
    {
        var match = new PropertySearchMatch
        {
            ClassName         = className,
            PropName          = propName,
            PropType          = propType,
            PropOffset        = offset,
            PropSize          = propSize,
            DefiningClassName = className,  // tests override per case
        };
        return new ScoredPropertyRow
        {
            Match             = match,
            FinalScore        = 5,
            Category          = cat,
            KeywordHits       = 1,
            ClassBonus        = 0,
            IsUnusualLocation = false,
        };
    }

    private static ScoredFunctionRow MakeScoredFunc(
        string className, string funcName, FunctionCategory cat,
        byte parms = 0, ushort parmsSize = 0)
    {
        var entry = new AllFunctionEntry
        {
            ClassName  = className,
            FuncName   = funcName,
            NumParms   = parms,
            ParmsSize  = parmsSize == 0 ? (ushort)(parms * 4) : parmsSize,
        };
        return new ScoredFunctionRow
        {
            Entry       = entry,
            FinalScore  = 5,
            Category    = cat,
            KeywordHits = 1,
            ClassBonus  = 0,
            FlagBonus   = 0,
        };
    }

    // ------------------------------------------------------------------
    // Test-local helpers
    // ------------------------------------------------------------------

    private static int CountOccurrences(string haystack, string needle)
    {
        int n = 0;
        int idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            n++;
            idx += needle.Length;
        }
        return n;
    }

    private static List<int> ExtractIds(string ct)
    {
        var ids = new List<int>();
        int idx = 0;
        const string open = "<ID>";
        const string close = "</ID>";
        while ((idx = ct.IndexOf(open, idx, StringComparison.Ordinal)) >= 0)
        {
            idx += open.Length;
            int end = ct.IndexOf(close, idx, StringComparison.Ordinal);
            if (end < 0) break;
            if (int.TryParse(ct.AsSpan(idx, end - idx), out int id))
                ids.Add(id);
            idx = end + close.Length;
        }
        return ids;
    }

    /// <summary>
    /// A saved .CT must carry the same game-thread reminder a PUSHED table gets. It is the
    /// only place the reader is told that mailbox commands are dispatched on the game
    /// thread -- so a paused or alt-tabbed game times every generated script out -- and
    /// that a timed-out command is not cancelled but lands whenever the game next ticks.
    /// A .CT handed to someone else is exactly where that context would otherwise be lost.
    /// </summary>
    [Fact]
    public void Build_AlwaysCarriesTheGameThreadReminder_Once_AndStaysWellFormed()
    {
        var rows = new List<CheatTableRow> { MakeFreezeRow("Stats", "C", "P1") };

        string ct = CheatTableBuilder.Build("title", rows);

        Assert.Equal(1, CountOccurrences(ct, CeInjectScriptGenerator.ReminderDescription));
        // The body, not just the description -- an entry with the right label and no script
        // would look correct in the grid and do nothing when ticked.
        Assert.Contains("GAME THREAD", ct, StringComparison.Ordinal);
        Assert.Contains("NOT cancelled", ct, StringComparison.Ordinal);

        // CE refuses a malformed table outright, and the reminder body is the only script
        // here that is pure prose -- the likeliest thing to carry an unescaped character.
        var doc = System.Xml.Linq.XDocument.Parse(ct);
        Assert.NotNull(doc.Root);
    }

    /// <summary>A caller that already supplies the reminder must not get two.</summary>
    [Fact]
    public void Build_DoesNotDuplicateAReminderTheCallerAlreadySupplied()
    {
        var rows = new List<CheatTableRow>
        {
            new CtScriptRow
            {
                Category = CeInjectScriptGenerator.RecordGroup,
                Description = CeInjectScriptGenerator.ReminderDescription,
                Script = CeInjectScriptGenerator.GenerateReminder(),
            },
            MakeFreezeRow("Stats", "C", "P1"),
        };

        string ct = CheatTableBuilder.Build("title", rows);

        Assert.Equal(1, CountOccurrences(ct, CeInjectScriptGenerator.ReminderDescription));
    }

}
