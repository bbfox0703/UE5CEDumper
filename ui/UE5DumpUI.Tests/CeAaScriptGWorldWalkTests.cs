using System.Linq;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Tests for the restart-stable "Copy CE AA Script" — the GWorld-anchored Lua
/// walk generator (CeXmlExportService.GenerateGWorldWalkedSymbolXml) and the
/// LiveWalkerViewModel 3-case dispatch (AOB walk / hardcoded-GWorld walk /
/// legacy hardcoded address).
/// </summary>
public class CeAaScriptGWorldWalkTests
{
    private static BreadcrumbItem Bc(string addr, string fieldName, bool deref = false,
        int offset = 0, bool container = false)
        => new()
        {
            Address = addr,
            Label = fieldName,
            FieldName = fieldName,
            FieldOffset = offset,
            IsPointerDeref = deref,
            IsContainerView = container,
        };

    // GWorld root → deref +0x30 → deref +0x98 → deref +0x1A0 (leaf).
    private static List<BreadcrumbItem> SampleSpine() => new()
    {
        Bc("0x1000", "GWorld"),
        Bc("0x2000", "PersistentLevel", deref: true, offset: 0x30),
        Bc("0x3000", "OwningGameInstance", deref: true, offset: 0x98),
        Bc("0x4000", "PlayerController", deref: true, offset: 0x1A0),
    };

    // ── Generator: AOB branch ──────────────────────────────────────────

    [Fact]
    public void Aob_branch_scans_walks_and_registers_leaf()
    {
        var xml = CeXmlExportService.GenerateGWorldWalkedSymbolXml(
            "BP_Test", SampleSpine(), useAob: true,
            aob: "48 8B 1D ?? ?? ?? ??", aobPos: 3, aobLen: 7, gworldSlotAddr: "");

        Assert.Contains("[ENABLE]", xml);
        Assert.Contains("[DISABLE]", xml);
        Assert.Contains("AOBScanModuleUE", xml);
        Assert.Contains("aob='48 8B 1D ?? ?? ?? ??'", xml);
        // Base deref then one readQword per spine step (index 1..3).
        Assert.Contains("local addr = gworld_base and readQword(gworld_base)", xml);
        Assert.Contains("readQword(addr + 0x30)", xml);
        Assert.Contains("readQword(addr + 0x98)", xml);
        Assert.Contains("readQword(addr + 0x1A0)", xml);
        Assert.Contains("registerSymbol('BP_Test', addr)", xml);
        // DISABLE unregisters BOTH the leaf and the GWorld base symbol.
        Assert.Contains("unregisterSymbol('BP_Test')", xml);
        Assert.Contains("unregisterSymbol('gworld_base_", xml);
        // Not the legacy hardcoded form.
        Assert.DoesNotContain("define(", xml);
    }

    [Fact]
    public void Aob_branch_does_not_hardcode_a_base_address()
    {
        var xml = CeXmlExportService.GenerateGWorldWalkedSymbolXml(
            "S", SampleSpine(), useAob: true,
            aob: "48 8B 1D", aobPos: 3, aobLen: 7, gworldSlotAddr: "0xDEADBEEF");
        Assert.DoesNotContain("tonumber('", xml);   // base comes from the scan, not a literal
    }

    [Fact]
    public void Walk_carries_debug_preamble_and_debug_gated_close()
    {
        var xml = CeXmlExportService.GenerateGWorldWalkedSymbolXml(
            "BP_Test", SampleSpine(), useAob: true,
            aob: "48 8B 1D ?? ?? ?? ??", aobPos: 3, aobLen: 7, gworldSlotAddr: "");

        // Quiet by default; UE5_DEBUG flips it on.
        Assert.Contains("local DEBUG = UE5_DEBUG or 0", xml);
        // Status lines are gated; the mid-walk WARNING still surfaces.
        Assert.Contains("dbg(string.format('[GWorldWalk] BP_Test = %X', addr))", xml);
        Assert.Contains("print('[GWorldWalk] WARNING: null pointer mid-walk", xml);
        // Close ONLY on a live leaf + DEBUG off (a null walk keeps the window open).
        Assert.Contains("if DEBUG == 0 and addr and addr ~= 0 then closeLuaEngine() end", xml);
        // DISABLE closes too, gated on DEBUG.
        Assert.Contains("if DEBUG == 0 then closeLuaEngine() end", xml);
    }

