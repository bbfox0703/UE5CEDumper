using System.Globalization;
using System.Text;

namespace UE5DumpUI.Services;

/// <summary>
/// Turns the UE property names of one struct into the C++ member names its SDK header declares.
///
/// <para>A UE name is not a C++ identifier, and the header used to emit it verbatim. Each of these
/// was measured against <c>cl.exe</c> (<c>tools/verify/compile_sdk_header.py</c>) and rejected: a
/// keyword (<c>class</c> C2236, <c>default</c> C2321), a second member with the same name (C2086), a
/// Blueprint variable that kept the space it was typed with (C3646), a member that takes a generated
/// <c>Pad_XXXX</c> name (C2040), and a member named after a type the same struct spells bare
/// (<c>int32_t FName;</c> before <c>FName Tag;</c>, C2327 — GCC/Clang reject the other order too,
/// as a declaration that "changes meaning").</para>
///
/// <para>Renames follow Dumper-7's shape (<c>CollisionManager</c>): the first holder of a name keeps
/// it, the next gets <c>_0</c>, then <c>_1</c>. Members whose UE name is already a clean identifier
/// are served first, so a sanitised or renamed member never takes a real member's name.</para>
/// </summary>
internal static class SdkMemberNames
{
    /// <summary>C++20 keywords and alternative tokens: a member with one of these names is a syntax error.</summary>
    private static readonly HashSet<string> CppKeywords = new(StringComparer.Ordinal)
    {
        "alignas", "alignof", "and", "and_eq", "asm", "auto", "bitand", "bitor", "bool", "break",
        "case", "catch", "char", "char8_t", "char16_t", "char32_t", "class", "compl", "concept",
        "const", "consteval", "constexpr", "constinit", "const_cast", "continue", "co_await",
        "co_return", "co_yield", "decltype", "default", "delete", "do", "double", "dynamic_cast",
        "else", "enum", "explicit", "export", "extern", "false", "float", "for", "friend", "goto",
        "if", "inline", "int", "long", "mutable", "namespace", "new", "noexcept", "not", "not_eq",
        "nullptr", "operator", "or", "or_eq", "private", "protected", "public", "register",
        "reinterpret_cast", "requires", "return", "short", "signed", "sizeof", "static",
        "static_assert", "static_cast", "struct", "switch", "template", "this", "thread_local",
        "throw", "true", "try", "typedef", "typeid", "typename", "union", "unsigned", "using",
        "virtual", "void", "volatile", "wchar_t", "while", "xor", "xor_eq",
    };

    /// <summary>
    /// Names that are legal here but break the program a user builds around this header.
    /// </summary>
    private static readonly HashSet<string> SdkReserved = new(StringComparer.Ordinal)
    {
        // The static functions every UE SDK base class declares. A member of that name hides the
        // function, so T::StaticClass() stops compiling at its use site (Dumper-7 reserves them).
        "StaticClass", "StaticName", "GetDefaultObj",

        // Object-like macros from <windows.h> and the C headers, which an injector includes before
        // any SDK header: each expands to nothing, a number or a keyword, so `int32_t ERROR;` stops
        // being a declaration. Dumper-7's set plus the ones measured in Windows SDK 10.0.26100's
        // minwindef.h / winnt.h / wingdi.h. windows.h defines thousands more; a function-like macro
        // (min, max) does not expand without a following '(' and is not listed.
        "NULL", "TRUE", "FALSE", "IN", "OUT", "OPTIONAL", "CONST", "VOID", "DELETE", "ERROR",
        "NO_ERROR", "EVENT_MAX", "IGNORE",

        // Fixed-width typedefs. Padding, bitfields and raw-byte members spell uint8_t in nearly
        // every struct, so a member of that name clashes with a type the struct uses (C2327); UE's
        // own spellings are reserved for an SDK that uses them (Dumper-7 reserves them).
        "int8_t", "int16_t", "int32_t", "int64_t", "uint8_t", "uint16_t", "uint32_t", "uint64_t",
        "int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64",
    };

    /// <summary>
    /// A keyword, or a name the program around the header already owns (a macro, a typedef, a UE SDK
    /// function). <see cref="SdkTypeNames"/> reserves the same set for type names.
    /// </summary>
    internal static bool IsReservedIdentifier(string name) =>
        CppKeywords.Contains(name) || SdkReserved.Contains(name);

