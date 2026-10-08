# Handover — how to work on this repo, on these machines

> 🤝 **START HERE.** This is the runbook: how to work in this repo on these machines — the first ten
> minutes, computer-use grants, launching fixtures and games, the session rules, gates, tests, builds
> and pipe rigs, driving Cheat Engine, and what is true only on one machine.
>
> **What it holds: procedures only** — no open work, no counts, no current state. What is open lives
> in [`todo.md`](todo.md) (its current-programme line is at the top); what shipped in
> [`dev-log.md`](dev-log.md); what is shipped but not yet proven live in
> [`verification-register.md`](verification-register.md); how to work, and why, in
> [`working-lessons.md`](working-lessons.md).
>
> **How to update it.** Edit in place whenever a procedure changes. Never write a count, a build
> number or a list of open items here; write the command that derives it. A measurement keeps its own
> date inline. A trap whose story `working-lessons.md` owns gets a one-line pointer here, not a copy.
> Cite this file by section (`handover §N`), never by line, and keep the §0–§10 numbering: an emptied
> section keeps its heading and says where its content went.
>
> Its two predecessors, and this file's own former name, are recorded in
> [`archive/README.md`](archive/README.md).
>
> ⚠ **`out/` is gitignored and does NOT travel.** Two of the most useful records of what was
> actually done (`out/NIGHT-RUN-2026-08-19.md`, `out/KILLED-2026-08-21.md`) exist only on this
> machine. This file, [`todo.md`](todo.md), [`dev-log.md`](dev-log.md),
> [`verification-register.md`](verification-register.md) and [`working-lessons.md`](working-lessons.md)
> are the whole travelling record.

-----

## 0. The first ten minutes

Run these three, in this order, before deciding anything:

```bash
py tools/check_all.py
```
```bash
git status -sb && cat build_number.txt
```
```bash
py tools/check_audit_register.py --list
```

⚠ `--list` names only the **HIGH/MED** rows; when there are none it prints the open count and names
**no rows**. That is the expected output, not an empty register.

Then read the current-programme line at the top of [`todo.md`](todo.md) for what is open (§7).

⚠ **Every ad-hoc Python one-liner in this repo needs this first line**, or it dies with
`UnicodeEncodeError` before printing anything — the console codepage here is **cp950** and every doc
is full of emoji and Traditional Chinese:

```python
import sys; sys.stdout.reconfigure(encoding="utf-8", errors="replace")
```

-----

## 1. State of the tree

Derive the state; values written into this file go stale.

| | derive it with |
|---|---|
| `build_number.txt` | `cat build_number.txt` |
| branch | `git status -sb` |
| CI gates | `py tools/check_all.py` — read its own `N gate(s) run` line |
| C# tests | the `total:` line of the last test run |
| `dist/UE5DumpUI.exe` | `ls -l dist/UE5DumpUI.exe` — ~54 MB is Native-AOT trimmed, ~107 MB is not |
| mailbox contract | `MAILBOX_CONTRACT` / `MAILBOX_CONTRACT_MIN` in `dll/src/Mimic.h` |
| experimental gate | `%LOCALAPPDATA%\UE5CEDumper\experimental.json` → `"enabled"` |

**Open a PR only when asked** — a PR runs CI, a full clean Native-AOT publish plus the whole suite.
The convention is commit to `dev` → `gh pr create --base main --head dev` →
`gh pr merge N --merge`, never `--admin`.

⚠ **`dist/` is gitignored**, so `git status` says nothing about which binary is sitting there. Since
`[DISTCOPY-2026-08-22]` `build.ps1` verifies the publish copy **by SHA256** and fails loudly — it
used to print `[OK] (54.7 MB)` and exit 0 over a copy that never happened, because a **stale** AOT
exe is 54.7 MB too. Checking the size is no longer the test.

-----

## 2. Computer-use grants

### ⭐ First: CHECK what is held. Grants both outlive sessions and vanish without one.

Two measurements, hours apart on the same machine, and they point opposite ways:

* **2026-08-22 morning** — `list_granted_applications` returned **20 grants made on 2026-08-19
  19:31–19:36**, still valid three days and many sessions later. So a new session does **not** clear
  them: the plan doc's "grants do not survive a session" (removed from it on 2026-10-08) is FALSE
  here.
* **2026-08-22 18:20, same boot** — the list came back **empty**, and all 20 had to be re-requested
  (batches of 7 · 7 · 6; every one granted, same names, same tiers). `GetTickCount64` puts the
  last boot at **2026-08-21 14:31**, *before* the morning reading — so **no reboot happened
  between the two**.

⛔ **"A reboot is the real invalidation event" is therefore REFUTED** — do not re-assert it. What
actually clears them is unidentified; an MCP-server or app restart is the obvious suspect and is
**unmeasured**, so do not write that down as the cause either.

⇒ The only safe rule, and it was already the right one:

```
call list_granted_applications  →  request only what is missing
```

A blanket re-request costs three or four dialogs and, if the maintainer is away, stalls the run for
nothing. An *assumed* grant is worse: it fails at the first click, mid-row. The check is one call
with no side effects — make it the first one, every time.

### System key combos are not granted

⚠ **`systemKeyCombos` is NOT granted.** `alt+F4`, `ctrl+alt+del` and friends are refused with
*"is a system-level shortcut"*. To close a window, post `WM_CLOSE` yourself:

```python
import ctypes, ctypes.wintypes as w
u = ctypes.windll.user32
# enumerate the target pid's visible titled windows, then:  u.PostMessageW(hwnd, 0x0010, 0, 0)
```

### Non-game grants — request these on a new PC, or when the list comes back empty

Games are granted per title (and their exe separately, see below). These are the **tools and
system windows** a session needs, recorded 2026-09-27 from `list_granted_applications`. Request
them in the two batches below (the ≤7 rule). **A `notInstalled` name fails its whole batch**, so on
a PC that lacks one (no NVIDIA App, no Everything), drop it from the batch rather than retrying.
Names are the Start-menu names of a **zh-TW Windows**; an English Windows says `Notepad` and
`File Explorer` instead.

| request name | listed as | why a session needs it |
|---|---|---|
| `UE5DumpUI` | UE5DumpUI | the UI under test, `dist\UE5DumpUI.exe` (a copy elsewhere, e.g. under `out\pathshape\`, is a separate grant: request its exe while it runs) |
| `Cheat Engine (64-bit SSE4-AVX2)` | same | ⭐ the CE the maintainer launches |
| `Cheat Engine (64-bit)` | same | the plain CE build, for rows that name it |
| `Cheat Engine tutorial (64-bit)` | same | CE's bundled tutorial; granted in August 2026, absent from the 2026-09-27 list, so in neither batch below: request it only when a row names it |
| `Notepad++` | same | reading logs and `.CT` files on screen |
| `記事本` | 記事本 | Notepad (not in the 2026-09-27 list; it was in earlier sessions) |
| `Everything` | same | file search |
| `檔案總管` | 檔案總管 | Explorer — **click tier only**; typing into it is refused |
| `textinputhost.exe` | Textinputhost | the Windows text-input host (IME candidate window, emoji panel). When it pops over the app, an ungranted host blocks the next call |
| `nvidia overlay.exe` | Nvidia overlay | the NVIDIA App overlay: after a SendInput key it takes the foreground, and every later call is refused as *"The user doesn't want to take this action right now"* (2026-09-27) |
| `Steam` | Steam | `steam.exe`, the bootstrapper (not in the 2026-09-27 list) |
| `steamwebhelper.exe` | Steamwebhelper | the Steam library window — **with `.exe`** (a bare name resolves back to `Steam`); only grantable while Steam runs |

- **Batch 1:** `UE5DumpUI`, `Cheat Engine (64-bit SSE4-AVX2)`, `Cheat Engine (64-bit)`, `Notepad++`,
  `記事本`, `Everything`, `檔案總管`.
- **Batch 2:** `textinputhost.exe`, `nvidia overlay.exe`, `Steam`, `steamwebhelper.exe` (start Steam
  first, and leave out whatever the PC does not have).

### Game grants — nice to have; when one is missing, say so and let the user decide

The titles the verification rows have used, recorded 2026-09-27. **None is required to start a
session**, and a PC may simply not own a game: when a row needs a title that is not installed or
not granted, **tell the user which one and why, and let them decide** whether to install it — never
install, download or buy anything yourself, and never assume the user owns a title. Check what is
installed with `fixture_census.py` (§3) before planning around one.

| request name | appid / kind | notes |
|---|---|---|
| `DumperTest Shipping` | fixture (UE 5.4) | ⭐ the default fixture (§3); self-built, so every PC can have it; its shortcut targets the inner exe, not the shim |
| `DumperTest Development` | fixture (UE 5.4) | the other-direction check; the inner exe, not the shim |
| `DumperTest58 Shipping` / `DumperTest58 Development` | fixture (UE 5.8) | |
| DumperTest51 | fixture (UE 5.1) | packaged; not in the 2026-09-27 list — request its exe while it runs |
| `冒險家艾略特的千年奇譚` | 3483510 | Elliot; clicking in-game also needs `Elliot-win64-shipping` |
| `勇者鬥惡龍 VII Reimagined` | 2499860 | DQ7R; in-game clicks need `Dq7r-win64-shipping` |
| `OCTOPATH TRAVELER` | 921570 | relaunches itself unless started with `-applaunch` (§3) |
| `EVERSPACE 2` | 1128920 | exits on a direct exe launch (§3) |
| `莊園領主 Manor Lords` | 1363080 | |
| `Lushfoil Photography Sim` | 1749860 | |
| `Solarpunk` | 1805110 | |
| `Star Trek Voyager - Across the Unknown` | 2643390 | exits on a direct exe launch (§3) |
| `Titan Quest II` | 1154030 | |
| `The Artisan of Glimmith` | 4160210 | Geri; in-game clicks need `Geri-Win64-Shipping.exe` (with `.exe`) |
| `Gg2game` | exe | GalGun Double Peace |

The Steam titles are granted by name (a `steam://rungameid/<appid>` grant) and their shipping exe
separately, only while it runs; `list_granted_applications` says what is granted now.

### The ≤7 rule, and what breaking it looks like

⛔ **At most ~7 apps per `request_access` call.** Measured 2026-08-17: an 18-app request rendered a
dialog **taller than the display**, the Accept button could not be reached, and it returned
**`user_denied` for all eighteen**. That reads exactly like a deliberate refusal and **is not one**.

> **Never diagnose a blanket `user_denied` as a decision without checking the batch size first.**

### Other grant facts worth knowing before you need them

* **`notInstalled` is not a refusal** — the request short-circuits at name resolution and the dialog
  is never shown. The resolver's index is the **all-users** Start Menu
  (`C:\ProgramData\Microsoft\Windows\Start Menu\Programs`); the per-user one is not enumerated here.
  **To make an app grantable that is not there** (`UE5DumpUI`, a DumperTest package): the maintainer
  creates a shortcut in that all-users folder through Explorer (it needs elevation, which computer use
  cannot answer), named **exactly** as `request_access` will ask, targeting the **inner** exe; then
  request it — in the same session, no restart (verified 2026-08-17, with the probes that found the
  folder: [`auto-verification-session-plan.md`](auto-verification-session-plan.md) §1, "SOLVED").
* **Resolver lag is real.** `DumperTest Development` / `Shipping` returned `notInstalled` minutes
  after their shortcuts existed and `Get-StartApps` already listed them; **retried ~10 minutes later
  with nothing changed, both granted**. Wait and retry — do not start editing names or paths on the
  strength of one refusal.
* **A bare exe is grantable only while it is RUNNING.** Launch first, then grant.
* ⚠⚠ **The Steam-game grants are keyed to `steam://rungameid/<appid>`, NOT to the shipping exe.**
  So a row that needs to *click inside the game* implies a **second `request_access` for the exe** —
  the one thing the preamble above says stalls an unattended batch. Plan it: launch the title,
  request the exe, then start the row. Rows that only inject and read the pipe need no such grant.
  * ⭐ **AND THE EXE NAME MUST CARRY `.exe`.** Measured on Geri 2026-08-23, three refusals deep:
    `Geri-Win64-Shipping` returns *"doesn't match any installed or running application"* — **with
    the process running and its window up** — because the resolver folds it into the window title
    (`The Artisan of Glimmith`), which already maps to the Steam URI. `Geri-Win64-Shipping.exe`
    granted first try. This is the **same trick the `steamwebhelper.exe` row records**, and it is
    now confirmed to generalise: *when a bare name collides with something already granted, add
    `.exe`.*
  * ⚠ A `notInstalled` result **short-circuits the WHOLE call** — the other apps in that batch are
    never shown to the maintainer. So do not pad a request with speculative names; one bad name
    costs the entire batch.
* ⚠ **`nvidia overlay.exe` must be granted** (listed as `Nvidia overlay`; request it WITH `.exe`).
  Measured 2026-09-27: after `tools/verify/send_key.py` sends a key, the foreground passes to the
  untitled NVIDIA overlay window, and every later computer-use call (a screenshot included) is
  refused with *"The user doesn't want to take this action right now"* — which reads like a human
  refusal and is not one. `front_window.py front UE5DumpUI` clears it for a moment; the grant
  (made the same day) removes the block.
* **Avowed is installed but ungrantable** — absent from the Start menu, so `request_access` cannot
  resolve it. Perfectly usable for headless pipe/log rows.
* Not needed: browsers (read-only tier anyway), terminals/IDEs (all shell work goes through Bash),
  `python.exe`.
* ⚠ `tools/verify/register_apps.py` is a **refuted hypothesis** (per-user registry registration made
  no difference). Do not run it; if its residue bothers you,
  `py tools/verify/register_apps.py --revert --apply`.

-----

## 3. Launching a fixture, and which process is the right one

### ⭐ The Steam question, answered

There are **three different "the process"** and picking the wrong one fails silently each time:

| you want | the right thing | the wrong thing that looks right |
|---|---|---|
| to **launch** a Steam title | `"C:\Program Files (x86)\Steam\steam.exe" -applaunch <appid>` | the shipping exe directly — some titles boot a **dead engine** or exit instantly (see below) |
| to **grant / front / screenshot** the Steam UI | `steamwebhelper.exe` (with the suffix) | `Steam` alone → the library is a masked black rectangle |
| to **inject / suspend / front** the game | `<Name>-Win64-Shipping.exe` | the same-stem **launcher shim** next to it, which sorts first and owns no window |

⚠ **`steam://rungameid/...` silently fails** via `open_application`. Drive the Steam library UI, or
use `steam.exe -applaunch`.

⚠ **`open_application` on a game's own grant is the direct launch too (2026-10-07).** The grant named after the
game ("Avowed") resolves to the launcher shim in the game folder, so `open_application` starts the exe outside
Steam. The game then restarts itself through Steam with the command line it was given, and Steam stops on a
**"launch with special parameters"** confirmation (zh-TW client: 使用特殊參數啟動) that nobody answers: the maintainer
saw it on Avowed after exactly that call, where the `-applaunch` launches of 2026-10-06 showed none. A grant is
for fronting and screenshots; the launch is always `steam.exe -applaunch <appid>`, with no extra arguments.

### The shim table — measured on this machine, 2026-08-22

The census's "first exe" column is the **launcher**; the process the DLL actually lives in is the
right-hand column, and it is what the log folder under `%LOCALAPPDATA%\UE5CEDumper\Logs\` is named
after.

| title | appid | launcher (wrong target) | **inject / front this** |
|---|---|---|---|
| Elliot | 3483510 | `Elliot.exe` | `Elliot-Win64-Shipping.exe` |
| Lushfoil | 1749860 | `LushfoilSim.exe` | `LushfoilSim-Win64-Shipping.exe` |
| Solarpunk | 1805110 | `Solarpunk.exe` | `SolarpunkSteam-Win64-Shipping.exe` |
| EVERSPACE 2 | 1128920 | `Everspace2.exe` | `ES2-Win64-Shipping.exe` |
| DQ7R | 2499860 | `DQ7R.exe` | `DQ7R-Win64-Shipping.exe` |
| OCTOPATH | 921570 | `Octopath_Traveler.exe` | `Octopath_Traveler-Win64-Shipping.exe` |
| Manor Lords | 1363080 | `ManorLords.exe` | `ManorLords-Win64-Shipping.exe` |
| STVoyager | 2643390 | `STVoyager.exe` | `STVoyagerSteam-Win64-Shipping.exe` |
| Satisfactory | 526870 | `FactoryGameSteam.exe` | `FactoryGameSteam-Win64-Shipping.exe` |

Steam libraries here: `C:\Program Files (x86)\Steam\steamapps` **and** `D:\SteamLibrary\steamapps`.

### ⚠ The trap that costs a whole session: a process that exists is not a game that BOOTED

Satisfactory's shipping exe **cannot** be launched directly — it puts up a modal
`Failed to open descriptor file ../../../FactoryGameSteam/FactoryGameSteam.uproject` **behind
everything**, because UE resolves the `.uproject` relative to the exe and this title's exe lives
under `Engine\Binaries\Win64\`. Steam supplies the working directory.

**What makes it dangerous is that nothing errors.** Against the never-booted engine the injection
succeeded, the pipe answered, GNames and GWorld *resolved* (exports work as soon as DLLs are mapped),
and the DLL reported `GObjects=0x0` and
`ExtraScanGObjects: No valid FUObjectArray found (763 candidates tested)`. A 20-minute run measured
a corpse. Relaunched with `steam.exe -applaunch 526870`: **all four globals, 137,425 objects**, with
`gobjects` at the exact address the failed run had rejected as empty.

**The tells, cheapest first:** an *empty* array behind a *resolved* symbol (`Num = 0 / -1`) means
"not initialised", not "wrong address" — a wrong address gives garbage, not zeros; `object_count == 0`
while GNames works; `docs/test-games.md` disagreeing; and **looking at the window**.

Other titles with the same shape: **STVoyager** and **EVERSPACE™** exit immediately on a direct
launch (Steam DRM wants the client). **DQ I&II** ships a top-level shim beside the real
`Game\Binaries\Win64\` exe. **OCTOPATH** relaunches itself unless started via `-applaunch`.

### Starting the UI

There is no launcher script for it — it is a plain exe, and the AV rule means **not** via PowerShell:

```bash
py -c "import subprocess,pathlib; subprocess.Popen([str(pathlib.Path('dist/UE5DumpUI.exe').resolve())], cwd='dist')"
```
```bash
py tools/verify/front_window.py front UE5DumpUI
```

Then click **Connect** (top-left) to attach to an injected game; the header goes
`Disconnected` → `Connected — UE504 (25,189 objects)`. ⚠ **The moment it connects it takes 2 of the
3 pipe slots**, so no `pipe_client.py` rig can run until you press **Disconnect** or close it.
⚠ It is a **single-instance** app (Mutex) — a second launch silently does nothing.
⚠ Many rows use the UI as their fixture, so this is usually step one, not an afterthought.

### DumperTest — the default fixture, never launched by hand

⛔ **USE `shipping` UNLESS THE ROW NEEDS OTHERWISE** (§4 rule 5). A Development build's offsets
are a minority shape in real games; `dev` is the other-direction check, not the default.

```bash
py tools/verify/launch_dumpertest.py shipping
```
```bash
py tools/verify/launch_dumpertest.py dev        # the SECOND opinion -- say so in the row
```
```bash
py tools/verify/inject.py --name DumperTest
```

`launch_dumpertest.py` pins the house settings in one place: `-windowed -ResX=1280 -ResY=720
-ExecCmds=t.MaxFPS 15` (the maintainer asked for 15 FPS so an all-night batch does not load the
machine — this PC also drives the game under test). ⚠ It is `t.MaxFPS`, **not** `-BENCHMARK -FPS=15`,
which would switch UE to a fixed timestep and silently change what every timing row measures. It
writes the PID to `out/host.pid` so the injector and the killer agree on one target, and it **fails
loudly** if the process died — a dead host makes every downstream "nothing found" meaningless.

`--idle` adds `-DumperTestIdle` and is **opt-in only**: B8's deferred half needs it, the D2
heartbeat row breaks with it, and it makes every game-thread dispatch time out while you work in the
dumper UI.

⚠ **DumperTest does not respawn its pawn.** A teleport to (0,0,0) in `ThirdPersonMap` drops it under
the floor, past KillZ, and it is destroyed; the next `Recall` returns `code -3` (`TP_ERR_NO_PAWN`) —
an honest error that reads exactly like "Save silently failed". Only relaunch + re-inject brings it
back. **Displace with `TP facing direction` (100 uu), never with absolute coordinates.**
⚠ Neither DumperTest package carries a proxy DLL, so its groups inject directly and **skip
`trigger_scan`**. Keep it that way.

### ⛔ PRECONDITION for every proxy-mode game row: refresh the proxies first

```bash
py tools/verify/proxy_refresh.py report
```

A proxy planted in a game before the last `dll/src` change loads the **old** DLL when the title
launches: you will reproduce a defect that is already fixed, and nothing in the logs will say which
build answered. (Measured 2026-08-22: 9 deployed proxies, all 9 STALE.)

```bash
py tools/verify/proxy_refresh.py refresh "<title substring>"
```

It refuses to refresh while a game is running and backs up with a SHA-256 first. ⚠ This does **not**
apply to DumperTest (no proxy — it is injected directly) and does not apply to a title you inject by
hand with `inject.py`, which loads `dist/UE5Dumper.dll` as it is on disk.
⚠ A republish of identical source also reads STALE (the comparison is byte-for-byte, and the build is
not reproducible). **The tell is that the sizes match exactly**: that one is a false alarm and is NOT
refreshed (working-lessons §3.x). A proxy of a different build — on 2026-08-22 it was real because
`dll/src` had changed — is refreshed without asking (the maintainer, 2026-09-29; working-lessons §7.3).

### Is it even installed?

```bash
py tools/verify/fixture_census.py
```

⛔ **A folder under `steamapps/common` is not an installed game — the census prints a `GHOSTS` list
of folders holding no executable at all** (read the tool; the list moves as titles come and go). On
2026-08-22 two of the ghosts were **FINAL FANTASY VII REBIRTH** and **Tower of Mask**, both cited in
`docs/` as the fixture an open row was waiting for. Run this before planning a session around a title.
⭐ And do **not** predict pool size from install size: OCTOPATH is 2 GB with 273,956 objects
(2026-08-18); Avowed is 64 GB with 92,036 at its main menu (2026-08-22) and 272,494 in the 2026-10-08
Live Funcs run.

-----

## 4. The rules that bound every session

1. ⛔ **NO PowerShell**, with `build.ps1` as the sole exception. Bitdefender's ATD flagged the chain
   `claude.exe → pwsh.exe → powershell.exe` and quarantined **six files**, one unrecoverable, then
   blocked the paths until reboot. Anything automated that creates or deletes goes through the Python
   tool. **Commit before executing anything new.** ⛔ Never `dist/inject-ue.ps1` — it also
   auto-elevates via `Start-Process -Verb RunAs`, a UAC dialog an unattended run can never answer.
2. ⛔ **One game at a time, sequential, never parallel.** DumperTest counts as a game. A long build or
   a full scan running *alongside* a game is the same violation — this PC over-loads.
3. ⛔ **Kill the game, CE and the UI the moment the row that needed them is done.** Confirm the
   process is actually gone before launching the next; a Steam title can leave its shipping exe alive
   after the window closes.
4. ⛔ **Never run `pipe_client.py` while the UI is connected.** `Fern::kMaxPipeInstances = 3` and the
   UI holds **2** lanes (`LaneRoutingPipeClient` opens an interactive `"I"` and a bulk `"B"`).
   Several rigs say so in their own docstrings; `pipebusy_capacity.py` is the one deliberate
   exception and opens all three itself.
5. ⛔ **SHIPPING IS THE FIXTURE. `dev` is a second opinion, not the default.**
   `py tools/verify/launch_dumpertest.py shipping` — maintainer's standing instruction,
   2026-09-09. **A Development build's offsets are a MINORITY shape in real games**: the layouts,
   the reflection data and the symbol surface a Development binary hands you are not what a
   shipped title hands you, so a row verified only on `dev` has been verified against the case we
   meet least. Development still earns its place — it is a check *in the other direction*
   (UCheatManager live, full logging, the diagnostics a Shipping build strips) — but it is the
   SECOND run, not the first. ⚠ A row closed on `dev` alone must SAY so; it is a narrower claim
   than one closed on `shipping`.
6. **A "not tested" is a legitimate, required result.** Recording one as PASS is the failure mode the
   whole register exists to avoid.
7. **Record every number with its conditions — including absences.**
8. ⚠ **Game windows steal focus.** `py tools/verify/front_window.py front <procname> ["<title>"]`
   before any click; it reports what actually ended up in front, because `SetForegroundWindow` fails
   silently.

-----

## 5. Running things

### Gates — every one, in CI's order

```bash
py tools/check_all.py
```

⚠ **All of them, not the few you remember.** `.github/workflows/ci.yml` runs every gate
`check_all.py --list` prints before the build (derive the count from the `N gate(s) run` line),
plus `check_proxy_exports --artifacts` over the built proxies; `check_ci_gate_parity` keeps the two
lists equal. On 2026-08-22 a session ran four of them all day, and the first full run **failed** on
`check_no_local_paths` over a test fixture committed hours earlier. Order matters — `aob_specificity`
reads the TSV `extract_patterns` writes.

### Tests

```bash
dotnet test ui/UE5DumpUI.Tests/UE5DumpUI.Tests.csproj -c Release
```

~36 s. ⚠ Do **not** pass `--nologo` or `-v`: this project runs Microsoft.Testing.Platform
and forwards them to xunit.v3, which prints help and reports `Zero tests ran`, **exit 5** — that reads
exactly like a broken test project. One class:
`-- --filter-class "UE5DumpUI.Tests.Xyz"`.

View-level behaviour on REAL Avalonia controls (a TextBox's caret, a list's selection) lives in a
second project on the headless platform — its own process, because a headless session replaces the
global UI Dispatcher the view-model tests must not share. `build.ps1` runs both.

```bash
dotnet test --project ui/UE5DumpUI.HeadlessTests/UE5DumpUI.HeadlessTests.csproj -c Release
```
⛔ **Never `build.ps1 -Target Test`** to run tests — it republishes `dist/` non-trimmed (CLAUDE.md
`## Build & Deploy`).

### Builds

What to build, which binary to hand over, the build-number bump, and why a plain `build.ps1` or
`-Target Test` is not read-only: CLAUDE.md `## Build & Deploy` and its build commands, the single copy.
What this machine adds:

```bash
py tools/verify/build_dll.py --targets UE5Dumper dll_helpers_test dll_core_test
```
```bash
powershell -NoProfile -ExecutionPolicy Bypass -File "D:\Github\UE5CEDumper\build.ps1" -Mode Publish
```

* ⚠ **A publish takes several minutes** (Native AOT links with MSVC). The Bash tool's default timeout
  is 120 s — **run it with `run_in_background: true`** or it is killed mid-link and reads as a build
  failure.
* ⚠ **A running `UE5DumpUI.exe`, or an injected game holding `dist\UE5Dumper.dll`, blocks the copy.**
  Kill both first. Since `[DISTCOPY-2026-08-22]` the failure is loud (per-file `copy failed`, a
  hash-mismatch verdict, exit 1, and `dist/publish/` **left in place** so the good binary survives) —
  but it is still a wasted build.
* ⚠ A hash confirms *this copy landed*, never that two builds are the same build: Native AOT is not
  byte-reproducible (working-lessons §2.5c).
* ⚠ Fresh clone only: `git submodule update --init vendor/minhook vendor/zydis`.
* ⚠ **Which `.cpp` files a test target compiles, and so what `-Target Test` cannot catch:** CLAUDE.md's
  "Run tests only" block, whose counts `check_derived_counts` pins. Build the DLL target
  (`build_dll.py --targets UE5Dumper`, or `-Target DLL`) before claiming a C++ change builds. Moving a
  rule into a header is how we pin it in a test; grep a target's include list before saying a fix
  cannot be tested.

### The pipe (headless, no UI)

`tools/verify/pipe_client.py` is the library the other pipe rigs import. It enforces the two traps itself:
`assert_build()` (a stale deployed **proxy** answers the pipe happily while the fresh DLL sits inert)
and `ensure_scanned()` (proxy mode starts the pipe **only**; `init` returns `ok:true` in ~0 ms having
scanned nothing, after which every pointer reads `not_found` and looks like a broken AOB table).

Other infrastructure worth knowing exists: `inject.py` (fails loudly at every Win32 step, and
**refuses** when one of our DLLs is already mapped — `LoadLibraryW` would hand back the old module
and nothing in the logs would say so), `mailbox_addr.py` (read-only `g_invokeMailbox` resolver, an
independent witness to CE's own `getAddress`), `mailbox_poke.py` (drives the CE mailbox with **no
Cheat Engine at all** — it is what turns "needs CE" rows into headless ones), `mutate_guard.py`
(restores mutated game memory in a `finally` with a verified read-back), `read_mem.py`,
`suspend.py` (per-**thread** suspend, because a whole-process suspend also stops the mailbox),
`front_window.py`, `fixture_census.py`, `build_dll.py`, `title_sweep.py`.
⚠ There is **no README in `tools/verify/`** — each file's own docstring is the documentation, and
they are long and self-critical. Read the docstring before running one; several have ordering
preconditions and a few refuse to run in the wrong state rather than degrading into a silent no-op.
⚠⚠ **`--help` is not safe across all of them.** Several parse `sys.argv` by hand, so
`py tools/verify/seethrough_arms.py --help` skips argparse entirely and immediately dials the pipe
(`PipeError: could not open \\.\pipe\UE5DumpBfx`). **Read the file, do not probe it.**

-----

## 6. Driving Cheat Engine

* ⛔ **`open_application` starts a NEW Cheat Engine every call** — CE does not single-instance itself.
  Use it once to start CE; front it afterwards with `py tools/verify/front_window.py front
  cheatengine-x86_64-SSE4-AVX2` (the measurement: `verification-register.md`, "open_application
  LAUNCHES A NEW CHEAT ENGINE EVERY CALL").
* The real process is **`cheatengine-x86_64-SSE4-AVX2.exe`** (`Cheat Engine.exe` is a shim). The
  AOBMaker plugin auto-loads from `plugins\AOBMaker_CEPlugin.dll` and writes `PipeServer: listening on
  \\.\pipe\AOBMakerCEBridge` to `%LOCALAPPDATA%\AOBMaker\CEPlugin.log`. ⚠ **The toolbar's "AOBMaker DLL" dot
  does not poll**: it re-probes on **⟳** and when an action uses the bridge, so press ⟳ AFTER that log line.
  The dot gates the per-row **Freeze** and the Live Walker's **INV**; a dot left grey from a probe made before
  CE started reads as a broken bridge, and on 2026-10-06 it cost a live step. With CE closed those buttons are
  present but dead.
* **Attaching**: process icon → pick the game → **Open** → answer **Yes** to *"Keep the current
  address list?"*, or you lose the record you just pushed. **Read the title bar before ticking** —
  the process list reorders between openings, and one attach in an earlier session landed on
  `UE5DumpUI.exe`.
* ⭐ **Drive records from the Lua Engine, not the checkbox.** `getAddressList()[i].Active = true`
  runs the `[ENABLE]` script and is readable. Reading state from the icon is unreliable — **a red ✗
  means ACTIVE**.
* ⭐ **When a record silently refuses to tick, run its own `[ENABLE]` text through `autoAssemble`.**
  A Lua **syntax error** is reported by CE as: `Active` stays `false`, **no dialog, nothing in the
  log**. That is exactly how `[INVOKEHINTQUOTE-2026-08-22]` hid — an unescaped apostrophe in
  `"read it in CE's memory viewer"` interpolated into a single-quoted Lua literal.
  ```lua
  local r = getAddressList()[0]
  local ok, err = autoAssemble(r.Script:match('%[ENABLE%](.-)%[DISABLE%]'))
  print(ok, err)
  ```
* Emitted scripts are quiet by design; `UE5_DEBUG = 1` in the Lua Engine turns the diagnostics on.
* ⚠ **Re-front the Lua Engine after every invoke-form FIRE** (measured 2026-09-15, L10). After a
  successful FIRE the Lua Engine was no longer where the next click aimed — cause not identified;
  the param form emits no `getLuaEngine().Close()` — and the click landed on the **address list**,
  where `ctrl+a` + `Delete` raised *"Do you want to delete the selected addresses?"*. Answer **No**.
  A keystroke chord typed into the wrong CE window is a destructive action there, not a no-op.
* ⚠ The CE address list and the Lua Engine are **separate top-level windows of one process** — pass
  the title filter to `front_window.py`.
* ⚠ **Tick an AUTO-CLOSING record from the address list, never from inside the Lua Engine**
  (measured 2026-09-16, L34). Every script `CeLuaHygiene` emits closes the Lua Engine on a clean
  exit, so ticking a record while that window has focus makes CE hand focus back to a window it
  just destroyed: `[TCustomForm.SetFocus] frmLuaEngine … Can not focus`. The script still ran —
  the exception is the DRIVER's, not the product's — but it eats a click and looks like a failure.
* ⭐ **Capture CE Lua output through `io.open`, not the output pane** (measured 2026-09-16, L88).
  The refusal texts this project cares about run to several wrapped lines in a narrow window, and
  reading them off a screenshot is how a quoted string gets silently paraphrased. Collect into a
  table and `f:write(table.concat(L,'\n'))` to the scratchpad, then `cat` it — the transcript is
  then byte-exact, and a Lua `error()` even carries its own `:line`, which pins WHICH branch spoke.
* ⚠ **A record you cannot see may still exist.** `scripts/UE5CEDumper.CT`'s real tick target,
  `Inject DLL + Start Pipe Server`, is a HIDDEN CHILD of `init` (`moHideChildren="1"`). Ticking
  `init` un-hides it **without activating it**. `getAddressList()` indexes hidden records
  normally, so enumerate the list before clicking rather than counting visible rows.

-----

## 7. What is open, and where to start

### The numbers, and how to derive them

```bash
py tools/check_audit_register.py --list          # audit #5 register (manual since 2026-09-03; no longer a gate)
grep -c '^> | `\[' docs/todo.md                  # OPEN FIXES INDEX rows -- the defect queue (still todo.md)
grep -c '^### ⬜ NEW DEFECT' docs/todo.md         # defect WRITE-UPS (not the queue!)
awk '/^## Pending live-game verification/,0' docs/verification-register.md \
  | awk '/^## /&&!/^## Pending live-game/{exit}1' | grep '^### ' | grep -c ⬜
```

⚠⚠ **The two defect greps answer different questions and the obvious one is wrong.** The
OPEN FIXES INDEX table near the top of `todo.md` sits **inside a blockquote** (`> ###`, `> |`), so it
is invisible to any `grep '^### '`; the `^### ⬜ NEW DEFECT` grep finds only the long write-ups.

Run the commands above for the counts; numbers written into this file go stale.

**Current work** is not written here: the current-programme line at the top of [`todo.md`](todo.md)
names it by tag. Each programme's live-check order and per-row status is in its live plan or build
ledger (indexed in [`README.md`](README.md)); what is shipped but not yet proven live is in
[`verification-register.md`](verification-register.md).

### Before planning off any row

Re-derive before planning — `grep` the row id across `docs/`, and read the ✅ block, not the ⬜
heading: closures are written where the work happened ([`working-lessons.md`](working-lessons.md)
§1.ab; §1.ab-2 for why a pointer file like this one rots fastest).

The checklist is [`pending-verification_zh-TW.md`](pending-verification_zh-TW.md). ⚠ **Two different
things are both called "step N"** and they are easy to transpose: `第 N 步` is a **bucket** (what the
row COSTS to run — 第 1 步 needs only the UI, 第 5 步 has no fixture anywhere), while "step 1 / step 2"
inside a row is a **sub-step**.

-----

## 8. Things that will mislead you

* ⭐⭐ **Demand a second, independent witness for any claim the system makes about itself** — the
  dominant defect shape here is the report and the reality computed by different code paths
  ([`working-lessons.md`](working-lessons.md) §1.4, §1.12).
* ⭐ **A detector must be shown able to FIRE before its silence means anything** (working-lessons
  §1.1) — and it must be right about the **format** of what it reads, not just the location.
  `walk_instance` renders a bit-field bool as `true (bit 7, mask 0x80)`, so `== "true"` reads a hidden
  actor as not hidden.
* ⚠ **A computer-use coordinate is a measurement and it expires** — re-read the control's position
  from a fresh screenshot before any click an assertion depends on, prove the click **landed**, and
  read a toggle's state back off the screen (working-lessons §2.5d).
* ⚠ **computer-use `type " "` — a lone space — is silently swallowed** (measured 2026-09-15, L10: a
  "space refused" test fired an EMPTY box and looked like a gate failure). Use `key space`, then
  prove the content before asserting on it: `shift+Home` must show a one-character selection, or
  type a visible character after it and check its indent.
* ⚠ **`find_instances` without `exact_match` is a NAME SUBSTRING match** (working-lessons §1.y).
* ⚠ **An old proxy reproduces a FIXED defect** — §3's proxy precondition: `proxy_refresh.py report`
  before every proxy-mode game row.
* ⚠ **Log-window measurement: use before/after COUNTS**, not line slices, byte offsets, or a timestamp
  watermark finer than its 1 s resolution allows (working-lessons §1.x).
* ⚠ **ProcessEvent vtable slots: the PATTERN SCAN is primary, the version table only the fallback** —
  how to read which one answered, and how to re-check a slot offline: working-lessons §4.7.
* ⚠ **Invoke order is `init → trigger_scan → invoke → pe_profile_start`.** Profiler-first used to
  poison the PE hook permanently.
* ⚠ **Elliot's PE hook is intermittent by title** ("sometimes yes, sometimes no"). Switch host to
  Lushfoil rather than retrying. Elliot also *detects* as UE 427 while really being 5.04 — honestly
  flagged `detected=no, lowConfidence=yes`, not a bug.
* ⚠ **OCTOPATH's `version.dll` proxy is silently bypassed** (only System32's VERSION.dll maps). Use
  `winmm.dll` or `dxgi.dll` there (dxgi crashed it until the fix in
  `docs/audit-2026-08-26-dxgi-appcompat-crash.md`). The honest check after launching any proxy title is
  whether `%LOCALAPPDATA%\UE5CEDumper\Logs\<ProcessName>\` was created at all.
* ⚠ **`Serie::GetString` drops the FName `Number`** unless its caller passes one. Measured 2026-08-20:
  40 of 42 objects were named differently by two pipe commands and 6 of 6 were unfindable by the name
  the DLL itself reports. Derive which call sites pass a Number with `grep -rn "GetString" dll/src/`
  rather than quoting a count (the 2026-08-20 figures, 71 sites and 4 passing a Number, no longer
  reproduce). ⛔ **Do not sed it** — display and identity are different jobs.

-----

## 9. How to close a verification row

Moved on 2026-10-08 to [`verification-register.md`](verification-register.md), "How to close a row",
which owns it. A programme's backlog row closes the same way, with its record in that programme's ledger.

-----

## 10. Machine-local, and the other PC

This is one of the two PCs — `%COMPUTERNAME%` says which. Things that are true only here:

* Hint cache `%LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.{COMPUTERNAME}.json` — the rigs derive
  that name from the environment (they used to hard-code it, which leaked the machine name
  into a public repo AND broke them on the other PC).
* Repo `D:\Github\UE5CEDumper`; Ghidra corpus `D:\UE_Analyze_data` (`$GHIDRA_PROJS` is *derived* —
  run `py tools/ghidra/preflight.py` first, and **never on USB**).
* `lua` 5.4.6 at `%LOCALAPPDATA%\Programs\Lua\bin\lua`; `scripts/tests/` rigs run the real CE helpers.
  Since 2026-09-25 they are gate `check_lua_suites`, on CE's own Lua VM (`out/ce_lua53`); where that host
  is not built -- CI included -- the gate reports SKIPPED, so a green CI run says nothing about them.
* CE source clone `D:\Github\cheat-engine` (tag **7.5**) vs installed **7.7** docs at
  `C:\Program Files\Cheat Engine\celua.txt` — **the public source lags the release**, so "absent from
  the source" does not prove "fixed in 7.7".
* Siblings: `D:\Github\AOBMaker` (the CE plugin), `D:\Github\CrimsonAtomtic` (AOT reference).
* **A local LLM, installed once per machine.** `py tools/llm/ollama_local.py status` says whether THIS
  PC has one (`ready`) or not (`disabled`: nothing to do). The installed copy, the model tag and the
  joined-repo list live in `%LOCALAPPDATA%\claude-local-llm\` and the game-VRAM hook in the USER-level
  `~/.claude/settings.json` (every repo's sessions), never in the repo. Installing, and how any other
  repo joins or leaves: [tools/llm/README.md](../tools/llm/README.md).

**Two-machine sync point: derive it, never quote it.** `git fetch`, compare with `origin/dev`, then
read [`dev-log.md`](dev-log.md) newest-first: a build number either PC has consumed is gone, never
reuse one. ⛔ **Build 3262 is BURNED** (it came to name three different binaries) and must not be
re-used as a version. When the other machine syncs, it needs
`git submodule update --init vendor/minhook vendor/zydis` and a fresh `-Mode Publish`; none of the
`dist/` artifacts travel.

⚠ **Skia/HarfBuzz are pinned to what Avalonia was built against** (3.119.4 / 8.3.1.3) and two guards
enforce it. **Do not "update all packages" past them** — that is what crashed the UI.