    // ── Generator: hardcoded-GWorld branch ─────────────────────────────

    [Fact]
    public void Hardcode_branch_uses_tonumber_and_no_aob_scan()
    {
        var xml = CeXmlExportService.GenerateGWorldWalkedSymbolXml(
            "BP_Test", SampleSpine(), useAob: false,
            aob: "", aobPos: 0, aobLen: 0, gworldSlotAddr: "0x7FF61234ABCD");

        Assert.DoesNotContain("AOBScanModuleUE", xml);
        Assert.Contains("tonumber('7FF61234ABCD', 16)", xml);   // 0x stripped, 64-bit safe
        Assert.Contains("registerSymbol('gworld_base_", xml);   // GWorld registered too
        Assert.Contains("readQword(addr + 0x30)", xml);
        Assert.Contains("registerSymbol('BP_Test', addr)", xml);
        Assert.Contains("unregisterSymbol('BP_Test')", xml);
        Assert.Contains("unregisterSymbol('gworld_base_", xml);  // DISABLE symmetry
    }

    // ── Generator: walk semantics + edge cases ─────────────────────────

    [Fact]
    public void Walk_starts_at_index_1_skipping_the_gworld_root_offset()
    {
        var spine = new List<BreadcrumbItem>
        {
            Bc("0x1000", "GWorld", offset: 0x9999),   // root offset must be ignored
            Bc("0x2000", "Step", deref: true, offset: 0x30),
        };
        var xml = CeXmlExportService.GenerateGWorldWalkedSymbolXml(
            "S", spine, useAob: false, aob: "", aobPos: 0, aobLen: 0, gworldSlotAddr: "0x10");

        Assert.Contains("readQword(addr + 0x30)", xml);
        Assert.DoesNotContain("0x9999", xml);   // the root's offset never appears as a step
    }

    [Fact]
    public void Inline_struct_crumb_adds_without_dereferencing()
    {
        var spine = new List<BreadcrumbItem>
        {
            Bc("0x1000", "GWorld"),
            Bc("0x1010", "InlineStruct", deref: false, offset: 0x10),   // inline: add, no deref
            Bc("0x2000", "Ptr", deref: true, offset: 0x20),             // deref
        };
        var xml = CeXmlExportService.GenerateGWorldWalkedSymbolXml(
            "S", spine, useAob: false, aob: "", aobPos: 0, aobLen: 0, gworldSlotAddr: "0x10");

        Assert.Contains("addr = addr + 0x10", xml);     // inline add
        Assert.Contains("(inline)", xml);
        Assert.Contains("readQword(addr + 0x20)", xml); // pointer deref
    }

    [Fact]
    public void Container_view_crumb_dereferences()
    {
        var spine = new List<BreadcrumbItem>
        {
            Bc("0x1000", "GWorld"),
            Bc("0x2000", "Arr", deref: false, offset: 0x40, container: true),  // container ⇒ deref
        };
        var xml = CeXmlExportService.GenerateGWorldWalkedSymbolXml(
            "S", spine, useAob: false, aob: "", aobPos: 0, aobLen: 0, gworldSlotAddr: "0x10");

        Assert.Contains("readQword(addr + 0x40)", xml);
    }

    [Fact]
    public void Every_walk_hop_guards_against_nil_and_zero()
    {
        var xml = CeXmlExportService.GenerateGWorldWalkedSymbolXml(
            "S", SampleSpine(), useAob: false, aob: "", aobPos: 0, aobLen: 0, gworldSlotAddr: "0x10");

        // CE readQword returns NIL (not 0) on an unreadable page, so every hop must
        // guard `addr and addr ~= 0` — a bare `if addr ~= 0` would let a nil through
        // and throw on `readQword(nil + off)`. No bare zero-only guard may remain.
        Assert.DoesNotContain("if addr ~= 0 then", xml);
        foreach (var line in xml.Split('\n')
                     .Where(l => l.Contains("addr = readQword(addr +") || l.Contains("addr = addr + 0x")))
            Assert.Contains("if addr and addr ~= 0 then", line);
    }

