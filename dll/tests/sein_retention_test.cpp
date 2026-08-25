// sein_retention_test.cpp
// UE5CEDumper — AB9: the log retention sweep, moved off the DllMain loader lock.
//
// WHAT THIS COVERS
//   Sein::RunRetentionSweep and the two sweeps it drives (PruneAgedLogs,
//   PruneStaleProcessFolders). Those two had NO tests of any kind — no test target
//   compiled Sein.cpp — while between them they hold the only `remove_all` in the DLL
//   and decide what gets deleted from the user's log tree on every process start.
//
// ⚠⚠ THE SAFETY GATE IS NOT OPTIONAL AND MUST NOT BE "SIMPLIFIED"
//   This test drives code whose job is recursive deletion, and it does it by pointing
//   Sein's file-static paths at a fixture. A fixture bug that left those pointing at
//   %LOCALAPPDATA%\UE5CEDumper\Logs would delete the real log corpus — 3,000+ files
//   across 32 process folders, which is the evidence base for several closed
//   verification rows and is not regenerable. So EVERY case routes through Fixture,
//   which refuses to run unless its root is: non-empty, under %TEMP%, not the live log
//   dir, and carrying a sentinel file this test itself wrote. AssertSafe() is called
//   again immediately before the sweep, not just at construction.
//
// WHY IT #includes THE .cpp
//   Sein::s_logDir / Sein::s_processDir / Sein::s_processDirReady / Sein::s_sweepDone are file statics with no
//   accessor, deliberately. Including the TU is the only way to point them at a fixture
//   without adding a test-only hook to shipping code. Same pattern as
//   grausam_window_test.
//
// ⚠ THE LATCH RESET IS LOad-BEARING
//   Sein::s_sweepDone is a process-global one-shot, so the SECOND case to call
//   RunRetentionSweep would no-op and pass vacuously — including under the negative
//   controls the cases exist to catch. Reset() clears it before every case. Because the
//   TU includes Sein.cpp the static is directly reachable; no shipping-code hook needed.

#include <windows.h>
#include <stdio.h>
#include <string>

namespace BuildStamp { const char* VersionString(); }

#include "../src/Sein.cpp"  // NOLINT — see "WHY IT #includes THE .cpp"

namespace BuildStamp { const char* VersionString() { return "sein_retention_test"; } }

// ── harness ──────────────────────────────────────────────────────────

static int g_pass = 0, g_fail = 0;

// Print each case as it STARTS, house style (freeze_helper_test does the same). Not
// decoration: this test drives recursive deletion and can crash, and a crash with no
// output cannot be located -- build.ps1:305 records that exact shape costing a CI
// investigation in dll_helpers_test.
static void blk(const char* name) { printf("- %s\n", name); }

static void check(const char* label, bool cond, const char* got = nullptr) {
    if (cond) { ++g_pass; }
    else { ++g_fail; printf("  FAIL  %s%s%s\n", label, got ? "   got: " : "", got ? got : ""); }
}

static fs::path TempRoot() {
    wchar_t buf[MAX_PATH]{};
    ::GetTempPathW(MAX_PATH, buf);
    return fs::path(buf) / (L"ue5-sein-test-" + std::to_wstring(::GetCurrentProcessId()));
}

static void Backdate(const fs::path& p, int days) {
    HANDLE h = ::CreateFileW(p.c_str(), FILE_WRITE_ATTRIBUTES,
                             FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                             nullptr, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, nullptr);
    if (h == INVALID_HANDLE_VALUE) return;
    FILETIME now{};
    ::GetSystemTimeAsFileTime(&now);
    ULARGE_INTEGER u{};
    u.LowPart = now.dwLowDateTime; u.HighPart = now.dwHighDateTime;
    u.QuadPart -= static_cast<ULONGLONG>(days) * 24ull * 60 * 60 * 10'000'000ull;
    FILETIME ft{};
    ft.dwLowDateTime = u.LowPart; ft.dwHighDateTime = u.HighPart;
    // ftLastWriteTime is the field FileWriteTime reads (Sein.cpp), so it is the one the
    // sweep judges by. Setting only creation/access would age nothing.
    ::SetFileTime(h, nullptr, nullptr, &ft);
    ::CloseHandle(h);
}

static void Write(const fs::path& p, const char* text = "x") {
    fs::create_directories(p.parent_path());
    FILE* f = nullptr;
    if (_wfopen_s(&f, p.c_str(), L"wb") == 0 && f) { fputs(text, f); fclose(f); }
}

