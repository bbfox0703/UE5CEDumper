#pragma once
// Grausam — グラオザーム (幻影魔法大師 — Seven Sages, Master of Illusion)
// ForegroundLock: hook user32!GetForegroundWindow so the game always believes it
// is the foreground application. UE's t.IdleWhenNotForeground idle and any
// focus-loss pause both gate on FApp::HasFocus() → IsThisApplicationForeground()
// → GetForegroundWindow() PID compare; casting the illusion that our own window
// is foreground keeps the game thread ticking (ProcessEvent invokes / POV reads)
// while our UI or Cheat Engine holds the real foreground.
//
// Side effect neutralized: because the game never learns it lost focus, its
// still-ticking thread keeps calling ClipCursor()/SetCursorPos() to confine the
// OS cursor to its viewport, trapping the mouse across the whole desktop. We also
// hook those two: while the lock is on but the game is backgrounded, the clip is
// released / recentring swallowed; when the game is genuinely foreground they
// pass through so in-game mouse-look is unaffected.

namespace Grausam {

// Enable/disable the foreground illusion. Idempotent. Returns:
//   1  = enabled (hook active)
//   0  = disabled (soft; hook left installed but passes through)
//  <0  = error installing the hook
[[nodiscard]] int SetForegroundLock(bool enable);

// 1 if the illusion is currently active, else 0.
int IsForegroundLockEnabled();

}  // namespace Grausam
