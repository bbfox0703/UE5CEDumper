using System.Text;
using System.Threading;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;

namespace UE5DumpUI.Services;

/// <summary>
/// Generates Cheat Engine XML address records using hierarchical nested format.
///
/// CE XML address resolution rules (hierarchical tree model):
/// - Root node: absolute address "Module.exe"+RVA
/// - Each child's Address is relative to its parent's RESOLVED address
/// - Pointer field: &lt;Address&gt;+{offset}&lt;/Address&gt; with &lt;Offsets&gt;&lt;Offset&gt;0&lt;/Offset&gt;&lt;/Offsets&gt;
///   → CE resolves to *(parentAddr + offset), children offset from the dereferenced value
/// - Inline field (scalar/struct): &lt;Address&gt;+{offset}&lt;/Address&gt; (no Offsets, no dereference)
/// - GroupHeader=1 makes an entry a collapsible folder with children
///
/// CE type mapping:
/// - Signed integers (IntProperty, Int8/16/64Property): ShowAsSigned=1
/// - Unsigned integers (UInt32/16/64Property, ByteProperty): ShowAsSigned=0
/// - BoolProperty with bit mask: VariableType=Binary, BitStart/BitLength from UE FieldMask
/// - Pointer fields (ObjectProperty navigable): ShowAsHex=1, GroupHeader placeholder
/// - Struct fields (StructProperty): real field names via DLL resolution, flattened nested structs
///
/// Struct expansion:
/// - StructProperty fields are resolved via WalkInstanceAsync to get real field names/types
/// - Nested StructProperty are recursively flattened (all inner fields at the same level)
/// - Pointer fields inside structs emit as 8 Bytes ShowAsHex placeholder
/// - Max recursion depth: 5 levels
/// </summary>
public static class CeXmlExportService
{
    // NOTE: _nextId is reset at the start of each Generate* method call,
    // so concurrent calls are safe as long as each completes atomically.
    // Using ThreadStatic to eliminate any cross-thread risk.
    [ThreadStatic]
    private static int _nextId;

    /// <summary>Max depth for recursive struct resolution.</summary>
    private const int MaxStructDepth = 5;

    /// <summary>Max entries for a CE DropDownList. Lists exceeding this are omitted.</summary>
    [ThreadStatic]
    private static int _maxDropDownEntries;

    /// <summary>
    /// CE String leaf display length — the &lt;Length&gt; window CE reads for a String
    /// field. Set per Generate* entry from the user's "String Length" export option
    /// (default 256, floored at 16 by the toolbar slider). Read by
    /// <see cref="EmitStringLeaf"/>; 0 (unset) falls back to 256. Because the value is a
    /// fixed read window (with ZeroTerminate=1 CE still stops at the null), a generous
    /// length never truncates a shorter live string — it only guards strings that later
    /// grow. Copy CE XML / Copy CE Field only (CSX uses its own Bytesize).
    /// </summary>
    [ThreadStatic]
    private static int _ceStringLength;

    /// <summary>
    /// Copy CE Field "Fabricate empty array slots" target count (0 = off, the default and
    /// the Copy CE XML value). When &gt; 0, a selected <c>TArray</c> is emitted with
    /// <c>max(count, walkedCount)</c> element rows (capped at <see cref="MaxFabricateElements"/>):
    /// slots the live save hasn't populated (null object pointers, or indices beyond the
    /// current Num) are FABRICATED so the CE table already has room for items a later save
    /// will hold. Object arrays replicate a resolved element's field layout (homogeneous-class
    /// assumption); scalar / all-null arrays extend the flat element leaf. TArray-only — Map/Set
    /// are sparse (free-list slots aren't future elements) and are never fabricated. Set per
    /// Generate* entry; only the Copy CE Field call sites pass a non-zero value.
    /// </summary>
    [ThreadStatic]
    private static int _fabricateArrayCount;

    /// <summary>Hard ceiling on fabricated element rows per array — a backstop beyond the
    /// UI slider's max, so a huge requested count can't alone blow the entry budget.</summary>
    private const int MaxFabricateElements = 4096;

    /// <summary>
    /// Fabrication (padding a TArray past its live element count) applies ONLY to a TOP-LEVEL
    /// selected array — a direct field of the walked object — NOT to an array reached by
    /// drilling THROUGH an object pointer. Without this "next layer only" gate, a deep element
    /// template (e.g. an inventory item's own Attributes / Catalysts arrays) would ALSO be
    /// fabricated to the target count at every nesting level, exploding the output
    /// combinatorially (observed in-game: Fabricate=256 on a 234-item Cargo produced 500k+
    /// XML lines — 256 items × 256 fabricated Attributes × … — which Cheat Engine couldn't
    /// load). <see cref="_emitPointerDepth"/> is 0 until <see cref="EmitDrilledPointer"/>
    /// crosses the first object boundary, so it cleanly distinguishes the selected array from
    /// arrays inside drilled sub-objects. (Inline structs on the selected object do NOT cross a
    /// pointer, so an array inside such a struct still counts as top-level.)
    /// </summary>
    private static bool FabricateActive => _fabricateArrayCount > 0 && _emitPointerDepth == 0;

    /// <summary>
    /// Tracks emitted DropDownList owners by UEnum address → parent group's Description.
    /// Reset per Generate* call. Enables DropDownListLink sharing for same-enum arrays.
    /// </summary>
    [ThreadStatic]
    private static Dictionary<string, string>? _dropDownOwners;

    /// <summary>
    /// Tracks emitted DropDownList parent descriptions to ensure uniqueness.
    /// CE uses Description text as DropDownListLink key, so duplicates cause ambiguity.
    /// If a duplicate is found, ".001", ".002" etc. suffix is appended.
    /// </summary>
    [ThreadStatic]
    private static HashSet<string>? _dropDownDescriptions;

    /// <summary>
    /// When true, every non-root GroupHeader folder (pointer/array/map/set deref
    /// nodes, struct groups, AND array/map/set element folders such as
    /// <c>[1]</c>) emits &lt;Options moHideChildren="1"
    /// moDeactivateChildrenAsWell="1"/&gt; to collapse it by default in Cheat
    /// Engine. The root node is excluded (its address is absolute, not "+...",
    /// so it stays expanded).
    /// </summary>
    [ThreadStatic]
    private static bool _collapsePointerNodes;

    /// <summary>
    /// When false (default), EmitFields SKIPS guessed (.IsGuessed) fields — so bulk
    /// exports (Copy CE XML, and Copy CE Field on a struct/container/reflected field)
    /// never dump the speculative "Guess?" rows, including nested guessed fields that
    /// surface during struct/pointer drilldown. Set true ONLY by the lone-focused-
    /// guessed-field Copy CE Field path, so a single guessed field the user explicitly
    /// targeted still exports (a guessed field is always a scalar leaf, so true only
    /// ever applies to a childless top-level field — recursion never sees it).
    /// </summary>
    [ThreadStatic]
    private static bool _includeGuessed;

    /// <summary>
    /// Opt-in description decorations (user toggles in the Live Walker export
    /// options dropdown). When off (the default) a memory-record Description is
    /// just the node's bare Name. _descShowOffset appends the node's own +offset
    /// (hex, no prefix); _descShowType appends the node's class / struct / element
    /// type. Both can combine: "Name (7E0, ClassName)". Set at the top of each
    /// Generate* entry point alongside the other ThreadStatic state. The folded
    /// chain (Collapse chain) honours offset but never type — see DecorateDesc's
    /// allowType.
    /// </summary>
    [ThreadStatic]
    private static bool _descShowOffset;
    [ThreadStatic]
    private static bool _descShowType;

    /// <summary>
    /// Path-based cycle detection for drilled pointer emit. Holds the PtrAddresses
    /// currently on the emit stack — we push on entry into EmitDrilledPointer and
    /// pop on exit. If a target appears in this set, the pointer is a back-edge
    /// (e.g. UWorld -&gt; PersistentLevel -&gt; OwningWorld) and must NOT be re-emitted
    /// as a group, otherwise the StringBuilder explodes (observed: 2GB capacity
    /// hit on DQ I&amp;II HD-2D with Drill Depth = 2).
    ///
    /// ResolvePointerInstancesAsync's visited set protects the *resolve* phase;
    /// the *emit* phase is independent and needs its own protection.
    /// </summary>
    [ThreadStatic]
    private static HashSet<string>? _emitPath;

    /// <summary>
    /// Opt-in shared-object dedup (default on for the Live Walker exports). The
    /// drilldown resolves each distinct object's fields ONCE (resolvedInstances is
    /// keyed by PtrAddress), but the emit phase re-expands a shared object's whole
    /// subtree under EVERY parent that points to it — on a dense graph (a field that
    /// reaches GWorld → Actors[…]) that unrolls into billions of duplicate entries.
    /// With dedup on, each object's subtree is emitted the FIRST time it's reached and
    /// every later reference becomes a flat pointer leaf marked "(shared)". Unlike
    /// _emitPath (push/pop, per-path) this set is global to the whole export and never
    /// cleared mid-walk, so it also subsumes cycle protection.
    /// </summary>
    [ThreadStatic]
    private static bool _dedupShared;
    [ThreadStatic]
    private static HashSet<string>? _emittedInstances;

    /// <summary>
    /// Hard ceiling on EmitDrilledPointer recursion depth — covers the rare case
    /// where ResolvePointerInstancesAsync produced a long but acyclic chain that
    /// would still trigger an XML blow-up. Set generously so legitimate trees
    /// (depth 4 + cascade) fit comfortably; the cycle protection above is the
    /// primary line of defence.
    /// </summary>
    private const int MaxEmitPointerDepth = 16;

    [ThreadStatic]
    private static int _emitPointerDepth;

    /// <summary>
    /// Global safety ceiling on the number of CE entries one Generate* call may emit.
    /// A densely-connected object graph (a field that reaches GWorld → PersistentLevel
    /// → Actors[…] → components → …) makes the drilldown re-expand shared sub-objects
    /// combinatorially — the per-path cycle guard (_emitPath) and per-pointer depth cap
    /// (MaxEmitPointerDepth) don't bound the BREADTH, so the StringBuilder previously
    /// grew until OutOfMemory (Copy CE XML on a full character view). Once this many
    /// entries are emitted the recursion stops and the export is flagged truncated.
    /// Generous enough that any legitimate single-object export fits; only a runaway
    /// deep-drill on a dense graph trips it.
    /// </summary>
    private const int MaxEmitEntries = 60_000;

    [ThreadStatic]
    private static int _emitEntryCount;
    [ThreadStatic]
    private static bool _emitTruncated;

    /// <summary>
    /// True when the most recent Generate* call hit <see cref="MaxEmitEntries"/> and
    /// stopped emitting early (the export is incomplete). The caller reads this right
    /// after the synchronous Generate* call (same thread) to warn the user.
    /// </summary>
    public static bool LastExportTruncated => _emitTruncated;

    /// <summary>
    /// Opt-in (default on at the Live Walker call sites): skip system/engine asset
    /// fields (Widget, SoundBase, Texture, Material, ParticleSystem, Niagara,
    /// AnimInstance …) encountered while RECURSIVELY expanding a CE export. Set per
    /// Generate* entry; the gate itself lives in <see cref="EmitFields"/> and only
    /// fires for children (emit depth &gt; 1), never the top-level user-selected fields.
    /// </summary>
    [ThreadStatic]
    private static bool _excludeSystemComponents;

    /// <summary>
    /// Opt-in (Live Walker "Flatten GAS attributes" toggle, default off): collapse a
    /// GAS <c>FGameplayAttributeData</c> struct (UE reflection name "GameplayAttributeData")
    /// by ONE level — instead of a parent group at +structOffset with BaseValue/CurrentValue
    /// children, promote each scalar child to a sibling leaf named "{Struct} ▸ {Child}" at
    /// the COMBINED offset (+structOff+childOff). Scoped strictly to that struct type and
    /// only when every child maps to a scalar CE leaf; any other struct keeps its group.
    /// Honoured in <see cref="EmitFields"/> (the shared StructProperty branch), so it applies
    /// uniformly to Copy CE XML and Copy CE Field, at any nesting depth.
    /// </summary>
    [ThreadStatic]
    private static bool _flattenGasAttributes;

    /// <summary>
    /// Opt-in (Live Walker "Flatten primitive-leaf structs" toggle, default off): collapse
    /// ANY terminal StructProperty by ONE level — the same promotion as the GAS flatten, but
    /// not restricted to a known struct type. A struct qualifies ONLY when its entire flattened
    /// subtree is made of <see cref="IsPrimitiveLeafField">primitive inline scalars</see>
    /// (float/double, int8–64, byte/uint16–64, bool, enum). If ANY descendant is a pointer/object,
    /// a string (FString/FName/FText — pointer-backed), a container (Array/Map/Set/Optional), or an
    /// unresolved nested struct, the struct keeps its nested group. This is a SUPERSET of the GAS
    /// flatten (an FGameplayAttributeData is a {float,float} primitive-leaf struct) and a natural
    /// fit for FVector/FRotator/FTransform (pure-float structs). A Vector reached through a pointer
    /// is never on this path, so pointer-shaped vectors are left untouched. Honoured in
    /// <see cref="EmitFields"/> alongside <see cref="_flattenGasAttributes"/>; Copy CE XML / Copy CE
    /// Field only (CSX is deliberately not affected).
    /// </summary>
    [ThreadStatic]
    private static bool _flattenLeafStructs;

    /// <summary>
    /// Opt-in (Live Walker "Flatten leaf records" toggle, default off): a STRICT SUPERSET of
    /// <see cref="_flattenLeafStructs"/>. Collapse ANY terminal StructProperty by ONE level when
    /// its entire flattened subtree is made of <see cref="IsTerminalLeafField">terminal leaves</see>
    /// — i.e. the primitive scalars/enum that primitive-leaf accepts PLUS <c>NameProperty</c> and the
    /// FString family (<c>StrProperty</c>/<c>Utf8StrProperty</c>/<c>AnsiStrProperty</c>). Designed for
    /// save-data "record" structs (e.g. {int Score, int Time, ERankID Rank, FName MsID, FString
    /// PilotName}) so each field becomes a sibling leaf instead of a per-struct group. NO field-count
    /// cap (the all-leaf gate is the safety). A name child renders as today's 4-byte int leaf; an
    /// FString child renders as a CE String leaf with <c>Offsets=[0]</c> (one deref of the FString.Data
    /// pointer) — see <see cref="EmitFlattenedStruct"/>. Pointers/objects, FText, containers, and
    /// unresolved nested structs still keep the struct grouped. Auto-applies to TMap/TArray element
    /// structs (they route through <see cref="EmitFields"/>). Copy CE XML / Copy CE Field only
    /// (CSX deliberately unaffected).
    /// </summary>
    [ThreadStatic]
    private static bool _flattenLeafRecords;

    /// <summary>
    /// Opt-in (Live Walker "Flatten Record Colors" dialog): tint flattened container-element rows
    /// by element-index parity so the records stay visually separable in CE once the per-element
    /// group boundary is gone. <see cref="_altColorEven"/> / <see cref="_altColorOdd"/> are CE
    /// COLORREF (BBGGRR) strings or null (= no colour for that parity). Applied only to flattened
    /// TMap / TArray element rows (<see cref="AltRowColor"/>); ordinary leaves are never coloured.
    /// </summary>
    [ThreadStatic]
    private static bool _altColorEnabled;
    [ThreadStatic]
    private static string? _altColorEven;
    [ThreadStatic]
    private static string? _altColorOdd;

    /// <summary>The CE colour to stamp on the leaf currently being emitted, or null. Set/cleared
    /// around each flattened container element; read by <see cref="EmitRowColor"/>.</summary>
    [ThreadStatic]
    private static string? _curRowColor;

    /// <summary>
    /// Opt-in (Live Walker "Collapse single-leaf pointers" toggle, default off): when a drilled
    /// pointer (Object/Class/Weak/Soft/Lazy/Interface) resolves to a target with EXACTLY ONE
    /// terminal-leaf field (a scalar / FName / FString), collapse the group + lone child into ONE
    /// CE record at the pointer field with a deref chain instead of a folder. The original
    /// "pointer-to-string" case: a pointer whose only payload is a single string is two CE nodes for
    /// one value. Encodings (<see cref="EmitOneDerefLeaf"/>): scalar/name child → Address=+ptrOff,
    /// Offsets=[childOff] (1 deref); FString child → Address=+ptrOff, Offsets=[0, childOff]
    /// (2 derefs: the pointer, then the FString.Data buffer). A multi-field pointee keeps its group
    /// (its object identity is worth a boundary). Honoured in <see cref="EmitDrilledPointer"/>.
    /// </summary>
    [ThreadStatic]
    private static bool _collapseLeafPointers;

    /// <summary>
    /// Current <see cref="EmitFields"/> nesting depth (1 = the top-level user-selected
    /// fields, &gt;1 = recursively-resolved struct / pointer children). The noise filter
    /// keys off this so it never drops a field the user explicitly put on screen.
    /// </summary>
    [ThreadStatic]
    private static int _emitDepth;

    /// <summary>Count of fields the noise filter skipped during the most recent
    /// Generate* call — surfaced to the user as an "N system fields hidden" note.</summary>
    [ThreadStatic]
    private static int _systemFieldsSkipped;

    /// <summary>Number of system/engine fields the noise filter dropped in the most
    /// recent Generate* call (read same-thread right after, like
    /// <see cref="LastExportTruncated"/>).</summary>
    public static int LastSystemFieldsSkipped => _systemFieldsSkipped;

    /// <summary>
    /// Per-call resolved-field dictionaries, mirrored into thread-static state so
    /// the container emitters (EmitMapProperty / EmitSetProperty / struct-array)
    /// can expand element VALUES that are structs/objects by delegating to
    /// EmitFields — without threading the dicts through every emit signature.
    /// Set at the top of each Generate* entry point; keyed by StructDataAddr /
    /// PtrAddress respectively (same keys ResolveDrilldownAsync populates).
    /// </summary>
    [ThreadStatic]
    private static Dictionary<string, List<LiveFieldValue>>? _resolvedStructsState;
    [ThreadStatic]
    private static Dictionary<string, List<LiveFieldValue>>? _resolvedInstancesState;

    /// <summary>CE field metadata for XML generation.</summary>
    private record CeFieldInfo(
        string VariableType,
        bool IsSigned = false,
        bool ShowAsHex = false,
        int BitStart = -1,
        int BitLength = 0);

    // ========================================
    // Struct field resolution (async, requires DLL pipe)
    // ========================================

    /// <summary>
    /// Pre-resolve all StructProperty fields by walking their inner structure via the DLL.
    /// Returns a dictionary keyed by field offset, containing flattened inner fields
    /// with relative offsets from the struct start and dot-prefixed names for nested structs.
    ///
    /// Example: StructA at offset 0x100 with inner StructB at +0x10 containing X at +0x0
    ///   -> resolvedStructs[0x100] = [
    ///        LiveFieldValue { Name="IntField", Offset=0x0 },
    ///        LiveFieldValue { Name="StructB.X", Offset=0x10 },
    ///        LiveFieldValue { Name="StructB.Y", Offset=0x14 },
    ///      ]
    /// </summary>
    /// <summary>
    /// Pre-resolve ObjectProperty / ClassProperty / WeakObjectProperty / Soft* / Lazy* /
    /// Interface* targets so the CE XML emitter can drop GroupHeader+Offsets=[0] children
    /// onto the pointer leaf, mirroring the same drilldown the CSX exporter ships
    /// (CsxExportService.ResolvePointerInstancesAsync). The result is keyed by PtrAddress
    /// so the emit-time lookup is O(1) per field.
    ///
    /// Cascades StructProperty resolution into <paramref name="resolvedStructs"/> for
    /// every drilled target's fields too — without that, drilled children with
    /// StructProperty (e.g. <c>PrimaryComponentTick (ActorComponentTickFunction)</c>
    /// inside a UComponent) would render as empty GroupHeader placeholders even
    /// though the user asked for full drill-down.
    ///
    /// Recurses into resolved targets up to <paramref name="depth"/>; uses a
    /// shared visited set for cycle detection. Returns empty when depth &lt;= 0.
    /// </summary>
    public static async Task<Dictionary<string, List<LiveFieldValue>>> ResolvePointerInstancesAsync(
        IDumpService dump,
        IReadOnlyList<LiveFieldValue> fields,
        int depth,
        int arrayLimit = 64,
        Dictionary<string, List<LiveFieldValue>>? resolvedStructs = null,
        bool lean = false)
    {
        var resolved = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        if (depth <= 0) return resolved;

        var visited = new HashSet<string>(StringComparer.Ordinal);
        await ResolvePointerInstancesRecursiveAsync(
            dump, fields, resolved, depth, arrayLimit, visited, resolvedStructs, lean);
        return resolved;
    }

    private static async Task ResolvePointerInstancesRecursiveAsync(
        IDumpService dump,
        IReadOnlyList<LiveFieldValue> fields,
        Dictionary<string, List<LiveFieldValue>> resolved,
        int remainingDepth,
        int arrayLimit,
        HashSet<string> visited,
        Dictionary<string, List<LiveFieldValue>>? resolvedStructs,
        bool lean)
    {
        if (remainingDepth <= 0) return;

        // Collect this LEVEL's pointer targets first, then walk them in ONE batched
        // call, then recurse. Previously each field was a separate round-trip, which
        // is where a Copy CE XML's 20,357 walk_instance calls came from — and the
        // measured split (multipipe-eval.md §10.4) showed 59-73% of that export's
        // wall-clock was pure round-trip overhead, ~2x the actual walking.
        //
        // Breadth-first per level is what makes batching possible at all: every
        // target at one depth is independent, so they can be requested together.
        // The visited/resolved guards stay HERE (not inside the batch) so cycle
        // protection and dedup behave exactly as before.
        var pending = new List<(string Addr, string? ClassAddr)>();
        foreach (var field in fields)
        {
            if (!IsObjectPropertyType(field.TypeName)) continue;
            if (string.IsNullOrEmpty(field.PtrAddress) || field.PtrAddress == "0x0") continue;
            if (resolved.ContainsKey(field.PtrAddress)) continue;
            if (!visited.Add(field.PtrAddress)) continue; // cycle protection
            pending.Add((field.PtrAddress, field.PtrClassAddr));
        }
        if (pending.Count == 0) return;

        IReadOnlyList<InstanceWalkResult> walked;
        try
        {
            walked = await dump.WalkInstanceBatchAsync(pending, arrayLimit, lean: lean);
        }
        catch
        {
            // Whole-batch failure is already handled per-chunk inside the service;
            // reaching here means something broader went wrong. Same behaviour as
            // before: skip the branch, pointers fall back to flat hex leaves.
            return;
        }

        for (int i = 0; i < pending.Count && i < walked.Count; i++)
        {
            var result = walked[i];
            if (result.Fields.Count == 0) continue;   // reclaimed / bad target

            try
            {
                resolved[pending[i].Addr] = result.Fields;

                // Cascade struct resolution so the drilled target's
                // StructProperty children expand to real sub-fields,
                // not empty GroupHeader placeholders.
                if (resolvedStructs != null)
                {
                    await ResolveStructFieldsIntoAsync(
                        dump, result.Fields, resolvedStructs, arrayLimit, lean);
                }

                // Recurse one level deeper for nested pointers in the resolved target
                await ResolvePointerInstancesRecursiveAsync(
                    dump, result.Fields, resolved, remainingDepth - 1,
                    arrayLimit, visited, resolvedStructs, lean);
            }
            catch
            {
                // Pipe error / target reclaimed — skip this branch quietly,
                // pointer falls back to a flat 8 Bytes hex leaf in the emit step.
            }
        }
    }

    /// <summary>
    /// Object/Class pointer family — same set CsxExportService treats as drilldown-eligible.
    /// </summary>
    private static bool IsObjectPropertyType(string typeName) => typeName is
        "ObjectProperty" or "ClassProperty" or "WeakObjectProperty" or
        "SoftObjectProperty" or "SoftClassProperty" or "LazyObjectProperty" or
        "InterfaceProperty";

    // Array inner types whose element slot holds a RAW 8-byte UObject* — so a
    // drilled element can dereference it with Offsets=[0]. This is a STRICT subset
    // of IsObjectPropertyType and matches the DLL's Ubel::IsPointerArrayType:
    // Weak (FWeakObjectPtr {ObjectIndex, SerialNumber}), Soft (FSoftObjectPath) and
    // Lazy (FGuid) elements are NOT raw pointers even though the DLL resolves them
    // to a live UObject* — dereferencing their slot would land CE at a garbage
    // address, so they must keep their existing leaf / Phase-G handling.
    private static bool IsRawObjectPtrArrayInner(string innerType) =>
        innerType is "ObjectProperty" or "ClassProperty";

