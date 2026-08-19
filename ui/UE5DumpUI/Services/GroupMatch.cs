using System;
using System.Collections.Generic;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Pure C# port of the DLL's <c>Orden</c> SDR / assignment matcher (header
/// <c>dll/src/Orden.h</c>), for **Snapshot Group Match** — finding objects in the
/// captured-snapshot corpus that hold ALL of N values at DISTINCT numeric offsets,
/// in any order (a System of Distinct Representatives). The source-agnostic seam the
/// group-value-scan spec reserved (§3.1): the matcher is fed <see cref="Leaf"/>s and
/// knows nothing about live memory, GObjects, or the SQLite store.
///
/// Snapshot semantics (vs the live DLL matcher): a leaf carries the per-snapshot
/// value SEQUENCE — the decoded numeric (<see cref="Leaf.Num"/>) for magnitude /
/// direction predicates and the raw hex (<see cref="Leaf.Hex"/>) for exact /
/// changed / unchanged byte compares — mirroring how <see cref="SpcEngine"/> splits
/// numeric vs hex. Mode A (single snapshot) → sequences of length 1, absolute
/// predicates only. Mode B (≥2 snapshots) → relative predicates compare first-vs-last.
///
/// Pure + AOT-safe: static methods, no reflection, no dynamic dispatch.
/// See docs/snapshot-group-match-spec.md.
/// </summary>
public static class GroupMatch
{
    /// <summary>Width scope for a slot — which leaf widths are eligible. Mirrors the
    /// live group's <c>NumericNoByte</c> / <c>NumericAll</c> meta types.</summary>
    public enum Scope
    {
        /// <summary>Numeric fields excluding 1-byte (Int8 / Byte) — the default
        /// (small values match a huge number of 1-byte fields).</summary>
        NumericNoByte,
        /// <summary>All numeric fields including 1-byte.</summary>
        NumericAll,
    }

    /// <summary>Per-slot predicate. Absolute predicates compare the leaf's value in
    /// the NEWEST snapshot against a target; relative predicates compare the leaf's
    /// value across the snapshot sequence (first-vs-last).</summary>
    public enum Predicate
    {
        // Absolute (need a target; evaluated on the newest snapshot).
        Exact,
        Bigger,
        Smaller,
        Between,
        // Relative (no target; compare first-vs-last across the sequence — needs ≥2).
        Changed,
        Unchanged,
        Increased,
        Decreased,
    }

    /// <summary>One numeric leaf of a captured object: its offset + declared property
    /// type + the per-snapshot value sequence. <see cref="Hex"/> and <see cref="Num"/>
    /// are index-aligned across the selected snapshots (oldest→newest). <see cref="Tag"/>
    /// is an opaque caller handle (echoed back via the matched leaf index) so the
    /// engine can rebuild display metadata without a second lookup; the matcher never
    /// interprets it.</summary>
    public sealed class Leaf
    {
        public int Offset;
        public string DeclaredType = "";
        public IReadOnlyList<string> Hex = Array.Empty<string>();
        public IReadOnlyList<double?> Num = Array.Empty<double?>();
        public int Tag;
    }

    /// <summary>One slot's predicate + (for absolute predicates) target value(s).</summary>
    public sealed class Slot
    {
        public Scope Scope = Scope.NumericNoByte;
        public Predicate Predicate = Predicate.Exact;
        public double? Target;     // absolute predicates
        public double? Target2;    // Between upper bound
        /// <summary>Displayed-integer reduction for Float/Double leaves (build 1672,
        /// replaces the old tolerance band). Integer leaves are reduce-invariant.</summary>
        public FloatRoundMode RoundMode = FloatRoundMode.Round;
    }

    // ---- width / type helpers (mirror SnapshotNumeric's declared-type set) ----

    private static int WidthBytes(string t) => t switch
    {
        "Int8Property" or "ByteProperty" => 1,
        "Int16Property" or "UInt16Property" => 2,
        "IntProperty" or "UInt32Property" or "FloatProperty" => 4,
        "Int64Property" or "UInt64Property" or "DoubleProperty" => 8,
        _ => 0, // non-numeric — never a group leaf
    };

