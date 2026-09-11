using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// A delegate field's CE address must include the UE 5.3+ access-detector pad.
///
/// ⛔ THE DEFECT, found 2026-09-09 under [D4B-DELEGATEPAD] — after the DLL half had already
/// been fixed, verified on four engine/configuration combinations and shipped. UE 5.3 gave
/// <c>TScriptDelegate</c> / <c>TMulticastScriptDelegate</c> a <c>TDelegateAccessHandlerBase</c>
/// base class whose <c>DO_CHECK</c> specialization holds one <c>std::atomic&lt;uint64&gt;</c>.
/// In a checked build — Debug, Development, DebugGame — every delegate payload starts EIGHT
/// BYTES LATE; in Shipping/Test the base is empty and EBO applies. The exporter emitted
/// <c>Offsets=[0]</c> at the field's raw offset, so the CE record dereferenced the zeroed
/// DETECTOR and pointed at address 0.
///
/// ⚠ THE ENUMERATION THAT MISSED IT said "five readers assumed the unpadded layout" and was
/// DLL-only. The CE exporter and <c>scripts/ue5_dissect.lua</c> bake the same assumption and
/// were not counted, so the repair shipped while the artefact the user actually pastes into
/// Cheat Engine still carried the pre-5.3 layout.
///
/// ⭐ The pad is DERIVED BY THE DLL from the engine's own <c>FProperty::ElementSize</c> and sent
/// on the wire as <c>delegate_pad</c>. These tests pin the exporter's USE of it. Re-deriving the
/// rule here would repeat the mistake that made the first pad survey verify a Python copy of
/// the rule rather than the shipped one.
/// </summary>
public class CeXmlDelegatePadTests
{
    private static string Xml(params LiveFieldValue[] fields) =>
        CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "Inst", "TestClass", fields);

    /// <summary>Shaped the way the DLL actually sends a multicast: an implicit
    /// DelegateProperty array. <paramref name="size"/> is the engine's ElementSize and
    /// <paramref name="pad"/> what the DLL derived from it.</summary>
    private static LiveFieldValue Multicast(int offset, int size, int pad) => new()
    {
        Name = "OnClicked",
        TypeName = "MulticastInlineDelegateProperty",
        Offset = offset,
        Size = size,
        DelegatePad = pad,
        ArrayCount = 1,
        ArrayInnerType = "DelegateProperty",
        ArrayElemSize = 16,
    };

    /// <summary>⭐ THE CONTROL, and the reason this went a year unnoticed: every shipped title
    /// measured in this repo is a Shipping build, where the pad is 0 and the old code was
    /// right. A fix that moved the offset unconditionally would have broken every real
    /// export.</summary>
    [Fact]
    public void ShippingBuild_PadIsZero_OffsetUnchanged()
    {
        var xml = Xml(Multicast(0x2C0, size: 16, pad: 0));

        Assert.Contains("<Address>+2C0</Address>", xml);
        Assert.DoesNotContain("<Address>+2C8</Address>", xml);
    }

    /// <summary>Development / Debug / DebugGame on UE 5.3+: the payload starts at +8, so the
    /// emitted address must too, or <c>Offsets=[0]</c> derefs the detector.</summary>
    [Fact]
    public void CheckedBuild_PadShiftsTheEmittedAddress()
    {
        var xml = Xml(Multicast(0x2C0, size: 24, pad: 8));

        Assert.Contains("<Address>+2C8</Address>", xml);
        Assert.DoesNotContain("<Address>+2C0</Address>", xml);
    }

    /// <summary>⭐ THE CONTROL THAT MATTERS MOST. The pad is applied at ~21 emit sites through
    /// one helper that every field passes through, so the guard against collateral damage is
    /// that a non-delegate field — always <c>DelegatePad == 0</c> — does not move.</summary>
    [Fact]
    public void NonDelegateFieldsInTheSameExport_AreNotMoved()
    {
        var xml = Xml(
            new LiveFieldValue { Name = "Health", TypeName = "FloatProperty", Offset = 0x40, Size = 4 },
            Multicast(0x2C0, size: 24, pad: 8));

        Assert.Contains("<Address>+40</Address>", xml);   // untouched
        Assert.Contains("<Address>+2C8</Address>", xml);  // only the delegate moved
    }

    /// <summary>The pad travels with the field, so two delegates at different offsets both
    /// shift — this is not a one-off adjustment of a single known field.</summary>
    [Fact]
    public void EveryDelegateFieldShifts_NotJustTheFirst()
    {
        var a = Multicast(0x100, size: 24, pad: 8);
        var b = Multicast(0x200, size: 24, pad: 8);
        var xml = Xml(a, b);

        Assert.Contains("<Address>+108</Address>", xml);
        Assert.Contains("<Address>+208</Address>", xml);
        Assert.DoesNotContain("<Address>+100</Address>", xml);
        Assert.DoesNotContain("<Address>+200</Address>", xml);
    }

    // ---------------------------------------------------------------------------------
    // The one-click buttons beside each Live Walker row, which the four tests above did
    // NOT cover.
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// ⛔ MEASURED, NOT HYPOTHETICAL: [CEPATHS-UNPADDED-2026-09-09]. The exporter had the pad
    /// right all along — the CE XML for <c>Multicast_Inline (980)</c> carries
    /// <c>&lt;Address&gt;+988&lt;/Address&gt;</c> — but the row's HEX button logged
    /// "AOBMaker: navigated hex view to 1ED06BBD460", the access detector, which reads 0. Both
    /// CE-facing button handlers now go through <c>PayloadAddress</c> -- and, since [A4-PUSHCE-UNPADDED], so
    /// does the third, the batch push (see <c>BatchPush_SendsThePayloadAddress</c>).
    /// </summary>
    [Fact]
    public void PayloadAddress_AddsTheDelegatePad()
    {
        var f = new LiveFieldValue { TypeName = "MulticastInlineDelegateProperty",
                                     Offset = 0x980, DelegatePad = 8,
                                     FieldAddress = "0x1ED06BBD460" };
        Assert.Equal("0x1ED06BBD468", f.PayloadAddress);
        // ⚠ And FieldAddress itself stays unpadded — it is the FIELD's address, which is what
        // the Address column shows and what a reader comparing against an offset table expects.
        Assert.Equal("0x1ED06BBD460", f.FieldAddress);
    }

    /// <summary>
    /// The pad is 0 on Shipping/Test and absent for every non-delegate field, so the two
    /// addresses must be the same object there — a payload address that drifted by a byte on
    /// an ordinary IntProperty would be a far worse defect than the one this fixes.
    /// </summary>
    [Theory]
    [InlineData("IntProperty", 0)]
    [InlineData("MulticastInlineDelegateProperty", 0)]
    public void PayloadAddress_IsTheFieldAddressWhenThereIsNoPad(string type, int pad)
    {
        var f = new LiveFieldValue { TypeName = type, Offset = 0x100, DelegatePad = pad,
                                     FieldAddress = "0x1ED06BBD460" };
        Assert.Equal("0x1ED06BBD460", f.PayloadAddress);
    }

    /// <summary>An unparseable or empty address must not become "0x8" or throw.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("0x[ply_base]")]
    public void PayloadAddress_SurvivesAnAddressItCannotParse(string addr)
    {
        var f = new LiveFieldValue { TypeName = "DelegateProperty", DelegatePad = 8,
                                     FieldAddress = addr };
        Assert.Equal(addr, f.PayloadAddress);
    }

    // ---------------------------------------------------------------------------------
    // [A4-PUSHCE-UNPADDED] The batch "+CE Field (flat)" push: the THIRD CE-facing handler.
    // ---------------------------------------------------------------------------------

    private sealed class RecordingBridge : IAobMakerBridge
    {
        public List<string> Addresses { get; } = new();
        public bool IsAvailable => true;
        public Task<bool> CheckAvailabilityAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> NavigateHexViewAsync(string hexAddress, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> NavigateDisassemblerAsync(string hexAddress, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> CreateAAScriptAsync(string description, string script, bool autoActivate = true,
            string? group = null, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> CreateSymbolScriptAsync(string name, string aob, int pos, int aoblen,
            string symbol, string module, bool autoActivate = true, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> CreateMemoryRecordAsync(string description, string address, int valueType,
            bool isSigned = false, bool showAsHex = false, CancellationToken ct = default)
        {
            Addresses.Add(address);
            return Task.FromResult(true);
        }
        public Task<(bool Ok, string? ErrorMessage)> InjectTableFileAsync(string fileName, string content,
            CancellationToken ct = default) => Task.FromResult<(bool, string?)>((true, null));
    }

    /// <summary>⛔ The multi-select batch form of the per-row +CE, and the one CE-facing handler
    /// [CEPATHS-UNPADDED-2026-09-09] missed: it still pushed FieldAddress, so on a checked build a delegate row's
    /// record landed on the access detector, which reads 0. Driven through the command, so the test is the caller's.</summary>
    [Theory]
    [InlineData("MulticastInlineDelegateProperty", 8, "1ED06BBD468")]   // the payload, past the detector
    [InlineData("IntProperty", 0, "1ED06BBD460")]                       // the control: nothing moves
    public async Task BatchPush_SendsThePayloadAddress(string type, int pad, string expected)
    {
        var bridge = new RecordingBridge();
        var vm = new LiveWalkerViewModel(new StubDumpService(), new MockLoggingService(),
                                         new MockPlatformService(Path.GetTempPath()), bridge);
        vm.SelectedField = new LiveFieldValue { Name = "F", TypeName = type, Offset = 0x980, DelegatePad = pad,
                                                FieldAddress = "0x1ED06BBD460" };

        await vm.PushCeFieldToCeCommand.ExecuteAsync(null);

        Assert.Equal(new[] { expected }, bridge.Addresses);
    }

    // ---------------------------------------------------------------------------------
    // [A4-DELEGATE-ARRAY-PAD] A TArray<FScriptDelegate>'s ELEMENTS: the pad is per element.
    // ---------------------------------------------------------------------------------

    /// <summary>A TArray of the standalone unicast delegate, shaped the way the DLL sends it: the pad on the ELEMENTS
    /// (<c>array_elem_delegate_pad</c>), never on the array field, whose own bytes are its TArray header.</summary>
    private static LiveFieldValue DelegateArray(int elemPad) => new()
    {
        Name = "Handlers",
        TypeName = "ArrayProperty",
        Offset = 0xA0,
        Size = 16,
        ArrayCount = 2,
        ArrayInnerType = "DelegateProperty",
        ArrayElemSize = 16 + elemPad,
        ArrayElemDelegatePad = elemPad,
        ArrayElements = new List<ArrayElementValue> { new() { Index = 0 }, new() { Index = 1 } },
    };

    [Fact]
    public void DelegateArray_CheckedBuild_EachElementLeafSitsOnItsPayload()
    {
        var xml = Xml(DelegateArray(elemPad: 8));

        Assert.Contains("<Address>+A0</Address>", xml);          // the array group: the FIELD, never moved
        Assert.Contains("<Address>+8</Address>", xml);           // [0]: 0*24 + 8
        Assert.Contains("<Address>+20</Address>", xml);          // [1]: 1*24 + 8
        Assert.DoesNotContain("<Address>+18</Address>", xml);    // [1] unpadded: the detector
    }

    [Fact]
    public void DelegateArray_ShippingBuild_NothingMoves()
    {
        var xml = Xml(DelegateArray(elemPad: 0));

        Assert.Contains("<Address>+0</Address>", xml);
        Assert.Contains("<Address>+10</Address>", xml);          // [1]: 1*16
    }

    [Fact]
    public void DelegateArray_FabricatedTail_IsPaddedToo()
    {
        var xml = CeXmlExportService.GenerateInstanceXml(
            "\"Game.exe\"+1000", "Inst", "TestClass", new[] { DelegateArray(elemPad: 8) }, fabricateArrayCount: 4);

        Assert.Contains("<Address>+38</Address>", xml);          // [2]: 2*24 + 8
        Assert.Contains("<Address>+50</Address>", xml);          // [3]: 3*24 + 8
    }

    [Fact]
    public void MulticastInvocationListElements_AreNeverPadded()
    {
        // ⛔ Gating on ArrayInnerType == "DelegateProperty" alone would break this: a multicast's invocation list has
        // that inner too, and its elements are the NotChecked variant, never padded. Only an ArrayProperty's are.
        var mc = new LiveFieldValue
        {
            Name = "OnClicked", TypeName = "MulticastInlineDelegateProperty", Offset = 0x2C0, Size = 16,
            ArrayCount = 2, ArrayInnerType = "DelegateProperty", ArrayElemSize = 16, ArrayElemDelegatePad = 8,
            ArrayElements = new List<ArrayElementValue> { new() { Index = 0 }, new() { Index = 1 } },
        };
        var xml = Xml(mc);

        Assert.Contains("<Address>+10</Address>", xml);          // [1]: 1*16, unpadded
        Assert.DoesNotContain("<Address>+18</Address>", xml);
    }
}
