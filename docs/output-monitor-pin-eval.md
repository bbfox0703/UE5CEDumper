# Output-monitor pin — can a game with no monitor-select UI be pinned to one screen?

> Moved out of [todo.md](todo.md) on 2026-08-25: it is a finished EVALUATION with a verdict, not an open task, and this folder already keeps evaluations as their own files (`multipipe-eval.md`, `ce-ccode-eval.md`, `log-compression-eval.md`, `text-translation-eval.md`, `aob-block-library-eval.md`). Nothing was edited, only moved.

-----

## Output-monitor pin — "the game has no monitor-select UI" — EVALUATED (2026-07-23), NOT BUILT

**Question:** on a dual-monitor setup, when a game exposes no output-display setting, can we fix it
with **UE functionality**? **Verdict: the UE reflection layer has no concept of an output monitor —
the monitor-selecting step is Win32/DXGI. UE reflection only contributes the windowed↔fullscreen
toggle and the persistence.** And the hard part is not the initial move, it is that the game
**drifts back** — so the deliverable is a *pin*, not a one-shot move.

**What UE reflection does and does not give us**

- Stock UE has **no** monitor-index `UPROPERTY`, no BlueprintCallable monitor selector, and no cvar.
  (The `-monitor=N` recipe circulating since Froyok's 2018 post is an *engine source modification*,
  not stock behaviour.) `r.setres WxH[w|f|wf]` changes mode/resolution, never the screen.
- **Invokable today** (BlueprintCallable ⇒ in the reflection function table ⇒ reachable via
  `invoke_function`): `UGameUserSettings::SetFullscreenMode(int32)` (`EWindowMode` 0=Fullscreen /
  1=WindowedFullscreen / 2=Windowed), `SetScreenResolution`, `ApplyResolutionSettings(bool)`,
  `ApplySettings(bool)`, `SaveSettings()`.
- **NOT invokable:** `SetWindowPosition()` / `GetWindowPosition()` are **not** BlueprintCallable, so
  they are absent from the reflection function table. The backing `WindowPosX` / `WindowPosY` *are*
  config properties (default `-1` = centre) ⇒ writable via Property Search / Live Walker / Solide
  Force. That yields a no-code path (**write WindowPosX/Y → invoke `SaveSettings()` → restart**) but
  it needs a restart and collides with the documented UE 4.16+ "re-centres itself after the startup
  map loads" override.
