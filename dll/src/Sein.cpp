// ============================================================
// Sein — 贊恩 (僧侶・記錄者 — Priest, Chronicler)
// Logger: 5-category per-process file logging with rotation
// ============================================================

#include "Sein.h"
#include "Grimoire.h"
#include "BuildStamp.h"

#include <Windows.h>
#include <ShlObj.h>
#include <cstdio>
#include <share.h>       // _SH_DENYWR -- the log must stay READABLE while we hold it open
#include <cstdarg>
#include <cstring>
#include <cerrno>
#include <mutex>
#include <atomic>
#include <filesystem>
#include <chrono>
#include <iomanip>
#include <sstream>
#include <vector>
#include <utility>

namespace fs = std::filesystem;

namespace Sein {

// ================================================================
// Log file categories
// ================================================================

enum LogFile : uint8_t {
    LF_Init = 0,   // init.log    — INIT, CEP, SUMMARY
    LF_Scan,        // scan.log    — SCAN:*, MEM
    LF_Offsets,     // offsets.log — DYNO:*, OARR, FNAM
    LF_Pipe,        // pipe.log    — PIPE:*
    LF_Walk,        // walk.log    — WALK:*
    LF_COUNT
};

// Both spellings on one line each so they cannot drift: the wide form names the
// file, the narrow one appears in the "this category could not open" notice, which
// is written with snprintf into a file that DID open.
struct LogFileName { const wchar_t* w; const char* a; };
static const LogFileName s_fileNames[LF_COUNT] = {
    { L"init",    "init"    },
    { L"scan",    "scan"    },
    { L"offsets", "offsets" },
    { L"pipe",    "pipe"    },
    { L"walk",    "walk"    },
};

// ================================================================
// Category → file routing (prefix-match, longest first)
// ================================================================

struct CatMapping {
    const char* prefix;
    uint8_t     prefixLen;
    LogFile     file;
};

// Sorted longest-prefix-first for correct matching
static const CatMapping s_catMap[] = {
    { "WALK:StructP",  12, LF_Walk    },
    { "WALK:ArrayP",   11, LF_Walk    },
    { "PIPE:world",    10, LF_Pipe    },
    { "PIPE:watch",    10, LF_Pipe    },
    { "DYNO:Enum",      9, LF_Offsets },
    { "SCAN:GObj",      8, LF_Scan    },
    { "SCAN:GNam",      8, LF_Scan    },
    { "SCAN:GWld",      8, LF_Scan    },
    { "SCAN:Ver",       8, LF_Scan    },
    { "PIPE:svr",       8, LF_Pipe    },
    { "PIPE:cmd",       8, LF_Pipe    },
    { "SUMMARY",        7, LF_Init    },
    { "INIT",           4, LF_Init    },
    { "SCAN",           4, LF_Scan    },
    { "DYNO",           4, LF_Offsets },
    { "OARR",           4, LF_Offsets },
    { "FNAM",           4, LF_Offsets },
    { "WALK",           4, LF_Walk    },
    { "FLY",            3, LF_Walk    },   // Dunste fly worker (movement-related)
    { "PIPE",           4, LF_Pipe    },
    { "CEP",            3, LF_Init    },
    { "MEM",            3, LF_Scan    },
};

static LogFile ResolveFile(const char* cat) {
    if (!cat || cat[0] == '\0') return LF_Init;
    for (const auto& m : s_catMap) {
        if (strncmp(cat, m.prefix, m.prefixLen) == 0) return m.file;
    }
    return LF_Init;  // fallback: unknown categories go to init.log
}

// ================================================================
// Per-file state
// ================================================================

struct LogFileState {
    FILE*          file    = nullptr;
    size_t         written = 0;
    fs::path       currentPath;
    std::wstring   baseName;
    const char*    baseNameA = "?";   // narrow twin, for the failure notices
};

static std::mutex     s_mutex;
static LogFileState   s_files[LF_COUNT];
static bool           s_filesOpen   = false;
static fs::path       s_logDir;                 // base: %LOCALAPPDATA%\UE5CEDumper\Logs
static fs::path       s_processDir;             // per-process subfolder
// TRUE only once s_processDir has actually been CREATED. Not derivable from
// `s_processDir.empty()`: it is assigned at :572 BEFORE create_directories at :574,
// so on the create-failure bail it is non-empty and names a directory that does not
// exist — and PruneStaleProcessFolders' own-folder guard is `fs::equivalent(entry,
// keep, ec)`, which is INERT against a nonexistent `keep`. Sweeping then could
// delete the folder we are about to log into. (AB9)
static bool           s_processDirReady = false;

// Early buffering: lines logged before InitProcessMirror opens files
static std::vector<std::pair<LogFile, std::string>> s_earlyBuffer;
static bool           s_buffering   = true;
static constexpr size_t EARLY_BUFFER_MAX = 100;

// ================================================================
// Helpers
// ================================================================

static fs::path GetLogDirectory() {
    wchar_t* appdata = nullptr;
    if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &appdata))) {
        fs::path dir = fs::path(appdata) / Grimoire::LOG_FOLDER_NAME / Grimoire::LOG_SUBFOLDER;
        CoTaskMemFree(appdata);
        return dir;
    }
    return fs::path(L".");
}

