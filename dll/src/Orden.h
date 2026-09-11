// ============================================================
// Orden — 歐爾登 (貴族家主, "秩序" — Noble House Head, "Order")
// GroupMatch: source-agnostic SDR / assignment core for "Multiple values
// group scan". Given a block's numeric leaves (a set of {position, width,
// value-bytes}) and N slot targets, decide whether ALL N targets can be
// satisfied SIMULTANEOUSLY by DISTINCT leaves (a System of Distinct
// Representatives), and return each slot's matching-leaf candidate list.
//
// This is the object-aware analogue of Cheat Engine's "Group Scan": the
// block is one UObject instance (and, in later phases, one numeric container
// / one struct-array element / one actor + its attribute components), and a
// logical hit is "this block holds value A at some offset AND value B at a
// DIFFERENT offset AND ..." — order-independent and position-independent
// (the slots' in-memory offsets may even be in any order).
//
// Source-agnostic by design: Orden never reads game memory and never touches
// GObjects. The CALLER produces the Leaf set — the live value scan reads
// property bytes via Ubel/Macht; a future Snapshot reuse decodes captured DB
// hex; a future Class-Pivot reuse passes per-snapshot sequence values — and
// Orden only does the pure combinatorial match. That seam is what lets
// Snapshot / SPC Query / Class Pivot reuse this without dragging in the
// scanner.
//
// Width + multi-numeric target machinery is shared with the single-value scan:
// a Leaf carries its resolved Radar::DataType width, and a slot's target is a
// Radar::NumericTargetSet (the same pre-parsed "one buffer per fitting width"
// structure BuildNumericTargets produces), so "Str = 24" transparently matches
// an Int16 / Int32 / Int64 / Float / Double leaf. P2: a slot also carries a
// Radar::ScanType predicate (Exact / Bigger / Smaller on the first scan) routed
// through Radar::ComparePredicate; prev-value predicates are handled by the
// caller's refine, not here (LeafSatisfiesSlot rejects them — no baseline).
//
// Dependency-free apart from Radar.h (itself std-only — see dll/CMakeLists.txt:
// "Radar.cpp is std-only"), so it unit-tests with synthetic leaves and NO live
// process. N is small (2..4 in the UI) so the assignment uses Kuhn's
// augmenting-path matching; per-slot match lists are capped (perSlotCap) to
// bound a GroupCandidate's memory.
// ============================================================

#pragma once

#include "Radar.h"

#include <cstdint>
#include <cstring>
#include <vector>

