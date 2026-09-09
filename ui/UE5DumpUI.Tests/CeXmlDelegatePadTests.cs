using UE5DumpUI.Models;
using UE5DumpUI.Services;
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
}
