using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

public class StandaloneTrainerScriptGeneratorTests
{
    private static TrainerOffsets Usable() => new()
    {
        Code = 0,
        Chain =
        {
            new TrainerChainHop { Field = "OwningGameInstance", Offset = 0x1B8, Deref = true },
            new TrainerChainHop { Field = "LocalPlayers",       Offset = 0x38,  Deref = true },
            new TrainerChainHop { Field = "LocalPlayers[0]",    Offset = 0x0,   Deref = true },
            new TrainerChainHop { Field = "PlayerController",   Offset = 0x30,  Deref = true },
            new TrainerChainHop { Field = "Pawn",               Offset = 0x2A8, Deref = true },
        },
        PawnToRoot = 0x1A0, RootToRelLoc = 0x120, FVectorWidth = 24,
        PawnToCmc = 0x520, WalkSpeedOff = 0x1B0, GravityOff = 0x100, JumpOff = 0x1A4,
        CtrlRotOff = 0x2C8, CtrlRotSize = 24, PawnToController = 0x210,
        MoveModeOff = 0x1F4, VelocityOff = 0x160, VelocitySize = 24,
        GodBits = { new TrainerProtectBit { Name = "bCanBeDamaged", ByteOffset = 0x9C, Mask = 0x1, Protect = 0 } },
        Module = "Game.exe", GWorldAob = "48 8B 1D ?? ?? ?? ??", GWorldAobPos = 3, GWorldAobLen = 7,
    };

    [Fact]
    public void Setup_is_first_entry_and_auto_activates()
    {
        var entries = StandaloneTrainerScriptGenerator.Generate(Usable());
        Assert.Equal(StandaloneTrainerScriptGenerator.SetupDescription, entries[0].Description);
        Assert.True(entries[0].AutoActivate);
        // Only Setup auto-activates; every feature toggle stays off.
        Assert.All(entries.Skip(1), e => Assert.False(e.AutoActivate));
    }

    [Fact]
    public void Setup_bakes_gworld_aob_and_the_full_chain()
    {
        var setup = StandaloneTrainerScriptGenerator.Generate(Usable())[0].Script;
        Assert.Contains("aob = '48 8B 1D ?? ?? ?? ??'", setup);
        Assert.Contains("pos = 3, aoblen = 7", setup);
        Assert.Contains("module = 'Game.exe'", setup);
        // Every chain hop baked as (offset, deref).
        Assert.Contains("off = 0x1B8, deref = true", setup);   // OwningGameInstance
        Assert.Contains("off = 0x2A8, deref = true", setup);   // Pawn
    }

    [Fact]
    public void Setup_bakes_field_offsets_and_lwc_width()
    {
        var setup = StandaloneTrainerScriptGenerator.Generate(Usable())[0].Script;
        Assert.Contains("rootOff = 0x1A0", setup);
        Assert.Contains("relLocOff = 0x120", setup);
        Assert.Contains("vecWidth = 8", setup);                // 24-byte FVector -> double
        Assert.Contains("cmcOff = 0x520", setup);
        Assert.Contains("maxWalkOff = 0x1B0", setup);
        Assert.Contains("off = 0x9C, mask = 0x1, protect = 0", setup);   // bCanBeDamaged bit
    }

    [Fact]
    public void Float_fvector_yields_element_width_4()
    {
        var o = Usable();
        o.FVectorWidth = 12;   // UE4 float FVector
        var setup = StandaloneTrainerScriptGenerator.Generate(o)[0].Script;
        Assert.Contains("vecWidth = 4", setup);
    }

    [Fact]
    public void All_features_present_when_offsets_resolve()
    {
        var descs = StandaloneTrainerScriptGenerator.Generate(Usable()).Select(e => e.Description).ToList();
        Assert.Contains(descs, d => d.Contains("Move Speed"));
        Assert.Contains(descs, d => d.Contains("Gravity"));
        Assert.Contains(descs, d => d.Contains("Super Jump"));
        Assert.Contains(descs, d => d.Contains("God Mode"));
        Assert.Contains(descs, d => d.Contains("TP Save"));
        Assert.Contains(descs, d => d.Contains("TP Recall"));
    }

