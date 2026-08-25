// ============================================================
// Heiter — 海塔 (僧侶・旅途起點 — Priest Who Started the Journey)
// dllmain: DLL entry point and auto-start logic
// ============================================================

#include <Windows.h>
#include <atomic>
#define LOG_CAT "INIT"
#include "Sein.h"
#include "BuildStamp.h"
#include "Grimoire.h"
#include "Tot.h"
#include "Voll.h"
#include "Utf8Helpers.h"
#include "Routine.h"   // Routine::RunThreadGuarded — a throw out of a thread proc is std::terminate (B14)

// Global DLL module handle — used by CEPlugin.cpp to resolve the DLL's
// own file path when injecting into the game process.
HMODULE g_hDllModule = nullptr;

// Set to true by CEPlugin_InitializePlugin when this DLL is loaded as a
// CE plugin (in CE's own process). Auto-start thread checks this flag to
// decide whether to self-initialize.
std::atomic<bool> g_isCEPlugin{false};

// Forward declaration — defined in ExportAPI.cpp
extern "C" bool UE5_AutoStart();
// The on-this-thread form. Declared here rather than by including Frieren.h,
// matching how every other export above is reached from this file.
// `extern "C"` is REQUIRED, not decorative: Frieren.h declares it inside the
// same extern "C" block as the exports, so a plain C++ declaration here would
// mangle differently and fail to link.
extern "C" bool UE5_AutoStartBlocking();
extern "C" bool UE5_StartPipeServer();

// Detach-on-destroy per Routine's contract. One handle, file-scope: this path runs once, from
// the single auto-start thread, so there is nothing to serialise against.
static Routine::SafeThread s_autoStartWatcher;
extern "C" void UE5_Shutdown();

// Note: the dxgi proxy resolves the real dxgi exports LAZILY, from its asm
// forwarding thunks (Lugner_Dxgi.asm -> DxgiProxy_EnsureResolved) on the
// first forwarded dxgi call. It is intentionally NOT resolved here in
// DllMain: loading dxgi.dll under the loader lock during the EXE's static-
// import init crashes games that import dxgi.dll before d3d11 (e.g.
// Octopath Traveler). See Lugner_Dxgi.cpp for the full rationale.

// Mailbox — shared memory interface for CE Lua
#include "Mimic.h"

// No handle is kept for the auto-start thread (audit #5 AB8). Nothing ever joins
// it: DLL_PROCESS_DETACH is a deliberate no-op (a join under the loader lock would
// deadlock) and UE5_Shutdown lives in another translation unit that could not reach
// a file-static here anyway. The two comments that used to claim a shutdown join
// were wrong, and the stored HANDLE was leaked for the process lifetime. The thread
// runs detached; its CreateThread handle is closed immediately below.

#ifdef UE5_PROXY_BUILD
// ── Proxy mutual exclusion ─────────────────────────────────────────────────
// When the user has both proxy DLLs (version.dll + dinput8.dll) sitting in
// the game folder, both will be loaded by the OS. Only the first to attach
// runs full init (pipe server, mailbox, AOB scan); the second becomes a
// passive forwarder — its OS-API stubs still work, but it skips all
// UE5CEDumper-side init so we don't get duplicate pipe servers, log files,
// or background threads.
//
// The mutex handle is intentionally never closed — its lifetime spans the
// whole process. The OS reclaims it on process exit.
static HANDLE g_primaryProxyMutex = nullptr;
// False when CreateMutexW itself failed — the guard is then not armed at all and a
// second proxy in this process would run alongside us. Logged once Sein exists. (B47)
static bool   g_primaryProxyMutexArmed = false;
// GetLastError() captured AT the CreateMutexW call (audit #5 AB10/AB11). The B47
// warning below is logged only after Sein::Init + several Win32/CRT calls, each of
// which clobbers the thread's last-error, so a live GetLastError() there names the
// wrong API. Stored here at the point of failure so the warning blames the right one.
static DWORD  g_primaryProxyMutexErr = 0;
#endif

#ifdef UE5_PROXY_BUILD
// ── Proxy DLL auto-start ───────────────────────────────────────────────────
// In proxy mode, we start the pipe server immediately (no scan).
// The user triggers scanning later from the UI when the game has loaded.
// No CE delay needed — proxy DLL is always in the game process.
static void AutoStartBody();

