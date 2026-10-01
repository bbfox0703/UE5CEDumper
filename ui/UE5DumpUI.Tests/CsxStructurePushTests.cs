using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [AOBM-DISSECT-INJECT] The CSX half: Live Walker's CSX export, built straight in CE's Structure Dissect by a CE Lua
/// script pushed through the plugin's <c>CreateAAScript</c> (AOBMaker declined a CSX command, R12). The structure
/// tests RUN the generated <c>[ENABLE]</c> block on CE's own Lua VM against stubs that record every property the
/// script sets, then compare what it built with the CSX it came from.
/// </summary>
public class CsxStructurePushTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"UE5DumpCsxPush_{Guid.NewGuid():N}");
    private readonly MockLoggingService _log = new();

    public CsxStructurePushTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // Every element shape CsxExportService writes: signed / unsigned / hex scalars, a float, a CE 7.7 Binary bit, and a
    // Pointer carrying a nested child structure whose element is a Unicode String with a byte size.
    private const string Csx = """

        <Structures>
          <Structure Name="BP_Hero_C_Hero_0" AutoFill="0" AutoCreate="1" DefaultHex="0" AutoDestroy="0" DoNotSaveLocal="0" RLECompression="1" AutoCreateStructsize="4096">
            <Elements>
              <Element Offset="16" Vartype="8 Bytes" Bytesize="8" OffsetHex="00000010" Description="VTable" DisplayMethod="hexadecimal"/>
              <Element Offset="40" Vartype="4 Bytes" Bytesize="4" OffsetHex="00000028" Description="Health" DisplayMethod="unsigned integer"/>
              <Element Offset="44" Vartype="2 Bytes" Bytesize="2" OffsetHex="0000002C" Description="Level" DisplayMethod="signed integer"/>
              <Element Offset="48" Vartype="Float" Bytesize="4" OffsetHex="00000030" Description="Speed" DisplayMethod="unsigned integer"/>
              <Element Offset="88" BitSize="1" Vartype="Binary" BitStart="5" Bytesize="1" OffsetHex="00000058" Description="bIsDead" DisplayMethod="unsigned integer"/>
              <Element Offset="96" Vartype="Pointer" Bytesize="8" OffsetHex="00000060" Description="Name 'x' \ ]]" DisplayMethod="unsigned integer">
                <Structure Name="FString_Name">
                  <Elements>
                    <Element Offset="0" Vartype="Unicode String" Bytesize="512" OffsetHex="00000000" DisplayMethod="unsigned integer"/>
                  </Elements>
                </Structure>
              </Element>
            </Elements>
          </Structure>
        </Structures>
        """;

    private static string Block(string script, string marker)
    {
        int start = script.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, marker + " missing");
        int from = script.IndexOf("{$lua}\n", start, StringComparison.Ordinal) + "{$lua}\n".Length;
        int to = script.IndexOf("{$asm}", from, StringComparison.Ordinal);
        return script[from..to];
    }

    /// <summary>CE's structure API as stubs that record what the script does. <c>VERSION</c> picks 7.7 or 7.5 (whose
    /// elements have no BitStart / BitSize, so assigning one raises, as an unknown property does in CE).</summary>
    private static string Harness(string body, string version, string before = "", string after = "") => $$"""
        UE5_DEBUG = 0
        vtByte=0 vtWord=1 vtDword=2 vtQword=3 vtSingle=4 vtDouble=5 vtString=6 vtUnicodeString=7 vtByteArray=8
        vtBinary=9 vtPointer=12
        function getCEVersion() return {{version}} end
        GLOBAL, REMOVED, DESTROYED, SHOWN, FAIL_AT, ADDS = {}, {}, {}, nil, nil, 0
        function showMessage(m) SHOWN = m end
        function createTimer() return {} end
        function synchronize(f) f() end
        function getLuaEngine() return { Close = function() end } end
        local function newElement()
          local e, raw = {}, {}
          return setmetatable(e, {
            __index = raw,
            __newindex = function(t, k, v)
              if getCEVersion() < 7.7 and (k == 'BitStart' or k == 'BitSize') then
                error('Unknown property: ' .. k)
              end
              raw[k] = v
            end })
        end
        function createStructure(name)
          local s = { Name = name, els = {} }
          s.beginUpdate = function() s.updating = (s.updating or 0) + 1 end
          s.endUpdate = function() s.updating = s.updating - 1 end
          s.addElement = function()
            ADDS = ADDS + 1
            if FAIL_AT and ADDS == FAIL_AT then error('stub: addElement failed') end
            local e = newElement() s.els[#s.els + 1] = e return e
          end
          s.addToGlobalStructureList = function() GLOBAL[#GLOBAL + 1] = s end
          s.removeFromGlobalStructureList = function()
            for i = #GLOBAL, 1, -1 do if GLOBAL[i] == s then table.remove(GLOBAL, i) end end
            REMOVED[#REMOVED + 1] = s.Name
          end
          s.destroy = function() DESTROYED[#DESTROYED + 1] = s.Name end
          return s
        end
        function getStructureCount() return #GLOBAL end
        function getStructure(i) return GLOBAL[i + 1] end
        {{before}}
        local chunk = assert(load('local syntaxcheck,memrec=...\n' .. [==[
        {{body}}
        ]==]))
        local REC = { ID = 9, Active = false }
        RAN_OK, RAN_ERR = pcall(chunk, false, REC)
        local function dump(s, depth)
          local pad = string.rep('  ', depth)
          print(pad .. 'S|' .. s.Name)
          for _, e in ipairs(s.els) do
            print(pad .. string.format('E|%s|%s|%s|%s|%s|%s|%s', tostring(e.Offset), tostring(e.Name), tostring(e.Vartype),
              tostring(e.BitStart), tostring(e.BitSize), tostring(e.Bytesize), tostring(e.DisplayMethod)))
            if e.ChildStruct then dump(e.ChildStruct, depth + 1) end
          end
        end
        for _, s in ipairs(GLOBAL) do dump(s, 0) end
        {{after}}
        print('RAN=' .. tostring(RAN_OK) .. '|' .. tostring(RAN_ERR))
        print('SHOWN=' .. tostring(SHOWN))
        """;

    private static string Run(string version, string before = "", string after = "")
    {
        var push = CsxStructurePushGenerator.Generate(Csx);
        var (exit, output) = CeLua53Host.Run(Harness(Block(push.Script, "[ENABLE]"), version, before, after));
        Assert.True(exit == 0, output);
        return output.Replace("\r\n", "\n");
    }

    [Fact]
    public void Every_element_kind_is_built_as_CE_would_import_it()
    {
        var output = Run("7.7");

        Assert.Contains("RAN=true|nil", output);
        Assert.Contains("SHOWN=nil", output);
        var expected = string.Join("\n",
            "S|BP_Hero_C_Hero_0",
            "E|16|VTable|3|nil|nil|nil|dtHexadecimal",
            "E|40|Health|2|nil|nil|nil|dtUnsignedInteger",
            "E|44|Level|1|nil|nil|nil|dtSignedInteger",
            "E|48|Speed|4|nil|nil|nil|dtUnsignedInteger",
            "E|88|bIsDead|9|5|1|nil|dtUnsignedInteger",
            "E|96|Name 'x' \\ ]]|12|nil|nil|nil|dtUnsignedInteger",
            "  S|FString_Name",
            "  E|0||7|nil|nil|512|dtUnsignedInteger");
        Assert.Contains(expected, output);
    }

    [Fact]
    public void Only_the_root_is_a_global_structure_and_no_build_is_left_open()
    {
        // CE's own CSX import builds a nested child only under a Pointer element and never lists it globally.
        var output = Run("7.7", after: """
            print('GLOBALS=' .. #GLOBAL)
            for _, s in ipairs(GLOBAL) do print('OPEN=' .. tostring(s.updating)) end
            """);
        Assert.Contains("GLOBALS=1", output);
        Assert.Contains("OPEN=0", output);
    }

    [Fact]
    public void Before_CE_7_7_a_bit_becomes_its_byte_named_with_the_bit()
    {
        // 7.5's structure elements have no BitStart / BitSize; assigning one there raises.
        var output = Run("7.5");

        Assert.Contains("RAN=true|nil", output);
        Assert.Contains("E|88|bIsDead (bit 5)|0|nil|nil|nil|dtUnsignedInteger", output);
    }

    [Fact]
    public void A_failed_build_registers_nothing_raises_and_shows_no_modal()
    {
        // The script is enabled THROUGH the plugin: a showMessage there would hold the plugin's pipe for every client
        // ([AOBM-TRAINER-SETUP-MODAL]). It raises instead, and the plugin reports CE's reason.
        var output = Run("7.7", before: "FAIL_AT = 3", after: """
            print('GLOBALS=' .. #GLOBAL)
            print('DESTROYED=' .. table.concat(DESTROYED, ','))
            """);
        Assert.Contains("RAN=false|", output);
        Assert.Contains("stub: addElement failed", output);
        Assert.Contains("GLOBALS=0", output);
        Assert.Contains("SHOWN=nil", output);
        Assert.Contains("DESTROYED=", output);
        Assert.DoesNotContain("DESTROYED=\n", output);
    }

    [Fact]
    public void A_second_push_replaces_the_structure_of_the_same_name()
    {
        var output = Run("7.7",
            before: "local old = createStructure('BP_Hero_C_Hero_0') old.addToGlobalStructureList()\n" +
                    "local keep = createStructure('Other') keep.addToGlobalStructureList()",
            after: "print('REMOVED=' .. table.concat(REMOVED, ',')) print('GLOBALS=' .. #GLOBAL)");
        Assert.Contains("REMOVED=BP_Hero_C_Hero_0", output);
        Assert.Contains("GLOBALS=2", output);   // 'Other' stays; the new structure replaces the old one
    }

    [Fact]
    public void The_script_is_a_one_shot_that_follows_the_CE_Lua_hygiene_rules()
    {
        var push = CsxStructurePushGenerator.Generate(Csx);
        var enable = Block(push.Script, "[ENABLE]");
        var disable = Block(push.Script, "[DISABLE]");

        Assert.StartsWith("if syntaxcheck then return end", enable);
        Assert.StartsWith("if syntaxcheck then return end", disable);
        Assert.Contains("local DEBUG", enable);
        Assert.Contains(CeLuaHygiene.DeferredUntickLua(), enable);   // a one-shot: the structure is the result
        Assert.DoesNotContain("showMessage", push.Script);
        Assert.Contains(CeLuaHygiene.Attribution, enable);
    }

    [Fact]
    public void The_counts_and_the_name_describe_what_is_built()
    {
        var push = CsxStructurePushGenerator.Generate(Csx);
        Assert.Equal("BP_Hero_C_Hero_0", push.RootName);
        Assert.Equal(2, push.Structures);
        Assert.Equal(7, push.Elements);
    }

    // ---- the status line ----

    private static readonly CsxPushScript Pushed = new("s", "BP_Hero", 2, 7);

    [Fact]
    public void The_status_follows_what_the_plugin_reported()
    {
        Assert.Equal((AobMakerActions.StructPushText(new SymbolScriptResult(true, true, null, null), Pushed)),
                     ("Structure 'BP_Hero' added to CE's Structure Dissect (2 structures, 7 elements)", false));
        Assert.True(AobMakerActions.StructPushText(new SymbolScriptResult(true, false, null, "boom"), Pushed).IsError);
        Assert.Contains("boom", AobMakerActions.StructPushText(new SymbolScriptResult(true, false, null, "boom"), Pushed).Text);
        Assert.False(AobMakerActions.StructPushText(new SymbolScriptResult(true, null, null, null), Pushed).IsError);
        Assert.True(AobMakerActions.StructPushText(new SymbolScriptResult(false, null, null, null, TimedOut: true), Pushed).IsError);
        Assert.True(AobMakerActions.StructPushText(new SymbolScriptResult(false, null, null, "no"), Pushed).IsError);
    }

    // ---- Live Walker ----

    private LiveWalkerViewModel Walker(ScriptedAobMakerBridge bridge)
    {
        var vm = new LiveWalkerViewModel(new StubDumpService(), _log, new MockPlatformService(_dir), bridge);
        vm.ApplyAobMakerProbe(bridge.Available);
        vm.CurrentAddress = "0x7FF600001000";
        vm.CurrentClassName = "BP_Hero_C";
        vm.CurrentObjectName = "Hero_0";
        vm.Fields.Add(new LiveFieldValue { Name = "Health", TypeName = "IntProperty", Offset = 0x28, Size = 4 });
        vm.HasData = true;
        return vm;
    }

    [Fact]
    public async Task LiveWalker_pushes_the_structure_through_the_CE_plugin_enabled_at_once()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true, AaDetail = new SymbolScriptResult(true, true, null, null) };
        var vm = Walker(bridge);

        await vm.PushCsxToCeCommand.ExecuteAsync(null);

        var aa = Assert.Single(bridge.AaScripts);
        Assert.Equal(CsxStructurePushGenerator.RecordPrefix + "BP_Hero_C_Hero_0", aa.Description);
        Assert.True(aa.AutoActivate);
        Assert.Equal(CeInjectScriptGenerator.RecordGroup, aa.Group);
        Assert.Contains("local NAME = 'BP_Hero_C_Hero_0'", aa.Script);
        Assert.StartsWith("Structure 'BP_Hero_C_Hero_0' added to CE's Structure Dissect", vm.StatusText);
    }

    [Fact]
    public async Task LiveWalker_without_the_plugin_pushes_nothing_and_says_why()
    {
        var bridge = new ScriptedAobMakerBridge { Available = false };
        var vm = Walker(bridge);

        await vm.PushCsxToCeCommand.ExecuteAsync(null);

        Assert.Empty(bridge.AaScripts);
        Assert.Equal(AobMakerUnavailable.Text(bridge), vm.StatusText);
    }
}
