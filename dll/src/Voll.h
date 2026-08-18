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

} // namespace Voll