// Guarded entry: AutoStartBody runs the whole AOB scan + pipe start, which allocates
// heavily, and this is a raw CreateThread proc where a throw is std::terminate. (B14)
static DWORD WINAPI AutoStartThreadProc(LPVOID)
{
    Routine::RunThreadGuarded("DllMain AutoStart", [] { AutoStartBody(); });
    return 0;
}

static void AutoStartBody()
{
    // Guard: if this DLL was accidentally loaded by UE5DumpUI.exe (e.g. both
    // files in the same directory), skip all initialization to avoid the UI
    // connecting to its own in-process pipe server.
    {
        wchar_t procName[MAX_PATH] = {};
        GetModuleFileNameW(nullptr, procName, MAX_PATH);
        std::wstring path(procName);
        auto slash = path.find_last_of(L"\\/");
        std::wstring exe = (slash != std::wstring::npos) ? path.substr(slash + 1) : path;
        // Case-insensitive compare
        for (auto& c : exe) c = towlower(c);
        if (exe == L"ue5dumpui.exe") {
            LOG_WARN("DllMain ProxyStart: loaded by UE5DumpUI.exe — skipping proxy init");
            g_invokeMailbox.initState = Mimic::INIT_SKIPPED;
            return;
        }
    }

    LOG_INFO("DllMain ProxyStart: proxy DLL mode — starting pipe server only (no scan)");
    g_invokeMailbox.initState = Mimic::INIT_RUNNING;

    // Brief delay for game to finish early init (avoid pipe creation during process startup)
    Sleep(500);

    bool ok = UE5_StartPipeServer();
    g_invokeMailbox.initState = ok ? Mimic::INIT_READY : Mimic::INIT_FAILED;
    LOG_INFO("DllMain ProxyStart: pipe server %s", ok ? "started" : "FAILED to start");
}
#else
// ── CE / Manual inject auto-start ──────────────────────────────────────────
// Spawned on DLL_PROCESS_ATTACH. Waits 1 second to allow CEPlugin_InitializePlugin
// to run (which sets g_isCEPlugin = true) if CE is loading this as a plugin.
// If g_isCEPlugin is still false after the delay, we're in a game process and
// call UE5_AutoStart() to scan for GObjects/GNames and start the pipe server.
//
// This eliminates the need for CE Lua to call UE5_Init via executeCodeEx,
// which was timing out due to the AOB scan taking longer than CE's remote-
// thread timeout.
static void AutoStartBody();

// Guarded entry: AutoStartBody runs the whole AOB scan + pipe start, which allocates
// heavily, and this is a raw CreateThread proc where a throw is std::terminate. (B14)
static DWORD WINAPI AutoStartThreadProc(LPVOID)
{
    Routine::RunThreadGuarded("DllMain AutoStart", [] { AutoStartBody(); });
    return 0;
}

