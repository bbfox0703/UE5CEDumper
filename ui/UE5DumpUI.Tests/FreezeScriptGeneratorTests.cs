using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Tests for <see cref="FreezeScriptGenerator"/>.
///
/// Exercise four axes:
/// 1. Type mapping (UE -> helper) covers every numeric + bool case.
/// 2. Lua escaping survives single-quote / backslash / newline.
/// 3. The rendered script includes (a) the helper-file lookup, (b) a
///    CFG block with className/offset/type/value embedded literally,
///    (c) start() in ENABLE and stop() in DISABLE.
/// 4. Embedded helper resource is reachable from the assembly manifest
///    (catches packaging drift).
/// </summary>
public class FreezeScriptGeneratorTests
{
    [Theory]
    [InlineData("BoolProperty",    "bool")]
    [InlineData("ByteProperty",    "uint8")]
    [InlineData("Int8Property",    "int8")]
    [InlineData("Int16Property",   "int16")]
    [InlineData("UInt16Property",  "uint16")]
    [InlineData("IntProperty",     "int32")]
    [InlineData("UInt32Property",  "uint32")]
    [InlineData("EnumProperty",    "int32")]
    [InlineData("Int64Property",   "int64")]
    [InlineData("UInt64Property",  "uint64")]
    [InlineData("FloatProperty",   "float")]
    [InlineData("DoubleProperty",  "double")]
    public void MapToHelperType_KnownTypes_MapsCorrectly(string ue, string expected)
    {
        Assert.Equal(expected, FreezeScriptGenerator.MapToHelperType(ue));
        Assert.True(FreezeScriptGenerator.IsTypeSupported(ue));
    }

    [Theory]
    [InlineData("StructProperty")]
    [InlineData("ObjectProperty")]
    [InlineData("ArrayProperty")]
    [InlineData("StrProperty")]
    [InlineData("NameProperty")]
    [InlineData("UnknownProperty")]
    public void MapToHelperType_UnsupportedTypes_ReturnsEmpty(string ue)
    {
        Assert.Equal("", FreezeScriptGenerator.MapToHelperType(ue));
        Assert.False(FreezeScriptGenerator.IsTypeSupported(ue));
    }

    // ==================================================================
    // Audit #5 Y15 — an EnumProperty's width comes from the ENGINE.
    //
    // The mapping used to answer "int32" for every enum, so freezing the
    // dominant UE shape (`enum class E : uint8`) emitted a 4-byte
    // writeInteger over a 1-byte field — destroying the three bytes after
    // it, 20 times a second, for as long as the freeze was active.
    // ==================================================================

    [Theory]
    [InlineData(1, "uint8")]
    [InlineData(2, "uint16")]
    [InlineData(4, "int32")]
    [InlineData(8, "int64")]
    [InlineData(0, "int32")]   // unreported (older DLL) → legacy default
    [InlineData(3, "int32")]   // nonsense width → legacy default, never a partial write
    [InlineData(-1, "int32")]
    public void HelperTypeForSize_PicksWriterByWidth(int size, string expected)
    {
        Assert.Equal(expected, FreezeScriptGenerator.HelperTypeForSize(size));
    }

    [Theory]
    [InlineData(1, "uint8")]
    [InlineData(2, "uint16")]
    [InlineData(4, "int32")]
    [InlineData(8, "int64")]
    [InlineData(0, "int32")]
    public void MapToHelperType_EnumProperty_FollowsReportedSize(int size, string expected)
    {
        Assert.Equal(expected, FreezeScriptGenerator.MapToHelperType("EnumProperty", size));
        // Supported at every width — the gate is a property of the TYPE.
        Assert.True(FreezeScriptGenerator.IsTypeSupported("EnumProperty"));
    }

