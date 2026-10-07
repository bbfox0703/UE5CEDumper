using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [LIVEFUNCS-STEP2] The store behind Live Funcs' ticks and snapshot choices: functions followed by name across
/// fetches, with their keys, live addresses and parameter size (docs/live-funcs-step2-items.md, U3).
/// </summary>
public class FunctionTickSetTests
{
    private static PeProfileEntry Row(string cls, string func, string addr, NameKey? key = null, bool unloaded = false,
                                      ushort parms = 0)
        => new() { ClassName = cls, FuncName = func, FuncAddr = addr, FnameKey = key, IsUnloaded = unloaded, ParmsSize = parms };

    [Fact]
    public void A_name_whose_rows_are_all_unloaded_keeps_its_keys_with_no_address()
    {
        var set = new FunctionTickSet();
        var row = Row("WBP_Inv_C", "OnOpen", "0x100", new NameKey(7, 0, 9, 0));
        set.Toggle(row, new[] { row });
        set.Refresh(new[] { Row("WBP_Inv_C", "OnOpen", "0x100", new NameKey(7, 0, 9, 0), unloaded: true) });

        Assert.Empty(set.AddressesOf("WBP_Inv_C::OnOpen"));   // the dead address is never sent
        var named = Assert.Single(set.Named());
        Assert.Equal(new NameKey(7, 0, 9, 0), Assert.Single(named.Keys));
    }

    [Fact]
    public void A_name_the_page_does_not_show_keeps_its_keys_and_addresses()
    {
        var set = new FunctionTickSet();
        var row = Row("Character", "Jump", "0x200", new NameKey(1, 0, 2, 0));
        set.Toggle(row, new[] { row });
        set.Refresh(new[] { Row("Character", "Walk", "0x300", new NameKey(3, 0, 2, 0)) });

        Assert.Equal(new[] { "0x200" }, set.AddressesOf("Character::Jump"));
        Assert.Single(set.Named());
    }

    [Fact]
    public void A_toggle_by_name_takes_every_class_and_key_of_that_name()
    {
        var set = new FunctionTickSet();
        var a = Row("Shop_C", "Open", "0x10", new NameKey(5, 0, 6, 0), parms: 16);
        var b = Row("Shop_C", "Open", "0x20", new NameKey(5, 0, 6, 1), parms: 40);   // another folder's Shop_C
        Assert.True(set.Toggle(a, new[] { a, b }));
        var named = Assert.Single(set.Named());
        Assert.Equal(2, named.Keys.Count);
        Assert.Equal(40, named.ParmsSize);
        Assert.Equal(2, set.LiveAddresses().Count);
        Assert.False(set.Toggle(b, new[] { a, b }));   // untick by name
        Assert.Equal(0, set.Count);
    }

    [Fact]
    public void Keys_only_grow_and_a_keyless_name_is_counted()
    {
        var set = new FunctionTickSet();
        var old = Row("A", "F", "0x1");                 // a DLL that predates keys
        set.Toggle(old, new[] { old });
        Assert.Equal(1, set.KeylessCount);
        Assert.Empty(set.Named());
        set.Refresh(new[] { Row("A", "F", "0x2", new NameKey(4, 0, 4, 0)) });
        Assert.Equal(0, set.KeylessCount);
        Assert.Equal(new[] { "0x2" }, set.AddressesOf("A::F"));
    }
}