static void AutoStartBody()
{
    // Log immediately (before any sleep) to confirm thread is running.
    LOG_INFO("DllMain AutoStart: thread started (g_isCEPlugin=%d)", (int)g_isCEPlugin.load());

    // Give CE time to call CEPlugin_InitializePlugin (~200 ms typical).
    Sleep(1000);

    LOG_INFO("DllMain AutoStart: after 1s delay (g_isCEPlugin=%d)", (int)g_isCEPlugin.load());

    if (g_isCEPlugin.load()) {
        LOG_INFO("DllMain AutoStart: CE plugin host — skipping auto-start");
        g_invokeMailbox.initState = Mimic::INIT_SKIPPED;
        return;
    }

    // Belt and braces for the same race (B34). CEPlugin_GetVersion now claims plugin
    // identity, which closes the ordinary window — but "did a human tick a checkbox in
    // under a second" is not a condition worth betting a full AOB scan of the WRONG
    // PROCESS on, and identity is never claimed at all when the DLL is injected into CE
    // by hand rather than loaded as a plugin. Cheat Engine is never a scan target.
    //
    // PREFIX match, not an exact-name list. The first version of this guard listed
    // "cheatengine-x86_64.exe" / "cheatengine-i386.exe" / "Cheat Engine.exe" and was
    // verified against a live capture that named the real executable:
    // **cheatengine-x86_64-SSE4-AVX2.exe** — a build variant none of those three
    // matched, so the DLL scanned CE for 5.8 s and opened \\.\pipe\UE5DumpBfx inside it.
    // CE ships several suffixed variants and can add more; the stable part is the stem.
    {
        wchar_t hostPath[MAX_PATH] = {};
        if (GetModuleFileNameW(nullptr, hostPath, MAX_PATH)) {
            const wchar_t* leaf = wcsrchr(hostPath, L'\\');
            leaf = leaf ? leaf + 1 : hostPath;
            if (IsCheatEngineExeName(leaf)) {
                // ⚠ CONVERT FIRST; NEVER `%ls`. [NONASCIILS-2026-08-24] — and this leaf is
                // NOT ASCII by construction: IsCheatEngineExeName anchors only the first 11
                // characters (`_wcsnicmp(leaf, L"cheatengine", 11)`), leaving the rest free.
                // Users rename CE builds routinely — `cheatengine-x86_64-SSE4-AVX2.exe` is
                // itself the rename this gate was widened for — so `cheatengine-測試.exe`
                // would lose the entire warning explaining why auto-start was skipped.
                const std::string leafU8 = Utf8Helpers::EncodeUtf16(leaf, wcslen(leaf));
                LOG_WARN("DllMain AutoStart: host process is '%s' — Cheat Engine is never "
                         "a scan target; skipping auto-start (CE plugin, not yet enabled, "
                         "or the DLL was injected into CE by hand?)", leafU8.c_str());
                g_invokeMailbox.initState = Mimic::INIT_SKIPPED;
                return;
            }
        }
    }

    // Is another UE5Dumper instance already serving? [RELAUNCHPIPE-2026-08-19]
    //
    // ⚠ This is the SECOND copy of that defect, on the CE / manual-inject path. It used to skip
    // the whole auto-start — init AND pipe server — permanently, on one CreateFileW. On a title
    // that RELAUNCHES ITSELF the holder is the process that is busy dying, so the survivor was
    // left un-scanned and unreachable for the rest of its life. Measured with two DumperTest
    // instances: the second logged `skipping auto-start` and never recovered when the first
    // exited. (The OCTOPATH report hit the sibling copy in Frieren's UE5_StartPipeServer, which
    // the proxy path calls directly — same bug, different entry point.)
    HANDLE testPipe = CreateFileW(
        Grimoire::PIPE_NAME,
        GENERIC_READ, 0, nullptr,
        OPEN_EXISTING, 0, nullptr);

    const bool pipeExists = (testPipe != INVALID_HANDLE_VALUE);
    DWORD holderPid = 0;
    if (pipeExists) {
        ULONG pid = 0;
        if (GetNamedPipeServerProcessId(testPipe, &pid)) holderPid = static_cast<DWORD>(pid);
        CloseHandle(testPipe);
    }

    if (Voll::DecideStart(pipeExists, holderPid, GetCurrentProcessId())
            == Voll::StartAction::DeferAndWatch) {
        LOG_WARN("DllMain AutoStart: pipe is held by PID %lu — deferring auto-start, and watching "
                 "for it to free", static_cast<unsigned long>(holderPid));
        // SKIPPED, not FAILED: a pipe server IS up (owned by the other instance), so a CE Lua
        // poller should proceed to connect rather than report an error. It stays SKIPPED only
        // until the watcher below actually runs the auto-start.
        g_invokeMailbox.initState = Mimic::INIT_SKIPPED;

        // Deliberately NOT "just let the normal path run and let Frieren's watcher handle the
        // pipe". Falling through would make this process pay a full GObjects scan while another
        // instance owns everything — which is the cost this guard exists to avoid. Waiting first
        // and scanning only if the pipe actually frees keeps that saving and still recovers.
        s_autoStartWatcher = std::thread([holderPid]() {
            for (int i = 0; i < Voll::kClaimWatchMaxTries; ++i) {
                if (Tot::ShutdownRequested()) return;
                Sleep(Voll::kClaimWatchPollMs);

                HANDLE probe = CreateFileW(Grimoire::PIPE_NAME, GENERIC_READ, 0, nullptr,
                                           OPEN_EXISTING, 0, nullptr);
                if (probe != INVALID_HANDLE_VALUE) { CloseHandle(probe); continue; }
                // ERROR_PIPE_BUSY means the holder is alive with every instance in use — still
                // theirs. Only "not found" means the pipe is really gone.
                if (GetLastError() == ERROR_PIPE_BUSY) continue;

                LOG_WARN("AutoStartWatcher: pipe freed by PID %lu after %d ms — running the "
                         "auto-start that was deferred",
                         static_cast<unsigned long>(holderPid),
                         (i + 1) * Voll::kClaimWatchPollMs);
                UE5_AutoStartBlocking();
                LOG_INFO("AutoStartWatcher: deferred auto-start returned");
                return;
            }
            LOG_WARN("AutoStartWatcher: PID %lu still holds the pipe after %d s — giving up. "
                     "This process is not initialised and nothing can connect to it.",
                     static_cast<unsigned long>(holderPid),
                     (Voll::kClaimWatchMaxTries * Voll::kClaimWatchPollMs) / 1000);
        });
        return;
    }

    // The BLOCKING form on purpose: this already IS the dedicated auto-start
    // thread, so the exported spawn-and-return entry point would only hand the
    // work to a second thread and leave this one to exit immediately. The
    // asynchrony that AB2 needs is for CE's remote stub, which has a 10 s
    // deadline; nothing here does.
    LOG_INFO("DllMain AutoStart: game process — running auto-start on this thread");
    UE5_AutoStartBlocking();
    LOG_INFO("DllMain AutoStart: auto-start returned");
    return;
}
#endif