static std::string GetTimestamp() {
    // Use the Win32 GetLocalTime() API, NOT the CRT localtime_s/std::put_time
    // path. The CRT path triggers __tzset() + locale-facet init on first use,
    // which dereferences not-yet-ready CRT/process global state and AVs when
    // this DLL is loaded EARLY in a host EXE's static-import graph (its DllMain
    // runs before the process is "warm"). Concrete failure: hijacking dxgi.dll
    // on Octopath Traveler — dxgi is imported before d3d11, so our DllMain's
    // very first log line faulted inside __tzset (write to null+0x24). The
    // version.dll/dinput8.dll proxies never hit it only because they load late.
    // GetLocalTime reads the system clock + timezone via ntdll with zero CRT
    // global dependency, so it is safe at any load time.
    SYSTEMTIME st;
    GetLocalTime(&st);

    char buf[32];
    snprintf(buf, sizeof(buf), "%04u-%02u-%02u %02u:%02u:%02u.%03u",
             st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute,
             st.wSecond, st.wMilliseconds);
    return std::string(buf);
}

// ================================================================
// Retention — archive by timestamp, then prune by age
// ================================================================
//
// The scheme here is:
//
//   <base>-0.log                 ALWAYS the current run. Readers, docs and the UI
//                                depend on this, so it is deliberately unchanged.
//   <base>-YYYYMMDD-HHMMSS.log   an archived earlier run
//   anything older than Grimoire::LOG_RETENTION_DAYS is deleted at startup
//
// The archive name is stamped from the file's OWN last-write time, never from
// "now". That is what makes the age prune honest — a log written a month ago but
// archived today still prunes on its real date. Stamping with the archive time
// would silently resurrect stale data for another full retention window.
//
// Why not the old generation shuffle: it ran on EVERY process start, not only on
// size, so N launches of one game in an afternoon evicted everything before them
// no matter how recent. An age policy cannot be expressed as a file count.

// Enumerate `dir` WITHOUT the throwing increment.
//
// A range-for over `fs::directory_iterator` advances with `operator++()`, which is
// the THROWING overload. `directory_iterator(p, ec)` only reports CONSTRUCTION
// failures — and on construction failure it compares equal to end(), so the loop
// body never runs and any `if (ec) break;` written INSIDE it is dead code. A
// mid-enumeration failure (a file vanishing under the cursor, an ACL change,
// FindNextFileW failing) therefore threw a `filesystem_error` out of a sweep that
// believed itself noexcept. This file contains no `catch` at all and the DLL builds
// with /EHa, so that unwound out of `InitProcessMirror` into DLL_PROCESS_ATTACH.
// (audit #5 SE2)
//
// Returns false when enumeration failed at ANY point — construction OR increment.
// Callers must not read that as "the directory is empty": the two are different
// facts, and one of them is a reason to delete things.
template <typename Fn>
static bool ForEachDirEntry(const fs::path& dir, Fn&& fn) {
    std::error_code ec;
    fs::directory_iterator it(dir, ec);
    if (ec) return false;

    const fs::directory_iterator last{};
    while (it != last) {
        fn(*it);
        it.increment(ec);
        if (ec) return false;
    }
    return true;
}