    [Fact]
    public void Leaf_only_registers_when_walk_is_non_null()
    {
        var xml = CeXmlExportService.GenerateGWorldWalkedSymbolXml(
            "S", SampleSpine(), useAob: false, aob: "", aobPos: 0, aobLen: 0, gworldSlotAddr: "0x10");
        Assert.Contains("if addr and addr ~= 0 then", xml);
        Assert.Contains("WARNING: null pointer mid-walk", xml);
    }

    // ── ViewModel dispatch: which case is chosen ───────────────────────

    private static LiveWalkerViewModel MakeVm(out MockPlatformService platform)
    {
        platform = new MockPlatformService(System.IO.Path.GetTempPath());
        return new LiveWalkerViewModel(new StubDumpService(), new MockLoggingService(), platform);
    }

    private static EngineState EngineWith(string gworldAob, string gworldAddr) => new()
    {
        GWorldAob = gworldAob,
        GWorldAobPos = 3,
        GWorldAobLen = 7,
        GWorldAddr = gworldAddr,
        ModuleName = "Game.exe",
        ModuleBase = "0x140000000",
    };

    private static void LoadGWorldSpine(LiveWalkerViewModel vm)
    {
        vm.Breadcrumbs.Clear();
        vm.Breadcrumbs.Add(Bc("0x1000", "GWorld"));
        vm.Breadcrumbs.Add(Bc("0x2000", "PersistentLevel", deref: true, offset: 0x30));
        vm.Breadcrumbs.Add(Bc("0x4000", "PlayerController", deref: true, offset: 0x1A0));
        vm.CurrentAddress = "0x4000";       // == last crumb ⇒ walkable
        vm.CurrentClassName = "BP_Player";
    }

    [Fact]
    public async Task Dispatch_gworld_root_with_aob_emits_aob_walk()
    {
        var vm = MakeVm(out var platform);
        LoadGWorldSpine(vm);
        vm.SetEngineState(EngineWith(gworldAob: "48 8B 1D ?? ?? ?? ??", gworldAddr: "0x150000000"));
        vm.UseAobSymbol = true;

        await vm.GenerateCeAAScriptCommand.ExecuteAsync(null);

        Assert.NotNull(platform.LastClipboard);
        Assert.Contains("AOBScanModuleUE", platform.LastClipboard);
        Assert.Contains("registerSymbol('BP_Player', addr)", platform.LastClipboard);
        Assert.DoesNotContain("define(", platform.LastClipboard);
    }

    [Fact]
    public async Task Dispatch_gworld_root_aob_unchecked_uses_hardcoded_base()
    {
        var vm = MakeVm(out var platform);
        LoadGWorldSpine(vm);
        // AOB is available, but the user left the checkbox OFF → respect it (Case B).
        vm.SetEngineState(EngineWith(gworldAob: "48 8B 1D ?? ?? ?? ??", gworldAddr: "0x150000000"));
        vm.UseAobSymbol = false;

        await vm.GenerateCeAAScriptCommand.ExecuteAsync(null);

        Assert.NotNull(platform.LastClipboard);
        Assert.DoesNotContain("AOBScanModuleUE", platform.LastClipboard);
        Assert.Contains("tonumber('150000000', 16)", platform.LastClipboard);
        Assert.Contains("registerSymbol('BP_Player', addr)", platform.LastClipboard);
    }