    private static bool IsOneByte(string t) => t is "Int8Property" or "ByteProperty";
    private static bool IsFloat(string t) => t is "FloatProperty" or "DoubleProperty";
    private static bool IsUnsigned(string t) =>
        t is "ByteProperty" or "UInt16Property" or "UInt32Property" or "UInt64Property";

    /// <summary>Is <paramref name="t"/> a numeric field eligible under <paramref name="scope"/>?
    /// Public so source-agnostic callers (e.g. the SPC group matcher, which evaluates
    /// per-slot predicate chains via <see cref="SpcEngine"/> instead of
    /// <see cref="LeafSatisfiesSlot"/>) can reuse the same width-scope gate.</summary>
    public static bool TypeInScope(string t, Scope scope)
    {
        if (WidthBytes(t) == 0) return false;
        if (scope == Scope.NumericNoByte && IsOneByte(t)) return false;
        return true;
    }

    /// <summary>Does an absolute target value fit the leaf's declared width? Mirrors the
    /// live matcher's <c>NumericTargetSet.Find(width) != null</c> gate — an integer leaf
    /// rejects a non-integral or out-of-range target; a float leaf accepts any finite
    /// double. This is what stops "70000" from matching an Int16 field.</summary>
    private static bool TargetFitsWidth(double target, string t)
    {
        if (!double.IsFinite(target)) return false;
        if (IsFloat(t)) return true; // Float/Double hold any finite value (lenient on precision)

        // Integer types: target must be integral and within range.
        if (Math.Floor(target) != target) return false;
        int w = WidthBytes(t);
        bool uns = IsUnsigned(t);
        // Range per width/signedness.
        double min, max;
        switch (w)
        {
            case 1: (min, max) = uns ? (0, 255) : (-128, 127); break;
            case 2: (min, max) = uns ? (0, 65535) : (-32768, 32767); break;
            case 4: (min, max) = uns ? (0, 4294967295d) : (-2147483648d, 2147483647d); break;
            // 8-byte: clamp to the exactly-representable double range; values beyond
            // 2^53 lose precision (same caveat SPC's numeric_value carries). Accept the
            // full 64-bit range conceptually — direction/magnitude stay correct.
            case 8: (min, max) = uns ? (0, 18446744073709551615d) : (-9223372036854775808d, 9223372036854775807d); break;
            default: return false;
        }
        return target >= min && target <= max;
    }

