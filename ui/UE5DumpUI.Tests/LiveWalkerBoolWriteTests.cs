using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [A3-BOOL-NATIVE-NOWRITE] Editing a native bool in Live Walker wrote nothing and reported
/// "Written".
///
/// <para>The DLL publishes a bool mask only for a single-bit FieldMask. A NATIVE bool — every
/// Blueprint bool, a plain <c>UPROPERTY() bool bFoo;</c>, every container element — has FieldMask
/// 0xFF, so no mask was sent and the UI parsed 0; <c>ApplyBoolMask(cur, 0, v)</c> returns
/// <c>cur</c> for both values, so the editor wrote back the byte it had just read and printed
/// "Written: bFoo = true" while the row still read false. The recorded safe shape: the DLL
/// recognises the native layout and says so (<c>bool_native</c>); the UI writes 0x01 / 0x00 only
/// for a confirmed native bool, read-modify-writes a single-bit mask, REFUSES when the mask is
/// unresolved (mask 0 also means "the probe missed", and a whole-byte write there is the AA1
/// corruption of up to 7 sibling bools), and reads the byte back instead of trusting the write.</para>
/// </summary>
public class LiveWalkerBoolWriteTests
{
    /// <summary>A byte-addressed memory the edit path reads and writes.</summary>
    private sealed class MemStub : StubDumpService
    {
        public readonly Dictionary<ulong, byte> Mem = new();
        public readonly List<(ulong Addr, byte[] Data)> Writes = new();
        /// <summary>The game recomputes the field: writes land, but the byte reads back unchanged.</summary>
        public bool GameIgnoresWrites;

        private static ulong P(string a) => System.Convert.ToUInt64(a.Replace("0x", "").Replace("0X", ""), 16);

        public override Task<byte[]> ReadMemAsync(string addr, int size, CancellationToken ct = default)
        {
            var a = P(addr);
            var r = new byte[size];
            for (int i = 0; i < size; i++) r[i] = Mem.GetValueOrDefault(a + (ulong)i);
            return Task.FromResult(r);
        }

        public override Task WriteMemAsync(string addr, byte[] data, CancellationToken ct = default)
        {
            var a = P(addr);
            Writes.Add((a, data.ToArray()));
            if (!GameIgnoresWrites)
                for (int i = 0; i < data.Length; i++) Mem[a + (ulong)i] = data[i];
            return Task.CompletedTask;
        }
    }

    private static LiveWalkerViewModel Vm(MemStub dump)
        => new(dump, new MockLoggingService(), new MockPlatformService(Path.GetTempPath()));

    private const ulong Addr = 0x100010;

    private static LiveFieldValue Bool(bool native = false, int mask = 0) => new()
    {
        Name = "bFlag", TypeName = "BoolProperty", Offset = 0x10, Size = 1,
        BoolNative = native, BoolFieldMask = mask,
        BoolBitIndex = mask != 0 ? System.Numerics.BitOperations.Log2((uint)mask) : -1,
        FieldAddress = "0x100010", TypedValue = "false",
    };

    [Fact]
    public async Task NativeBool_True_WritesAWholeByte01_AndSaysWritten()
    {
        var dump = new MemStub();
        dump.Mem[Addr] = 0x00;
        var vm = Vm(dump);

        await vm.CommitFieldEditAsync(Bool(native: true), "true");

        var w = Assert.Single(dump.Writes);
        Assert.Equal(Addr, w.Addr);
        Assert.Equal(new byte[] { 0x01 }, w.Data);   // not the byte it just read back
        Assert.Contains("Written: bFlag = true", vm.StatusText);
    }

    [Fact]
    public async Task NativeBool_False_Writes00()
    {
        var dump = new MemStub();
        dump.Mem[Addr] = 0x01;
        var vm = Vm(dump);

        await vm.CommitFieldEditAsync(Bool(native: true), "false");

        Assert.Equal(new byte[] { 0x00 }, Assert.Single(dump.Writes).Data);
    }

