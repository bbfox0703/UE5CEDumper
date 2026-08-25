// grausam_window_test.cpp
// UE5CEDumper — DLL LOW L10, second half: Grausam must RE-SUBCLASS the game window
// after the window is destroyed and recreated.
//
// WHY THIS EXISTS RATHER THAN A GAME SESSION
//   The row says "destroy and recreate the game window with a fullscreen toggle, with the
//   foreground lock enabled". That is a GAME PROCEDURE, not the claim. The claim is
//   Grausam's own state machine plus four ordinary Win32 calls over REAL HWNDs:
//     * one cached std::atomic<HWND>            (Grausam.cpp g_gameWindow)
//     * one invalidation predicate               `if (!gw || !::IsWindow(gw))`
//     * a non-blocking try_lock re-subclass      (so the hot hook never blocks)
//     * a GetPropW double-subclass guard         (SubclassEnumProc)
//   Nothing about Unreal, a GPU or a swapchain is asserted. A fullscreen toggle is just
//   one way to make IsWindow() go false; DestroyWindow is another, and it is the one a
//   test can drive deterministically.
//
// WHY IT #includes THE .cpp
//   HookedGetForegroundWindow, SubclassProc, SubclassAllGameWindows, g_gameWindow and
//   g_enabled all live in Grausam.cpp's ANONYMOUS namespace — deliberately, they are not
//   API. Including the translation unit is the only way to reach them without widening
//   the header for a test.
//
// ⚠ NO MinHook HOOK IS INSTALLED
//   The test calls HookedGetForegroundWindow DIRECTLY. user32 is never patched inside the
//   test process, so a crash here cannot take the runner's desktop with it. MinHook is
//   still LINKED (Grausam.cpp's SetForegroundLock references MH_* at external linkage) —
//   linked, not called.
//
// ⚠ THE ARMING PRECONDITION, satisfied BY CONSTRUCTION rather than by luck
//   HookedGetForegroundWindow returns early when the real foreground window belongs to
//   THIS process — "genuinely foreground, keep the truth". A test whose own window happens
//   to be foreground would therefore never reach the re-find, and would pass while
//   asserting nothing. So g_origGetForegroundWindow is pointed at a stub returning
//   nullptr: pid stays 0, 0 != GetCurrentProcessId(), and the path is always reached. That
//   also makes the test independent of whatever else is on the desktop.

#include <windows.h>
#include <stdio.h>
#include <atomic>
#include <thread>
#include <vector>

// Sein is the ONLY module Grausam.cpp calls into (Info x3, Error x5). Stubbing the two
// varargs symbols is enough; compiling Sein.cpp would drag in the whole logging stack —
// file rotation, directory sweeps — none of which this test is about.
namespace Sein {
void Info(const char*, const char*, ...) {}
void Error(const char*, const char*, ...) {}
}  // namespace Sein

#include "../src/Grausam.cpp"  // NOLINT — see "WHY IT #includes THE .cpp" above

// ── harness ──────────────────────────────────────────────────────────

static int g_pass = 0, g_fail = 0;

static void check(const char* label, bool cond, const char* got = nullptr) {
    if (cond) { ++g_pass; }
    else {
        ++g_fail;
        printf("  FAIL  %s%s%s\n", label, got ? "   got: " : "", got ? got : "");
    }
}

// The last wParam the ORIGINAL wndproc was handed. This is the behavioural payoff: the
// subclass is supposed to rewrite WM_ACTIVATEAPP's wParam to TRUE on the way through, so
// what the original proc sees is the observable.
static std::atomic<WPARAM> g_lastActivateApp{0xDEAD};
static std::atomic<int>    g_activateAppSeen{0};

static LRESULT CALLBACK TestWndProc(HWND h, UINT msg, WPARAM w, LPARAM l) {
    if (msg == WM_ACTIVATEAPP) {
        g_lastActivateApp.store(w);
        g_activateAppSeen.fetch_add(1);
    }
    return ::DefWindowProcW(h, msg, w, l);
}

static const wchar_t* kClass = L"UE5CEDumperGrausamTestWnd";

static void RegisterTestClass() {
    WNDCLASSEXW wc{};
    wc.cbSize        = sizeof(wc);
    wc.lpfnWndProc   = &TestWndProc;
    wc.hInstance     = ::GetModuleHandleW(nullptr);
    wc.lpszClassName = kClass;
    ::RegisterClassExW(&wc);
}

// WS_POPUP + WS_VISIBLE and NO owner — SubclassEnumProc requires visible, unowned and
// same-process. SW_SHOWNOACTIVATE keeps it off the foreground so the test does not fight
// the desktop; the stubbed g_origGetForegroundWindow makes that moot, but a window that
// steals focus on a developer's machine is rude.
static HWND MakeWindow() {
    HWND h = ::CreateWindowExW(WS_EX_TOOLWINDOW, kClass, L"grausam-test", WS_POPUP,
                               10, 10, 200, 150, nullptr, nullptr,
                               ::GetModuleHandleW(nullptr), nullptr);
    if (h) ::ShowWindow(h, SW_SHOWNOACTIVATE);
    return h;
}