    [Theory]
    // Every type whose width its NAME already fixes must ignore the size argument —
    // a bogus/missing size from the wire must not turn a float into a byte. Only
    // EnumProperty is width-ambiguous, so only EnumProperty consults it.
    [InlineData("BoolProperty",   "bool")]
    [InlineData("ByteProperty",   "uint8")]
    [InlineData("Int8Property",   "int8")]
    [InlineData("Int16Property",  "int16")]
    [InlineData("IntProperty",    "int32")]
    [InlineData("Int64Property",  "int64")]
    [InlineData("UInt64Property", "uint64")]
    [InlineData("FloatProperty",  "float")]
    [InlineData("DoubleProperty", "double")]
    public void MapToHelperType_NonEnumTypes_IgnoreReportedSize(string ue, string expected)
    {
        foreach (var size in new[] { 0, 1, 2, 4, 8, 99 })
            Assert.Equal(expected, FreezeScriptGenerator.MapToHelperType(ue, size));
    }

    [Fact]
    public void MapToHelperType_SizelessOverload_MatchesSizeZero()
    {
        // The 1-arg form is what IsTypeSupported and the row gates call. It must be
        // exactly the legacy behaviour, not a second table that can drift.
        foreach (var ue in new[]
                 {
                     "BoolProperty", "ByteProperty", "Int8Property", "Int16Property",
                     "UInt16Property", "IntProperty", "UInt32Property", "EnumProperty",
                     "Int64Property", "UInt64Property", "FloatProperty", "DoubleProperty",
                     "StructProperty", "NopeProperty",
                 })
        {
            Assert.Equal(FreezeScriptGenerator.MapToHelperType(ue, 0),
                         FreezeScriptGenerator.MapToHelperType(ue));
        }
    }

    [Fact]
    public void Generate_OneByteEnum_EmitsUint8NotInt32()
    {
        var p = new FreezeScriptParams
        {
            ClassName      = "ABP_Player_C",
            PropertyName   = "CurrentStance",
            PropertyOffset = 0x2C1,
            UeTypeName     = "EnumProperty",
            PropertySize   = 1,
            BoolFieldMask  = 0,
            ValueLiteral   = "3",
        };

        var script = FreezeScriptGenerator.Generate(p);

        Assert.Contains("valueType          = 'uint8',", script);
        // The defect verbatim: a 4-byte writer aimed at a 1-byte field.
        Assert.DoesNotContain("valueType          = 'int32',", script);
    }

    [Theory]
    [InlineData(1, "uint8")]
    [InlineData(2, "uint16")]
    [InlineData(4, "int32")]
    [InlineData(8, "int64")]
    public void Generate_Enum_CfgValueTypeAlwaysMatchesTheMapping(int size, string expected)
    {
        var p = new FreezeScriptParams
        {
            ClassName      = "AFoo",
            PropertyName   = "Bar",
            PropertyOffset = 0x10,
            UeTypeName     = "EnumProperty",
            PropertySize   = size,
            BoolFieldMask  = 0,
            ValueLiteral   = "0",
        };

        var script = FreezeScriptGenerator.Generate(p);

        Assert.Equal(expected, FreezeScriptGenerator.MapToHelperType("EnumProperty", size));
        Assert.Contains($"valueType          = '{expected}',", script);
        // The debug line names the same type — the CFG and the log must not disagree
        // about what is being written (audit #4's root cause: report and reality
        // computed by different code paths).
        Assert.Contains($"({expected}@0x10)", script);
    }

    [Theory]
    [InlineData("plain",          "plain")]
    [InlineData(@"back\slash",    @"back\\slash")]
    [InlineData("with'quote",     @"with\'quote")]
    [InlineData("line\nbreak",    @"line\nbreak")]
    [InlineData("carriage\rret",  @"carriage\rret")]
    [InlineData("tab\there",      @"tab\there")]
    public void EscapeLua_HandlesSpecialChars(string input, string expected)
    {
        Assert.Equal(expected, FreezeScriptGenerator.EscapeLua(input));
    }