namespace Orden {

// One numeric leaf within a block (source-agnostic). `position` is an
// offset-within-object (direct field) or a container element index; Orden does
// not interpret it — it is carried through so the caller can report WHERE each
// value matched. `width` selects which NumericTargetSet entry to compare
// against; `bytes` holds the little-endian value (only SizeOf(width) bytes are
// significant). `descriptorIdx` / `elementIndex` are opaque caller handles
// (e.g. an index into Radar's FieldDescriptor pool) echoed back in the match so
// the caller can rebuild display metadata without a second lookup.
struct Leaf {
    int32_t         position      = 0;
    Radar::DataType width         = Radar::DataType::Int32;
    uint8_t         bytes[8]      = {};
    uint32_t        descriptorIdx = 0;
    int32_t         elementIndex  = -1;
};

// One slot's target: the pre-parsed multi-width target set for the user's value
// (built once per slot via Radar::BuildNumericTargets for a meta type, or a
// single-entry set for a concrete width) plus a per-slot predicate.
//
// P2: `st` is the slot's first-scan predicate (Exact / Bigger / Smaller /
// Between — a targeted compare against `targets`, with `targets2` the upper
// bound for Between). Prev-value predicates (Changed / Increased / Decreased /
// Unchanged) compare against a per-leaf baseline that only exists AFTER a
// refine, so they are honoured by the refine path (Aura::RefineGroupCandidates),
// never by the first-scan MatchGroup below — LeafSatisfiesSlot rejects them so a
// prev-value slot can never spuriously match on the first scan. `roundMode` is
// the float displayed-integer reduction (build 1672, replaces the old tolerance).
struct SlotTarget {
    const Radar::NumericTargetSet* targets   = nullptr;
    Radar::ScanType                st        = Radar::ScanType::Exact;
    Radar::RoundMode               roundMode = Radar::RoundMode::Round;
    const Radar::NumericTargetSet* targets2  = nullptr;  // Between upper bound
};

// Per-slot list of leaf indices (into the Leaf vector) that currently satisfy
// the slot. This IS the progressive-convergence list the caller persists as a
// GroupCandidate: refine re-reads these positions and shrinks the list; a
// singleton means the offset is "locked".
struct SlotMatches {
    std::vector<int> leafIdx;
};

// True when `leaf` satisfies `slot` under the slot's first-scan predicate
// (Exact / Bigger / Smaller / Between). Prev-value predicates have no baseline
// on the first scan, so they never match here (the refine path handles them).
// The width gate asks the target SET, not "does the value fit": for Exact a width
// with no encoding is skipped, but for Smaller/Bigger the set may instead hold an
// AlwaysTrue verdict -- every value of the width satisfies (audit #5 AB4) -- and
// that is honoured. Exact reduces to byte-equality via ComparePredicate; Between
// additionally needs the upper bound (`targets2`) to fit the width.
inline bool LeafSatisfiesSlot(const Leaf& leaf, const SlotTarget& slot) {
    if (!slot.targets) return false;
    if (Radar::IsPrevValueScanType(slot.st)) return false;  // refine-only; no first-scan baseline
    // [W2-ORDEN-FINDENTRY] FindEntry, not Find: Find() hides an AlwaysTrue entry, so a
    // `Bigger -5` slot skipped every unsigned leaf and one lost width dropped the group.
    const Radar::NumericTargetSet::Entry* te = slot.targets->FindEntry(leaf.width);
    if (!te) return false;                                  // no value of this width can satisfy it
    const size_t n = Radar::SizeOf(leaf.width);
    if (n == 0 || n > sizeof(leaf.bytes)) return false;     // non-fixed-width (string/etc.)
    const uint8_t* tb2 = nullptr;
    if (slot.st == Radar::ScanType::Between) {
        // Between needs BOTH bounds and is never given a verdict (BuildNumericTargets emits
        // AlwaysTrue for Smaller/Bigger only), so only a real encoding counts here.
        if (te->fit != Radar::NumericTargetSet::Fit::Encoded) return false;
        tb2 = slot.targets2 ? slot.targets2->Find(leaf.width) : nullptr;
        if (!tb2) return false;                             // upper bound can't fit this width
    }
    return Radar::ComparePredicate(leaf.width, slot.st, leaf.bytes, te, tb2, slot.roundMode);
}

// Internal: Kuhn's augmenting path — try to assign `slot` a distinct leaf,
// bumping already-assigned slots along an alternating path if needed.
inline bool TryAssign_(int slot, const std::vector<SlotMatches>& m,
                       std::vector<int>& leafToSlot, std::vector<char>& seen) {
    for (int leaf : m[static_cast<size_t>(slot)].leafIdx) {
        const size_t lf = static_cast<size_t>(leaf);
        if (seen[lf]) continue;
        seen[lf] = 1;
        if (leafToSlot[lf] == -1
            || TryAssign_(leafToSlot[lf], m, leafToSlot, seen)) {
            leafToSlot[lf] = slot;
            return true;
        }
    }
    return false;
}

// True when every slot can be assigned a DISTINCT leaf (a System of Distinct
// Representatives exists). `nLeaves` bounds the leaf-index space. O(slots *
// edges) — trivial for the 2..4 slots the UI allows. An empty slot list means
// no SDR. Handles the duplicate-value case ("Str = 24, Def = 24" needs two
// different leaves both holding 24) naturally via the matching.
inline bool HasDistinctAssignment(const std::vector<SlotMatches>& m, int nLeaves) {
    if (nLeaves < 0) return false;
    std::vector<int> leafToSlot(static_cast<size_t>(nLeaves), -1);
    for (size_t s = 0; s < m.size(); ++s) {
        if (m[s].leafIdx.empty()) return false;
        std::vector<char> seen(static_cast<size_t>(nLeaves), 0);
        if (!TryAssign_(static_cast<int>(s), m, leafToSlot, seen)) return false;
    }
    return true;
}

// Build each slot's matching-leaf list (capped at perSlotCap) and report
// whether a distinct simultaneous assignment exists. `out` is filled even on a
// false return (callers may inspect partial matches), but a false return means
// the block is NOT a group candidate. Early-rejects the moment a slot has zero
// matches. Returns false for an empty slot set (a group scan needs >= 2 slots;
// the caller enforces the 2..4 bound).
/// @param perSlotCap  How many satisfying leaves to KEEP per slot.
/// @param outTruncated Set true if any slot had more matches than the cap.
///
/// THE CAP IS ORDER-BIASED, AND THAT BIAS WAS A BUG (2026-08-05). Leaves arrive in
/// field-declaration order, base class first, so on any AActor the first 8 satisfying
/// leaves are `PrimaryActorTick.*`, `CustomTimeDilation` and friends -- and a derived
/// class's OWN fields, which are the ones a user actually searches for, never made the
/// list. The kept set is then ALSO what a later refine re-reads, so a `Changed` pass
/// compared only never-changing engine fields and pruned every candidate to zero. The
/// symptom looked like "group scan cannot see my property"; the cause was a cap of 8.
///
/// One list was serving two purposes: the assignment check needs only a handful, the
/// refine needs everything. The cap now sizes for the REFINE, and truncation is reported
/// instead of being silent -- a cap that quietly drops the interesting half is exactly
/// the kind of thing nothing else in this codebase can notice.
/// Sized for the REFINE, not the assignment check: it must hold every leaf a later
/// Changed/Decreased pass might need to re-read. 256 covers any realistic object's
/// numeric fields, and the truncation flag catches the rest rather than hiding them.
inline constexpr int kDefaultPerSlotCap = 256;

inline bool MatchGroup(const std::vector<Leaf>& leaves,
                       const std::vector<SlotTarget>& slots,
                       std::vector<SlotMatches>& out,
                       int perSlotCap = kDefaultPerSlotCap,
                       bool* outTruncated = nullptr) {
    out.assign(slots.size(), SlotMatches{});
    if (slots.empty()) return false;
    for (size_t s = 0; s < slots.size(); ++s) {
        int satisfying = 0;
        for (size_t li = 0; li < leaves.size(); ++li) {
            if (LeafSatisfiesSlot(leaves[li], slots[s])) {
                ++satisfying;
                if (static_cast<int>(out[s].leafIdx.size()) < perSlotCap)
                    out[s].leafIdx.push_back(static_cast<int>(li));
            }
        }
        if (satisfying > perSlotCap && outTruncated) *outTruncated = true;
        if (out[s].leafIdx.empty()) return false;
    }
    return HasDistinctAssignment(out, static_cast<int>(leaves.size()));
}

} // namespace Orden