static bool FileWriteTime(const fs::path& p, FILETIME& out) {
    WIN32_FILE_ATTRIBUTE_DATA fad{};
    if (!GetFileAttributesExW(p.c_str(), GetFileExInfoStandard, &fad)) return false;
    out = fad.ftLastWriteTime;
    return true;
}

static ULONGLONG AsU64(const FILETIME& ft) {
    ULARGE_INTEGER u;
    u.LowPart  = ft.dwLowDateTime;
    u.HighPart = ft.dwHighDateTime;
    return u.QuadPart;
}

// FILETIME ticks are 100 ns, so one day is 24*60*60*10^7.
static constexpr ULONGLONG kTicksPerDay = 864000000000ULL;

static ULONGLONG RetentionTicks() {
    return static_cast<ULONGLONG>(Grimoire::LOG_RETENTION_DAYS) * kTicksPerDay;
}

// "YYYYMMDD-HHMMSS" in LOCAL time. Uses the Win32 conversion rather than the CRT
// for the same reason GetTimestamp() does — see the comment there.
static std::wstring StampFor(const FILETIME& ft) {
    FILETIME local{};
    SYSTEMTIME st{};
    if (!FileTimeToLocalFileTime(&ft, &local)) return std::wstring();
    if (!FileTimeToSystemTime(&local, &st))    return std::wstring();
    wchar_t buf[32];
    swprintf(buf, 32, L"%04u%02u%02u-%02u%02u%02u",
             st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond);
    return std::wstring(buf);
}

// Rename `src` to <base>-<its own mtime>.log. On any failure the file is removed
// rather than left behind: an un-archivable log would otherwise be invisible to
// the age prune (which keys on the dated name's mtime) and accumulate forever.
static void ArchiveByWriteTime(const fs::path& src, const fs::path& dir,
                               const wchar_t* baseName) {
    std::error_code ec;
    if (!fs::exists(src, ec)) return;

    FILETIME ft{};
    std::wstring stamp;
    if (FileWriteTime(src, ft)) stamp = StampFor(ft);
    if (stamp.empty()) { fs::remove(src, ec); return; }

    // Two rotations inside the same second are possible on a busy category
    // (8 MB fills fast during a scan), so disambiguate rather than overwrite.
    for (int dup = 0; dup < 100; ++dup) {
        std::wstring name = std::wstring(baseName) + L"-" + stamp;
        if (dup > 0) name += L"-" + std::to_wstring(dup);
        name += L".log";
        auto dst = dir / name;
        if (!fs::exists(dst, ec)) {
            fs::rename(src, dst, ec);
            if (!ec) return;
            break;                 // rename failed (locked?) — fall through to remove
        }
    }
    fs::remove(src, ec);
}

// Delete archived logs past the retention window. Only touches *.log, and never
// the live -0.log (callers archive that first, so it does not exist here).
// One error_code for ITERATION, a fresh one per filesystem call. Sharing a single ec
// let a failed fs::remove poison the loop's own `if (ec) break`: the first undeletable
// entry ended the sweep — and because enumeration order is stable, it ended it at the
// SAME entry on every launch, so the advertised 21-day retention silently stopped
// applying past that point forever. One locked file is enough to do that. (B19)
static void PruneAgedLogs(const fs::path& dir) {
    FILETIME nowFt{};
    GetSystemTimeAsFileTime(&nowFt);
    const ULONGLONG now    = AsU64(nowFt);
    const ULONGLONG maxAge = RetentionTicks();

    // Enumeration failure (either end) just ends the sweep early — same outcome the
    // old `if (iterEc) break;` intended, minus the throw. Nothing is deleted on the
    // strength of a partial read here because each decision is per-file.
    ForEachDirEntry(dir, [&](const fs::directory_entry& entry) {
        std::error_code ec;
        if (!entry.is_regular_file(ec)) return;
        if (entry.path().extension() != L".log") return;

        FILETIME ft{};
        if (!FileWriteTime(entry.path(), ft)) return;
        const ULONGLONG t = AsU64(ft);
        // A failure here is per-file and must not stop the sweep.
        if (now > t && (now - t) > maxAge) fs::remove(entry.path(), ec);
    });
}