    [Fact]
    public void Generate_FloatProperty_ProducesExpectedSections()
    {
        var p = new FreezeScriptParams
        {
            ClassName      = "BP_Teammate_C",
            PropertyName   = "CurrentHealth",
            PropertyOffset = 0x4F8,
            UeTypeName     = "FloatProperty",
            PropertySize   = 4,
            BoolFieldMask  = 0,
            ValueLiteral   = "9999.0",
        };

        var script = FreezeScriptGenerator.Generate(p);

        // [ENABLE] / [DISABLE] block structure
        Assert.Contains("[ENABLE]", script);
        Assert.Contains("[DISABLE]", script);

        // Helper file lookup (no filesystem fallback)
        Assert.Contains("findTableFile('ue5_freeze_helper.lua')", script);

        // CFG block fields literal
        Assert.Contains("className          = 'BP_Teammate_C',", script);
        Assert.Contains("propOffset         = 0x4F8,", script);
        Assert.Contains("valueType          = 'float',", script);
        Assert.Contains("value              = 9999.0,", script);

        // Start in ENABLE, stop in DISABLE -- handles tracked in a shared
        // keyed table so multiple Freeze scripts don't clobber each other.
        var enableIdx = script.IndexOf("[ENABLE]");
        var disableIdx = script.IndexOf("[DISABLE]");
        Assert.True(enableIdx < disableIdx);
        var enableBlock = script.Substring(enableIdx, disableIdx - enableIdx);
        var disableBlock = script.Substring(disableIdx);
        Assert.Contains("handleOrErr.start", enableBlock);
        Assert.Contains("h.stop", disableBlock);
        // Per-script key includes the class + prop + offset
        Assert.Contains("BP_Teammate_C::CurrentHealth@0x4F8", script);
        // Shared global table -- avoids one script's [DISABLE] killing another's handle
        Assert.Contains("_ue5_freeze_handles", script);
    }

    [Fact]
    public void Generate_BoolProperty_EmitsBoolHelperType()
    {
        var p = new FreezeScriptParams
        {
            ClassName      = "PlayerCharacter",
            PropertyName   = "bCanBeDamaged",
            PropertyOffset = 0x328,
            UeTypeName     = "BoolProperty",
            PropertySize   = 1,
            BoolFieldMask  = 0,   // native bool: owns its whole byte
            ValueLiteral   = "false",
        };

        var script = FreezeScriptGenerator.Generate(p);

        Assert.Contains("valueType          = 'bool',", script);
        Assert.Contains("value              = false,", script);
        // A native bool owns its whole byte, so NO mask must be emitted —
        // emitting one would make the helper touch a single bit of a byte
        // that is entirely this property's. (audit #5 AA1)
        Assert.DoesNotContain("boolMask", script);
    }

    // ── audit #5 AA1: packed bitfield bools ──────────────────────────────
    //
    // UE packs `uint8 bFoo:1` bools eight to a byte. The freeze pipeline used
    // to drop the FBoolProperty FieldMask, so the helper stamped the whole
    // byte ~16x/sec: up to 7 sibling bools clobbered, and — whenever the mask
    // was not 0x01 — the intended bool never set at all (writing 1 sets bit 0),
    // so the feature silently no-opped WHILE corrupting its neighbours.

    [Theory]
    [InlineData(0x01)]
    [InlineData(0x02)]
    [InlineData(0x04)]
    [InlineData(0x08)]
    [InlineData(0x10)]
    [InlineData(0x20)]
    [InlineData(0x40)]
    [InlineData(0x80)]
    public void Generate_PackedBoolMask_EmitsBoolMaskIntoCfg(int mask)
    {
        var p = new FreezeScriptParams
        {
            ClassName      = "PlayerCharacter",
            PropertyName   = "bIsInvulnerable",
            PropertyOffset = 0x328,
            UeTypeName     = "BoolProperty",
            PropertySize   = 1,
            BoolFieldMask  = mask,
            ValueLiteral   = "true",
        };

        var script = FreezeScriptGenerator.Generate(p);

        Assert.Contains($"boolMask           = 0x{mask:X2},", script);
    }