    [Fact]
    public async Task UnresolvedMask_IsRefused_NothingIsWritten()
    {
        // Mask 0 and not native: the probe missed. The byte may hold up to 8 packed bools, so a
        // whole-byte write would flip siblings — the AA1 corruption. Refuse, and say why.
        var dump = new MemStub();
        dump.Mem[Addr] = 0x05;
        var vm = Vm(dump);

        await vm.CommitFieldEditAsync(Bool(native: false, mask: 0), "true");

        Assert.Empty(dump.Writes);
        Assert.Contains("Not written", vm.StatusText);
        Assert.DoesNotContain("Written:", vm.StatusText);
    }

    [Fact]
    public async Task PackedMask_ReadModifyWrite_KeepsTheSiblingBits()
    {
        // The control: a single-bit mask was already right, and must stay right.
        var dump = new MemStub();
        dump.Mem[Addr] = 0x03;
        var vm = Vm(dump);

        await vm.CommitFieldEditAsync(Bool(mask: 0x04), "true");

        Assert.Equal(new byte[] { 0x07 }, Assert.Single(dump.Writes).Data);
        Assert.Contains("Written: bFlag = true", vm.StatusText);
    }

    [Fact]
    public async Task ReadBackMismatch_IsReported_NotCalledWritten()
    {
        // The write lands, but the byte reads back unchanged (the game recomputes the field, or
        // the address is wrong). "Written" would be a claim nobody measured.
        var dump = new MemStub { GameIgnoresWrites = true };
        dump.Mem[Addr] = 0x00;
        var vm = Vm(dump);

        await vm.CommitFieldEditAsync(Bool(native: true), "true");

        Assert.Contains("but the game now reads", vm.StatusText);
        Assert.DoesNotContain("Written:", vm.StatusText);
    }

