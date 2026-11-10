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
#include <string_view>
#include <vector>
#include <json.hpp>    // the holders list (pipe_clients, the at-capacity line)

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


// ── The command a request names ──────────────────────────────────────────────
// [PIPE-PER-PROCESS-2026-11-06] Stage C. The connection thread names each request for the repeat-suppressed log line,
// Stop's drain diagnostic, get_diagnostics' per-command timing and pipe_clients' "last command".
inline constexpr size_t kMaxCmdNameChars = 64;

// Python's json.dumps writes `"cmd": "x"` with a space, which an exact match for `"cmd":"` never names; whitespace is
// allowed on both sides of the colon. Only identifier characters are kept, at most kMaxCmdNameChars, so the name is
// safe to log and to put in JSON whatever the client sent. A cheap scan, not a parse: DispatchCommand parses the line.
inline std::string ExtractCmdName(std::string_view line) {
    auto isSpace = [](char c) { return c == ' ' || c == '\t' || c == '\r' || c == '\n'; };
    const size_t key = line.find("\"cmd\"");
    if (key == std::string_view::npos) return {};
    size_t i = key + 5;
    while (i < line.size() && isSpace(line[i])) ++i;
    if (i >= line.size() || line[i] != ':') return {};
    ++i;
    while (i < line.size() && isSpace(line[i])) ++i;
    if (i >= line.size() || line[i] != '"') return {};
    ++i;
    std::string name;
    while (i < line.size() && name.size() < kMaxCmdNameChars) {
        const char c = line[i++];
        const bool ident = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
        if (!ident) break;
        name.push_back(c);
    }
    return name;
}

// ── Who holds the pipe's connections ────────────────────────────────────────
// [PIPE-PER-PROCESS-2026-11-06] Stage C, the maintainer's policy (2026-11-10): no client is ever disconnected for being
// idle, but who holds the connections can be seen -- the pipe_clients command, and, when every connection is taken
// (no client can connect to ask), the at-capacity log line, which a client that cannot connect reads from the DLL's
// pipe log.
struct Holder {
    uint64_t    seq = 0;
    DWORD       pid = 0;            // the client process; 0 when it could not be read
    std::string exe;                // its exe file name, UTF-8, at most kMaxHolderExeBytes; "?" when unread
    long long   connectedMs = 0;    // how long ago it connected
    std::string lastCmd;            // its last (or current) command; empty before its first
    long long   lastCmdAgeMs = -1;  // since that command began; -1 before its first
    bool        inFlight = false;   // a command is running now
    std::string phase;              // Fern's Connection::Phase name
    bool        self = false;       // the connection asking (pipe_clients)
};

inline constexpr size_t kMaxHolderExeBytes = 64;
// The holders on one log line stay under this many bytes: Sein formats a line into 4096, and a longer one is cut
// silently. Holders past it are dropped and counted (" more=N").
inline constexpr size_t kHoldersLineBudget = 3000;

// `s` cut to at most `maxBytes` without splitting a UTF-8 sequence.
inline std::string TruncateUtf8(const std::string& s, size_t maxBytes) {
    if (s.size() <= maxBytes) return s;
    size_t cut = maxBytes;
    while (cut > 0 && (static_cast<unsigned char>(s[cut]) & 0xC0) == 0x80) --cut;   // back off continuation bytes
    return s.substr(0, cut);
}

inline nlohmann::json HolderToJson(const Holder& h) {
    nlohmann::json j;
    j["seq"]             = h.seq;
    j["pid"]             = static_cast<uint32_t>(h.pid);
    j["exe"]             = h.exe;
    j["connected_ms"]    = h.connectedMs;
    j["last_cmd"]        = h.lastCmd;
    j["last_cmd_age_ms"] = h.lastCmdAgeMs;
    j["in_flight"]       = h.inFlight;
    j["phase"]           = h.phase;
    j["self"]            = h.self;
    return j;
}

// The holders as one compact JSON array, at most `budget` bytes, then " more=N" when some did not fit. Never throws:
// invalid UTF-8 in a name is replaced, not thrown on -- on the accept thread, a throw would end the server.
inline std::string HoldersJson(const std::vector<Holder>& holders, size_t budget = kHoldersLineBudget) {
    std::string out = "[";
    size_t listed = 0, more = 0;
    for (const auto& h : holders) {
        const std::string item = HolderToJson(h).dump(-1, ' ', false, nlohmann::json::error_handler_t::replace);
        if (out.size() + (listed ? 1 : 0) + item.size() + 1 > budget) { ++more; continue; }
        if (listed) out += ',';
        out += item;
        ++listed;
    }
    out += ']';
    if (more) out += std::string(Grimoire::PIPE_HOLDERS_MORE_TOKEN) + std::to_string(more);
    return out;
}

// ── Who owns what a pipe client turns on ────────────────────────────────────
// [PIPE-PER-PROCESS-2026-11-06] Stage C
//
// The client's PROCESS, not its connection: the UI holds two lanes, and a lane that reconnects while its sibling stays
// must not release what that process still uses. A connection whose pid could not be read owns things alone; the top
// bit keeps that key apart from every pid, which is 32 bits, and from 0, which names no owner.
constexpr uint64_t ClientOwnerKey(DWORD pid, uint64_t seq) noexcept {
    (void)pid; (void)seq;
    return 0;   // RED stub
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
