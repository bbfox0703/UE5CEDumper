// ============================================================
// Voll — フォル (老矮人友人 — "full", an old dwarf friend)
// Pipe-accept capacity logging policy: decide WHAT the accept loop should log
// when CreateNamedPipe cannot create the next listener instance because all
// kMaxPipeInstances are already in use (ERROR_PIPE_BUSY) — the DESIGNED
// behaviour when the pool is FULL, not a fault.
//
// Extracted (Scharf pattern) so the accept loop's once-not-per-second logging
// can be unit-tested without a running pipe server; no test target compiles
// Fern.cpp. The original inline check logged ERROR_PIPE_BUSY at ERROR once a
// second forever ([PIPEBUSY-2026-08-18]: 1,826 ERROR lines in ~31.5 min on one
// Avowed session, evicting real diagnostics as the 8 MB pipe log rotated, and
// naming the wrong thing — "CreateNamedPipe failed" reads like a broken server).
// This turns the at-capacity state into one INFO on entry + one on recovery,
// while ANY OTHER errno still logs ERROR every time.
// ============================================================

#pragma once

#include <Windows.h>   // DWORD, ERROR_PIPE_BUSY

namespace Voll {

// What the accept loop should log for a given CreateNamedPipe outcome.
enum class AcceptLog {
    None,             // stay silent (already-announced capacity, or an ordinary success)
    EnterCapacity,    // INFO once: all instances busy, now waiting for a free slot
    RecoverCapacity,  // INFO once: a slot freed, resuming accept
    Error,            // ERROR: a genuinely unexpected CreateNamedPipe failure
};

// Called on a CreateNamedPipe FAILURE. `err` is GetLastError(); `atCapacity` is the
// accept loop's latch (in/out).
//
// ERROR_PIPE_BUSY == "all pipe instances are in use" == the cap doing its job. Announce
// it once (EnterCapacity), then stay silent (None) while it holds. ANY OTHER errno is a
// real fault and ALWAYS returns Error — the capacity latch NEVER suppresses it — so a
// later, different failure is never lost. A different-errno failure does NOT clear the
// latch: whether the pool is at capacity is unrelated to a transient unrelated error, so
// once the pool frees, the recovery line still fires exactly once.
inline AcceptLog OnCreateFailure(DWORD err, bool& atCapacity) noexcept {
    if (err == ERROR_PIPE_BUSY) {
        if (atCapacity) return AcceptLog::None;
        atCapacity = true;
        return AcceptLog::EnterCapacity;
    }
    return AcceptLog::Error;
}

// Called on a CreateNamedPipe SUCCESS. Returns RecoverCapacity exactly once — when we had
// been at capacity — and clears the latch; otherwise None.
inline AcceptLog OnCreateSuccess(bool& atCapacity) noexcept {
    if (atCapacity) {
        atCapacity = false;
        return AcceptLog::RecoverCapacity;
    }
    return AcceptLog::None;
}


// ── Who should own the pipe at startup ──────────────────────────────────────
// [RELAUNCHPIPE-2026-08-19]
//
// `UE5_StartPipeServer` used to decide with a single `CreateFileW(OPEN_EXISTING)`: pipe
// answers => "another instance is running" => skip, permanently. On a title that RELAUNCHES
// ITSELF that is a TOCTOU race against the DYING first process — measured on OCTOPATH
// TRAVELER, 3 runs out of 3: PID A logs `pipe server started`, PID B starts 3 s later and
// logs `pipe already exists — skipping`, then A exits and takes the pipe with it. The
// survivor is left with our DLL mapped, the game running fine, and NO server at all, so
// nothing can ever connect. It was proven by repair: calling the export by hand in the
// survivor started the server and the sweep then completed normally.
//
// Splitting the decision out here (Scharf pattern, same reason as the accept policy above)
// is what makes it testable — no test target compiles Frieren.cpp either.
// How long a deferring instance keeps watching, and how often it looks. Shared by BOTH
// watchers — Frieren's (claims the pipe) and Heiter's (re-runs the whole auto-start) — so the
// two cannot drift apart.
//
// The case they exist for, a self-relaunching title, frees the pipe within SECONDS, so this is
// generous rather than tuned. BOUNDED rather than endless, so an instance deferring to a
// genuinely long-lived other one does not carry an immortal thread; giving up is logged, and it
// is never worse than the behaviour it replaces, which gave up instantly and silently.
inline constexpr int kClaimWatchPollMs   = 500;
inline constexpr int kClaimWatchMaxTries = 600;   // 600 x 500 ms = 5 minutes

enum class StartAction {
    StartNow,        // nobody holds the pipe — create the server
    AlreadyOurs,     // this process already serves it; the call is a no-op, not a conflict
    DeferAndWatch,   // someone else holds it — do not compete, but WATCH for it to free
};

// `pipeExists` = CreateFileW(OPEN_EXISTING) succeeded. `holderPid` = the server end's owner
// from GetNamedPipeServerProcessId, or 0 when that could not be determined.
//
// ⚠ An UNKNOWN holder defers rather than starts. Competing would be the worse error: two
// servers on one name means a client lands on whichever instance Windows hands it, so a
// connection could reach the wrong game's DLL and answer questions about the wrong process.
// Deferring merely delays us, and the watcher makes that delay temporary — which is the whole
// difference between this and the behaviour it replaces.
inline StartAction DecideStart(bool pipeExists, DWORD holderPid, DWORD selfPid) {
    if (!pipeExists) return StartAction::StartNow;
    if (holderPid != 0 && holderPid == selfPid) return StartAction::AlreadyOurs;
    return StartAction::DeferAndWatch;
}

} // namespace Voll