    [Fact]
    public void Move_speed_omitted_when_walkspeed_offset_missing()
    {
        var o = Usable();
        o.WalkSpeedOff = -1;
        var descs = StandaloneTrainerScriptGenerator.Generate(o).Select(e => e.Description).ToList();
        Assert.DoesNotContain(descs, d => d.Contains("Move Speed"));
        // Gravity still present (its offset resolved) — omission is per-feature.
        Assert.Contains(descs, d => d.Contains("Gravity"));
    }

    [Fact]
    public void Fly_present_per_preset_and_bakes_config()
    {
        var entries = StandaloneTrainerScriptGenerator.Generate(Usable());
        var fly = entries.Where(e => e.Description.Contains("Fly")).ToList();
        Assert.Equal(3, fly.Count);   // WASD / Numpad / Arrows
        var setup = entries[0].Script;
        Assert.Contains("moveModeOff = 0x1F4", setup);
        Assert.Contains("velOff = 0x160", setup);
        Assert.Contains("controllerOff = 0x210", setup);
        Assert.Contains("flySpeed = 1200", setup);
        var wasd = fly.First(e => e.Description.Contains("WASD")).Script;
        Assert.Contains("writeByte(c + UE5T.moveModeOff, 5)", wasd);   // force MOVE_Flying
        Assert.Contains("isKeyPressed(0x57)", wasd);                   // W = forward
        // Keys only drive movement while the game is foreground (else hover).
        Assert.Contains("getForegroundProcess() == getOpenedProcessID()", wasd);
        // Each preset bakes its own key set.
        Assert.Contains("isKeyPressed(0x68)", fly.First(e => e.Description.Contains("Numpad")).Script);   // Num8
        Assert.Contains("isKeyPressed(0x26)", fly.First(e => e.Description.Contains("Arrows")).Script);    // Up arrow
    }

    [Fact]
    public void Fly_omitted_when_move_mode_offset_missing()
    {
        var o = Usable();
        o.MoveModeOff = -1;
        var descs = StandaloneTrainerScriptGenerator.Generate(o).Select(e => e.Description).ToList();
        Assert.DoesNotContain(descs, d => d.Contains("Fly"));
        Assert.Contains(descs, d => d.Contains("Move Speed"));   // per-feature omission
    }

    [Fact]
    public void God_mode_omitted_when_no_protection_bit()
    {
        var o = Usable();
        o.GodBits.Clear();
        var descs = StandaloneTrainerScriptGenerator.Generate(o).Select(e => e.Description).ToList();
        Assert.DoesNotContain(descs, d => d.Contains("God Mode"));
    }

    [Fact]
    public void Movement_disable_restores_captured_base()
    {
        var setup = StandaloneTrainerScriptGenerator.Generate(Usable())[0].Script;
        // stopKnob must write the captured natural base back (so DISABLE reverts,
        // and a later re-enable can't re-capture a forced value).
        Assert.Contains("writeFloat(c + off, base)", setup);
        Assert.Contains("UE5T[key..'_off'] = off", setup);
    }

    [Fact]
    public void Tp_entries_are_momentary_via_untick()
    {
        var save = StandaloneTrainerScriptGenerator.Generate(Usable())
            .First(e => e.Description.Contains("TP Save")).Script;
        Assert.Contains("getMemoryRecordByDescription", save);
        Assert.Contains("mr.Active = false", save);
    }

    [Fact]
    public void Aob_scan_failure_aborts_before_close_no_fallback()
    {
        var setup = StandaloneTrainerScriptGenerator.Generate(Usable())[0].Script;
        // On AOB miss: surface + return, and there is no hardcoded-base fallback.
        Assert.Contains("AOB scan FAILED", setup);
        Assert.DoesNotContain("tonumber('", setup);   // no baked absolute address fallback
    }

