# Dev Log

Append-only milestone history, newest first. Each entry references a
build number from `build_number.txt` so commits can be cross-referenced.
**Reading tip:** grep `^## ` for the index, then read the top (newest-first).
Entries for **builds ≤3261** are archived (update this number on every split: after the third one it
lagged three weeks). Builds 2779–3261 in
[archive/dev-log-2026-08-pre-build-3263.md](archive/dev-log-2026-08-pre-build-3263.md),
builds 2220–2747 in
[archive/dev-log-2026-08-pre-build-2779.md](archive/dev-log-2026-08-pre-build-2779.md),
builds 1800–2168 in
[archive/dev-log-2026-07-pre-build-2200.md](archive/dev-log-2026-07-pre-build-2200.md),
builds 1178–1799 in
[archive/dev-log-2026-06-pre-build-1800.md](archive/dev-log-2026-06-pre-build-1800.md),
builds 939–1177 in
[archive/dev-log-2026-06-pre-build-1180.md](archive/dev-log-2026-06-pre-build-1180.md),
builds 715–937 in
[archive/dev-log-2026-06-pre-build-940.md](archive/dev-log-2026-06-pre-build-940.md),
builds ≤696 in
[archive/dev-log-2026-05-pre-build-700.md](archive/dev-log-2026-05-pre-build-700.md).

> **Looking for current state?** See [roadmap.md](roadmap.md) for the
> capability matrix / per-game configuration / tested games, and
> [todo.md](todo.md) for the prioritized next-work list. This file
> records *what shipped* — the other two record *what works now* and
> *what's next*.

-----

## 2026-09-26 (build 3559) — SCAN-EARLY: the multi-module scan pins each module; skeptic rounds 9-11; S5 closed, published AOT

**3559 = 3558 plus one DLL fix, three review rounds' fixes and the S5 live pass, up to `7d2b2fcb`.**
- **`[SCAN-EARLY-TRIGGER-CONTAINED]`, found during tonight's live pass, now fixed (DLL).**
  - **Cause.** A `trigger_scan` sent ~1 s after launch died with `RunScan: UNCAUGHT non-standard exception —
    contained`. `Macht::AOBScanAllModules` snapshots `EnumProcessModules`, then read each module's headers and code
    with no reference on it. A DLL the booting engine freed in between was read after it was unmapped.
  - **Fix.** `AOBScanAll` now pins any non-exe module with `GetModuleHandleExW` for the length of its scan, and skips
    a module that is already gone.
  - **Tests.** Red→green in `dll_core_test` (a freed System32 DLL). A test-only seam frees a module mid-scan to prove
    the pin holds the image and is released exactly once. Rounds 10-11 hardened this case against three pin mutations.
  - **Live** (`tools/verify/scan_early_live.py`, five launches each, trigger at 0.8 s):
    - red on 3558: 0/5 clean;
    - green on a tree-built proxy: 5/5;
    - green on this published build's proxy (`618fcc388e66`): 5/5.
    - GObjects is now found on every early launch, so the row's "the scan finds nothing that early" half was the fault
      itself.
- **Skeptic round 9** (6 findings) — **1 MED:** R9-01, the Snapshot row floors pushed an opened Noise picker below the
  window (measured: 299 DIP of overflow). They are now capped at the room the Auto rows leave, on every layout pass.
  - **1 LOW:** the twin rig refused relative paths.
  - **4 INFO:**
    - the Load column is 280 px for its longest text, "shared · stale · loaded <date>";
    - a stale comment;
    - the layout pins strengthened;
    - the TextBox clipboard read re-taken against Avalonia 12.1.3.
- **Round 10** (7 raised, 5 confirmed, 2 refuted) and **round 11** (4 confirmed): all fixes in tests, comments and the
  SCAN-EARLY rig. The rig now checks the proxy's SHA before every launch.
- **S5 live (Proxy Deploy, 3558):** rows 14-18 all PASS. The fixture was hosted in the repo's `EVERSPACE™ 2` shape and
  found by Scan drives; row 18's second folder came from `tools/verify/shared_exe_twin.py`. **All 18 path-shape rows
  are closed.** Also live PASS: `[PATH-UI-LEGACY-QMARK]` (the 3558 UI against a 3553 proxy that sends `?` names).
- **Found and handed to the maintainer:**
  - `[UI-TOOLTIP-RIGHT-THIRD]`: no tooltip past ~1138 DIP on the 4K-at-225 % screen, on 3557 and 3558 alike. It needs a
    real-mouse check.
  - `[PATH-METHODE-NO8DOT3]`'s live step: registering a CE plugin was not done unattended.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3558 → 3559.
- `dist\UE5DumpUI.exe`: AOT, 58,177,024 bytes (sha `58dfbe42ceaf`).
- `UE5Dumper.dll`: 3,015,680 bytes (sha `c55a879b5b01`), FileVersion `1.0.0.3559`.
- Proxies: version `618fcc388e66`, dinput8 `cbe1dbde4ed6`, dxgi `71ae9a48a21b`, winmm `69d05ae3164e`.
- Tests: UI 5596/5596. `dll_core_test` 467, `dll_helpers_test` 2962, `utf8_helpers_test` 273,
  `sein_retention_test` 30, `grausam_window_test` 22.
- Gates: 26/26.
- Checked on screen (AOT, 225 %): with the Snapshot Noise picker open, its buttons are inside the window.

## 2026-09-25 (build 3558) — R8-01, NuGet upgrades, and the 4K-at-225 % layout pass, published AOT

**3558 = 3557 plus four things, up to `5191b39c`.** It has no DLL or `.CT` code change; the DLL differs only in the
build stamp.
- **Round 8 (`wf_41d4c1ab-5e4`): 1 finding, INFO.** `[R8-01]`: the seventh round widened the Load column for a font it
  never gets. A DataGrid cell is Fluent's 15 px Inter with 12 px margins, not the grid's 12 px, so 210 px still cut
  `shared · loaded 2026-09-25`, and a trailing `(stale)` never showed at all. Now the marks lead
  (`stale · loaded <date>`), the column is 240 px, and the header tooltip explains both marks.