// One-time migration of the pre-retention numbered generations (-1 .. -9).
// Without this they orphan: nothing rotates them any more, and the age prune
// would only reach them once they aged out on their own.
static void MigrateLegacyGenerations(const fs::path& dir, const wchar_t* baseName) {
    std::error_code ec;
    for (int i = 1; i <= 9; ++i) {
        auto legacy = dir / (std::wstring(baseName) + L"-" + std::to_wstring(i) + L".log");
        if (fs::exists(legacy, ec)) ArchiveByWriteTime(legacy, dir, baseName);
    }
}

// Append one already-formatted line to the first category whose file is open,
// bypassing WriteToFile (and therefore RotateIfNeeded) so a failure notice can
// never re-enter rotation. Used only for the "a category is dead" notices below.
// LF_Init is index 0, so it is preferred — matching ResolveFile's own fallback.
static void EmergencyNote(const char* line) {
    for (int i = 0; i < LF_COUNT; ++i) {
        if (!s_files[i].file) continue;
        int n = fprintf(s_files[i].file, "%s\n", line);
        if (n > 0) s_files[i].written += static_cast<size_t>(n);
        fflush(s_files[i].file);
        return;
    }
}

static void RotateIfNeeded(LogFileState& fs_state) {
    if (fs_state.written < Grimoire::LOG_MAX_SIZE) return;

    fflush(fs_state.file);
    fclose(fs_state.file);
    fs_state.file = nullptr;

    ArchiveByWriteTime(fs_state.currentPath, fs_state.currentPath.parent_path(),
                       fs_state.baseName.c_str());

    // ⛔ _wfsopen, NOT _wfopen_s. `fopen_s`/`_wfopen_s` open with EXCLUSIVE (deny
    // read/write) access -- documented CRT behaviour, and the one difference from `_wfopen`
    // that matters here. Build 3370 shipped the _s form and every live `<cat>-0.log` then
    // answered ERROR_SHARING_VIOLATION (32) to EVERY reader: no tail, no rig, no operator,
    // for as long as the game ran. That silently broke the capture method that
    // docs/verification-register.md and docs/log-verification-checklist.md are built on,
    // and it is invisible from the UI. Measured on EVERSPACE 2 2026-09-05: an archived
    // sibling in the same folder read fine at the same instant, the live one did not.
    // _wfsopen takes the share mode explicitly (_SH_DENYWR: we stay the only writer,
    // anyone may read) and, being an underscore-prefixed CRT extension rather than a
    // deprecated ISO name, still raises no C4996 in the test target -- so 70d28548's
    // zero-warning goal is kept. errno is read only on the failure path and immediately,
    // so the clobber window the sibling call site documents cannot open either.
    fs_state.file = _wfsopen(fs_state.currentPath.c_str(), L"w", _SH_DENYWR);
    const errno_t reopenErr = fs_state.file ? 0 : errno;
    fs_state.written = 0;

    if (fs_state.file) {
        auto ts = GetTimestamp();
        int n = fprintf(fs_state.file, "[%s] [INFO] [INIT] Log rotated | build: %s\n",
                        ts.c_str(), BuildStamp::VersionString());
        if (n > 0) fs_state.written += static_cast<size_t>(n);
        fflush(fs_state.file);
    } else {
        // The truncating reopen failed (disk full, a viewer holding the file). The
        // category is dead for the rest of the process: WriteToFile's leading null
        // check means RotateIfNeeded is never reached again, so `written = 0` above
        // is not what strands it — the handle is. WriteLog now reroutes this
        // category's lines to a file that IS open; say so once, or a later grep
        // reads the silence as "that code path never ran". (audit #5 SE1)
        auto ts = GetTimestamp();
        char line[384];
        snprintf(line, sizeof(line),
                 "[%s] [ERROR] [INIT] Logger: category '%s' could not be reopened after "
                 "rotation (errno=%d) — its lines are rerouted here for the rest of this run",
                 ts.c_str(), fs_state.baseNameA, reopenErr);
        EmergencyNote(line);
    }
}