    [Theory]
    // 0 = the DLL reported no mask (native bool, or a pre-AA1 DLL).
    [InlineData(0)]
    // 0xFF = UE's OWN native-bool marker: SetBoolSize writes FieldMask = 255
    // when bIsNativeBool. Treating it as a bit mask would write bit 0..7 of a
    // byte the property already owns outright.
    [InlineData(0xFF)]
    // Multi-bit values are not a shape UE produces for a single bool; ORing
    // them in would set bits belonging to nobody.
    [InlineData(0x03)]
    [InlineData(0x05)]
    [InlineData(0x81)]
    // Defensive: a negative can only arrive from a corrupt wire value.
    [InlineData(-1)]
    public void Generate_NonPackedBoolMask_OmitsBoolMask(int mask)
    {
        var p = new FreezeScriptParams
        {
            ClassName      = "PlayerCharacter",
            PropertyName   = "bCanBeDamaged",
            PropertyOffset = 0x328,
            UeTypeName     = "BoolProperty",
            PropertySize   = 1,
            BoolFieldMask  = mask,
            ValueLiteral   = "false",
        };

        var script = FreezeScriptGenerator.Generate(p);

        Assert.DoesNotContain("boolMask", script);
    }

    [Fact]
    public void Generate_NonBoolType_NeverEmitsBoolMask()
    {
        // The mask is meaningless off a BoolProperty. A row carrying a stale
        // one must not turn an int freeze into a bit write — the CFG guard is
        // on the resolved helper type, not just on the mask value.
        var p = new FreezeScriptParams
        {
            ClassName      = "Foo",
            PropertyName   = "Count",
            PropertyOffset = 0x10,
            UeTypeName     = "IntProperty",
            PropertySize   = 4,
            BoolFieldMask  = 0x04,
            ValueLiteral   = "42",
        };

        var script = FreezeScriptGenerator.Generate(p);

        Assert.Contains("valueType          = 'int32',", script);
        Assert.DoesNotContain("boolMask", script);
    }

    [Theory]
    [InlineData(0x01, true)]
    [InlineData(0x02, true)]
    [InlineData(0x80, true)]
    [InlineData(0x00, false)]   // no mask reported
    [InlineData(0xFF, false)]   // UE's native-bool marker
    [InlineData(0x03, false)]   // two bits
    [InlineData(0x100, false)]  // outside a byte
    [InlineData(-2, false)]
    public void IsPackedBoolMask_AcceptsOnlySingleBitsInAByte(int mask, bool expected)
        => Assert.Equal(expected, FreezeScriptGenerator.IsPackedBoolMask(mask));

    [Fact]
    public void FreezeHelper_WriteBool_HonoursTheMaskItIsGiven()
    {
        // The generator emitting `boolMask` is only half the fix — the helper
        // has to ACT on it. This pins the helper source, because nothing else
        // in the suite executes Lua: the byte-stamping write must be reachable
        // ONLY when no packed mask was supplied.
        var lua = FreezeHelperLuaResource.Read();

        // The mask reaches the writer (tick passes it as the 3rd argument).
        Assert.Contains("w(addr + offset, value, mask)", lua);
        Assert.Contains("handle.cfg.boolMask", lua);

        // Read-modify-write of a single bit, arithmetic-only because CE's Lua
        // has no bAnd/bOr/bNot (same idiom as UE5T_setbit).
        Assert.Contains("isPackedBoolMask(mask)", lua);
        Assert.Contains("math.floor(b / mask) % 2", lua);

        // The 0/0xFF exclusions live in the helper too, not only in C#.
        Assert.Contains("BOOL_BIT_MASKS", lua);
        Assert.DoesNotContain("[255]", lua);
        Assert.DoesNotContain("[0] =", lua);

        // The old unconditional comment must be gone: it documented the defect
        // as intended behaviour, which is how it survived so long.
        Assert.DoesNotContain("We do NOT support packed bitfield bools", lua);
    }

    // ── audit #5 AA2/AA3: recycled slots and a cache kept forever ────────────
    //
    // The behaviour of these two is covered properly by scripts/tests/
    // freeze_helper_test.lua, which EXECUTES the helper against stubbed CE
    // globals. CI has no Lua interpreter, so these source-level assertions are
    // the tripwire: they cannot prove the guard works, only that nobody deleted
    // it. Run the Lua harness after touching the helper.