- **NuGet (maintainer's request, evaluated one by one):**
  - **Avalonia 12.1.1 → 12.1.3** (Win32, Skia, HarfBuzz, Themes.Fluent, Fonts.Inter). Avalonia.Skia 12.1.3 and
    Avalonia.HarfBuzz 12.1.3 still declare **SkiaSharp 3.119.4 / HarfBuzzSharp 8.3.1.3** (read from their nuspecs).
    So `<SkiaSharpVersion>` / `<HarfBuzzSharpVersion>` do not move: SkiaSharp 4.x / HarfBuzzSharp 14.x are not what
    this Avalonia is built against (the ABI crash of the archived todo). `Avalonia.Controls.DataGrid` stays at 12.1.2,
    the newest published.
  - **xunit.v3 3.2.2 → 4.0.1, xunit.runner.visualstudio 3.1.5 → 4.0.0, Microsoft.NET.Test.Sdk 18.8.1 → 18.10.1.**
    xunit.v3 4 drops Microsoft Testing Platform v1, so the graph moves from `mtp-v1` / MTP 1.9.1 to `mtp-v2` /
    MTP 2.4.0, still transitive. The forbidden-pin guard is unchanged. Nothing here uses what 4.0 broke: no orderers,
    no `CollectionBehavior` parallel properties, no `-report-*` switches. xunit.analyzers 2.1.0 raised no new
    warning, and `--filter-class` still works.
- **`[UI-SPACE-2026-09-25]` (maintainer's request).** Measured on the maintainer's 3840x2400 laptop at 225 %
  (1707x1067 DIP), build 3557:
  - **Main tab strip.** It wrapped to three 48 DIP rows. It is now 18 px headers in 36 DIP rows: two rows.
  - **Snapshot.** The diff grid had only its header. Both grid rows now carry a MinHeight, the auto-snapshot hint is
    a tooltip, and the saved header and the usage row share one wrapping row. On screen: the saved list ~4 rows, the
    diff grid ~3.
  - **Class Pivot.** The field picker and the results were below the window. Now the three paragraphs are one trimmed
    line each, with the full text on hover. The limits box opens from a toggle. Discover and the pivot target scroll
    under a cap of half the panel's height. On screen, both work grids show.
  - Pinned by `PanelSpaceBudgetTests` (red `f0df71a8`). Checked on screen from a staged build before publishing.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3557 → 3558.
- `dist\UE5DumpUI.exe`: AOT, 58,174,976 bytes (sha `a6ca83e47cad`).
- `UE5Dumper.dll`: 3,015,680 bytes (sha `c5fe3f2fbea6`), FileVersion `1.0.0.3558`.
- Proxies: version `33c4372d300d`, dinput8 `62c2086159f4`, dxgi `b9b263781dcb`, winmm `1f4750d7e27b`.
- Tests: UI 5594/5594. `utf8_helpers_test`, `dll_helpers_test` passed; `dll_core_test` 455, `sein_retention_test` 30,
  `grausam_window_test` 22.
- Gates: 26/26.

## 2026-09-25 (build 3557) — PATH-SHAPE: the seventh skeptic round's fixes, published AOT

**3557 = 3556 plus the seventh skeptic round's fixes, up to `45fd9a9c`.** It has no DLL or `.CT` code change; its
DLL differs only in the build stamp.
- **Round 7:** 6 findings, all INFO, nothing LOW or above.
  - **R7-01 / R7-02, R7-03 / R7-04:** test pins, each proven by mutation. The breadcrumb call is pinned
    comment-proof. The older-entry loop and a comment-only `dll-path.txt` are pinned. The drive-root fix is pinned on
    the writer's own line and in lower case, on both sides.
  - **R7-05:** the Proxy Deploy panel's Load column widened from 150 to 210 px, so a shared row shows its date.
  - **R7-06:** the Suggested column's tooltip now follows `ProxyImportAnalyzer.Recommend`'s real order.
- **Live so far (plan `docs/path-shape-live-plan.md`):**
  - S1: 15/15 on 3555.
  - S2: `[PATH-ES2-CE-SYMBOLS]` answered on 3556, not a finding.
  - S3 #6: the AOBMaker plugin matches the `遊戲` exe by name.
  - S3 #10: the confirmed record uses the real name.
  - Still open: S3's UI-side items and S5.
- **Also:**
  - A peer session added the `local-llm` skill and `tools/llm/ollama_local.py`. Its selftest is gate 26, in
    `check_all` and CI.
  - This session reviewed it (sound) and used it. It summarised the 63 KB archived scan log of an early
    `trigger_scan` for `[SCAN-EARLY-TRIGGER-CONTAINED]`, and the lead was checked against the raw log.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3556 → 3557.
- `dist\UE5DumpUI.exe`: AOT, 58,004,992 bytes (sha `ffcb286e1abd`).
- `UE5Dumper.dll`: 3,015,680 bytes (sha `2f70377c3aaa`), FileVersion `1.0.0.3557`, from `45fd9a9c-dirty`.
- Proxies: version `c965b79bc80b`, dinput8 `918c80f4392a`, dxgi `e2ccfe7656c7`, winmm `e9ad326eddd4`.
- Tests: UI 5588/5588. `dll_helpers_test` 2962/0, `utf8_helpers_test` 273/0, `dll_core_test` 455, `sein_retention_test`
  30, `grausam_window_test` 22. All 11 Lua suites pass on CE's VM.
- Gates: 26/26.

## 2026-09-25 (build 3556) — PATH-SHAPE: the sixth skeptic round's fixes, published AOT

**3556 = 3555 plus the sixth skeptic round's fixes, up to `9aa99ca2`.** The DLL's code is unchanged from 3555; only its
build stamp differs. So the live checks S1 passed on 3555 (`module_name`, `ce_base`, the log folder) stand for the DLL.
The changes are to the `.CT` and the UI:
- **`[R6-01]`, LOW** (a regression from R5-04, measured by the reviewer on CE's VM): a DLL folder at a drive root
  (`E:\`) was written to `dll-path.txt` as a bare `E:`, which every reader dropped as relative. Both writers now keep
  the root separator, and both readers read a legacy bare `X:` as that drive's root.
- **`[R6-02]` / `[R6-06]`:** the breadcrumb slot is now a function the Lua suite runs. It says "holds only relative
  folders" when every recorded line was dropped.
- **`[R6-03]` / `[R6-05]`:** the `shared · ` ambiguity tag now LEADS the Proxy Deploy panel's Load and Suggested texts.
  A trailing mark was clipped by the columns' width. The two column headers carry a tooltip saying what the tag means.
  The Load tag follows the log folder, not only the exe name.
- **`[R6-04]`:** a negative control.
- Also since 3555: `[SCAN-EARLY-TRIGGER-CONTAINED]` is recorded (a `trigger_scan` during engine boot is contained but
  not retried). The live-check rig now waits for the engine to boot.
- A seventh skeptic round, over these fixes, is running.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3555 → 3556.
- `dist\UE5DumpUI.exe`: AOT, 58,004,992 bytes (sha `7d6dfbcbe87d`).
- `UE5Dumper.dll`: 3,015,680 bytes (sha `cb4031cc0917`), FileVersion `1.0.0.3556`, from `9aa99ca2-dirty`.
- Proxies: version `bc599b640d6b`, dinput8 `1cb6ed3486b3`, dxgi `1ddf3d8b803a`, winmm `bb659306e0af`.
- Tests: UI 5587/5587. `dll_helpers_test` 2962/0, `utf8_helpers_test` 273/0, `dll_core_test` 455, `sein_retention_test`
  30, `grausam_window_test` 22. All 11 Lua suites pass on CE's VM.
- Gates: 25/25.

## 2026-09-25 (build 3555) — PATH-SHAPE: non-ASCII / multi-space / special-character paths and exe names, five skeptic rounds, published AOT

**3555 = 3554 plus 49 product commits, up to `7bcab1ba`** (`git log --oneline 641ea268..7bcab1ba -- dll/src
ui/UE5DumpUI scripts`; 131 commits in all). The rows are in `docs/todo.md` under `[PATH-SHAPE-2026-09-25]`.
- **Maintainer request.** While fixing `[PROXY-CONFIRM-EXE-KEY]` (a non-ASCII exe name), also check the path shapes on
  this PC: a non-ASCII folder (`EVERSPACE™ 2`), two spaces in a row (`DragonSword  Awakening`), special-but-legal
  characters (`No Man's Sky`). A 6-agent read-only workflow mapped them into 18 rows. Each row was fixed red → green,
  one commit per row.
- **The two measured facts behind the fixes.**
  - CE never sees a module's Unicode name. It sees the ANSI `Module32First` name: best-fit narrowing, then cut after
    the last 0x5C byte, so Big5 功 = A5 5C and `功夫-…` is `夫-…` to CE. CE-facing strings now use that view (the UI's
    `ISystemCodePage.AnsiModuleName`, the DLL's `Methode::CeModuleNameUtf8`), while keys use the real UTF-8 name
    (`module_name`).
  - CE's `injectDLL` hands the path's bytes to `LoadLibraryA`. The generated scripts, the `.CT` and the CE plugin now
    use, in order:
    1. an ASCII path as it is;
    2. an ASCII 8.3 alias of the FOLDER, keeping the DLL's own name (a file alias renames the loaded module to
       `UE5DUM~1.DLL`);
    3. the exact ANSI narrowing (never best fit);
    4. the alias's exact narrowing;
    5. otherwise a refusal that says why.
    Before, CE silently manual-mapped the DLL (no TLS, no `.pdata`).
- **Also fixed:**
  - a log folder for a stem ending in a space or dots;
  - an apostrophe in the trainer's Lua;
  - `&` in CE XML;
  - braces in Serilog output;
  - the PS1's elevation quoting and `-LiteralPath`;
  - the `.CT`'s recent-files split on `\0`, which read reg.exe output as UTF-8;
  - a proxy-named file we cannot read is now a third state, UNREADABLE (maintainer's call): never written or
    deleted, and said once;
  - the one-shot import-risk note outlives the refresh;
  - `[CI-GATE-DRIFT-2026-09-25]`: nine gates never reached CI; a parity gate, `check_ci_gate_parity`, now requires
    each CI gate line's exit check;
  - gate 25, `check_lua_suites`, runs the Lua suites on CE's own VM, and a skipped gate is counted as skipped.
- **Maintainer's decisions, 2026-09-25:**
  - `[PROXY-CONFIRM-SHARED-EXE]`: mark it ambiguous. An exe name that games in different folders ship uses neither
    exe-keyed record (confirmed, injected), and the Load column says the load may be another game's.
  - `[PATH-AOBMAKER-ANSI-MATCH]`: fixed in AOBMaker's own session (`ce8247c`); the deployed plugin is that build.
  - Path shapes come from a committed script inside the repo: `tools/verify/path_shape_folders.py` builds 10 folders
    under `out/pathshape/`, including the maintainer's letterlike-symbol folder in both spellings, and the unit tests
    pin them.
- **Five skeptic rounds over the fixes:** 24, 27, 9, 7 and 13 findings. Every one was fixed, a test gap's red measured
  by mutation. One HIGH, a regression from our own fix: the 8.3 FILE alias renamed the loaded DLL (`bc21def8`). A
  sixth round, over the fifth's fixes, is running.
- **Not yet run on a game:** the live checks, in `docs/path-shape-live-plan.md` (5 sessions, 18 checks), together with
  build 3554's `[PROXY-DEPLOY-UX]` checks.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3554 → 3555.
- `dist\UE5DumpUI.exe`: AOT, 55.3 MB (58,001,920 bytes, sha `9654a38bf11a`).
- `UE5Dumper.dll`: 3,015,680 bytes, sha `0689f44d3e62`, FileVersion `1.0.0.3555`, built from `7bcab1ba-dirty` (the
  dirty suffix is the bumped `build_number.txt`).
- Proxies in `dist\proxy\`: version `80c7ec21eb6a`, dinput8 `33baedb17bfb`, dxgi `cda5136f3043`, winmm `920ac9b05a08`.
- Tests: UI **5585/5585**. `dll_helpers_test` 2962/0, `utf8_helpers_test` 273/0, `dll_core_test` 455 checks, `sein_retention_test` 30,
  `grausam_window_test` 22. All 11 Lua suites pass on CE's VM.
- Gates: 25/25.

## 2026-09-25 (build 3554) — Proxy Deploy: never a second of our proxies; "Use confirmed-working proxy", published AOT

**3554 = 3553 plus 4 product commits, up to `64dcd886`** (`git log --oneline 55327e9d..64dcd886 -- dll/src
ui/UE5DumpUI scripts`). The rows are in `docs/todo.md` under `[PROXY-DEPLOY-UX-2026-09-25]`.
- **Maintainer requests, two.** (1) Deploying one type across many games put a SECOND proxy of ours into every game
  that already carried another type. (2) A game whose confirmed-working proxy is known, but whose folder is clean
  again (a reinstall), should get that type whatever the radio says.
- **Evaluation.** A 5-agent read-only workflow (`wf_b499f5ad-c49`: two maps, two designs, a judge that checked both
  against the source) found both worth building, as one design. Two things weighed:
  - At runtime the first-loaded proxy wins Heiter's mutex, and a double leaves no log trace.
  - Undeploy removes BOTH proxies, so a user repairing a double can delete the one that worked.
- **`[PROXY-DOUBLE-GUARD]` (`f4bf14ed`, `22083ab8`).** An always-on guard, with no checkbox.
  - A folder that already holds another of OUR proxies (by PE ProductName) is skipped by Deploy, even with Force
    Overwrite and even with foreign consent.
  - The same type still follows the Force rules.
  - The row says why: "Skipped: winmm.dll (ours) is already deployed here — Deploy never adds a second of our
    proxies. Undeploy first to switch type." The result line counts `skipped: N`.
  - `PlanDeploy` has a new `OtherProxyOfOurs` verdict, and `DeployAsync` refuses too, for any caller.
  - Update All is unchanged: it only rewrites types already there.
- **`[PROXY-USE-CONFIRMED]` (`7376ae05`).** A new checkbox, "Use confirmed-working proxy", which is not persisted.
  - For a ticked game with NONE of our proxies and a confirmed record, Deploy writes the recorded type.
  - Foreign consent is never used for it.
  - The row says "Deployed winmm.dll (confirmed working) instead of version.dll". The result line counts `confirmed
    type used: N`.
- **Review (`wf_c116385e-ad2`, 4 lenses; file safety found nothing), fixed in `0ef097ec`, red `3f306407`.**
  - Skipping an already-doubled folder had wiped the refresh's "Multiple proxy DLLs deployed" warning. It is kept now.
  - A foreign file at the confirmed type's name failed with a message that named neither type. It now says so.
  - A switched row's failure now says the confirmed type failed.
  - The checkbox tooltip over-claimed, and is now narrower.
  - Five test gaps closed, the service backstop through the real `DeployAsync` among them.
- **Not yet run on a game:** the live check, recorded as pending on both rows.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3553 → 3554.
- `dist\UE5DumpUI.exe`: AOT, 55.2 MB (57,897,984 bytes, sha `767e52a6098b`).
- `UE5Dumper.dll`: 3,008,000 bytes, sha `8c3a7fbd6b24`, FileVersion `1.0.0.3554`, built from `64dcd886-dirty` (the
  dirty suffix is the bumped `build_number.txt`).
- Four proxies went to `dist\proxy\`.
- Tests: UI **5430/5430**. `dll_helpers_test` and `utf8_helpers_test` pass. `dll_core_test` 455 checks,
  `grausam_window_test` 22 and `sein_retention_test` 14 checks.
- Gates: 23/23.

-----

## 2026-09-25 (build 3553) — Proxy Deploy: Update All honours Force Overwrite; Deploy stops claiming no-op writes, published AOT

**3553 = 3552 plus 3 product commits, up to `55327e9d`** (`git log --oneline 9019eada..55327e9d -- dll/src
ui/UE5DumpUI scripts`). The rows are in `docs/todo.md` under `[PROXY-FORCE-UPDATEALL-2026-09-25]`.
- **Reported by the maintainer.** With Force Overwrite ticked, Update All said "All N deployed proxy DLL(s) already
  up-to-date" and wrote nothing. So a rebuild that kept its build number (the proxy's FileVersion is only
  `1.0.0.<build_number>`) was never redeployed, which is exactly what happened on the other PC.
- **Cause.** `UpdateAllAsync` ran its own FileVersion check and never read the checkbox. The Deploy button already
  passed it.
- **The fix (`d5f60f09`).** Update All skips a same-version proxy only when Force is off. The checkbox is read once per
  run. The result line says what Force did: `Updated: N (M rewritten at the same version — Force Overwrite)`.
  - There is no hash or timestamp check: the maintainer's call, now settled in working-lessons §6.
  - AC1 is untouched: only a proxy already deployed AND ours is written, and foreign consent is never passed.
  - Both tooltips state the rule.
- **Mapping.** A 3-agent read-only workflow mapped every same-version skip and everything that pins or describes one.
  Only this one path broke the rule. The archived AC1 live check had recorded this very symptom as a PASS.
- **Three skeptic lenses over the fix** found no defect. Their test gap, pinning the `ForceSameVersion: true` the forced
  path now relies on, is closed in `8039ab4b`.
- **The adjacent row they found, `[PROXY-DEPLOY-NOOP-COUNT]` (`0aee3925`, `bd8869cf`).** With Force off, Deploy
  onto our proxy already at the source's version wrote nothing (the service answers AlreadyCurrent), yet showed a
  green `Deployed: 1 success`. It now counts that as `already current: N (tick Force Overwrite to rewrite them)`, in
  neutral colour, as Update All does. Two more skeptics found behaviour parity with the service case by case.
- ⚠ **Side effect, intended.** Force Overwrite is persisted, so a tick left from an earlier session now makes every
  Update All rewrite every deployed proxy of ours. A running game's proxy then fails as "Target in use" instead of
  being skipped silently.
- **Not yet run on a game:** the live check, recorded as pending on both rows.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3552 → 3553: `dist\UE5DumpUI.exe` AOT 55.1 MB
(57,810,432 bytes, sha `8ba915f779ed`), `UE5Dumper.dll` 3,008,000 bytes, sha `cef9994d70f5`, stamp `1.0.0.3553
55327e9d-dirty` (the dirty suffix is the bumped `build_number.txt`). Four proxies went to `dist\proxy\`
(`check_proxy_exports --artifacts` OK). Tests: UI **5397/5397**, `dll_helpers_test` 2921/0, `utf8_helpers_test`
265/0, `dll_core_test` 455 checks, `grausam_window_test` 22 and `sein_retention_test` 14 checks. Gates 23/23.

-----

## 2026-09-25 (build 3552) — R7-X8: Instance Finder's truncation advice follows every container, published AOT

**3552 = 3551 plus 2 product commits, up to `9019eada`** (`git log --oneline c6d667ec..9019eada -- dll/src
ui/UE5DumpUI scripts`). The row is `docs/todo.md` `[R7-X8]`.
- **Found live on 3551, by R7-S11's finding probe.** DumperTest's NestedBag had 16,000 pairs at Array Limit 16384.
  The copy truncated with "no toolbar setting shrinks this export", yet the same copy at 8192 was complete (49,738
  entries). R7-S6 named the limit only for a container bound by the CURRENT limit. But lowering the limit shrinks
  every container longer than the new one.
- **The advice now names the Array Limit** whenever a walked container has more elements than the slider's floor
  (`Constants.MinArrayLimit` = 2), largest first. It keeps "… or use Open in Live Walker → Copy CE Field for the part
  you need" beside it, because shrinking is not fitting.
- **Two more containers stop counting:** a sparse delegate and a `TArray<TFieldPath>`. Each is written as one entry
  at any length, so it is neither a lever nor a clipped export.
- **One skeptic reviewed the fix.** It found no defect; its two notes are the follow-up `abf9991e`.
- **Not yet run on a game:** the NestedBag copy at 16,000 / 16384 must now name the Array Limit (the row's live
  check).
- Also since 3551, docs only: the live PASS records for X5 and X6 on 3551. X6's NestedBag export stops at 60,001
  entries, marked TRUNCATED (3550 copied 98,890 unflagged). X5: a 1.4 invoke helper replaces a resident 1.3 in the same
  CE process, and R7-S3's re-inject then passes.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3551 → 3552: `dist\UE5DumpUI.exe` AOT 55.1 MB
(57,807,360 bytes, sha `479735bbe97c`), `UE5Dumper.dll` 3,008,000 bytes, sha `05ce27367fca`, stamp `1.0.0.3552
9019eada-dirty` (the dirty suffix is the bumped `build_number.txt`). Four proxies went to `dist\proxy\`
(`check_proxy_exports --artifacts` OK). Tests: UI **5389/5389**, `dll_helpers_test` 2921/0, `utf8_helpers_test`
265/0, `dll_core_test` 455 checks, `grausam_window_test` 22 and `sein_retention_test` 14 checks. Gates 23/23.

-----

## 2026-09-25 (build 3551) — Review 7's live pass: every live row PASS, and four more rows fixed, published AOT

**3551 = 3550 plus 6 product commits, up to `c6d667ec`.** Derive them: `git log --oneline 1c514220..c6d667ec
-- dll/src ui/UE5DumpUI scripts`. Rows, recipes and evidence: `docs/todo.md` `[REVIEW7-2026-09-24]` and
`docs/review7-live-plan.md` (sessions 1-26).
- **The live pass.** Every Review 7 row with a live check is a live PASS red→green, recorded on its own row. The
  reds came from staged pre-fix DLLs and pre-fix UIs identified by SHA. A few are partial, and each says which arm
  stays unit-only. The plan's own errors were corrected in place: D-07 / S10 need a pointer array; S12's own-probe
  arm does not discriminate; S11's truncated arms wait on X6.
- **Four more rows, all fixed here** (X4-X6 from the live pass, X7 from its skeptic):
  - `[R7-X4]` (MED). A stock UE 5.3 title was reported as 5.4. The marker is now CMC's `SetGravityDirection`
    UFUNCTION, not the `GravityDirection` property that 5.3 already reflects. Live on stock 5.3: 503, and
    R7-B-01's `[garbage]` tag now reaches it. DragonSword and Avowed are 5.3.
  - `[R7-X5]`. The CE invoke helper kept the first copy a CE session loaded; R7-S3's latch fix never reached a
    table opened earlier. It is now version-gated like the freeze helper (1.4).
  - `[R7-X6]`. Eight container element loops ignored the CE XML export's 60,000-entry ceiling. A map emitted
    last copied 98,890 entries unflagged.
  - `[R7-X7]`, text only. The Gravity Direction card said "UE5.4+". Measured, stock 5.3 honours the field.
- **One skeptic over X4-X6:** no defect in the fixes. Its X4-adjacent note became X7 after the measurement, and its
  three stale comments were fixed.
- **Not yet run on a game:** X5 (a 1.3 helper, then 1.4, in one CE process) and X6 (the NestedBag export must now read
  TRUNCATED, which is also R7-S11's F-20000 arm). Both are recorded as pending on their rows.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3550 → 3551: `dist\UE5DumpUI.exe` AOT 55.1 MB
(57,773,568 bytes, sha `8d0322b5a355`), `UE5Dumper.dll` 3,008,000 bytes, sha `25bb1f2878fe`, stamp `1.0.0.3551
c6d667ec-dirty`. The suffix is the bumped `build_number.txt` and nothing else. Four proxies went to `dist\proxy\`
(`check_proxy_exports --artifacts` OK). Tests: UI **5385/5385**, `dll_helpers_test` 2921/0, `utf8_helpers_test`
265/0, `dll_core_test` 455 checks, `grausam_window_test` 22 and `sein_retention_test` 14 checks. The Lua suites
pass 10/10 on CE's own Lua 5.3 VM. Gates 23/23.

-----

## 2026-09-25 (build 3550) — Review 7: the code since Review 6, fixed at every tier, published AOT

**3550 = 3549 plus 32 product commits, up to `1c514220`.** Derive them: `git log --oneline c88ca562..1c514220
-- dll/src ui/UE5DumpUI scripts`. Every row, test and piece of live evidence is in `docs/todo.md`
`[REVIEW7-2026-09-24]`: 35 fixed rows, one per commit, red before green where testable.
- **The review.** Four area finders and four skeptics over the code added since Review 6 raised 20 rows and
  refuted one: 2 MED, 12 LOW, 4 INFO, all fixed. Among them: UE 5.0-5.3's default PendingKill state is tagged
  `[garbage]` like 5.4's; the compact-set guard reaches the sparse-delegate readers; `apply_rescan` re-initialises
  under the init fence; the Fly / KeepForeground / SeeThrough scripts no longer pop a modal on an untick; a CSX
  FString child's byte size is in bytes.
- **Seven skeptic rounds over the fixes themselves** added R7-S1..S14. The Lua freeze helper no longer wedges the
  shared mailbox latch after a re-inject, and the mailbox init check reads the fence flag last. Instance Finder's
  truncation notice is now derived from the walk; this closes `[INSTEXPORT-TRUNC-ADVICE]`, which R7-D-03 had first
  fixed the wrong way. Its export also no longer pairs one walk's rows with another instance or limit. Every
  AOBMaker "not reachable" line follows the latest probe's reason. The last round found the final fix correct;
  its two small notes (an unexercised guard, a refusal's wording) were closed in a follow-up.
- **The live regression found two more** (R7-X2, R7-X3). Find References now reports an unlocated sparse storage
  as skipped. The storage validator accepts a vtable in any mapped module, not only the main exe. Live on the UE
  4.27 editor `-game`: before the fix, `sparse_delegates` was 0x0 and the storage was rejected. After it, the storage
  is located, the `CollisionCylinder` bindings decode as `[CharMoveComp::PhysicsVolumeChanged]` /
  `[CharMoveComp::CapsuleTouched]`, and Find References on the live `CharMoveComp` returns both. DumperTest 5.4
  Shipping `fixture_smoke` against 3549, on the Review 7 DLL before X2/X3: 0 differences.
- **Not yet run on a game:** the Instance Finder advice (R7-S6 / S11 / S13 / S14; the L63 rig). The DumperTest
  "±2 GB" comments wait for its next repackage.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3549 → 3550: `dist\UE5DumpUI.exe` AOT 55.1 MB
(57,771,520 bytes, sha `133c75b34b1b`), `UE5Dumper.dll` sha `a32cb03df296`, stamp `1.0.0.3550 1c514220-dirty`.
The suffix is the bumped `build_number.txt` and nothing else. Four proxies went to `dist\proxy\`
(`check_proxy_exports --artifacts` OK). Tests: UI **5375/5375**, `dll_helpers_test` 2913/0, `utf8_helpers_test`
265/0, `dll_core_test` 455 checks, and `grausam_window_test` / `sein_retention_test` passed. Gates 23/23.

-----

## 2026-09-24 (build 3549) — weak Force-null, and the case-preserving-name bundle (VND583-07) measured on two editor hosts

**3549 = 3548 plus four product commits, up to `c88ca562`.** Derive them: `git log --oneline 26993098..c88ca562
-- dll ui`. The row text, tests and live evidence are in `docs/todo.md` (`[VND583-DOC]` D7-04 and `[VND583-07]`).
- **Force-null holds a weak pointer** (`c181d742`, D7-04). `{0, 0}` is UE's own null, not "GObjects[0]", so an
  8-byte WeakObjectProperty is now held at UE's reset value: `{INDEX_NONE, 0}` up to 5.0, `{0, 0}` from 5.1.
  Soft and lazy pointers, and the 16-byte remote-handle weak pointer, are still refused. The UI's Force-null
  button covers the weak type. Live on DumperTest 5.4 Shipping with `-DumperTestWeakGarbage`: 3548 answers -12;
  the new build holds `WeakToGarbage` at null while the fixture re-points it every 5 s.
- **A case-preserving host that resolves** (`d936681f`). The UE 5.4 editor running DumperTest `-game`: the
  exported `FName::ToString` reaches NamePoolData only through a call, so `SymbolCallFollow` now follows up to
  four calls. Before this, EOSSDK's own name pool won the AOB fallback and 0 of 10 names resolved.
- **FName::Number is measured** (`937c216c`, A9 step 11). A case-preserving UE4 / 5.0 build keeps Number at +8,
  after the DisplayIndex. On the UE 4.27 editor running UE427_3rdPerson `-game`: the pre-fix decode (`d936681f`)
  put the DisplayIndex in the number on 3145 of 3145 names; the fixed one is right on 3300 of 3300.
- **sizeof(FName) is measured** (`c88ca562`, A9 steps 1 and 7). It is the modal NameProperty ElementSize. The
  rule no longer overrides a plausible engine size. Live: 12 on the 4.27 editor, 8 on DumperTest 5.4 Shipping, both
  64 of 64. `fixture_smoke shipping` against 3548 differs in 0 offset verdicts and 0 AOB winners.

**The build.** `build.ps1 -Mode Publish`, one run, bumped 3548 → 3549: `dist\UE5DumpUI.exe` AOT 55.0 MB
(57,723,392 bytes, sha `79bb951b2de9`), `UE5Dumper.dll` sha `31126a429fed`, stamp `1.0.0.3549 9d4fdb69-dirty`.
The suffix is the bumped `build_number.txt` and nothing else (see the correction in the build 3368 entry, 2026-09-03).
Four proxies went to `dist\proxy\`. Tests: UI **5329/5329**, `dll_helpers_test` 2903/0,
`utf8_helpers_test` 265/0, `dll_core_test` 430 checks, and `grausam_window_test` / `sein_retention_test` passed.
Gates 23/23.

-----

## 2026-09-24 (build 3548) — vendor audit #7 fixed: VND583-01..17 and the DOC bundle, published AOT

**3548 = 3547 plus the `[VND583-*]` rows, up to `26993098`.** Derive the list, do not copy it:
`git log --oneline b9e6aff0..26993098 -- dll ui` (20 commits, 14 row tags). Every row's commit, test and
live evidence is in `docs/todo.md` `[VENDOR-UE583-2026-09-24]`; the source-side facts are in
`docs/audit-2026-09-24-vendor-ue583.md`. In short:
- **Measured instead of keyed on version**: UFunction::FunctionFlags by a vote over parameter chains (01);
  UField::Next in FProperty mode (02); alignof(FName) on a single-FName ScriptStruct (03); the UEnum value
  column on ENetRole (04, uint8 on 4.9–4.14); FSoftObjectPath's shape on the SoftObjectPath struct (14).
- **Weak pointers**: a top-level weak says null / null (stale) / unreadable (05); a target UE's `Get()` would
  refuse is labelled `[garbage]` (06); serial 0 is UE's explicit null (08).
- **Layout and version**: UE 5.2 keeps the 16-byte FFieldVariant defaults (09); the static-struct GObjects
  resolver reads 5.8's array and the 5.7+ item (10); the CPN property family starts at +0x34 (11); FNameData
  enums mean 5.7+ (12); compact TSet/TMap builds are guarded (13).
- **Tools**: the CRC oracle merges instead of overwriting, with a new gate (16); `find_gobjects.java` finds 5.8's
  narrow anchor string (17). 07 (A9) and 15 are filed without code by the audit's own instruction; 18 (minhook)
  stays on HOLD.

**Live, red → green where a host exists**: DQ XI S (01, 03), NEKOPALIVE (04), DumperTest 5.4 (05, 06, 08),
forced static recovery on DumperTest58 5.8 (10, whose live arm found a second defect, fixed in `de3d397a`), and
Ghidra on StackOBot 5.8 (17). Regressions: DumperTest 5.1 / 5.4 / 5.8 and DQ I & II HD-2D (09, 11–14).
**New hosts**: `-DumperTestWeakGarbage` (DumperTest repackaged: Development, Shipping and DebugGame; its
`pe_hash` changed again), and the UE 5.4 editor running DumperTest `-game`, the first case-preserving host.
CPN is detected there, but its name pool does not resolve yet (`[VND583-07]`).

**The build.** `build.ps1 -Mode Publish` bumped 3547 → 3548 but failed its UI tests. A source-pinning test
still expected the old `FindGObjectsStaticStruct` call; it was fixed in `26993098`. It then re-ran with
`-NoBumpBuildNumber`: `dist\UE5DumpUI.exe` AOT 55.0 MB (sha `d2c240608c52`), `UE5Dumper.dll` sha
`318682738eeb`, stamp `1.0.0.3548 26993098`, and four proxies to `dist\proxy\`. Tests: UI
**5327/5327**, `dll_helpers_test` 2877/0, `utf8_helpers_test` 265/0, `dll_core_test` 414 checks, and
`grausam_window_test` / `sein_retention_test` passed. Gates 23/23 (`crc_oracle_selftest` is new).

-----

## 2026-09-24 (build 3547) — the first bump since 3546: the whole fix pass, published AOT

**Why an entry now.** Build 3546 was stamped at `567b9afc` (2026-09-12). Every product change of the
fix pass since then shipped as "3546, no bump", so no build number could map a binary to its source.
The maintainer asked on 2026-09-24 for the number to move again, so that this log can map builds to
changes. **3547 = the fix pass up to `0f1be99f`.**

**What 3547 contains that 3546 did not.** 139 commits touch `dll/` or `ui/` (395 in all), with 127
distinct row tags. Derive the list, do not copy it:
`git log --oneline 567b9afc..0f1be99f -- dll ui`. The per-row ledger is `docs/todo.md`
`[FIXPASS-2026-09-10]`, and the live checks are `docs/fixpass-low-live-plan.md` plus the todo.md
Live-check backlog. Two points matter to anyone holding an old binary or table:
- the CE Lua ↔ DLL **mailbox contract is v5** (`[W5-OFFSETS-MAILBOX]`, min 1);
- the See-through producer probe fix (`[SEETHRU-PROBE-SUBSTRING]`, `39ccb2a4`) and the export-status fix
  (`[EXPORT-STATUS-LATE-PROGRESS]`) are the last product changes before the bump.

**The build.** `build.ps1 -Mode Publish`: `dist\UE5DumpUI.exe` AOT 55.0 MB (sha `24b6ce92b330`),
`UE5Dumper.dll` sha `7b1a26a20950`, stamp `1.0.0.3547 b9e6aff0` (the tree then differed from
`0f1be99f` only in docs). Four proxies to `dist\proxy\`. Tests: UI **5327/5327**, `dll_helpers_test`
2752/0, `utf8_helpers_test` 265/0, `dll_core_test` 356 checks, and `grausam_window_test` and
`sein_retention_test` passed. Gates 22/22.

**The same day, no product change:**
- **Vendor sync + audit #7** (`docs/audit-2026-09-24-vendor-ue583.md`): UE 5.8.3, RE-UE4SS `f58e8f84`
  with UEPseudo and patternsleuth initialised for the first time, Dumper-7 `dd8fe34`, and minhook HELD.
  UE 5.8.3 changes nothing for us. Reading the new sources filed `[VND583-01..18]`, led by FunctionFlags
  keyed on version (breaking on FF7R / DQ XI S).
- **DumperTest58 repackaged with the UE 5.8.3 editor** (Shipping + Development). Its `pe_hash` changed,
  which orphans its old per-game app-data folders. `tools/verify/fixture_smoke.py` found the DLL's 57
  verdict lines and every AOB winner identical to the 5.8.2 runs.
- **Live checks closed**:
  - L6 step 2: an injected Escape never reaches the app on this rig, so `send_key.py --post` is used (lesson 1.af).
  - L7 and L21: through the slow-walk staging.
  - L13 steps 2-3: through a UI seam.
  - L33 step 2: on Avowed through a staging, with a correction.
- **New rigs**: `fixture_smoke.py`, `proxy_swap.py`, `send_key.py`, and `staging/slow-walk.json`.

-----

## 2026-09-12 (build 3546, no bump) — the three HIGH live checks, run on a game

No product change: three verification runs and their evidence. `[FIXPASS-2026-09-10]`'s ledger has
exactly **three HIGH rows** — 1 `[W1-QUOTA-UNLIMITED]`, 2 `[W1-SNAP-FAULT]`, 3
`[P4-OTHER-INSTANCE]` — and L1/L2/L3 in the live-check backlog are their checks. All three now
pass, so **HIGH is closed** and the backlog resumes at L4, the first MED.

Fixture throughout: DumperTest Shipping, DLL `1.0.0.3546`, 24,497 objects, UE 504, driven by the
**AOT-trimmed** `dist\UE5DumpUI.exe` (55.0 MB, sha `e278e02a`). The build matters for L1 in
particular: a `ComboBox.SelectedItem` bound to a boxed value plus a JSON round trip is exactly the
shape that works untrimmed and fails only after trimming.

### L1 `[W1-QUOTA-UNLIMITED]` — `df7681d4`

Picking *Unlimited* rewrote `experimental.json` to `"snapshotQuotaMb": 0` immediately rather than on
exit, and the setting survived a clean `WM_CLOSE` + relaunch — and then, unplanned, a full machine
reboot. Nothing was FIFO-deleted, the DB kept its row, usage read "4.2 MB (no limit)".

### L3 `[P4-OTHER-INSTANCE]` — `5b5971f3`

Two `StaticMeshActor` instances opened through Instance Finder with no GWorld click between them:
A = `0x29254B67C00`, B = `0x29254B67EC0`.

  * **addressing** — every one of 16 `walk_instance` replies echoed B's address and B's
    `struct_data_addr`, so it holds ACROSS Auto ticks, not merely on the initial open;
  * **write** — the half a read-only check cannot cover, because a base mix-up that READS right can
    still WRITE wrong. Editing `bCanBeDamaged` produced exactly ONE `write_mem` in the whole
    session, `{"addr":"0x29254B67F1A","bytes":"24"}`, and A's corresponding byte address
    `0x29254B67C5A` appears ZERO times in the log. An independent pipe re-read — deliberately not
    the UI's own grid — gave B `24`/true and A `20`/false, with B's sibling bits in that same packed
    byte untouched, so the read-modify-write set bit 2 and nothing else;
  * **scroll** — 7 Auto ticks before the write and 9 after, the grid held at 0x59/0x5A throughout,
    which also means the value read back after the write was a fresh memory read and not a stale
    cell. `IsEditing` visibly suppressed the tick ("paused (editing)") during the edit.

### L2 `[W1-SNAP-FAULT]` — `4715dbe5`

`9f86f7b6` fixed this in source and said so itself: *"the DLL half has no unit seam"*, because
`dll_core_test` can only fault `ParallelGObjectsScan` through its own stub. This is the run that
exercises the real one — a hardware access violation inside a scan worker, reaching the `/EHa`
`catch (...)` at `Aura.cpp:285` — by pointing `UE5_SetObjectDecryption` at `0x1000` and restoring it.

A control ran first, because "everything is marked unusable" would satisfy the assertion while
proving nothing: an unarmed capture finalised `is_usable=1`, 1700 objects. Armed, the capture
finalised `is_usable=0` with `partial_reason=''` — correctly NOT `cap`/`disklow`, since a fault
lands in `is_usable`; the status named a worker FAULT and never a deadline; `offsets-0.log` carried
16 `ParallelGObjectsScan: worker tid=N [a,b) faulted` lines covering `[0,8192)` with no gap, i.e.
one whole chunk and only that chunk, so the capture genuinely STOPPED there; and the snapshot was
excluded from the SPC Query, Class Pivot and Diff pickers alike.

**Two limits, recorded rather than glossed.** The capture came back EMPTY, not partial: the window
is ~150 ms — 4 chunks, because `SnapshotChunkSize >= ScanThreadCount`'s 8192 threshold — and cannot
be entered from outside the process, so the arm must precede the capture and then all 16 workers of
chunk 1 fault. The "some rows survived" variant is **not** exercised and is not claimed. A game with
~1M objects (EVERSPACE 2 with a save loaded) would give ~122 chunks and a window wide enough to arm
mid-capture; that is how to close it. Second, the row's "the armed scan must be the process's FIRST
parallel scan" was inherited from the value-scan rig, where the no-fault-on-a-second-scan effect was
pinned on `ScanForValue`'s index builder; `CaptureSnapshotChunk` has no such reuse, so the
constraint probably does not apply to this path at all.

### Method notes worth keeping

  * **the only `ParallelGObjectsScan` log line is the fault `LOG_ERROR`**, so its ABSENCE says
    nothing about whether a parallel scan has run — a reading that briefly sent this session the
    wrong way. Seven entry points reach that function (`FindInContainers`, `FindInContainersDeep`,
    `FindReferencesToUObject`, `FindPropertyXrefs`, `FindFunctionsByClassParam`, `ScanForValue`,
    `CaptureSnapshotChunk`); the object tree, ListClasses and FindInstances are NOT among them, so
    connecting the UI does not spend a "first scan";
  * `sw1_worker_fault.py` resolves the module BEFORE it starts counting `--delay`, and that
    resolution costs ~4.6 s here — the first L2 attempt therefore armed 6 s after the capture had
    already finished. A rig that must hit a sub-second window has to pre-resolve.

-----

## 2026-09-12 (builds 3508 → 3546) — the fix pass closed, then reviewed against itself, and a flake that was the test all along

184 commits. Every recorded bug row repaired one commit at a time, HIGH → MED → LOW, each one red
before green and each one mutation-checked. Then a 14-agent adversarial review **of that pass**,
whose confirmed findings became four more rows. Then a load-flaky test that turned out to be loose
in precisely the way its own fix commit had claimed to have fixed.

### `[FIXPASS-2026-09-10]` — one row, one commit, forty-five batches

The ledger and the live-check backlog are `docs/todo.md` `[FIXPASS-2026-09-10]`; each row's commit is
`git log --grep <TAG>`. Three of the later batches are worth naming here because they moved a
contract or a wire format:

- **`[W5-OFFSETS-MAILBOX]`** (L45) gave CE Lua a way to ask whether the DynOff offsets were
  MEASURED — `CMD_OFFSETS_VERDICT = 16`, result 1/0 plus the reason in `paramsData`. That is
  `MAILBOX_CONTRACT` **4 → 5**, additive: a new Cmd at an unused number, `MAILBOX_CONTRACT_MIN`
  stays 1, so every saved `.CT` keeps working. The command is the SECOND init-exempt one, because
  its answer *is* the init state — gating it would return `-10` in exactly the case the caller asked
  about.
- **`[A2-TOPTIONAL-STRUCT-DESCENT]`** (L40) and **`[A2-TOPTIONAL-VALUESCAN]`** (L41) stopped Find
  Refs, the Address Finder and Value Scan from reading a reset `TOptional` as live data. UE's
  `MarkUnset` clears the flag and zeroes **nothing**, so the old "an unset slot is zero" comment was
  false in both places.
- **`[A4-AB4-BETWEEN]`** (L42) builds a Between scan's two bounds jointly, clamped per width, in
  both matchers.

### Review 6 — the pass, read adversarially

A read-only 14-agent review of `76f93b94..HEAD` (24 commits, 126 files, +6844 / −631): six area
finders, then one skeptic per finding, each defaulting to REFUTED. **11 raw findings, 8 verified, 4
confirmed, 4 refuted**, and 3 lower-ranked LOWs checked by hand. All four confirmed rows are fixed:

- **`[A2-TOPTIONAL-REFINE]`** (MED) — the lead L41 recorded but did not trace, now traced and closed:
  `RefineCandidates` re-read every candidate by absolute address with no optional gate, so a Next
  Scan with **Unchanged** kept a reset optional's stale value forever. ⚠ The finding's own verifier
  corrected two overstatements, and both are recorded in the row rather than smoothed away.
- **`[A2-SENTINEL-OVERREAD]`** (LOW) — L41's intrusive gate read a flat 16 bytes from a field that is
  `sizeof(T)`; an 8-byte `TOptional<FName>` at a page edge therefore dropped a **set** optional.
- **`[A1-VERDICT-STALEMB]`** (MED) — L45's new CE wrapper cleared the busy flag after a timeout
  without the AA19 stale-mailbox latch, so the next invoke could overwrite a command the DLL still
  owned. Fixed for **both** small wrappers through one shared guard.
- **`[A1-REVIEW6-PINS]`** (LOW) — two decision comments that still claimed one init exemption after
  there were two, and a `%ls` gate with no guard against its own scan matching nothing.

The four refuted findings are recorded with their refutations under `#### ⛔ REFUTED — do not
re-raise (review 6)`, including one whose *test-strength* half stands as a lead even though the
shipped behaviour was proven correct.

### `[TESTFLAKE-2026-09-12]` — the flake was the test, and its own fix commit had said otherwise

`ConcurrentRescores_SettleOnTheNewestMode_NotTheLastToFinish` failed 1 in a 5204-test run and 1 of 3
class-isolated runs. ⭐ `3ad4f524` parked this test's two siblings with `GatedEntryList` and its
message says it parked this one too — **it did not**. Both toggles still fired straight after
`ExecuteAsync`, and since the fake dump service returns `Task.FromResult`, `LoadAsync`'s tail resumes
on a pool thread: the toggle had FOUR landing zones, and in three of them the scenario never formed
(`PendingRescore` null, both `if (… != null)` drains skipped, the assertions proving nothing) while
the single-slot `PendingToggleRescore` orphaned the first re-score, which could still be rebuilding
`Results` under the assertion's own enumeration.

Measured before concluding, because "it passes in isolation" is exactly what got this filed as a
test-suite flake once before and was wrong then: the OLD test passed **15/15 idle and 25/25 under
deliberate CPU contention** — its end state was never wrong, only which zone it hit. The fix enforces
the interleaving with a multi-pass `PhasedEntryList`, releasing the NEWER request first and the OLDER
request's scoring LAST, and pins both re-scores in flight at once. Shown able to fail: deleting the
generation guard reds it in **50 ms** with the original `Assert.DoesNotContain() Failure: Filter
matched in collection`. **22/22** consecutive isolated runs green. No retries anywhere.

⬜ **Recorded, not claimed:** `RescoreAsync`'s generation check is still not atomic with its publish,
and `ApplyFilter` rebuilds `Results` unsynchronised. Avalonia's dispatcher serialises both in the
shipped app, and no seam can suspend a run between its guard and its publish — so that window has
**no reproduction** and was deliberately left unfixed rather than passed off as the cause.

### ⚠ What the mutation step caught that reading did not

Three pins written this session were **vacuous**, and none was caught by review:

- two in L48, asserting strings that also exist elsewhere in the same file — `invokeUFunction`'s
  pre-existing AA19 latch, and my own doc comment — so deleting the code they claimed to pin left
  them green. Fixed by counting occurrences and by asserting the executable line;
- one in L49, where the guard-the-guard added *against* a vacuous scan was itself cleared by half the
  scan: `scannedFiles >= 25` is met by `dll/src`'s ~31 `.cpp` files alone once `.h` is dropped.

Each was found by a mutant SURVIVING. That is the mutation step earning its cost, and the reason a
"red: 0" line is never to be waved through.

### Evidence

- UI suite **5298 / 0 failed**; `dll_core_test` **332 checks / 0**; `dll_helpers_test` **2752 / 0**;
  gates **22 run, 0 failed** (final close-out, batch L49).
- `build.ps1 -NoBumpBuildNumber` SUCCESS, then `-Mode Publish -NoBumpBuildNumber` SUCCESS.
  `dist\UE5DumpUI.exe` **55.0 MB, sha256 `e278e02a`** — AOT-trimmed, which is the binary to hand
  over. (Before: 54.8 MB / `628552a1`.)
- ⬜ **Live checks are DEFERRED by the maintainer's direction**, not skipped: the backlog is
  `[FIXPASS-2026-09-10]` rows L2…L89 in `todo.md`, and **nothing above is closed until its check
  passes on a running game**. Several need Cheat Engine, which is announced before use.

## 2026-09-09 (builds 3462 → 3508) — a delegate layout that only holds in Shipping, nine register rows driven to a verdict, and 166 claims sliced down to 30 hand-reads

43 commits. Two engine versions of UE4 exercised for the first time, a wire field that never left
the process, six more defects out of a population three agent sweeps had produced but not
adjudicated — and one correction of my own claim, which had been refuted the day before.

### `[D4B-DELEGATEPAD-2026-09-09]` — the delegate readers assumed a layout only a Shipping build has

`FScriptDelegate` / `FMulticastScriptDelegate` carry an **access detector** in non-Shipping builds,
so every delegate read was off by a pad the code never accounted for. The fingerprint is nasty
because it is *not* a crash: on a Development build the reads land on neighbouring bytes and
publish plausible-looking bindings. `D3b` turned out to be the same root cause.

⭐ **Surveyed rather than patched to the fixture.** The pad derivation was re-run on a **second
engine version**, then on a **real title** — Titan Quest II, **279,587 objects**
(`[D4B-PADSURVEY-TQ2-2026-09-09]`). ⛔ Two things that run corrected are written down in the row;
read them before quoting the numbers.

**A sixth site, and then a seventh defect underneath it.** `Ubel::ReadDelegateArrayElements`
carried **all three** of this sweep's shapes at once, and the fixture built to prove the sixth
immediately surfaced a seventh (`[D4B-SITE6-2026-09-09]`). Everything was re-run against HEAD's
binary afterwards, which also un-staled three rows.

**`(stale)` — five sites, one definition.** `Ubel::DescribeScriptDelegate` now owns the string that
five call sites had each spelled for themselves (`[STALE-NONE-2026-09-09]`).

### `[D4B-UE4-2026-09-09]` — the UProperty path, finally exercised

A portable delegate fixture (`tools/ue-sample/ue4-delegate-fixture/`) packaged and injected on
**UE 4.23 and 4.27**. ⚠ It was re-run in **Shipping** because the first UE4 pass had the population
backwards — the same mistake that later became a house rule this same day. ⛔ **4.15 and 4.18
cannot build on this machine and the reason has no override**: it is the Windows SDK, not
permissions. Three UE4 blockers are recorded, all measured, two still open.

### `[D4B-TAIL-2026-09-09]` / `[RECON-REGISTER-2026-09-09]` — the reconciliation

`git log -- docs/verification-register.md` returned **nothing** for this entire work stream while
todo.md said the live rows belonged there. Nine `SW` rows written; four already-closed names
removed from the long-tail heading (the sweep had found two and written *"fix when next editing the
register"*, then never edited it — the file had four). Every row names the observable on **both**
sides, per the register's own charter.

⛔ **Then all nine were driven to a verdict in one stream, and none was blocked** — against my own
written prediction that several were unreachable. **Five of my own claims were wrong.** SW1 (a
faulted worker yields a *partial* result — 1,649 of 4,115, not zero), SW2 (a failed clipboard
delivery, proven from both sides *and* from outside the process), SW3 (`onUnreadable` in real Cheat
Engine — a dead process, then the busy-mailbox half), SW4 (CE resolves a delegate record to the
InvocationList, not to address 0), SW5 (the AMBER and RED push branches produced for the first
time), SW6 (both ARRAY refusal arms, manufactured at ElementSize 32), SW8 (`GetMapPairLayout` does
**not** refuse on a real title — TQ2, 1,685 map leaves), SW9 (a delegate BINDING read on 4.27 and
4.23, off an actor the engine spawned).

### ⛔ `[SW7]` found a defect nothing else could have: a wire field that never left the process

A scalar `DelegateProperty`'s `delegate_pad` was computed correctly and then **never serialised**,
so the UI and every CE path built chains without it. Fixed on the wire (`486c70a5`), and Live
Walker's CE paths now carry it (`[CEPATHS-UNPADDED-2026-09-09]`) — as a **payload** address,
distinct from the field address.

⚠ **And the rig itself was the bug once**: `l12` sent `addr=` where `Fern` reads `instance_addr`,
so 2,000 requests measured **nothing** and reported cleanly (`7186f1c8`). That is the same
send/receive shape as SW7, on our own side of the pipe.

### Five more defects the rigs found while measuring something else (`e16d2052`)

Two verified live, three code-level. All one shape: **a failure that happened correctly and said
nothing, or said the wrong thing.** [A] the delegate-array refusal reached the UI as *nothing* —
an `ArrayProperty` never sets `typedValue`, so a refused array was indistinguishable from an empty
one. [B] the two scalar stride refusals emitted no log line at all. [C] `Aura`'s worker-fault line
claimed *"results are partial"* — a claim about the whole scan one worker cannot make, and false in
the commonest case (`parallel=false`: one chunk, `total=0`, `scanned_objects=0`). [D]
`[TMAPGEOM-TWIN]` — both inlined copies of the map geometry guessed silently where
`GetMapPairLayout` refuses; behaviour deliberately unchanged, the silence was the defect.

`[TMAPGEOM-2026-09-09]` itself is the fixture that looked impossible: a faulted `Struct` read,
manufactured at a **page edge** in our own process, with the control and the number the mutation
produced both recorded.

### `[CLAIMS-SLICE-2026-09-09]` — the ~166 unadjudicated claims, sliced and closed

⛔ **First, what they are not: a list.** Three agent sweeps filed **209 claims and confirmed 23**;
what remained was a *population*, not a queue. Sliced four ways and adjudicated by hand:

| slice | outcome |
|---|---|
| **A** CE Lua emission layer | 8 read → **1 defect**, fixed and live-verified (`[TG1-CLEARALL-RESULT]`, in real CE) |
| **B** discarded effect-appliers | 7 read → **2 defects** + 1 hygiene; the proposed gate re-scored and still refused |
| **C** discarded `Read*Safe` | 15 read → **3 defects**, two manufactured fixtures (`[IFACEREAD]`, `[UNREADVAL]`) |
| **D** the 30-item tail | **closed by decision** — a round 4 the sweep forbade, measured yield 0 |

**30 sites read by hand → 6 defects and 1 hygiene fix.** ⚠ That is not a claim that reading beats
sweeping: the sweeps produced the populations these slices walked. The narrower claim the rounds
themselves kept making is that **the candidate rows were worthless and the populations were not.**

- **Slice C** — three walker handlers (`EnumProperty`, `ByteProperty`-with-`UEnum`,
  `OptionalProperty`) published an un-set 0 dressed as a value; now one shared refusal formatter,
  `Ubel::DescribeUnreadableField`. `[IFACEREAD]`'s manufactured fixture **found a defect in the
  fix** before it shipped.
- **Slice B** — both `MovementMode` writes in `Dunste::SetEnabled` dropped `Macht::WriteBytes`'
  result, so ENABLE logged *"Fly: ENABLED"* over a pawn that never left its old mode and DISABLE
  logged *"Fly: DISABLED"* over a pawn still in `MOVE_Flying` with nothing tracking it. Both UI
  status lines were the same lie in C#.
- ⚠ **CORRECTION, same day**: `Wirbel.cpp:618` was **refuted in round 2** and I re-raised it as a
  defect without checking the refuted list. The refutation was re-derived and found sound; the
  change was kept as **hygiene**, slice B's count corrected 3 → 2, and the process lesson written
  as working-lessons §1.w2.

**Live arms on DumperTest, on Shipping *and* Development**: `unreadval_live_arm.py` walked **2,391
fields across 2,105 live instances with not one refusal**, 1,466 of them genuinely reading 0 — the
state the refusal must never be confused with. `sliceb_fly_arm.py` is 10/10 across two full
enable/disable cycles with three independently-computed witnesses.

⛔ **And one measured null result, shipped as evidence.** The `FR_ERR_WRITE` arm is **unreachable
from outside the process** — `sliceb_fly_fail_probe.py` is the probe that says so. ⭐⭐ It also
reproduces `[CLASSCACHE-FRONTED-2026-09-09]` from a second, unrelated experiment: `FindField` →
`WalkClass` is memoised in a 2048-entry LRU that nothing invalidates, so a patched `FField` is
invisible to both `Dunste` and the walker. That open row now blocks **four** verification arms, not
two. ⚠ The probe's own sanity guard is what kept it safe: `FField+0x4C` read **675**, not 513 —
`Grimoire.h`'s `FPROPERTY_OFFSET` is a *compile-time default* and Genau derives the real one per
title (68 here). Without the guard that was a wild 4-byte write into a live game object.

### 📐 Rules and lessons this day added

- ⭐ **SHIPPING is the DumperTest fixture; Development is the second opinion** — a Development
  build's offsets are a *minority shape in real games*, so a row closed on `dev` alone is a
  narrower claim and must say so. `docs/handover-2026-08-22.md` §4 rule 5, and
  `launch_dumpertest.py`'s docstring. Both live arms above were re-run on Shipping for this.
- working-lessons **§1.w2** (a mechanical scanner surfaces *refuted* rows as fresh hits — check the
  refuted list before raising), **§1.v** (targeted **and** broad sampling: the first UNREADVAL run
  strided the pool and found **zero** `OptionalProperty`, because two of 25,231 objects declare
  one), **§1.v2** (a valueless field on a metaobject is a *branch*, not a regression — read the
  ratio; 77 of 96 fields across fourteen type families, of which the fix touched three).

-----

## 2026-09-08 (builds 3423 → 3457) — audit #3 re-checked and deliberately NOT re-run, a sweep that finished by grep, and 29 copies that claimed a write which did nothing

20 commits. The question was whether audit #3 — the only audit on this repo not produced by Opus 5,
and its own doc never records that — should be re-run. **The answer is no, and the measurement is
why.**

### 🔎 audit #3 re-check — 23 of 23 fixes still live, 0 reopens, and 5 confirmed misses

Re-checked by a 12-agent workflow (4 evidence + 2 miss-hunts + 6 **refute-mandated** skeptics),
every `file:line` re-read at HEAD because audit-doc line numbers have drifted hard.

- **23 of 23 shipped fixes are still live**, across ~1,250 builds. **All 9 dropped items re-derived
  → 0 reopens.** ⭐ Fable 5's adversarial judgement was **sound** — the refutations were right. The
  gap is in *coverage*, not in reasoning.
- **5 confirmed misses** (1 further claim refuted), and they share one shape: three of them were
  **inside functions audit #3 itself rewrote that day**. The 🔴 headline — `Schlacht::Tick` recorded
  **intent, not effect**; See-through was a no-op on non-Actors while `hiddenCount`, the pipe's
  `hidden_count` and the UI status string all reported success — was found **six weeks later** by
  `[SEETHRUTALLY]`.
- ⭐ **The audit's own cross-cutting root cause rested on a false premise** (`H1` asserted in writing
  that an unexpected pipe death is safe, falsified by two catch filters 30 lines away), and the
  shared helper it recommended was **never built** — so the same shape was re-found later as `AC10`.

### 📐 The structural coverage gap, and no later audit touches it

audit #3's window (`88ee170..af2ce50`, 2026-07-03..07-15) has **22,853 lines still alive at HEAD,
of which 100% have never been re-swept at line level by any later audit** — tautological under
`git blame`, since audit #4's baseline *is* audit #3's endpoint. ⛔ **Hard core: 80 files / 4,648
lines unchanged since `af2ce50` and cited by no later audit document at all.** `dll/src/Grausam.cpp`
is 279/279 lines from that window, byte-identical since, and appears in audit #5 only as a
*precedent* and inside a refutation. The derivation recipe is in todo.md — derive it, never quote it.

⚠ **Verification asymmetry**: 13 of 23 fixes have a real headless rig; **10 have a unit test only
and appear nowhere in `verification-register.md`** — all UI-side, `H1` included, which violates the
register's own single-owner rule.

### 🔎 Blind-spot sweep, three rounds — 23 confirmed, 20 refuted, and it ended by grep

A **pattern** sweep for the named blind spot, not an area audit: a function returning `bool`/an
error code whose call sites drop it, and any count/status/log reported from *intent* rather than
from re-reading the effect.

- ⛔ **The instrument was 76% blind in round 1, and the negative control caught it every time** — it
  was wrong three times. Round 2's *"68 Services files / 24,651 lines"* framing was the **wrong
  predictor**, and cost nothing only because it was checked before being spent.
- ⭐ **The load-bearing finding is why two families in the same layer diverge 51% → 0%.**
  `IAobMakerBridge` callers were written *against a fallback*, so the bool **had** to be tested to
  pick a branch — 42/55 consumed, **0 defects**. `CopyToClipboardAsync` is the **last link with
  nothing to fall back to**, so testing it buys the author only an honest message — **55 of 57
  dropped, 29 defects.** Line count predicted nothing.
- ⛔ **The sweep is finished, and that is one grep, not a judgement**: all 17 `bool`-returning
  methods on every `Core/I*.cs` interface were enumerated and every call site of the other nine
  checked. Outside the two families the entire C# population is **1 site, 0 defects**. There is no
  third family. **Do not run a round 4.**
- ⚠ Three independent counts gave three different answers (56/17, 57/53, **57/55**); only the
  clipboard number survived. That is audit #4's B34 lesson landing again.

### ✅ Fixed: 14 delivery copies, 5 DLL rows, and the CE Lua idle wait

**C# side** — all 14 *delivery* copies routed through a shared `Helpers/ClipboardDelivery`, in three
batches; `RequestCopyText` became `Func<string,Task<bool>>` so four cross-file claims stop lying.

**DLL side** — fixes were **derived and adversarially checked before being written**, because this
repo's history says a verdict is not authority on the repair. All five came back
`sound-with-corrections`; none was an AB4.

| row | what it stopped claiming |
|---|---|
| **D1** `Dunste.cpp` 🟠 | `InvokeSetCollision` returned *"the setter was FOUND"*; three call sites read it as *"collision CHANGED"* |
| **D2** `Aura.cpp` | a scan worker that **threw** let the run report **complete** |
| **D3+D5** `Ubel.cpp` | an unreadable delegate array published the affirmative `"(0 bindings)"`; a faulted `FGuid` published a fabricated all-zero GUID |
| **D4** `Aura.cpp` | a sparse delegate whose `bIsBound` byte **read 1** reported `"(0 bindings, sparse)"` |
| CE Lua | Invoke's idle wait reported a **dead game process** as a **busy mailbox** |

⭐ **D1 was bigger than filed**: the dropped `int32_t` re-opened the hole audit #4 **B8** was written
to close — a refused restore left the pawn ghosted *and* wiped the record that would have started
`PendingRestoreLoop`. It reaches that through the **dispatcher**, which is why B8 did not cover it;
hence a tri-state `CollisionApply` rather than a bool. ⭐ **D4 found a fourth site nobody had
filed** — `FindReferencesToUObject`'s sparse pass `continue`s on a fault, so the binding is silently
**absent** from Find Refs, and an absence is a *stronger* wrong claim than a wrong count.

**Two gates shipped**, both on the rule that earns them — *pick a predicate whose legitimate
population is EMPTY*: `check_ce_untick_placement` (17a) and `check_clipboard_delivery` (17b).
⛔ Gate #17 **as proposed in round 1 was refuted**; 17b as first scoped was refuted too and shipped
narrower. ⚠ **Three gate bugs were caught by their own negative controls, each after the check was
already green** (working-lessons §1.2a).

### ⬜ Filed, not fixed — the Fly report publishes the WISH, not the FACT

Found by D1's checker and deliberately left out of the fix, because it is a pipe-contract + UI
change: `Fern.cpp` publishes `data["noclip"]` from **what was asked for**, and the fact reaches no
channel at all. ⚠ `collisionOff` cannot simply be published as the fact — B8 gives it *"intended,
and retrying cannot help"* semantics, so publishing it would ship a **new** lie; it needs a
tri-state. ⭐ `FlyStatus.Noclip` is **already dead on the C# side**: `ApplyFlyReadout` never reads
it and the `✈ Fly ON (noclip)` badge is built from the local checkbox — **it never asks the DLL.**

Live verification round 1 ran the same evening on DumperTest (UE 5.4, DLL 3457): **25,229 objects**,
offsets `validated: true` — checked before anything was believed, because a dead engine reports
coherent zeros. D5 and D3 confirmed; D4's fix fires and surfaced a real defect underneath it.

-----

## 2026-09-07 (build 3423) — a cancel that crossed connections, a Guess? that invented rows, and a queue that lied

38 commits. Four shipped defects, two verification rows closed on real hosts, one fixture that had
never worked, and a queue audit whose most useful output was proving my own hypothesis wrong.

### `[MULTIPIPE-CANCEL-2026-09-07]` — a foreign client's death truncated your scan and said `ok:true`

todo.md recorded multipipe-eval §9.6 item 5 as closed. The evidence on file described the **opposite
experiment**: the item asks for "disconnect only ONE lane, the other keeps working", and the run
logged had closed the whole GAME mid-snapshot, so both lanes died together. §9.3's mitigation
(`inFlightHeavy`) had never shipped — `Fern::MonitorLoop` tripped the process-wide
`Tot::g_perCommand` for **any** broken in-flight connection.

Measured (`tools/verify/multipipe_cancel_isolation.py`): 5,157 of 5,264 replies collapsed to **77
bytes against a 720,793-byte baseline**, every one reporting `ok: true`. ⛔ The severity was never
the truncation — it was that a caller could not tell it from a complete answer.

Fixed in three parts: the loss is now **flagged** (`truncated: true`, joining a convention 10 other
commands already used), pipe handlers got a **per-connection** cancel, and Aura's **parallel
workers** — which do the actual work on any real game — inherit their caller's cancellation context.

⭐ **Verified with a true before/after on ONE real host.** Octopath Traveler (UE 4.18, 406,060
objects) still carried a build-3380 proxy from an older session, so the pre-fix state was directly
measurable: **10 of 125 replies truncated to 238 bytes before, 0 of 115 across three runs after**,
with `client gone mid-command` logged exactly once in *both* directions — so the AFTER is a real
negative, not a missed trigger.

⛔ **The obvious design was wrong and is documented as rejected in `Tot.h`.**
`return (t_connCancel && t_connCancel->load()) || g_shutdown` makes *unbound* identical to
*cancel-immune*, inverting the module's default from fail-safe to **fail-silent**. Three of four
review lenses returned FATAL on it before a line was written.

### Guess? invented a phantom row over every static C-array element

The row said "RESOLVED — working as designed". It was resolved for *one* footprint family: its
evidence was a TArray + TMap, and for a **dynamic** container `ElementSize` is the entire inline
footprint, so the defect is structurally invisible there. A static `UPROPERTY Type Foo[N]` renders
as ONE element, so elements 1..N-1 looked unclaimed. Measured on the fixture's `int32 FixedArr[8]`:
**7 fake rows before, 0 after**, 34 legitimate guessed rows still emitted elsewhere.
⛔ A comment had asserted the bug was fine ("its output is unchanged by the new ArrayDim field").
The fix was a **deletion** — `ComputeClassHoles` already had the right formula.

### The FProperty family had a THIRD writer, and it split the family

`Grimoire.h` says "Never assign a member of this family directly"; audit #5 G12 unified the writers
and recorded "Both writers now go through here". There were three. The failure is silent and
half-correct: struct reads stay right while TArray element descriptors and every enum name read 8
bytes off. Now gated (`tools/check_property_family.py`, 16th gate) because a prose invariant plus a
hand-counted writer list is exactly what failed here twice.

### `[MHPOISON-STARVE-2026-09-07]` — the retry machinery had never run on a live game

Build 2358's fix shipped; its in-game re-check never happened, because the hook kept installing on
the first try. ⛔ And the fixture built to force a failure **was itself broken in a way that read as
success** — it reserved 1 MB at a time stepping 1 MB, and `VirtualAlloc(MEM_RESERVE)` fails for the
*whole* request on any overlap, so it reported "reserved 3824 block(s)" (93%, looks fine) while
leaving ~960 KB free in each of 272 gaps. MinHook needs a few KB. Now walks the window with
`VirtualQuery`: **2 blocks instead of 3,824, and the count going down is the fix.**

All four acceptance criteria then landed in one run: six retries at the configured 5 s cooldown
(not one — the permanent latch is gone), **one** fallback WARN for 61 fallback invokes (against a
historical 552 unsafe-call lines in 19 s), See-through refusing with `hook_active:false` in its
reply, and `hook RECOVERED on attempt 7`.

### `[G2-TIER1-UE5-2026-09-07]` — the first UE5 Tier-1 detection on this machine

The register's own sweep found 6 Tier-1 lines across every archived scan log and **all were UE4**.
Two `.rc` fixtures (5.4 → `1.2`, 5.8 → `1.8`) make the packaged samples fall through Tier 0 while
keeping their `++UE5+Release-N.N` needle. Both engines now produce a live `Tier 1` line — and they
report **different** dummy versions deliberately, so a constant could not fake it.

### Smaller, but user-visible

* **dxgi's "late-load games only" restriction was lifted three days after it was written, and
  shipping C# never heard.** `ProxyDeployViewModel.cs` still steered users away from a proxy that
  works, as the documented selection rule for the Proxy Deploy radio buttons.
* **A cancelled bulk reply no longer claims to be a complete one** (`truncated: true` on
  `list_enums` / `walk_instance_batch` / `pe_profile_get`).
* **A real data race**: `FindSparseDelegateStorage` is the one scan reachable from pipe-command
  threads, so two clients could enter its slow path and both write the same file-static
  `ScanReport` — which owns a `std::vector`. Now serialised. ⛔ Not `call_once`: MA1 deliberately
  does not latch a cancelled scan.
* **Native-C P3's SPC-Query arm** closed offline against the 2026-09-06 capture still on disk — all
  8,556 `<raw@0x..>` rows join under the product's Strict key, none with a vacuous offset.
* **The `0x100000` ceilings** split five ways by tracing each value's producing read and consuming
  arithmetic.

### ⚠ What this session got wrong, because the pattern repeats

* **A todo audit over 12 open bullets expected to find stale "already done" rows** — 5 of 12 had
  bodies contradicting their headers, so the hypothesis felt safe. Two adversarial refuters per
  verdict, told to default to "still open", **demoted every single one**. Zero were stale. Had the
  first pass been trusted, the row containing a live bug would have been deleted.
* **Two proposed fixes in todo.md were both wrong** (the DynOff-atomics row): dropping the
  "redundant" write would have removed the only probe that can land a ±0x10 layout, and making the
  offsets atomic would have fixed the race and left the actual bug.
* **I deleted a bullet and left a header stale** — a patch anchored on a line it did not mean to
  remove, and a header still reading "REOPENED with a REPRODUCED DEFECT" hours after it was fixed.
  Both found by re-scanning the queue mechanically instead of answering from memory.
* **A rig produced a clean, plausible, meaningless green** at 2 FPS: the arithmetic said the window
  was wide enough, and it wasn't. Rigs now treat "trigger never observed" as INCONCLUSIVE, never a
  pass.

-----

## 2026-09-06 - RELEASED as v3397 -- the first release since v3362, 97 commits

Tag `v3397` on `1b55cce3`, published from the draft the release workflow builds. **This entry is a
POINTER, not a summary**: every fix it covers already has its own entry below, and a second copy
would go stale independently -- which is exactly what `roadmap.md`'s banner describes happening to
it.

**[v3362...v3397](https://github.com/bbfox0703/UE5CEDumper/compare/v3362...v3397)** -- 97 commits,
33 DLL/UI files, +1755/-415.

### What the release notes say, and what they deliberately do NOT

The notes are written for someone who only uses the tool: ten one-line bullets, no internals. Two
things were CUT after review, and the reasoning is worth keeping because it applies to every future
release:

  * ⛔ **Documentation and tooling changes are not release notes.** A user does not care that a
    filename's case changed.
  * ⛔ **A defect introduced AND fixed inside the release window never shipped, so it is not a fix.**
    The `_wfopen_s` logging regression (introduced build 3370, fixed 3371) never reached a user --
    `git show v3362:dll/src/Sein.cpp` still had the correct `_wfopen`. The proxy SRWLOCK deadlock is
    the same shape: introduced by the 3363 crash fix, gone by 3365, so the two commits are ONE
    user-visible item ("the dxgi proxy used to stop some games launching").

⚠ The test for this is not "is the commit newer than the tag" -- that is true of every commit in the
window, and conflating a FIX commit with an INTRODUCING one gives the wrong answer both ways. Read
the code at the old tag: `git show v3362:<file>`.

### Verify-then-publish

`release.yml` builds a DRAFT deliberately so the artifact can be checked before anything is public.
Done, and worth doing every time:

    sha256   recorded == actual                    615b3d6c...df38
    zip      16 entries
    UE5DumpUI.exe   54.7 MB  <- the AOT-TRIMMED build, not the ~107 MB one
    build_number.txt embeds 3397 == the tag

⭐ That 54.7 MB check is the one that matters. CLAUDE.md records that `-Target Test` / `-Target UI`
silently replace `dist/UE5DumpUI.exe` with the non-trimmed binary, and shipping that would hand
users a build in which every AOT/trimming defect is invisible.

-----

## 2026-09-06 - CrashReportClient.exe becomes a second version source, and a file-hash allow-list was measured to reject everything (build 3393)

`Genau::kVersionDetectLogicRev` **6 -> 7**. Evidence:
[`evidence/2026-09/crc-version-source/`](evidence/2026-09/crc-version-source/), claim tag
`[CRCSOURCE-2026-09-06]`. Oracle: `tools/ue-crc-oracle.json`.

### Why a second source at all

`CrashReportClient.exe` ships **with the engine** and is never authored by the game team, so unlike
the game exe its version can never be the GAME's version -- and the game exe's field frequently is
(measured: OCTOPATH 1.0, DQ7R 1.1, Elliot 1.2, Titan Quest II 63339.64744).

⭐ Its value is not the hit rate but WHERE it hits. DQ I&II HD-2D carried a misdetected `UE5.05` in
this repo's own `docs/test-games.md` for months -- an error **across the UE4/UE5 boundary** -- and
its CrashReportClient says `4.27.2.0`. P3R (a 4.27 fork) is the same shape. Those are exactly the
titles where the memory-string tiers struggle.

⭐ And it is INDEPENDENT, which is the real argument (working-lessons.md 1.4). Both poisoned
hint-cache entries this repo has found -- Avowed and DragonSword, each holding a persisted runtime
raise of 504 -- would have been caught by it: CrashReportClient reports 503 for both, agreeing with
detection and disagreeing with the cache.

### What was measured and REFUSED

The proposal included a conservative form: accept a game's CRC only when its version **and file
hash** match a known-official binary. Measured over 8 installed Editors and 66 game folders, that
rule **accepts nothing** -- 0 of 8 game copies match an Editor copy, because CrashReportClient is
rebuilt as part of each game's engine build. The version string is stock; the bytes are not:

    4.27.2.0   Editor 19,469,792 B   DQ I&II 19,496,448 B   P3R 19,487,232 B
    5.7.4.0    Editor 26,711,480 B   TQ2     26,725,376 B

So `tools/ue-crc-oracle.json` pins the ProductVersion -> code ARITHMETIC against 8 real Epic
binaries (4.11-5.8, all mapping cleanly) and is explicitly **not** an allow-list.

⚠ Coverage is a per-machine sample, not a rate: 8 of 66 folders here ship one. Avowed, DQ XI S,
OCTOPATH and DumperTest ship none, so "absent -> skip" is the common path and a first-class branch.

### Priority, and the question it answers

CrashReportClient is a **build-provenance** signal, so it ranks with the static detectors and NOT
above the runtime ladder in `Frieren.cpp`. Measured on DragonSword: CRC 5.3.2.0 -> 503, detection
503, and `CMC::GravityDirection` then raises to 504 at init. That is not a conflict -- the two
answer different questions ("built from which engine" vs "has which engine features"), and a
licensee fork backports features. The ladder keeps the last word.

Where both resource sources exist and disagree, CRC wins and a WARN says so -- the disagreement is
itself the finding.

### Implementation notes

`DetectVersionFromPEResource` was split into `ReadUeVersionFromFile(path, productLabel, fileLabel)`
so both files are parsed by the SAME code; a second reader with its own copy of the parse and its
own bounds is the defect shape 1.12 catalogues. The version arithmetic -- written out **four times**
in that one function -- is now `Grimoire::UeVersionCode` once, with the 4.0-4.27 / 5.0-5.9 bounds
that reject a game's own version.

⚠ Every existing log line is preserved BYTE-FOR-BYTE, including the UE4 branch's different wording:
`tools/verify/sweep_title.py` greps the literal `PE VERSIONINFO` and a committed evidence README
quotes the UE5 line verbatim.

Rev 7 is mandatory under `Genau.h`'s own rule: a title cached under rev 6 holds a verdict reached
without this source and would never consult it.

Tests: `dll_helpers_test` 2337 -> **2365** (+28) -- the version-code bounds, including every real
game-version literal that must be rejected and every oracle entry that must map, plus the
walk-up path derivation. Live: both branches on a manufactured fixture, the DISAGREE run being the
negative control that proves the code runs at all.

-----

## 2026-09-06 - the version cache held a value detection cannot produce: `kVersionDetectLogicRev` 5 -> 6 (build 3390, measured live on DumperTest)

`Genau::kVersionDetectLogicRev` **5 -> 6**. Evidence:
[`evidence/2026-09/revbump6-cache-restamp/`](evidence/2026-09/revbump6-cache-restamp/),
claim tag `[REVBUMP6-2026-09-06]`.

### The first bump that is NOT a logic change

Every previous bump followed `Genau.h`'s own rule -- *the detection logic changed, so re-derive
every cached verdict under it*. Rev 6 does not: `DetectVersionDetailed` is byte-identical. What
changed is that a cached **value** was found which the current detection cannot produce.

Measured while settling Avowed's row: its record held `ueVersion=504`, and a forced fresh
detection re-stamped it **503**. 504 for that binary is the runtime `CMC::GravityDirection`
raise (`Frieren.cpp`) -- and that ladder runs in `UE5_Init`, **after** `Flamme::SaveResults` at
the end of `FindAll`, so today it cannot write back. The 504 is residue of a write path that no
longer exists.

⭐ **And nothing would ever have re-derived it.** The cache-reuse branch skips detection entirely
for any record stamped with the current rev, *however wrong the value is*. The stamp is not one of
several ways to reach a poisoned entry -- it is the only one. So `Genau.h`'s bump rule gained a
second clause: **also bump when a cached VALUE is found that the current detection cannot
produce.** The rule as written covered the logic and said nothing about the data.

### What it costs, and what it does not

Live on DumperTest Development (cached 504 / rev 5):

```
FindAll: Cached version 504 was stamped by logic rev 5 (current 6) - re-detecting once and re-stamping.
DetectVersion: PE VERSIONINFO -> UE 5.4 -> 504
FindAll: UE Version = 504 (tier=1, detected=yes, lowConfidence=no, publisher=-)
```

The invalidation fires and the verdict is **unchanged** -- which is the point. Cost on this binary
was **2 ms**, not rev 4's ~0.35 s; that older figure is the memory string-scan path, which a Tier-1
VERSIONINFO hit never reaches. A stripped title (SquareEnix) still pays the scan. Locally this
re-derives 44 cached records once each; Avowed re-detects 503 and is then raised to 504 at runtime
exactly as before, so its badge does not move.

⚠ **This does not overturn the A4 decision.** `audit-2026-09-05-vendor-ue582.md` and
`verification-register.md` both say *do not bump for the 507/508 ladder*, and that still holds --
the ladder is runtime-only and re-applied on every init, so it needs no invalidation. Rev 6 is for
the residue of an older write path, not for the ladder.

-----

## 2026-09-03 - FName index 1 is INTERIOR to "None": a probe that could never succeed, warning on every session (build 3368, verified live on DumperTest; re-verified 3369)

Two fixes, both found by asking why a log line said something odd, and neither was the thing that
was actually asked about.

### 1. The FNamePool `BlockOffsetBits` WARN was a false alarm BY CONSTRUCTION

`FNamePool: BlockOffsetBits = 16 (stock default kept; index 1 did NOT decode cleanly at this
width — name resolution may be degraded)` had fired on **6 of 6 sessions** since audit #5 G4
introduced it, across **four games and three engine versions** (ES2 UE5.06, Elliot UE5.04, P3R
UE4.27 fork). It has never once been about the game it fired on. `FName[1]=''` is in **every log
this repo has ever produced**, including the pre-G4 ones that did not probe at all.

**An FName index is a stride-quantised BYTE OFFSET inside its block, not an entry ordinal:**

```
vendor .../Core/Private/UObject/UnrealNames.cpp:592
    return FNameEntryHandle(CurrentBlock, ByteOffset / Stride);
:679  return *reinterpret_cast<FNameEntry*>(Blocks[Block] + Stride * Offset);   // no validity check
:518  enum { Stride = alignof(FNameEntry) };
:253  static constexpr uint32 FNameBlockOffsetBits = 16;   // compile-time, no #ifndef
UnrealNames.inl:12  REGISTER_NAME(0,None)
```

Entry 0 is always `"None"` and occupies `prefix + 4` bytes — always **more than one stride unit**.
So index 1 addresses byte 2 of block 0: the `'N','o'` **inside the "None" string**. Not inference —
our own `ValidateGNames` DEBUG dump had been printing the proof all along, byte-identical on five
games across two engine generations:

```
1E 01 | 4E 6F 6E 65 | 10 03 | 42 79 74 65 50 72 6F 70 65 72 74 79 | C0 02 | 49 6E 74…
hdr=4 |  N  o  n  e | hdr=12|  B  y  t  e  P  r  o  p  e  r  t  y | hdr=11|  I  n  t
idx 0 --------------- idx 3 --------------------------------------- idx 10 ----------
```

The probe read `0x6F4E` as a header: lenA `(0x6F4E>>6)&0x3FF` = 445, lenB `(0x6F4E>>1)&0x7FF` =
1959, both outside the accepted `1..256` → `len = 0` → WARN, unconditionally, forever. The
hash-prefixed layout fails identically (byte 4 → `'n','e'` → 405 / 695). **In both stock layouts
the first valid non-zero index is 3.**

**DELETED, not moved to index 3** — that is the tempting fix and it is wrong.
`BlockBitsAreIndistinguishable(3, 16, 14, 2)` is still **true**, so index 3 measures the width no
better than index 1 did; it would merely start *succeeding*, printing a confirmation beside a
number nobody measured. That is precisely what G4 removed. A regression guard pins it. G4's
conclusion stands — there is no reliable offline discriminator — so the width is now logged as the
engine constant it is.

**The second copy is why this was a code change and not a one-line deletion.** `Init` and
`InitObfuscated` each hand-wrote the *same* wrong index in their verification line — that is where
`FName[1]=''` came from — and fixing only the WARN would have left the defect visible in the line
directly below it (working-lessons §2.17). Both now call one helper sampling the **derived** first
boundary, `Serie::FirstEntryAfterNone(noneOffset, stride)`; `DetectStride` already computed
`noneOffset` and threw it away. `GetString(0)` is dropped from the line too — it short-circuits
`nameIndex <= 0` to the literal `"None"` without touching memory, so it verified nothing.

⚠ **Derived, never a literal 3.** Both stock layouts give 3, which is exactly what makes a
hardcoded 3 look safe — but MindsEye's obfuscated fork inserts a 2-byte tag (`payloadGap = 2`),
giving **4**. A literal there would be this bug again with a new number. That is the load-bearing
test. `InitUE4` is deliberately untouched: in `TNameEntryArray` mode an index *is* a pointer-array
ordinal, so its index 1 is a genuine second entry — said so in the header so nobody "fixes" it.

**Name resolution was never degraded, and the logs said so one millisecond later**:
`UE5_Init: Name sanity: 10/10 objects resolved` on 9 of 9 FNamePool sessions, 38 whole-pool
censuses with 0 `named`/`nonNull` mismatches. Three direct 16-bit proofs the WARN made everyone
doubt — DQ7R resolved `/Script/Engine` at `idx=387225`, Avowed at `idx=24315`, DumperTest at
`idx=200623`, all far above 2¹⁴ and impossible to decode correctly at 14 bits.

**Verified live on DumperTest (UE504 Development config, 25,213 objects), TWICE — build 3368
(`bdb1f613`) and again after a clean recompile at build 3369 (`cbe02dfd`), byte-identical results:**
`BlockOffsetBits = 16 (… not detectable offline — audit #5 G4)` as **INFO**, **zero** FNAM
WARN/ERROR in either session, and `FName[3]='ByteProperty'` — the derived boundary resolving
in-process to exactly what the block-0 hex predicted. Name health `Loaded 25,213 named` of
25,213 = **100%**, `scanned=25213, nonNull=25213`, 154 `WalkClass:` lines with **0** empty names
(game-specific `BP_ThirdPersonCharacter_C` included). The only WARNs in either run are the ordinary
AOB candidate-rejection chatter (`ValidateGNames` / `ValidateGObjects` refusing bad candidates, and
the cross-module guard refusing `EOSSDK-Win64-Shipping.dll`). These runs also exercised the
`UE5-Extended` FUObjectItem layout at ItemSize 32 — a different one from the `Default`/24 the
reporting session used — and the second run's scan log is 12.7 KB against the first's 357 KB
because the hint cache hit (`GOBJ_ES53_1` / `GNAM_V1` / `GWLD_TQ_1`).

⚠ **Correction to this entry's first draft, because the wrong version would teach a future reader
to distrust every test binary.** It said the `-dirty` build stamp meant the fix was uncommitted and
"not reproducible from a clean tag". That is wrong. **The `-dirty` suffix is STRUCTURAL: any build
that bumps the build number stamps itself dirty.** `dll/CMakeLists.txt:43-52` decides it with
`git diff --quiet HEAD` over the *whole* tree, and the build has by then already written the bumped
`build_number.txt` — on both runs `git status` showed that file and nothing else. So `cbe02dfd-dirty`
means "the source of `cbe02dfd`, built with the counter one ahead", not "someone left changes lying
around". A genuinely clean stamp requires `-NoBumpBuildNumber` **and** an already-clean tree.

9 new assertions, `dll_helpers_test` 1684 pass / 0 fail. Controls: the `payloadGap=2` case fails if
the helper is constant-folded to 3; the idx-3 assertion fails if a probe is re-added there.
13/13 gates, `check_proxy_exports --artifacts` green, C# suite green.
⚠ `-Target Test` clobbered `dist/` with the 112,052,516 B non-trimmed build exactly as CLAUDE.md
warns; restored via `-Mode Publish -NoBumpBuildNumber` (54.7 MiB / 57,398,784 B, sha `bc91f375`).

### 2. `docs/pipe-protocol.md` said a synthetic hop was a real pointer edge

Found while clarifying why Locate-in-GWorld returned `AuthorityGameMode` rather than
`OwningGameInstance` on ES2 — that question was a non-issue (both are stock `UWorld` UPROPERTYs,
`World.h:1424` / `:1482`; both routes are co-optimal 2-hop chains and the winner is decided by
ascending field offset, 0x1A8 before 0x228, in `Ubel.cpp`'s "sort by offset **for clean display**").

The spec, however, described the `ok_via_level` recovery as *"finds the actor in `ULevel::Actors`"*
with *"The remaining steps are real edges"*. Both false since audit #5 F8 (build 3220):
`Level.h:429` declares `TArray<TObjectPtr<AActor>> Actors;` with **no `UPROPERTY`** (control:
`DestroyedReplicatedStaticActors` at `:886` has one), so the lookup was deleted outright and
`Aura.cpp:4085-4105` builds **both** leading hops as synthetic (`fieldOffset -1`, `WorldLevel` /
`LevelActor`, `element_index -1`). Not cosmetic: telling a reader an offset-less back-reference is
a real pointer edge is the exact premise behind `e88190ba`, where a hop believed to have a real
offset produced CE tables resolving into the world's **vtable**.

The sample response was the worse half and was not in the report — it showed
`PersistentLevel → Actors[12]` as an `ArrayProperty` at `field_offset 0x98` with `element_index 12`,
a step the forward BFS *cannot* produce since it enumerates `GetClassRefMeta`. Replaced with
`GameState → PlayerArray[0]` (both reflected UPROPERTYs, a real logged path) and an explicit
PLACEHOLDER warning on the numbers. `todo.md`'s still-open `ok_via_level` row carried the same
stale `Actors[k]`; the preserved *Original note* is left verbatim per convention with a warning
above it.

-----

## 2026-08-27 (later) - The crash fix turned into a hang, because a deferral said the second half was out of scope (build 3365, verified in-game 3366)

Build 3363 (previous entry) stopped OCTOPATH TRAVELER crashing with our `dxgi.dll` proxy. The game
then **hung with no window**, and its log stopped at `DllMain: auto-start thread created OK`.

That line being last is diagnostic by itself: a thread created inside DllMain cannot run until the
loader lock is released, so the auto-start thread never printing its own first line means **the
loader lock was never released**. `tools/pe/minidump_triage.py` walks the FAULTING thread and a
hang has none, so `tools/verify/hang_dump.py` was written to dump a live process and census every
thread's stack by module.

The dump named it exactly:

```
ntdll+0x6019B                  <- RtlAcquireSRWLockExclusive's wait (ZwWaitForAlertByThreadId)
dxgi.dll+0x2ACC90              <- our own .data: the SRWLOCK object itself
dxgi.dll+0x18993F              <- our thunk, SECOND pass
AcGenral+0x5D78/5C00/22E2/5420, apphelp+0x1E696/1F81F     <- this block appears TWICE
dxgi.dll+0x1889CC              <- SetAppCompatStringPointer thunk, FIRST pass
```

with all three DllMain-created threads parked in `ZwWaitForSingleObject` behind the loader lock.

**Our own `LoadLibraryW` re-enters us on the same thread.** Loading the real `dxgi.dll` makes the
loader raise `apphelp!SE_DllLoaded`; `AcGenral`'s DXGICompat hookset does
`GetModuleHandleW(L"dxgi.dll")` — which resolves back to **us**, because we are the module
registered under that name — and calls our thunk again while the resolver is still inside
`LoadLibraryW`. **SRWLOCK is documented non-recursive.** Self-deadlock.

### The part worth keeping

That lock is audit #4 **B43**, which removed it from the winmm twin for the sibling lock-order
reason and left dxgi alone, noting the dxgi original's safety argument was *"explicitly CONDITIONAL
on RHI init being the only entry point"*. Build 3363's own audit doc recorded finishing it as
**"NOT done — deliberately out of scope"**. It was not out of scope; it was the live defect, and
the deferral cost a full extra round trip through a real game. Logged as a new row in
[working-lessons.md §2.13](working-lessons.md) — *a deferral reason ages worse than the finding it
defers* — because the specific mistake was rating a **sibling's already-proven finding** as
theoretical in the flavour it had not been applied to.

### What shipped

- **The SRWLOCK is gone.** Correctness without one is B43's argument verbatim: `mProcs[]` stores
  are aligned and pointer-sized so they cannot tear, racing resolvers write identical values, and
  "nobody observes a half-populated table" is preserved by the thunk's own null test. The log line
  is claimed once with `InterlockedCompareExchange`, after the work — the winmm shape.
- **`Lugner::ResolveReentry`**, in all four flavours. A nested call returns at once; its thunk then
  sees an unresolved slot with `mResolveAttempted` still 0 and answers 0 **without forwarding** —
  exactly what ReShade's compat exports do. The **outer** call completes the resolve and forwards
  for real. ⚠ Deliberately not a "first caller wins" mutual exclusion: a loser returning with a
  null slot would hand the *game* a null `CreateDXGIFactory1`. Removing the lock is the cure; the
  guard is defence.

### Verified in-game (build 3366, 2026-08-27)

```
10:17:32.450 [WARN]  Proxy: 1 forwarded call(s) arrived BEFORE our CRT was initialised …
10:17:32.452 [INFO]  DllMain: auto-start thread created OK
10:17:32.456 [INFO]  dxgi proxy: lazily forwarded 20/20 exports to real System32 dxgi.dll
10:17:32.475 [INFO]  DllMain ProxyStart: proxy DLL mode — starting pipe server only (no scan)
10:17:32.984 [INFO]  DllMain ProxyStart: pipe server started
10:18:13.670 [SUMMARY] GObjects=0x7FF659775C10 GNames=0x20440C60010 GWorld=0x7FF6598590F8 Objects=406060
```

Two lines to actually read. The **pre-CRT warning is still there and that is the point** — it is
the shim engine's direct fingerprint, so the root-cause analysis is no longer inference. And
**`20/20` lands at `.456`, before `proxy DLL mode` at `.475`**: the resolve completed on the
*loader thread*, inside the shim's outer call, and only then did the loader release and let our
threads run. Under 3363 that resolve never returned.

⚠ `version.dll` / `dinput8.dll` have had no in-game regression run since 3363 — and they are also
the two flavours the offline rig provably cannot discriminate pre/post fix on. Queued in todo.md.

-----

## 2026-08-27 - The AppCompat shim calls our export before our CRT exists; four proxies fixed, and one "fix" that would have killed the export (build 3363)

OCTOPATH TRAVELER would not start with our `dxgi.dll` proxy deployed, while the **same 2.9 MB
binary** as `winmm.dll` worked in the same game. Root cause confirmed at instruction level from
two crash dumps and the shim engine's own disassembly; full dossier in
[audit-2026-08-26-dxgi-appcompat-crash.md](audit-2026-08-26-dxgi-appcompat-crash.md).

The game carries an AppCompat layer (`HKCU\...\AppCompatFlags\Layers` = `HIGHDPIAWARE`), so
`apphelp.dll` + `AcGenral.dll` load at module [4]/[5] — ahead of `msvcrt`. `AcGenral`'s
`NS_DXGICompat` shim does `GetModuleHandleW(L"dxgi.dll")` →
`GetProcAddress("SetAppCompatStringPointer")` → call, driven by `apphelp!SE_DllLoaded`, which the
loader raises when a module is **MAPPED** — before its init routine runs. Our export for that name
was a lazy asm thunk whose resolver logs; logging allocates; `__acrt_heap` was still NULL;
`HeapAlloc(NULL, 0, 0x20)` faulted in `ntdll!RtlAllocateHeap+0x54` and the process died before the
EXE entry point.

`winmm` survives because `AcGenral` names `dxgi.dll` / `ninput.dll` / `d3d9.dll` and **not**
`winmm.dll`: nothing enters our winmm thunks until the game's own code runs, ~2.0 s after DllMain.

### What shipped, across all four flavours

- **`Lugner::g_crtReady`** — a plain `volatile LONG` with a *constant* initialiser, so it lives in
  zero-filled `.data` with no dynamic initialiser and is readable before the CRT exists. Set by
  `Lugner::MarkCrtReady()` as the **first statement** of `DLL_PROCESS_ATTACH`.
- **A pre-CRT gate on every resolver** — `DxgiProxy_EnsureResolved`, `WinmmProxy_EnsureResolved`,
  `LoadRealVersion`, `LoadRealDinput8`. ⚠ It **refuses outright** rather than doing the
  kernel32-only resolve the plan called for: that would have been allocation-safe but *not*
  loader-lock-safe, and the pre-CRT caller is the loader itself. Nothing is latched, so the host's
  own first call resolves normally. This is what ReShade does.
- **`Lugner::g_preCrtCalls`** — an `InterlockedIncrement` counter reported once from DllMain as a
  `LOG_WARN`. Without it the refusal is completely invisible: no log, no crash, nothing.
- **The thunks now tell two null cases apart**, via a new `mResolveAttempted` data symbol. This is
  the part that needed care: the obvious "return 0 on null" would have silently reversed B44/B48's
  recorded decision, where a stub returning 0 is *worse* because winmm's `0 == TIMERR_NOERROR`
  would make a missing `timeBeginPeriod` silently no-op the 1 ms tick. `mResolveAttempted == 0`
  (resolver refused) → `xor eax,eax / ret`; `== 1` (name genuinely absent) → keep the deliberate
  loud `jmp rax` with `rax == 0`.

### The change the fix forced, which was not in the plan

`Lugner.cpp` (17 sites) and `Lugner_Dinput8.cpp` (6) cached with
`static auto fn = reinterpret_cast<Fn>(RealProc("..."));`. **A magic static evaluates its
initialiser exactly once and latches the result forever** — so adding the gate above without
touching them would have latched `nullptr` on a pre-CRT first call and left the export dead for
the life of the process. That is a *worse* failure than the crash being removed: silent, permanent
and invisible. All 23 became `static Fn fn = nullptr; if (!fn) fn = ...`, which also drops the
CRT's `_Init_thread_header` machinery off a path that must not need the CRT.

### An unrelated defect found on the way

`scripts/gen_proxy_forwarders.py --check` reported **`Lugner_Winmm.cpp STALE`** *before* any of
this work: the AD18 `SystemDllPath` fix had been hand-applied to the generated .cpp and never
back-ported, so re-running the generator would have silently reintroduced the
drive-root-relative-path defect — and that check is **not in CI**. AD18 and the new changes are
both in the generator now; `--check` is clean.

### Verification

`tools/verify/proxy_precrt_gate.py` maps a proxy with
`LoadLibraryExW(..., DONT_RESOLVE_DLL_REFERENCES)` — image mapped, exports callable, **DllMain not
called** — and calls a thunk from a child process so a fault is an exit code. The pre-fix
`out/proxy-backups/Avowed.dxgi.dll.20260823-212124.bak` faults `0xC0000005` at **`+0x187A2E`, the
same RVA on the faulting stack of both Octopath minidumps**; `dist/proxy/dxgi.dll` returns cleanly
at `+0x1889CC`. Same result for winmm. 13/13 gates and
`check_proxy_exports --artifacts` pass.

⚠ The rig provably **cannot** discriminate pre/post fix for `version` / `dinput8` (plain C
forwarders, not asm thunks — their pre-fix path does not fault in this harness), and it says so
rather than printing a green tick. Their gate is verified by construction only. **The in-game
Octopath run is still owed** — see todo.md.

-----

## 2026-08-26 (night) - The three log-audit findings, fixed; an adversarial design review changed the shape of two of them (build 3362)

The three open findings from the P3R log audit (previous entry). Each was designed, then faced a
safety lens and an honesty/scope lens before a line was written — and that was not ceremony: five
of six verdicts came back fatal, and two of the three fixes are **not** the shape I would have
shipped from my own analysis.

### `[SANEPROPS]` — one constant answering two questions

`kMaxSanePropertiesSize = 1 MB`, whose comment claimed *"even the largest generated Blueprint /
UClass layouts are at most tens of KB"*. P3R refutes it with `XRD777SaveGame` (3,671,800) and
`AstreaSaveGame` (3,671,816), both of which the same log shows walking their fields cleanly.

Split into `kMaxGapFillBytes` (1 MB, **unchanged** — the byte-sweep work cap the 827 MB Elliot
wedge exists to stop, and also a WIRE bound, since `GuessGapTypes` emits ~N/4 rows for an N-byte
gap) and `kMaxPlausiblePropertiesSize` (**64 MB** — admission control for caches that are never
erased). ⚠ 64 MB is an admission bound, not a fact about UE; it sits ~17× above the largest real
sample and ~13× below the observed garbage, and both samples are in the header so the next person
re-derives rather than re-guesses.

⭐ **My own analysis had two errors, and the review found both.** I concluded "nothing scales with
PropertiesSize after the gate" — wrong: gap-fill emits one row per 4-byte stride, so a 3.67 MB
class is ~918,000 rows serialised with no cap. (The split still holds, because that site uses the
work cap — but I knew that by luck, not by measurement.) And I recorded the cache refusal as
"only costs a re-walk". It does not: `WalkClassEx` refuses to **RETURN**, handing back an empty
`ClassInfo`, and Aura's caches read `WalkClassEx(cls).Address != cls` as the refusal signal — so
both `USaveGame` classes were invisible to **Value Search, Group Scan, snapshot capture, CE export,
Solitar and Solide**, with no log line at all. That refusal now logs.

⚠ **Writing the test reproduced A10 by accident.** One class blob reused across four cases had
every case after the first read the first one's memoised answer, because `s_walkClassExCache` is
keyed by address and nothing erases it. One blob per case, and the comment says why.

### `[FUNCDENOM]` — a denominator that counted classes contributing nothing

*The wire field was not the problem.* `scanned_classes` is correctly named, correctly documented,
and one of the three UI sentences ("N classes scanned") was already reading it correctly. What was
wrong was every sentence phrased as **provenance**.

So the contributing count is **derived** — a computed property over the rows the panel is
displaying — with **no new wire field**. Two reasons, both found independently by the reviewers: a
parsed field would be `0` in every VM fixture (they all override `ListAllFunctionsAsync` and bypass
the parser), and a `??` fallback fires on *absent*, not on *zero*, so a DLL that shipped the key and
missed the assignment would render *"9,760 functions from 0 of 2,293 classes"* — a new wrong report
at the one site the fix exists to protect.

⚠ The DLL's log line is **append-only** on its format string: nothing in `dll/src` carries
`_Printf_format_string_`, so MSVC diagnoses no format/argument mismatch anywhere here, and a
reordered trailing `%s%s` reading an `int` as a `char*` is an access violation on the pipe worker
inside a shipping game.

### `[STALLDEFAULT]` — a default posing as a measurement

Fixed by **withholding** `game_thread_stalled` when it is not a measurement — the cheapest correct
answer, not a compromise: absence is already a live wire state and it costs fewer bytes.

⚠⚠ **The naive version of this fix is a regression, and the review caught it.** The hook can go back
DOWN mid-session (`Frieren.cpp` → `Stark::RemoveHook()` on a validation failure). Today a banner
raised by a real stall is cleared **by the lie** — the next `false` clears it. Withhold the key
without teaching the client that absence *withdraws* the claim and that banner sticks ON for the
rest of the session. DLL half and client half therefore landed in one commit.

A tri-state **string** was rejected on a measured path, not a guess: `GetValue<bool>()` throws
`InvalidOperationException`, `PipeClient`'s read loop catches only `JsonException`, so the throw
escapes, the loop exits, and every in-flight request fails with "Pipe disconnected" — it would
**disconnect** an old client, not degrade it.

`get_diagnostics` had a **second report path** with the identical defect, rendered to the user as
"Responsive" in the System tab. It gains `liveness`; `responsive` stays unchanged so an older UI
does not start rendering "Stalled" on a healthy game.

Three rigs relied on the lying default and were **armed, not weakened**. ⚠ One of them,
`seethrough_arm_b.py`, read the key with `[...]` **between `suspend-tid` and `resume-tid` with no
try/finally** — a `KeyError` there would have left the game thread SUSPENDED and the process
needing a kill.

---

**Method note worth keeping.** The `PERF` denominator fixed in 3361 and `[SANEPROPS]` are the same
sentence in different files: *a fix that landed in one of its two copies*. In 3361 it was `busyMs`
corrected and `dispatches` two lines below it not; here it was `PathStepToBreadcrumbs` marked and
`PopulateFromWorld` not. Three instances in one day, in three unrelated modules. The grep that
finds them is aimed at the CONCEPT, not at the diff.

C++ suite green (265 + 1649 + 22 + 14 + 17), C# **4,750/4,750** with six negative controls across
the three fixes, gates **13/13**.

## 2026-08-26 (evening) - The P3R log confirmed the fix; auditing the same logs found two more reporting defects, and named the title that closes row 5 (build 3361)

The maintainer re-ran the reported game. `Logs\P3R` (build 3360, `e88190ba-dirty`, proxy DLL,
UE427, 65,021 objects) against the 3358 session on the **same actor in the same game**:

| | 3358 (broken) | 3360 (fixed) |
|---|---|---|
| nav | `NAV→ KernelActor addr=0x64AF68C0 off=0x0` | `NAV→ KernelActor addr=0x101646B80 off=none` |
| spine | `KernelActor(P,0x0,68C0)` | `KernelActor(P,none,6B80)` |
| export | `bcCount=2` | `bcCount=1` |
| AOB | `AOB=True` | `AOB=False` + `AOB requested but root is not GWorld` |

⭐ **`AOB=True → False` is the sharpest line in the whole affair**: build 3358 wrapped the bogus
chain in a **GWorld AOB script**, i.e. it made the wrong address *survive a restart*. And the
"before" artifact was on disk the whole time — `Y:\Copy CE XML.xml`, saved 09:47 — which measures
identically to the after: root `gworld_addr_9E30D6`, then `KernelActor (0)` at `+0`, and the
actor's real address `64AF68C0` appearing **0 times in 1,288 entries**. That is precisely what the
unit test asserts when the row marker is removed.

**Then the logs were audited rather than merely read** — five diverse lenses over both sessions, a
refute-mandated skeptic per finding (19 of 40 killed), and a completeness critic. It changed the
conclusion twice, in both directions.

⚠ **Two corrections to what this session had already claimed.**
1. "The two sessions are apples-to-apples" was **too strong**. They differ in injection mode
   (proxy vs auto-start), AOB hint-cache state (`scan #1` vs `#17`), `array_limit`, and whether
   AOBMaker was up. The narrow comparison — same PE `69CE343916376000`, same actor, same class,
   same gesture — holds, and that is what should have been said.
2. The P3R logs witness the **mechanism**, not the **bytes**: no export's XML content is ever
   logged, and a `bcCount=1 / AOB=False` pair is also what a **GameEngine-rooted** export would
   print — and that session rooted one on GameEngine at 10:50:13. Here the same line's `BC=` trace
   names a two-crumb GWorld spine, so it is disambiguated; but the signature alone is not.

**That second point was a real gap, and it is now closed in code.** The re-anchor announced itself
only through `StatusText`, which reaches no log and is overwritten by the next status. `LogReanchor`
now says it outright at both export sites:

```
CEXML re-anchored: dropped 2 offset-less hop(s); root is now (level actor) @ 0x2B8B783A8D0
                   (absolute, session-only — no pointer chain from GWorld exists)
```

**Two reporting defects fixed, and both are the same shape as the bug that started the day.**

⭐ `[PERFDENOM-2026-08-26]` — the `PERF` line printed three numbers over **two denominators**:
`11 dispatches` beside `busy 15.2 ms` beside `per call: dll 1.524`, where 15.2/11 = 1.38. `busyMs`
is summed from the per-command rows, which deliberately skip the probe's own `get_diagnostics` —
there is a comment saying so, added when a 57.7 ms operation reported "161.2%" because the probe
was measuring itself. `dispatches`, **two lines below it**, still read the global counter. The
divisor was always `dispatches − 1`, on two games and two builds. *The fix landed in one of its two
copies* — the same sentence as the GWorld defect, in a different file. The existing test
**pinned** it (`R(3, …, one call)` asserting `"3 dispatches"`), sitting directly beside the test
that made exactly this correction for `busyMs`.

`[PERFPEAK-2026-08-26]` — `max` was a running high-water mark, so it was the worst call *since the
last reset*, not the worst in the operation; two exports minutes apart printed a byte-identical
`max 3.5ms` while their totals moved. `TopDeltas`' own comment said *"let the label carry the
caveat"* and the label said `max`. It now reads `top (peak = session high-water, not this op)`.

⚠ Fixing that label broke `Split_never_goes_negative…`, which asserted **no `-` anywhere in the
line** as a proxy for *no negative number* — and `high-water` has a hyphen. The proxy was corrected
to the actual predicate rather than the wording bent to fit it.

**Row 5 is closed, and the log tree named the title.** Three lenses guessed "needs a
World-Partition game"; the critic found the reproducer already on disk —
`Logs\TQ2-Win64-Shipping\ui-view-20260823-091415.log` from five days earlier, showing
`(world level)(S,0xFFFFFFFF,A960) > (level actor)(S,0xFFFFFFFF,FA60)`. Two `0xFFFFFFFF` hops in a
real spine, and nobody had ever pressed Copy CE XML on one — which is why the third defect stayed
latent. Re-run on **Titan Quest II** (UE507, 279,587 objects; the same `Actor` → 3 results → 🌍
Locate): the hops now render `none`, the export re-roots (`dropped 2 offset-less hop(s)`), and the
emitted table has **777 `<Address>` entries, exactly one absolute** — the level actor's own — with
`FFFFFFFF` appearing **0** times anywhere in the file. AA Script: `define(Actor,2B8B783A8D0)`.

Three findings stay **open** and are recorded in [todo.md](todo.md): `[SANEPROPS-2026-08-26]`
(a 1 MB sanity ceiling rejects two legitimate ~3.5 MB P3R SaveGame classes, and the *instance*
walker uses the same predicate to declare a live object stale), `[FUNCDENOM-2026-08-26]`,
`[STALLDEFAULT-2026-08-26]`.

Suite **4,738/4,738**, gates **13/13**, `dist/` republished AOT-trimmed (54.7 MB, sha `25e65203`,
byte-identical to the native output).

## 2026-08-26 (later) - The GWorld actor-chain fix, driven on a live game; and the sentinel was logged as `0xFFFFFFFF` (build 3360)

**Live half of `[GWORLDACTORCHAIN-2026-08-26]`** (previous entry). Rows 1–4 of its verification
register were run end to end against a running **DumperTest Shipping** (UE504, **24,479 objects**,
`dist/UE5Dumper.dll` injected, GWorld resolved by AOB `GWLD_TQ_1`). Full evidence is in
[todo.md](todo.md); the short form:

- The Offset column shows `—` for every Outer-derived actor/component and `0x30` for
  `PersistentLevel`.
- Copy CE XML from `PlayerStart0`: of **382** `<Address>` entries the copied table carries **exactly
  one** absolute address — the actor's own, as the root. The UWorld address appears **0** times.
- Copy CE AA Script: *"hardcoded address (GWorld path not forward-walkable)"*, script body
  `define(PlayerStart,1872FEDBE00)`, no GWorld walk.
- Control: `GWorld → PersistentLevel → LevelScriptActor` still emits the AOB-wrapped restart-stable
  chain with real offsets and no re-root. The fix did not over-reach.

⭐ **Two things made this cheap, and both are reusable.** The defect lives in `PopulateFromWorld`,
which consumes the DLL's `walk_world` reply and never looks at the engine version — so the
**already-granted DumperTest fixture stands in for the reported P3R (UE4.27) session** and no new
computer-use grant or purchased title was needed. And `clipboardRead` is granted, so the emitted
table was **read back as bytes** instead of being eyeballed in Cheat Engine: every claim above is a
count over the actual XML, not a reading of a screenshot. Most "needs CE" export rows are like this.

⚠ **One real find from the live run, and it was in the LOG.** The first pass printed the no-offset
sentinel through `0x{-1:X}`:

```
NAV→ PlayerStart addr=0x1872FEDBE00 off=0xFFFFFFFF ptr=True
```

That is meaningless as an offset **and** confusable with `+FFFFFFFF` — the emitted-table defect the
sentinel exists to prevent. A future reader grepping the logs for `FFFFFFFF` would have hit a
**correctly marked** hop and read it as the bug still present. `FormatCrumbOffset` now prints
`off=none` at all six sites (nav, struct-nav, synthetic-container rehydrate, both export pre-checks,
and the breadcrumb trace). Logs are this project's primary evidence channel — a sentinel has to be
legible there too.

## 2026-08-26 - "Start from GWorld" published level actors as fields at offset 0; every CE chain through one walked into the world's vtable (build 3359)

**`[GWORLDACTORCHAIN-2026-08-26]`.** Reported on **P3R** (`UE427`, 65,158 objects, build 3358):
Copy CE XML from an object reached through the GWorld actor list produced *completely wrong*
addresses. CE resolved the emitted table to

```
base            P->603BB0A0   8 Bytes  0000000144AF6408
  KernelActor (0)  P->144AF6408
```

`0x144AF6408` is inside the executable image (base `0x140000000`) — it is the value at
`UWorld + 0`, i.e. the **world's vtable pointer**. The actor really lives at `0x64AF68C0`.

**Root cause — a fix that landed in one of its two copies.** Audit #5 F8/F9 established that
`ULevel::Actors` carries **no UPROPERTY**, so the actor list is reconstructed from each actor's
`Outer` (`Aura::FindActorsInLevel`): there is no offset from UWorld to an actor, and no element
index either. `LiveWalkerViewModel.PathStepToBreadcrumbs` **already knew this** — a `LevelActor`
path step is stamped `FieldOffset = -1`, `IsPointerDeref = false`, with a comment citing F8.
`PopulateFromWorld`, which builds the same hop for the *Start from GWorld* list, was never given
the marker: it published every actor and component as `Offset = 0` + `isPointer: true`, which is
a positive claim that the actor sits at `[UWorld + 0]`.

Three consumers believed it, and the third is the one that shows the shape of the bug best:

| | path | what it emitted |
|---|---|---|
| 1 | Copy CE XML / Copy CE Field | `[GWorld] + 0` → the vtable (the report) |
| 2 | Copy CE AA Script | `gworldWalkable` requires `spine.Skip(1).All(bc => bc.FieldOffset >= 0)` — **the gate was already correct**; the crumb lied to it with a `0`, so it emitted a restart-stable walk into the vtable |
| 3 | Locate-in-GWorld → export | that path *does* stamp `-1`, and nothing downstream handled it: `$"+{step.Offset:X}"` formatted it as **`+FFFFFFFF`** |

So the marker existed, the gate that consumes it existed, and the emitter that had to survive it
did not. Row 3 was latent the whole time.

**Fix.**

- `LiveFieldValue.HasNoParentOffset` — "this row is not reachable from the current object by a byte
  offset". `Offset` deliberately **stays 0** (bookmarks and the same-layout row-reuse path key on
  it); the flag is what stops it being read as `+0`. `PopulateFromWorld` sets it on actors and
  components; `PersistentLevel`, which IS a reflected field of UWorld, keeps its real offset.
- Navigation stamps the existing `-1` sentinel rather than inventing a second representation, and
  skips `MapValueDrillOffset` so the sentinel cannot be turned back into a number.
- `CeXmlExportService.AnchorAtLastUnchainableHop` — re-roots the spine at the **deepest** hop with
  no offset and drops everything above it. Idempotent (index 0 is never examined, because a root's
  offset is not applied by any emit path), so the VM and the generator can both call it. Both
  export commands do, and say so: *"⚠ Chain re-rooted at KernelActor (absolute address,
  session-only)"* — the trade is restart-stability for correctness, and the user is told.
- `ProjectBreadcrumb` now **throws** on a negative offset instead of formatting it. That is what
  kills row 3, and it is a reachable invariant, not dead code: with the re-anchor removed, the
  export fails with *"spine still contains an offset-less hop 'KernelActor' … was not applied"*.
- The Offset column renders `—` instead of `0x0` for such a row.

⚠ **The offset column's Binding change broke its sort, and `DataGridSortWiringTests` caught it in
the same run.** `SortMemberPath="Offset"` was rooted by the column's own `Binding`; pointing that
at `OffsetDisplay` un-roots it, and under trimming the header goes inert. This is the **third**
disguise of that trap recorded in `LiveWalkerPanel.axaml.cs` (after a template-column conversion
and an element-syntax `MultiBinding`) — and here sorting on the display string would have been
*worse* than inert: it orders hex text, putting `0x9` after `0x10`. Fixed with a wired
`DataGridSortComparers.Number` entry.

**Verification.** `ui/UE5DumpUI.Tests/LiveWalkerGWorldActorChainTests.cs` — 7 checks driving the
real production path (stubbed `walk_world` → row → breadcrumb → clipboard XML), with **five
negative controls, each isolating one half**: remove the row marker → 4 red (and the copied XML
contains **no trace of the actor's address**, which is the reported defect reproducing); remove the
export re-anchor → 1 red, via the generator guard firing in production; remove the generator guard
→ 1 red; revert the offset display → 1 red. Full suite **4,734/4,734**, gates **13/13**.

⛔ **Not yet confirmed on P3R itself** — see `[GWORLDACTORCHAIN-2026-08-26]` in
[todo.md](todo.md)'s live-verification register.

## 2026-08-24 (later) - The C1 spawner fixture already existed; the repo's copy of the sample source did not know (build 3349)

**`[C1-SPAWNER-EXISTS-2026-08-24]`.** Found by accident while picking a queued-route fixture for
b636: `list_all_functions` on a running DumperTest reports **17** `DumperTestActor` UFunctions, and
they include `Spawn_Holders`, `Spawn_Decoys`, `Spawn_DestroyHolders`, `Spawn_CountHolders`,
`Spawn_Generation`, `Spawn_LateInstance`, `Spawn_RecycleChurn`, `Spawn_LastRecycledAddr` and
`Spawn_ManyComponents` — the entire set the classification doc drafted as **bucket C1, the one
fixture addition it said would unlock seven rows**, plus one it never asked for.

It is not a declaration stub: three `Spawn_LateInstance` calls moved the live
`DumperTestLateSpawn` population **2 → 5**.

⚠ **Two sessions concluded the opposite, including this one.** The check both made was to grep
`tools/ue-sample/DumperTest/Source/DumperTest/DumperTestActor.h` — which is dated **2026-08-19** and
contains none of it, while the packaged `DumperTest.exe` used for every verification run is dated
**2026-08-23** and contains all of it. **The repo's copy of the sample source is stale relative to
the binary that is actually used.** So grepping `tools/ue-sample` is not a valid way to answer "does
this fixture exist"; ask the running game. Earlier today I corrected an agent for saying the spawner
had shipped — the agent was right and the correction was wrong.

ℹ️ Recorded separately so it is not mistaken for a fixture defect: invoking any **parameterised**
`DumperTestActor` UFunction over the pipe returns `ProcessEvent error code -4`, while every
**zero-parameter** one returns 0. The split is exact and hits the pre-existing `AD4_*` functions as
hard as the new `Spawn_*` getters, so it belonged to the parameterised-invoke path, not to this fixture. **DIAGNOSED AND FIXED 2026-08-24** (build 3350): `invoke_function` sized its param buffer from the caller's `parms_size`, which **defaults to 0**, so omitting the field handed ProcessEvent a zero-length heap buffer and it wrote the return value past the end. Half the fault was mine for not sending the field; the other half is that the DLL had `ufuncAddr` and could read `UFunction::ParmsSize` itself, and the protocol doc called the field *optional (default 0)*.

## 2026-08-24 - The b637 return-value fix had a sibling it never covered: int64 (build 3348)

**`[RETINT64-2026-08-24]`.** Found while closing the b637/b644 verification row, not by looking for
it. That row asks a human to confirm a pointer return "shows a `0x` prefix" — a compile-time string
literal already pinned by C# tests, and blind to what build 637 actually fixed, which is a **read
width**: `readUFunctionReturn` has no `'pointer'` type, so the pre-fix spelling fell through to the
signed int32 default and read **four bytes of an eight-byte slot**.

Replacing that check with one that measures the width (`scripts/tests/return_read_test.lua`, the real
helper driven against byte-accurate stub memory) immediately showed the fix was incomplete.
`BakedScriptGenerator.cs:331` is `readType = displayType == "pointer" ? "qword" : displayType` — it
rewrites **only** `"pointer"`. `MapToHelperType` also emits `"int64"`, from two routes
(`Int64Property` at :522 and the size-8 `EnumProperty` case at :519), and that word reached the
helper verbatim where **no `'int64'` branch existed**. Measured: `0x0000000123456789` read back as
**591751049**. `UInt64Property` was unaffected — it maps to `"pointer"` and so rode the fix.

Fix: `'int64'` joins the 8-byte branch. **No sign folding**, and that is a property of the width
rather than an oversight — Lua integers are 64-bit two's complement, so the bytes CE returns already
*are* the signed value (`FF FF FF FF FF FF FF FF` -> `-1`, measured). At 64 bits `'int64'` and
`'uint64'` read the same bits and only the caller's format specifier differs; the 32-bit case is
different precisely because CE widens 4 bytes into a positive Lua number, which is AA20.

Two things worth carrying forward. **`-1` is a bad discriminator** — it passes even with the fix
removed, because a 4-byte signed read of `FF FF FF FF` is also `-1`; the test needs a value wider
than 32 bits. And **a Lua-only fix does not ship until the UI is republished**:
`scripts/ue5_invoke_helper.lua` is an `EmbeddedResource` of the UI (`UE5DumpUI.csproj:146`), served
through `GetManifestResourceStream`, and is not copied into `dist\` as a loose file. Verified after
republishing by byte-searching the AOT exe for the new branch.

## 2026-08-23 (evening) - The proxy advisory hid winmm; found by reading import tables, not code (build 3337)

**`[PROXYALTWINMM-2026-08-23]`.** Working the A6 offline bucket — the "Lushfoil proxy did not load"
row — the useful move was to stop reading our code and parse the games' PE import tables with
`tools/pe/pe_imports_exports.py`. Over every UE shipping `.exe` installed on this machine:

```
16 shipping exes:  14 import winmm   ·   13 import dxgi   ·   4 import version   ·   0 import dinput8
```

`ProxyImportAnalyzer.DescribeImportable` built its `alt:` list from **dxgi and dinput8 only** — so it
recommended the flavour **nothing** imports and suppressed the one **almost everything** does, even
though the analyzer has parsed `ImportsWinmm` since 2026-07-27 (`a2c81a0c`, *"teach the analyzer
winmm"*), winmm is one of the four proxies we build, and the class's own remarks group `dxgi`/`winmm`
as the *"pure static-import hijacks"* — the deterministic pair. On the 13 games importing both, the
user saw `alt: dxgi` and was never told winmm was equally available.

⚠ **Root cause is `working-lessons.md` §2.3, verbatim.** `ImportsWinmm` was appended to the record
**with a default** — the only defaulted member — so all four `Recommend` tests construct three
positional arguments and silently assert the no-winmm case. `DescribeImportable` was edited *again*
on 2026-08-10 (`c28e3a78`), two weeks after winmm was taught, and still not updated. The test file's
comment still read *"none of OUR three"*.

⚠⚠ **And the structural guard I wrote for it was VACUOUS on its first draft.** It asserted
`Display.Contains("winmm")` — but the corrected empty-case sentence is `no dxgi/winmm/dinput8`,
which *contains* `winmm`, so with the fix removed the guard **still passed** while the two
hand-written cases failed. Only the negative control exposed it. It now matches inside the `alt:`
segment. Final control: dropping the winmm line fails **all three**; restoring returns 51/51.

Suite **4,712 / 0 failed**, **13/13** gates, `dist/` republished AOT-trimmed.

ℹ️ **What this did NOT settle.** The Lushfoil row itself stays open. Offline forensics established the
proxy is present, correctly placed beside `LushfoilSim-Win64-Shipping.exe`, the right flavour (78
`version.dll` exports) and dated 2026-08-19, and that the exe was **not** patched (2026-02-22) — but
the exe never imports `version.dll`, which `ProxyImportAnalyzer`'s own remarks already document as
**normal and non-diagnostic** for that flavour (Lushfoil is named there among 11 games running a
working version proxy without the import; the load is a run-time `LoadLibrary`). So the filesystem
cannot say why it failed on 2026-08-21. **Actionable instead:** Lushfoil statically imports both
`dxgi` and `winmm`, either of which loads deterministically rather than riding a run-time
`GetFileVersionInfo` call — which is exactly what the advisory now surfaces.

## 2026-08-23 (later still) - AC13's untested metric gets two tests, and the route that does not work is written down (no build change)

**No product change; `build_number.txt` stays 3335/3336** — this adds tests only, and the final shape
needed **zero** production edits.

`PipeTransportStats` appeared in **no test source at all** — the sibling of the same defect family
(`ClassifySendFailure`) is tested only because it was split out as a pure function, while AC13's fix
is a `try`/`finally` **placement**. `[AC13-2026-08-22]` had already found the live row unobservable,
so the metric had no coverage of any kind.

**Two deterministic tests, each negative-controlled against a deliberately broken build:**

* the not-connected guard sits **above** the timer, so a refusal that sent nothing logs no 0 ms
  sample — control: moved the timer above the guard, **only that test failed**;
* `Snapshot()` is monotonic and converts ticks→ms correctly (record exactly `Frequency/100`, expect
  10 ms) — control: factor 1000.0 → 500.0, **only that test failed**.

Both controls reverted to an empty diff and a green 2/2. Suite: **4,709 tests, 0 failed, 34.7 s.**

⛔ **The positive half is still uncovered, and the attempt is recorded rather than repeated.**
`SendAsync` needs a live connection to reach the timer at all. `Constants.PipeName` is a hardcoded
`const`, so a test server would bind the name a running game's DLL also serves — the hazard behind
*"never run `pipe_client.py` while the UI is connected"*. An injectable `pipeName` ctor parameter was
prototyped and then **reverted with the test**, rather than left in production justifying something
that no longer exists. With it in place, `PipeClient.ConnectAsync` **reproducibly never completes**
against an in-process `NamedPipeServerStream`, while a raw `NamedPipeClientStream` with identical
arguments connects in **0.15 s** — measured at `maxNumberOfServerInstances` 1 and 4, on and off the
xUnit sync context, always with the server's own `WaitForConnectionAsync` reporting completed. The
dialled name was confirmed from the client's own log line, and a `PipeClient` aimed at an unserved
name throws at its 5 s timeout as designed. Not diagnosed; not an AC13 defect.

⚠ **The lesson that cost the most.** The first draft had an unbounded `await` in a helper, so it
**hung with no message** — and that is what made the full suite report `4708 succeeded / error: 1`
with **zero failures listed** after the host was killed. Bounding every await (working-lessons §2.7,
*a hang is not a test result*) turned it into a 10 s failure naming the exact line, which is the only
reason the wall above could be characterised instead of guessed at. The clean suite now runs in
**34.7 s** — the earlier 9m46s was entirely the hang.

## 2026-08-23 (later) - A sibling clip found by sweep, and the sweep became gate 13 (build 3336)

**`[DUMPHDRCLIP-2026-08-23]`.** Closing the classification doc's A6 item *"`[FORCESTATUSCLIP]`
sibling `.axaml` sweep"*. `DumpExplorerPanel.axaml:28` carried `TextTrimming="CharacterEllipsis"` as
the last child of a **horizontal `StackPanel`, behind four fixed-width buttons** — the exact
structure fixed on 2026-08-22 elsewhere. A `StackPanel` hands each child its **desired** width, so
the trimming can never fire and the text is hard-cut with no ellipsis and no tooltip.

The tail is again the part that matters: `BuildHeader` emits
`UE {ver} · {module} · … · {DumpedAt}`, so the first thing lost is the **dump timestamp** — the field
that says whether the dump is stale. Fixed like the precedent: `DockPanel`, buttons `Left`-docked,
`HeaderText` as the fill child, plus `ToolTip.Tip`.

⭐ **The narrowing is the reusable part.** The naive query — bound `TextBlock` in a horizontal
`StackPanel`, no tooltip — returns **138 hits**, nearly all short scalars (`PoseX`, `ArrayLimit`).
That is `working-lessons.md` §2's "~52% wrong" shape in miniature: a real structural pattern with no
severity filter is noise. The discriminator is **the author's own `TextTrimming`** — its presence
says they expected clipping and asked for an ellipsis, and the layout makes it impossible. 138 → 1.

⚠ **A dropout dropped for the wrong reason.** `ValueSearchPanel.axaml:694` has `Width="520"` and is
genuinely fine. But `MainWindow.axaml:338/353` were excluded by a first draft that examined only
**direct** children; their tooltips sit on the wrapping `Border`. The correct rule is *a tooltip
anywhere up the ancestor chain*, and the draft would have hidden a real case nested one level
deeper. The shipped check walks ancestors for `ToolTip.Tip` **and** `Width`/`MaxWidth`.

**New gate:** `tools/check_inert_trimming.py`, wired into `check_all.py` — **13 gates now**. This
defect class has shipped four times (`FORCESTATUSCLIP`, `V8PREVIEWCLIP`, `TYPECOLCLIP`,
`DUMPHDRCLIP`), which is what makes a one-off sweep the wrong deliverable. Negative-controlled
against the pre-fix file via `git show HEAD:…`: it reports the hit there and none on the fixed tree.

⚠ **Stop quoting the gate count** — it has been 4, 12, and now 13. The handover row and the memory
index were changed from a hard number to *"derive it from `N gate(s) run`"*.

## 2026-08-23 - The freeze abandon modal blamed the wrong cause; `ue5_freeze_helper.lua` -> 1.5 (build 3335)

**`[FREEZEFIRSTERR-2026-08-23]` — found by a verification row, not by an audit.** Closing AA3 step 5
(*"a permanent rescan failure must stop the writes"*) on a live suspended DumperTest, the abandon
modal read:

```
[ue5_freeze] DumperTestHolder: 3 consecutive rescans failed -- freeze STOPPED writing
(last error: mailbox busy (concurrent invoke or rescan)). This record has been unticked...
```

The row still PASSED — the freeze did stop, untick and print exactly once. But the **reported cause
was a consequence**, and structurally so:

* `waitDone`'s timeout path is `if not wok then return nil, 0, werr end` and does **not** clear
  `OFF_CMD` — deliberately, because the DLL may still write its reply later;
* so rescan #1 times out with `cmd` left set, and rescans #2 and #3 short-circuit on the in-flight
  guard (`:645-650`) in microseconds;
* `_lastError` was overwritten by each, and the message reported the **last** one.

So for the entire *"the DLL took the command and wedged"* family — a suspended process, a hung game
thread — the modal was **guaranteed** to name a transient concurrency cause for a permanent fault,
in the one place a user ever reads it, and to discard the timeout's actionable hint
(*"stale g_invokeMailbox address? re-inject, or re-enable the table"*). That is the distinction
CLAUDE.md's *"never report a mailbox failure by guessing"* exists to preserve.

**Fix:** track `_firstError` (set only on the 0→1 transition, cleared with `_lastError` on any clean
rescan) and report it, appending a differing latest one:

```
... freeze STOPPED writing (first error: mailbox timeout after 5008ms -- the DLL never picked
this up (stale g_invokeMailbox address? re-inject, or re-enable the table); then: mailbox busy
(concurrent invoke or rescan)).
```

⭐ **The live modal was reproduced in the rig before anything was changed.** `freeze_helper_test.lua`
AA31 stages the real sequence — a healthy start, then the fake DLL is killed by mutating the captured
`installMailbox` opts — and printed the byte-identical `(last error: mailbox busy …)`, failing. Its
control (every failure identical) passed throughout, so the case discriminates.

**Helper version 1.4 → 1.5, and the bump is load-bearing**: `versionLess` makes a same-version
re-load a **no-op**, so a 1.4 chunk already resident in a user's CE table would never have received
this. Two copies of the version exist (the doc block at `:90` and `THIS_HELPER_VERSION` at `:273`);
both moved.

⚠ **The bump broke two AA30 cases and silently weakened a third**, which is the more reusable
lesson: they hard-coded `'1.4'`/`'1.5'`, so with the file at 1.5 the *"an OLDER file does not
downgrade a newer resident"* case was re-loading a **same-version** file and passing on the wrong
branch entirely. AA30 now **derives** `OLDER`/`CUR`/`NEWER` from whatever the helper declares.
Negative-controlled: forcing `OLDER = CUR` makes the replace case fail.

Suite: **159 checks, 0 failures**. Gates: **12/12**. `dist/` republished AOT-trimmed — the helper is
an `EmbeddedResource` (`UE5DumpUI.csproj:150-152`), so the shipped exe carries its own copy and
*"Export Freeze Helper Lua File…"* would otherwise have kept writing 1.4.

## 2026-08-22 (later) - Eleven more register rows, the twelve-gate discovery, and a new single-entry handover (no build change)

**No source change to the product.** `build_number.txt` stays **3315**; the only code added is a
tooling script and two verification rigs. The entry below covers the work done *after* the one above
was written.

### ⚠ The finding that matters most for anyone reading this next

**CI runs TWELVE doc/source gates before it builds, plus a thirteenth over the built proxies — and
this session ran FOUR of them all day**, because four is what the docs named. `tools/check_all.py`
now runs the twelve in CI's order (which is not alphabetical: `aob_specificity` reads the TSV that
`extract_patterns --check` writes) and reprints CI's own failure text, so a local failure reads like
the one that would have appeared on the PR.

⭐ **Its first run failed** — `CeLuaQuotingTests` carried a literal user-home path (`<user home>\O'Brien\UE5Dumper.dll`) — quoting it verbatim here trips the same gate, which is a fair demonstration —
and `check_no_local_paths` rejects a concrete user home in a tracked file. That test had been
committed hours earlier against a green four-gate run. The apostrophe was the point of the fixture;
the home directory never was. Now `D:\O'Brien Studios\…`, with a comment saying why.

### Register rows settled

| row | outcome |
|---|---|
| `A6` step 3 | ✅ the Force walks the **super-chain, not the name** — `StaticMeshActor` (derives from `AActor`, name does *not* start with "Actor") **is** held, while 33 diffable non-derived objects including the genuine same-prefix `ActorSequence` are **untouched**. The pair is the proof; either half alone is consistent with the wrong matcher |
| `A6` step 5 | 🟡 CDO half ✅ (CDO clean through a 256-instance hold, with 12/12 sampled live components **actually** forced as the channel proof); spawn half **blocked**, measured three ways — the debug camera is one-shot per process, cycling it gives 295 → 295 objects, and no `ConsoleCommand`/`RestartLevel` exists in the 3,142 functions listed here |
| `AC13` step 1 | ✅ closing a **connected** UI leaves no `Pipe: ReadLoop error`; non-vacuous because `ui-init-0.log` shows the logger alive and flushing at the exact moment (`UE5DumpUI shutting down...`) |
| `AC13` steps 2–3 | ⛔ **unobservable as written** — there is no IPC figure on the System tab, and step 3's own action destroys its observable: the probe's closing `GetDiagnosticsAsync` sits under `catch { return; }`, so closing the game mid-request suppresses the PERF line entirely |
| `B10` | ✅ CLOSED — capture 644 objects / 12,155 fields, `wall 638.6 ms` recorded as a **new baseline** (the only prior figure is a different target and not comparable), and struct / enum / bool all decode. ⭐ Discriminating because the grid prints the **raw byte beside the decoded name**: `03 → ROLE_Authority` and `01 → DORM_Awake` are two different enums each decoded right |
| `A3` | ✅ CLOSED by step 3, and the 2026-08-19 measurement **reproduced to the digit** across a different build (3,450 candidates · 72 · 34 · 19 · 19) |
| `G11` steps 3–4 | ✅ answered as a measured **negative**: over 66 detection attempts across 23 hosts, `Tier 2` has **never** fired and `Tier 3` has **no subject**. The channel is proven — the ladder reached Tier 1 six times on three games |
| `AF25` step 3 | ✅ teleport opcode still 8 and the mirror **is** guarded — a negative control (`CmdTeleport = 9`) leaves the contract gate `CHECK OK` but fails **6 tests** |
| `V6/U8` step 1 | 🟡 attempted, **not closed**, and deliberately no defect filed — see below |

The 繁中 checklist went **38 → 33**.

### ⚠ Two rows where the *instrument* was the problem, not the product

* `V6/U8` — the Live Walker toolbar **reflows** once an object loads (`Find Refs` / `Related`
  appear), so coordinates captured earlier in the same session go stale silently. Two "press ▼"
  clicks landed on the **"2 matches" label**. The claim "the stepper stopped working after
  auto-refresh" was one sentence from being filed; it is unverified because the actuator was never
  shown to fire in that state. `working-lessons.md` §2.5d.
* `A3` step 1 — following the 繁中 step literally (*Value Search, **Float***) gives **0** vector
  components, a clean-looking FAIL of a working fix, because under LWC an `FVector` is a
  double-precision `FVector3d`. The warning was already in `todo.md` from 2026-08-19. **Read the
  register entry before running the mirror's step.**

### Docs brought to current

* **`docs/handover-2026-08-22.md`** — a new single entry point, replacing both predecessors, which
  moved to `docs/archive/`. It carries what a fresh session needs in its first ten minutes: the exact
  **computer-use grant list** (20, with their `request_access` names), the **≤7 batching rule** and
  the correction that **grants persist across sessions** (measured — a *reboot* is the real
  invalidation), `systemKeyCombos` being ungranted, **which Steam process is which**
  (`steam.exe -applaunch` to launch · `steamwebhelper.exe` to see the library · the
  `*-Win64-Shipping.exe` to inject, with a measured shim table for all nine granted titles), the
  dead-engine trap, the hard rules, the build/test/gate commands with their timeouts, how to drive
  Cheat Engine, what is open with a derivation for every number, and how to close a row.
* `todo.md`'s header was stale in two measurable ways (build **3263**, "30 open batches" against a
  derived **15**) — the file that says *"read this when deciding what to do next"*. Fixed.
* The OPEN FIXES INDEX heading said **3** while the table needed a fourth row
  (`[FORCESTATUSCLIP]`); added, with the observation that **none of the four is a straightforward
  code fix**.
* CLAUDE.md's `Schlacht` capability row still described the pre-fix hit resolution and claimed a
  verification that `[SEETHRUNOOP]` had just invalidated. Rewritten.
* The 繁中 checklist's own derivation instruction said to subtract **the two** preamble headings; a
  third was added on 2026-08-22, so following it literally now yields 34 against a correct 33.
  Reworded to count them rather than name a number.

-----

## 2026-08-22 - Unattended verification session: 8 defects found and fixed while working the register (builds 3309 → 3315, 42 commits)

**Shipped**: `build_number.txt` **3315**, `dist/` republished as the Native-AOT trimmed binary
(54.7 MB, `sha 8CA03D81BAAB`) with the DLL rebuilt alongside. Not a feature session — every fix below
was found by *running a verification row*, not by looking for bugs.

### The defects, in the order they surfaced

- **`[CADENCEGAP]`** — `Linie` dropped every same-millisecond inter-arrival sample (`>` where `>=`
  belonged), so a 17 ms callback reported the same 33 ms period as a 33 ms one. Seen in the UI:
  `CameraModifier` 496 calls / 17 ms against `ABP_Manny_C` 248 calls / 33 ms — twice the calls, half
  the period, where both had read 33 ms.
- **`[PARAMSSORT]`** — three grids sorted the **label** (`"9 (144B)"`) instead of the number, so
  Params ordered lexicographically. Fixed at all three sites, plus an address column that sorted as
  text. Two new AOT-sort guards; the click-through on the trimmed binary showed columns that
  actually discriminate (Params `19,19,19,17,17,16,15,15`; Offset `0x28…0x64`).
- **`[FREEZECFGNAME]`** — the freeze script bakes the class name into its runtime messages while the
  freeze itself reads `CFG.className`, and the product *instructs* the user to edit that CFG. Editing
  it made every message name the class you had just replaced. ⭐ The defect had been **pinned by a
  test** that used the baked literal as a convenient anchor, which is why it looked deliberate.
- **`[INVOKEHINTQUOTE]`** — an unescaped apostrophe (`"read it in CE's memory viewer"`) interpolated
  into a single-quoted Lua literal made the **whole `[ENABLE]` block a syntax error**, and Cheat
  Engine reports that by leaving `Active` at `false` with no dialog and nothing in the log. Any
  invoke with a large by-value struct return was affected. New `CeLuaQuotingTests` runs **19
  generators** through a Lua quote scanner — behavioural, because the offending value came from a
  variable ten lines above its use and a grep of the emitting line found nothing.
- **`[FORCESTATUSCLIP]`** *(filed LOW, not fixed)* — the Force status line is right-clipped at ~30
  characters with no trimming or tooltip, and the clipped tail is the clause whose own code comment
  says it exists because "on 256 instance(s)" reads as "all of them" without it.
- **`[SEETHRUTALLY]`** — `InvokeSetHidden` returns `bool` and **both call sites discarded it**, so
  `hiddenActors` / `hidden_count` / the UI card / the log's `disabled (N restored)` all reported
  **intent**. `Tick()` now records only what was applied.
- **`[SEETHRUNOOP]`** — and what that honesty exposed: on **UE 5.4** the object read out of
  `FHitResult` is a `UStaticMeshComponent`, so `InvokeSetHidden`'s Actor guard rejected every hit and
  **See-through was a complete no-op while every channel said it was working**. Fixed by trying
  `Actor` → `HitObjectHandle` → `Component` and taking the first that resolves to an actor
  (`ResolveToActor` walks `Outer`, bounded). ⭐ Cannot regress a working build: the first two members
  are still tried first, and `ResolveToActor` is the identity at hop 0 for anything already an actor.
- **`[DISTCOPY]`** — `build.ps1` reported a successful publish over a copy that never happened. A
  locked destination left `$exitCode` at 0, `Remove-Item` then deleted the correctly-built binary, and
  the success line printed the **stale** file's size — which is 54.7 MB exactly like a good one. The
  copy is now verified by **SHA256** and `dist/publish/` is kept when it fails.

### Verification closed

`Y10/Y13` (all four steps) · `MB3` (the CE half, through real `.CT` records) · `AA12/AA13` ·
`AE4–AE7` · `M1–M5` steps 2/3/4/5 and step 1's arms (c)+(d) · `B19` · `AF7` · `AF22/AF12/AF13` ·
`AC17` · `AF21` · `A11` · `A12` · `V11` · `Y12` · `W8` · Genau RIP decode (GNames + GWorld).
The 繁中 checklist went **50 → 35** sections.

### Two capabilities added because a row could not be verified without them

- **`seethrough_get_state` now returns `hidden_actors`** — the count alone could not be audited from
  outside, and it cost a full round of guessing (33 candidate actors walked, none hidden, no way to
  tell a wrong candidate set from a failed hide) before the set was exposed and the answer appeared
  immediately.
- **`tools/verify/seethrough_arms.py`** — two independent detectors that refuse to report a pass when
  the count never rises or when the DLL names actors whose own `bHidden` is not set.

### What this session is really about

Six of the eight defects are the same shape, and it is the one audit #4 named: **the report and the
reality computed by different code paths**. `[SEETHRUTALLY]` is the extreme case — the "reality" path
did not exist at all, which is exactly why the feature could be dead for as long as it was. Every one
of them was caught by insisting on a **second, independent witness** for a claim the system makes
about itself.

-----

## 2026-08-20 (evening) - Fourth pass on 3263: the UI and Cheat-Engine modes, 3 more defects, AA12/AA13 opened up

**No source change.** `build_number.txt` is **3263** and the tree is clean. ⚠ The caveat from the
entry below still stands and is worth repeating because it changes what a build means today:
`dist/UE5Dumper.dll` **was rebuilt** for the PEHOOK step-3b experiment (identical source), so its
bytes differ from the handed-over 3263 — and **`proxy_refresh.py` therefore reports all nine
deployed proxies as `*** STALE ***`, which is a FALSE ALARM**. `dist/UE5DumpUI.exe` was **not**
rebuilt and is still the 54.7 MB AOT-trimmed binary, which is what made the AOT-sort work below
possible without a rebuild.

This pass changed **mode** twice rather than finding more headless rows: first driving the real UI
with computer-use, then driving **Cheat Engine**. Both were productive, and the CE half found three
defects in one sitting.

### The UI pass — one batch closed, several completed

* **`PEHOOK` step 1** — the last non-UI-blocked step. Self-Test on a SIB-less build gives
  `✗ Add_IntInt(3,4) expected 7, got 0` with advice naming a **MIS-DETECTED vtable slot**. ⭐ Clicking
  it a second time returns a **completely different** string — *"The DLL REFUSED this call"* — because
  the validator had condemned the slot in between. Two state-appropriate advices from one button
  minutes apart is the row's headline claim (*the advice is chosen from `get_diagnostics`, not
  asserted*) demonstrated rather than argued, and it is the UI face of the `-3` refusal measured
  headlessly in step 3b.
* **`PEHOOKONCE` step 5 — batch CLOSED.** On a fresh Lushfoil the UI genuinely shows
  *"Connected — waiting for scan"* with **Start Scan** unpressed, so the pre-scan window is real in
  the UI and not only over the pipe. Live Funcs → Start before any scan gives the actionable
  *"Run a scan, then Start again"*; after a scan, Start again **without restarting the game** records
  **67 distinct functions / 98,236 calls**. The order-swap that used to poison the PE hook for a whole
  process now recovers inside one process.
* **`PASTECRASH` 4b + 4c** — and they turn out to be **two separate handlers**. The Copy button logs
  *"Clipboard copy FAILED — nothing was copied, the app is unaffected"* while the
  `Input-layer fault swallowed (#N)` counter does **not** move. 4b's safety half passes; its predicted
  undo residue did **not** occur (6 typed characters took exactly 6 `Ctrl+Z`), so the row's
  explanation of that residue should be treated as unconfirmed.
* **AOT sort — 2 grids → 8**, all on the `-Mode Publish` binary because a non-trimmed build passes
  with the bug present: Live Funcs, Detect Stats, Live Walker, Snapshot, Class Pivot, SPC Query.
  ⭐ SPC's is the largest sort exercised anywhere in the item — **12,153 rows** — which is where a
  per-row reflective comparer would be most visible. The **Props/Invoke picker** is the only named
  grid left and has **no fixture here** (it returns zero rows on every function tried).
* **`AF4`**, **`AE2`/`AE3` steps 1-3**, **`AF22`**, and **`L6`'s `X5` auto-snapshot clause** all pass.
  AE2's filter is recorded as the row demands (`DumperTest` → 22 results, 10 class-like then 12
  instance rows) and the three headers it discriminates on are mutually unmistakable — 1760 / 12 /
  928 properties.

### The Cheat-Engine pass — `AA12`/`AA13` finally reachable, and three defects

Launching CE flips the UI to **AOBMaker Connected**, which is what *enables* the per-row **Freeze**
button (`PropertySearchPanel.axaml:285`). That single fact unblocked a batch that had **six steps and
zero evidence**.

* **`AA12`/`AA13` step 1 — PASS**, with the release as its control: `TickCount` held at **9999** across
  10 s on a field that had been climbing ~1 Hz, the Lua window auto-closed, the record stayed ticked,
  and unticking let it resume **9999 → 10039 → 10048**.
* **Step 6 — PASS.** A float and a double freeze coexist (`555.5` / `777.75`); unticking one released
  **only** that one. The differing widths mean a shared write path would have shown up as one
  clobbering the other.
* **Step 2 — HALF.** The message half is exactly right (*"nothing was frozen: g_invokeMailbox symbol
  not found -- is UE5Dumper.dll injected?"*); the untick half fails.
* **Step 3 — the fixture was wrong**, and finding that out was the result: `NiagaraComponent` previews
  as `0 (CDO default)` but the freeze reports **2 live instances**.

**Three defects, all with reproducers:**

* **`[FREEZEUNTICK-2026-08-20]`** — a bail-out that applies nothing leaves the record **`Active=true`**,
  on **both** the helper-missing and DLL-not-injected paths. The generator *does* emit
  `if memrec then memrec.Active = false end`, and assigning the same property externally works — so it
  is the in-ENABLE assignment that does not survive. This is precisely what CLAUDE.md's rule forbids,
  and it makes `AA12`/`AA13` step 3 non-discriminating until fixed.
* **`[FREEZEINJECT-CRLF-2026-08-20]`** — "Inject Freeze Helper" reports
  `wrote 58345, stream has 57208`. The arithmetic is exact: the helper is **58,345 bytes with 1,137
  CRLF endings**, and 58,345 − 1,137 = 57,208. CE stores it LF-normalised; the check compares a CRLF
  byte count to an LF stream. **The write succeeds** (`findTableFile` → FOUND) — only the verification
  is wrong, but it tells the user the setup step failed.
* **`[CDOSCOPE-2026-08-20]`** — the `(CDO default)` marker is decided on an **exact** `ClassPrivate`
  match while Force and Freeze on that same row scope **derived**. A row can read "nothing live" and
  the action then hit two instances. Same exact-vs-derived split as `FREEZESCOPE` / audit #5 `A6`, one
  layer up.

### Method notes worth keeping

* **Read `memrec.Active` from CE's Lua Engine, never from the checkbox** — measured this pass: red ✗
  is **ACTIVE**, empty box is inactive. Reading the ✗ as "failed" would have inverted every finding
  above.
* **Check CE's title bar before ticking.** The process list reorders between openings, and one attach
  landed on `UE5DumpUI.exe`; a freeze against the wrong process fails in a way that looks like a
  product defect.
* **The CE-Lua hygiene contract is confirmed live**: `[Freeze] Started/Stopped` lines are silent by
  default and appear only after `UE5_DEBUG=1`.

-----

## 2026-08-20 (later) - Third verification pass on 3263: ST1 and PEHOOK closed out, 1 new defect, a §4.4 retirement, and one void run caught

**No source change.** `build_number.txt` stays **3263** and the tree is clean.
⚠ **Correction to the entry below it: `dist/` is NOT untouched any more.** PEHOOK step 3b needs a
DLL whose SIB pattern alternates are disabled, and the row sanctions building one; that build and
the restoring rebuild both overwrite `dist/`. Source is identical (git clean, `-NoBumpBuildNumber`
on both), so the binaries differ only by build non-determinism — but they are **not** byte-identical
to the handed-over 3263, and `proxy_refresh.py` now reports all nine deployed proxies `*** STALE ***`
as a result. **That report is a false alarm; do not act on it** (it compares SHA-256, and the sizes
still match exactly).

**Hosts, one at a time, each killed when its rows were done:** DumperTest Development (shipping DLL
*and* a SIB-less variant injected by path), **DQ7R**, **Lushfoil**, **EVERSPACE 2**, **Echoes of
Aincrad Demo**, **Satisfactory**.

### Two batches closed

* **`ST1` — all six steps, headless.** Two techniques made a batch whose table repeatedly says
  *"a paused/menu game"* and *"ordinary gameplay for a few minutes"* runnable with nobody present:
  **suspending the UE game thread** is a scriptable and strictly *stronger* form of every idle-game
  precondition (frozen = exactly 0 ProcessEvent fires, where backgrounding still ticks ~120/s), and
  where a log line structurally could not decide a step, **an observable side effect in memory
  could** — steps 4 and 6 were settled by watching `AActor::bHidden` flip after a resume, not by an
  absent log line.
* **`PEHOOK` — 1b/2/3/3b/4/5/6/7/8.** Only the UI Self-Test *text* (step 1) and the structurally
  unreachable 3c remain. Step 3 reached its **terminal 3/3 + "giving up"** state for the first time;
  step 5's false-positive guard was exercised for the first time (previous runs only showed the
  branch was never entered).

### One new defect

**`[INVOKEINHERIT-2026-08-20]`** — `Ubel::WalkFunctions` never climbs `SuperStruct`, so
`UE5_FindFunctionByName` can only resolve a function a class **declares**. `AActor`'s 140 functions
are unreachable on every subclass; 11 of 42 live objects can invoke *nothing at all*; and
`UE5_SetDebugCamera` resolves `ToggleDebugCamera` off the live CheatManager's class, so the shipped
Debug Camera toggle fails on any game with a derived CheatManager. Reproducer committed
(`invoke_inherited_function.py`, exits 1 while the defect stands). **Not fixed** — and the fix shape
carries a warning: do *not* make `WalkFunctions` inherit, because it is also what LISTS a class's
functions; the change belongs in the resolvers.

### A documented rule retired, and a blast radius measured

* **`working-lessons` §4.4's Everspace 2 evidence is withdrawn.** It recorded a KismetMathLibrary
  no-op diagnosed *while the hook was in the wrong vtable slot*, with the stub hypothesis "never
  re-verified against the corrected hook". It has now been re-verified on that same title:
  `Add_IntInt(3,4)` returns **7** at `vtable+0x278`. The failing pattern the section rested on has no
  surviving instance here.
* **The open `Serie::GetString` dropped-`Number` lead now has a measured blast radius** (it was
  explicitly "not measured"): 40 of 42 live objects are named differently by two pipe commands, and
  6 of 6 are **unfindable by the name the DLL itself reports** — the bare name silently resolving a
  *different* object. 71 call sites, 4 of which pass a Number. Still "do not sed it": the
  discriminator is display/identity vs matching.

### One run caught and voided

**`G3` steps 3+4 on Satisfactory produced a full set of coherent readings from a game that had never
booted.** Launching its shipping exe directly puts up *"Failed to open descriptor file
`../../../FactoryGameSteam/FactoryGameSteam.uproject`"* — UE resolves the `.uproject` relative to the
exe, and this title's exe lives in `Engine\Binaries\Win64\`. Injection, the pipe, and the scan all
succeeded against a dead engine, and the numbers looked *specific*: GNames and GWorld resolved (symbol
exports work as soon as DLLs are mapped), `GObjects=0x0`, and `ExtraScanGObjects: No valid
FUObjectArray found (763 candidates tested)`. Relaunching via `steam.exe -applaunch 526870` resolves
**all four globals, 137,425 objects** — with `gobjects` at **the exact address the failed run had
found and rejected as empty**, which isolates "array not populated" from "wrong address" by holding
the address constant. Satisfactory is exonerated, `test-games.md` was right, and **G3 3+4 have no
fixture on this machine at all**. Written up as `working-lessons` §3.w.

### Also settled

`AA2/AA3` step 3 (mailbox driven from Python, witness checked against `ReadProcessMemory` across 29
classes — and the row's assertion narrowed, because a derived listing's `classWitness=0x0` is
*correct*) · `U8` on a staged fixture (`Number := 8` written and restored; Value Search matched the
same 8 bytes, the bare string matching the CDO instead) · `L4/MB1` both rows (the invoke ROUTE made
observable by freezing the game thread: 5 ms + success vs 5006 ms + timeout) · `L10` step 6's
retention clause (a 30-day `TeleportCoords` file survives while a 30-day `Snapshots` group is deleted
in the same launch — the control that makes it mean anything) · `G11` steps 3 and 4 (Tier 2 has now
failed to fire for three *different* reasons; an offline exe scan proves the silence is correct on a
tag-stripped title) · Solide `capped` and `FREEZESCOPE` step 4 (unlocked by noticing Solide's pool is
**not Actor-only**: an 819-instance `ActorComponent` sweep gives `held=256, truncated=true`) ·
`PEHOOKONCE` step 3 in its literal pre-scan form.

### Rig lessons worth more than the rows

`working-lessons` §1 now carries **four** variants of one mistake, all found in this batch: a log
window coarser than the events it separates *reports a confident wrong answer*. Line-count slicing
across several growing files; a one-second timestamp watermark between cells milliseconds apart
(this one printed `FAIL` on a run whose own `result=-5` proved the opposite); a counter read outside
the timed window; and a byte offset recorded before a process start that **rotates** the log. The
reliable primitive is a before/after **count**. Also §1.y: `find_instances` without `exact_match` is
a *name substring* match, so "the first live instance of `Actor`" was a `UActorSequence` — and a
wrong-type invoke left queued can drain later against a non-actor.

-----

## 2026-08-20 - Second live-verification pass on 3263: 26 register rows settled, 2 new defects, 4 rigs (no build change)

**No source changed.** Everything below is `docs/todo.md` evidence, `tools/verify/` rigs and the
gitignored run ledger; `dist/` and `build_number.txt` are untouched at **1.0.0.3263**. Both CI gates
(`check_live_verification.py`, `check_audit_register.py`) stayed green after every commit.

**Hosts used, one at a time and killed after each group** (`working-lessons` §3.9): DumperTest
Development and Shipping, **The Adventures of Elliot** (UE504, 84,990 objects, `dxgi` proxy) and
**DQ7R** (UE**4.27**, 149,408 objects, `version` proxy) — the first UE4 title driven end to end in
this programme, and the largest install on the machine.

**Two new defects, both reproducers committed.**

* **`[STAGELOCK-2026-08-20]`** — `AC11` step 2's premise is wrong. `CopyProxyStaged` publishes with
  `File.Move(overwrite:true)`, and a target carrying an **image section** refuses the replacing
  rename with `ERROR_ACCESS_DENIED` (5), not the `ERROR_SHARING_VIOLATION` (32) the old direct
  `File.Copy` raised. .NET turns 5 into `UnauthorizedAccessException`, which is not an `IOException`,
  so it misses both arms of `DeployAsync`'s filter and the user is told **"Access to the path is
  denied."** — a message naming no path — instead of "File locked (game running?)". Established three
  independent ways: an OS-level probe (`tools/verify/ac11_locked_rename.py`), a throwaway xunit test
  against the real `CopyProxyStaged`, and finally **a live game** (Elliot running, Force Overwrite →
  `[EROR] … Access to the path is denied.`, status `ErrorOther`). `UndeployAsync` and the orphan
  sweep already carry the `catch (UnauthorizedAccessException)` arm; `DeployAsync` is the only one of
  the three without it, and the only one whose write became a rename.
* **`[ORPHANCANCEL-2026-08-20]`** (LOW) — cancelling a leftover-proxy cleanup mid-row leaves that row
  **untallied, still ticked, and its folder chain half-pruned**: the token is passed *into*
  `RemoveOrphanProxyAsync`, so it recycles the file and *then* throws, skipping `files +=`, `ok++`,
  `row.IsSelected = false` and `DropOrphanRow`. The log showed **three** recycles under a summary
  saying two. Audit #4's root cause verbatim — the report and the reality computed by different code
  paths.

**Batches closed.** Audit **L9** (AE13 / AE20 / AE30) is fully verified and its heading flipped, so
the register's open count went **40 → 39**. **L6** is complete but for the CE-only `X8` and the
maintainer-only `X12`; **`[AUTOREFRESH-2026-08-19]` is complete — all seven steps**; **F5** is
complete (steps 1–3).

**Things that could only be learned by running them**, now recorded next to their rows:

* `mtime` **cannot** witness a re-deploy — `File.Copy` carries the source's timestamp through
  `File.Move`, so the deployed proxy is byte-*and*-timestamp identical after a second deploy. Use
  `ctime`.
* `AUTOREFRESH` step 6's "drill → stays OFF" **expectation is wrong**: a field drill never calls
  `StopAutoRefreshTimer`, and what actually happens (re-target to the new root, 12 consecutive 10.0 s
  ticks) is better.
* `AC13` is **not observable as written** — the IPC figure only exists via `DiagnosticsProbe`, which
  returns early on exactly the disconnect the row prescribes.
* `AE27`'s Package filter is a **prefix** match on values beginning `//`, so `Script` matches nothing
  and reads exactly like the blank-memo defect it is meant to catch.
* `AF8`'s `force_field` takes **`kind`** (defaulting to `"bool"`), not `mode`, and a numeric `value`
  — the string `"-5"` parses to `0.0`, giving `held:0` that looks like a failed fix.
* A game window **steals focus back**, so a computer-use click on the UI behind it silently
  re-activates instead of pressing; `tools/verify/front_window.py` first.

**Rows that cannot be closed on this machine, with the measurement that proves it** — `G7`-style
results rather than "not tried": `Z8` (DQ7R's whole pool is 51,255 UFunctions, 51 % of the cap;
ratio ≈ objects ÷ 3, so ~300k objects are needed), `A7` (a full 149,408-object `FindByAddress` with
the deep cap at its 4,096 maximum takes **152 ms** — no window to disconnect inside), L3 step 1
(**none** of `GWLD_TQ_3/TQ_4/GOBJ_PS1/PS6` has ever won across **170 scan logs / 25 processes**),
`AD18`'s `dinput8` arm (**no** installed title imports the name, all 16 exes read), `Z13`/`Z12`-deep
and `AE30`'s control.

**New rigs:** `ac11_locked_rename.py`, `ac10_kill_midstream.py`, `ae20_orphans.py`,
`af7_af8_pipe.py`.

-----

## 2026-08-19 (night) - The first live-verification pass on build 3263: 13 register items settled, 2 new defects found, 12 rigs added (no build change)

**Nothing shipped and no source changed** — `build_number.txt` stays **3263**, `dist/` is untouched
(54.7 MB AOT UI, 2,879,488-byte DLL). This entry records the *verification* programme the handover
asked for, run unattended against the shipping build.

### Register items settled

| item | outcome |
|---|---|
| `AA38` 1/2/3 | ✅ re-confirmed on **3263** (the ✅ was earned on 3262). Refusal names `atcuf64.dll` — Bitdefender's own filter, present in every run on this machine |
| `B25` | ✅ **both** branches, on two purpose-built marker exes. 3,886 vs 10 log lines separates "swept the tables" from "refused before starting" |
| `Genau RIP decode` | ✅ both halves — candidates **4085 → 4083**, reproduced exactly over 4 runs; GWorld unmoved |
| `F5` 1+2 | ✅ 204,758 wire lines across two concurrent connections, 1,179 interleaved watch events, **zero malformed** |
| `MB3` | ✅ ordinary dispatch: 50 consecutive mailbox commands, 0 failures, 0 `[ERROR]` lines |
| `A3` 1/2/4 | ✅ **151 classes** contribute >1 FVector field. ⚠ the row's "use Float" is wrong on UE5 — LWC makes FVector a double |
| `FL1`/`FL2` | ✅ stale staging file swept, **fresh one survived** — the age guard proven, not assumed |
| `SE1` | ✅ announcement *and* **597 `[SCAN*]` lines** rerouted into `init-0.log` |
| `MB2` | ✅ foreground toggle 0→1→1→0 on a host with all three pointers `not_found` |
| `AB14`/`AB16` | ✅ 834 enum/byte candidates; the Origin filter **partitions 278+94=372 exactly** |
| `A6` step 3 | ✅ derivation, not substring — `Character`=1 vs `CharacterMovementComponent`=7, with a reachability control |
| `A8` | ✅ 7/7 on OCTOPATH. ⚠ the row's "(none available here)" was **wrong** — OCTOPATH is flat and installed |
| `A7` | ⛔ **not observable here, measured**: 0.11 s over 273,956 objects. Needs a pool ~100× larger |
| GROUP 7 | 🟡 **nine titles swept.** `U2` CPN **all-false** across UE4+UE5; **no Tier 2 line anywhere**; `X2`'s >5,000-class title found (Avowed `total_classes` 5,102) |

### Two new defects, both found in passing

* **`[RELAUNCHPIPE-2026-08-19]`** — a game that **relaunches itself** ends up with our DLL mapped and
  **no pipe server at all**. `UE5_StartPipeServer`'s one-shot `CreateFileW(OPEN_EXISTING)` guard
  races the dying first process. Reproduced 3/3 on OCTOPATH and **proven by repair**.
* **`[PROXYDEPS-2026-08-19]`** — six proxy objects carry no recorded header dependencies, so a `.h`
  edit may not rebuild the four shipped proxy DLLs.

Plus a blocking machine-state fact: **all nine deployed game proxies were stale** (pre-3263).

### Rigs added under `tools/verify/` (Python only — ad-hoc PowerShell is blocked here)

`inject.py` (with a stale-module guard) · `launch_dumpertest.py` · `build_dll.py` · `call_export.py` ·
`proxy_refresh.py` · `title_sweep.py` · `b25_marker_exes.py` · `genau_rip_ab.py` · `f5_envelope.py` ·
`mailbox_poke.py` · `fl_staging_sweep.py` · `se1_log_reroute.py` · `ab_radar_batch.py` ·
`a3_struct_path.py` · `a6_derivation.py` · `a8_flat_layout.py`.

### Method notes that cost real time and are now written down

**`working-lessons.md` §3.8** — this machine has **two Visual Studios**; `build.ps1` takes the newer
(MSVC 14.51), so pointing a builder at 2022's `vcvars64` mixes toolsets and fails at LINK with
unresolved `__std_rotate` in a file you never edited. **§3.9** — "one game at a time" is a
**correctness** rule: a second injected host logs `pipe already exists … skipping auto-start` and
never scans, so every reading from it is an absence the injection itself caused.

⚠ **Three quantities are all called "the UE version"** — the cached `ueVersion`, the
`FindAll: UE Version = N` log line, and `get_pointers.ue_version` (which is **after** any runtime
raise). Avowed detects 503 then raises to 504; comparing the wrong pair manufactures a G11 false
alarm, and did.

-----

## 2026-08-19 - Audit #5 closed out (166 → 4 of 297) + the field-reported defect queue (12 → 1), in 32 commits (build 3263)

**One unattended programme, one entry.** This is the rollup for the 32 commits between `9062f08f`
(build 3261, the A12 fix) and `10b00cf8`. Two queues were emptied in parallel:

| queue | before | after | what is left, and why |
|---|---|---|---|
| audit #5 register (`check_audit_register.py`) | **166 open of 297** | **4 open of 297** — 0 HIGH · 0 MED · 3 LOW · 1 INFO | `AB9`, `A10`, `AA39`, `AB23` — each left open **deliberately**; reasons below |
| the field-found OPEN FIXES INDEX in [todo.md](todo.md) | **12** | **1** of the original twelve (`[STALEDLL]`(a), a maintainer-only file deletion) | the audit itself then surfaced **3 new deferred items**, so the index reads **4 rows** — see "the index is 4, not 1" below |

⚠ **NOTHING in this entry has been verified on a running game.** Every fix is unit-pinned,
negative-controlled, or rig-covered offline; the live checks are queued in todo.md's
`## Pending live-game verification`, which grew to **40 open batches** as a direct result. Treat the
whole programme as *shipped and unproven* until that register moves.

### The twelve audit layers (L1–L12)

Audit #5 scanned the **48,950 lines authored before 2026-06-01** that audits #3 and #4 structurally
could not reach. Closing it ran as twelve batches, one per segment, each ending in a negative control:

- **L1** `9270046c` — DLL engine (`Ubel`/`Serie`/`Genau`/`Aura`). Enum reads of size 1 stopped
  sign-extending the UHT `MAX=255` sentinel into a miss; the `FString` count cap went 256 → 8192
  after re-deriving that it bounds only a *garbage* count's allocation, not the hot path;
  `TOptional<FText>` decodes through `ReadFTextString` instead of reading `FText+0x10`, which is the
  `uint32` Flags.
- **L2** `ab0fd6a6` — `Radar` value scan. `EnumProperty` is now scannable (mapped to `UInt8` and
  added to the `NumericAll` union); integer parsing is base 10 unless `0x`-prefixed, so a leading
  zero no longer means octal-for-ints and decimal-for-floats inside one meta scan; the group witness
  assignment gained an augmenting-path repair so one leaf can no longer appear in two slots.
- **L3** `cda2f720` — headers / `Himmel`. **The durable one:** nothing validated a signature's
  `(instrOffset, opcodeLen, totalLen)` against its own pattern bytes. Now enforced twice — by
  `extract_patterns.py --check` *and* by a compile-time `ASSERT_RIP_GEOMETRY` over all five tables —
  and negative-controlled 6 for 6 against four historical defects plus two invented slips. Four real
  RIP-decode offsets were wrong and are corrected (PS1 23→21, PS6 14→12, TQ_3/TQ_4 3→0).
- **L4** `569a1d59` — `Mimic`/`Sein`/`Flamme`. `HandleInvoke` routed on `functionFlags`, a
  DLL-filled **output** field, so a second CE FIRE routed on whatever the previous command left
  behind. Fixed by re-reading the flags from the `UFunction` rather than by promoting the field to
  an input — the latter would have been a **meaning** change the contract hash cannot see.
- **L5** `e8893e5a` — the three standalone CE Lua scripts, all covered by the real `lua` 5.4.6 rigs
  in `scripts/tests/` (dissect 50→83, freeze 132→154, invoke 63→91 checks).
- **L6** `6fc00e4d` — `MainWindowViewModel`. Disconnect now resets **all** process-scoped panels
  (was 3 of ~15); the three long exports thread a real `CancellationToken`; the Dump All completion
  line is composed from the actual `DumpResult` counts instead of the file's byte length.
- **L7** `9ef5b8ca` — UI services: data durability, honest failure levels, VDF parsing.
- **L8** `a87706c7` — VMs + scoring. Four findings turned out to be one theme — *a truncation or
  deadline signal exists and is discarded before the user sees it* — so the wording lives in one new
  `Core/PartialResultNotice.cs` rather than in four new spellings.
- **L9** `d4fdd418` — ViewModels/Core/DTOs. Two rows were closed by **re-deriving against current
  code** and finding them already fixed, rather than by re-fixing them.
- **L10** `ec72d7c0` — Views / app root. **A user-sortable DataGrid column is AOT-safe only if its
  Binding path equals its `SortMemberPath`, or a comparer is wired.** Swept the whole tree instead of
  trusting the list: 34 grids / 162 sortable columns / **30 unsafe across 10 sites**, of which the
  findings named six. `DataGridSortWiringTests` now machine-enforces the pairing offline.
- **L11** `179d2f80` — the last LOW batch. One theme runs through all twelve rows: **the report and
  the thing it reports on were produced by different code.** The worst was the DataTable RowMap
  drill, whose crumb printed the DLL's true row total over a grid holding a fixed 64-row page.
- **L12** `5374e662` — 25 of the 26 INFO rows. See the gate work below.

### Mailbox contract 2 → 3 (`2c2a950c`) — additive, `MAILBOX_CONTRACT_MIN` stays 1

`[FREEZESCOPE]`: a Property Search row for an **inherited** field is keyed to the class that
*declares* it, so freezing a pawn's `bCanBeDamaged` submitted `Actor` and the exact-name pool
returned one incidental `ChaosDebugDrawActor` out of a 25,179-object level. `CMD_LIST_INSTANCES`
gained an opt-in `LI_IN_DERIVED` flag routed to `Aura::FindInstancesDerivedFrom`, plus
`LI_OUT_TRUNCATED` when the pool is capped.

**Why this is additive:** `MailboxData` grew only at its tail (`cmdFlags` in, `cmdOutFlags` out), the
flag defaults to `0` = the old exact match, the handler clears it after every use so it cannot be
inherited, and the 16-byte derived-page format is unreachable without it. A pre-contract-3 `.CT`
keeps the exact-match pool byte for byte. `check_mailbox_contract.py`'s golden block records why.

The same commit fixed `[FREEZESTUCK]`: an abandoned freeze reported its failure with a `print()` into
a Lua Engine window hygiene had already closed, while CE still showed a ticked record — so the user
was told a cheat was applied while nothing was written. The record now unticks from a one-shot timer
(deferred, because `Active = false` runs `[DISABLE]` synchronously and that block calls `stop()`).

### Gate work — the part that outlives the fixes

Five gates got stronger, and one had a hole big enough to matter:

- **`check_mailbox_contract.py` could not see five of the six copies of the layout** (`AA36`). It
  hashed `Mimic.h` and read `CeMailboxLayout.ContractVersion`, and was blind to both standalone CE
  helpers, the shipped `.CT`, and both offline Lua rigs. **A gate with a hole reads as covered.** It
  now compares **67 literals across those 5 mirrors** against offsets *computed* from the packed
  struct, and the registry is **closed** — an unregistered layout-shaped constant is a hard failure.
- **`check_audit_register.py` did not enforce ID uniqueness** (`69fa412a`). `AE11`/`AE12` each sat on
  two rows, so marking one closed closed both in every derived count.
- **`check_axaml_strings.py` was red for four commits** and was wrongly written off as pre-existing
  and a false positive. It was neither: `ec72d7c0` had resolved a `StaticResource` key by
  interpolation, so eight real keys became invisible to static inspection — **exactly the property
  the gate exists to defend.** Fixed at the call sites, not by exempting the checker (`a1bdd205`).
- **New rigs:** `tools/verify/compile_sdk_header.py` puts the real emitter's output in front of
  `cl.exe` (nothing in this repo had ever *compiled* a generated header — they were only read), and
  `tools/verify/pe_pattern_regression.py` pins the ProcessEvent pattern set across 22 shipped games.

### Field-reported defects (the OPEN FIXES INDEX)

`[PASTECRASH]` took three commits and is the one worth reading: a clipboard read that failed inside
Avalonia's `TextBox.Paste()` surfaced as an unobserved dispatcher exception and **terminated the UI
31 minutes into a connected session**. The guard swallows only what a pure, narrow classifier
positively identifies as a platform input-layer fault — and `4365a1eb` reverses part of its own
predecessor on a weighing, not a fact: a swallowed `Ctrl+X` orphans an undo snapshot, but that is a
no-op `Ctrl+Z` against a terminated process taking a loaded object tree with it.

Also closed: `[SDKHDR]` (the array extent was baked into the *type* string, so 5 of 75,342 emitted
lines were not valid C++), `[PEHOOK]`/`[PEHOOKONCE]` (a failed ProcessEvent **detection** was
permanent; three sentinels now distinguish re-armable from terminal), `[PIPEBUSY]` (at-capacity
logged an ERROR every second — 1,826 lines in 31.5 min, evicting real diagnostics as the log
rotated), `[CLASSTOTAL]`, `[CONTAINERCAP]`, `[SLOTSYM]`, `[STALEDLL]`(b), `[PROXYLOAD]` and
`[AUTOREFRESH]`.

**The index is 4 rows, not 1.** The original twelve went to one — `[STALEDLL]`(a), deleting a
6-month-old `UE5Dumper.dll` from CE's install folder, which is maintainer-only. But the audit work
itself surfaced three new **deferred** items that now sit in the same table: `[PROPSEARCHCAP]` (a
feature — Property Search has no Max control), `[VOLUMEROOT]` (three sites ask `DriveInfo` about a
mount point and answer about the host volume) and `[SCANIDENTITY]` (needs a product decision plus a
live game with mid-scan object churn). None is a regression from tonight.

### The four audit findings left open on purpose

`AB9` — DllMain does filesystem work under the loader lock; a half-fix is worse than the defect, so
it needs its own session. `A10` — needs the by-reference→by-value restructuring `U5` deferred; a
partial invalidation would dangle held references. `AA39` — its prescribed fix was **measured to be a
no-op**; do not re-raise. `AB23` — interning `GroupSlotMatch::ownerClass` touches `Aura.cpp` and
`Fern.cpp`, which **no test target compiles**, so it is in-game-only work.

### Method notes worth keeping

- **No test target compiles most of `dll/src`.** Every pure decision rule fixed in a `.cpp` this
  programme was *moved into a header* `dll_helpers_test` includes, and given a negative control.
  That is now the standard move, not an improvisation.
- **Refutations are results.** `bd9b6d1b` refutes "ninja records no header deps" — it was a
  measurement artifact (CMake emits rules into `CMakeFiles/rules.ninja`, so grepping `build.ninja`
  returns 0 and looks like breakage). `AA39`, `Z11`, `AD16` and halves of `MB2`/`SE1` were also
  refuted and are recorded as do-not-re-raise.
- **A PASS can carry a wrong procedure.** `141e8119` corrects verification records whose evidence
  never covered the step they closed — `V6`'s auto-refresh half could not have been performed on the
  build it was recorded against. Recorded as a half-pass in place, with the reason inline.
- **`9a8ddd24`**: two ViewModel tests set an `[ObservableProperty]` whose generated hook is
  fire-and-forget, then awaited a *second* call that raced the first. Measured in-process at 4,000
  iterations — 1.25% / 2.275% / 0.625% flake. Whole-process runs cannot sample this, which is why
  160 clean cold runs had proved nothing.

### Build

Native AOT publish clean at **build 3263**. ⚠ **3262 was deliberately skipped**: `dist/` already
carried a 3262 and so does the maintainer's second machine, and a plain `build.ps1` had since
overwritten local `dist/` with a **non-trimmed** 106.8 MB exe under that same number. Reusing it
would have made one build number mean three different binaries. Note that **every** `build.ps1`
invocation bumps `build_number.txt`, not just `-Mode Publish` (use `-NoBumpBuildNumber` to suppress).

-----

## Older entries

Builds **2779–3261** (2026-08-10 … 2026-08-17, 75 entries) were moved to
[`archive/dev-log-2026-08-pre-build-3263.md`](archive/dev-log-2026-08-pre-build-3263.md)
on 2026-09-26 (nothing edited, only moved).
Builds **2220–2747** were moved to
[`archive/dev-log-2026-08-pre-build-2779.md`](archive/dev-log-2026-08-pre-build-2779.md)
on 2026-08-25 (nothing edited, only moved). Everything before that is in the four older
archives that file links.