    [Fact]
    public async Task ArrayOfBool_ElementRows_AreNativeBools()
    {
        // UE containers of bool always hold native bools (FieldMask 0xFF): an element row the VM
        // builds itself must say so, or editing one is the same silent no-op.
        const string A = "0x100000";
        var dump = new MemStub();
        dump.RegisterStruct(A, new InstanceWalkResult
        {
            Address = A, Name = "Obj", ClassName = "BP_Obj_C", ClassAddr = "0x900000",
            Fields = new List<LiveFieldValue>
            {
                new()
                {
                    Name = "Flags", TypeName = "ArrayProperty", Offset = 0x40, Size = 16,
                    ArrayCount = 2, ArrayInnerType = "BoolProperty", ArrayElemSize = 1, ArrayDataAddr = "0x500000",
                    ArrayElements = new() { new() { Index = 0, Value = "true", Hex = "01" },
                                            new() { Index = 1, Value = "false", Hex = "00" } },
                },
            },
        });
        var vm = Vm(dump);
        await vm.NavigateToAddressCommand.ExecuteAsync(A);
        await vm.NavigateToContainerCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "Flags"));

        Assert.All(vm.Fields, f => Assert.True(f.BoolNative, $"{f.Name} must be a native bool"));
    }

    // --- B05 review follow-up: the masked read-back, and the map / set row builders -------------

    [Fact]
    public async Task PackedMask_ReadBackMismatch_IsReported()
    {
        // The read-back covers the MASKED write too. A sibling bit is set and the game reverts ours:
        // comparing the whole byte, or `back != 0`, would call this "Written".
        var dump = new MemStub { GameIgnoresWrites = true };
        dump.Mem[Addr] = 0x01;
        var vm = Vm(dump);

        await vm.CommitFieldEditAsync(Bool(mask: 0x04), "true");

        Assert.Contains("but the game now reads", vm.StatusText);
        Assert.DoesNotContain("Written:", vm.StatusText);
    }

    [Fact]
    public async Task PackedMask_ClearWithASiblingSet_IsWrittenAndVerified()
    {
        // The other direction: clearing our bit leaves the sibling set, so the byte reads back
        // non-zero -- a `back != 0` read-back would report a mismatch that did not happen.
        var dump = new MemStub();
        dump.Mem[Addr] = 0x05;
        var vm = Vm(dump);

        await vm.CommitFieldEditAsync(Bool(mask: 0x04), "false");

        Assert.Equal(new byte[] { 0x01 }, Assert.Single(dump.Writes).Data);
        Assert.Contains("Written: bFlag = false", vm.StatusText);
        Assert.DoesNotContain("game now reads", vm.StatusText);
    }

    [Fact]
    public async Task MapOfBool_ValueRows_AreNativeBools_AndWriteTheValueByte()
    {
        const string A = "0x100000";
        var dump = new MemStub();
        dump.RegisterStruct(A, new InstanceWalkResult
        {
            Address = A, Name = "Obj", ClassName = "BP_Obj_C", ClassAddr = "0x900000",
            Fields = new List<LiveFieldValue>
            {
                new()
                {
                    Name = "Unlocked", TypeName = "MapProperty", Offset = 0x40, Size = 80,
                    MapCount = 1, MapKeyType = "IntProperty", MapValueType = "BoolProperty",
                    MapKeySize = 4, MapValueSize = 1, MapDataAddr = "0x500000",
                    MapValueOffset = 4, MapStride = 16,
                    MapElements = new() { new() { Index = 0, Key = "7", Value = "false", KeyHex = "07000000", ValueHex = "00" } },
                },
            },
        });
        var vm = Vm(dump);
        await vm.NavigateToAddressCommand.ExecuteAsync(A);
        await vm.NavigateToContainerCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "Unlocked"));

        var row = Assert.Single(vm.Fields);
        Assert.True(row.BoolNative, "a map's bool VALUE is a native bool");

        await vm.CommitFieldEditAsync(row, "true");
        var w = Assert.Single(dump.Writes);
        Assert.Equal(0x500004UL, w.Addr);                // the value, not the key
        Assert.Equal(new byte[] { 0x01 }, w.Data);
    }

    [Fact]
    public async Task SetOfBool_ElementRows_AreNativeBools()
    {
        const string A = "0x100000";
        var dump = new MemStub();
        dump.RegisterStruct(A, new InstanceWalkResult
        {
            Address = A, Name = "Obj", ClassName = "BP_Obj_C", ClassAddr = "0x900000",
            Fields = new List<LiveFieldValue>
            {
                new()
                {
                    Name = "Seen", TypeName = "SetProperty", Offset = 0x40, Size = 80,
                    SetCount = 2, SetElemType = "BoolProperty", SetElemSize = 1,
                    SetDataAddr = "0x500000", SetStride = 12,
                    SetElements = new() { new() { Index = 0, Value = "true", ValueHex = "01" },
                                          new() { Index = 1, Value = "false", ValueHex = "00" } },
                },
            },
        });
        var vm = Vm(dump);
        await vm.NavigateToAddressCommand.ExecuteAsync(A);
        await vm.NavigateToContainerCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "Seen"));

        Assert.Equal(2, vm.Fields.Count);
        Assert.All(vm.Fields, f => Assert.True(f.BoolNative, $"{f.Name} must be a native bool"));
    }

    [Theory]
    [InlineData(true,  0,    UE5DumpUI.Core.FieldValueConverter.BoolWriteMode.NativeByte)]
    [InlineData(false, 0x04, UE5DumpUI.Core.FieldValueConverter.BoolWriteMode.MaskedBit)]
    [InlineData(false, 0x80, UE5DumpUI.Core.FieldValueConverter.BoolWriteMode.MaskedBit)]
    [InlineData(false, 0,    UE5DumpUI.Core.FieldValueConverter.BoolWriteMode.Refuse)]   // unresolved, NOT native
    [InlineData(false, 0x06, UE5DumpUI.Core.FieldValueConverter.BoolWriteMode.Refuse)]   // not a single bit
    [InlineData(false, 0xFF, UE5DumpUI.Core.FieldValueConverter.BoolWriteMode.Refuse)]   // 0xFF without the native flag
    public void PlanBoolWrite_ChoosesTheOnlySafeWrite(bool native, int mask, UE5DumpUI.Core.FieldValueConverter.BoolWriteMode expected)
        => Assert.Equal(expected, UE5DumpUI.Core.FieldValueConverter.PlanBoolWrite(native, mask));
}