    [Fact]
    public void FreezeHelper_TickGuardsOnClassIdentity_NotJustTheVtable()
    {
        var lua = FreezeHelperLuaResource.Read();

        // The witness travels from the mailbox into the handle...
        Assert.Contains("OFF_INSTANCE", lua);
        Assert.Contains("OFF_UFUNC", lua);
        Assert.Contains("handle._classPtr", lua);

        // ...and the tick compares the object's live ClassPrivate against it. The
        // witness is now per-ENTRY in derived scope and page-wide in exact scope, so
        // the comparison reads `want` — resolved from clsOf[i] or cPtr — rather than
        // cPtr directly. The line that matters is that a compare still gates the write.
        Assert.Contains("local want = clsOf and clsOf[i] or cPtr", lua);
        Assert.Contains("readQword(addr + cOff) == want", lua);

        // The old guard tested only "is qword 0 non-zero", which a recycled or
        // pooled block passes because it holds old bytes or a free-list link.
        // It survives ONLY as the no-witness fallback, so the bare form that
        // used to gate the write must not be the gate any more.
        Assert.DoesNotContain("local vt = readQword(addr)\n        if vt and vt ~= 0 then", lua);
    }

    [Fact]
    public void FreezeHelper_PersistentRescanFailure_StopsWritingAndSurfaces()
    {
        var lua = FreezeHelperLuaResource.Read();

        // A bounded failure streak, not "keep the stale cache forever".
        Assert.Contains("MAX_FAIL_STREAK", lua);
        Assert.Contains("handle._failStreak", lua);
        Assert.Contains("handle._abandoned", lua);

        // _lastError had three writers and zero readers — the failure never
        // reached anyone. Both accessors are the readers.
        Assert.Contains("handle.lastError = function()", lua);
        Assert.Contains("handle.isAbandoned = function()", lua);
    }

    [Fact]
    public void FreezeHelper_BakesTheSameContractVersionAsTheGenerator()
    {
        // The helper is a hand-maintained table file carrying its OWN copy of the
        // contract number, so it can drift from the DLL/C# pair that
        // check_mailbox_contract.py keeps in step. It reads the contract-2
        // identity witness, so a helper stuck at 1 would run happily against a
        // DLL that never fills those fields.
        var lua = FreezeHelperLuaResource.Read();

        Assert.Contains(
            $"local UE5_SCRIPT_CONTRACT = {CeMailboxLayout.ContractVersion}", lua);
    }

    // ==================================================================
    // Audit #5 AA12 + AA13 — a freeze that applied NOTHING used to report
    // a clean success.
    //
    // `pcall` answers "did Lua raise", never "did anything get frozen", and
    // no mailbox failure can raise (they are all caught inside the helper's
    // own pcall). So the old `pcall(handleOrErr.start)` was true for a DLL
    // that was not injected, a contract mismatch, and a stale mailbox alike
    // — and the generator then auto-closed the Lua window over a CE record
    // left ticked. start() now returns (ok, err, count).
    // ==================================================================

    private static string EnableBlockOf(string script)
    {
        var e = script.IndexOf("[ENABLE]", System.StringComparison.Ordinal);
        var d = script.IndexOf("[DISABLE]", System.StringComparison.Ordinal);
        return script.Substring(e, d - e);
    }

    private static FreezeScriptParams SampleParams() => new()
    {
        ClassName      = "BP_Teammate_C",
        PropertyName   = "CurrentHealth",
        PropertyOffset = 0x4F8,
        UeTypeName     = "FloatProperty",
        PropertySize   = 4,
        BoolFieldMask  = 0,
        ValueLiteral   = "9999.0",
    };

    [Fact]
    public void Generate_ReadsStartOutcome_NotJustThePcallStatus()
    {
        var script = FreezeScriptGenerator.Generate(SampleParams());

        // Five captures: pcall status, then start()'s own (ok, err, count, capped).
        // `scapped` joined the tuple with the derived scope — a capped pool makes the
        // count a floor rather than a total, and dropping the capture here would put
        // the caveat back out of reach. (`[FREEZESCOPE-2026-08-18]`)
        Assert.Contains("local sok, sok2, serr, scount, scapped = pcall(handleOrErr.start)",
                        script);
    }