static HWND WINAPI NoForegroundWindow() { return nullptr; }

static bool IsSubclassed(HWND h) { return ::GetPropW(h, Grausam::kOrigProcProp) != nullptr; }

// Put Grausam back to a known state between cases. The module has no Reset of its own —
// it is a process-lifetime singleton — so the test owns this.
static void ResetGrausam() {
    Grausam::g_enabled.store(false);
    Grausam::g_gameWindow.store(nullptr);
    Grausam::g_origGetForegroundWindow = &NoForegroundWindow;
}

static void DestroyAndSettle(HWND h) {
    ::DestroyWindow(h);
    // Drain this thread's queue so the destroy actually completes before IsWindow is read.
    MSG m;
    while (::PeekMessageW(&m, nullptr, 0, 0, PM_REMOVE)) { ::TranslateMessage(&m); ::DispatchMessageW(&m); }
}

// ── cases ────────────────────────────────────────────────────────────

int main() {
    // UNBUFFERED. A crash mid-run must still show what was already established: this
    // test can genuinely crash -- removing the double-subclass guard makes SubclassProc
    // save ITSELF as the original, and the next dispatched message recurses until the
    // stack blows, exit 127 with no output. build.ps1:305 records the same shape biting
    // dll_helpers_test under CI ("produced ZERO output"), which is why DLL_TEST_TRACE
    // exists there.
    setvbuf(stdout, nullptr, _IONBF, 0);
    printf("grausam_window_test — DLL LOW L10, the window re-subclass half\n");
    RegisterTestClass();

    {   // 1. The baseline: SubclassAllGameWindows installs the subclass at all.
        ResetGrausam();
        HWND a = MakeWindow();
        check("setup: a test window was created", a != nullptr);
        if (!a) { printf("\n%d checks, %d failure(s)\n", g_pass + g_fail, g_fail); return 1; }

        check("baseline: not subclassed before the call", !IsSubclassed(a));
        Grausam::SubclassAllGameWindows();
        check("baseline: subclassed after the call", IsSubclassed(a));

        // The double-subclass guard: a second sweep must not save OUR proc as "the
        // original", which would make the chain call itself forever.
        WNDPROC saved = reinterpret_cast<WNDPROC>(::GetPropW(a, Grausam::kOrigProcProp));
        Grausam::SubclassAllGameWindows();
        check("baseline: a second sweep does not re-save the proc",
              reinterpret_cast<WNDPROC>(::GetPropW(a, Grausam::kOrigProcProp)) == saved);
        check("baseline: and the saved proc is NOT SubclassProc itself",
              saved != &Grausam::SubclassProc);
        DestroyAndSettle(a);
    }

    {   // 2. ⭐ THE ROW: the cached window is destroyed and a new one appears. The GFW
        //    hook must notice IsWindow() went false, re-find, and re-subclass.
        ResetGrausam();
        HWND a = MakeWindow();
        Grausam::SubclassAllGameWindows();
        Grausam::g_gameWindow.store(a);
        Grausam::g_enabled.store(true);

        // ARMING PRECONDITION 1 — the early return must not swallow the case.
        HWND real = Grausam::g_origGetForegroundWindow();
        DWORD pid = 0; if (real) ::GetWindowThreadProcessId(real, &pid);
        check("re-find: the arming precondition holds (foreground is not this process)",
              pid != ::GetCurrentProcessId());

        DestroyAndSettle(a);
        // ARMING PRECONDITION 2 — the premise the "fullscreen toggle" phrasing assumes.
        // Asserted because USER handles are recycled: a stale HWND can silently become
        // valid again, and then the predicate never fires.
        check("re-find: the old window really is gone (IsWindow false)", !::IsWindow(a));

        HWND b = MakeWindow();
        check("re-find: the new window is not subclassed yet", b && !IsSubclassed(b));

        HWND got = Grausam::HookedGetForegroundWindow();
        check("re-find: the hook now reports the NEW window", got == b);
        check("re-find: and it SUBCLASSED the new window", b && IsSubclassed(b));

        // ⭐ BEHAVIOURAL PAYOFF — not "a prop exists" but "deactivation is neutralised".
        // SendMessageW dispatches straight into the wndproc on this thread, so no pump is
        // needed. WM_ACTIVATEAPP with wParam FALSE ("app is being deactivated") must reach
        // the original proc as TRUE.
        g_lastActivateApp.store(0xDEAD); g_activateAppSeen.store(0);
        ::SendMessageW(b, WM_ACTIVATEAPP, FALSE, 0);
        check("re-find: the original proc SAW the message", g_activateAppSeen.load() == 1);
        check("re-find: WM_ACTIVATEAPP(FALSE) was rewritten to TRUE",
              g_lastActivateApp.load() == TRUE);
        DestroyAndSettle(b);
    }

    {   // 3. CONTROL (a): without the hook call, nothing re-subclasses. Proves case 2's
        //    pass came from the hook and not from some ambient sweep.
        ResetGrausam();
        HWND a = MakeWindow();
        Grausam::SubclassAllGameWindows();
        Grausam::g_gameWindow.store(a);
        Grausam::g_enabled.store(true);
        DestroyAndSettle(a);
        HWND b = MakeWindow();
        check("control(a): no hook call -> the new window stays unsubclassed", !IsSubclassed(b));

        g_lastActivateApp.store(0xDEAD);
        ::SendMessageW(b, WM_ACTIVATEAPP, FALSE, 0);
        check("control(a): and WM_ACTIVATEAPP arrives unmodified (FALSE)",
              g_lastActivateApp.load() == FALSE);
        DestroyAndSettle(b);
    }

    {   // 4. CONTROL (b): the PREDICATE, not the mere call. With the cached window still
        //    alive, the hook must NOT re-find — so a window created afterwards stays
        //    unsubclassed even though the hook ran.
        ResetGrausam();
        HWND a = MakeWindow();
        Grausam::SubclassAllGameWindows();
        Grausam::g_gameWindow.store(a);
        Grausam::g_enabled.store(true);

        HWND b = MakeWindow();          // a is still alive
        HWND got = Grausam::HookedGetForegroundWindow();
        check("control(b): the cached window is still reported", got == a);
        check("control(b): IsWindow(a) still true -> no re-find -> b untouched",
              !IsSubclassed(b));
        DestroyAndSettle(b);
        DestroyAndSettle(a);
    }

    {   // 5. CONTROL (c): the message rewrite reads g_enabled. A subclassed window with
        //    the lock OFF must pass FALSE through — otherwise the subclass would keep
        //    lying after the user disabled the feature.
        ResetGrausam();
        HWND a = MakeWindow();
        Grausam::SubclassAllGameWindows();
        check("control(c): the window is subclassed", IsSubclassed(a));
        Grausam::g_enabled.store(false);

        g_lastActivateApp.store(0xDEAD);
        ::SendMessageW(a, WM_ACTIVATEAPP, FALSE, 0);
        check("control(c): lock OFF -> WM_ACTIVATEAPP passes through as FALSE",
              g_lastActivateApp.load() == FALSE);

        Grausam::g_enabled.store(true);
        g_lastActivateApp.store(0xDEAD);
        ::SendMessageW(a, WM_ACTIVATEAPP, FALSE, 0);
        check("control(c): lock ON  -> the same message becomes TRUE",
              g_lastActivateApp.load() == TRUE);
        DestroyAndSettle(a);
    }

    {   // 6. ⭐ THE LEG A LIVE SESSION COULD NEVER OBSERVE: concurrent GFW-hook threads.
        //    Grausam.cpp's own comment says the non-blocking try_lock exists so concurrent
        //    hook threads cannot race SubclassEnumProc's check-then-act, "which would
        //    corrupt the saved WNDPROC". The corruption shape is specific and checkable:
        //    the saved prop ends up pointing at SubclassProc itself, and every message
        //    then recurses. Nobody can arrange this by toggling fullscreen.
        ResetGrausam();
        HWND w = MakeWindow();
        Grausam::g_enabled.store(true);

        std::atomic<bool> stop{false};
        std::atomic<int>  corrupted{0};
        std::vector<std::thread> ts;
        for (int i = 0; i < 4; ++i) {
            ts.emplace_back([&] {
                for (int n = 0; n < 400 && !stop.load(); ++n) {
                    Grausam::g_gameWindow.store(nullptr);   // force the re-find every time
                    Grausam::HookedGetForegroundWindow();
                    auto p = reinterpret_cast<WNDPROC>(::GetPropW(w, Grausam::kOrigProcProp));
                    if (p == &Grausam::SubclassProc) corrupted.fetch_add(1);
                }
            });
        }
        for (auto& t : ts) t.join();

        check("concurrency: the saved WNDPROC never became SubclassProc itself",
              corrupted.load() == 0);
        check("concurrency: the window ended up subclassed exactly once", IsSubclassed(w));
        auto finalProc = reinterpret_cast<WNDPROC>(::GetPropW(w, Grausam::kOrigProcProp));
        check("concurrency: and the saved proc is the ORIGINAL one",
              finalProc == &TestWndProc);
        DestroyAndSettle(w);
    }

    ResetGrausam();
    printf("\n%d checks, %d failure(s)\n", g_pass + g_fail, g_fail);
    return g_fail == 0 ? 0 : 1;
}