// ── the fixture, and the gate that keeps it from eating the real corpus ──

struct Fixture {
    fs::path root;
    fs::path proc;

    Fixture() {
        root = TempRoot();
        std::error_code ec;
        fs::remove_all(root, ec);              // safe: TempRoot() is pid-scoped under %TEMP%
        fs::create_directories(root, ec);
        Write(root / "SENTINEL.ue5test", "sein_retention_test fixture");
        proc = root / "TestProc";
        fs::create_directories(proc, ec);

        // Point Sein at the fixture.
        Sein::s_logDir           = root;
        Sein::s_processDir       = proc;
        Sein::s_processDirReady  = true;
        Sein::s_sweepDone.store(false);   // the latch reset — see the header note
    }

    ~Fixture() {
        std::error_code ec;
        fs::remove_all(root, ec);
        Sein::s_logDir.clear(); Sein::s_processDir.clear(); Sein::s_processDirReady = false;
    }

    // Called at construction AND again immediately before every sweep. Four independent
    // conditions; any one failing aborts the process rather than deleting something.
    void AssertSafe(const char* where) const {
        const std::wstring r = Sein::s_logDir.wstring();
        bool ok = !r.empty();
        if (ok) {
            wchar_t tmp[MAX_PATH]{}; ::GetTempPathW(MAX_PATH, tmp);
            ok = r.rfind(std::wstring(tmp), 0) == 0;                       // under %TEMP%
        }
        if (ok) ok = r.find(L"ue5-sein-test-") != std::wstring::npos;      // our name
        if (ok) ok = fs::exists(Sein::s_logDir / "SENTINEL.ue5test");            // our sentinel
        if (ok) ok = r.find(L"LOCALAPPDATA") == std::wstring::npos
                  && r.find(L"AppData\\Local\\UE5CEDumper") == std::wstring::npos;
        if (!ok) {
            printf("\n*** ABORT (%s): fixture root is not a safe scratch dir. "
                   "Refusing to run a recursive delete. ***\n", where);
            fflush(stdout);
            ::TerminateProcess(::GetCurrentProcess(), 2);
        }
    }

    void Sweep() {
        AssertSafe("pre-sweep");
        Sein::s_sweepDone.store(false);
        Sein::RunRetentionSweep();
    }
};

static bool Exists(const fs::path& p) { std::error_code ec; return fs::exists(p, ec); }

// ── cases ────────────────────────────────────────────────────────────