    /// <summary>
    /// The emitted name for each UE name, in the same order. <paramref name="typeSpellings"/> are
    /// the C++ type strings the struct's members are declared with: every identifier they spell
    /// WITHOUT a <c>struct</c> / <c>class</c> in front is a type name no member may take.
    /// <paramref name="layoutEnd"/> is where the struct's layout ends; no padding is generated at
    /// or past it.
    /// </summary>
    internal static string[] Assign(IReadOnlyList<string> ueNames, IEnumerable<string> typeSpellings,
                                    int layoutEnd)
    {
        var typeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spelling in typeSpellings)
            AddBareTypeNames(spelling, typeNames);

        // Every set here is finite -- the padding names included, which is why they are bounded by
        // layoutEnd rather than matched as a shape -- so the suffix loop below always finds a name.
        bool Reserved(string name) =>
            CppKeywords.Contains(name) || SdkReserved.Contains(name)
            || typeNames.Contains(name) || IsPaddingName(name, layoutEnd);

        var result = new string[ueNames.Count];
        var taken = new HashSet<string>(StringComparer.Ordinal);

        // Pass 1: a UE name that is already a clean identifier keeps it, first come first served.
        for (int i = 0; i < ueNames.Count; i++)
        {
            var name = ueNames[i] ?? "";
            if (MakeIdentifier(name) == name && !Reserved(name) && taken.Add(name))
                result[i] = name;
        }

        // Pass 2: everything else is sanitised, then suffixed past every name already handed out.
        for (int i = 0; i < ueNames.Count; i++)
        {
            if (result[i] is not null) continue;
            var baseName = MakeIdentifier(ueNames[i] ?? "");
            if (!Reserved(baseName) && taken.Add(baseName))
            {
                result[i] = baseName;
                continue;
            }
            for (int n = 0; ; n++)
            {
                var candidate = baseName + "_" + n.ToString(CultureInfo.InvariantCulture);
                if (!Reserved(candidate) && taken.Add(candidate))
                {
                    result[i] = candidate;
                    break;
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Every character that cannot continue a C++ identifier becomes <c>_</c>; a leading digit gets
    /// a <c>_</c> in front. Letters outside ASCII are kept, as Dumper-7 keeps them: the compile rig
    /// passes <c>/utf-8</c>, and a game's own names are not guaranteed ASCII.
    /// </summary>
    internal static string MakeIdentifier(string ueName)
    {
        if (string.IsNullOrEmpty(ueName)) return "Unnamed";

        var sb = new StringBuilder(ueName.Length + 1);
        bool first = true;
        foreach (var rune in ueName.EnumerateRunes())
        {
            if (first && Rune.IsDigit(rune)) sb.Append('_');
            first = false;

            if (rune.Value == '_' || Rune.IsLetterOrDigit(rune) || IsCombiningMark(rune))
                sb.Append(rune.ToString());
            else
                sb.Append('_');
        }
        return sb.ToString();
    }

    private static bool IsCombiningMark(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
    }

    /// <summary>
    /// True when the emitter could generate this padding name inside the layout: <c>Pad_</c> plus an
    /// offset below <paramref name="layoutEnd"/>, spelled exactly as <c>EmitPadding</c> spells it
    /// (<c>X4</c>). A UE member of that name is renamed whether or not padding lands at that
    /// offset, so the rule does not depend on the layout pass.
    /// </summary>
    private static bool IsPaddingName(string name, int layoutEnd)
    {
        if (!name.StartsWith("Pad_", StringComparison.Ordinal)) return false;
        var hex = name.AsSpan(4);
        return int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int offset)
            && offset >= 0 && offset < layoutEnd
            && hex.SequenceEqual(offset.ToString("X4", CultureInfo.InvariantCulture));
    }

    /// <summary>Adds the identifiers of one type string that are not preceded by <c>struct</c> / <c>class</c> / <c>enum</c>.</summary>
    private static void AddBareTypeNames(string spelling, HashSet<string> into)
    {
        string previous = "";
        int i = 0;
        while (i < spelling.Length)
        {
            char c = spelling[i];
            if (c == '_' || char.IsLetter(c))
            {
                int start = i;
                while (i < spelling.Length && (spelling[i] == '_' || char.IsLetterOrDigit(spelling[i]))) i++;
                var word = spelling[start..i];
                if (previous is not ("struct" or "class" or "enum") && !CppKeywords.Contains(word))
                    into.Add(word);
                previous = word;
                continue;
            }
            if (!char.IsWhiteSpace(c)) previous = "";
            i++;
        }
    }
}