    /// <summary>True when <paramref name="leaf"/> satisfies <paramref name="slot"/>.
    /// Absolute predicates evaluate the NEWEST snapshot value (last index); relative
    /// predicates compare first-vs-last and require ≥2 snapshots (a single snapshot has
    /// no baseline, mirroring the live first-scan rejecting prev-value predicates).</summary>
    public static bool LeafSatisfiesSlot(Leaf leaf, Slot slot)
    {
        if (leaf == null || slot == null) return false;
        if (!TypeInScope(leaf.DeclaredType, slot.Scope)) return false;

        switch (slot.Predicate)
        {
            case Predicate.Exact:
            case Predicate.Bigger:
            case Predicate.Smaller:
            case Predicate.Between:
            {
                if (slot.Target is not double target) return false;
                if (leaf.Num.Count == 0) return false;
                if (leaf.Num[leaf.Num.Count - 1] is not double v) return false; // NaN/null in newest
                // Comparisons use the decoded double (leaf.Num), like the rest of the
                // snapshot subsystem (SPC / Diff / Pivot all key on numeric_value). Integer
                // values above 2^53 lose precision — a known, snapshot-WIDE limitation.
                // RoundMode (build 1672) reduces a Float/Double value to the integer the
                // game DISPLAYS before comparing a WHOLE target; a fractional target on an
                // integer field is coerced via the mode. The width-fit gate runs on the
                // COERCED target so a fractional target/bound is range-checked at its
                // integer value (the live DLL coerces identically in BuildNumericTargets).
                bool isFloat = SnapshotNumeric.IsFloatType(leaf.DeclaredType);
                double fitTarget = isFloat ? target : SnapshotNumeric.CoerceIntTarget(target, slot.RoundMode);
                if (!TargetFitsWidth(fitTarget, leaf.DeclaredType)) return false;
                return slot.Predicate switch
                {
                    Predicate.Exact   => SnapshotNumeric.ExactMatch(v, target, leaf.DeclaredType, slot.RoundMode),
                    Predicate.Bigger  => SnapshotNumeric.OrderedMatch(v, target, leaf.DeclaredType, slot.RoundMode, bigger: true),
                    Predicate.Smaller => SnapshotNumeric.OrderedMatch(v, target, leaf.DeclaredType, slot.RoundMode, bigger: false),
                    Predicate.Between => slot.Target2 is double hi
                        && TargetFitsWidth(isFloat ? hi : SnapshotNumeric.CoerceIntTarget(hi, slot.RoundMode), leaf.DeclaredType)
                        && SnapshotNumeric.BetweenMatch(v, target, hi, leaf.DeclaredType, slot.RoundMode),
                    _ => false,
                };
            }

            case Predicate.Changed:
            case Predicate.Unchanged:
            {
                if (leaf.Hex.Count < 2) return false; // no baseline (Mode A)
                bool unchanged = slot.Predicate == Predicate.Unchanged;
                // Float leaves compare the DISPLAYED (reduced) values so sub-display
                // jitter (100.1→100.4) reads as Unchanged — the prev-value mode the user
                // asked for. Integer / non-finite leaves keep byte-exact hex (no >2^53
                // double-precision regression; an integer is reduce-invariant anyway).
                if (SnapshotNumeric.IsFloatType(leaf.DeclaredType)
                    && leaf.Num.Count >= 2
                    && leaf.Num[0] is double f0 && leaf.Num[leaf.Num.Count - 1] is double fN)
                    return SnapshotNumeric.TemporalMatch(f0, fN, leaf.DeclaredType, slot.RoundMode, unchanged ? 0 : 2);
                string a = leaf.Hex[0] ?? "";
                string b = leaf.Hex[leaf.Hex.Count - 1] ?? "";
                bool same = string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
                return unchanged ? same : !same;
            }

            case Predicate.Increased:
            case Predicate.Decreased:
            {
                if (leaf.Num.Count < 2) return false;
                if (leaf.Num[0] is not double first) return false;
                if (leaf.Num[leaf.Num.Count - 1] is not double last) return false;
                return SnapshotNumeric.TemporalMatch(first, last, leaf.DeclaredType, slot.RoundMode,
                                                     slot.Predicate == Predicate.Increased ? 1 : -1);
            }

            default:
                return false;
        }
    }

    // ---- SDR / Kuhn's augmenting-path matching (port of Orden::HasDistinctAssignment) ----

    private static bool TryAssign(int slot, List<int>[] m, int[] leafToSlot, bool[] seen)
    {
        foreach (int leaf in m[slot])
        {
            if (seen[leaf]) continue;
            seen[leaf] = true;
            if (leafToSlot[leaf] == -1 || TryAssign(leafToSlot[leaf], m, leafToSlot, seen))
            {
                leafToSlot[leaf] = slot;
                return true;
            }
        }
        return false;
    }

    /// <summary>True when every slot can be assigned a DISTINCT leaf (an SDR exists).
    /// Handles the duplicate-value case (two slots both wanting "24" need two different
    /// leaves) naturally via the matching. O(slots × edges) — trivial for 2..4 slots.</summary>
    public static bool HasDistinctAssignment(List<int>[] perSlot, int nLeaves)
    {
        if (nLeaves < 0) return false;
        var leafToSlot = new int[nLeaves];
        for (int i = 0; i < nLeaves; i++) leafToSlot[i] = -1;
        for (int s = 0; s < perSlot.Length; s++)
        {
            if (perSlot[s].Count == 0) return false;
            var seen = new bool[nLeaves];
            if (!TryAssign(s, perSlot, leafToSlot, seen)) return false;
        }
        return true;
    }