int main() {
    setvbuf(stdout, nullptr, _IONBF, 0);
    printf("sein_retention_test — AB9, the retention sweep off the loader lock\n");

    const int keep = Grimoire::LOG_RETENTION_DAYS;   // 21 at time of writing; derived, not typed
    printf("  (retention window: %d days)\n", keep);

    {   // ---- PruneAgedLogs: what ages out of OUR OWN process folder ----
        Fixture fx;
        blk("PruneAgedLogs: own-folder archives");
        auto old_ = fx.proc / "init-20260101-000000.log";
        auto new_ = fx.proc / "init-20260820-000000.log";
        auto note = fx.proc / "notes.txt";
        auto live = fx.proc / "scan-0.log";
        Write(old_); Write(new_); Write(note); Write(live);
        Backdate(old_, keep + 1);
        Backdate(new_, keep - 1);
        Backdate(note, 100);
        // live keeps its current mtime

        fx.Sweep();

        check("T1 an archive past the window is removed",      !Exists(old_));
        check("T2 an archive inside the window is kept",        Exists(new_));
        check("T3 a non-.log file is kept however old",         Exists(note));
        check("T4 the live -0.log is kept",                     Exists(live));
    }

    {   // ---- PruneStaleProcessFolders: whole folders, and the guard on our own ----
        Fixture fx;
        blk("PruneStaleProcessFolders: whole folders + own-folder guard");
        // ⚠ OUR OWN FOLDER MUST BE MADE A GENUINE CANDIDATE FOR REMOVAL, or the guard
        // under test is never reached. PruneStaleProcessFolders judges a folder by the
        // NEWEST FILE INSIDE it, not by the folder's own mtime — so a current init-0.log
        // here keeps the folder "fresh" and it survives whether the guard exists or not.
        // Measured: with a fresh file, deleting the fs::equivalent guard did not fail this
        // case at all. So put ONE aged archive in and nothing else: PruneAgedLogs deletes
        // it, PruneStaleProcessFolders then sees an EMPTY folder — which it removes — and
        // the only thing that can save ours is the guard.
        auto ourOld = fx.proc / "init-20260101-000000.log";
        Write(ourOld);
        Backdate(ourOld, keep + 1);
        Backdate(fx.proc, keep + 1);

        auto stale = fx.root / "StaleGame";
        auto fresh = fx.root / "FreshGame";
        auto empty = fx.root / "EmptyGame";
        Write(stale / "init-0.log");
        Write(fresh / "init-0.log");
        std::error_code ec; fs::create_directories(empty, ec);
        Backdate(stale / "init-0.log", keep + 1);
        Backdate(stale, keep + 1);

        // ⚠ The case that matters most: age OUR OWN folder past the window. The sweep
        // must keep it anyway — otherwise it deletes the folder it is logging into.
        Backdate(fx.proc, 100);

        fx.Sweep();

        check("T5 a stale sibling folder is removed",           !Exists(stale));
        check("T6 a fresh sibling folder is kept",               Exists(fresh));
        check("T7 an EMPTY sibling folder is removed",          !Exists(empty));
        check("T8 our OWN folder is kept even when backdated",   Exists(fx.proc));
        check("T8b ...even after PruneAgedLogs emptied it",      Exists(fx.proc)
                                                                 && !Exists(ourOld));
    }

    {   // ---- what THIS change added: the readiness guard ----
        Fixture fx;
        blk("readiness guard (this change)");
        auto stale = fx.root / "StaleGame";
        Write(stale / "init-0.log");
        Backdate(stale / "init-0.log", keep + 1);
        Backdate(stale, keep + 1);

        // The state after InitProcessMirror bailed on create_directories: Sein::s_processDir is
        // NON-EMPTY (it is assigned before the create) but the folder was never made. A
        // guard written as `if (procDir.empty()) return;` would sweep here — and
        // PruneStaleProcessFolders' own-folder check is fs::equivalent against a path that
        // does not exist, which is inert.
        Sein::s_processDirReady = false;
        Sein::s_processDir = fx.root / "NeverCreated";
        Sein::s_sweepDone.store(false);
        fx.AssertSafe("readiness-guard");
        Sein::RunRetentionSweep();

        check("T9 not-ready -> the sweep does nothing at all", Exists(stale));
    }

    {   // ---- and the latch ----
        Fixture fx;
        blk("the one-shot latch");
        auto a = fx.proc / "init-20260101-000000.log";
        Write(a); Backdate(a, keep + 1);
        fx.Sweep();
        check("T10 first call sweeps", !Exists(a));

        auto b = fx.proc / "scan-20260101-000000.log";
        Write(b); Backdate(b, keep + 1);
        Sein::RunRetentionSweep();            // NOT via fx.Sweep() — the latch stays set
        check("T11 the latch makes a second call a no-op", Exists(b));
    }

    {   // ---- ⭐ THE POINT OF THE WHOLE CHANGE: the sweep does not hold the log mutex ----
        //
        // Before AB9 the sweep ran inside InitProcessMirror, which holds Sein::s_mutex for its
        // whole body — so moving it to a thread WITHOUT releasing the lock would have
        // moved the stall from the loader lock onto every logging thread rather than
        // removing it. This probes the mutex while a sweep runs over a fixture big enough
        // to take real time.
        Fixture fx;
        blk("the sweep must NOT hold the log mutex");
        for (int i = 0; i < 400; ++i) {
            auto f = fx.root / ("Game" + std::to_string(i)) / "init-0.log";
            Write(f);
            Backdate(f, keep + 1);
            Backdate(fx.root / ("Game" + std::to_string(i)), keep + 1);
        }

        std::atomic<bool> running{true};
        std::atomic<int>  acquired{0};
        std::thread prober([&] {
            while (running.load()) {
                if (Sein::s_mutex.try_lock()) { Sein::s_mutex.unlock(); acquired.fetch_add(1); }
                ::Sleep(0);
            }
        });

        fx.Sweep();
        running.store(false);
        prober.join();

        check("T12 the log mutex was acquirable DURING the sweep", acquired.load() > 0,
              std::to_string(acquired.load()).c_str());
        check("T12b and the sweep still did its work", !Exists(fx.root / "Game0"));
    }

    printf("\n%d checks, %d failure(s)\n", g_pass + g_fail, g_fail);
    return g_fail == 0 ? 0 : 1;
}