- Why the move-then-fullscreen sequence works at all: UE `WindowedFullscreen` resolves via
  `MonitorFromWindow`, and DXGI exclusive fullscreen picks "the output containing most of the client
  area" when `pTarget` is NULL — **both follow the window**. So `SetFullscreenMode(2) → move the
  HWND → SetFullscreenMode(1)` lands on the target screen.

**Drift is event-driven, not continuous** — regain focus / alt-tab / `WM_DISPLAYCHANGE` /
swapchain reset. Unity's issue tracker documents exactly this symptom ("exclusive fullscreen always
opens on monitor 1 after regaining focus even when monitor 2 is set as primary"). So a pin does
**not** need a high-frequency poll.

**Three pin mechanisms, lightest first**

- **(a) Rewrite `WM_WINDOWPOSCHANGING` — the good one.** `Grausam.cpp` `SubclassProc` (~line 144)
  already subclasses the game WndProc and `Grausam.cpp` `FindGameWindow()` (~line 61) already resolves the HWND
  (`EnumWindows` + same PID + largest visible). Patching `WINDOWPOS.x/y` **before the move happens**
  is flicker-free and the game never notices. Any "detect it moved, move it back" scheme flickers and
  fights the game's own repositioning — which is the user-visible "it just snaps back" symptom.
- **(b) Low-frequency watchdog — the backstop.** ~4-5 Hz worker; if
  `MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST) != target`, `SetWindowPos`. Structurally
  **identical to the Solide / Hemmung / Laufen write-on-drift re-assert workers** — copy the shape.
  Covers paths (a) can't see (game switches mode via the swapchain, not via `SetWindowPos`).
- **(c) Hook `IDXGISwapChain::SetFullscreenState` — the real fix for exclusive fullscreen.** MSDN is
  explicit: `pTarget` **is** the output selector; NULL means "DXGI guesses from window placement",
  and on alt-enter **NULL is the only option DXGI has**. So for a true-exclusive-fullscreen game
  (a)+(b) are palliative — the game's next `SetFullscreenState(TRUE, NULL)` re-guesses. Substituting
  the user's chosen `IDXGIOutput*` is the cure. MinHook is already vendored (Stark/Grausam), but
  `Lugner_Dxgi.cpp` is a **pure export forwarder (asm thunks), not a
  swapchain vtable hook** — this is entirely new work, and per-API (D3D11 / D3D12 / Vulkan separately).

**This feature is not UE-bound (scope decision needed).** `Heiter.cpp` (`ProxyStart`, ~lines 57-86)
shows **proxy mode starts the pipe server immediately with no AOB scan**, and all three mechanisms
above are pure Win32/DXGI with zero UE reflection ⇒ injected via the `dxgi.dll` proxy this would work
in a **Unity** game too. Blocker: every UI panel currently assumes UE init succeeded, so a non-UE
process shows a wall of errors. Either accept "only this one card works, everything else is red" or
build a minimal non-UE mode — decide before advertising it as a capability.

**Try the no-code per-engine fixes first** (this class of game is rare; don't pre-build):

- **Unity:** `HKCU\Software\<Company>\<Product>` → **`UnitySelectMonitor`** (0-based), and the
  documented **`-adapter N`** launch arg. ⚠ *Engine-specific*: in **UE**, `-adapter` selects the **GPU
  adapter** and does nothing for monitor choice — the two engines are not interchangeable here, and
  `-adapter` is widely mis-recommended for UE.
- **UE:** `-windowed -WinX= -WinY= -ResX= -ResY=` (Steam launch options) or the same keys in
  `%LOCALAPPDATA%\<Game>\Saved\Config\Windows\GameUserSettings.ini` — subject to the 4.16+ recentre bug.
- **Engine-agnostic:** disable the unwanted display before launch (MultiMonitorTool / `DisplaySwitch`),
  re-enable after. 100% effective against "always picks output 0" games.
- **Why "set it as primary" fails:** enumeration order comes from the adapter's output connectors
  (`EnumDisplayMonitors` / DXGI output order); Windows exposes **no** way to reorder it, and the
  primary flag doesn't change it. That is why physically re-ordering the DP cables is the only clean
  non-tool fix.

**Prior art — check before building.** Special K already does this (Window Management X/Y offset,
retained across launches). For one or two games it is the faster answer. Our differentiators: the
zero-flicker (a) that Special K lacks, integration with the existing UI, and the (c) DXGI path —
Special K's own multi-display borderless-fullscreen limitation is still open (SpecialKO/SpecialK#87).

| Phase | Scope | Effort | Risk |
|---|---|---|---|
| **P1** | (a) `WM_WINDOWPOSCHANGING` + (b) watchdog + `EnumDisplayMonitors` listing + 2 pipe cmds (`list_monitors` / `set_game_monitor`) + one Teleport card. Borderless/windowed only | **M** | low |
| **P2** | (c) `SetFullscreenState` hook — covers exclusive fullscreen | **M-L** | med — swapchain vtable hooks read as overlay behaviour to some anti-cheat; per-graphics-API work |
| **P3** | Minimal non-UE-mode UI boundary (unlocks Unity/other engines) | **M** | med |

**Naming:** take **Böse** (barrier/guard) from the [naming-convention.md](naming-convention.md) roster —
the module's job is *holding the window in place*, a barrier, not a transfer (so not `Zart`, and
teleport semantics stay with Wirbel).

**Recommendation:** spend ten minutes on `UnitySelectMonitor` / `-adapter N` against the actual
offending game first. If that sticks, park this entirely — P1+P2 is M-L of work for a handful of
games. If it doesn't stick *and* more than one such game is on hand, P1 alone is cheap: it reuses
Grausam's subclass and Solide's re-assert shape, leaving only monitor enumeration and the
`WM_WINDOWPOSCHANGING` branch as genuinely new code.

*Parent: Grausam foreground-lock infrastructure (dev-log builds ~1950-1984;
project-foreground-lock-grausam). Sibling evaluation of the Schlacht see-through and Hemmung
time-control evals above.*

-----
