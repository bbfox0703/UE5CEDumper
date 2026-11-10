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

#include <Windows.h>   // DWORD, ERROR_PIPE_BUSY, ERROR_ALREADY_EXISTS, GetCurrentProcessId
#include <string>

#include "Grimoire.h"  // PIPE_NAME_PREFIX

namespace Voll {

// ── The pipe's name: one per game process ───────────────────────────────────
// [PIPE-PER-PROCESS-2026-11-06]
//
// Every injected game used to serve the same \\.\pipe\UE5DumpBfx. Windows hands a client the OLDEST listening instance
// of a name, across processes (measured 2026-11-06), so with two games serving it a client -- even one of the UI's two
// lanes -- could reach the other game: a scan started in one game while its progress was read from another. With the
// pid in the name each game's server is its own, and a client names the game it wants.
//
// A pipe leaves \\.\pipe\ the moment its server process dies, a killed one included, even while a client still holds
// its handle (measured 2026-11-06). A listed name is therefore always a live server, and a reused pid cannot be
// mistaken for a dead game's.
// Plain decimal, no padding: the UI and the rigs parse the digits after the prefix.
inline std::wstring PipeNameForPid(DWORD pid) {
    return std::wstring(Grimoire::PIPE_NAME_PREFIX) + std::to_wstring(pid);
}

// This process's pipe. The pid never changes, so the name is built once.
inline const std::wstring& OwnPipeName() {
    static const std::wstring name = PipeNameForPid(GetCurrentProcessId());
    return name;
}

// A startup probe opens this process's pipe as a client to learn whether a server is already there. `opened` = the
// CreateFileW succeeded; `lastError` = GetLastError() when it did not.
//
// ERROR_PIPE_BUSY means a server IS there with every instance taken. The old probes read it as "no pipe" and started
// a second server on the same name. With the name per process, that second server would be a second copy of the DLL
// in this process (a stale proxy beside a fresh injection), and a client would reach either copy. ERROR_ACCESS_DENIED
// also means the name exists; only "not found" means there is no server.
inline bool ProbeFoundServer(bool opened, DWORD lastError) noexcept {
    return opened || lastError == ERROR_PIPE_BUSY || lastError == ERROR_ACCESS_DENIED;
}

// A deferring watcher only needs to know whether a server still holds this process's name, and asks every
// kClaimWatchPollMs for up to kClaimWatchMaxTries. Opening the pipe to ask CONNECTS to that server: each probe is a
// client arriving and leaving, twice a second, and a server with no other client tears its session state down each
// time. WaitNamedPipeW answers without connecting (measured 2026-11-10: TRUE while an instance listens, and the server
// saw no client; ERROR_SEM_TIMEOUT while every instance is busy; ERROR_FILE_NOT_FOUND once the name is gone).
// `waited` = WaitNamedPipeW's result; `lastError` = GetLastError() when it returned FALSE.
inline bool WaitSaysServed(bool waited, DWORD lastError) noexcept {
    return waited || lastError == ERROR_SEM_TIMEOUT || lastError == ERROR_PIPE_BUSY
                  || lastError == ERROR_ACCESS_DENIED;
}

// The watchers' WaitNamedPipeW timeout. Not 0: 0 is NMPWAIT_USE_DEFAULT_WAIT, the server's default wait.
inline constexpr DWORD kWatchWaitMs = 1;

// Who else serves this process's name, checked once on the FIRST instance a server start creates. The startup probe
// already defers to a server it can see; this catches what a probe cannot: two copies probing in the same moment, and a
// name another process created.
//
// Not FILE_FLAG_FIRST_PIPE_INSTANCE: it refuses a legitimate restart while this server's own connections from before
// the restart still hold their instances.
enum class FirstInstance {
    Sole,             // the name was free
    SharedInProcess,  // the name already had instances, created by this process: another copy of the DLL, or this
                      // server's own connections from before a restart. Serve, and say so in the log.
    ForeignCreator,   // another process created the name first: a client could reach it instead. Do not serve.
};

// `createError` = GetLastError() read right after a SUCCESSFUL CreateNamedPipeW. It is ERROR_ALREADY_EXISTS when the
// name already had an instance, in this process or another (measured 2026-11-10: 0 on a fresh name, 183 on a second
// instance from either). `creatorPid` = GetNamedPipeServerProcessId on the new SERVER handle, which names the process
// that created the name's FIRST instance, not this one (measured: a second process's server handle reported the first
// process), or 0 when the call failed.
//
// A process that creates the name AFTER us is not caught here: the name is ours from then on, and the UI checks the pid
// `get_pointers` reports after it connects.
inline FirstInstance ClassifyFirstInstance(DWORD createError, DWORD creatorPid, DWORD selfPid) noexcept {
    if (creatorPid != 0 && creatorPid != selfPid) return FirstInstance::ForeignCreator;
    if (createError == ERROR_ALREADY_EXISTS) return FirstInstance::SharedInProcess;
    return FirstInstance::Sole;
}

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
//
// [PIPE-PER-PROCESS-2026-11-06] The name now carries the pid, so a relaunched title's new process has a name of its
// own and never meets the dying one's server. The holder a probe finds can only be this process (a second copy of the
// DLL) or a process that created this process's name. The decision is unchanged.
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
// ⚠ An UNKNOWN holder defers rather than starts. ProbeFoundServer reports a busy pipe as found
// with holder 0, and that holder is another copy of the DLL in this process. Competing would be
// the worse error: two servers on one name means a client lands on whichever instance Windows
// hands it, so a connection could reach the other copy (an older build, typically) and report
// its answers as this one's. Deferring merely delays us, and the watcher makes that delay
// temporary — which is the whole difference between this and the behaviour it replaces.
inline StartAction DecideStart(bool pipeExists, DWORD holderPid, DWORD selfPid) {
    if (!pipeExists) return StartAction::StartNow;
    if (holderPid != 0 && holderPid == selfPid) return StartAction::AlreadyOurs;
    return StartAction::DeferAndWatch;
}

} // namespace Voll
