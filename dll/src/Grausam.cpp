// Grausam — グラオザーム (幻影魔法大師 — Seven Sages, Master of Illusion)
// ForegroundLock: illusions so the game never learns it lost focus —
//   1. MinHook user32!GetForegroundWindow → report the game's own window as
//      foreground (defeats t.IdleWhenNotForeground idle + FApp::HasFocus polling).
//   2. Subclass every top-level game window's WndProc and rewrite the
//      deactivation messages (WM_ACTIVATEAPP / WM_ACTIVATE / WM_NCACTIVATE →
//      "active", swallow WM_KILLFOCUS) → defeats WM_ACTIVATEAPP-driven pauses
//      (UE's OnApplicationActivationChanged + any game-side focus-loss pause).
//   3. MinHook user32!ClipCursor + SetCursorPos → while the lock is on but the
//      game is BACKGROUNDED, release the cursor confinement / swallow recentring
//      so the mouse isn't trapped in the game window (the whole-desktop lock
//      symptom); pass through untouched when the game is genuinely foreground.
// See Grausam.h.

#include "Grausam.h"
#include "Sein.h"
#include "Falsch.h"

#include <windows.h>
#include <MinHook.h>
#include <atomic>
#include <mutex>
#include <utility>

namespace Grausam {

namespace {

using GetForegroundWindow_t = HWND(WINAPI*)();
using ClipCursor_t          = BOOL(WINAPI*)(const RECT*);
using SetCursorPos_t        = BOOL(WINAPI*)(int, int);

std::mutex             g_mutex;
GetForegroundWindow_t  g_origGetForegroundWindow = nullptr;
ClipCursor_t           g_origClipCursor          = nullptr;  // trampoline — NEVER call ::ClipCursor from the hook (recursion)
SetCursorPos_t         g_origSetCursorPos        = nullptr;
std::atomic<bool>      g_enabled{false};
std::atomic<HWND>      g_gameWindow{nullptr};
bool                   g_hookInstalled = false;
bool                   g_mhInitialized = false;

// Per-window property key holding the original WNDPROC of a window we subclassed.
constexpr const wchar_t* kOrigProcProp = L"UE5CEDumper_Grausam_OrigProc";

// ── Window enumeration ───────────────────────────────────────────────

BOOL CALLBACK PickLargestProc(HWND hwnd, LPARAM lp) {
    DWORD pid = 0;
    ::GetWindowThreadProcessId(hwnd, &pid);
    if (pid != ::GetCurrentProcessId()) return TRUE;
    if (!::IsWindowVisible(hwnd)) return TRUE;
    if (::GetWindow(hwnd, GW_OWNER) != nullptr) return TRUE;  // skip owned dialogs/tooltips

    RECT rc{};
    if (!::GetWindowRect(hwnd, &rc)) return TRUE;
    const long area = (rc.right - rc.left) * (rc.bottom - rc.top);
    auto* best = reinterpret_cast<std::pair<HWND, long>*>(lp);
    if (area > best->second) { best->first = hwnd; best->second = area; }
    return TRUE;
}

HWND FindGameWindow() {
    std::pair<HWND, long> best{nullptr, 0};
    ::EnumWindows(&PickLargestProc, reinterpret_cast<LPARAM>(&best));
    return best.first;
}

// Fwd decl: the GFW hook re-subclasses newly-created game windows on a window
// re-find (L10); SubclassAllGameWindows is defined further down.
void SubclassAllGameWindows();

// ── (1) GetForegroundWindow hook ─────────────────────────────────────

HWND WINAPI HookedGetForegroundWindow() {
    HWND real = g_origGetForegroundWindow ? g_origGetForegroundWindow() : ::GetForegroundWindow();
    if (!g_enabled.load(std::memory_order_relaxed))
        return real;

    DWORD pid = 0;
    if (real) ::GetWindowThreadProcessId(real, &pid);
    if (pid == ::GetCurrentProcessId())
        return real;  // genuinely foreground — keep the truth

    HWND gw = g_gameWindow.load(std::memory_order_relaxed);
    if (!gw || !::IsWindow(gw)) {
        gw = FindGameWindow();
        g_gameWindow.store(gw, std::memory_order_relaxed);
        // The cached window went invalid (e.g. a fullscreen toggle recreated it) —
        // subclass any new top-level game windows so WM_ACTIVATEAPP rewriting covers
        // them too, not just the ones present at enable. Runs only on this rare re-find;
        // SubclassEnumProc skips already-subclassed windows. Non-blocking try_lock on
        // g_mutex serializes with the enable-path subclass so concurrent GFW-hook threads
        // can't race SubclassEnumProc's check-then-act (which would corrupt the saved
        // WNDPROC), and the hot hook never blocks. (L10)
        if (g_mutex.try_lock()) {
            SubclassAllGameWindows();
            g_mutex.unlock();
        }
    }
    return (gw && ::IsWindow(gw)) ? gw : real;
}

// ── (3) Cursor-confinement neutralization ────────────────────────────
// Keeping the game "foreground" also keeps its game thread ticking, so it
// re-applies ClipCursor()/SetCursorPos() every frame to confine the OS cursor
// to its viewport — even after the user switched to another app. Windows
// auto-releases ClipCursor on real deactivation, but the still-running game
// re-clips within a frame, trapping the mouse across the whole desktop. While
// the lock is on AND the game is NOT genuinely foreground we release the clip /
// swallow recentring; when the game truly is foreground (the user is actually
// playing) both pass through so in-game mouse-look is untouched.

// Read focus truth through the un-hooked GetForegroundWindow trampoline so it is
// not fooled by our own illusion (HookedGetForegroundWindow).
bool GameGenuinelyForeground() {
    HWND real = g_origGetForegroundWindow ? g_origGetForegroundWindow() : ::GetForegroundWindow();
    if (!real) return false;
    DWORD pid = 0;
    ::GetWindowThreadProcessId(real, &pid);
    return pid == ::GetCurrentProcessId();
}

BOOL WINAPI HookedClipCursor(const RECT* rc) {
    if (!g_origClipCursor) return FALSE;  // defensive — never null once hooked
    // Only intercept CONFINEMENT (rc != null). A release (rc == null) is exactly
    // what we want, so it passes through. Confinement while backgrounded →
    // release instead so the cursor is free for the app the user switched to.
    if (rc && g_enabled.load(std::memory_order_relaxed) && !GameGenuinelyForeground())
        return g_origClipCursor(nullptr);
    return g_origClipCursor(rc);
}

BOOL WINAPI HookedSetCursorPos(int x, int y) {
    if (!g_origSetCursorPos) return FALSE;
    // Swallow per-frame recentring while backgrounded so the cursor does not snap
    // back into the game window; report success so the game's own logic is
    // unaffected.
    if (g_enabled.load(std::memory_order_relaxed) && !GameGenuinelyForeground())
        return TRUE;
    return g_origSetCursorPos(x, y);
}

// ── (2) WndProc subclass — neutralize deactivation messages ──────────

LRESULT CALLBACK SubclassProc(HWND h, UINT msg, WPARAM w, LPARAM l) {
    auto orig = reinterpret_cast<WNDPROC>(::GetPropW(h, kOrigProcProp));

    if (g_enabled.load(std::memory_order_relaxed)) {
        switch (msg) {
            case WM_ACTIVATEAPP:  w = TRUE;                              break;  // always "activated"
            case WM_NCACTIVATE:   w = TRUE;                              break;  // keep frame active
            case WM_ACTIVATE:     w = MAKEWPARAM(WA_ACTIVE, HIWORD(w));  break;  // low word = WA_ACTIVE
            case WM_KILLFOCUS:    return 0;                                      // swallow focus loss
            default: break;
        }
    }

    if (orig) return ::CallWindowProcW(orig, h, msg, w, l);
    return ::DefWindowProcW(h, msg, w, l);
}

BOOL CALLBACK SubclassEnumProc(HWND hwnd, LPARAM /*lp*/) {
    DWORD pid = 0;
    ::GetWindowThreadProcessId(hwnd, &pid);
    if (pid != ::GetCurrentProcessId()) return TRUE;
    if (!::IsWindowVisible(hwnd)) return TRUE;
    if (::GetWindow(hwnd, GW_OWNER) != nullptr) return TRUE;
    if (::GetPropW(hwnd, kOrigProcProp)) return TRUE;  // already subclassed by us

    WNDPROC orig = reinterpret_cast<WNDPROC>(
        static_cast<LONG_PTR>(::GetWindowLongPtrW(hwnd, GWLP_WNDPROC)));
    if (!::SetPropW(hwnd, kOrigProcProp, reinterpret_cast<HANDLE>(orig)))
        return TRUE;
    ::SetWindowLongPtrW(hwnd, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(&SubclassProc));

    // NB: no GetWindowTextW here — a synchronous same-process WM_GETTEXT under g_mutex
    // can hang the pipe/mailbox thread if the window's owning thread isn't pumping
    // (the paused/backgrounded game this feature targets), and the title was unused. (L8)
    RECT rc{}; ::GetWindowRect(hwnd, &rc);
    Sein::Info("Grausam", "Subclassed window 0x%p (%dx%d, unicode=%d)",
               reinterpret_cast<void*>(hwnd),
               (int)(rc.right - rc.left), (int)(rc.bottom - rc.top),
               (int)::IsWindowUnicode(hwnd));
    return TRUE;
}

void SubclassAllGameWindows() {
    ::EnumWindows(&SubclassEnumProc, 0);
}

// [MH-TRAMPOLINE-PREFLIGHT] A user32 export MinHook would trampoline wrongly -- a Windows build whose export opens
// with an instruction MinHook's decoder misreads -- must cost the feature, not the game. Logs why when it refuses.
bool VetForHook(FARPROC p, const char* name) {
    const Falsch::Report r = Falsch::VetHookTarget(reinterpret_cast<uintptr_t>(p));
    if (r.verdict == Falsch::Verdict::MinHookRefuses)   // MH_CreateHook will fail by itself; say why it will
        Sein::Warn("Grausam", "%s: %s", name, r.why.c_str());
    if (r.verdict != Falsch::Verdict::Unsafe) return true;
    Sein::Error("Grausam", "%s hook refused by the trampoline pre-flight at +%d: %s", name, r.offset, r.why.c_str());
    return false;
}

}  // namespace

int SetForegroundLock(bool enable) {
    std::lock_guard<std::mutex> lock(g_mutex);

    if (enable) {
        if (!g_hookInstalled) {
            if (!g_mhInitialized) {
                MH_STATUS st = MH_Initialize();
                if (st != MH_OK && st != MH_ERROR_ALREADY_INITIALIZED) {
                    Sein::Error("Grausam", "MH_Initialize failed: %s", MH_StatusToString(st));
                    return -1;
                }
                g_mhInitialized = true;
            }

            HMODULE user32 = ::GetModuleHandleW(L"user32.dll");
            FARPROC pGfw  = user32 ? ::GetProcAddress(user32, "GetForegroundWindow") : nullptr;
            FARPROC pClip = user32 ? ::GetProcAddress(user32, "ClipCursor")          : nullptr;
            FARPROC pSetP = user32 ? ::GetProcAddress(user32, "SetCursorPos")        : nullptr;
            if (!pGfw) {
                Sein::Error("Grausam", "GetProcAddress(user32!GetForegroundWindow) failed");
                return -2;
            }

            // (1) GetForegroundWindow — the primary illusion (idle/pause defeat).
            if (!VetForHook(pGfw, "GetForegroundWindow")) return -3;
            MH_STATUS st = MH_CreateHook(reinterpret_cast<LPVOID>(pGfw),
                                         reinterpret_cast<LPVOID>(&HookedGetForegroundWindow),
                                         reinterpret_cast<LPVOID*>(&g_origGetForegroundWindow));
            if (st != MH_OK) {
                Sein::Error("Grausam", "MH_CreateHook(GetForegroundWindow) failed: %s", MH_StatusToString(st));
                return -3;
            }
            st = MH_EnableHook(reinterpret_cast<LPVOID>(pGfw));
            if (st != MH_OK) {
                Sein::Error("Grausam", "MH_EnableHook(GetForegroundWindow) failed: %s", MH_StatusToString(st));
                MH_RemoveHook(reinterpret_cast<LPVOID>(pGfw));
                return -4;
            }

            // (3) ClipCursor + SetCursorPos — cursor-lock neutralization. Best
            // effort: a game that never confines the cursor never triggers them,
            // and a failure to hook them must NOT abort the (primary) lock. Enable
            // per-target, NOT MH_ALL_HOOKS — every module that hooks shares this one
            // MinHook instance, and the holder's hook must never be enabled.
            if (pClip && VetForHook(pClip, "ClipCursor")
                && MH_CreateHook(reinterpret_cast<LPVOID>(pClip),
                                 reinterpret_cast<LPVOID>(&HookedClipCursor),
                                 reinterpret_cast<LPVOID*>(&g_origClipCursor)) == MH_OK)
                MH_EnableHook(reinterpret_cast<LPVOID>(pClip));
            else
                Sein::Error("Grausam", "ClipCursor hook unavailable — cursor may stay locked when backgrounded");

            if (pSetP && VetForHook(pSetP, "SetCursorPos")
                && MH_CreateHook(reinterpret_cast<LPVOID>(pSetP),
                                 reinterpret_cast<LPVOID>(&HookedSetCursorPos),
                                 reinterpret_cast<LPVOID*>(&g_origSetCursorPos)) == MH_OK)
                MH_EnableHook(reinterpret_cast<LPVOID>(pSetP));

            g_hookInstalled = true;
        }

        g_gameWindow.store(FindGameWindow(), std::memory_order_relaxed);
        g_enabled.store(true, std::memory_order_relaxed);
        // Subclass the game window(s) so WM_ACTIVATEAPP-driven pauses are defeated
        // too (the GetForegroundWindow hook alone only covers the polling path).
        SubclassAllGameWindows();
        Sein::Info("Grausam", "Foreground lock ENABLED (fg-window=0x%p)",
                   reinterpret_cast<void*>(g_gameWindow.load()));
        return 1;
    }

    // Soft-disable: leave the hooks + subclass installed (they gate on g_enabled and
    // pass through when off) to avoid an unhook/unsubclass race with an in-flight call.
    g_enabled.store(false, std::memory_order_relaxed);
    // Actively release any confinement we were masking so the cursor is freed the
    // instant the lock goes off (the game's own release-on-focus-loss was
    // suppressed while we were on). Skip if the game is genuinely foreground — the
    // user is playing and a live in-game clip must be left alone.
    if (g_origClipCursor && !GameGenuinelyForeground())
        g_origClipCursor(nullptr);
    Sein::Info("Grausam", "Foreground lock DISABLED");
    return 0;
}

int IsForegroundLockEnabled() {
    return g_enabled.load(std::memory_order_relaxed) ? 1 : 0;
}

}  // namespace Grausam