    [Fact]
    public async Task Dispatch_non_gworld_root_emits_legacy_hardcoded_address()
    {
        var vm = MakeVm(out var platform);
        vm.Breadcrumbs.Clear();
        vm.Breadcrumbs.Add(Bc("0x9000", "SomeActor"));   // root is NOT GWorld
        vm.CurrentAddress = "0x9000";
        vm.CurrentClassName = "BP_Other";
        vm.SetEngineState(EngineWith(gworldAob: "48 8B 1D", gworldAddr: "0x150000000"));

        await vm.GenerateCeAAScriptCommand.ExecuteAsync(null);

        Assert.NotNull(platform.LastClipboard);
        Assert.Contains("define(BP_Other,", platform.LastClipboard);
        Assert.Contains("registersymbol(BP_Other)", platform.LastClipboard);
        Assert.DoesNotContain("{$lua}", platform.LastClipboard);   // not a walk script
    }

    [Fact]
    public async Task Dispatch_negative_offset_hop_falls_back_to_hardcoded()
    {
        var vm = MakeVm(out var platform);
        vm.Breadcrumbs.Clear();
        vm.Breadcrumbs.Add(Bc("0x1000", "GWorld"));
        // A WorldLevel back-reference recovery hop carries FieldOffset -1 — not
        // forward-walkable, so the dispatch must fall back to the hardcoded address.
        vm.Breadcrumbs.Add(Bc("0x2000", "OwningWorld", deref: true, offset: -1));
        vm.CurrentAddress = "0x2000";
        vm.CurrentClassName = "BP_WP";
        vm.SetEngineState(EngineWith(gworldAob: "48 8B 1D", gworldAddr: "0x150000000"));
        vm.UseAobSymbol = true;

        await vm.GenerateCeAAScriptCommand.ExecuteAsync(null);

        Assert.NotNull(platform.LastClipboard);
        Assert.Contains("define(BP_WP,", platform.LastClipboard);
        Assert.DoesNotContain("AOBScanModuleUE", platform.LastClipboard);
    }

    [Fact]
    public async Task Dispatch_spine_not_landing_on_current_falls_back()
    {
        var vm = MakeVm(out var platform);
        LoadGWorldSpine(vm);
        vm.CurrentAddress = "0xDEAD";   // != last crumb (0x4000) ⇒ not a faithful spine
        vm.CurrentClassName = "BP_Mismatch";
        vm.SetEngineState(EngineWith(gworldAob: "48 8B 1D", gworldAddr: "0x150000000"));
        vm.UseAobSymbol = true;

        await vm.GenerateCeAAScriptCommand.ExecuteAsync(null);

        Assert.NotNull(platform.LastClipboard);
        Assert.Contains("define(BP_Mismatch,", platform.LastClipboard);
    }

    [Fact]
    public async Task Dispatch_container_leaf_class_name_is_sanitized_to_valid_symbol()
    {
        var vm = MakeVm(out var platform);
        vm.Breadcrumbs.Clear();
        vm.Breadcrumbs.Add(Bc("0x9000", "SomeActor"));   // Case C path
        vm.CurrentAddress = "0x9000";
        vm.CurrentClassName = "Map<FName, int>";          // container view class name
        vm.SetEngineState(EngineWith(gworldAob: "", gworldAddr: ""));

        await vm.GenerateCeAAScriptCommand.ExecuteAsync(null);

        Assert.NotNull(platform.LastClipboard);
        // < > , spaces all become '_' so the XML <Description> + define() stay valid.
        Assert.DoesNotContain("Map<FName, int>", platform.LastClipboard);
        Assert.DoesNotContain("<FName", platform.LastClipboard);
        Assert.Contains("Map_FName", platform.LastClipboard);
    }

    // ── [PATH-CEXML-AMP] an '&' in the exe name (legal) must not break the XML ──

    private const string AmpModule = "\"Tom&Jerry-Win64-Shipping.exe\"+1234";

    [Fact]
    public void RegisterSymbolXml_AnAmpersandInTheModule_IsWellFormed_AndThePushedScriptIsRaw()
    {
        var xml = CeXmlExportService.GenerateRegisterSymbolXml("BP_Test", AmpModule);

        var doc = System.Xml.Linq.XDocument.Parse(xml);          // a raw '&' threw here
        string script = doc.Descendants("AssemblerScript").Single().Value;
        Assert.Contains("define(BP_Test,\"Tom&Jerry-Win64-Shipping.exe\"+1234)", script);
        // The AOBMaker push un-escapes, so it byte-matches what CE runs after a paste.
        Assert.Contains("define(BP_Test,\"Tom&Jerry-Win64-Shipping.exe\"+1234)",
            CeXmlExportService.ExtractAssemblerScript(xml));
    }