// AB9 — the log retention sweep, off the loader lock.
//
// ⚠ THIS MUST LIVE IN THE COMMON REGION, i.e. AFTER the #endif above and outside both
// halves of the UE5_PROXY_BUILD split. Heiter.cpp defines AutoStartThreadProc TWICE —
// once inside `#ifdef UE5_PROXY_BUILD` and once in its `#else` — while the CreateThread
// call site below is common code. Putting this proc beside either copy compiles the DLL
// it happens to match and breaks the other four flavours with an undeclared identifier,
// and NO test target compiles Heiter.cpp, so only a `-Target DLL` build catches it.
//
// RunThreadGuarded for the same reason the auto-start proc uses it: std::filesystem
// throws, and a throw out of a raw thread proc is std::terminate. (B14)
static DWORD WINAPI RetentionSweepThreadProc(LPVOID) {
    Routine::RunThreadGuarded("Sein Retention", [] { Sein::RunRetentionSweep(); });
    return 0;
}
BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID /*reserved*/) {
    switch (reason) {
    case DLL_PROCESS_ATTACH: {
        g_hDllModule = hModule;
        DisableThreadLibraryCalls(hModule);

#ifdef UE5_PROXY_BUILD
        // First-loaded-wins: if another UE5CEDumper proxy DLL is already loaded into
        // THIS process, become a passive forwarder. We MUST do this before Sein::Init()
        // so the second instance doesn't open log files in the same per-process
        // subfolder as the first.
        //
        // Returning TRUE here skips Sein::Init / Mimic::StartThread / the
        // auto-start thread; the only thing this DLL still does is service
        // its OS-API export forwarders (Lugner.cpp / Lugner_Dinput8.cpp).
        //
        // The name is per-PROCESS, and that is the whole point — the comment above has
        // always said so, but the name used to be `Global\…`, which is a different
        // thing in two ways (B47). Creating a `Global\` object needs
        // SeCreateGlobalPrivilege, which a non-elevated game does NOT have: CreateMutexW
        // returned NULL, the guard was skipped entirely, and the same-process dedup it
        // exists for silently never worked. When it DID work (elevated game) it was
        // worse — machine-wide, so a second instrumented game anywhere on the system
        // turned this proxy passive. Local\ + PID is the scope the design intends.
        {
            wchar_t mutexName[64];
            swprintf_s(mutexName, L"Local\\UE5CEDumper_PrimaryProxy_%lu",
                       GetCurrentProcessId());
            g_primaryProxyMutex = CreateMutexW(nullptr, FALSE, mutexName);
            // Capture last-error NOW: everything below (and the deferred B47 warning)
            // clobbers it, so reading it later blames the wrong call. (AB10/AB11)
            const DWORD mutexErr = GetLastError();
            if (g_primaryProxyMutex != nullptr &&
                mutexErr == ERROR_ALREADY_EXISTS) {
                // Cannot log — Sein is deliberately not initialized in passive mode, and
                // initializing it is exactly what this branch must avoid. The first
                // instance records the pairing from its own side instead (below).
                return TRUE;
            }
            // A NULL handle means the guard is not armed at all. Record it once we have
            // a logger, because a silently-unarmed dedup is what made this defect
            // invisible: two proxies in one process would BOTH run, and the only symptom
            // is interleaved logs in one folder.
            g_primaryProxyMutexArmed = (g_primaryProxyMutex != nullptr);
            if (!g_primaryProxyMutexArmed) g_primaryProxyMutexErr = mutexErr;
        }
#endif

        Sein::Init();
        // Decided from the host executable below, and read by the thread-start block that
        // follows. Threads must not be created at all in a Cheat Engine host — see there.
        bool startBackgroundThreads = true;
        {
            // Log which process loaded this DLL — distinguishes CE plugin
            // host (ce.exe) from game process injection in the log file.
            wchar_t procPathW[MAX_PATH] = {};
            GetModuleFileNameW(nullptr, procPathW, MAX_PATH);

            // Extract filename from path for log + mirror
            std::wstring fullPath(procPathW);
            auto lastSlash = fullPath.find_last_of(L"\\/");
            std::wstring fileName = (lastSlash != std::wstring::npos)
                ? fullPath.substr(lastSlash + 1) : fullPath;

            // Same guard AutoStartBody already applies, hoisted here because the decision
            // it drives is "create threads or not" and that has to be made BEFORE the
            // threads exist. Fed the FULL path, which is what the tested helper takes —
            // DllMain itself is unreachable from a test (audit #5 AB1).
            startBackgroundThreads = HostAllowsBackgroundThreads(procPathW);

            // UTF-8 encode the WIDE path already in hand rather than asking Windows for a
            // narrow one. GetModuleFileNameA converts through the ANSI code page, which
            // DESTROYS any character the code page cannot represent: a real install at
            // "...\EVERSPACE™ 2\..." logged as "EVERSPACE? 2", and '?' is not a legal path
            // character — so the logged path could not be opened, existence-checked, or matched
            // back to a folder. The log files are UTF-8, so the wide path round-trips exactly.
            // This line is a data source for the Proxy Deploy orphan scan, which recovers
            // deployment locations from it; a mangled path is an unusable row there.
            std::string procPathU8 = Utf8Helpers::EncodeUtf16(fullPath.c_str(), fullPath.size());
            LOG_INFO("UE5Dumper DLL loaded | build: %s | process: %s [PID=%lu]",
                     BuildStamp::VersionString(), procPathU8.c_str(), GetCurrentProcessId());

            // Initialize per-process mirror log subfolder
            Sein::InitProcessMirror(fileName);

#ifdef UE5_PROXY_BUILD
            // The first moment this can be said out loud. A dedup that is not armed
            // must not look like a dedup that found nothing. (B47)
            if (!g_primaryProxyMutexArmed) {
                LOG_WARN("Proxy: first-loaded-wins guard is NOT armed (CreateMutexW failed, "
                         "err=%lu) — a second UE5CEDumper proxy in this process would run "
                         "alongside this one", g_primaryProxyMutexErr);
            }
#endif
        }
        // ── Threads: never in a Cheat Engine host (audit #5 AB1) ──────────────────
        //
        // CE loads a plugin DLL and then UNLOADS it, repeatedly:
        //   * Settings → Plugins → Add does LoadLibrary → CEPlugin_GetVersion →
        //     FreeLibrary (cheat-engine 7.5, plugin.pas:1497 / :1522 / :1525), so the
        //     refcount hits 0 microseconds after this function returns;
        //   * every CE exit does FormClose → pluginhandler.free → UnloadPlugin →
        //     FreeLibrary (plugin.pas:1417, MainUnit.pas:7832) — and that runs BEFORE CE
        //     writes its settings, so a crash here also loses the user's CE config.
        // docs/ce-plugin-api-reference.md already records the load/free cycle.
        //
        // Mimic's poller returns from Sleep(1) into our code at least every millisecond.
        // If the image is unmapped underneath it that is an access violation on a thread
        // with no handler — i.e. WE crash Cheat Engine, the user's main tool.
        //
        // AutoStartBody has always refused to do anything in a CE host, but the refusal
        // lived INSIDE the thread it should have prevented, and the mailbox poller had no
        // such check at all. DLL_PROCESS_DETACH cannot rescue this: its `reserved`
        // parameter is unnamed, so it cannot tell a FreeLibrary unload from process exit,
        // and joining threads from DETACH would deadlock under the loader lock anyway.
        if (!startBackgroundThreads) {
            LOG_WARN("DllMain: host is Cheat Engine — NOT starting the mailbox poller or "
                     "the auto-start thread. CE FreeLibrary's plugin DLLs (on Settings→Add "
                     "and on exit), and a thread left running in an unmapped image takes CE "
                     "down with it. The CE-plugin entry points still work; they inject into "
                     "the GAME, which is where the poller belongs.");
            g_invokeMailbox.initState = Mimic::INIT_SKIPPED;

            // Retention runs INLINE here, and that is deliberate, not an oversight.
            // AB1: in a CE host the module is NOT pinned (see the PIN below, which is in
            // the other branch for exactly this reason), so creating ANY thread here
            // risks CE's FreeLibrary unmapping the image out from under it. A ~140 ms
            // stall inside a plugin-list refresh costs nothing.
            // ⚠ And it must not simply be SKIPPED: PruneAgedLogs only ever prunes the
            // CURRENT process's folder, so skipping it here leaves CE's own log folder
            // growing forever — no other process would ever prune it, and only
            // PruneStaleProcessFolders (which needs a whole idle folder) could.
            Sein::RunRetentionSweep();
        } else {
            // Our threads live in this image, so the image must not become unmappable
            // while they run. Nothing in this repo calls FreeLibrary on us — but "nothing
            // in this repo" is exactly the assumption AB1 falsified, and pinning costs one
            // call. Deliberately NOT done in the CE branch above: with no threads running
            // there is nothing to protect, and CE should stay free to unload us cleanly.
            HMODULE pinned = nullptr;
            if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_PIN |
                                    GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
                                    reinterpret_cast<LPCWSTR>(&DllMain), &pinned)) {
                LOG_WARN("DllMain: module PIN failed (err=%lu) — a FreeLibrary of this DLL "
                         "while the poller runs would unmap code that is executing",
                         GetLastError());
            }

            // Start mailbox polling thread (CE Lua shared memory interface).
            // Runs in both proxy and inject modes — handles auto-init on first command.
            Mimic::StartThread();

            // Spawn auto-start thread. It will self-terminate if g_isCEPlugin
            // is set true by CEPlugin_InitializePlugin within 1 second. Nothing
            // joins it (see the note by the removed g_hAutoStartThread), so close
            // our handle at once and let it run detached rather than leak it. (AB8)
            HANDLE hAutoStart = CreateThread(nullptr, 0, AutoStartThreadProc, nullptr, 0, nullptr);
            if (hAutoStart) {
                LOG_INFO("DllMain: auto-start thread created OK");
                CloseHandle(hAutoStart);
            } else {
                LOG_ERROR("DllMain: CreateThread failed (error=%lu)", GetLastError());
            }

            // AB9 — retention on its own thread, so the sweep is off the loader lock.
            //
            // ⚠ UNCONDITIONAL within this branch, i.e. OUTSIDE the `if (hAutoStart)`
            // above. Putting it after the CloseHandle inside that `if` would run
            // retention only while auto-start thread creation is SUCCEEDING and stop it
            // silently when that starts failing — a feature switched off by an unrelated
            // failure, which is the B19 shape.
            //
            // The module PIN a few lines above has already run, so this thread cannot be
            // left executing in an unmapped image. A thread created during
            // DLL_PROCESS_ATTACH does not begin until the loader lock is released, which
            // is precisely the property being bought here.
            HANDLE hSweep = CreateThread(nullptr, 0, RetentionSweepThreadProc, nullptr, 0, nullptr);
            if (hSweep) {
                CloseHandle(hSweep);   // detached, like the auto-start thread (AB8)
            } else {
                LOG_WARN("DllMain: retention sweep thread failed (err=%lu) — running it inline",
                         GetLastError());
                Sein::RunRetentionSweep();
            }
        }
        break;
    }

    case DLL_PROCESS_DETACH:
        // Audit findings #1, #2, #14: heavy work in DLL_PROCESS_DETACH
        // (thread joins, MinHook teardown, FreeLibrary, log file flushes)
        // risks loader-lock deadlocks because joined threads or unhooked
        // code may take the loader lock that DllMain already holds.
        //
        // The OS terminates all threads and reclaims handles, file
        // descriptors, loaded modules, and memory automatically when the
        // process exits — that's the right place for this teardown.
        //
        // Active shutdowns still go through the full UE5_Shutdown path:
        //   - CE Lua "Disable" → ue5_callDLL("UE5_Shutdown")
        //   - Future hooks for proxy-mode graceful exit
        // Only the implicit process-exit DETACH is a no-op.
        break;

    default:
        break;
    }
    return TRUE;
}
