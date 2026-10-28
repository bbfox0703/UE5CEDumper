using System;
using System.Collections.Generic;

namespace UE5DumpUI.Helpers;

/// <summary>
/// Splits GObjects rows into <b>live gameplay instances</b> vs the <b>reflection /
/// type layer</b> (UClass, UFunction, UScriptStruct, UEnum, UPackage, and — on UE4,
/// where UProperty is still a UObject — the <c>FooProperty</c> family). Powers the
/// Object Tree's "Instances only" toggle so a global keyword search over the whole
/// loaded pool can hide the reflection noise it would otherwise drown in.
///
/// <para>Why this is NOT just the class-like meta set: dropping only
/// <c>Class</c>/<c>BlueprintGeneratedClass</c>/… (as <see cref="Services.DumpAllService.IsClassLikeMetaName"/>
/// / <c>Aura::IsClassLikeMeta</c> do) would still leave every <c>UFunction</c>,
/// <c>UScriptStruct</c>, <c>UPackage</c> and <c>UEnum</c> in the result — i.e. it would
/// fail at its headline job. The gate here excludes the <b>entire</b> reflection layer.</para>
///
/// <para><b>Matching field.</b> The DLL's <c>get_object_list</c> reports each object's
/// CLASS name (<c>obj->GetClass()->GetName()</c>). For a reflection object that class
/// name IS its meta-kind: a <c>UFunction</c> instance reports <c>"Function"</c>, a
/// <c>UScriptStruct</c> reports <c>"ScriptStruct"</c>, etc. — the same field
/// <see cref="Services.DumpAllService"/> keys on. So an exact-name set over
/// <c>ClassName</c> is the correct test.</para>
///
/// <para><b>Not filtered (by design):</b> Class Default Objects (<c>Default__Foo_C</c>)
/// and archetypes report their game class as <c>ClassName</c>, so they survive — they
/// ARE live UObjects a user may want to inspect. Filtering them needs a name/flag check
/// that Phase 1 deliberately leaves out.</para>
/// </summary>
public static class ReflectionMetaClassifier
{
    /// <summary>Exact <c>ClassName</c> values that denote a reflection/type-layer object
    /// rather than a gameplay instance. These are engine type names, never gameplay
    /// class names, so an exact match is safe. Keep the class-family entries in sync with
    /// <see cref="Services.DumpAllService"/>'s <c>ClassLikeMetas</c> / <c>Aura::IsClassLikeMeta</c>.</summary>
    private static readonly HashSet<string> ReflectionMetas = new(StringComparer.Ordinal)
    {
        // Class family (mirrors DumpAllService.ClassLikeMetas / Aura::IsClassLikeMeta)
        "Class",
        "BlueprintGeneratedClass",
        "AnimBlueprintGeneratedClass",
        "WidgetBlueprintGeneratedClass",
        "DynamicClass",
        // Function family (UFunction and its delegate flavours)
        "Function",
        "DelegateFunction",
        "SparseDelegateFunction",
        // Struct descriptors (UScriptStruct + BP-defined structs)
        "ScriptStruct",
        "UserDefinedStruct",
        // Enum descriptors (UEnum + BP-defined enums)
        "Enum",
        "UserDefinedEnum",
        // Package
        "Package",
    };

    /// <summary>
    /// True when <paramref name="className"/> names a reflection/type-layer object
    /// (UClass, UFunction, UScriptStruct, UEnum, UPackage, and on UE4 the
    /// <c>FooProperty</c> family) rather than a live gameplay instance. Null / empty
    /// class names are treated as NOT reflection (kept), so a malformed row is never
    /// silently hidden.
    /// </summary>
    public static bool IsReflectionMeta(string? className)
    {
        if (string.IsNullOrEmpty(className)) return false;
        if (ReflectionMetas.Contains(className)) return true;
        // UE4 (a priority target) keeps UProperty as a UObject, so property descriptors
        // sit in GObjects; their class name is the property type and always ends in
        // "Property" (IntProperty, ObjectProperty, StructProperty, ArrayProperty,
        // EnumProperty, …). UE5 makes FProperty non-UObject, so this suffix never fires
        // there. "Property" is a UHT-generated engine convention — gameplay classes
        // don't use it — so the suffix test is safe.
        if (className.EndsWith("Property", StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// True when <paramref name="className"/> denotes a live gameplay instance row
    /// (the inverse of <see cref="IsReflectionMeta"/>). This is the predicate the Object
    /// Tree's "Instances only" filter applies to every row of the fully-loaded pool.
    /// </summary>
    public static bool IsLiveInstanceRow(string? className) => !IsReflectionMeta(className);

    /// <summary>[CEXML-DEFINITION-VIEW-AS-DATA] The metaclasses <c>Ubel::WalkInstance</c> answers with a DEFINITION view:
    /// a walk of a UScriptStruct / UClass object returns the TYPE's member layout, whose offsets describe instances of
    /// the type, not the type object, so nothing may read or write them as data at that address. A copy of the DLL's
    /// own list, because an older DLL's lean walk drops <c>is_definition</c> and the pointing field's class name is then
    /// all a resolver has; <c>DefinitionViewExportTests</c> pins it to Ubel.cpp.</summary>
    private static readonly HashSet<string> DefinitionMetas = new(StringComparer.Ordinal)
    {
        "ScriptStruct",
        "Class",
        "BlueprintGeneratedClass",
        "WidgetBlueprintGeneratedClass",
    };

    internal static IReadOnlyCollection<string> DefinitionViewMetas => DefinitionMetas;

    /// <summary>True when an object of class <paramref name="className"/> walks as a definition view -- a pointer to it
    /// points at a type, and drilling it would lay the type's layout out as the pointee's data.</summary>
    public static bool WalksAsDefinition(string? className)
        => !string.IsNullOrEmpty(className) && DefinitionMetas.Contains(className);
}