    [Fact]
    public void InstanceXml_AModuleRootedAddressWithAnAmpersand_IsWellFormed()
    {
        var xml = CeXmlExportService.GenerateInstanceXml(AmpModule, "Inst", "TestClass",
            new[] { new LiveFieldValue { Name = "Health", TypeName = "FloatProperty", Offset = 0x10, Size = 4 } });

        var doc = System.Xml.Linq.XDocument.Parse(xml);
        Assert.Contains(doc.Descendants("Address"), a => a.Value == AmpModule);
    }

    // ── ExtractAssemblerScript: raw body for the AOBMaker CreateAAScript push ──

    [Fact]
    public void Extract_returns_raw_body_without_xml_wrapper()
    {
        var xml = CeXmlExportService.GenerateRegisterSymbolXml("BP_Test", "\"Game.exe\"+1234");
        var body = CeXmlExportService.ExtractAssemblerScript(xml);

        // The body is exactly what CE executes — the [ENABLE]/[DISABLE] script.
        Assert.StartsWith("[ENABLE]", body);
        Assert.Contains("define(BP_Test,\"Game.exe\"+1234)", body);
        Assert.Contains("registersymbol(BP_Test)", body);
        Assert.Contains("[DISABLE]", body);
        Assert.Contains("unregistersymbol(BP_Test)", body);
        // No XML wrapper survives.
        Assert.DoesNotContain("<AssemblerScript>", body);
        Assert.DoesNotContain("<CheatTable>", body);
        Assert.DoesNotContain("<CheatEntry>", body);
    }

    [Fact]
    public void Extract_from_gworld_walk_xml_yields_runnable_lua()
    {
        var xml = CeXmlExportService.GenerateGWorldWalkedSymbolXml(
            "BP_Test", SampleSpine(), useAob: false,
            aob: "", aobPos: 0, aobLen: 0, gworldSlotAddr: "0x7FF61234ABCD");
        var body = CeXmlExportService.ExtractAssemblerScript(xml);

        Assert.Contains("{$lua}", body);
        Assert.Contains("registerSymbol('BP_Test', addr)", body);
        Assert.DoesNotContain("</AssemblerScript>", body);
    }

    [Fact]
    public void Extract_unescapes_xml_entities_so_lua_runs_verbatim()
    {
        // CE un-escapes &amp;/&lt;/&gt; before running the script; the pushed body
        // must match the clipboard-pasted one byte-for-byte.
        var xml = "<CheatTable><CheatEntries><CheatEntry><AssemblerScript>\n"
                + "[ENABLE]\nlocal x = a &amp; b   -- A&lt;B and C&gt;D\n[DISABLE]\n"
                + "      </AssemblerScript></CheatEntry></CheatEntries></CheatTable>";
        var body = CeXmlExportService.ExtractAssemblerScript(xml);

        Assert.Contains("local x = a & b   -- A<B and C>D", body);
        Assert.DoesNotContain("&amp;", body);
        Assert.DoesNotContain("&lt;", body);
        Assert.DoesNotContain("&gt;", body);
    }

    [Fact]
    public void Extract_returns_empty_when_no_assembler_script_marker()
    {
        Assert.Equal("", CeXmlExportService.ExtractAssemblerScript("<CheatTable></CheatTable>"));
        Assert.Equal("", CeXmlExportService.ExtractAssemblerScript(""));
    }

    // ── AOB export toggle: persisted preference vs. live gated checkbox ──────
    // The "AOB" item in the Live Walker export-options dropdown must be REMEMBERED
    // (AobSymbolPreference), and a game where GWorld came from a FALLBACK (no AOB)
    // — which force-unchecks the live box — must NOT erase that preference.

