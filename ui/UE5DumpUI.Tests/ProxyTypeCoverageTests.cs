using System;
using System.Linq;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Guards against one failure mode: a proxy flavour exists in the enum but some
/// hand-maintained list elsewhere never learned about it, so a feature silently
/// stops covering it.
///
/// A double-inject guard (<c>Methode.cpp</c>, <c>UE5CEDumper.CT</c>) that tests a
/// hand-written set of proxy names leaves every flavour outside that set with no
/// guard at all. These tests make every C#-side list derive from <see cref="ProxyType"/>
/// and fail loudly if one is added by hand instead.
/// </summary>
public class ProxyTypeCoverageTests
{
    [Fact]
    public void Every_enum_value_maps_to_a_distinct_dll_name()
    {
        var names = Enum.GetValues<ProxyType>().Select(t => t.GetDllName()).ToArray();
        Assert.All(names, n => Assert.EndsWith(".dll", n, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void GetDllName_and_GetDisplayName_agree_for_every_value()
    {
        // Two independent switch statements; a new value added to only one of them
        // would fall through to the version.dll default and be invisible.
        foreach (var t in Enum.GetValues<ProxyType>())
            Assert.Equal(t.GetDllName(), t.GetDisplayName());
    }

    [Fact]
    public void FromDllName_round_trips_every_value()
    {
        // The reverse map is a chain of ifs — the easiest place to forget a flavour.
        foreach (var t in Enum.GetValues<ProxyType>())
        {
            Assert.Equal(t, ProxyTypeExtensions.FromDllName(t.GetDllName()));
            Assert.Equal(t, ProxyTypeExtensions.FromDllName(t.GetDllName().ToUpperInvariant()));
        }
    }

    [Fact]
    public void FromDllName_rejects_non_proxy_names()
    {
        Assert.Null(ProxyTypeExtensions.FromDllName("UE5Dumper.dll"));
        Assert.Null(ProxyTypeExtensions.FromDllName("kernel32.dll"));
        Assert.Null(ProxyTypeExtensions.FromDllName(""));
        Assert.Null(ProxyTypeExtensions.FromDllName(null));
    }

    [Fact]
    public void Module_detector_recognises_every_proxy_flavour()
    {
        // DumperModuleDetector derives its list from the enum; this proves it,
        // and would fail if someone replaced it with literals.
        foreach (var t in Enum.GetValues<ProxyType>())
            Assert.True(DumperModuleDetector.IsInterestingModuleName(t.GetDllName().ToLowerInvariant()),
                        $"module filter does not know {t.GetDllName()}");
    }

    [Fact]
    public void Module_detector_also_recognises_the_injected_dll_and_nothing_else()
    {
        Assert.True(DumperModuleDetector.IsInterestingModuleName("ue5dumper.dll"));
        Assert.False(DumperModuleDetector.IsInterestingModuleName("kernel32.dll"));
        Assert.False(DumperModuleDetector.IsInterestingModuleName("d3d11.dll"));
    }

    [Fact]
    public void Winmm_is_present_because_it_is_the_spare_slot()
    {
        // Not for coverage — its reach matches dxgi's. It exists so a
        // user whose dxgi.dll slot is taken (ReShade) and whose version.dll slot is
        // taken (a game shipping its own) still has a universally-viable choice.
        Assert.Equal("winmm.dll", ProxyType.Winmm.GetDllName());
        Assert.Equal(ProxyType.Winmm, ProxyTypeExtensions.FromDllName("winmm.dll"));
    }
}
