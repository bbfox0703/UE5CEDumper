using UE5DumpUI.Helpers;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Locks down the Object Tree "Instances only" predicate: it must hide the ENTIRE
/// reflection/type layer (not merely class-like metas) while keeping every live
/// gameplay instance. Getting the set wrong is the documented failure mode — dropping
/// only UClass-family rows would leave UFunction / UScriptStruct / UPackage / UEnum in
/// the result, defeating the filter's whole purpose.
/// </summary>
public class ReflectionMetaClassifierTests
{
    // ── Reflection metas are excluded (the full type layer, not just classes) ──────

    [Theory]
    // Class family (the class-like metas — mirrors DumpAllService.ClassLikeMetas)
    [InlineData("Class")]
    [InlineData("BlueprintGeneratedClass")]
    [InlineData("AnimBlueprintGeneratedClass")]
    [InlineData("WidgetBlueprintGeneratedClass")]
    [InlineData("DynamicClass")]
    // Function family — the headline gap a class-only filter would miss
    [InlineData("Function")]
    [InlineData("DelegateFunction")]
    [InlineData("SparseDelegateFunction")]
    // Struct / enum descriptors
    [InlineData("ScriptStruct")]
    [InlineData("UserDefinedStruct")]
    [InlineData("Enum")]
    [InlineData("UserDefinedEnum")]
    // Package
    [InlineData("Package")]
    public void IsReflectionMeta_TrueForTypeLayer(string className)
    {
        Assert.True(ReflectionMetaClassifier.IsReflectionMeta(className));
        Assert.False(ReflectionMetaClassifier.IsLiveInstanceRow(className));
    }

    [Theory]
    // UE4 keeps UProperty as a UObject, so property descriptors flood GObjects; their
    // class name always ends in "Property". These MUST be hidden on UE4 games.
    [InlineData("IntProperty")]
    [InlineData("FloatProperty")]
    [InlineData("BoolProperty")]
    [InlineData("ObjectProperty")]
    [InlineData("StructProperty")]
    [InlineData("ArrayProperty")]
    [InlineData("MapProperty")]
    [InlineData("EnumProperty")]
    [InlineData("MulticastInlineDelegateProperty")]
    [InlineData("FieldPathProperty")]
    public void IsReflectionMeta_TrueForUE4PropertyFamily(string className)
        => Assert.True(ReflectionMetaClassifier.IsReflectionMeta(className));

    // ── Live instances are kept ────────────────────────────────────────────────────

    [Theory]
    [InlineData("BP_Enemy_C")]        // Blueprint instance
    [InlineData("Character")]
    [InlineData("Pawn")]
    [InlineData("PlayerController")]
    [InlineData("Actor")]
    [InlineData("StaticMeshComponent")]
    [InlineData("AbilitySystemComponent")]
    [InlineData("GameplayAbility")]
    [InlineData("MyDataManager")]     // ends in "Manager", not "Property" → kept
    public void IsLiveInstanceRow_TrueForGameplayInstances(string className)
    {
        Assert.True(ReflectionMetaClassifier.IsLiveInstanceRow(className));
        Assert.False(ReflectionMetaClassifier.IsReflectionMeta(className));
    }

    // ── Edge cases / contract ────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NullOrEmpty_TreatedAsInstance_NotSilentlyHidden(string? className)
    {
        // A malformed / unnamed row must never be silently swallowed by the meta gate.
        Assert.False(ReflectionMetaClassifier.IsReflectionMeta(className));
        Assert.True(ReflectionMetaClassifier.IsLiveInstanceRow(className));
    }

    [Fact]
    public void Match_IsCaseSensitive_MirrorsUeExactCasing()
    {
        // UE emits meta names exactly cased ("Class", never "class"). Ordinal match keeps
        // the contract tight so an accidental lowercase never masks a real instance.
        Assert.True(ReflectionMetaClassifier.IsReflectionMeta("Class"));
        Assert.False(ReflectionMetaClassifier.IsReflectionMeta("class"));
    }

    [Fact]
    public void ClassDefaultObject_IsNotHidden_ByDesign()
    {
        // A CDO / archetype reports its GAME class as ClassName (e.g. Default__BP_Player_C
        // has ClassName "BP_Player_C"), so the meta gate — which only sees ClassName —
        // keeps it. The gate deliberately does not filter CDOs.
        Assert.True(ReflectionMetaClassifier.IsLiveInstanceRow("BP_Player_C"));
    }
}