    [Fact]
    public void Generate_HardFailure_StopsTimersUnticksAndReturns()
    {
        var enable = EnableBlockOf(FreezeScriptGenerator.Generate(SampleParams()));

        // The handle slot must not be dropped without stopping first: start() has
        // already created both timers, so nil'ing the slot alone strands them
        // writing into the game with nothing able to reach them.
        var stopIdx   = enable.IndexOf("pcall(handleOrErr.stop)", System.StringComparison.Ordinal);
        var clearIdx  = enable.IndexOf("_ue5_freeze_handles[FREEZE_KEY] = nil",
                                       stopIdx < 0 ? 0 : stopIdx, System.StringComparison.Ordinal);
        Assert.True(stopIdx >= 0, "hard-failure branch must stop the timers");
        Assert.True(clearIdx > stopIdx, "stop() must come before the slot is cleared");
        // ⚠ This used to assert the literal `if memrec then memrec.Active = false end`, i.e. it
        // pinned the BROKEN shape: CE ignores an in-[ENABLE] untick entirely, because setActive
        // exits early while fActive is still false and then sets it true after the script returns
        // ([FREEZEUNTICK-2026-08-20]). The assertion passed for as long as the untick did nothing.
        // What the branch has to do is untick DEFERRABLY, via the shared emitter.
        Assert.Contains(CeLuaHygiene.DeferredUntickLua().Trim(), enable, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_HardFailurePath_ReturnsBeforeTheWindowClose()
    {
        // CLAUDE.md: "On ANY error path the close MUST be unreachable." A reorder
        // that moved the close above the bail-out would still compile and still
        // pass every "contains" assertion — this is the one that catches it.
        var enable = EnableBlockOf(FreezeScriptGenerator.Generate(SampleParams()));

        var bailIdx  = enable.IndexOf("'[Freeze] nothing was frozen:", System.StringComparison.Ordinal);
        var closeIdx = enable.IndexOf(CeLuaHygiene.CloseCall, System.StringComparison.Ordinal);
        Assert.True(bailIdx >= 0, "the hard-failure message must be emitted");
        Assert.True(closeIdx >= 0, "the success close must still be emitted");
        Assert.True(bailIdx < closeIdx,
            "the hard-failure bail-out must precede the close, or the window shuts over an error");

        // And the return that makes it unreachable sits between the two.
        var returnIdx = enable.IndexOf("return", bailIdx, System.StringComparison.Ordinal);
        Assert.True(returnIdx > bailIdx && returnIdx < closeIdx,
            "the bail-out must return before reaching the close");
    }

    [Fact]
    public void Generate_ArmedButEmpty_DoesNotUntick_AndKeepsTheWindowOpen()
    {
        // A class-wide freeze armed before its instances spawn is the helper's
        // advertised purpose, so zero live instances must NOT untick the record —
        // that would turn the feature into the bug. It must still be visible, and
        // the window must stay up to show it.
        var enable = EnableBlockOf(FreezeScriptGenerator.Generate(SampleParams()));

        Assert.Contains("elseif scount == 0 then", enable);
        Assert.Contains("[Freeze] armed: no live instances of BP_Teammate_C", enable);

        // The close is gated on a reported outcome, a non-zero count, AND an
        // un-capped pool — a capped one printed a caveat that must not be closed over.
        Assert.Contains("if sok2 == true and scount ~= 0 and not scapped and DEBUG == 0 then",
                        enable);

        // The armed branch must not carry an untick. Slice from the branch to the
        // end of the if-chain and assert the untick is not inside it.
        var armedIdx = enable.IndexOf("elseif scount == 0 then", System.StringComparison.Ordinal);
        var endIdx   = enable.IndexOf("\nend", armedIdx, System.StringComparison.Ordinal);
        var armedBranch = enable.Substring(armedIdx, endIdx - armedIdx);
        Assert.DoesNotContain("memrec.Active = false", armedBranch);
    }

    [Fact]
    public void Generate_OlderHelper_IsReportedAsUnknown_NotAsSuccessOrFailure()
    {
        // A helper at <= 1.1 returns nothing from start(), so sok2 is nil. Calling
        // that a success (close the window, stay ticked) or a failure (untick a
        // freeze that may well be running) would both be invented verdicts.
        var enable = EnableBlockOf(FreezeScriptGenerator.Generate(SampleParams()));

        var nilIdx = enable.IndexOf("if sok2 == nil then", System.StringComparison.Ordinal);
        Assert.True(nilIdx >= 0, "the old-helper state must be handled first");

        var failIdx = enable.IndexOf("elseif not sok2 then", System.StringComparison.Ordinal);
        Assert.True(failIdx > nilIdx,
            "nil must be tested BEFORE `not sok2`, or an old helper is misread as a hard failure");

        Assert.Contains("older ue5_freeze_helper.lua", enable);
        // `sok2 == true` in the close condition is what keeps nil from closing.
        Assert.Contains("sok2 == true", enable);
    }

    [Fact]
    public void Generate_ClassNameWithQuote_IsEscaped()
    {
        var p = new FreezeScriptParams
        {
            ClassName      = "Weird'Class",
            PropertyName   = "X",
            PropertyOffset = 0x10,
            UeTypeName     = "IntProperty",
            PropertySize   = 4,
            BoolFieldMask  = 0,
            ValueLiteral   = "1",
        };

        var script = FreezeScriptGenerator.Generate(p);

        // Single quote must be backslash-escaped inside the Lua literal
        Assert.Contains(@"className          = 'Weird\'Class',", script);
    }

    [Fact]
    public void Generate_OffsetRendersAsHex()
    {
        // 256 = 0x100 -- verify the formatter produces 0x{X} not 256.
        var p = new FreezeScriptParams
        {
            ClassName      = "Foo",
            PropertyName   = "Bar",
            PropertyOffset = 256,
            UeTypeName     = "IntProperty",
            PropertySize   = 4,
            BoolFieldMask  = 0,
            ValueLiteral   = "0",
        };

        var script = FreezeScriptGenerator.Generate(p);

        Assert.Contains("propOffset         = 0x100,", script);
    }

    // ==================================================================
    // [FREEZESTUCK-2026-08-18] — an abandoned freeze left the record ACTIVE.
    //
    // The helper stopped writing after MAX_FAIL_STREAK failed rescans and said so
    // with a print() into a Lua Engine window this generator had already closed. CE
    // shows a ticked record (a red X on the checkbox means ACTIVE, not failed), so
    // the user was told a cheat was applied while nothing was being written — and
    // the message's own advice ("re-enable the record") was unfollowable, because
    // nothing had disabled it.
    //
    // The BEHAVIOUR is proven by scripts/tests/freeze_helper_test.lua, which runs the
    // helper against a stubbed CE (including a memory record whose Active=false
    // dispatches the [DISABLE] chunk, so the reentrancy hazard is real there). These
    // are the tripwires for the half that lives in the generated script.
    // ==================================================================

    [Fact]
    public void Generate_HandsTheCeRecordToTheHelper()
    {
        var enable = EnableBlockOf(FreezeScriptGenerator.Generate(SampleParams()));

        // Without this the helper has nothing to untick and the checkbox keeps lying.
        Assert.Contains("CFG.memrec = memrec", enable);

        // It must reach the helper BEFORE the handle is built, or the handle it is
        // stored in does not have it.
        var wireIdx  = enable.IndexOf("CFG.memrec = memrec", System.StringComparison.Ordinal);
        var buildIdx = enable.IndexOf("pcall(freezeProperty, CFG)", System.StringComparison.Ordinal);
        Assert.True(buildIdx > wireIdx,
            "the record must be in CFG before freezeProperty reads it");
    }

    [Fact]
    public void FreezeHelper_Abandonment_UnticksTheRecordAndSaysSo()
    {
        var lua = FreezeHelperLuaResource.Read();

        // The record travels into the handle...
        Assert.Contains("cfg.memrec", lua);
        // ...and the abandonment path drives it inactive.
        Assert.Contains("abandonAndUntick", lua);
        Assert.Contains("rec.Active = false", lua);

        // Deferred, not inline: setting Active=false runs [DISABLE], which calls
        // stop(), which destroys the timer whose handler is on the stack.
        Assert.Contains("timer.destroy()", lua);

        // ...and the diagnosis must not be killed by its own fix. Unticking runs
        // [DISABLE], whose last line is this generator's auto-close, so a print()
        // into the Lua Engine window disappears the moment the record clears.
        Assert.Contains("pcall(showMessage, message)", lua);

        // The unfollowable advice must be GONE, not merely joined by better wording —
        // it told the user to re-enable a record that had never been disabled.
        Assert.DoesNotContain("Re-enable the record after fixing it.", lua);
        Assert.Contains("has been unticked", lua);
        // ...and the no-record case still tells the truth about who has to act.
        Assert.Contains("Untick and re-tick this record", lua);
    }

    // ==================================================================
    // [FREEZESCOPE-2026-08-18] — the freeze held the DECLARING class only.
    //
    // A Property Search row for an inherited field is keyed to the class that
    // DECLARES it, so freezing a pawn's bCanBeDamaged submitted "Actor" and the DLL's
    // exact-name pool returned one incidental debug actor. The Force submenu on the
    // SAME ROW already walked subclasses (Solide, audit #5 A6).
    // ==================================================================

    [Fact]
    public void Generate_AsksForTheDerivedScope()
    {
        var script = FreezeScriptGenerator.Generate(SampleParams());

        // In the EDITABLE block, because narrowing the scope is a legitimate thing to
        // want and CFG is where this script documents its knobs.
        Assert.Contains("derived            = true,", script);
    }

    [Fact]
    public void FreezeHelper_DerivedScope_IsWiredAllTheWayToTheWire()
    {
        var lua = FreezeHelperLuaResource.Read();

        // The request flag reaches the mailbox...
        Assert.Contains("LI_IN_DERIVED", lua);
        Assert.Contains("writeInteger(mb + OFF_CMD_FLAGS", lua);

        // ...the reply is unpacked at the WIDER stride, which is the half that fails
        // silently: at 8 bytes the second "address" is entry 1's class pointer.
        Assert.Contains("ENTRY_SIZE_DERIVED", lua);

        // ...and each entry keeps its own identity witness, because a sweep across
        // subclasses has no single class for the contract-2 page witness to name.
        Assert.Contains("handle._cacheCls", lua);

        // Truncation is read from the DLL rather than guessed from the page size.
        Assert.Contains("LI_OUT_TRUNCATED", lua);
        Assert.Contains("handle.isTruncated", lua);
    }

    [Fact]
    public void Generate_CappedPool_IsReportedAsAFloorNotATotal()
    {
        var enable = EnableBlockOf(FreezeScriptGenerator.Generate(SampleParams()));

        Assert.Contains("elseif scapped then", enable);
        Assert.Contains("CAP REACHED", enable);
        Assert.Contains("floor, not a total", enable);

        // A capped pool is a SUCCESS, so it must not untick — same rule as the
        // armed-but-empty branch beside it.
        var idx = enable.IndexOf("elseif scapped then", System.StringComparison.Ordinal);
        var end = enable.IndexOf("\nend", idx, System.StringComparison.Ordinal);
        Assert.DoesNotContain("memrec.Active = false", enable.Substring(idx, end - idx));
    }

    [Fact]
    public void HeldClassName_PrefersTheDefiningClass()
    {
        // One definition, shared by the dialog's "Class:" row and the script the VM
        // builds — they used to choose separately and could disagree.
        Assert.Equal("Actor", FreezeScriptGenerator.HeldClassName("BP_Pawn_C", "Actor"));
        Assert.Equal("BP_Pawn_C", FreezeScriptGenerator.HeldClassName("BP_Pawn_C", ""));
        Assert.Equal("BP_Pawn_C", FreezeScriptGenerator.HeldClassName("BP_Pawn_C", null));
        Assert.Equal("", FreezeScriptGenerator.HeldClassName(null, null));
    }

    [Fact]
    public void FreezeHelperLuaResource_Read_ReturnsNonTrivialContent()
    {
        var content = FreezeHelperLuaResource.Read();

        Assert.NotNull(content);
        Assert.True(content.Length > 500,
            $"freeze helper content suspiciously short ({content.Length} chars)");
        // Sanity check: contains the public API surface the generator depends on.
        Assert.Contains("freezeProperty", content);
        Assert.Contains("CMD_LIST_INSTANCES", content);
    }
}