    // ========================================
    // Unified drilldown resolver (docs/ce-export-drilldown-spec.md Phase A)
    // ========================================

    /// <summary>
    /// One recursive pass that resolves everything the emitter needs to expand:
    /// (1) StructProperty fields (flattened, depth-free), (2) ObjectProperty
    /// pointer targets (cost 1 level), and (3) CONTAINER ELEMENT VALUES that are
    /// structs/objects (Map values, Set elements, struct-array elements — cost 1
    /// level), recursing into each so nested containers expand too. Populates
    /// <paramref name="resolvedStructs"/> (keyed by StructDataAddr) and
    /// <paramref name="resolvedInstances"/> (keyed by PtrAddress) — the same keys
    /// the emit phase looks up. Replaces the separate ResolveStructFieldsAsync +
    /// ResolvePointerInstancesAsync calls for CE XML export.
    /// </summary>
    public static async Task ResolveDrilldownAsync(
        IDumpService dump,
        IReadOnlyList<LiveFieldValue> fields,
        Dictionary<string, List<LiveFieldValue>> resolvedStructs,
        Dictionary<string, List<LiveFieldValue>> resolvedInstances,
        int depth,
        int arrayLimit = 64,
        Action? onWalk = null,
        bool lean = false,
        CancellationToken ct = default)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        await ResolveDrilldownRecAsync(dump, fields, resolvedStructs, resolvedInstances,
            depth, arrayLimit, visited, onWalk, lean, ct);
    }

    private static async Task ResolveDrilldownRecAsync(
        IDumpService dump,
        IReadOnlyList<LiveFieldValue> fields,
        Dictionary<string, List<LiveFieldValue>> resolvedStructs,
        Dictionary<string, List<LiveFieldValue>> resolvedInstances,
        int depth,
        int arrayLimit,
        HashSet<string> visited,
        Action? onWalk,
        bool lean,
        CancellationToken ct)
    {
        // Abort promptly between pipe round-trips when the user cancels the export.
        ct.ThrowIfCancellationRequested();

        // (1) Structs at this level — flatten nested (depth-free, MaxStructDepth-bound),
        //     then descend into each resolved struct's own fields (still depth-free) so
        //     containers/pointers INSIDE the struct are reached.
        await ResolveStructFieldsIntoAsync(dump, fields, resolvedStructs, arrayLimit, lean, ct);
        onWalk?.Invoke();
        foreach (var f in fields)
        {
            if (f.TypeName is not ("StructProperty" or "OptionalProperty")) continue;
            if (string.IsNullOrEmpty(f.StructDataAddr)) continue;
            if (!resolvedStructs.TryGetValue(f.StructDataAddr, out var sub)) continue;
            if (!visited.Add("S:" + f.StructDataAddr)) continue;
            await ResolveDrilldownRecAsync(dump, sub, resolvedStructs, resolvedInstances,
                depth, arrayLimit, visited, onWalk, lean, ct);
        }

        if (depth <= 0) return;

        // (2) Pointers — cost 1 level.
        foreach (var f in fields)
        {
            if (!IsObjectPropertyType(f.TypeName)) continue;
            if (string.IsNullOrEmpty(f.PtrAddress) || f.PtrAddress == "0x0") continue;
            if (resolvedInstances.ContainsKey(f.PtrAddress)) continue;
            if (!visited.Add("P:" + f.PtrAddress)) continue;
            await WalkAndRecurseAsync(dump, f.PtrAddress, f.PtrClassAddr, resolvedStructs,
                resolvedInstances, depth - 1, arrayLimit, visited, onWalk, lean, ct);
        }

        // (3) Container element VALUES (struct + object) — cost 1 level.
        foreach (var f in fields)
        {
            var valueFields = BuildContainerValueFields(f);
            if (valueFields.Count == 0) continue;

            var structVals = valueFields
                .Where(v => v.TypeName is "StructProperty"
                            && !string.IsNullOrEmpty(v.StructDataAddr)
                            && !string.IsNullOrEmpty(v.StructClassAddr))
                .ToList();
            if (structVals.Count > 0)
            {
                await ResolveStructFieldsIntoAsync(dump, structVals, resolvedStructs, arrayLimit, lean, ct);
                onWalk?.Invoke();
                foreach (var sv in structVals)
                {
                    if (!resolvedStructs.TryGetValue(sv.StructDataAddr, out var sub)) continue;
                    if (!visited.Add("S:" + sv.StructDataAddr)) continue;
                    await ResolveDrilldownRecAsync(dump, sub, resolvedStructs, resolvedInstances,
                        depth - 1, arrayLimit, visited, onWalk, lean, ct);
                }
            }

            foreach (var ov in valueFields)
            {
                if (!IsObjectPropertyType(ov.TypeName)) continue;
                if (string.IsNullOrEmpty(ov.PtrAddress) || ov.PtrAddress == "0x0") continue;
                if (resolvedInstances.ContainsKey(ov.PtrAddress)) continue;
                if (!visited.Add("P:" + ov.PtrAddress)) continue;
                await WalkAndRecurseAsync(dump, ov.PtrAddress, ov.PtrClassAddr, resolvedStructs,
                    resolvedInstances, depth - 1, arrayLimit, visited, onWalk, lean, ct);
            }
        }
    }

    private static async Task WalkAndRecurseAsync(
        IDumpService dump, string ptrAddr, string ptrClassAddr,
        Dictionary<string, List<LiveFieldValue>> resolvedStructs,
        Dictionary<string, List<LiveFieldValue>> resolvedInstances,
        int depth, int arrayLimit, HashSet<string> visited, Action? onWalk, bool lean,
        CancellationToken ct)
    {
        try
        {
            var r = await dump.WalkInstanceAsync(ptrAddr, ptrClassAddr, arrayLimit, lean: lean, ct: ct);
            if (r.Fields.Count > 0)
            {
                resolvedInstances[ptrAddr] = r.Fields;
                onWalk?.Invoke();
                await ResolveDrilldownRecAsync(dump, r.Fields, resolvedStructs,
                    resolvedInstances, depth, arrayLimit, visited, onWalk, lean, ct);
            }
        }
        // Let a cancel abort the whole export; only real pipe/target failures fall through
        // to a leaf. TaskCanceledException derives from OperationCanceledException, so this
        // single guard covers both.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Pipe error / reclaimed target — leave unresolved; emit falls back to a leaf.
        }
    }

    /// <summary>
    /// Build synthetic value fields for a container's struct/object element VALUES,
    /// each carrying the value's absolute <c>StructDataAddr</c> (struct) or
    /// <c>PtrAddress</c> (object) for the resolver to walk. Scalar values are
    /// skipped (they emit as plain leaves). The absolute address formulas match
    /// the emitters (and PopulateMapContainerFields) exactly, so the resolver's
    /// keys line up with the emit-time lookups.
    /// </summary>
    private static List<LiveFieldValue> BuildContainerValueFields(LiveFieldValue field)
    {
        var list = new List<LiveFieldValue>();
        switch (field.TypeName)
        {
            case "MapProperty" when field.MapElements is { Count: > 0 }:
            {
                bool isStruct = field.MapValueType == "StructProperty"
                                && !string.IsNullOrEmpty(field.MapValueStructAddr);
                bool isObj = IsObjectPropertyType(field.MapValueType);
                if (!isStruct && !isObj) break;
                ulong dataBase = ParseHexAddr(field.MapDataAddr);
                int valOffset = ContainerGeometry.MapValueOffsetOf(field);
                int stride = ContainerGeometry.MapStrideOf(field);
                foreach (var e in field.MapElements)
                {
                    long off = (long)e.Index * stride + valOffset;
                    if (isStruct && dataBase != 0)
                        list.Add(new LiveFieldValue
                        {
                            TypeName = "StructProperty",
                            StructDataAddr = AbsAddr(dataBase, off),
                            StructClassAddr = field.MapValueStructAddr,
                            StructTypeName = field.MapValueStructType,
                        });
                    else if (isObj && !string.IsNullOrEmpty(e.ValuePtrAddress) && e.ValuePtrAddress != "0x0")
                        list.Add(new LiveFieldValue
                        {
                            TypeName = field.MapValueType,
                            PtrAddress = e.ValuePtrAddress,
                            PtrName = e.ValuePtrName,
                            PtrClassName = e.ValuePtrClassName,
                        });
                }
                break;
            }
            case "SetProperty" when field.SetElements is { Count: > 0 }:
            {
                bool isStruct = field.SetElemType == "StructProperty"
                                && !string.IsNullOrEmpty(field.SetElemStructAddr);
                bool isObj = IsObjectPropertyType(field.SetElemType);
                if (!isStruct && !isObj) break;
                ulong dataBase = ParseHexAddr(field.SetDataAddr);
                int stride = ContainerGeometry.SetStrideOf(field);
                foreach (var e in field.SetElements)
                {
                    long off = (long)e.Index * stride;
                    if (isStruct && dataBase != 0)
                        list.Add(new LiveFieldValue
                        {
                            TypeName = "StructProperty",
                            StructDataAddr = AbsAddr(dataBase, off),
                            StructClassAddr = field.SetElemStructAddr,
                            StructTypeName = field.SetElemStructType,
                        });
                    else if (isObj && !string.IsNullOrEmpty(e.KeyPtrAddress) && e.KeyPtrAddress != "0x0")
                        list.Add(new LiveFieldValue
                        {
                            TypeName = field.SetElemType,
                            PtrAddress = e.KeyPtrAddress,
                            PtrName = e.KeyPtrName,
                            PtrClassName = e.KeyPtrClassName,
                        });
                }
                break;
            }
            case "ArrayProperty" when field.ArrayInnerType == "StructProperty"
                    && !string.IsNullOrEmpty(field.ArrayStructClassAddr)
                    && field.ArrayElements is { Count: > 0 }:
            {
                ulong dataBase = ParseHexAddr(field.ArrayDataAddr);
                if (dataBase == 0) break;
                foreach (var e in field.ArrayElements)
                    list.Add(new LiveFieldValue
                    {
                        TypeName = "StructProperty",
                        StructDataAddr = AbsAddr(dataBase, (long)e.Index * field.ArrayElemSize),
                        StructClassAddr = field.ArrayStructClassAddr,
                        StructTypeName = field.ArrayStructType,
                    });
                break;
            }
            // TArray<ObjectProperty> (object pointers, e.g. SpawnedAttributes): emit
            // each non-null element pointer so the resolver walks the target object
            // and populates resolvedInstances — without this, drilling into a
            // selected object-array element was a no-op (the element emitted as a
            // plain 8-byte pointer regardless of drilldown depth). ArrayElementValue
            // carries no PtrClassAddr; WalkInstance resolves the class from the
            // pointer itself when class_addr is omitted.
            case "ArrayProperty" when IsRawObjectPtrArrayInner(field.ArrayInnerType)
                    && field.ArrayElements is { Count: > 0 }:
            {
                foreach (var e in field.ArrayElements)
                    if (!string.IsNullOrEmpty(e.PtrAddress) && e.PtrAddress != "0x0")
                        list.Add(new LiveFieldValue
                        {
                            TypeName = field.ArrayInnerType,
                            PtrAddress = e.PtrAddress,
                            PtrName = e.PtrName,
                            PtrClassName = e.PtrClassName,
                        });
                break;
            }
        }
        return list;
    }

    private static ulong ParseHexAddr(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var t = (s.StartsWith("0x") || s.StartsWith("0X")) ? s.Substring(2) : s;
        return ulong.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
    }

    private static string AbsAddr(ulong dataBase, long offset)
        => dataBase == 0 ? "" : $"0x{dataBase + (ulong)offset:X}";

    /// <summary>
    /// Parse the first <paramref name="numBytes"/> of a byte-sequence hex string
    /// (e.g. ContainerElementValue.ValueHex "A4AD310000000000") as a little-endian
    /// integer — the raw int CE reads at the value address (FName ComparisonIndex /
    /// enum value), used to key the value DropDownList.
    /// </summary>
    private static long ParseHexLeInt(string? hex, int numBytes)
    {
        if (string.IsNullOrEmpty(hex)) return 0;
        long v = 0;
        int n = Math.Min(numBytes, hex.Length / 2);
        for (int i = 0; i < n; i++)
        {
            int b = Convert.ToInt32(hex.Substring(i * 2, 2), 16);
            v |= (long)b << (i * 8);
        }
        return v;
    }

    /// <summary>
    /// Pre-resolve all StructProperty fields in <paramref name="fields"/> by walking
    /// each one's inner UScriptStruct via the DLL.
    ///
    /// Result is keyed by <see cref="LiveFieldValue.StructDataAddr"/> (the absolute
    /// memory address of the struct data) — NOT by field.Offset — so the same
    /// dictionary can hold struct fields from multiple drilled-pointer instances
    /// without offset-based key collisions (e.g. two different objects each have
    /// a StructProperty at offset 0x30, but their StructDataAddr differs).
    /// </summary>
    public static async Task<Dictionary<string, List<LiveFieldValue>>> ResolveStructFieldsAsync(
        IDumpService dump, IReadOnlyList<LiveFieldValue> fields, int arrayLimit = 64,
        bool lean = false)
    {
        var result = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        await ResolveStructFieldsIntoAsync(dump, fields, result, arrayLimit, lean);
        return result;
    }

    /// <summary>
    /// Walk <paramref name="fields"/>'s StructProperty entries and add their
    /// resolved sub-fields into <paramref name="resolved"/> (keyed by
    /// StructDataAddr). Used both for top-level resolution and for cascading
    /// into drilled pointer targets — letting one dict cover the whole tree.
    /// </summary>
    /// <summary>Cache key for a prefetched struct walk. Includes the class address:
    /// the same data address walked as a different class is a different walk.</summary>
    private static string StructCacheKey(string addr, string? classAddr)
        => addr + "|" + (classAddr ?? "");

    /// <summary>Does this field recurse in <see cref="ResolveStructRecursiveAsync"/>?
    /// One predicate, so the prefetch fetches EXACTLY the set the emit pass asks
    /// for. A mismatch is harmless (a superset wastes a walk, a subset falls back
    /// to a live call) but a match is what makes the batching pay off.</summary>
    private static bool IsRecursableStruct(LiveFieldValue f)
        => f.TypeName == "StructProperty"
           && !string.IsNullOrEmpty(f.StructClassAddr)
           && !string.IsNullOrEmpty(f.StructDataAddr)
           && f.StructDataAddr != "0x0";

    /// <summary>
    /// Walk the struct tree BREADTH-first with one batched call per level and
    /// return every result keyed by <see cref="StructCacheKey"/>.
    ///
    /// <para><b>Why a separate prefetch pass instead of batching the recursion.</b>
    /// <see cref="ResolveStructRecursiveAsync"/> produces a DEPTH-first flattened
    /// list whose order -- and whose accumulated name prefixes and offsets -- ARE
    /// the export's field order. Restructuring it breadth-first would change the
    /// output. Prefetching leaves the emit traversal exactly as it was and only
    /// changes where its data comes from.</para>
    ///
    /// <para>MEASURED motivation (multipipe-eval.md 10.4): this tree, not the
    /// object-pointer drilldown, is where a Copy CE XML's ~22,500 walk_instance
    /// calls come from -- each carrying 0.16-0.21 ms of round-trip overhead
    /// against 0.08 ms of actual work.</para>
    /// </summary>
    private static async Task<Dictionary<string, InstanceWalkResult>> PrefetchStructTreeAsync(
        IDumpService dump,
        IReadOnlyList<(string Addr, string ClassAddr)> roots,
        int arrayLimit,
        bool lean,
        CancellationToken ct)
    {
        var cache = new Dictionary<string, InstanceWalkResult>(StringComparer.Ordinal);
        var level = new List<(string Addr, string ClassAddr)>(roots);

        // Same bound as the emit recursion (depth 0..MaxStructDepth-1), so the
        // prefetch covers exactly the levels that will be asked for.
        for (int depth = 0; depth < MaxStructDepth && level.Count > 0; depth++)
        {
            ct.ThrowIfCancellationRequested();

            var todo = new List<(string Addr, string? ClassAddr)>();
            var keys = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (a, c) in level)
            {
                var k = StructCacheKey(a, c);
                // Dedup against earlier levels AND within this one. Doubles as the
                // cycle guard: a self-referential struct is fetched once, then the
                // depth bound stops the descent.
                if (cache.ContainsKey(k) || !seen.Add(k)) continue;
                keys.Add(k);
                todo.Add((a, c));
            }
            if (todo.Count == 0) break;

            IReadOnlyList<InstanceWalkResult> walked;
            try
            {
                walked = await dump.WalkInstanceBatchAsync(todo, arrayLimit, lean: lean, ct: ct);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Prefetch is an optimisation, never a requirement: on any failure
                // return what we have and let the emit pass walk the rest live.
                return cache;
            }

            var next = new List<(string Addr, string ClassAddr)>();
            for (int i = 0; i < keys.Count && i < walked.Count; i++)
            {
                cache[keys[i]] = walked[i];
                foreach (var f in walked[i].Fields)
                    if (IsRecursableStruct(f))
                        next.Add((f.StructDataAddr, f.StructClassAddr));
            }
            level = next;
        }
        return cache;
    }

    private static async Task ResolveStructFieldsIntoAsync(
        IDumpService dump,
        IReadOnlyList<LiveFieldValue> fields,
        Dictionary<string, List<LiveFieldValue>> resolved,
        int arrayLimit,
        bool lean = false,
        CancellationToken ct = default)
    {
        // Collect this call's struct roots and prefetch the whole tree with batched
        // per-level calls, so the depth-first emit below reads from memory instead
        // of issuing one round-trip per struct.
        var roots = new List<(string Addr, string ClassAddr)>();
        foreach (var field in fields)
        {
            var isStructRoot = (field.TypeName == "StructProperty"
                                || field.TypeName == "OptionalProperty")
                               && !string.IsNullOrEmpty(field.StructClassAddr)
                               && !string.IsNullOrEmpty(field.StructDataAddr)
                               && field.StructDataAddr != "0x0"
                               && !resolved.ContainsKey(field.StructDataAddr);
            if (isStructRoot) roots.Add((field.StructDataAddr, field.StructClassAddr));
        }
        var prefetch = roots.Count > 0
            ? await PrefetchStructTreeAsync(dump, roots, arrayLimit, lean, ct)
            : null;

        foreach (var field in fields)
        {
            // Abort promptly between per-field struct walks when the export is cancelled.
            ct.ThrowIfCancellationRequested();

            // Both StructProperty and OptionalProperty<Struct> have the same
            // {StructClassAddr, StructDataAddr, StructTypeName} triple stamped
            // by the walker when the value is set, so the resolver treats
            // them uniformly — the emit-time branch decides how to render.
            var isStruct = field.TypeName == "StructProperty"
                        || field.TypeName == "OptionalProperty";
            if (!isStruct
                || string.IsNullOrEmpty(field.StructClassAddr)
                || string.IsNullOrEmpty(field.StructDataAddr)
                || field.StructDataAddr == "0x0")
                continue;
            if (resolved.ContainsKey(field.StructDataAddr)) continue;

            var subResolved = new List<LiveFieldValue>();
            try
            {
                await ResolveStructRecursiveAsync(dump, field.StructDataAddr, field.StructClassAddr,
                    "", 0, subResolved, 0, arrayLimit, lean, ct, prefetch);
            }
            // A cancel unwinds the whole export; only genuine failures leave the struct
            // empty (emit falls back to a placeholder). OperationCanceledException covers
            // its TaskCanceledException subclass too.
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // If resolution fails (pipe error, etc.), leave empty — will fall back to placeholder
            }

            if (subResolved.Count > 0)
                resolved[field.StructDataAddr] = subResolved;
        }
    }

    private static async Task ResolveStructRecursiveAsync(
        IDumpService dump, string dataAddr, string classAddr,
        string namePrefix, int baseOffset, List<LiveFieldValue> output, int depth,
        int arrayLimit = 64, bool lean = false, CancellationToken ct = default,
        Dictionary<string, InstanceWalkResult>? prefetch = null)
    {
        if (depth >= MaxStructDepth) return;

        ct.ThrowIfCancellationRequested();
        // Prefetched by PrefetchStructTreeAsync when available; a miss (older DLL,
        // failed batch, or a shape the prefetch predicate did not anticipate) walks
        // live, exactly as before batching existed.
        InstanceWalkResult walkResult;
        if (prefetch != null && prefetch.TryGetValue(StructCacheKey(dataAddr, classAddr), out var cached))
            walkResult = cached;
        else
            walkResult = await dump.WalkInstanceAsync(dataAddr, classAddr, arrayLimit: arrayLimit, lean: lean, ct: ct);

        foreach (var f in walkResult.Fields)
        {
            var displayName = string.IsNullOrEmpty(namePrefix) ? f.Name : $"{namePrefix}.{f.Name}";
            var absOffset = baseOffset + f.Offset;

            if (f.TypeName == "StructProperty"
                && !string.IsNullOrEmpty(f.StructClassAddr)
                && !string.IsNullOrEmpty(f.StructDataAddr)
                && f.StructDataAddr != "0x0")
            {
                // Nested struct — recurse and flatten into the same list
                await ResolveStructRecursiveAsync(dump, f.StructDataAddr, f.StructClassAddr,
                    displayName, absOffset, output, depth + 1, arrayLimit, lean, ct, prefetch);
            }
            else if (f.IsPointerNavigation)
            {
                // Pointer inside struct — emit as pointer placeholder
                output.Add(new LiveFieldValue
                {
                    Name = displayName,
                    TypeName = f.TypeName,
                    Offset = absOffset,
                    Size = f.Size,
                    PtrAddress = f.PtrAddress,
                    PtrName = f.PtrName,
                    PtrClassName = f.PtrClassName,
                    PtrClassAddr = f.PtrClassAddr,
                });
            }
            else
            {
                // Scalar or array field — add with accumulated offset and prefixed name
                output.Add(new LiveFieldValue
                {
                    Name = displayName,
                    TypeName = f.TypeName,
                    Offset = absOffset,
                    Size = f.Size,
                    HexValue = f.HexValue,
                    TypedValue = f.TypedValue,
                    BoolBitIndex = f.BoolBitIndex,
                    BoolFieldMask = f.BoolFieldMask,
                    // Preserve the within-field byte index so a flattened bit-field bool keeps
                    // landing on the right byte (base + Offset + ByteOffset). CE XML ignores it,
                    // but CSX 7.7+ Binary export needs it to place the bit switch correctly.
                    BoolByteOffset = f.BoolByteOffset,
                    ArrayCount = f.ArrayCount,
                    ArrayInnerType = f.ArrayInnerType,
                    ArrayElemSize = f.ArrayElemSize,
                    ArrayStructType = f.ArrayStructType,
                    ArrayStructClassAddr = f.ArrayStructClassAddr,
                    ArrayElements = f.ArrayElements,
                    ArrayDataAddr = f.ArrayDataAddr,
                    ArrayEnumAddr = f.ArrayEnumAddr,
                    ArrayEnumEntries = f.ArrayEnumEntries,
                    SoftArrayFNameSize = f.SoftArrayFNameSize,
                    SoftArrayIsTopLevelAssetPath = f.SoftArrayIsTopLevelAssetPath,
                    EnumName = f.EnumName,
                    EnumValue = f.EnumValue,
                    EnumAddr = f.EnumAddr,
                    EnumEntries = f.EnumEntries,
                    StrValue = f.StrValue,
                    MapCount = f.MapCount,
                    MapKeyType = f.MapKeyType,
                    MapValueType = f.MapValueType,
                    MapKeySize = f.MapKeySize,
                    MapValueSize = f.MapValueSize,
                    MapValueOffset = f.MapValueOffset,
                    MapStride = f.MapStride,
                    MapDataAddr = f.MapDataAddr,
                    MapElements = f.MapElements,
                    // Container value/key struct metadata — REQUIRED so a Map/Set/Array
                    // nested INSIDE a struct can resolve+expand its struct values
                    // (e.g. MsTuneData → MsTunes {Map → Struct}).
                    MapKeyStructAddr = f.MapKeyStructAddr,
                    MapKeyStructType = f.MapKeyStructType,
                    MapValueStructAddr = f.MapValueStructAddr,
                    MapValueStructType = f.MapValueStructType,
                    SetCount = f.SetCount,
                    SetElemType = f.SetElemType,
                    SetElemSize = f.SetElemSize,
                    SetStride = f.SetStride,
                    SetDataAddr = f.SetDataAddr,
                    SetElemStructAddr = f.SetElemStructAddr,
                    SetElemStructType = f.SetElemStructType,
                    SetElements = f.SetElements,
                });
            }
        }
    }

    // ========================================
    // XML generation
    // ========================================

    /// <summary>
    /// Generate hierarchical CE XML from the navigation breadcrumb trail and current fields.
    ///
    /// Algorithm:
    /// - Root (breadcrumbs[0]): absolute address, GroupHeader
    /// - Each breadcrumb[i] (i>=1): Address=+{fieldOffset}
    ///   - If the breadcrumb is a pointer (IsPointerDeref): add Offsets=[0] to dereference
    ///   - If inline (struct): no Offsets
    ///   Parent's Offsets=[0] resolves the pointer, so children just add their offset
    /// - Leaf fields: always Address=+{field.Offset}, no Offsets
    ///   (Parent breadcrumb already resolved any pointer dereference via its Offsets=[0])
    /// - StructProperty (inline): Address=+{structOffset}, no Offsets, children at relative offsets
    /// - ArrayProperty (scalar): Address=+{fieldOffset}, Offsets=[0] (deref TArray.Data)
    ///   Element children: Address=+{N*elemSize} (Data pointer already dereferenced by parent)
    /// </summary>
    public static string GenerateHierarchicalXml(
        string rootAddress,
        string rootName,
        IReadOnlyList<BreadcrumbItem> breadcrumbs,
        IReadOnlyList<LiveFieldValue> currentFields,
        Dictionary<string, List<LiveFieldValue>>? resolvedStructs = null,
        bool collapsePointerNodes = false,
        int maxDropDownEntries = 512,
        Dictionary<string, List<LiveFieldValue>>? resolvedInstances = null,
        bool flattenChain = false,
        bool includeGuessed = false,
        bool descShowOffset = false,
        bool descShowType = false,
        bool dedupShared = false,
        bool excludeSystemComponents = false,
        bool flattenGasAttributes = false,
        bool flattenLeafStructs = false,
        bool flattenLeafRecords = false,
        bool altColorEnabled = false,
        string? altRowColorEvenRgb = null,
        string? altRowColorOddRgb = null,
        bool collapseLeafPointers = false,
        int ceStringLength = 256,
        int fabricateArrayCount = 0)
    {
        // Clean breadcrumbs: remove navigation cycles (e.g., Child->Parent->Child)
        // before generating XML to avoid deeply nested duplicate pointer chains.
        var cleanedBc = CleanBreadcrumbs(breadcrumbs);

        _nextId = 100;
        _collapsePointerNodes = collapsePointerNodes;
        _includeGuessed = includeGuessed;
        _descShowOffset = descShowOffset;
        _descShowType = descShowType;
        _dedupShared = dedupShared;
        _excludeSystemComponents = excludeSystemComponents;
        _flattenGasAttributes = flattenGasAttributes;
        _flattenLeafStructs = flattenLeafStructs;
        _flattenLeafRecords = flattenLeafRecords;
        _altColorEnabled = altColorEnabled;
        _altColorEven = RgbToCeColor(altRowColorEvenRgb);
        _altColorOdd = RgbToCeColor(altRowColorOddRgb);
        _curRowColor = null;
        _collapseLeafPointers = collapseLeafPointers;
        _ceStringLength = ceStringLength;
        _fabricateArrayCount = fabricateArrayCount;
        _maxDropDownEntries = maxDropDownEntries;
        _dropDownOwners = new Dictionary<string, string>();
        _dropDownDescriptions = new HashSet<string>(StringComparer.Ordinal);
        _emitPath = new HashSet<string>(StringComparer.Ordinal);
        _emitPointerDepth = 0;
        _emitDepth = 0;
        _emitEntryCount = 0;
        _systemFieldsSkipped = 0;
        _emitTruncated = false;
        _emittedInstances = new HashSet<string>(StringComparer.Ordinal);
        _resolvedStructsState = resolvedStructs;
        _resolvedInstancesState = resolvedInstances;
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<CheatTable>");
        // Best-effort note when the game uses the UE5.7+ UNVERIFIED packed layout (no-op otherwise).
        sb.Append(PackedLayoutNotice.XmlComment);
        sb.AppendLine("  <CheatEntries>");

        // Build the nested structure recursively via indentation tracking
        var indent = "    ";
        var openTags = 0;

        // Root entry (cycle removal preserves breadcrumbs[0], so rootAddress/rootName are still valid)
        EmitGroupOpen(sb, indent, rootName, rootAddress, null, showAsHex: true, varType: "8 Bytes");
        openTags++;

        // Intermediate breadcrumb levels (navigation path)
        // Each breadcrumb: go to field offset from parent's resolved address.
        // If this field is a pointer, add Offsets=[0] to dereference it.
        // Container views (Array/Map/Set) also need Offsets=[0] to dereference
        // TArray::Data / TSparseArray::Data pointer at the field offset.
        // Parent's own Offsets=[0] (if pointer) already resolved the dereference,
        // so children just add their field offset.
        // Navigation spine. With Collapse chain on, fold every breadcrumb after the
        // root into ONE CE multi-level-pointer entry; otherwise emit the nested chain
        // (one group per breadcrumb). spineLevels = group levels the spine occupies,
        // so the leaf indent / close loop work for both shapes.
        int spineLevels;
        var folded = flattenChain ? FoldBreadcrumbSpine(cleanedBc) : null;
        if (folded != null)
        {
            EmitGroupOpen(sb, indent + "  ", folded.Description,
                folded.Address, folded.Offsets, showAsHex: folded.ShowAsHex);
            openTags++;
            spineLevels = 1;
        }
        else
        {
            for (int i = 1; i < cleanedBc.Count; i++)
            {
                // Both emit paths derive (offset, deref, label) from ProjectBreadcrumb
                // so the nested and folded shapes can never disagree about a
                // breadcrumb's pointer semantics. Containers/pointers deref
                // TArray::Data / TSparseArray::Data via Offsets=[0]; inline structs
                // just add their offset.
                var step = ProjectBreadcrumb(cleanedBc[i]);
                var childIndent = indent + new string(' ', i * 2);
                EmitGroupOpen(sb, childIndent,
                    DecorateDesc(step.Description, step.Offset, SpineTypeLabel(cleanedBc[i])),
                    $"+{step.Offset:X}",
                    step.DerefAfter ? new[] { 0 } : null,
                    showAsHex: step.DerefAfter);
                openTags++;
            }
            spineLevels = cleanedBc.Count - 1;
        }

        // Leaf fields at the deepest level. Parent breadcrumb (nested or folded)
        // already resolved any pointer dereference, so leaf fields use Address=+{off}.
        var leafIndent = indent + new string(' ', (spineLevels + 1) * 2);
        EmitFields(sb, leafIndent, currentFields, resolvedStructs, resolvedInstances);

        // Close all nested levels (innermost first)
        for (int i = openTags - 1; i >= 0; i--)
        {
            var closeIndent = indent + new string(' ', i * 2);
            EmitGroupClose(sb, closeIndent);
        }

        sb.AppendLine("  </CheatEntries>");
        sb.AppendLine("</CheatTable>");

        return sb.ToString();
    }

    /// <summary>
    /// Generate CE XML for an instance with no navigation history (Instance Finder).
    /// Root = the instance itself. Fields are direct children with +{offset}.
    /// </summary>
    public static string GenerateInstanceXml(
        string rootAddress,
        string rootName,
        string className,
        IReadOnlyList<LiveFieldValue> fields,
        Dictionary<string, List<LiveFieldValue>>? resolvedStructs = null,
        bool collapsePointerNodes = false,
        int maxDropDownEntries = 512,
        Dictionary<string, List<LiveFieldValue>>? resolvedInstances = null,
        bool includeGuessed = false,
        bool descShowOffset = false,
        bool descShowType = false,
        bool dedupShared = false,
        bool excludeSystemComponents = false,
        bool flattenGasAttributes = false,
        bool flattenLeafStructs = false,
        bool flattenLeafRecords = false,
        bool altColorEnabled = false,
        string? altRowColorEvenRgb = null,
        string? altRowColorOddRgb = null,
        bool collapseLeafPointers = false,
        int ceStringLength = 256,
        int fabricateArrayCount = 0)
    {
        _nextId = 100;
        _collapsePointerNodes = collapsePointerNodes;
        _includeGuessed = includeGuessed;
        _descShowOffset = descShowOffset;
        _descShowType = descShowType;
        _dedupShared = dedupShared;
        _excludeSystemComponents = excludeSystemComponents;
        _flattenGasAttributes = flattenGasAttributes;
        _flattenLeafStructs = flattenLeafStructs;
        _flattenLeafRecords = flattenLeafRecords;
        _altColorEnabled = altColorEnabled;
        _altColorEven = RgbToCeColor(altRowColorEvenRgb);
        _altColorOdd = RgbToCeColor(altRowColorOddRgb);
        _curRowColor = null;
        _collapseLeafPointers = collapseLeafPointers;
        _ceStringLength = ceStringLength;
        _fabricateArrayCount = fabricateArrayCount;
        _maxDropDownEntries = maxDropDownEntries;
        _dropDownOwners = new Dictionary<string, string>();
        _dropDownDescriptions = new HashSet<string>(StringComparer.Ordinal);
        _emitPath = new HashSet<string>(StringComparer.Ordinal);
        _emitPointerDepth = 0;
        _emitDepth = 0;
        _emitEntryCount = 0;
        _systemFieldsSkipped = 0;
        _emitTruncated = false;
        _emittedInstances = new HashSet<string>(StringComparer.Ordinal);
        _resolvedStructsState = resolvedStructs;
        _resolvedInstancesState = resolvedInstances;
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<CheatTable>");
        // Best-effort note when the game uses the UE5.7+ UNVERIFIED packed layout (no-op otherwise).
        sb.Append(PackedLayoutNotice.XmlComment);
        sb.AppendLine("  <CheatEntries>");

        var indent = "    ";
        EmitGroupOpen(sb, indent, $"{className}: {rootName}", rootAddress, null,
            showAsHex: true, varType: "8 Bytes");

        var leafIndent = indent + "  ";
        EmitFields(sb, leafIndent, fields, resolvedStructs, resolvedInstances);

        EmitGroupClose(sb, indent);

        sb.AppendLine("  </CheatEntries>");
        sb.AppendLine("</CheatTable>");

        return sb.ToString();
    }

    /// <summary>
    /// Generate a CE-compatible XML with an AutoAssembler script that registers a symbol.
    /// Accepts a pre-formatted address string (e.g., "module.exe"+RVA or plain hex).
    /// </summary>
    public static string GenerateRegisterSymbolXml(string symbolName, string formattedAddress)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<CheatTable>");
        sb.AppendLine("  <CheatEntries>");
        sb.AppendLine($"    <CheatEntry>");
        sb.AppendLine($"      <ID>0</ID>");
        sb.AppendLine($"      <Description>\"{EscapeXmlContent(symbolName)}\"</Description>");
        sb.AppendLine($"      <VariableType>Auto Assembler Script</VariableType>");
        sb.AppendLine($"      <AssemblerScript>");

        sb.AppendLine("[ENABLE]");
        sb.AppendLine($"define({symbolName},{formattedAddress})");
        sb.AppendLine($"registersymbol({symbolName})");
        sb.AppendLine();

        sb.AppendLine("[DISABLE]");
        sb.AppendLine($"unregistersymbol({symbolName})");

        sb.AppendLine($"      </AssemblerScript>");
        sb.AppendLine($"    </CheatEntry>");
        sb.AppendLine("  </CheatEntries>");
        sb.AppendLine("</CheatTable>");

        return sb.ToString();
    }

    /// <summary>
    /// Extract the raw Auto Assembler body (the un-escaped text inside the generated
    /// &lt;AssemblerScript&gt; node) from a CE AA-script CheatTable XML. CE's
    /// CreateAAScript (AOBMaker plugin) wants the raw script text, not the XML wrapper,
    /// so this lets the "Copy CE AA Script" handoff push straight into CE's address
    /// list when the plugin is reachable. Returns "" when no &lt;AssemblerScript&gt;
    /// marker is present (e.g. a non-AA CheatTable XML). The only XML entities our
    /// generators ever emit inside the script are &amp;amp; / &amp;lt; / &amp;gt; (from
    /// Lua-comment sanitisation), which CE un-escapes before running — reproduced here
    /// so the pushed script byte-matches the clipboard-pasted one.
    /// </summary>
    public static string ExtractAssemblerScript(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return "";
        const string open = "<AssemblerScript>";
        const string close = "</AssemblerScript>";
        var s = xml.IndexOf(open, StringComparison.Ordinal);
        if (s < 0) return "";
        s += open.Length;
        var e = xml.IndexOf(close, s, StringComparison.Ordinal);
        if (e < 0) return "";
        var body = xml.Substring(s, e - s).Trim();
        // Unescape &amp; last so an escaped entity like "&amp;lt;" isn't collapsed twice.
        return body.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");
    }

    /// <summary>
    /// Generate CE XML with an AOB-scanning AA script root instead of a hardcoded address.
    /// The script scans for the GWorld AOB pattern at runtime, registers a unique CE symbol,
    /// and a "base" pointer entry dereferences it. All breadcrumb/field children nest under base.
    /// This format survives game restarts (re-scans AOB on script activation).
    /// </summary>
    public static string GenerateAobWrappedXml(
        string rootName,
        IReadOnlyList<BreadcrumbItem> breadcrumbs,
        IReadOnlyList<LiveFieldValue> currentFields,
        string aob, int aobPos, int aobLen, string moduleName,
        Dictionary<string, List<LiveFieldValue>>? resolvedStructs = null,
        bool collapsePointerNodes = false,
        int maxDropDownEntries = 512,
        Dictionary<string, List<LiveFieldValue>>? resolvedInstances = null,
        bool flattenChain = false,
        bool includeGuessed = false,
        bool descShowOffset = false,
        bool descShowType = false,
        bool dedupShared = false,
        bool excludeSystemComponents = false,
        bool flattenGasAttributes = false,
        bool flattenLeafStructs = false,
        bool flattenLeafRecords = false,
        bool altColorEnabled = false,
        string? altRowColorEvenRgb = null,
        string? altRowColorOddRgb = null,
        bool collapseLeafPointers = false,
        int ceStringLength = 256,
        int fabricateArrayCount = 0)
    {
        var cleanedBc = CleanBreadcrumbs(breadcrumbs);

        _nextId = 100;
        _collapsePointerNodes = collapsePointerNodes;
        _includeGuessed = includeGuessed;
        _descShowOffset = descShowOffset;
        _descShowType = descShowType;
        _dedupShared = dedupShared;
        _excludeSystemComponents = excludeSystemComponents;
        _flattenGasAttributes = flattenGasAttributes;
        _flattenLeafStructs = flattenLeafStructs;
        _flattenLeafRecords = flattenLeafRecords;
        _altColorEnabled = altColorEnabled;
        _altColorEven = RgbToCeColor(altRowColorEvenRgb);
        _altColorOdd = RgbToCeColor(altRowColorOddRgb);
        _curRowColor = null;
        _collapseLeafPointers = collapseLeafPointers;
        _ceStringLength = ceStringLength;
        _fabricateArrayCount = fabricateArrayCount;
        _maxDropDownEntries = maxDropDownEntries;
        _dropDownOwners = new Dictionary<string, string>();
        _dropDownDescriptions = new HashSet<string>(StringComparer.Ordinal);
        _emitPath = new HashSet<string>(StringComparer.Ordinal);
        _emitPointerDepth = 0;
        _emitDepth = 0;
        _emitEntryCount = 0;
        _systemFieldsSkipped = 0;
        _emitTruncated = false;
        _emittedInstances = new HashSet<string>(StringComparer.Ordinal);
        _resolvedStructsState = resolvedStructs;
        _resolvedInstancesState = resolvedInstances;

        // Generate unique symbol name to avoid CE overwrite on repeated copies
        var suffix = Random.Shared.Next(0x100000, 0xFFFFFF).ToString("X6");
        var symbolName = $"gworld_addr_{suffix}";

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<CheatTable>");
        sb.AppendLine("  <CheatEntries>");

        // ---- Outer: AA Script entry ----
        var indent = "    ";
        sb.AppendLine($"{indent}<CheatEntry>");
        sb.AppendLine($"{indent}  <ID>{_nextId++}</ID>");
        sb.AppendLine($"{indent}  <Description>\"GWorld \u2192 {EscapeXmlContent(symbolName)}\"</Description>");
        sb.AppendLine($"{indent}  <Options moHideChildren=\"1\" moDeactivateChildrenAsWell=\"1\"/>");
        sb.AppendLine($"{indent}  <LastState/>");
        sb.AppendLine($"{indent}  <VariableType>Auto Assembler Script</VariableType>");
        sb.AppendLine($"{indent}  <AssemblerScript>");
        BuildAobAssemblerScript(sb, symbolName, aob, aobPos, aobLen);
        sb.AppendLine($"{indent}  </AssemblerScript>");
        sb.AppendLine($"{indent}  <CheatEntries>");

        // ---- "base" pointer entry: dereferences the symbol ----
        var baseIndent = indent + "    ";
        sb.AppendLine($"{baseIndent}<CheatEntry>");
        sb.AppendLine($"{baseIndent}  <ID>{_nextId++}</ID>");
        sb.AppendLine($"{baseIndent}  <Description>\"base\"</Description>");
        sb.AppendLine($"{baseIndent}  <ShowAsHex>1</ShowAsHex>");
        sb.AppendLine($"{baseIndent}  <ShowAsSigned>0</ShowAsSigned>");
        sb.AppendLine($"{baseIndent}  <VariableType>8 Bytes</VariableType>");
        sb.AppendLine($"{baseIndent}  <Address>{symbolName}</Address>");
        sb.AppendLine($"{baseIndent}  <Offsets>");
        sb.AppendLine($"{baseIndent}    <Offset>0</Offset>");
        sb.AppendLine($"{baseIndent}  </Offsets>");
        sb.AppendLine($"{baseIndent}  <CheatEntries>");

        // ---- Inner breadcrumb chain (skip root at index 0, base replaces it) ----
        // With Collapse chain on, fold the whole spine into ONE entry under base;
        // otherwise emit the nested chain. spineLevels = group levels under base.
        var innerOpenTags = 0;
        int spineLevels;
        var folded = flattenChain ? FoldBreadcrumbSpine(cleanedBc) : null;
        if (folded != null)
        {
            EmitGroupOpen(sb, baseIndent + "    ", folded.Description,
                folded.Address, folded.Offsets, showAsHex: folded.ShowAsHex);
            innerOpenTags++;
            spineLevels = 1;
        }
        else
        {
            for (int i = 1; i < cleanedBc.Count; i++)
            {
                // Shared projection: see GenerateHierarchicalXml for the rationale.
                var step = ProjectBreadcrumb(cleanedBc[i]);
                var childIndent = baseIndent + "    " + new string(' ', (i - 1) * 2);
                EmitGroupOpen(sb, childIndent,
                    DecorateDesc(step.Description, step.Offset, SpineTypeLabel(cleanedBc[i])),
                    $"+{step.Offset:X}",
                    step.DerefAfter ? new[] { 0 } : null,
                    showAsHex: step.DerefAfter);
                innerOpenTags++;
            }
            spineLevels = Math.Max(0, cleanedBc.Count - 1);
        }

        // ---- Leaf fields ----
        var leafIndent = baseIndent + "    " + new string(' ', spineLevels * 2);
        EmitFields(sb, leafIndent, currentFields, resolvedStructs, resolvedInstances);

        // ---- Close inner breadcrumb groups ----
        for (int i = innerOpenTags - 1; i >= 0; i--)
        {
            var closeIndent = baseIndent + "    " + new string(' ', i * 2);
            EmitGroupClose(sb, closeIndent);
        }

        // ---- Close "base" ----
        sb.AppendLine($"{baseIndent}  </CheatEntries>");
        sb.AppendLine($"{baseIndent}</CheatEntry>");

        // ---- Close AA Script entry ----
        sb.AppendLine($"{indent}  </CheatEntries>");
        sb.AppendLine($"{indent}</CheatEntry>");

        sb.AppendLine("  </CheatEntries>");
        sb.AppendLine("</CheatTable>");

        return sb.ToString();
    }

    /// <summary>
    /// Build the AA script body that scans for an AOB pattern and registers a CE symbol.
    /// Matches the format produced by the CEPlugin's BuildSymbolScanScript.
    /// </summary>
    private static void BuildAobAssemblerScript(StringBuilder sb, string symbolName,
        string aob, int aobPos, int aobLen)
    {
        sb.AppendLine("[ENABLE]");
        sb.AppendLine("{$lua}");
        sb.AppendLine("if syntaxcheck then return end");
        AppendDebugPreamble(sb);
        sb.AppendLine($"-- {CeLuaHygiene.Attribution}");
        sb.AppendLine();

        // Idempotent Lua helpers, shared verbatim with GenerateGWorldWalkedSymbolXml.
        AppendAobScanModuleUEHelper(sb);
        AppendCloseLuaEngineHelper(sb);

        // AOB entries table
        sb.AppendLine("local AOBs = {");
        sb.AppendLine($"  {{name='GWorld \u2192 {symbolName}', aob='{aob}', pos={aobPos}, aoblen={aobLen}, symbol='{symbolName}'}},");
        sb.AppendLine("}");
        sb.AppendLine();

        // Use CE global 'process' for the attached process module name
        sb.AppendLine("local module_name = process");
        sb.AppendLine();

        // Scan and register loop
        sb.AppendLine("local scan_ok = true");
        sb.AppendLine("for _, entry in ipairs(AOBs) do");
        sb.AppendLine("  local aob_addr_str = AOBScanModuleUE(module_name, entry.aob)");
        sb.AppendLine("  if aob_addr_str then");
        sb.AppendLine("    local aob_addr_val = tonumber(aob_addr_str, 16)");
        sb.AppendLine("    local offset_addr = aob_addr_val + entry.pos");
        sb.AppendLine("    local relative_offset = readInteger(offset_addr, true)");
        sb.AppendLine("    local final_addr = relative_offset + aob_addr_val + entry.aoblen");
        sb.AppendLine("    synchronize(function()");
        sb.AppendLine("      unregisterSymbol(entry.symbol)");
        sb.AppendLine("      registerSymbol(entry.symbol, final_addr)");
        sb.AppendLine("    end)");
        sb.AppendLine("    dbg(string.format('[SymbolScanner] %s registered at: %X', entry.name, final_addr))");
        sb.AppendLine("  else");
        sb.AppendLine("    scan_ok = false");
        sb.AppendLine("    print(string.format('[SymbolScanner] WARNING: AOB scan failed for %s', entry.name))");
        sb.AppendLine("  end");
        sb.AppendLine("end");
        sb.AppendLine();
        // Close only on a clean scan; a failed scan keeps the window open so the
        // WARNING stays readable.
        sb.AppendLine("if DEBUG == 0 and scan_ok then closeLuaEngine() end");
        sb.AppendLine("{$asm}");
        sb.AppendLine();

        // DISABLE section
        sb.AppendLine("[DISABLE]");
        sb.AppendLine("{$lua}");
        sb.AppendLine("if syntaxcheck then return end");
        AppendDebugPreamble(sb);
        sb.AppendLine($"unregisterSymbol('{symbolName}')");
        sb.AppendLine("if DEBUG == 0 then closeLuaEngine() end");
        sb.AppendLine("{$asm}");
    }

    /// <summary>AOBScanModuleUE Lua helper (idempotent — won't redefine if already
    /// loaded). Shared verbatim by BuildAobAssemblerScript and the GWorld-walk script.</summary>
    private static void AppendAobScanModuleUEHelper(StringBuilder sb)
    {
        sb.AppendLine("if not AOBScanModuleUE then");
        sb.AppendLine("  function AOBScanModuleUE(moduleName, signature)");
        sb.AppendLine("    local baseAddr = nil");
        sb.AppendLine("    local maxAddr = 0");
        sb.AppendLine("    local modList");
        sb.AppendLine("    synchronize(function()");
        sb.AppendLine("      modList = enumModules()");
        sb.AppendLine("    end)");
        sb.AppendLine("    for _, mod in ipairs(modList) do");
        sb.AppendLine("      if string.lower(mod.Name) == string.lower(moduleName) then");
        sb.AppendLine("        baseAddr = mod.Address");
        sb.AppendLine("        maxAddr = baseAddr + mod.Size");
        sb.AppendLine("        break");
        sb.AppendLine("      end");
        sb.AppendLine("    end");
        sb.AppendLine("    if not baseAddr then return nil end");
        sb.AppendLine("    local ms = createMemScan()");
        sb.AppendLine("    synchronize(function()");
        sb.AppendLine("      ms.firstScan(soExactValue, vtByteArray, nil, signature,");
        sb.AppendLine("        nil, baseAddr, maxAddr, '+X-C-W', fsmNotAligned, '1', true, true, false, false)");
        sb.AppendLine("    end)");
        sb.AppendLine("    ms.waitTillDone()");
        sb.AppendLine("    local results = createFoundList(ms)");
        sb.AppendLine("    results.initialize()");
        sb.AppendLine("    local addr");
        sb.AppendLine("    synchronize(function()");
        sb.AppendLine("      if results.getCount() &gt; 0 then");
        sb.AppendLine("        addr = results[0]");
        sb.AppendLine("      end");
        sb.AppendLine("    end)");
        sb.AppendLine("    results.destroy()");
        sb.AppendLine("    ms.destroy()");
        sb.AppendLine("    return addr");
        sb.AppendLine("  end");
        sb.AppendLine("end");
        sb.AppendLine("registerLuaFunctionHighlight('AOBScanModuleUE')");
        sb.AppendLine();
    }

    /// <summary>Emit the shared DEBUG preamble into the embedded AA-script Lua so
    /// it honours <c>UE5_DEBUG</c> like every other generator: <c>dbg()</c> is
    /// quiet unless the flag is set, and the trailing <c>closeLuaEngine()</c> is
    /// gated on <c>DEBUG == 0</c>. Text is identical to
    /// <see cref="CeLuaHygiene.AppendDebugPreamble"/>; emitted via AppendLine here
    /// to keep this file's line-ending style consistent within the XML payload.</summary>
    private static void AppendDebugPreamble(StringBuilder sb)
    {
        sb.AppendLine("local DEBUG = UE5_DEBUG or 0   -- 1 = show diagnostics + keep this window open");
        sb.AppendLine("local function dbg(...) if DEBUG ~= 0 then print(...) end end");
    }

    /// <summary>closeLuaEngine Lua helper (idempotent). Shared verbatim by
    /// BuildAobAssemblerScript and the GWorld-walk script.</summary>
    private static void AppendCloseLuaEngineHelper(StringBuilder sb)
    {
        sb.AppendLine("if not closeLuaEngine then");
        sb.AppendLine("  function closeLuaEngine()");
        sb.AppendLine("    synchronize(function()");
        sb.AppendLine("      getLuaEngine().Close()");
        sb.AppendLine("    end)");
        sb.AppendLine("  end");
        sb.AppendLine("end");
        sb.AppendLine("registerLuaFunctionHighlight('closeLuaEngine')");
        sb.AppendLine();
    }

    /// <summary>
    /// Generate a RESTART-STABLE Auto Assembler script that registers the current
    /// object as a CE symbol by WALKING from GWorld down the navigation spine at
    /// enable time — instead of the hardcoded absolute address that dies on ASLR.
    ///
    /// The GWorld slot (&amp;GWorld) is recovered either by an AOB scan
    /// (<paramref name="useAob"/>=true — survives restart automatically) or
    /// hardcoded from <paramref name="gworldSlotAddr"/> (useAob=false — the user
    /// updates that value after a restart). The Lua then deref's *GWorld → UWorld*
    /// and applies each breadcrumb step (readQword on a pointer-deref crumb, plain
    /// add on an inline-struct crumb), null-guarding every hop, and finally
    /// registerSymbol's the resulting leaf address.
    ///
    /// The caller MUST pass a GWorld-rooted, forward-walkable spine: breadcrumbs[0]
    /// is the GWorld root (its offset is unused — the base deref replaces it) and
    /// every later crumb has FieldOffset &gt;= 0. breadcrumbs[^1] is the object being
    /// registered. Pointer math uses tonumber(hex,16)/readQword — both proven 64-bit
    /// in this project's shipped Lua (ue5_freeze_helper, BuildAobAssemblerScript).
    /// Internal (not public): the sole caller (LiveWalkerViewModel.BuildAaScript)
    /// enforces the forward-walkable precondition, and the tests reach it via
    /// InternalsVisibleTo — so the contract can't be bypassed by an outside caller.
    /// </summary>
    internal static string GenerateGWorldWalkedSymbolXml(
        string leafSymbol,
        IReadOnlyList<BreadcrumbItem> breadcrumbs,
        bool useAob,
        string aob, int aobPos, int aobLen,
        string gworldSlotAddr)
    {
        var cleanedBc = CleanBreadcrumbs(breadcrumbs);
        // Unique GWorld symbol per script so two enabled tables can't unregister
        // each other's GWorld on [DISABLE] (mirrors GenerateAobWrappedXml's suffix).
        var gworldSymbol = $"gworld_base_{Random.Shared.Next(0x100000, 0xFFFFFF):X6}";

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<CheatTable>");
        sb.AppendLine("  <CheatEntries>");
        sb.AppendLine("    <CheatEntry>");
        sb.AppendLine("      <ID>0</ID>");
        sb.AppendLine($"      <Description>\"{EscapeXmlContent(leafSymbol)}\"</Description>");
        sb.AppendLine("      <VariableType>Auto Assembler Script</VariableType>");
        sb.AppendLine("      <AssemblerScript>");

        // ---- ENABLE ----
        sb.AppendLine("[ENABLE]");
        sb.AppendLine("{$lua}");
        sb.AppendLine("if syntaxcheck then return end");
        AppendDebugPreamble(sb);
        sb.AppendLine($"-- {CeLuaHygiene.Attribution}");
        sb.AppendLine();
        if (useAob) AppendAobScanModuleUEHelper(sb);
        AppendCloseLuaEngineHelper(sb);

        // ---- Resolve the GWorld slot (&GWorld) into gworld_base + register it ----
        if (useAob)
        {
            sb.AppendLine($"local entry = {{aob='{aob}', pos={aobPos}, aoblen={aobLen}, symbol='{gworldSymbol}'}}");
            sb.AppendLine("local gworld_base = nil");
            sb.AppendLine("local aob_addr_str = AOBScanModuleUE(process, entry.aob)");
            sb.AppendLine("if aob_addr_str then");
            sb.AppendLine("  local aob_addr_val = tonumber(aob_addr_str, 16)");
            sb.AppendLine("  local relative_offset = readInteger(aob_addr_val + entry.pos, true)");
            sb.AppendLine("  gworld_base = relative_offset + aob_addr_val + entry.aoblen");
            sb.AppendLine("  synchronize(function()");
            sb.AppendLine("    unregisterSymbol(entry.symbol)");
            sb.AppendLine("    registerSymbol(entry.symbol, gworld_base)");
            sb.AppendLine("  end)");
            sb.AppendLine("  dbg(string.format('[GWorldWalk] %s = %X', entry.symbol, gworld_base))");
            sb.AppendLine("else");
            sb.AppendLine("  print('[GWorldWalk] WARNING: GWorld AOB scan failed')");
            sb.AppendLine("end");
        }
        else
        {
            var baseHex = NormalizeHex(gworldSlotAddr);
            sb.AppendLine($"local gworld_base = tonumber('{baseHex}', 16)   -- GWorld slot pointer; UPDATE THIS after a game restart");
            sb.AppendLine("synchronize(function()");
            sb.AppendLine($"  unregisterSymbol('{gworldSymbol}')");
            sb.AppendLine($"  registerSymbol('{gworldSymbol}', gworld_base)");
            sb.AppendLine("end)");
        }
        sb.AppendLine();

        // ---- Walk the spine: *GWorld -> UWorld* -> ... -> leaf ----
        // Every hop guards `addr and addr ~= 0`: CE readQword returns NIL (not 0)
        // on an unreadable page, so a mid-walk null (e.g. a streaming/World-Partition
        // transition) must short-circuit before `readQword(nil + off)` / `nil + off`
        // throws — mirrors the shipped idiom in ue5_freeze_helper.lua.
        sb.AppendLine("local addr = gworld_base and readQword(gworld_base) or 0   -- *GWorld = UWorld*");
        for (int i = 1; i < cleanedBc.Count; i++)
        {
            var step = ProjectBreadcrumb(cleanedBc[i]);
            var note = SanitizeLuaComment(step.Description);
            if (step.DerefAfter)
                sb.AppendLine($"if addr and addr ~= 0 then addr = readQword(addr + 0x{step.Offset:X}) end   -- {note}");
            else
                sb.AppendLine($"if addr and addr ~= 0 then addr = addr + 0x{step.Offset:X} end   -- {note} (inline)");
        }
        sb.AppendLine();

        // ---- Register the leaf (only when the walk produced a live address) ----
        sb.AppendLine("if addr and addr ~= 0 then");
        sb.AppendLine("  synchronize(function()");
        sb.AppendLine($"    unregisterSymbol('{leafSymbol}')");
        sb.AppendLine($"    registerSymbol('{leafSymbol}', addr)");
        sb.AppendLine("  end)");
        sb.AppendLine($"  dbg(string.format('[GWorldWalk] {leafSymbol} = %X', addr))");
        sb.AppendLine("else");
        sb.AppendLine($"  print('[GWorldWalk] WARNING: null pointer mid-walk; {leafSymbol} not registered')");
        sb.AppendLine("end");
        // Close only when the walk produced a live leaf; a null mid-walk keeps the
        // window open so the WARNING stays readable.
        sb.AppendLine("if DEBUG == 0 and addr and addr ~= 0 then closeLuaEngine() end");
        sb.AppendLine("{$asm}");
        sb.AppendLine();

        // ---- DISABLE ----
        sb.AppendLine("[DISABLE]");
        sb.AppendLine("{$lua}");
        sb.AppendLine("if syntaxcheck then return end");
        AppendDebugPreamble(sb);
        sb.AppendLine($"unregisterSymbol('{leafSymbol}')");
        sb.AppendLine($"unregisterSymbol('{gworldSymbol}')");
        sb.AppendLine("if DEBUG == 0 then closeLuaEngine() end");
        sb.AppendLine("{$asm}");

        sb.AppendLine("      </AssemblerScript>");
        sb.AppendLine("    </CheatEntry>");
        sb.AppendLine("  </CheatEntries>");
        sb.AppendLine("</CheatTable>");
        return sb.ToString();
    }

    /// <summary>Strip a leading 0x/0X and surrounding whitespace, leaving bare hex
    /// digits for a Lua tonumber(.,16). Returns "0" for empty input.</summary>
    private static string NormalizeHex(string? addr)
    {
        var s = (addr ?? "").Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
        return string.IsNullOrEmpty(s) ? "0" : s;
    }

    /// <summary>Make a breadcrumb description safe inside a single-line Lua comment
    /// embedded in XML: strip newlines (would end the comment early) and XML-escape
    /// &amp;/&lt;/&gt; (CE un-escapes them back before Lua sees the text).</summary>
    private static string SanitizeLuaComment(string? s)
        => string.IsNullOrEmpty(s) ? ""
            : s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
               .Replace("\r", " ").Replace("\n", " ");

    // ========================================
    // Breadcrumb cleaning
    // ========================================

    /// <summary>
    /// Remove cycles from the breadcrumb navigation path before XML generation.
    ///
    /// A cycle occurs when the user navigates away from an object and later returns to
    /// the same address (e.g., Child -> Parent -> Child again). The intermediate entries
    /// (the detour) are removed, keeping only the shortest path.
    ///
    /// Example: [A, B, C, A, B] -> A appears at 0 and 3 -> remove [1..3] -> [A, B]
    /// This gives the clean CE pointer chain: Root(A) -> field(B) instead of
    /// Root(A) -> field(B) -> Outer(C) -> field(A) -> field(B).
    /// </summary>
    /// <summary>
    /// Collapse runs of CONSECUTIVE breadcrumb crumbs that resolve to the exact
    /// same deref step — same field offset, same resolved address, same name, and
    /// same container/pointer kind. Such a pair is always redundant (you can't move
    /// from object X to X via the same field) and would otherwise emit a duplicate
    /// CE deref level. This happens e.g. when a Locate-in-GWorld path leaves a
    /// synthetic container crumb and the user then re-enters that same container,
    /// stacking two identical <c>Foo(C,+N)</c> crumbs. The LATER crumb is kept (it
    /// carries the live <c>ContainerField</c> for a real container view; an earlier
    /// path-synthetic crumb has none). Unlike the cycle pass below, this also
    /// collapses container-view crumbs (which the cycle pass deliberately skips).
    /// </summary>
    internal static IReadOnlyList<BreadcrumbItem> DedupeConsecutiveBreadcrumbs(
        IReadOnlyList<BreadcrumbItem> breadcrumbs)
    {
        if (breadcrumbs.Count <= 1) return breadcrumbs;
        var result = new List<BreadcrumbItem>(breadcrumbs.Count);
        foreach (var bc in breadcrumbs)
        {
            if (result.Count > 0)
            {
                var prev = result[^1];
                if (prev.FieldOffset == bc.FieldOffset
                    && prev.IsContainerView == bc.IsContainerView
                    && string.Equals(prev.Address, bc.Address, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(prev.FieldName, bc.FieldName, StringComparison.Ordinal))
                {
                    result[^1] = bc;  // keep the later (richer) crumb
                    continue;
                }
            }
            result.Add(bc);
        }
        return result;
    }

    internal static IReadOnlyList<BreadcrumbItem> CleanBreadcrumbs(IReadOnlyList<BreadcrumbItem> breadcrumbs)
    {
        if (breadcrumbs.Count <= 1) return breadcrumbs;

        // First collapse consecutive duplicate crumbs (e.g. a path-synthetic
        // container crumb followed by the user re-entering the same container),
        // then run the cycle-removal pass below.
        var result = new List<BreadcrumbItem>(DedupeConsecutiveBreadcrumbs(breadcrumbs));

        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < result.Count && !changed; i++)
            {
                for (int j = i + 1; j < result.Count; j++)
                {
                    // Container view breadcrumbs (Array/Map/Set) share their parent's address
                    // by design — they represent TArray::Data / TSparseArray::Data dereference,
                    // not a navigation cycle. Skip them as cycle endpoints.
                    if (result[j].IsContainerView) continue;

                    if (string.Equals(result[i].Address, result[j].Address, StringComparison.OrdinalIgnoreCase))
                    {
                        // Found cycle from i to j -- remove entries (i+1) through j inclusive.
                        // Keeps the first occurrence at i and continues with j+1 onward.
                        result.RemoveRange(i + 1, j - i);
                        changed = true;
                        break;
                    }
                }
            }
        }

        return result;
    }

    // ========================================
    // Breadcrumb chain flattening (Collapse chain)
    // ========================================

    /// <summary>
    /// One emit step in the navigation spine: the offset added from the parent's
    /// resolved address, whether a pointer dereference follows the add, and the
    /// node's display name. The normal (nested) and the flattened (collapsed) emit
    /// paths BOTH derive their steps from this single projection, so they can never
    /// disagree about a breadcrumb's pointer semantics -- and any breadcrumb type
    /// is handled identically by both as long as it is either inline (no
    /// dereference) or a single dereference.
    /// </summary>
    private readonly record struct BreadcrumbStep(int Offset, bool DerefAfter, string Description);

    private static BreadcrumbStep ProjectBreadcrumb(BreadcrumbItem bc)
        => new(bc.FieldOffset,
               bc.IsPointerDeref || bc.IsContainerView,
               // Default Description = the clean field/property name. Container-view
               // breadcrumbs carry their "[N x Type]" suffix only in Label, so prefer
               // FieldName and fall back to a suffix-stripped Label when (rarely) the
               // field name wasn't recorded. The +Offset/+Type decoration is applied
               // by the caller (it differs for the folded vs nested spine).
               !string.IsNullOrEmpty(bc.FieldName) ? bc.FieldName : StripDescriptorSuffix(bc.Label));

    /// <summary>
    /// Best-effort: strip a trailing " [..." container descriptor (e.g.
    /// "LocalPlayers [1 x ObjectProperty]" → "LocalPlayers") from a breadcrumb
    /// Label that has no separate FieldName. Leaves anything without that pattern
    /// untouched.
    /// </summary>
    private static string StripDescriptorSuffix(string label)
    {
        if (string.IsNullOrEmpty(label)) return label;
        int br = label.IndexOf(" [", StringComparison.Ordinal);
        return br > 0 ? label.Substring(0, br) : label;
    }

    /// <summary>
    /// Best-effort +Type label for a navigation-spine breadcrumb. Container-view
    /// breadcrumbs carry their source field (ContainerField), so the element /
    /// signature type is recoverable; pointer / inline-struct breadcrumbs do not
    /// carry a class name, so they return null (the spine node then shows just its
    /// name + optional offset). Only used by the nested spine — the folded spine
    /// never carries a type.
    /// </summary>
    private static string? SpineTypeLabel(BreadcrumbItem bc)
    {
        // Container-view spine node (PlayerArray, SupportActionGauge): surface the
        // element / signature type from the source field. Path-derived container
        // crumbs carry no ContainerField (re-hydrated only on Back-nav) → no type.
        if (bc.IsContainerView)
        {
            var cf = bc.ContainerField;
            if (cf == null) return null;
            if (cf.ArrayCount >= 0)
                return !string.IsNullOrEmpty(cf.ArrayStructType) ? cf.ArrayStructType
                     : !string.IsNullOrEmpty(cf.ArrayInnerType) ? cf.ArrayInnerType : null;
            if (cf.MapCount >= 0 && !string.IsNullOrEmpty(cf.MapKeyType))
                return $"{cf.MapKeyType} → {(string.IsNullOrEmpty(cf.MapValueType) ? "?" : cf.MapValueType)}";
            if (cf.SetCount >= 0 && !string.IsNullOrEmpty(cf.SetElemType))
                return cf.SetElemType;
            if (cf.DataTableRowCount >= 0 && !string.IsNullOrEmpty(cf.DataTableStructName))
                return cf.DataTableStructName;
            return null;
        }

        // Pointer-deref / array-element spine node (GameState, [0]=PlayerState,
        // PawnPrivate=BP_PlayerCharacter_C): the resolved target object's class,
        // captured on the breadcrumb at navigation / path-build time.
        return !string.IsNullOrEmpty(bc.TargetClassName) ? bc.TargetClassName : null;
    }

    /// <summary>
    /// Build a CE memory-record Description from a node's bare <paramref name="name"/>,
    /// applying the opt-in "+Offset" / "+Type" decorations the user enabled. With
    /// both toggles off (the default) the result is just the name. The offset is
    /// rendered as bare uppercase hex (matching the "+{offset:X}" used in the CE
    /// Address); the type is the node's class / struct / element type.
    ///
    /// <paramref name="allowType"/> = false suppresses the type even when the
    /// _descShowType toggle is on (the folded Collapse-chain spine never carries a
    /// type — only a name and, optionally, an offset).
    ///
    /// A null/empty <paramref name="typeLabel"/> simply omits the type part, so a
    /// scalar leaf with +Type on still shows just its (optionally offset-annotated)
    /// name without an empty "()".
    /// </summary>
    private static string DecorateDesc(string name, int offset, string? typeLabel, bool allowType = true)
    {
        bool wantOffset = _descShowOffset;
        bool wantType = _descShowType && allowType && !string.IsNullOrEmpty(typeLabel);
        if (!wantOffset && !wantType) return name;

        string annotation = wantOffset && wantType ? $"{offset:X}, {typeLabel}"
            : wantOffset ? $"{offset:X}"
            : typeLabel!;
        return $"{name} ({annotation})";
    }

    /// <summary>
    /// +Type label for a scalar / pointer leaf: the resolved pointer class for
    /// object-shaped properties (Object/Class/Weak/Soft/Lazy/Interface, which
    /// MapCeField emits as an 8-byte hex leaf), else null. Pure scalars / strings
    /// never populate PtrClassName, so they get no type.
    /// </summary>
    private static string? LeafTypeLabel(LiveFieldValue f)
        => !string.IsNullOrEmpty(f.PtrClassName) ? f.PtrClassName : null;

    /// <summary>Result of collapsing a breadcrumb spine into one CE entry.</summary>
    internal sealed record FoldedChain(string Address, int[]? Offsets, string Description, bool ShowAsHex);

    /// <summary>
    /// Collapse the navigation spine (every breadcrumb after the root) into a
    /// SINGLE CE multi-level-pointer entry, turning a deep GWorld -> ... -> target
    /// chain into base -> one folded node -> target field instead of N nested
    /// groups. Returns null when there are fewer than 2 navigation breadcrumbs to
    /// merge (folding a single node just reproduces the normal output) -- the
    /// caller then emits the nested chain unchanged.
    ///
    /// Math (verified against CE's pointer resolution; see docs/export-formats.md).
    /// CE resolves an entry with Address=+Xbase and Offsets O[0..m-1] as:
    ///   start = parentResolved + Xbase;  p = deref(start);
    ///   for k = m-1..1: p = deref(p + O[k]);  finalAddr = p + O[0]
    /// i.e. the FIRST listed offset O[0] is the OUTERMOST (added without a final
    /// deref) and the LAST listed offset O[m-1] is the first deref after the base.
    /// Folding a spine of (offset, derefAfter) steps:
    ///   - accumulate each run of offsets up to (and including) a deref step into D[]
    ///   - F = the trailing inline run after the last deref (0 if it ended on a deref)
    ///   - Address = +D[0];  Offsets (document order) = [F] ++ reverse(D[1..])
    /// A pure-inline spine (no deref at all) folds to Address=+F with no Offsets.
    ///
    /// Robustness: this reads ONLY (Offset, DerefAfter) per step and never inspects
    /// the leaf-field subtree, so new expandable field types emitted by EmitFields
    /// are neither seen nor affected. Every breadcrumb the app creates is inline or
    /// single-deref (DataTable's 2-level deref is modelled as TWO single-deref
    /// breadcrumbs), so the fold is total over the current breadcrumb model.
    /// </summary>
    internal static FoldedChain? FoldBreadcrumbSpine(IReadOnlyList<BreadcrumbItem> cleanedBc)
    {
        // cleanedBc[0] is the root/base (kept as-is by the caller). The spine is
        // cleanedBc[1..]; need >= 2 nodes there to actually merge anything.
        if (cleanedBc.Count < 3) return null;

        var d = new List<int>(cleanedBc.Count - 1);   // deref-terminated segment sums
        int seg = 0;
        var descParts = new List<string>(cleanedBc.Count - 1);
        for (int i = 1; i < cleanedBc.Count; i++)
        {
            var step = ProjectBreadcrumb(cleanedBc[i]);
            // The folded spine accepts +Offset on each merged hop but never +Type
            // (allowType:false) — the collapsed nodes only show name (+offset).
            descParts.Add(DecorateDesc(step.Description, step.Offset, null, allowType: false));
            seg += step.Offset;
            if (step.DerefAfter) { d.Add(seg); seg = 0; }
        }
        int f = seg;

        // Joined spine so the user can see exactly what was collapsed (decision #1).
        var description = string.Join(" ▸ ", descParts);

        if (d.Count == 0)
        {
            // Pure-inline spine: a single horizontal offset, no dereference.
            return new FoldedChain($"+{f:X}", null, description, ShowAsHex: false);
        }

        // CE document order: outermost (final, no-deref) offset F first, then the
        // deref offsets in reverse depth order. Summed hex per offset (decision #2).
        var offsets = new int[d.Count];
        offsets[0] = f;
        for (int k = 1; k < d.Count; k++)
            offsets[k] = d[d.Count - k];
        return new FoldedChain($"+{d[0]:X}", offsets, description, ShowAsHex: true);
    }

    // ========================================
    // Private helpers
    // ========================================

    /// <summary>
    /// Emit all leaf fields, handling scalars, resolved structs, and navigable placeholders.
    /// All fields use Address=+{field.Offset} (no Offsets) because parent breadcrumb/group
    /// already resolved any pointer dereference via its own Offsets=[0].
    /// </summary>
    private static void EmitFields(StringBuilder sb, string indent,
        IReadOnlyList<LiveFieldValue> fields,
        Dictionary<string, List<LiveFieldValue>>? resolvedStructs,
        Dictionary<string, List<LiveFieldValue>>? resolvedInstances = null)
    {
        // Track nesting depth so the system-component noise filter (below) fires only
        // for recursively-resolved children (depth > 1), never the top-level fields the
        // user explicitly selected (depth == 1). Plain inc/dec (no try/finally) mirrors
        // _emitPointerDepth: EmitFields doesn't throw under normal use, and the next
        // Generate* resets _emitDepth, so an exception can't leak a stale depth across calls.
        _emitDepth++;
        foreach (var field in fields)
        {
            // Global safety budget: a dense object graph can fan the drilldown out
            // combinatorially. Once the cap is hit, stop emitting (the per-path cycle
            // guard + depth cap don't bound breadth) so the StringBuilder can't grow
            // to OutOfMemory. Every recursive emitter funnels its children through
            // EmitFields, so this single break bounds the whole tree.
            if (_emitEntryCount >= MaxEmitEntries) { _emitTruncated = true; break; }

            // Guessed ("Guess?") fields are CE-exportable only when the user explicitly
            // focuses a single guessed field (Copy CE Field sets _includeGuessed). In any
            // bulk/container/drilldown context (_includeGuessed=false, incl. all recursive
            // calls) drop them, so a struct/pointer export never silently dumps a pile of
            // speculative guessed rows the user didn't ask for.
            if (field.IsGuessed && !_includeGuessed) continue;

            // System/engine asset noise filter (default on at the Live Walker call sites):
            // skip a pointer field whose pointee class is a known engine asset
            // (Widget / SoundBase / Texture / Material / Particle / Niagara / AnimInstance …)
            // — but ONLY while recursing into resolved children (depth > 1). The top-level
            // fields the user explicitly selected (depth == 1) are never dropped. Tests
            // PtrClassName only, so a plain struct member (e.g. a GAS FGameplayAttributeData,
            // whose StructTypeName we deliberately ignore) is never affected by this.
            if (_excludeSystemComponents && _emitDepth > 1
                && CeExportNoiseFilter.IsSystemComponent(field.PtrClassName))
            {
                _systemFieldsSkipped++;
                continue;
            }

            // Check if this StructProperty has pre-resolved children. Key is
            // StructDataAddr (absolute address) — unique across instances, so
            // the same dict can serve nested struct fields inside drilled
            // pointer targets without offset-collision.
            if (field.TypeName == "StructProperty"
                && resolvedStructs != null
                && !string.IsNullOrEmpty(field.StructDataAddr)
                && resolvedStructs.TryGetValue(field.StructDataAddr, out var structChildren)
                && structChildren.Count > 0)
            {
                // Flatten (opt-in): collapse a struct ONE level — its children become sibling
                // leaves at the combined offset instead of a nested parent group. Two gates,
                // either of which fires the SAME promotion (EmitFlattenedStruct):
                //  • GAS: a GameplayAttributeData struct whose children are all CE-scalar (the
                //    original BaseValue/CurrentValue case; pointers count as 8B scalars here but
                //    GAS attrs never have any).
                //  • Leaf: ANY terminal struct whose entire flattened subtree is primitive inline
                //    scalars (float/int/bool/enum) — NOT pointers, strings, FName, containers, or
                //    unresolved structs. Subsumes GAS and naturally flattens FVector/FRotator.
                // Otherwise the struct keeps its normal nested group.
                bool gasFlatten = _flattenGasAttributes && IsGasAttributeStruct(field)
                                  && structChildren.All(c => MapCeField(c) != null);
                bool leafFlatten = _flattenLeafStructs
                                   && structChildren.All(IsPrimitiveLeafField);
                //  • Record: ANY terminal struct whose flattened subtree is ALL terminal leaves
                //    (the primitive scalars PLUS NameProperty + FString family). A superset of
                //    leafFlatten; EmitFlattenedStruct renders a string child as a CE String leaf
                //    (Offsets=[0]) and a name child as a 4-byte int. Save-data "record" structs.
                bool recordFlatten = _flattenLeafRecords
                                     && structChildren.All(IsTerminalLeafField);
                if (gasFlatten || leafFlatten || recordFlatten)
                    EmitFlattenedStruct(sb, indent, field, structChildren);
                else
                    EmitResolvedStruct(sb, indent, field, structChildren);
                continue;
            }

            // Pointer drill-down: ObjectProperty / ClassProperty / Weak/Soft/Lazy/Interface
            // with a pre-resolved target → emit GroupHeader+Offsets=[0] and recurse into
            // the target's fields. CE will dereference *(parent + field.Offset) and lay
            // the children out at their natural offsets within the target.
            //
            // Lookup is by PtrAddress so two fields pointing to the same instance share
            // the same resolved field list (this is also what enables cycle protection
            // from ResolvePointerInstancesAsync).
            if (resolvedInstances != null
                && IsObjectPropertyType(field.TypeName)
                && !string.IsNullOrEmpty(field.PtrAddress)
                && field.PtrAddress != "0x0"
                && resolvedInstances.TryGetValue(field.PtrAddress, out var ptrChildren)
                && ptrChildren.Count > 0)
            {
                EmitDrilledPointer(sb, indent, field, ptrChildren, resolvedStructs, resolvedInstances);
                continue;
            }

            // OptionalProperty: TOptional<T> wraps an inner value at field+0.
            // - TOptional<Struct>: walker stamps StructDataAddr/StructClassAddr
            //   when set, so we can render the struct sub-fields inline (no
            //   pointer dereference; struct lives directly at field+0).
            // - All other inner shapes (scalar / pointer / weak / etc): emit
            //   as a flat 8-byte hex leaf so the user has a watchable address
            //   for the value slot. The trailing bIsSet byte (when present)
            //   isn't surfaced separately — UE intrusively encodes it for
            //   FString / FName / FText / pointer types, and the byte's
            //   location for non-intrusive scalars depends on inner T size
            //   that's not exposed to the C# emitter.
            if (field.TypeName == "OptionalProperty")
            {
                if (resolvedStructs != null
                    && !string.IsNullOrEmpty(field.StructDataAddr)
                    && resolvedStructs.TryGetValue(field.StructDataAddr, out var optStructChildren)
                    && optStructChildren.Count > 0)
                {
                    EmitResolvedStruct(sb, indent, field, optStructChildren);
                }
                else
                {
                    // Flat leaf — at minimum CE shows the first 8 bytes of
                    // the optional slot so the user can poke at the value.
                    EmitLeaf(sb, indent, DecorateDesc(field.Name, field.Offset, LeafTypeLabel(field)),
                        new CeFieldInfo("8 Bytes", ShowAsHex: true),
                        $"+{field.Offset:X}", null);
                }
                continue;
            }

            // ArrayProperty: emit as group with element children (Phase C).
            // Multicast delegates are exposed as implicit DelegateProperty arrays
            // (the field's first 8 bytes are the InvocationList::Data pointer,
            // matching TArray addressing — Offsets=[0] derefs it correctly).
            if (field.ArrayCount >= 0
                && (field.TypeName == "ArrayProperty"
                    || field.TypeName == "MulticastInlineDelegateProperty"
                    || field.TypeName == "MulticastDelegateProperty"))
            {
                EmitArrayProperty(sb, indent, field);
                continue;
            }

            // MapProperty: emit as group with key/value children per element
            if (field.TypeName == "MapProperty" && field.MapCount >= 0)
            {
                EmitMapProperty(sb, indent, field);
                continue;
            }

            // SetProperty: emit as group with element children
            if (field.TypeName == "SetProperty" && field.SetCount >= 0)
            {
                EmitSetProperty(sb, indent, field);
                continue;
            }

            // DataTableRows: emit as 2-level deref group (TSparseArray.Data → uint8* row → fields)
            if (field.TypeName == "DataTableRows" && field.DataTableRowCount > 0)
            {
                EmitDataTableRowsProperty(sb, indent, field);
                continue;
            }

            // A guessed ("Guess?") field's Description is left exactly as its name —
            // it carries its own guessed-type marker and the user explicitly asked
            // for it untouched. Every other leaf gets +Offset; object-shaped pointer
            // leaves (emitted as an 8-byte hex leaf by MapCeField) also carry their
            // resolved class under +Type.
            string ScalarDesc() => field.IsGuessed
                ? field.Name : DecorateDesc(field.Name, field.Offset, LeafTypeLabel(field));

            // FString-family (StrProperty / Utf8StrProperty / AnsiStrProperty): emit as a
            // CE String leaf with pointer deref to the Data buffer. Wide (UTF-16) vs byte,
            // and UTF-8 byte (CodePage), are selected by the Unicode/CodePage flags.
            if (IsStringProperty(field.TypeName))
            {
                EmitStringLeaf(sb, indent, ScalarDesc(), $"+{field.Offset:X}",
                    offsets: [0], unicode: field.TypeName == "StrProperty",
                    codepage: field.TypeName == "Utf8StrProperty");
                continue;
            }

            var ceField = MapCeField(field);
            if (ceField != null)
            {
                // Non-array EnumProperty/ByteProperty: DropDownList support
                var baseDesc = ScalarDesc();
                var ddLink = TryGetEnumDropDown(field, baseDesc);
                EmitLeaf(sb, indent, ddLink.desc ?? baseDesc, ceField,
                    $"+{field.Offset:X}", null,
                    dropDownContent: ddLink.content,
                    dropDownListLink: ddLink.link);
            }
            else if (field.IsNavigable)
            {
                EmitNavigableField(sb, indent, field,
                    $"+{field.Offset:X}", null);
            }
        }
        _emitDepth--;
    }

    /// <summary>
    /// Check if a non-array enum field should have a DropDownList.
    /// Returns (content, link, desc): content for first occurrence, link for shared reuse,
    /// desc = unique description to use in the CE entry (ensures DropDownListLink matching).
    /// <paramref name="baseDesc"/> is the already-decorated entry name; the unique
    /// link key is derived from it so the +Offset/+Type decoration stays consistent
    /// between the DropDownList owner and any leaves linking to it.
    /// </summary>
    private static (string? content, string? link, string? desc) TryGetEnumDropDown(
        LiveFieldValue field, string baseDesc)
    {
        if (field.TypeName is not ("EnumProperty" or "ByteProperty")) return (null, null, null);
        if (field.EnumEntries is not { Count: > 0 }) return (null, null, null);

        var maxDd = _maxDropDownEntries > 0 ? _maxDropDownEntries : 512;
        if (field.EnumEntries.Count > maxDd) return (null, null, null);

        _dropDownOwners ??= new Dictionary<string, string>();
        var enumKey = field.EnumAddr;

        if (!string.IsNullOrEmpty(enumKey) && _dropDownOwners.TryGetValue(enumKey, out var existing))
        {
            // Shared: link to first occurrence
            return (null, existing, null);
        }

        // First occurrence: emit DropDownList content; use unique description for link matching
        var content = BuildDropDownContent(field.EnumEntries.Select(e => (e.Value, e.Name)));
        var desc = EnsureUniqueDropDownDesc(baseDesc);
        if (!string.IsNullOrEmpty(enumKey))
            _dropDownOwners[enumKey] = desc;
        return (content, null, desc);
    }

    /// <summary>
    /// Emit a StructProperty with pre-resolved inner fields as a CE group.
    /// Struct is inline (not a pointer), so Address=+{structOffset}, no Offsets.
    /// Children are flattened (nested structs already expanded with dot-prefixed names).
    /// Each child's Offset is relative to the struct start.
    /// </summary>
    /// <summary>
    /// Emit an ObjectProperty / Class / Weak / Soft / Lazy / Interface field whose
    /// pointer target was pre-resolved by ResolvePointerInstancesAsync. The leaf
    /// becomes a GroupHeader with Address=+{fieldOffset}, Offsets=[0] (CE
    /// dereferences *(parent + fieldOffset)) and the resolved target's fields as
    /// children at their natural offsets within the target instance.
    ///
    /// Reuses the standard EmitFields recursion so nested struct flattening,
    /// container expansion, enum DropDownLists, and further pointer drill-downs
    /// all work uniformly inside the target — depth was already capped during
    /// the resolve phase, so this loop terminates.
    /// </summary>
    private static void EmitDrilledPointer(StringBuilder sb, string indent,
        LiveFieldValue field,
        List<LiveFieldValue> children,
        Dictionary<string, List<LiveFieldValue>>? resolvedStructs,
        Dictionary<string, List<LiveFieldValue>> resolvedInstances)
    {
        // Global emit budget (see MaxEmitEntries): once tripped, drilled pointers
        // stop expanding entirely — emit nothing and unwind. Caller loops that don't
        // route through EmitFields (object-array elements) reach here per element, so
        // this is the second backstop against the StringBuilder OOM.
        if (_emitEntryCount >= MaxEmitEntries) { _emitTruncated = true; return; }

        // ---- Shared-object dedup (see _dedupShared) ----
        // Each distinct object's subtree is emitted ONCE; a later reference to the
        // same PtrAddress becomes a flat pointer leaf marked "(shared)" instead of
        // re-expanding the whole subtree (the combinatorial OOM source). _emittedInstances
        // is global + never popped, so a back-edge to an already-emitted ancestor is also
        // caught here — making it a strict superset of the _emitPath cycle guard. We only
        // CHECK here; the address is MARKED emitted once we actually commit to the group
        // below, so a cycle/depth-elided first hit doesn't suppress a later full expansion.
        bool dedupTrack = _dedupShared && !string.IsNullOrEmpty(field.PtrAddress)
                          && field.PtrAddress != "0x0";
        if (dedupTrack && _emittedInstances != null
            && _emittedInstances.Contains(field.PtrAddress))
        {
            EmitLeaf(sb, indent,
                DecorateDesc(field.Name, field.Offset, field.PtrClassName) + " (shared)",
                new CeFieldInfo("8 Bytes", ShowAsHex: true),
                $"+{field.Offset:X}", null);
            return;
        }

        // ---- Cycle / depth guards ----
        // ResolvePointerInstancesAsync caches resolved[X] keyed by PtrAddress, so
        // back-pointers (UWorld -> PersistentLevel -> OwningWorld) are still
        // populated in the dictionary. Without an emit-side path check, drilling
        // would oscillate between A's and B's child lists indefinitely.
        _emitPath ??= new HashSet<string>(StringComparer.Ordinal);
        bool alreadyOnPath = !string.IsNullOrEmpty(field.PtrAddress)
                             && _emitPath.Contains(field.PtrAddress);
        bool depthExceeded = _emitPointerDepth >= MaxEmitPointerDepth;

        if (alreadyOnPath || depthExceeded)
        {
            // Emit a flat 8-byte hex leaf so the user keeps a watchable address
            // for the pointer, instead of nothing or an unbounded group. The
            // description tags the reason so it's not mysterious in CE.
            var reason = alreadyOnPath ? " (cycle elided)" : " (max drill depth reached)";
            EmitLeaf(sb, indent,
                DecorateDesc(field.Name, field.Offset, field.PtrClassName) + reason,
                new CeFieldInfo("8 Bytes", ShowAsHex: true),
                $"+{field.Offset:X}", null);
            return;
        }

        // Feature B (Collapse single-leaf pointers): the resolved target is a SINGLE terminal leaf
        // (scalar / FName / FString) — the pointer node + its lone child are two CE rows for one
        // value. Collapse to ONE record at the pointer field with a deref chain (no group). We do
        // NOT mark the pointer emitted (dedup) or push the cycle path: a leaf has no subtree, so
        // later references may collapse independently and there is nothing to recurse into.
        if (_collapseLeafPointers && children.Count == 1 && IsTerminalLeafField(children[0]))
        {
            EmitOneDerefLeaf(sb, indent, field, children[0]);
            return;
        }

        // Committing to the full expansion — mark this object emitted so any later
        // reference to it dedups to a "(shared)" flat leaf.
        if (dedupTrack)
            (_emittedInstances ??= new HashSet<string>(StringComparer.Ordinal)).Add(field.PtrAddress);

        // Default Description = the bare field name; the +Type opt-in re-adds the
        // resolved class so the user can tell BP_X (UCharacter) from BP_X (UPawn).
        var description = DecorateDesc(field.Name, field.Offset, field.PtrClassName);

        // Address=+{fieldOffset}, Offsets=[0] — CE dereferences the pointer
        // and treats children's +{N} as offsets from the resolved target.
        EmitGroupOpen(sb, indent, description, $"+{field.Offset:X}", new[] { 0 },
            showAsHex: true);

        // Push self onto the path before recursing; pop on exit (try/finally
        // is overkill — EmitFields doesn't throw under normal use, and a
        // missing pop just makes the SAME pointer non-drillable next time
        // within this call, which is benign).
        bool pushed = !string.IsNullOrEmpty(field.PtrAddress)
                      && _emitPath.Add(field.PtrAddress);
        _emitPointerDepth++;

        var childIndent = indent + "  ";
        EmitFields(sb, childIndent, children, resolvedStructs, resolvedInstances);

        _emitPointerDepth--;
        if (pushed) _emitPath.Remove(field.PtrAddress);

        EmitGroupClose(sb, indent);
    }

    private static void EmitResolvedStruct(StringBuilder sb, string indent,
        LiveFieldValue structField, List<LiveFieldValue> children)
    {
        // Struct is inline: just offset from parent, no dereference
        var address = $"+{structField.Offset:X}";

        // Default Description = the bare field name; +Type re-adds the struct type.
        var description = DecorateDesc(structField.Name, structField.Offset, structField.StructTypeName);

        EmitGroupOpen(sb, indent, description, address, null);
        var childIndent = indent + "  ";

        // Children's offsets are relative to the struct base; the struct group is
        // at +structOffset, so EmitFields lays each child at +childOffset under it.
        // Delegating to EmitFields (instead of a bespoke loop) means struct children
        // that are themselves structs / pointers / Maps / Sets expand richly when
        // they were resolved — the core of the drilldown contract.
        EmitFields(sb, childIndent, children, _resolvedStructsState, _resolvedInstancesState);

        EmitGroupClose(sb, indent);
    }

    /// <summary>
    /// True when <paramref name="field"/> is a GAS <c>FGameplayAttributeData</c> struct
    /// member — the value container every GAS attribute (HealthPoint, Mana, …) stores as a
    /// StructProperty of {float BaseValue, float CurrentValue}. UE reflection drops the
    /// leading 'F' ("GameplayAttributeData"); the F-prefixed spelling is accepted too so a
    /// future DLL/format change can't silently disable the flatten.
    /// </summary>
    private static bool IsGasAttributeStruct(LiveFieldValue field)
        => field.TypeName == "StructProperty"
           && field.StructTypeName is "GameplayAttributeData" or "FGameplayAttributeData";

    /// <summary>
    /// True when <paramref name="field"/> is a PRIMITIVE inline scalar — a value CE reads
    /// directly at the field address, with no pointer dereference and no buffer. This is the
    /// gate for the "Flatten primitive-leaf structs" option (<see cref="_flattenLeafStructs"/>):
    /// a struct flattens only when EVERY field in its already-flattened subtree passes this.
    ///
    /// Included: float/double, signed/unsigned ints (8–64), byte, bool, enum — plus the
    /// "Guess What" numeric labels (Float?/Int32?/…) so a guessed-scalar struct still flattens.
    /// EXCLUDED on purpose (each keeps the struct grouped):
    ///   • Pointer/object shapes (Object/Class/Weak/Soft/Lazy/Interface/Delegate/Multicast,
    ///     Text, "Pointer?") — "pointers stay unflattened".
    ///   • Strings (StrProperty/Utf8StrProperty/AnsiStrProperty) and FName — pointer-backed /
    ///     identifier values, not the int/float the user asked to flatten.
    ///   • Containers (Array/Map/Set/Optional) and StructProperty — not a leaf.
    /// Every type returned true here also maps non-null in <see cref="MapCeField"/>, so the
    /// promoted leaf always renders as a real CE scalar.
    /// </summary>
    private static bool IsPrimitiveLeafField(LiveFieldValue field) => field.TypeName is
        "FloatProperty" or "DoubleProperty"
        or "Int8Property" or "Int16Property" or "IntProperty" or "Int64Property"
        or "ByteProperty" or "UInt16Property" or "UInt32Property" or "UInt64Property"
        or "BoolProperty" or "EnumProperty"
        // "Guess What" heuristic scalar labels (Ubel::GuessGapTypes); "Pointer?" is excluded.
        or "Float" or "Float?" or "Double" or "Double?"
        or "Int32" or "Int32?" or "Int16" or "Int16?" or "Int64" or "Int64?"
        or "Byte" or "Byte?";

    /// <summary>
    /// True when <paramref name="field"/> is a TERMINAL LEAF — a value that never expands into
    /// further CE rows. The gate for the "Flatten leaf records" option (<see cref="_flattenLeafRecords"/>),
    /// a STRICT SUPERSET of <see cref="IsPrimitiveLeafField"/>: every primitive scalar PLUS
    /// <c>NameProperty</c> (an FName index pair, rendered as a 4-byte int) and the FString family
    /// (<c>StrProperty</c>/<c>Utf8StrProperty</c>/<c>AnsiStrProperty</c>, rendered as a CE String leaf
    /// with one Data-pointer deref). These are leaves: they hold a single watchable value and have
    /// no sub-fields to drill. STILL EXCLUDED (each keeps the struct grouped): pointers/objects,
    /// <c>TextProperty</c> (FText's internal chain has no clean CE encoding), containers, and
    /// StructProperty. <see cref="EmitFlattenedStruct"/> renders the string children specially.
    /// </summary>
    private static bool IsTerminalLeafField(LiveFieldValue field) =>
        IsPrimitiveLeafField(field)
        || field.TypeName == "NameProperty"
        || IsStringProperty(field.TypeName);

    /// <summary>
    /// Flatten a struct ONE level: emit each resolved child as a sibling leaf instead of a
    /// parent group + children. Shared by the GAS flatten, the primitive-leaf flatten, and the
    /// "Flatten leaf records" superset. The child's CE Address is the COMBINED offset (struct
    /// member offset + child offset within the struct) — identical to what CE would resolve
    /// through the group (inline struct, no dereference), so the watched address is unchanged;
    /// only the tree is flatter. Description = "{Struct} ▸ {Child}" with per-segment +Offset
    /// honouring DescShowOffset; the struct's type label is appended ONCE at the end (DescShowType)
    /// so the merged text stays readable.
    ///
    /// Child rendering by type (the record-flatten gate may add string/name children that the
    /// primitive-only gates never produce):
    ///   • FString family (StrProperty/Utf8StrProperty/AnsiStrProperty) → a CE String leaf at
    ///     +combinedOffset with Offsets=[0] — one deref of the inline FString.Data pointer (the
    ///     header sits at the combined offset; the chars are one pointer hop away). MapCeField
    ///     returns null for these (they are normally handled by EmitStringLeaf), so they MUST take
    ///     this branch or they'd mis-render as an 8-byte hex blob.
    ///   • Everything else (scalar/enum/FName) → EmitLeaf with MapCeField, no Offsets (0-deref).
    /// </summary>
    private static void EmitFlattenedStruct(StringBuilder sb, string indent,
        LiveFieldValue structField, List<LiveFieldValue> children)
    {
        // Struct type appended once at the very end (per the design: repeating the class name
        // on every segment reads awkwardly). Honours DescShowType only.
        var typeSuffix = (_descShowType && !string.IsNullOrEmpty(structField.StructTypeName))
            ? $" ({structField.StructTypeName})"
            : "";

        foreach (var child in children)
        {
            // Same global emit budget as EmitFields — each promoted child is one CE entry.
            if (_emitEntryCount >= MaxEmitEntries) { _emitTruncated = true; break; }

            // Per-segment +Offset (allowType:false so the type isn't repeated on each part);
            // the struct type is appended once via typeSuffix below. "▸" = ▸ (U+25B8).
            var seg1 = DecorateDesc(structField.Name, structField.Offset, null, allowType: false);
            var seg2 = DecorateDesc(child.Name, child.Offset, null, allowType: false);
            var desc = $"{seg1} ▸ {seg2}{typeSuffix}";

            // Inline struct: child address = struct member offset + child-in-struct offset.
            var combinedOffset = structField.Offset + child.Offset;

            // FString-family child: render as a CE String leaf with a single Data-pointer deref
            // (Offsets=[0]), the same encoding EmitFields uses for a top-level string — the FString
            // header is inline at the combined offset, its chars one hop away.
            if (IsStringProperty(child.TypeName))
            {
                EmitStringLeaf(sb, indent, desc, $"+{combinedOffset:X}",
                    offsets: [0], unicode: child.TypeName == "StrProperty",
                    codepage: child.TypeName == "Utf8StrProperty");
                continue;
            }

            // Scalar / enum / FName: a direct value at the combined offset, no dereference.
            // The primitive-only gates guarantee MapCeField != null; stay defensive anyway.
            var ceField = MapCeField(child) ?? new CeFieldInfo("8 Bytes", ShowAsHex: true);
            EmitLeaf(sb, indent, desc, ceField, $"+{combinedOffset:X}", null);
        }
    }

    /// <summary>
    /// Collapse a drilled pointer whose target is a SINGLE terminal leaf into ONE CE record at the
    /// pointer field (Feature B — the "pointer-to-string" case). The pointer is dereferenced through the
    /// leaf's Offsets, so the watched address is the SAME value CE would reach through the old
    /// group + child; only the tree is flatter. Description = "{ptr} ▸ {child}" (per-segment +Offset;
    /// the pointer class type appended once via +Type). CE pointer model (CeXmlExportService.cs
    /// header math): Address is dereferenced first, then offsets apply with O[0] outermost.
    ///   • FString-family child → CE String leaf, Address=+ptrOff, Offsets=[0, childOff] — TWO
    ///     derefs: follow the pointer, then the inline FString.Data buffer at +childOff.
    ///   • scalar / FName child → Address=+ptrOff, Offsets=[childOff] — ONE deref: follow the
    ///     pointer; the value sits inline at +childOff within the target.
    /// </summary>
    private static void EmitOneDerefLeaf(StringBuilder sb, string indent,
        LiveFieldValue ptrField, LiveFieldValue child)
    {
        var seg1 = DecorateDesc(ptrField.Name, ptrField.Offset, null, allowType: false);
        var seg2 = DecorateDesc(child.Name, child.Offset, null, allowType: false);
        var typeSuffix = (_descShowType && !string.IsNullOrEmpty(ptrField.PtrClassName))
            ? $" ({ptrField.PtrClassName})"
            : "";
        var desc = $"{seg1} ▸ {seg2}{typeSuffix}";
        var address = $"+{ptrField.Offset:X}";

        if (IsStringProperty(child.TypeName))
        {
            EmitStringLeaf(sb, indent, desc, address,
                offsets: [0, child.Offset], unicode: child.TypeName == "StrProperty",
                codepage: child.TypeName == "Utf8StrProperty");
        }
        else
        {
            var ceField = MapCeField(child) ?? new CeFieldInfo("8 Bytes", ShowAsHex: true);
            EmitLeaf(sb, indent, desc, ceField, address, offsets: [child.Offset]);
        }
    }

    /// <summary>
    /// Emit an ArrayProperty as a CE group with per-element children.
    /// Scalar arrays (Float, Int, Bool, Byte, Enum, Name) get individual leaf entries.
    /// Non-scalar arrays (Struct, Object) or empty arrays emit as placeholder only.
    ///
    /// TArray addressing:
    /// - Group header: Address=+{fieldOffset}, Offsets=[0] → dereferences TArray.Data pointer
    /// - Element children: Address=+{N*elemSize} → simple offset from the dereferenced Data pointer
    /// </summary>
    private static void EmitArrayProperty(StringBuilder sb, string indent,
        LiveFieldValue field)
    {
        // Default Description = the bare field name. The old "[N x Type (SizeB)]"
        // descriptor is dropped; the +Type opt-in re-adds just the element type
        // (struct type for struct arrays, else the inner property type).
        var typeLabel = !string.IsNullOrEmpty(field.ArrayStructType)
            ? field.ArrayStructType : field.ArrayInnerType;
        var desc = DecorateDesc(field.Name, field.Offset, typeLabel);

        // Phase F: struct array with resolved sub-fields → per-element group emission
        if (field.ArrayInnerType == "StructProperty"
            && field.ArrayElements is { Count: > 0 }
            && field.ArrayElements[0].StructFields is { Count: > 0 })
        {
            EmitStructArrayProperty(sb, indent, field, desc);
            return;
        }

        // StructProperty array without resolved sub-fields:
        // Still emit with Offsets=[0] for TArray.Data deref and per-element placeholder groups.
        // CE users can manually add sub-entries for the struct fields within each element.
        if (field.ArrayInnerType == "StructProperty"
            && field.ArrayCount > 0 && field.ArrayElemSize > 0)
        {
            EmitGroupOpen(sb, indent, desc, $"+{field.Offset:X}", new[] { 0 });
            var elemIndent = indent + "  ";

            if (field.ArrayElements is { Count: > 0 })
            {
                foreach (var elem in field.ArrayElements)
                {
                    int elemByteOffset = elem.Index * field.ArrayElemSize;
                    EmitGroupPlaceholder(sb, elemIndent,
                        DecorateDesc($"[{elem.Index}]", elemByteOffset, field.ArrayStructType),
                        $"+{elemByteOffset:X}", null);
                }
            }

            EmitGroupClose(sb, indent);
            return;
        }

        // Phase G: TArray<TSoftObjectPtr/TSoftClassPtr> — emit per-element
        // struct group with WeakPtr leaf at +0 + FName leaf(s) at +0x10 (and
        // +0x10+fnameSize for UE5.1+'s FTopLevelAssetPath layout). Without
        // this, the inner element collapses to a single 8B WeakPtr hex blob
        // and the FSoftObjectPath::AssetPathName / PackageName is invisible.
        // Soft array layout metadata (fnameSize + FTopLevelAssetPath flag)
        // comes from the DLL — see Ubel.cpp Phase G handler.
        if ((field.ArrayInnerType == "SoftObjectProperty"
             || field.ArrayInnerType == "SoftClassProperty")
            && field.SoftArrayFNameSize > 0
            && field.ArrayCount > 0 && field.ArrayElemSize > 0)
        {
            EmitSoftObjectArrayProperty(sb, indent, field, desc);
            return;
        }

        // TArray<ObjectProperty> with pre-resolved element targets → per-element
        // drilled group (one EmitDrilledPointer per pointer slot). The array group
        // derefs TArray.Data (Offsets=[0]); each element pointer is then dereffed by
        // its own Offsets=[0] so the target's fields lay out at their natural
        // offsets. Elements whose target wasn't resolved (depth/cycle/limit, or a
        // null slot) fall back to the flat 8-byte leaf. Only taken when at least
        // one element actually resolved, so depth=0 / unresolved arrays keep the
        // prior generic-leaf behavior below.
        if (IsRawObjectPtrArrayInner(field.ArrayInnerType)
            && _resolvedInstancesState != null
            && field.ArrayElements is { Count: > 0 }
            && field.ArrayElemSize > 0
            && field.ArrayElements.Any(e => !string.IsNullOrEmpty(e.PtrAddress)
                    && e.PtrAddress != "0x0"
                    && _resolvedInstancesState.ContainsKey(e.PtrAddress)))
        {
            EmitObjectArrayProperty(sb, indent, field, desc);
            return;
        }

        // Map inner type to CE type
        var ceElem = MapInnerTypeToCeField(field.ArrayInnerType);

        // Non-scalar, empty, or no inline elements → placeholder only (no deref needed)
        if (ceElem == null || field.ArrayCount <= 0
            || field.ArrayElements == null || field.ArrayElements.Count == 0)
        {
            EmitGroupPlaceholder(sb, indent, desc, $"+{field.Offset:X}", null);
            return;
        }

        // CE DropDownList: determine if this array should have dropdown support.
        // DropDownList goes on the parent GroupHeader; all children use DropDownListLink.
        _dropDownOwners ??= new Dictionary<string, string>();
        string? dropDownContent = null;
        string? dropDownLinkTarget = null;
        var maxDd = _maxDropDownEntries > 0 ? _maxDropDownEntries : 512;
        bool isEnumArray = field.ArrayInnerType is "EnumProperty" or "ByteProperty"
            && field.ArrayEnumEntries is { Count: > 0 } && field.ArrayEnumEntries.Count <= maxDd;
        bool isNameArray = field.ArrayInnerType == "NameProperty"
            && field.ArrayElements is { Count: > 0 } && field.ArrayElements.Count <= maxDd;
        // Fallback: enum/byte array with per-element enum names but no full UEnum entries list.
        // Build DropDownList from element values (like NameProperty), no sharing.
        bool isEnumFallback = !isEnumArray
            && field.ArrayInnerType is "EnumProperty" or "ByteProperty"
            && field.ArrayElements is { Count: > 0 } && field.ArrayElements.Count <= maxDd
            && field.ArrayElements.Any(e => !string.IsNullOrEmpty(e.EnumName));

        if (isEnumArray)
        {
            var enumKey = field.ArrayEnumAddr;
            if (!string.IsNullOrEmpty(enumKey) && _dropDownOwners.TryGetValue(enumKey, out var existing))
            {
                // Shared: this parent and all children link to first occurrence's parent
                dropDownLinkTarget = existing;
            }
            else
            {
                // First occurrence: parent gets DropDownList, children link to this parent.
                // Ensure unique description (CE uses Description text as DropDownListLink key).
                dropDownContent = BuildDropDownContent(
                    field.ArrayEnumEntries!.Select(e => (e.Value, e.Name)));
                desc = EnsureUniqueDropDownDesc(desc);
                dropDownLinkTarget = desc;
                if (!string.IsNullOrEmpty(enumKey))
                    _dropDownOwners[enumKey] = desc;
            }
        }
        else if (isEnumFallback)
        {
            // Build from current element enum values (deduplicated)
            var seen = new HashSet<long>();
            var pairs = new List<(long, string)>();
            foreach (var e in field.ArrayElements!)
            {
                if (seen.Add(e.RawIntValue) && !string.IsNullOrEmpty(e.EnumName))
                    pairs.Add((e.RawIntValue, e.EnumName));
            }
            if (pairs.Count > 0)
            {
                dropDownContent = BuildDropDownContent(pairs);
                desc = EnsureUniqueDropDownDesc(desc);
                dropDownLinkTarget = desc;
            }
        }
        else if (isNameArray)
        {
            // Build from current element values (deduplicated)
            var seen = new HashSet<long>();
            var pairs = new List<(long, string)>();
            foreach (var e in field.ArrayElements!)
            {
                if (seen.Add(e.RawIntValue) && !string.IsNullOrEmpty(e.Value))
                    pairs.Add((e.RawIntValue, e.Value));
            }
            if (pairs.Count > 0)
            {
                dropDownContent = BuildDropDownContent(pairs);
                desc = EnsureUniqueDropDownDesc(desc);
                dropDownLinkTarget = desc;
            }
        }

        // Array group: Address=+{fieldOffset}, Offsets=[0] to dereference TArray.Data pointer.
        // TArray layout: { Data* +0x00, Count +0x08, Max +0x0C }
        // Offsets=[0] reads the pointer at TArray+0x00 (the Data pointer).
        // DropDownList/DropDownListLink is emitted on this parent group node.
        if (dropDownContent != null)
        {
            EmitGroupOpen(sb, indent, desc, $"+{field.Offset:X}", new[] { 0 },
                dropDownContent: dropDownContent);
        }
        else if (dropDownLinkTarget != null)
        {
            // Shared enum: parent links to first occurrence's parent
            EmitGroupOpen(sb, indent, desc, $"+{field.Offset:X}", new[] { 0 },
                dropDownListLink: dropDownLinkTarget);
        }
        else
        {
            EmitGroupOpen(sb, indent, desc, $"+{field.Offset:X}", new[] { 0 });
        }
        var childIndent = indent + "  ";

        // Fabricate (Copy CE Field, _fabricateArrayCount > 0): the target row count for
        // this leaf-element array. Scalar arrays and all-null object arrays (which reach
        // this generic path — no resolved element to drill) get extra element leaves past
        // the live Num so the CE table has rows for values a later save will hold.
        int walkedLeaf = field.ArrayElements.Count;
        int targetLeaf = (FabricateActive && field.ArrayElemSize > 0)
            ? Math.Min(Math.Max(_fabricateArrayCount, walkedLeaf), MaxFabricateElements)
            : walkedLeaf;

        foreach (var elem in field.ArrayElements)
        {
            // Element: simple offset from the already-dereferenced Data pointer.
            int elemByteOffset = elem.Index * field.ArrayElemSize;

            // Default Description = the bare index "[N]". The instance name (PtrName)
            // and enum value name are dropped — CE's Value column / the DropDownList
            // already show the live value. The +Type opt-in re-adds an object
            // element's class; scalar/enum elements have no class so stay just "[N]".
            string elemDesc = DecorateDesc($"[{elem.Index}]", elemByteOffset,
                !string.IsNullOrEmpty(elem.PtrClassName) ? elem.PtrClassName : null);

            if (dropDownLinkTarget != null)
            {
                // All children link to the parent (or first occurrence's parent) Description
                EmitLeaf(sb, childIndent, elemDesc, ceElem,
                    $"+{elemByteOffset:X}", null,
                    dropDownListLink: dropDownLinkTarget);
            }
            else
            {
                EmitLeaf(sb, childIndent, elemDesc, ceElem,
                    $"+{elemByteOffset:X}", null);
            }
        }

        // Fabricated tail: element leaves for indices [Num .. target) at +i*ElemSize. The
        // group already deref'd TArray.Data, so these auto-follow a realloc; they read
        // past-the-end memory (harmless, CE shows unknowns) until the game grows the array.
        for (int i = walkedLeaf; i < targetLeaf; i++)
        {
            if (_emitEntryCount >= MaxEmitEntries) { _emitTruncated = true; break; }
            int elemByteOffset = i * field.ArrayElemSize;
            var elemDesc = DecorateDesc($"[{i}]", elemByteOffset, null);
            if (dropDownLinkTarget != null)
                EmitLeaf(sb, childIndent, elemDesc, ceElem, $"+{elemByteOffset:X}", null,
                    dropDownListLink: dropDownLinkTarget);
            else
                EmitLeaf(sb, childIndent, elemDesc, ceElem, $"+{elemByteOffset:X}", null);
        }

        EmitGroupClose(sb, indent);
    }

    /// <summary>
    /// Emit a TArray&lt;ObjectProperty&gt; whose element pointer targets were
    /// pre-resolved by the drilldown resolver. The array group derefs TArray.Data
    /// (Offsets=[0]); each resolved element becomes a drilled GroupHeader (its own
    /// Offsets=[0] derefs the 8-byte element pointer, children at their natural
    /// offsets) via EmitDrilledPointer — so nested structs/pointers/containers
    /// expand and the same cycle/depth guards apply. Unresolved or null elements
    /// fall back to a flat 8-byte pointer leaf (the pre-fix behavior).
    /// </summary>
    private static void EmitObjectArrayProperty(StringBuilder sb, string indent,
        LiveFieldValue field, string desc)
    {
        // Array group: Address=+{fieldOffset}, Offsets=[0] derefs TArray.Data.
        EmitGroupOpen(sb, indent, desc, $"+{field.Offset:X}", new[] { 0 });
        var elemIndent = indent + "  ";

        var elems = field.ArrayElements!;
        int walked = elems.Count;

        // Fabricate (Copy CE Field only, _fabricateArrayCount > 0): replicate a resolved
        // element's field layout onto slots the live save hasn't populated — null pointers
        // within Num AND, when the requested count exceeds Num, indices beyond it — so the
        // CE table already has room for items a later save/ship will hold. Homogeneous-class
        // assumption (TArray<ConcreteType*>). Element [i] lives at +i*ElemSize from the
        // already-dereferenced TArray.Data, so extended rows auto-follow a realloc.
        int target = walked;
        List<LiveFieldValue>? template = null;
        string? templateClass = null;
        if (FabricateActive && field.ArrayElemSize > 0)
        {
            target = Math.Min(Math.Max(_fabricateArrayCount, walked), MaxFabricateElements);
            foreach (var e in elems)
            {
                if (!string.IsNullOrEmpty(e.PtrAddress) && e.PtrAddress != "0x0"
                    && _resolvedInstancesState != null
                    && _resolvedInstancesState.TryGetValue(e.PtrAddress, out var t) && t.Count > 0)
                { template = t; templateClass = e.PtrClassName; break; }
            }
        }

        // TArray.Data base — used to key each fabricated slot by its ABSOLUTE element-slot
        // address. The dedup / cycle guards (_emittedInstances) are export-GLOBAL, so a key
        // that folds in only (field.Offset, index) collides across two different same-class
        // arrays sharing a property offset (e.g. Item[0].Mods and Item[1].Mods both at +0x50),
        // wrongly collapsing the second array's fabricated slots to "(shared)". The slot
        // address is unique per array instance, so it can't collide.
        ulong arrDataBase = ParseHexAddr(field.ArrayDataAddr);

        // TArray indices are contiguous, but map by Index defensively.
        var byIndex = new Dictionary<int, ArrayElementValue>();
        foreach (var e in elems) byIndex[e.Index] = e;

        for (int i = 0; i < target; i++)
        {
            // The per-element foreach is not otherwise budget-checked; a large fabricate
            // count must stop cleanly (and honestly flag truncation) rather than overshoot.
            if (_emitEntryCount >= MaxEmitEntries) { _emitTruncated = true; break; }

            int elemByteOffset = i * field.ArrayElemSize;
            // Bare index name; EmitDrilledPointer / DecorateDesc re-add the class only under
            // the +Type opt-in, so the synth Name must be just "[N]" to avoid doubling.
            var baseName = $"[{i}]";
            byIndex.TryGetValue(i, out var elem);

            // Live resolved element → drill its own target's fields.
            if (elem != null && !string.IsNullOrEmpty(elem.PtrAddress) && elem.PtrAddress != "0x0"
                && _resolvedInstancesState != null
                && _resolvedInstancesState.TryGetValue(elem.PtrAddress, out var children)
                && children.Count > 0)
            {
                var synth = new LiveFieldValue
                {
                    Name = baseName,
                    TypeName = field.ArrayInnerType,
                    Offset = elemByteOffset,
                    PtrAddress = elem.PtrAddress,
                    PtrName = elem.PtrName,
                    PtrClassName = elem.PtrClassName,
                };
                EmitDrilledPointer(sb, elemIndent, synth, children,
                    _resolvedStructsState, _resolvedInstancesState!);
                continue;
            }

            // Fabricated slot with a template → drill the template's field layout. A unique
            // synthetic PtrAddress per slot keeps the shared-dedup / cycle guards from
            // collapsing the fabricated siblings into "(shared)"; EmitDrilledPointer uses the
            // key only for those guards and never emits it (the group is +off / Offsets=[0]).
            if (template != null)
            {
                var synth = new LiveFieldValue
                {
                    Name = baseName,
                    TypeName = string.IsNullOrEmpty(field.ArrayInnerType) ? "ObjectProperty" : field.ArrayInnerType,
                    Offset = elemByteOffset,
                    PtrAddress = arrDataBase != 0
                        ? $"fab:{arrDataBase + (ulong)elemByteOffset:X}"   // globally-unique slot addr
                        : $"fab:{field.Offset:X}:{i:X}",                   // fallback (no Data addr)
                    PtrClassName = templateClass ?? elem?.PtrClassName ?? "",
                };
                EmitDrilledPointer(sb, elemIndent, synth, template,
                    _resolvedStructsState, _resolvedInstancesState!);
                continue;
            }

            // No template (fabrication off, or every walked element null) → flat 8-byte
            // pointer leaf. Default "[N]"; +Type re-adds class.
            EmitLeaf(sb, elemIndent,
                DecorateDesc(baseName, elemByteOffset, elem?.PtrClassName),
                new CeFieldInfo("8 Bytes", ShowAsHex: true),
                $"+{elemByteOffset:X}", null);
        }

        EmitGroupClose(sb, indent);
    }

    /// <summary>
    /// Phase G: Emit a TArray&lt;TSoftObjectPtr|TSoftClassPtr&gt; with per-element
    /// struct groups so the FName leaf(s) at the FSoftObjectPath sub-offset
    /// are addressable in CE — instead of a single 8B WeakPtr hex blob.
    ///
    /// Element layout (DLL-provided fname size + FTopLevelAssetPath flag):
    ///   +0x00 FWeakObjectPtr (8B: int32 ObjectIndex + int32 SerialNumber)
    ///   +0x08 Tag (4B) + pad (4B)
    ///   +0x10 FName AssetPathName  (UE4 / UE5.0)         — single FName
    ///         OR FName PackageName (UE5.1+ FTopLevelAssetPath)
    ///   +0x10+fnameSize  FName AssetName (UE5.1+ only)
    ///
    /// FName CE rendering: ComparisonIndex (uint32) at field+0 — emitted as
    /// a "4 Bytes" leaf with a deduplicated DropDownList built from the live
    /// elements so users see the resolved asset path text in CE's Value column.
    ///
    /// Array group: Address=+{fieldOffset}, Offsets=[0] (deref TArray.Data)
    /// Element group: Address=+{N*elemSize}, no Offsets (inline within Data)
    /// Leaves: Address=+{subOffset} (relative to element start)
    /// </summary>
    private static void EmitSoftObjectArrayProperty(StringBuilder sb, string indent,
        LiveFieldValue field, string desc)
    {
        EmitGroupOpen(sb, indent, desc, $"+{field.Offset:X}", new[] { 0 });
        var elemIndent = indent + "  ";

        // Build a shared DropDownList for the AssetPath/PackageName FName from
        // the live element values. Each elem.RawIntValue is the FName
        // ComparisonIndex (set by ReadSoftObjectArrayElements when the path
        // resolves); fall back to no DropDown if values are missing.
        var maxDd = _maxDropDownEntries > 0 ? _maxDropDownEntries : 512;
        string? sharedDropDown = null;
        if (field.ArrayElements is { Count: > 0 } && field.ArrayElements.Count <= maxDd)
        {
            var seen = new HashSet<long>();
            var pairs = new List<(long, string)>();
            foreach (var e in field.ArrayElements)
            {
                if (e.RawIntValue == 0 || string.IsNullOrEmpty(e.Value)) continue;
                if (seen.Add(e.RawIntValue))
                    pairs.Add((e.RawIntValue, e.Value));
            }
            if (pairs.Count > 0)
                sharedDropDown = BuildDropDownContent(pairs);
        }

        var ceWeakPtr  = new CeFieldInfo("8 Bytes", ShowAsHex: true);
        var ceFNameIdx = new CeFieldInfo("4 Bytes");

        foreach (var elem in field.ArrayElements ?? new List<ArrayElementValue>())
        {
            int elemByteOffset = elem.Index * field.ArrayElemSize;
            // The soft-path string is a meaningful asset identity (not an object
            // instance name), so it's kept as the element's name. +Offset annotates;
            // there is no class type here so +Type is a no-op.
            string elemKey = !string.IsNullOrEmpty(elem.Value)
                ? $"[{elem.Index}] {elem.Value}"
                : $"[{elem.Index}]";
            string elemDesc = DecorateDesc(elemKey, elemByteOffset, null);

            EmitGroupOpen(sb, elemIndent, elemDesc, $"+{elemByteOffset:X}", null);
            var fieldIndent = elemIndent + "  ";

            // FWeakObjectPtr at +0 — useful when the asset is currently loaded
            // (8 bytes packing ObjectIndex + SerialNumber). +Offset annotates these
            // fixed FSoftObjectPath sub-leaves the same way Map Key/Value leaves are.
            EmitLeaf(sb, fieldIndent, DecorateDesc("WeakPtr", 0, null), ceWeakPtr, "+0", null);

            // FName ComparisonIndex (and Number at +4) for the
            // AssetPathName / PackageName at +0x10.
            string firstFNameLabel = field.SoftArrayIsTopLevelAssetPath
                ? "PackageName"
                : "AssetPath";
            if (sharedDropDown != null)
            {
                EmitLeaf(sb, fieldIndent, DecorateDesc(firstFNameLabel, 0x10, null), ceFNameIdx,
                    "+10", null, dropDownContent: sharedDropDown);
            }
            else
            {
                EmitLeaf(sb, fieldIndent, DecorateDesc(firstFNameLabel, 0x10, null), ceFNameIdx,
                    "+10", null);
            }

            // UE5.1+: FTopLevelAssetPath has a second FName (AssetName) right
            // after PackageName. Stride is the same fnameSize used by the
            // backing FName.
            if (field.SoftArrayIsTopLevelAssetPath)
            {
                int assetNameOffset = 0x10 + field.SoftArrayFNameSize;
                EmitLeaf(sb, fieldIndent, DecorateDesc("AssetName", assetNameOffset, null), ceFNameIdx,
                    $"+{assetNameOffset:X}", null);
            }

            EmitGroupClose(sb, elemIndent);
        }

        EmitGroupClose(sb, indent);
    }

    /// <summary>
    /// Phase F: Emit struct array with per-element groups containing field children.
    /// Array group: Offsets=[0] (deref TArray.Data)
    /// Element group: Address=+{N*elemSize}, no Offsets (inline within Data)
    /// Field leaf: Address=+{fieldOffset} (relative to element start)
    /// </summary>
    private static void EmitStructArrayProperty(StringBuilder sb, string indent,
        LiveFieldValue field, string desc)
    {
        EmitGroupOpen(sb, indent, desc, $"+{field.Offset:X}", new[] { 0 });
        var elemIndent = indent + "  ";

        ulong arrDataBase = ParseHexAddr(field.ArrayDataAddr);
        bool canResolveElem = arrDataBase != 0
                              && !string.IsNullOrEmpty(field.ArrayStructClassAddr)
                              && _resolvedStructsState != null;

        foreach (var elem in field.ArrayElements!)
        {
            int elemByteOffset = elem.Index * field.ArrayElemSize;
            // Bare index for the synth field (EmitResolvedStruct re-decorates it via
            // EmitFields); a separately-decorated form for the shallow placeholder paths.
            var elemName = $"[{elem.Index}]";
            var elemDesc = DecorateDesc(elemName, elemByteOffset, field.ArrayStructType);

            // Prefer a full re-walk of the element struct (nested structs/maps expand)
            // when the resolver walked it; fall back to the shallow per-element preview.
            string elemStructAddr = canResolveElem
                ? AbsAddr(arrDataBase, (long)elem.Index * field.ArrayElemSize) : "";
            if (canResolveElem
                && _resolvedStructsState!.TryGetValue(elemStructAddr, out var rs) && rs.Count > 0)
            {
                var sv = new LiveFieldValue
                {
                    Name = elemName, TypeName = "StructProperty", Offset = elemByteOffset,
                    StructDataAddr = elemStructAddr, StructClassAddr = field.ArrayStructClassAddr,
                    StructTypeName = field.ArrayStructType,
                };
                // Alternating row colour by element parity, but ONLY when the element actually
                // flattens (a grouped element keeps its [i] boundary, so it needs no tint).
                _curRowColor = WouldFlattenLeafStruct(rs) ? AltRowColor(elem.Index) : null;
                EmitFields(sb, elemIndent, new[] { sv }, _resolvedStructsState, _resolvedInstancesState);
                _curRowColor = null;
                continue;
            }

            if (elem.StructFields is { Count: > 0 })
            {
                // Element group: inline offset from Data pointer
                EmitGroupOpen(sb, elemIndent, elemDesc, $"+{elemByteOffset:X}", null);
                var fieldIndent = elemIndent + "  ";

                foreach (var sf in elem.StructFields)
                {
                    // Enum width follows the sub-field's real byte size (a 1-byte
                    // enum must NOT be read as 4 bytes — that pulls in the next
                    // field's bytes). Other scalars/pointers map by type name.
                    var ceField = sf.TypeName == "EnumProperty"
                        ? new CeFieldInfo(CeWidthForSize(sf.Size))
                        : MapInnerTypeToCeField(sf.TypeName);
                    if (ceField != null)
                    {
                        EmitLeaf(sb, fieldIndent,
                            DecorateDesc(sf.Name, sf.Offset,
                                !string.IsNullOrEmpty(sf.PtrClassName) ? sf.PtrClassName : null),
                            ceField, $"+{sf.Offset:X}", null);
                    }
                    else
                    {
                        // Non-scalar sub-field (nested struct / map / set / array):
                        // the Phase F array read doesn't carry its inner data, so
                        // surface it as a collapsed placeholder folder at its offset
                        // instead of dropping it silently — the user still sees every
                        // field and its address (and can add children in CE).
                        EmitGroupPlaceholder(sb, fieldIndent,
                            DecorateDesc(sf.Name, sf.Offset, null), $"+{sf.Offset:X}", null);
                    }
                }

                EmitGroupClose(sb, elemIndent);
            }
            else
            {
                EmitGroupPlaceholder(sb, elemIndent, elemDesc, $"+{elemByteOffset:X}", null);
            }
        }

        // Fabricate (Copy CE Field, _fabricateArrayCount > 0): extend a struct array past Num
        // by replicating a resolved element's field layout at each new +i*ElemSize. Struct
        // arrays are dense (every walked slot resolves), so this is pure extend — no null slots
        // to fill. Rich re-walked template only; the shallow Phase-F preview isn't fabricated.
        int walkedStruct = field.ArrayElements!.Count;
        if (FabricateActive && field.ArrayElemSize > 0 && canResolveElem)
        {
            int targetStruct = Math.Min(Math.Max(_fabricateArrayCount, walkedStruct), MaxFabricateElements);
            string templateAddr = "";
            foreach (var e in field.ArrayElements!)
            {
                var a = AbsAddr(arrDataBase, (long)e.Index * field.ArrayElemSize);
                if (_resolvedStructsState!.TryGetValue(a, out var rs2) && rs2.Count > 0) { templateAddr = a; break; }
            }
            if (!string.IsNullOrEmpty(templateAddr))
            {
                for (int i = walkedStruct; i < targetStruct; i++)
                {
                    if (_emitEntryCount >= MaxEmitEntries) { _emitTruncated = true; break; }
                    int elemByteOffset = i * field.ArrayElemSize;
                    // StructDataAddr = the template's addr (keys the resolved layout, never
                    // emitted); Offset = i*ElemSize places the group at the fabricated slot.
                    var sv = new LiveFieldValue
                    {
                        Name = $"[{i}]", TypeName = "StructProperty", Offset = elemByteOffset,
                        StructDataAddr = templateAddr, StructClassAddr = field.ArrayStructClassAddr,
                        StructTypeName = field.ArrayStructType,
                    };
                    _curRowColor = null;
                    EmitFields(sb, elemIndent, new[] { sv }, _resolvedStructsState, _resolvedInstancesState);
                }
            }
        }

        EmitGroupClose(sb, indent);
    }

    /// <summary>
    /// Emit a MapProperty as a CE group with per-element children.
    /// TMap uses TSparseArray internally. Data pointer is at +0x00 (same as TArray).
    /// Element stride comes from the DLL via ContainerGeometry.MapStrideOf (never recomputed here).
    /// Each allocated element: key at +0, value at +valOffset (aligned) within the element.
    ///
    /// TSparseArray addressing:
    /// - Group header: Address=+{fieldOffset}, Offsets=[0] → dereferences TSparseArray.Data pointer
    /// - Element group: Address=+{allocatedIndex * stride} → element start from Data pointer
    ///   - Key leaf: Address=+0, type from MapKeyType
    ///   - Value leaf: Address=+{keySize}, type from MapValueType
    /// </summary>
    private static void EmitMapProperty(StringBuilder sb, string indent, LiveFieldValue field)
    {
        var keyLabel = !string.IsNullOrEmpty(field.MapKeyType) ? field.MapKeyType : "?";
        var valLabel = !string.IsNullOrEmpty(field.MapValueType) ? field.MapValueType : "?";
        // Default Description = the bare field name; the old "{Map: N, K \u2192 V}"
        // descriptor is dropped. +Type re-adds the concise "K \u2192 V" type signature.
        var desc = DecorateDesc(field.Name, field.Offset,
            field.MapCount > 0 ? $"{keyLabel} \u2192 {valLabel}" : null);

        // Need elements + sizes for addressable CE entries.
        if (field.MapCount <= 0
            || field.MapElements == null || field.MapElements.Count == 0
            || field.MapKeySize <= 0 || field.MapValueSize <= 0)
        {
            EmitGroupPlaceholder(sb, indent, desc, $"+{field.Offset:X}", null);
            return;
        }

        // Scalar key/value → leaf; struct/object values drill via EmitFields. The
        // value column NEVER bakes the resolved name into the description (the stored
        // int can change at runtime) — Name/Enum values instead get a CE DropDownList
        // (rawInt → resolved name) on the map group that the leaves link to, so CE
        // shows the LIVE name. Enum key/value widths follow the real byte size.
        int valOffset = ContainerGeometry.MapValueOffsetOf(field);
        int stride = ContainerGeometry.MapStrideOf(field);
        ulong dataBase = ParseHexAddr(field.MapDataAddr);
        bool valStruct = field.MapValueType == "StructProperty"
                         && !string.IsNullOrEmpty(field.MapValueStructAddr);
        bool valScalar = !valStruct && !IsObjectPropertyType(field.MapValueType);

        var ceKey = field.MapKeyType == "EnumProperty"
            ? new CeFieldInfo(CeWidthForSize(field.MapKeySize))
            : MapInnerTypeToCeField(field.MapKeyType);

        // Shared value DropDownList (rawInt → name) for Name/Enum values.
        string? valueDropDown = null;
        string? valueDropLink = null;
        if (valScalar && (field.MapValueType == "NameProperty" || field.MapValueType == "EnumProperty"))
        {
            int ddBytes = field.MapValueType == "NameProperty" ? 4 : field.MapValueSize;
            var maxDd = _maxDropDownEntries > 0 ? _maxDropDownEntries : 512;
            var seen = new HashSet<long>();
            var pairs = new List<(long, string)>();
            foreach (var e in field.MapElements)
            {
                if (string.IsNullOrEmpty(e.Value)) continue;
                long raw = ParseHexLeInt(e.ValueHex, ddBytes);
                if (seen.Add(raw)) pairs.Add((raw, e.Value));
            }
            if (pairs.Count > 0 && pairs.Count <= maxDd)
            {
                valueDropDown = BuildDropDownContent(pairs);
                desc = EnsureUniqueDropDownDesc(desc);
                valueDropLink = desc;
            }
        }

        // Map group: Address=+{fieldOffset}, Offsets=[0] (deref TSparseArray.Data)
        EmitGroupOpen(sb, indent, desc, $"+{field.Offset:X}", new[] { 0 }, dropDownContent: valueDropDown);
        var elemIndent = indent + "  ";

        foreach (var elem in field.MapElements)
        {
            int elemByteOffset = elem.Index * stride;

            // Record / primitive-leaf flatten: when the map VALUE is a struct whose entire subtree
            // is leaf fields, collapse the per-element [i] group too — emit the Key and the value's
            // fields as flat "[i] key ▸ Field" siblings at the COMBINED offset (element base + value
            // offset + field offset), instead of an [i] folder + a Value sub-group. Mirrors how a
            // struct ARRAY already flattens its elements (EmitStructArrayProperty). The map group
            // already derefs Data (Offsets=[0]), so the combined offset is the live field address.
            // Gate matches the StructProperty branch in EmitFields (leaf / record), minus GAS.
            string valStructAddr = valStruct ? AbsAddr(dataBase, elemByteOffset + valOffset) : "";
            if (valStruct && _resolvedStructsState != null
                && _resolvedStructsState.TryGetValue(valStructAddr, out var valFlatChildren)
                && WouldFlattenLeafStruct(valFlatChildren))
            {
                var rawElemLabel = !string.IsNullOrEmpty(elem.Key)
                    ? $"[{elem.Index}] {elem.Key}" : $"[{elem.Index}]";
                // Alternating row colour by element parity (no-op when the feature is off); tints
                // BOTH the Key leaf and the flattened value fields so the whole record reads as one.
                _curRowColor = AltRowColor(elem.Index);
                // Key as a flat sibling leaf at the element base ("[i] key ▸ Key").
                if (ceKey != null)
                    EmitLeaf(sb, elemIndent,
                        $"{DecorateDesc(rawElemLabel, elemByteOffset, null, allowType: false)} ▸ Key",
                        ceKey, $"+{elemByteOffset:X}", null);
                // Value record fields as flat "[i] key ▸ Field" siblings at the combined offset.
                EmitFlattenedStruct(sb, elemIndent, new LiveFieldValue
                {
                    Name = rawElemLabel,
                    TypeName = "StructProperty",
                    Offset = elemByteOffset + valOffset,
                    StructDataAddr = valStructAddr,
                    StructTypeName = field.MapValueStructType,
                }, valFlatChildren);
                _curRowColor = null;
                continue;
            }

            // An object key's instance name is dropped (+Type re-adds its class); a
            // scalar/string key is the slot's identity, so it's kept as the name.
            var elemDesc = !string.IsNullOrEmpty(elem.KeyPtrName)
                ? DecorateDesc($"[{elem.Index}]", elemByteOffset, elem.KeyPtrClassName)
                : !string.IsNullOrEmpty(elem.Key)
                    ? DecorateDesc($"[{elem.Index}] {elem.Key}", elemByteOffset, null)
                    : DecorateDesc($"[{elem.Index}]", elemByteOffset, null);

            // Element group: inline from Data pointer
            EmitGroupOpen(sb, elemIndent, elemDesc, $"+{elemByteOffset:X}", null);
            var fieldIndent = elemIndent + "  ";

            // Key leaf at +0 — label only, no baked-in dynamic value.
            if (ceKey != null)
                EmitLeaf(sb, fieldIndent, DecorateDesc("Key", 0, null), ceKey, "+0", null);

            // Value at +valOffset.
            if (valStruct || IsObjectPropertyType(field.MapValueType))
            {
                var valueField = BuildElementValue("Value", field.MapValueType, valOffset, field.MapValueSize,
                    valStruct, AbsAddr(dataBase, elemByteOffset + valOffset),
                    field.MapValueStructAddr, field.MapValueStructType,
                    elem.ValuePtrAddress, elem.ValuePtrName, elem.ValuePtrClassName);
                EmitFields(sb, fieldIndent, new[] { valueField }, _resolvedStructsState, _resolvedInstancesState);
            }
            else
            {
                var ceVal = field.MapValueType == "EnumProperty"
                    ? new CeFieldInfo(CeWidthForSize(field.MapValueSize))
                    : MapInnerTypeToCeField(field.MapValueType);
                if (ceVal != null)
                    EmitLeaf(sb, fieldIndent, DecorateDesc("Value", valOffset, null), ceVal,
                        $"+{valOffset:X}", null,
                        dropDownListLink: valueDropLink);
            }

            EmitGroupClose(sb, elemIndent);
        }

        EmitGroupClose(sb, indent);
    }

    /// <summary>
    /// Build the synthetic VALUE field of a container element for the emit phase:
    /// a struct (StructDataAddr set → drills when resolved), an object pointer
    /// (PtrAddress set → drills when resolved), or a scalar leaf. Offset is relative
    /// to the element group; StructDataAddr is absolute (matches the resolver key).
    /// </summary>
    private static LiveFieldValue BuildElementValue(
        string name, string typeName, int offset, int size,
        bool isStruct, string structDataAddr, string structClassAddr, string structTypeName,
        string? ptrAddr, string? ptrName, string? ptrClassName)
    {
        if (isStruct && !string.IsNullOrEmpty(structDataAddr))
            return new LiveFieldValue
            {
                Name = name, TypeName = "StructProperty", Offset = offset, Size = size,
                StructDataAddr = structDataAddr, StructClassAddr = structClassAddr,
                StructTypeName = structTypeName,
            };
        if (IsObjectPropertyType(typeName) && !string.IsNullOrEmpty(ptrAddr) && ptrAddr != "0x0")
            return new LiveFieldValue
            {
                Name = name, TypeName = typeName, Offset = offset, Size = size,
                PtrAddress = ptrAddr!, PtrName = ptrName ?? "", PtrClassName = ptrClassName ?? "",
            };
        return new LiveFieldValue { Name = name, TypeName = typeName, Offset = offset, Size = size };
    }

    /// <summary>
    /// Emit a SetProperty as a CE group with per-element children.
    /// TSet uses TSparseArray. Data pointer at +0x00.
    /// Element stride comes from the DLL via ContainerGeometry.SetStrideOf (never recomputed here).
    ///
    /// TSparseArray addressing:
    /// - Group header: Address=+{fieldOffset}, Offsets=[0] → dereferences TSparseArray.Data pointer
    /// - Element leaf: Address=+{allocatedIndex * stride}, type from SetElemType
    /// </summary>
    private static void EmitSetProperty(StringBuilder sb, string indent, LiveFieldValue field)
    {
        var elemLabel = !string.IsNullOrEmpty(field.SetElemType) ? field.SetElemType : "?";
        // Default Description = the bare field name; the old "{Set: N, T}" descriptor
        // is dropped. +Type re-adds the element type.
        var desc = DecorateDesc(field.Name, field.Offset,
            field.SetCount > 0 ? elemLabel : null);

        // Empty / no elements → placeholder. Struct/object elements (ceElem == null)
        // now expand via EmitFields instead of collapsing the whole set.
        if (field.SetCount <= 0
            || field.SetElements == null || field.SetElements.Count == 0
            || field.SetElemSize <= 0)
        {
            EmitGroupPlaceholder(sb, indent, desc, $"+{field.Offset:X}", null);
            return;
        }

        var ceElem = MapInnerTypeToCeField(field.SetElemType);   // null for struct/object
        int stride = ContainerGeometry.SetStrideOf(field);
        ulong dataBase = ParseHexAddr(field.SetDataAddr);
        bool elemStruct = field.SetElemType == "StructProperty"
                          && !string.IsNullOrEmpty(field.SetElemStructAddr);

        // Set group: Address=+{fieldOffset}, Offsets=[0] (deref TSparseArray.Data)
        EmitGroupOpen(sb, indent, desc, $"+{field.Offset:X}", new[] { 0 });
        var childIndent = indent + "  ";

        foreach (var elem in field.SetElements)
        {
            int elemByteOffset = elem.Index * stride;
            // An object element's instance name is dropped (its class returns via +Type
            // downstream); a scalar/string element value is its identity, kept as the name.
            string bareName = !string.IsNullOrEmpty(elem.KeyPtrName)
                ? $"[{elem.Index}]"
                : !string.IsNullOrEmpty(elem.Key)
                    ? $"[{elem.Index}] {elem.Key}"
                    : $"[{elem.Index}]";

            if (ceElem != null)
            {
                // Scalar element → flat leaf (decorated with +Offset; no class type).
                EmitLeaf(sb, childIndent, DecorateDesc(bareName, elemByteOffset, null),
                    ceElem, $"+{elemByteOffset:X}", null);
            }
            else
            {
                // Struct / object element → expand via the shared EmitFields dispatch,
                // which re-decorates bareName with the struct/class type + offset.
                var ev = BuildElementValue(bareName, field.SetElemType, elemByteOffset, field.SetElemSize,
                    elemStruct, AbsAddr(dataBase, elemByteOffset),
                    field.SetElemStructAddr, field.SetElemStructType,
                    elem.KeyPtrAddress, elem.KeyPtrName, elem.KeyPtrClassName);
                EmitFields(sb, childIndent, new[] { ev }, _resolvedStructsState, _resolvedInstancesState);
            }
        }

        EmitGroupClose(sb, indent);
    }

    /// <summary>
    /// Emit DataTable RowMap as a CE group with 2-level pointer dereference.
    ///
    /// DataTable RowMap addressing (2-level deref):
    /// - Level 1: Address=+{RowMapOffset}, Offsets=[0] → dereferences TSparseArray.Data pointer
    /// - Level 2: Address=+{sparseIndex*stride+fnameSize}, Offsets=[0] → dereferences uint8* row data pointer
    /// - Level 3: Address=+{fieldOffset} → inline field within the row data buffer
    ///
    /// Unlike TMap where values are inline (no second deref), DataTable RowMap stores uint8*
    /// pointers that must be dereferenced to reach the actual row data.
    /// </summary>
    private static void EmitDataTableRowsProperty(StringBuilder sb, string indent,
        LiveFieldValue field)
    {
        var structName = !string.IsNullOrEmpty(field.DataTableStructName)
            ? field.DataTableStructName : "Row";
        // Default Description = the bare field name; the old "[DataTable: N x Struct]"
        // descriptor is dropped. +Type re-adds the row struct name.
        var desc = DecorateDesc(field.Name, field.Offset,
            field.DataTableRowCount > 0 ? structName : null);

        // Need row data for addressable CE entries
        if (field.DataTableRowData == null || field.DataTableRowData.Count == 0
            || field.DataTableStride <= 0 || field.DataTableFNameSize <= 0)
        {
            EmitGroupPlaceholder(sb, indent, desc, $"+{field.Offset:X}", null);
            return;
        }

        // Level 1: RowMap group — deref TSparseArray.Data
        EmitGroupOpen(sb, indent, desc, $"+{field.Offset:X}", new[] { 0 });
        var rowIndent = indent + "  ";

        foreach (var row in field.DataTableRowData)
        {
            // Level 2: Row — deref uint8* at sparseIndex*stride+fnameSize. The row's
            // FName key is its identity, kept as the name; +Offset annotates.
            int rowPtrOffset = row.SparseIndex * field.DataTableStride + field.DataTableFNameSize;
            var rowBare = !string.IsNullOrEmpty(row.RowName)
                ? $"[{row.SparseIndex}] {row.RowName}" : $"[{row.SparseIndex}]";
            var rowDesc = DecorateDesc(rowBare, rowPtrOffset, null);

            if (row.Fields.Count == 0)
            {
                EmitGroupPlaceholder(sb, rowIndent, rowDesc, $"+{rowPtrOffset:X}", new[] { 0 });
                continue;
            }

            EmitGroupOpen(sb, rowIndent, rowDesc, $"+{rowPtrOffset:X}", new[] { 0 });
            var fieldIndent = rowIndent + "  ";

            // Level 3: Fields — inline offset within dereferenced row data
            foreach (var rowField in row.Fields)
            {
                // FString-family within row: CE String leaf with pointer deref
                if (IsStringProperty(rowField.TypeName))
                {
                    EmitStringLeaf(sb, fieldIndent,
                        DecorateDesc(rowField.Name, rowField.Offset, LeafTypeLabel(rowField)),
                        $"+{rowField.Offset:X}", offsets: [0],
                        unicode: rowField.TypeName == "StrProperty",
                        codepage: rowField.TypeName == "Utf8StrProperty");
                    continue;
                }

                var ceField = MapCeField(rowField);
                if (ceField != null)
                {
                    var baseDesc = DecorateDesc(rowField.Name, rowField.Offset, LeafTypeLabel(rowField));
                    var ddLink = TryGetEnumDropDown(rowField, baseDesc);
                    EmitLeaf(sb, fieldIndent, ddLink.desc ?? baseDesc, ceField,
                        $"+{rowField.Offset:X}", null,
                        dropDownContent: ddLink.content,
                        dropDownListLink: ddLink.link);
                }
                else if (rowField.IsNavigable)
                {
                    EmitNavigableField(sb, fieldIndent, rowField,
                        $"+{rowField.Offset:X}", null);
                }
                else if (rowField.TypeName == "ArrayProperty" && rowField.ArrayCount >= 0)
                {
                    EmitArrayProperty(sb, fieldIndent, rowField);
                }
                else if (rowField.TypeName == "MapProperty" && rowField.MapCount >= 0)
                {
                    EmitMapProperty(sb, fieldIndent, rowField);
                }
                else if (rowField.TypeName == "SetProperty" && rowField.SetCount >= 0)
                {
                    EmitSetProperty(sb, fieldIndent, rowField);
                }
            }

            EmitGroupClose(sb, rowIndent);
        }

        EmitGroupClose(sb, indent);
    }

    /// <summary>Emit a group header that will contain child entries (opens CheatEntries block).</summary>
    private static void EmitGroupOpen(StringBuilder sb, string indent, string description,
        string address, int[]? offsets, bool showAsHex = false, string? varType = null,
        string? dropDownContent = null, string? dropDownListLink = null)
    {
        _emitEntryCount++;
        sb.AppendLine($"{indent}<CheatEntry>");
        sb.AppendLine($"{indent}  <ID>{_nextId++}</ID>");
        sb.AppendLine($"{indent}  <Description>\"{EscapeXmlContent(description)}\"</Description>");
        // CE DropDownList: inline list on this group, or link to another group's list
        if (dropDownContent != null)
            sb.AppendLine($"{indent}  <DropDownList DisplayValueAsItem=\"1\">{dropDownContent}</DropDownList>");
        else if (dropDownListLink != null)
            sb.AppendLine($"{indent}  <DropDownListLink>{EscapeXmlContent(dropDownListLink)}</DropDownListLink>");
        if (showAsHex)
            sb.AppendLine($"{indent}  <ShowAsHex>1</ShowAsHex>");
        sb.AppendLine($"{indent}  <ShowAsSigned>0</ShowAsSigned>");
        sb.AppendLine($"{indent}  <GroupHeader>1</GroupHeader>");
        // Collapse every non-root group folder (pointer/array deref nodes, struct
        // groups, AND element folders like [1]). Root is excluded — its address is
        // absolute, not "+...".
        if (_collapsePointerNodes && address.StartsWith("+"))
            sb.AppendLine($"{indent}  <Options moHideChildren=\"1\" moDeactivateChildrenAsWell=\"1\"/>");
        if (varType != null)
            sb.AppendLine($"{indent}  <VariableType>{varType}</VariableType>");
        sb.AppendLine($"{indent}  <Address>{address}</Address>");
        EmitOffsets(sb, indent, offsets);
        sb.AppendLine($"{indent}  <CheatEntries>");
    }

    /// <summary>Close a group header's CheatEntries block.</summary>
    private static void EmitGroupClose(StringBuilder sb, string indent)
    {
        sb.AppendLine($"{indent}  </CheatEntries>");
        sb.AppendLine($"{indent}</CheatEntry>");
    }

    /// <summary>
    /// Emit a group placeholder -- a GroupHeader with no children.
    /// Used for navigable struct/pointer fields at leaf level when resolution is unavailable.
    /// Pointer fields get ShowAsHex=1.
    /// </summary>
    private static void EmitGroupPlaceholder(StringBuilder sb, string indent, string description,
        string address, int[]? offsets, bool showAsHex = false)
    {
        _emitEntryCount++;
        sb.AppendLine($"{indent}<CheatEntry>");
        sb.AppendLine($"{indent}  <ID>{_nextId++}</ID>");
        sb.AppendLine($"{indent}  <Description>\"{EscapeXmlContent(description)}\"</Description>");
        if (showAsHex)
            sb.AppendLine($"{indent}  <ShowAsHex>1</ShowAsHex>");
        sb.AppendLine($"{indent}  <ShowAsSigned>0</ShowAsSigned>");
        sb.AppendLine($"{indent}  <GroupHeader>1</GroupHeader>");
        // Collapse every non-root group folder (see EmitGroupOpen). Root is
        // excluded — its address is absolute, not "+...".
        if (_collapsePointerNodes && address.StartsWith("+"))
            sb.AppendLine($"{indent}  <Options moHideChildren=\"1\" moDeactivateChildrenAsWell=\"1\"/>");
        sb.AppendLine($"{indent}  <Address>{address}</Address>");
        EmitOffsets(sb, indent, offsets);
        sb.AppendLine($"{indent}</CheatEntry>");
    }

    /// <summary>
    /// Emit a scalar leaf entry with proper CE type, signedness, and bit field support.
    /// </summary>
    private static void EmitLeaf(StringBuilder sb, string indent, string description,
        CeFieldInfo ceField, string address, int[]? offsets,
        string? dropDownContent = null, string? dropDownListLink = null)
    {
        _emitEntryCount++;
        sb.AppendLine($"{indent}<CheatEntry>");
        sb.AppendLine($"{indent}  <ID>{_nextId++}</ID>");
        sb.AppendLine($"{indent}  <Description>\"{EscapeXmlContent(description)}\"</Description>");
        // CE DropDownList: inline list content (first occurrence of this enum)
        if (dropDownContent != null)
            sb.AppendLine($"{indent}  <DropDownList DisplayValueAsItem=\"1\">{dropDownContent}</DropDownList>");
        // CE DropDownListLink: reference to another entry's DropDownList
        else if (dropDownListLink != null)
            sb.AppendLine($"{indent}  <DropDownListLink>{EscapeXmlContent(dropDownListLink)}</DropDownListLink>");
        if (ceField.ShowAsHex)
            sb.AppendLine($"{indent}  <ShowAsHex>1</ShowAsHex>");
        sb.AppendLine($"{indent}  <ShowAsSigned>{(ceField.IsSigned ? 1 : 0)}</ShowAsSigned>");
        EmitRowColor(sb, indent);
        sb.AppendLine($"{indent}  <VariableType>{ceField.VariableType}</VariableType>");
        if (ceField.BitStart >= 0)
        {
            sb.AppendLine($"{indent}  <BitStart>{ceField.BitStart}</BitStart>");
            sb.AppendLine($"{indent}  <BitLength>{ceField.BitLength}</BitLength>");
            sb.AppendLine($"{indent}  <ShowAsBinary>0</ShowAsBinary>");
        }
        sb.AppendLine($"{indent}  <Address>{address}</Address>");
        EmitOffsets(sb, indent, offsets);
        sb.AppendLine($"{indent}</CheatEntry>");
    }

    /// <summary>
    /// True for the three FString-family property types (FString / FUtf8String /
    /// FAnsiString), all sharing the TArray string header and emitting as a CE String leaf.
    /// </summary>
    private static bool IsStringProperty(string? typeName) =>
        typeName is "StrProperty" or "Utf8StrProperty" or "AnsiStrProperty";

    /// <summary>
    /// Emit a CE String leaf with proper Length/Unicode/CodePage/ZeroTerminate.
    /// CE's String type encodes three modes via the Unicode + CodePage flags:
    ///   StrProperty     = FString (wchar_t*, UTF-16)  -> Unicode=1, CodePage=0
    ///   AnsiStrProperty = FAnsiString (char*, ANSI)   -> Unicode=0, CodePage=0
    ///   Utf8StrProperty = FUtf8String (char*, UTF-8)  -> Unicode=0, CodePage=1
    /// All three share the FString TArray header { Data ptr, int32 Count, int32 Max },
    /// so Offsets=[0] dereferences the Data pointer to reach the character buffer.
    /// </summary>
    private static void EmitStringLeaf(StringBuilder sb, string indent, string description,
        string address, int[]? offsets, bool unicode, bool codepage = false)
    {
        _emitEntryCount++;
        // CE String display window: the per-export "String Length" option (default 256,
        // floored at 16 by the toolbar slider); 0 (unset) falls back to 256. With
        // ZeroTerminate=1 a generous length never truncates a shorter live string — it
        // only reserves room for strings that later grow (differ per save/progress).
        int length = _ceStringLength > 0 ? _ceStringLength : 256;
        sb.AppendLine($"{indent}<CheatEntry>");
        sb.AppendLine($"{indent}  <ID>{_nextId++}</ID>");
        sb.AppendLine($"{indent}  <Description>\"{EscapeXmlContent(description)}\"</Description>");
        sb.AppendLine($"{indent}  <ShowAsSigned>0</ShowAsSigned>");
        EmitRowColor(sb, indent);
        sb.AppendLine($"{indent}  <VariableType>String</VariableType>");
        sb.AppendLine($"{indent}  <Length>{length}</Length>");
        sb.AppendLine($"{indent}  <Unicode>{(unicode ? 1 : 0)}</Unicode>");
        sb.AppendLine($"{indent}  <CodePage>{(codepage ? 1 : 0)}</CodePage>");
        sb.AppendLine($"{indent}  <ZeroTerminate>1</ZeroTerminate>");
        sb.AppendLine($"{indent}  <Address>{address}</Address>");
        EmitOffsets(sb, indent, offsets);
        sb.AppendLine($"{indent}</CheatEntry>");
    }

    /// <summary>
    /// Emit a CE &lt;Color&gt; element for the entry currently being written, when an alternating
    /// row color is active (<see cref="_curRowColor"/>). CE colours the record's TEXT; the value
    /// is a COLORREF hex string (BBGGRR). Set only around flattened container-element emission
    /// (<see cref="EmitMapProperty"/> / <see cref="EmitStructArrayProperty"/>), so ordinary
    /// (non-flattened) leaves never carry a colour.
    /// </summary>
    private static void EmitRowColor(StringBuilder sb, string indent)
    {
        if (!string.IsNullOrEmpty(_curRowColor))
            sb.AppendLine($"{indent}  <Color>{_curRowColor}</Color>");
    }

    /// <summary>
    /// The alternating row colour for a container element at <paramref name="index"/> — even
    /// indices (struct[0],[2],…) get <see cref="_altColorEven"/>, odd indices get
    /// <see cref="_altColorOdd"/>. Null (no &lt;Color&gt;, CE uses its theme) when the feature is
    /// off or that parity is unset. The stored values are already CE COLORREF (BBGGRR) strings.
    /// </summary>
    private static string? AltRowColor(int index) =>
        !_altColorEnabled ? null : ((index & 1) == 0 ? _altColorEven : _altColorOdd);

    /// <summary>
    /// Convert a UI RGB hex string ("RRGGBB", optional '#') to a CE COLORREF hex ("BBGGRR"), or
    /// null when empty/malformed (→ no &lt;Color&gt; emitted). CE stores a Win32 COLORREF
    /// (0x00BBGGRR), so the byte order is reversed from RGB: "0080FF" (azure) → "FF8000".
    /// </summary>
    private static string? RgbToCeColor(string? rgb)
    {
        if (string.IsNullOrWhiteSpace(rgb)) return null;
        var s = rgb.Trim().TrimStart('#');
        if (s.Length != 6) return null;
        foreach (var c in s)
            if (!Uri.IsHexDigit(c)) return null;
        // RRGGBB -> BBGGRR
        return (s.Substring(4, 2) + s.Substring(2, 2) + s.Substring(0, 2)).ToUpperInvariant();
    }

    /// <summary>
    /// True when a struct whose resolved <paramref name="children"/> are supplied WOULD be
    /// flattened by the current leaf / record gates (mirrors the StructProperty branch in
    /// <see cref="EmitFields"/>, minus GAS). Used by the container emitters to decide whether to
    /// collapse a per-element wrapper and whether to apply an alternating row colour.
    /// </summary>
    private static bool WouldFlattenLeafStruct(List<LiveFieldValue> children) =>
        children.Count > 0
        && ((_flattenLeafStructs && children.All(IsPrimitiveLeafField))
            || (_flattenLeafRecords && children.All(IsTerminalLeafField)));

    /// <summary>Emit Offsets block if offsets are provided.</summary>
    private static void EmitOffsets(StringBuilder sb, string indent, int[]? offsets)
    {
        if (offsets != null && offsets.Length > 0)
        {
            sb.AppendLine($"{indent}  <Offsets>");
            foreach (var o in offsets)
                sb.AppendLine($"{indent}    <Offset>{o:X}</Offset>");
            sb.AppendLine($"{indent}  </Offsets>");
        }
    }

    /// <summary>
    /// Build DropDownList content string from value:name pairs.
    /// Format: newline-separated "value:name" entries (decimal values, no leading zeros).
    /// </summary>
    private static string BuildDropDownContent(IEnumerable<(long value, string name)> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine();  // newline after opening tag
        foreach (var (v, n) in entries)
            sb.AppendLine($"{v}:{n}");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Escape special characters for XML element text content.
    ///
    /// <para><b>Every</b> <c>&lt;Description&gt;</c> goes through this, not just the ones
    /// that looked risky. Description text is arbitrary GAME memory — TMap keys, TSet
    /// elements, soft-object paths, DataTable row names — and a single <c>&amp;</c> in any
    /// of them produces an invalid entity reference that makes Cheat Engine reject the
    /// <b>whole document</b>. A multi-thousand-entry export then imports as nothing, with
    /// no indication which record was at fault (audit #4 B3). Escaping a string that was
    /// already safe is a no-op, so there is no reason to be selective.</para>
    /// </summary>
    private static string EscapeXmlContent(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>
    /// Ensure a DropDownList parent Description is unique.
    /// CE uses Description text as DropDownListLink key, so duplicates cause ambiguity.
    /// Appends ".001", ".002" etc. suffix if the description was already used.
    /// </summary>
    private static string EnsureUniqueDropDownDesc(string desc)
    {
        _dropDownDescriptions ??= new HashSet<string>(StringComparer.Ordinal);
        if (_dropDownDescriptions.Add(desc))
            return desc;  // first use — unique

        // Collision: append suffix .001, .002, ...
        for (int i = 1; i < 1000; i++)
        {
            var suffixed = $"{desc}.{i:D3}";
            if (_dropDownDescriptions.Add(suffixed))
                return suffixed;
        }
        return desc;  // fallback (should never happen)
    }

    /// <summary>
    /// Emit a navigable field as a group placeholder (no resolved children available).
    /// Pointer fields get ShowAsHex=1.
    /// </summary>
    private static void EmitNavigableField(StringBuilder sb, string indent,
        LiveFieldValue field, string address, int[]? offsets)
    {
        // Unresolved struct / pointer leaf placeholder. +Type re-adds the pointer's
        // class or the inline struct's type; +Offset annotates the field offset.
        var typeLabel = field.IsPointerNavigation ? field.PtrClassName : field.StructTypeName;
        EmitGroupPlaceholder(sb, indent, DecorateDesc(field.Name, field.Offset, typeLabel),
            address, offsets, showAsHex: field.IsPointerNavigation);
    }

    /// <summary>
    /// Map UE property type + field metadata to CE field info.
    /// Returns null for unsupported/unknown types (struct, array, delegate, etc.).
    ///
    /// Signedness rules:
    /// - Signed: IntProperty (int32), Int8Property, Int16Property, Int64Property
    /// - Unsigned: UInt32Property, UInt16Property, UInt64Property, ByteProperty
    ///
    /// BoolProperty rules:
    /// - If BoolBitIndex >= 0: Binary type with BitStart/BitLength (CE bit field)
    /// - Otherwise: Byte type (fallback for bool without bit info)
    /// </summary>
    /// <summary>
    /// CE integer-width keyword for a property's byte size. UE enums/bytes can be
    /// 1/2/4/8 bytes wide; emitting the wrong width makes CE read neighbouring
    /// fields — e.g. a 1-byte enum read as "4 Bytes" pulls in the next 3 bytes
    /// (the cause of the SaveSlotList enums reporting 5376 instead of 0).
    /// </summary>
    private static string CeWidthForSize(int size) => size switch
    {
        1 => "Byte",
        2 => "2 Bytes",
        4 => "4 Bytes",
        8 => "8 Bytes",
        _ => "4 Bytes",   // unknown / unreported size → legacy default
    };

    private static CeFieldInfo? MapCeField(LiveFieldValue field)
    {
        return field.TypeName switch
        {
            "FloatProperty" => new CeFieldInfo("Float"),
            "DoubleProperty" => new CeFieldInfo("Double"),

            // Signed integers
            "Int8Property" => new CeFieldInfo("Byte", IsSigned: true),
            "Int16Property" => new CeFieldInfo("2 Bytes", IsSigned: true),
            "IntProperty" => new CeFieldInfo("4 Bytes", IsSigned: true),
            "Int64Property" => new CeFieldInfo("8 Bytes", IsSigned: true),

            // Unsigned integers
            "ByteProperty" => new CeFieldInfo("Byte"),
            "UInt16Property" => new CeFieldInfo("2 Bytes"),
            "UInt32Property" => new CeFieldInfo("4 Bytes"),
            "UInt64Property" => new CeFieldInfo("8 Bytes"),

            // Bool with bit field support
            "BoolProperty" when field.BoolBitIndex >= 0 =>
                new CeFieldInfo("Binary", BitStart: field.BoolBitIndex, BitLength: 1),
            "BoolProperty" => new CeFieldInfo("Byte"),

            // FName index
            "NameProperty" => new CeFieldInfo("4 Bytes"),

            // Enum -- width follows the underlying integer size (uint8 default,
            // but can be 1/2/4/8). Reading a 1-byte enum as 4 bytes corrupts it.
            "EnumProperty" => new CeFieldInfo(CeWidthForSize(field.Size)),

            // StrProperty is handled by EmitStringLeaf (not MapCeField)
            // TextProperty: FText internal pointer chain — CE can't resolve, show as hex
            "TextProperty" => new CeFieldInfo("8 Bytes", ShowAsHex: true),

            // Pointer-shaped property types — single field is a raw 8B pointer.
            // Without these, MapCeField returns null and EmitFields falls through
            // to EmitNavigableField -> EmitGroupPlaceholder, which emits a
            // <GroupHeader>1</GroupHeader> entry with NO <VariableType> — CE
            // shows it as an empty folder rather than a readable pointer.
            // Listing them here promotes them to a proper "8 Bytes / ShowAsHex"
            // leaf so Copy CE Field / Copy CE XML for an ObjectProperty selection
            // produces a usable pointer entry CE can actually display.
            "ObjectProperty" => new CeFieldInfo("8 Bytes", ShowAsHex: true),
            "ClassProperty" => new CeFieldInfo("8 Bytes", ShowAsHex: true),
            "WeakObjectProperty" => new CeFieldInfo("8 Bytes", ShowAsHex: true),

            // Soft/Lazy object: FName-based — CE can't resolve, show as hex
            "SoftObjectProperty" => new CeFieldInfo("8 Bytes", ShowAsHex: true),
            "SoftClassProperty" => new CeFieldInfo("8 Bytes", ShowAsHex: true),
            "LazyObjectProperty" => new CeFieldInfo("8 Bytes", ShowAsHex: true),

            // Interface: first 8 bytes is UObject*, show as pointer
            "InterfaceProperty" => new CeFieldInfo("8 Bytes", ShowAsHex: true),

            // "Guess What" heuristic labels (Ubel::GuessGapTypes) — produced for the
            // Live Walker "Guess?" toggle AND the auto-fill_gaps fallback for structs/
            // objects with no reflected UPROPERTY fields. These are confidence-suffixed
            // display labels ("Float?", "Int32?", ...), NOT canonical UE property-type
            // strings, so without explicit cases MapCeField returns null and EmitFields
            // silently DROPS the row (the null path falls to `field.IsNavigable`, which
            // is always false for guessed fields). Mapping them mirrors the DLL's
            // Ubel::NormalizeGuessedTypeToProperty so Copy CE XML / Copy CE Field export
            // guessed scalar rows. ("Padding" — an all-zero run — is intentionally left
            // to the null default: it is not a meaningful value to watch in CE.)
            "Float" or "Float?" => new CeFieldInfo("Float"),
            "Double" or "Double?" => new CeFieldInfo("Double"),
            "Int32" or "Int32?" => new CeFieldInfo("4 Bytes", IsSigned: true),
            "Int16" or "Int16?" => new CeFieldInfo("2 Bytes", IsSigned: true),
            "Int64" or "Int64?" => new CeFieldInfo("8 Bytes", IsSigned: true),
            "Byte" or "Byte?" => new CeFieldInfo("Byte"),
            "Pointer?" => new CeFieldInfo("8 Bytes", ShowAsHex: true),

            _ => null // Unknown -- not a scalar (StructProperty, ArrayProperty, etc.)
        };
    }

    /// <summary>
    /// CE memory-record type descriptor for the AOBMaker <c>CreateMemoryRecord</c> pipe
    /// command: a numeric CE <c>TVariableType</c> plus the signed / hex display flags.
    /// </summary>
    public readonly record struct CeRecordType(int ValueType, bool IsSigned, bool ShowAsHex);

    // CE TVariableType numeric codes for AOBMaker CreateMemoryRecord.
    // Source: AOBMaker docs/API-CEPlugin.md (the CE plugin SDK header is WRONG — use these).
    private const int CeVtByte = 0, CeVtWord = 1, CeVtDword = 2, CeVtQword = 3,
                      CeVtSingle = 4, CeVtDouble = 5, CeVtString = 6,
                      CeVtUnicodeString = 7, CeVtByteArray = 8, CeVtBinary = 9;

    /// <summary>
    /// Map a Live Walker field to a CE memory-record type for a one-click "Add to CE"
    /// push (AOBMaker <c>CreateMemoryRecord</c>). Reuses the same UE→CE mapping that drives
    /// Copy CE XML / Copy CE Field so the single-record push stays consistent with the
    /// clipboard exports. Non-scalar fields (struct/array/etc.) fall back to 8 Bytes /
    /// ShowAsHex; bit-field bools — which the single-record command can't fully express —
    /// fall back to the containing Byte.
    /// </summary>
    public static CeRecordType MapFieldToCeRecordType(LiveFieldValue field)
    {
        var info = MapCeField(field);
        if (info == null)
            return PointerRecordType; // non-scalar (struct/array/etc.) -> 8 Bytes hex
        return new CeRecordType(KeywordToValueType(info.VariableType), info.IsSigned, info.ShowAsHex);
    }

    /// <summary>
    /// CE record type for a raw 8-byte pointer target (a dereferenced object/struct base):
    /// 8 Bytes shown as hex. Used by the one-click "Add ptr target to CE" push.
    /// </summary>
    public static CeRecordType PointerRecordType => new(CeVtQword, IsSigned: false, ShowAsHex: true);

    /// <summary>
    /// Convert a CE VariableType keyword (as produced by <see cref="MapCeField"/>) to its
    /// numeric <c>TVariableType</c> code. "Binary" (a bit-field bool) maps to Byte since the
    /// single-record command carries no bit start/length — pushing the containing byte is the
    /// most useful target for a "what accesses this address" breakpoint.
    /// </summary>
    private static int KeywordToValueType(string keyword) => keyword switch
    {
        "Byte" => CeVtByte,
        "2 Bytes" => CeVtWord,
        "4 Bytes" => CeVtDword,
        "8 Bytes" => CeVtQword,
        "Float" => CeVtSingle,
        "Double" => CeVtDouble,
        "String" => CeVtString,
        "Binary" => CeVtByte,
        _ => CeVtQword,
    };

    /// <summary>
    /// Map an array inner type name to CE field info.
    /// Similar to MapCeField but takes a type name string (for array element types).
    /// BoolProperty in arrays = full byte (no bitfield).
    /// Returns null for non-scalar types (StructProperty, ObjectProperty, etc.).
    /// </summary>
    private static CeFieldInfo? MapInnerTypeToCeField(string innerTypeName)
    {
        return innerTypeName switch
        {
            "FloatProperty" => new CeFieldInfo("Float"),
            "DoubleProperty" => new CeFieldInfo("Double"),

            // Signed integers
            "Int8Property" => new CeFieldInfo("Byte", IsSigned: true),
            "Int16Property" => new CeFieldInfo("2 Bytes", IsSigned: true),
            "IntProperty" => new CeFieldInfo("4 Bytes", IsSigned: true),
            "Int64Property" => new CeFieldInfo("8 Bytes", IsSigned: true),

            // Unsigned integers
            "ByteProperty" => new CeFieldInfo("Byte"),
            "UInt16Property" => new CeFieldInfo("2 Bytes"),
            "UInt32Property" => new CeFieldInfo("4 Bytes"),
            "UInt64Property" => new CeFieldInfo("8 Bytes"),

            // Bool in arrays: stored as full bytes (no bitfield)
            "BoolProperty" => new CeFieldInfo("Byte"),

            // FName index
            "NameProperty" => new CeFieldInfo("4 Bytes"),

            // Enum -- underlying value is typically int32
            "EnumProperty" => new CeFieldInfo("4 Bytes"),

            // Phase D: pointer types — 8 bytes, shown as hex
            "ObjectProperty" => new CeFieldInfo("8 Bytes", ShowAsHex: true),
            "ClassProperty" => new CeFieldInfo("8 Bytes", ShowAsHex: true),

            // Phase E: weak object pointer — 8 bytes (ObjectIndex + SerialNumber)
            "WeakObjectProperty" => new CeFieldInfo("8 Bytes", ShowAsHex: true),

            // Phase G: TSoftObjectPtr / TSoftClassPtr — first 8 bytes is FWeakObjectPtr
            // (ObjectIndex + SerialNumber). Element stride uses ArrayElemSize so
            // consecutive elements remain aligned to TPersistentObjectPtr layout.
            "SoftObjectProperty" => new CeFieldInfo("8 Bytes", ShowAsHex: true),
            "SoftClassProperty"  => new CeFieldInfo("8 Bytes", ShowAsHex: true),

            // Phase H: TLazyObjectPtr — first 8 bytes is FWeakObjectPtr
            "LazyObjectProperty" => new CeFieldInfo("8 Bytes", ShowAsHex: true),

            // Phase I: TScriptInterface — first 8 bytes is UObject*, show as pointer
            "InterfaceProperty" => new CeFieldInfo("8 Bytes", ShowAsHex: true),

            // Phase J: FScriptDelegate — first 8 bytes is FWeakObjectPtr (target).
            // Element stride uses ArrayElemSize so consecutive elements stay aligned
            // (16 without CasePreservingName, 24 with).
            "DelegateProperty" => new CeFieldInfo("8 Bytes", ShowAsHex: true),

            // Phase K: FMulticastScriptDelegate — first 8 bytes is the inner
            // TArray<FScriptDelegate>::Data pointer; element stride is 16.
            "MulticastDelegateProperty" => new CeFieldInfo("8 Bytes", ShowAsHex: true),
            "MulticastInlineDelegateProperty" => new CeFieldInfo("8 Bytes", ShowAsHex: true),

            _ => null // Non-scalar (StructProperty, etc.)
        };
    }
}
