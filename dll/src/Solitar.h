#pragma once

// ============================================================
// Solitar — 索莉塔 (研究人類的大魔族 — Greater demon studying humanity)
// GodMode: force the local player pawn's AActor::bCanBeDamaged bitfield off via
// live UE reflection (the same FBoolProperty bit-write Wirbel uses for
// bShowMouseCursor, retargeted to the pawn). Stateful ON/OFF toggle backed by a
// re-assert worker so it survives respawns. Pure memory write — no UFunction
// invoke, no game thread. UE4/UE5 version-agnostic. Contract: docs/godmode-spec.md.
// ============================================================

#include <cstdint>
#include <string>
#include <vector>

#include "Ubel.h"   // ClassifyBoolLayout: PickBoolBits takes its Native verdict

namespace Solitar {

// One protection bool resolved on the live pawn, expressed as offsets from the
// PAWN base so a standalone CE-Lua trainer can freeze the bit without the DLL.
// ByteOffset = FBoolProperty.Offset + within-byte ByteOffset; Mask is a single
// bit; Protect is the bit value that means "protected" (what GodMode ON writes).
// A native bool's whole byte still bakes one bit: TrainerMaskFor says why.
struct ProtectBit {
    std::string name;       // property name (e.g. "bCanBeDamaged")
    int32_t     byteOffset; // absolute byte offset from the pawn base
    uint8_t     mask;       // single-bit mask (TrainerMaskFor)
    int8_t      protect;    // bit value meaning "protected" (0 for bCanBeDamaged)
};

// Result codes shared across exports, pipe, and mailbox (docs/godmode-spec.md §6.3).
// Non-negative SetGodMode/GetGodMode returns are the OBSERVED live state (1/0).
enum ProtectResult : int32_t {
    PR_OK            = 0,
    PR_ERR_NOT_INIT  = -1,   // DLL not initialized / GWorld unavailable
    PR_ERR_NO_PAWN   = -3,   // local pawn null (menu/cutscene/spectator)
    PR_ERR_REFLECT   = -4,   // bCanBeDamaged FBoolProperty not resolved
    PR_ERR_WRITE     = -10,  // raw bit write failed
};

// Combined snapshot for the UI badge / connect-time query.
struct State {
    int8_t want;          // desired toggle (1 = GodMode requested), survives UI reconnect
    int8_t live;          // observed live state (1 = immune, 0 = can be damaged, -1 = unresolvable)
    bool   resolvable;    // could the live pawn + property be read this instant
};

// Apply a desired single-bit value to a byte, leaving the other 7 bits intact.
// Pure helper — header-inline so dll_helpers_test can cover it without linking
// Solitar.cpp (the project's leaf-test pattern).
inline uint8_t ApplyBoolBit(uint8_t cur, uint8_t mask, bool value) {
    return value ? static_cast<uint8_t>(cur | mask)
                 : static_cast<uint8_t>(cur & ~mask);
}

// [SOLIDE-NATIVE-BOOL] Where one bool property's value sits in its byte, and how it is read and written: UE's own
// FBoolProperty rule -- read (byte & FieldMask) != 0, write (byte & ~FieldMask) | (value ? ByteMask : 0). A packed
// bitfield bool owns one bit, both masks being that bit, so this is ApplyBoolBit. A NATIVE bool (every Blueprint bool,
// a plain `bool bFoo`) owns the whole byte, FieldMask 0xFF, and writes ByteMask 0x01 -- 0x01 / 0x00, never 0xFF,
// which C++ that XORs a bool reads as still true ([A3-BOOL-NATIVE-NOWRITE]).
struct BoolBits {
    uint8_t byteOff   = 0;   // ByteOffset: the byte inside the property's Offset_Internal
    uint8_t fieldMask = 0;   // the bits the value owns
    uint8_t byteMask  = 0;   // what true writes there
};

inline bool ReadBoolValue(uint8_t cur, const BoolBits& b) {
    return (cur & b.fieldMask) != 0;
}

inline uint8_t WriteBoolValue(uint8_t cur, const BoolBits& b, bool value) {
    return static_cast<uint8_t>((cur & ~b.fieldMask) | (value ? b.byteMask : 0));
}

// [SOLIDE-NATIVE-BOOL] review: the mask a standalone trainer bakes for a protection bool. Its Lua sets and clears ONE
// bit by arithmetic -- isSet = floor(byte / mask) % 2, then byte + mask or byte - mask -- so the mask must be a single
// bit, and true must land as UE's own value: a packed bool's bit, a native bool's ByteMask 0x01, which over the 0 / 1 a
// C++ bool holds writes exactly 0x01 / 0x00. A native bool's FieldMask 0xFF would write 0xFF over 0x00 (the value
// [A3-BOOL-NATIVE-NOWRITE] forbids) and 0x100 -- stored as 0x00 -- over 0x01, turning protection OFF when asked ON.
inline uint8_t TrainerMaskFor(const BoolBits& b) {
    return b.fieldMask;
}

// A bool property's layout from the {FieldSize, ByteOffset, ByteMask, FieldMask} quads read at each probed offset,
// in probe order (`readOk[i]` false = that read faulted). A packed layout at ANY offset is taken first, under the rule
// this probe has always applied -- one FieldMask bit, a ByteOffset inside the 8-byte bitfield word, a one-bit
// ByteMask -- so a packed bool resolves exactly as it did. Only when there is none does Ubel::ClassifyBoolLayout's
// Native verdict count. Anything else is refused: the all-zero a missed probe reads is never native, and a whole-byte
// write there is the AA1 corruption of up to 7 sibling bools.
inline bool PickBoolBits(const uint8_t (*quads)[4], const bool* readOk, int n, BoolBits& out) {
    for (int i = 0; i < n; ++i) {
        if (!readOk[i]) continue;
        const uint8_t fieldSize = quads[i][0], bo = quads[i][1], bm = quads[i][2], fm = quads[i][3];
        if (fieldSize == 1 && fm && (fm & (fm - 1)) == 0 && bo <= 7 && bm && (bm & (bm - 1)) == 0) {
            out = BoolBits{ bo, fm, fm };
            return true;
        }
    }
    for (int i = 0; i < n; ++i) {
        if (!readOk[i]) continue;
        if (Ubel::ClassifyBoolLayout(quads[i][0], quads[i][1], quads[i][2], quads[i][3]) == Ubel::BoolLayout::Native) {
            out = BoolBits{ 0, 0xFF, 0x01 };
            return true;
        }
    }
    return false;
}

// Generic damage/invincibility flag matcher (T2 — the "Build 1256" note at the top of docs/godmode-spec.md).
// Given an ALREADY-LOWERCASED reflected bool property name, return true when it
// is a known damage/invincibility flag and set outProtect to the value that
// means "protected" (what GodMode ON writes; OFF writes the inverse). Universal
// keyword table — no per-game config, same philosophy as PropertyScoringTable.
// Pure — covered by dll_helpers_test. Deliberately conservative: only
// unambiguous receive-damage / invincibility terms (NOT bare "candamage" /
// "nodamage" / "damageable", which can mean "deals damage" on a pawn).
inline bool MatchProtectionBool(const std::string& nameLower, bool& outProtect) {
    struct Rule { const char* kw; bool protect; };
    static const Rule kRules[] = {
        { "invulnerab",      true  },  // bInvulnerable / bIsInvulnerable
        { "invincib",        true  },  // bInvincible / bIsInvincible
        { "immort",          true  },  // bImmortal / bIsImmortal
        { "godmode",         true  },  // bGodMode
        { "unkillable",      true  },
        { "cannotdie",       true  },
        { "cannotbekilled",  true  },
        { "deathless",       true  },
        { "muteki",          true  },  // 無敵 (romaji) — common in JP titles
        { "damageimmun",     true  },  // bDamageImmune / DamageImmunity
        { "notakedamage",    true  },
        { "cantakedamage",   false },  // false = cannot take damage
        { "canbedamaged",    false },  // AActor::bCanBeDamaged
    };
    for (const auto& r : kRules)
        if (nameLower.find(r.kw) != std::string::npos) { outProtect = r.protect; return true; }
    return false;
}

// Turn GodMode on/off. GodMode ON ⇒ AActor::bCanBeDamaged set FALSE. Applies to
// the live pawn immediately and starts/stops the re-assert worker. Returns the
// OBSERVED live state (1/0) or a negative ProtectResult.
int32_t SetGodMode(bool on);

// Read the OBSERVED GodMode state on the live pawn (1 = immune, 0 = can be
// damaged) or a negative ProtectResult when the pawn/property can't be resolved.
int32_t GetGodMode();

// The desired toggle (s_wantGod), independent of whether a pawn currently
// resolves — lets the UI restore the toggle on reconnect even from a menu.
bool IsGodModeWanted();

// Combined state for the badge. Always returns PR_OK (fills State; live = -1 and
// resolvable = false when no pawn).
int32_t GetState(State& out);

// General primitive (docs/godmode-spec.md §5.4) — v2 hook for "force any bool"
// from Property Search. Resolve + read-modify-write one reflected FBoolProperty
// on an object -- its bit, or a native bool's whole byte (PickBoolBits). `on` is
// the DESIRED VALUE OF THE PROPERTY. One-shot (no re-assert). Returns the observed
// property value (1/0) or a negative ProtectResult.
int32_t SetActorBool(uintptr_t obj, uintptr_t classAddr, const char* propName, bool on);

// Read-only companion to SetActorBool: resolve + read one reflected FBoolProperty
// on an object. Returns the observed value (1/0) or a negative ProtectResult
// when the object/property can't be resolved. Used by Solide to capture a bool's
// restore base before forcing it.
int32_t GetActorBool(uintptr_t obj, uintptr_t classAddr, const char* propName);

// Resolve ALL matched protection bits on the CURRENT live pawn (bCanBeDamaged +
// any invincibility bool the universal keyword table matches), expressed as
// offsets from the pawn base — for baking into a standalone CE-Lua trainer. Read
// only: does NOT touch the toggle state or the re-assert worker's cache. Returns
// PR_OK (out may be empty when the pawn has no matched bool) or a negative
// ProtectResult when the pawn can't be resolved.
int32_t ResolveProtectBits(std::vector<ProtectBit>& out);

// Stop and join the re-assert worker. Idempotent. Called by SetGodMode(false)
// and from UE5_Shutdown() before the DLL unloads.
void StopWorker();

} // namespace Solitar