// ================================================================
// Init / open / close files
// ================================================================

// Returns false when the category's -0.log could not be opened. THE CALLER MUST
// HONOUR IT: InitProcessMirror used to discard this bool and set s_filesOpen
// regardless. (audit #5 SE1)
static bool OpenFileInDir(LogFileState& fs_state, const fs::path& dir,
                          const LogFileName& name, int* outErr) {
    const wchar_t* baseName = name.w;
    fs_state.baseName    = baseName;
    fs_state.baseNameA   = name.a;
    fs_state.currentPath = dir / (std::wstring(baseName) + L"-0.log");

    MigrateLegacyGenerations(dir, baseName);
    ArchiveByWriteTime(fs_state.currentPath, dir, baseName);   // last run -> dated archive

    // ⛔ _wfsopen, NOT _wfopen_s -- see RotateIfNeeded above: the _s form opens EXCLUSIVELY
    // and makes the live log unreadable to every other process for the life of the game.
    // _SH_DENYWR keeps us the sole writer while leaving the file readable. errno is
    // captured on the spot, so the clobber hazard the old comment worried about (the
    // failure notice is written four more open attempts later) still cannot arise.
    fs_state.file = _wfsopen(fs_state.currentPath.c_str(), L"w", _SH_DENYWR);
    const errno_t openErr = fs_state.file ? 0 : errno;
    if (!fs_state.file) {
        if (outErr) *outErr = (openErr != 0) ? openErr : EACCES;
        return false;
    }
    fs_state.written = 0;

    auto ts = GetTimestamp();
    int n = fprintf(fs_state.file, "[%s] [INFO] [INIT] Logger started | build: %s\n",
                    ts.c_str(), BuildStamp::VersionString());
    if (n > 0) fs_state.written += static_cast<size_t>(n);
    fflush(fs_state.file);
    return true;
}

static void CloseFile(LogFileState& fs_state) {
    if (fs_state.file) {
        fflush(fs_state.file);
        fclose(fs_state.file);
        fs_state.file = nullptr;
    }
}

// Pick the file a line for `target` should actually land in: itself when its
// handle is open, otherwise the first category that IS open (LF_Init is index 0,
// so it wins — the same fallback ResolveFile already uses for unknown categories).
// Returns LF_COUNT when nothing is open at all.
//
// This is what stops a failed open from silently swallowing a category for the
// whole session. The victim of the old behaviour was the log-verification
// procedure itself: a grep that finds nothing reads as "this code path never ran"
// when the truth is "the file never opened". (audit #5 SE1)
static LogFile ResolveSink(LogFile target) {
    if (s_files[target].file) return target;
    for (int i = 0; i < LF_COUNT; ++i)
        if (s_files[i].file) return static_cast<LogFile>(i);
    return LF_COUNT;
}

// Write a pre-formatted line to a file
static void WriteToFile(LogFileState& fs_state, const char* line) {
    if (!fs_state.file) return;
    RotateIfNeeded(fs_state);
    // RotateIfNeeded RETURNS VOID and can leave `file` null: it closes the old handle
    // unconditionally, and the truncating reopen can fail (disk full — the 21-day
    // retention has no size cap — or a viewer holding the file open). fprintf on a NULL
    // FILE* hits the UCRT invalid-parameter handler, which terminates the INJECTED GAME
    // with no message. Logging is best-effort; the game is not. (B11)
    //
    // The line that TRIGGERED the failed rotation would otherwise still be the one
    // line lost — WriteLog picked this sink while the handle was alive. Hand it to
    // EmergencyNote, which writes to the first surviving file without re-entering
    // rotation. Every LATER line for this category is rerouted by ResolveSink. (SE1)
    if (!fs_state.file) { EmergencyNote(line); return; }
    int written = fprintf(fs_state.file, "%s\n", line);
    if (written > 0) fs_state.written += static_cast<size_t>(written);
    fflush(fs_state.file);
}