    [Fact]
    public void Aob_user_check_on_gworld_root_records_the_preference()
    {
        var vm = MakeVm(out _);
        LoadGWorldSpine(vm);                                             // root = GWorld
        vm.SetEngineState(EngineWith("48 8B 1D ?? ?? ?? ??", "0x150000000")); // AOB available
        Assert.True(vm.CanUseAobSymbol);                                // checkbox enabled

        vm.UseAobSymbol = true;                                          // user clicks it
        Assert.True(vm.AobSymbolPreference);                            // intent captured
    }

    [Fact]
    public void Aob_fallback_gworld_force_unchecks_live_box_but_keeps_preference()
    {
        var vm = MakeVm(out _);
        LoadGWorldSpine(vm);
        vm.SetEngineState(EngineWith("48 8B 1D ?? ?? ?? ??", "0x150000000"));
        vm.UseAobSymbol = true;                                          // opted in
        Assert.True(vm.AobSymbolPreference);

        // Next game: GWorld found via FALLBACK (no AOB) → symbol unavailable.
        vm.SetEngineState(EngineWith(gworldAob: "", gworldAddr: ""));
        Assert.False(vm.CanUseAobSymbol);                              // checkbox disabled
        Assert.False(vm.UseAobSymbol);                                // live box force-unchecked
        Assert.True(vm.AobSymbolPreference);                           // ← preference NOT erased
    }

    [Fact]
    public void Aob_preference_restores_when_symbol_becomes_available_again()
    {
        var vm = MakeVm(out _);
        LoadGWorldSpine(vm);
        vm.SetEngineState(EngineWith("48 8B 1D ?? ?? ?? ??", "0x150000000"));
        vm.UseAobSymbol = true;
        vm.SetEngineState(EngineWith(gworldAob: "", gworldAddr: ""));   // fallback → force-uncheck
        Assert.False(vm.UseAobSymbol);

        // Back to an AOB-capable game on a GWorld root → the stored choice returns.
        vm.SetEngineState(EngineWith("48 8B 1D ?? ?? ?? ??", "0x150000000"));
        Assert.True(vm.CanUseAobSymbol);
        Assert.True(vm.UseAobSymbol);
    }

    [Fact]
    public void Aob_leaving_gworld_root_force_unchecks_but_keeps_preference()
    {
        var vm = MakeVm(out _);
        LoadGWorldSpine(vm);
        vm.SetEngineState(EngineWith("48 8B 1D ?? ?? ?? ??", "0x150000000"));
        vm.UseAobSymbol = true;

        // Start from GameEngine / open an instance → root is no longer GWorld.
        vm.Breadcrumbs.Clear();
        vm.Breadcrumbs.Add(Bc("0x9000", "SomeActor"));
        Assert.False(vm.CanUseAobSymbol);
        Assert.False(vm.UseAobSymbol);            // force-unchecked off a non-GWorld root
        Assert.True(vm.AobSymbolPreference);      // preference survives

        // Walk GWorld again → restored.
        LoadGWorldSpine(vm);
        Assert.True(vm.UseAobSymbol);
    }

    [Fact]
    public void Aob_never_opted_in_stays_unchecked_on_gworld_root()
    {
        // A user who never touched the box must not see it auto-enable just because
        // AOB happens to be available (default preference = off).
        var vm = MakeVm(out _);
        LoadGWorldSpine(vm);
        vm.SetEngineState(EngineWith("48 8B 1D ?? ?? ?? ??", "0x150000000"));
        Assert.True(vm.CanUseAobSymbol);
        Assert.False(vm.AobSymbolPreference);
        Assert.False(vm.UseAobSymbol);
    }

    [Fact]
    public void Aob_applied_preference_reflects_into_live_box_when_gate_open()
    {
        // Mirrors ApplyOptions restoring a persisted preference: setting the intent
        // while the gate is already open re-derives the live checkbox immediately.
        var vm = MakeVm(out _);
        LoadGWorldSpine(vm);
        vm.SetEngineState(EngineWith("48 8B 1D ?? ?? ?? ??", "0x150000000"));

        vm.AobSymbolPreference = true;            // as ApplyOptions would set it
        Assert.True(vm.UseAobSymbol);
    }
}
