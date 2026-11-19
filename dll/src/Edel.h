#pragma once

// ============================================================
// Edel — 艾德爾 (催眠魔法 — Hypnosis, "noble")
// CurrentTarget: auto-detect the actor the local player is currently
// targeting / focused on. Walks the GWorld → PlayerController → Pawn chain and
// scores the player's outgoing object-pointer fields (Edel reads what the
// player's mind is fixed on), so the user does not have to guess a class-name
// keyword to find the focused enemy + its related objects (AttributeSet / HP).
// ============================================================

#include <cstdint>
#include <string>
#include <vector>

namespace Edel {

// One scored candidate target actor.
struct TargetCandidate {
    uintptr_t   addr         = 0;
    int32_t     index        = -1;    // GObjects InternalIndex; -1 if unreadable
    std::string name;
    std::string className;
    int32_t     score        = 0;     // higher = more likely the focused target
    uintptr_t   sourceObject = 0;     // the PC / Pawn / component the pointer was read FROM
    std::string sourceClass;          // class name of sourceObject (UI subtitle)
    std::string fieldName;            // reflected field/path that pointed here
    int32_t     fieldOffset  = -1;    // offset within sourceObject; -1 for container/path edges
    std::string reason;               // human string: which signals matched
};

// Diagnostics for the resolution chain — ALWAYS populated, even on failure, so
// the UI can tell the user exactly where detection stopped (no world? no PC?
// PC but no pawn? pawn but zero candidates?).
struct CurrentTargetResult {
    bool        resolved         = false; // chain reached a Pawn AND a positive-scoring candidate
    uintptr_t   world            = 0;     // resolved UWorld (0 = GWorld not available)
    uintptr_t   playerController = 0;     // 0 = no local PC
    uintptr_t   playerPawn       = 0;     // 0 = PC has no pawn
    std::vector<TargetCandidate> candidates;  // sorted desc by score; [0] is the auto-pick
    std::string note;                     // user-facing status / failure reason
};

// Detect the actor the local player is currently targeting / focused on.
//
// Walks GWorld → local PlayerController → Pawn (a self-contained copy of the
// teleport resolution chain — engine-stable field names, version-agnostic),
// enumerates the outgoing object pointers of {PC, Pawn, and their owned
// ActorComponents} via Aura::CollectOutgoingObjectPtrs, keeps those that pass
// the is-Actor structural super-chain test (excluding the player's own PC/pawn),
// scores each by name keyword + structural signal, and returns them sorted
// best-first. The top candidate (score > 0) is the auto-pick.
//
// Fast + bounded: NO full GObjects scan except the PlayerController
// instance-scan fallback inside the chain (only when the engine chain fails).
// Read-only — writes nothing to game memory. Honors Tot::Requested().
// maxCandidates caps the returned list (default 8; a value <= 0 means the default,
// and anything above 64 is clamped to 64).
CurrentTargetResult DetectCurrentTarget(int32_t maxCandidates = 8);

}  // namespace Edel