// ================================================================
// Process folder cleanup
// ================================================================

// Age-based, matching the file policy: a per-process folder goes when NOTHING in
// it has been written for LOG_RETENTION_DAYS. Judging by the newest file inside
// (not the folder's own mtime, which Windows does not reliably bump on writes to
// existing children) is also what makes this safe while another game is running:
// a live folder's files are seconds old, so it can never be selected.
//
// `keep` is the folder this process owns and is never removed, even in the
// pathological case of a system clock jump.
static void PruneStaleProcessFolders(const fs::path& parentDir, const fs::path& keep) {
    FILETIME nowFt{};
    GetSystemTimeAsFileTime(&nowFt);
    const ULONGLONG now    = AsU64(nowFt);
    const ULONGLONG maxAge = RetentionTicks();

    // Same per-entry error_code discipline as PruneAgedLogs: an undeletable folder
    // (a game still running, a viewer holding a file) must cost that folder, not the
    // rest of the sweep. The old `ec.clear()` sat BEFORE both remove_all calls, so it
    // could not undo the failure that actually broke the loop. (B19)
    ForEachDirEntry(parentDir, [&](const fs::directory_entry& entry) {
        std::error_code ec;
        if (!entry.is_directory(ec)) return;
        if (fs::equivalent(entry.path(), keep, ec)) return;

        ULONGLONG newest = 0;
        bool sawFile = false;
        const bool enumerated = ForEachDirEntry(entry.path(),
            [&](const fs::directory_entry& sub) {
                FILETIME ft{};
                if (!FileWriteTime(sub.path(), ft)) return;
                sawFile = true;
                const ULONGLONG t = AsU64(ft);
                if (t > newest) newest = t;
            });

        // Could not FULLY enumerate it — leave it alone rather than guess it is
        // empty. This guard now covers a mid-iteration failure as well, which the
        // old `if (subEc)` structurally could not: a construction failure it caught,
        // but a failure halfway through threw instead. The distinction is not
        // academic here — the very next branch DELETES the folder when it saw no
        // files, and a half-read folder must never reach it. (audit #5 SE2)
        if (!enumerated) return;

        std::error_code rmEc;
        // An empty folder is removable immediately; there is nothing to retain.
        if (!sawFile) { fs::remove_all(entry.path(), rmEc); return; }
        if (now > newest && (now - newest) > maxAge) fs::remove_all(entry.path(), rmEc);
    });
}

// ================================================================
// Core write functions
// ================================================================

static void WriteLog(const char* level, const char* cat, const char* fmt, va_list args) {
    std::lock_guard<std::mutex> lock(s_mutex);

    auto ts = GetTimestamp();
    char msgBuf[4096];
    vsnprintf(msgBuf, sizeof(msgBuf), fmt, args);

    char lineBuf[4352];
    if (cat && cat[0] != '\0') {
        snprintf(lineBuf, sizeof(lineBuf), "[%s] [%s] [%s] %s", ts.c_str(), level, cat, msgBuf);
    } else {
        snprintf(lineBuf, sizeof(lineBuf), "[%s] [%s] %s", ts.c_str(), level, msgBuf);
    }

    LogFile target = ResolveFile(cat);

    if (s_filesOpen) {
        LogFile sink = ResolveSink(target);
        if (sink != LF_COUNT) WriteToFile(s_files[sink], lineBuf);
    } else if (s_buffering && s_earlyBuffer.size() < EARLY_BUFFER_MAX) {
        s_earlyBuffer.emplace_back(target, std::string(lineBuf));
    }
}