    /// <summary>After a successful <see cref="Run"/> / <see cref="HasDistinctAssignment"/>,
    /// recover the actual SDR: <c>result[s]</c> = the DISTINCT leaf index assigned to slot
    /// <c>s</c> (or -1 if none). Use this for each slot's representative leaf instead of
    /// <c>perSlot[s][0]</c> — the first matching leaf can be shared by two slots, which would
    /// render the same field twice; the SDR guarantees distinct representatives. Returns null
    /// if no complete assignment exists (caller should only call this when Run returned true).</summary>
    public static int[]? Assignment(List<int>[] perSlot, int nLeaves)
    {
        if (nLeaves < 0) return null;
        var leafToSlot = new int[nLeaves];
        for (int i = 0; i < nLeaves; i++) leafToSlot[i] = -1;
        for (int s = 0; s < perSlot.Length; s++)
        {
            if (perSlot[s].Count == 0) return null;
            var seen = new bool[nLeaves];
            if (!TryAssign(s, perSlot, leafToSlot, seen)) return null;
        }
        var slotToLeaf = new int[perSlot.Length];
        for (int s = 0; s < slotToLeaf.Length; s++) slotToLeaf[s] = -1;
        for (int leaf = 0; leaf < nLeaves; leaf++)
            if (leafToSlot[leaf] >= 0) slotToLeaf[leafToSlot[leaf]] = leaf;
        return slotToLeaf;
    }

    /// <summary>Build each slot's matching-leaf list (capped at <paramref name="perSlotCap"/>)
    /// and report whether a distinct simultaneous assignment exists. <paramref name="perSlot"/>
    /// is filled even on a false return (the convergence lists the caller persists), but a
    /// false return means the object is NOT a group candidate. Returns false for &lt; 2 slots
    /// (a group needs ≥ 2 values; the caller enforces the 2..4 bound).</summary>
    /// <param name="perSlotCapHit">A slot matched MORE leaves than the cap kept, so its
    /// witness list — and the "All fields" popup built from it — is a page. Required, not
    /// optional: the DLL sibling has published <c>per_slot_cap_hit</c> on the wire since
    /// AE13, and the snapshot path silently discarded the same fact (audit #5 AF13). It is
    /// set independently of the return value, because a truncated slot can still fail the
    /// assignment — and that is the case where the user most needs to know the miss might
    /// be an artifact of the cap.</param>
    /// <remarks>
    /// THE DEFAULT WAS 8 AND THAT WAS A BUG — the same one the DLL's Orden had (fixed
    /// 2026-08-05). The cap truncates in leaf order, which is field-declaration order with
    /// the BASE class first, so on any Actor the first eight satisfying leaves are
    /// PrimaryActorTick/CustomTimeDilation and a derived class's OWN fields never make the
    /// list. Because this list is also what a later temporal pass compares against, a
    /// Changed/Unchanged match could only ever see never-changing engine fields.
    ///
    /// <para><b>What is shared with the live Group Scan is the DEFAULT, not the cap in
    /// force</b> (audit #5 AF12 — the previous wording claimed the stronger thing). Both
    /// sides start from <see cref="Constants.GroupPerSlotCap"/>, but Value Search exposes
    /// a per-session picker (<c>GroupPerSlotCap</c>, 8..4096) that it sends to the DLL as
    /// <c>per_slot_cap</c>, and a snapshot query has no such control — so the moment the
    /// user moves that picker the two DO diverge, and the same object can keep a longer
    /// witness list live than in the corpus. Coupling a snapshot query to another panel's
    /// combo box would be a surprising product decision, not a bug fix; what this code
    /// owes the user instead is to SAY which cap it applied and whether it bit, which is
    /// what <paramref name="perSlotCapHit"/> is for.</para>
    /// </remarks>
    public static bool Run(IReadOnlyList<Leaf> leaves, IReadOnlyList<Slot> slots,
                           out List<int>[] perSlot, out bool perSlotCapHit,
                           int perSlotCap = Constants.GroupPerSlotCap)
    {
        perSlot = new List<int>[slots.Count];
        perSlotCapHit = false;
        for (int s = 0; s < slots.Count; s++) perSlot[s] = new List<int>();
        if (slots.Count < 2) return false;

        for (int s = 0; s < slots.Count; s++)
        {
            for (int li = 0; li < leaves.Count; li++)
            {
                if (LeafSatisfiesSlot(leaves[li], slots[s]))
                {
                    if (perSlot[s].Count < perSlotCap) perSlot[s].Add(li);
                    else perSlotCapHit = true;   // matched, but not kept
                }
            }
            if (perSlot[s].Count == 0) return false; // early reject
        }
        return HasDistinctAssignment(perSlot, leaves.Count);
    }
}