    [Fact]
    public void Every_entry_carries_the_project_attribution_url()
    {
        foreach (var e in StandaloneTrainerScriptGenerator.Generate(Usable()))
            Assert.Contains("https://github.com/bbfox0703/UE5CEDumper", e.Script);
    }

    [Fact]
    public void Tp_entries_note_they_are_weaker_than_the_dll()
    {
        var entries = StandaloneTrainerScriptGenerator.Generate(Usable());
        foreach (var e in entries.Where(e => e.Description.Contains("TP ")))
        {
            Assert.Contains("RAW memory write", e.Script);
            Assert.Contains("WEAKER than the in-app Teleport", e.Script);
        }
    }

    [Fact]
    public void Every_entry_has_enable_and_disable_and_is_lf_only()
    {
        foreach (var e in StandaloneTrainerScriptGenerator.Generate(Usable()))
        {
            Assert.Contains("[ENABLE]", e.Script);
            Assert.Contains("[DISABLE]", e.Script);
            Assert.DoesNotContain("\r", e.Script);   // CE wants LF-only
        }
    }

    [Fact]
    public void Bit_ops_are_arithmetic_not_ce_bitwise_helpers()
    {
        // CE Lua has no bAnd/bOr/bNot — the generator must use arithmetic.
        var setup = StandaloneTrainerScriptGenerator.Generate(Usable())[0].Script;
        Assert.Contains("math.floor(b / mask) % 2", setup);
        Assert.DoesNotContain("bAnd(", setup);
        Assert.DoesNotContain("bOr(", setup);
    }

    [Fact]
    public void An_apostrophe_in_the_exe_name_is_escaped()
    {
        // [PATH-TRAINER-APOSTROPHE] LuaModule was the one name-derived Lua literal that skipped EscapeLuaString:
        // module = 'Tony's-Win64-Shipping.exe' is a syntax error on CE's VM, so Setup could never enable.
        var o = Usable();
        o.Module = "Tony's-Win64-Shipping.exe";
        var setup = StandaloneTrainerScriptGenerator.Generate(o)[0].Script;
        Assert.Contains(@"module = 'Tony\'s-Win64-Shipping.exe',", setup);
        Assert.DoesNotContain("module = 'Tony's", setup);
    }

    [Theory]
    [InlineData("Tony's-Win64-Shipping.exe")]
    [InlineData("Tony's&Jerry-Win64-Shipping.exe")]
    [InlineData("功夫-Win64-Shipping.exe")]
    public void Every_entry_compiles_on_CEs_VM_and_the_module_literal_reads_back(string exe)
    {
        // (second review, T-TRAINER-APOS-NOCOMPILE) The apostrophe fix above is pinned by its TEXT only. This compiles
        // every {$lua} block of every entry on CE's own VM, then evaluates the baked literal. Skips where the host is
        // not built (CeLua53Host).
        var o = Usable();
        o.Module = exe;
        var entries = StandaloneTrainerScriptGenerator.Generate(o);
        var sb = new StringBuilder();
        int n = 0;
        foreach (var e in entries)
            foreach (Match m in Regex.Matches(e.Script, @"\{\$lua\}\r?\n(.*?)\r?\n\{\$asm\}", RegexOptions.Singleline))
                sb.Append($"assert(load([==[{m.Groups[1].Value}]==], 'chunk{n++}'))\n");
        Assert.True(n >= entries.Count, $"{n} Lua block(s) in {entries.Count} entries");
        string rhs = Regex.Match(entries[0].Script, @"module = ('(?:[^'\\]|\\.)*'),").Groups[1].Value;
        sb.Append($"local v = {rhs}\nprint('MODULE=' .. v)\n");

        var (exit, output) = CeLua53Host.Run(sb.ToString());
        Assert.True(exit == 0, output);
        Assert.Contains("MODULE=" + exe, output);
    }
}