static void WriteSummary(const char* fmt, va_list args) {
    std::lock_guard<std::mutex> lock(s_mutex);

    auto ts = GetTimestamp();
    char msgBuf[4096];
    vsnprintf(msgBuf, sizeof(msgBuf), fmt, args);

    char lineBuf[4352];
    snprintf(lineBuf, sizeof(lineBuf), "[%s] [SUMMARY] %s", ts.c_str(), msgBuf);

    if (s_filesOpen) {
        LogFile sink = ResolveSink(LF_Init);
        if (sink != LF_COUNT) WriteToFile(s_files[sink], lineBuf);
    } else if (s_buffering && s_earlyBuffer.size() < EARLY_BUFFER_MAX) {
        s_earlyBuffer.emplace_back(LF_Init, std::string(lineBuf));
    }
}

// ================================================================
// Public API
// ================================================================

bool Init() {
    std::lock_guard<std::mutex> lock(s_mutex);

    s_logDir = GetLogDirectory();
    std::error_code ec;
    fs::create_directories(s_logDir, ec);

    // Enable early buffering — files will be opened in InitProcessMirror
    s_buffering = true;
    s_earlyBuffer.clear();
    s_earlyBuffer.reserve(EARLY_BUFFER_MAX);
    s_filesOpen = false;

    return true;
}

void InitProcessMirror(const std::wstring& processName) {
    std::lock_guard<std::mutex> lock(s_mutex);

    if (processName.empty()) return;

    // Sanitize process name
    std::wstring safeName = processName;
    auto dotPos = safeName.rfind(L'.');
    if (dotPos != std::wstring::npos) safeName = safeName.substr(0, dotPos);
    for (wchar_t& c : safeName) {
        if (c == L'/' || c == L'\\' || c == L':' || c == L'*' ||
            c == L'?' || c == L'"' || c == L'<' || c == L'>' || c == L'|') {
            c = L'_';
        }
    }

    s_processDir = s_logDir / safeName;
    std::error_code ec;
    fs::create_directories(s_processDir, ec);
    if (ec) return;
    s_processDirReady = true;

    // Open all 5 category files. Each archives its own previous -0.log first.
    //
    // The bool is HONOURED now. It used to be dropped and s_filesOpen set
    // unconditionally, which cost twice over: the failed category was dead for the
    // process with nothing said anywhere, and the early buffer was flushed into its
    // NULL FILE* and then clear()ed — destroying lines that another, perfectly
    // healthy file could have taken. (audit #5 SE1)
    bool opened[LF_COUNT] = {};
    int  openErr[LF_COUNT] = {};
    int  openCount = 0;
    for (int i = 0; i < LF_COUNT; ++i) {
        opened[i] = OpenFileInDir(s_files[i], s_processDir, s_fileNames[i], &openErr[i]);
        if (opened[i]) ++openCount;
    }

    if (openCount > 0) {
        s_filesOpen = true;
        s_buffering = false;

        // Name the failures FIRST, so the top of the surviving log says which
        // categories are missing and where their lines went. Written with snprintf
        // + WriteToFile rather than through Sein::Warn on purpose: WriteLog takes
        // s_mutex, this function already holds it, and std::mutex is not recursive.
        for (int i = 0; i < LF_COUNT; ++i) {
            if (opened[i]) continue;
            auto ts = GetTimestamp();
            char line[512];
            snprintf(line, sizeof(line),
                     "[%s] [ERROR] [INIT] Logger: category '%s' could not open "
                     "'%s-0.log' (errno=%d) — its lines are rerouted here for this run",
                     ts.c_str(), s_fileNames[i].a, s_fileNames[i].a, openErr[i]);
            LogFile sink = ResolveSink(static_cast<LogFile>(i));
            if (sink != LF_COUNT) WriteToFile(s_files[sink], line);
        }

        // Flush the early buffer, rerouting anything whose own category did not
        // open so no buffered line is thrown away.
        for (auto& [target, line] : s_earlyBuffer) {
            LogFile sink = ResolveSink(target);
            if (sink != LF_COUNT) WriteToFile(s_files[sink], line.c_str());
        }
        s_earlyBuffer.clear();
        s_earlyBuffer.shrink_to_fit();
    }
    // else: NOTHING opened. Keep buffering and keep the buffer — it is capped at
    // EARLY_BUFFER_MAX, so the cost is bounded, and clearing it would destroy the
    // only record of the run for no gain. The retention sweeps below are unaffected
    // by our own files and still run.

    // The retention sweep used to run HERE, inline, under the loader lock. It is now
    // RunRetentionSweep() below, called by DllMain — see AB9 and the contract in Sein.h.
    // The ordering constraint it had is preserved by that call site being later still:
    // the sweep must run AFTER the files are open, so the live -0.log of every category
    // already exists and the archives are the only *.log left to age out.
}

// Latched one-shot. The sweep is pure maintenance and there is nothing to gain from
// running it twice in a process; the latch is what lets DllMain call it from either the
// inline (CE host) or the threaded path without either having to know about the other.
static std::atomic<bool> s_sweepDone{false};

void RunRetentionSweep() {
    // ORDER IS THE POINT: copy under the lock, RELEASE, then sweep unlocked.
    //
    // Holding s_mutex across the sweep would MOVE the stall — off the loader lock and onto
    // every thread that logs — which is not the fix. And it is worse than that, measured
    // 2026-08-24 rather than reasoned: the obvious shape (one lock_guard at the top of this
    // function) SELF-DEADLOCKS. The !ready branch below calls LOG_WARN, WriteLog takes
    // s_mutex unconditionally, and std::mutex is not recursive — sein_retention_test dies
    // with a fastfail (exit 127) in its readiness case under exactly that edit. Nothing the sweep touches
    // is shared state: PruneAgedLogs only deletes *.log ARCHIVES in our own folder (the
    // live -0.log files are open and carry a current mtime), and PruneStaleProcessFolders
    // is explicitly told to keep ours.
    fs::path logDir, procDir;
    bool ready = false;
    {
        std::lock_guard<std::mutex> lock(s_mutex);
        logDir  = s_logDir;
        procDir = s_processDir;
        ready   = s_processDirReady;
    }

    if (!ready) {
        // Not an error worth a dialog, but it must not be SILENT: it means the sweep was
        // called before InitProcessMirror succeeded, so retention did not run this session.
        LOG_WARN("Sein::RunRetentionSweep: per-process folder not ready — retention skipped");
        return;
    }

    // Latch AFTER the readiness check, so an early call does not consume the one-shot and
    // leave a later, valid call doing nothing.
    if (s_sweepDone.exchange(true)) return;

    // Timed and LOGGED. Before AB9 this work was inline in InitProcessMirror and its cost
    // was only ever visible as a gap between two unrelated DllMain lines; now that it is
    // deferred and silent, nothing would show it ran at all — and "it ran" is exactly what
    // a live check of this change needs to see. The line is also the measurement: if the
    // sweep is ever suspected of being slow again, the number is already in the log.
    const ULONGLONG t0 = ::GetTickCount64();
    PruneAgedLogs(procDir);
    PruneStaleProcessFolders(logDir, procDir);
    LOG_INFO("Sein::RunRetentionSweep: retention sweep done in %llu ms (off the loader lock)",
             static_cast<unsigned long long>(::GetTickCount64() - t0));
}

void Shutdown() {
    std::lock_guard<std::mutex> lock(s_mutex);
    for (int i = 0; i < LF_COUNT; ++i) {
        CloseFile(s_files[i]);
    }
    s_filesOpen = false;
    s_buffering = false;
    s_earlyBuffer.clear();
}

void SetChannel(LogChannel /*ch*/) {
    // No-op: category routing replaces channel switching
}

LogChannel GetChannel() {
    return LogChannel::Scan;  // No-op
}

void Info(const char* cat, const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    WriteLog("INFO", cat, fmt, args);
    va_end(args);
}

void Error(const char* cat, const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    WriteLog("ERROR", cat, fmt, args);
    va_end(args);
}

void Warn(const char* cat, const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    WriteLog("WARN", cat, fmt, args);
    va_end(args);
}

void Debug(const char* cat, const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    WriteLog("DEBUG", cat, fmt, args);
    va_end(args);
}

void Summary(const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    WriteSummary(fmt, args);
    va_end(args);
}

} // namespace Sein
