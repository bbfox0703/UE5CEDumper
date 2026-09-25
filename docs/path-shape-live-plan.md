# PATH-SHAPE + PROXY-DEPLOY-UX — live-check plan (2026-09-25)

⭐ **Open this before running any live check of `docs/todo.md` `[PATH-SHAPE-2026-09-25]` or
`[PROXY-DEPLOY-UX-2026-09-25]`.** Every row there, and every fix from the four skeptic reviews under it, has been
fixed red → green in unit tests (or red by mutation for a test gap). What remains is the live check each row names.
This file holds the session order, what each session proves, and each row's status.

⛔ **Rules that travel with this file:**
1. **Build first.** Every session runs against ONE published AOT build (`build.ps1 -Mode Publish`; check `dist\`'s size
   and SHA, and `pipe_client.assert_build`). A stale proxy serves a confident wrong answer (working-lessons §2.6).
2. **One game at a time.** Kill the game, CE and the UI as soon as the session is done (handover §4). Never end a turn
   while a game is alive.
3. **Path shapes come from the repo** (maintainer, 2026-09-25): `py tools/verify/path_shape_folders.py make` builds
   them under `out/pathshape/`. The packaged fixture, which is outside the repo because it is too big to copy, is
   MOVED into a shape (`host` / `unhost`, same volume only), never used in place under a hand-made folder.
4. **After each verified row, update BOTH records in the same commit:** the row in `docs/todo.md` and its status
   below. One row per commit, then push.
5. **Back up, then restore:** `%LOCALAPPDATA%\UE5CEDumper\ui-options.json` and `dll-path.txt` before S1, restored
   after S5. Nothing is left deployed in the fixture.

**Fixtures:**
- **PathShape** (`tools/verify/path_shape_fixture.py`): the DumperTest51 Shipping package, with its exe hard-linked
  under five names (`DumperTest51遊戲-…`, `DumperTest51™-…`, `功夫-…`, `Tony's&Jerry-…`, `DumperTest51-Win64-Shipping .exe`).
- **Folder shapes** (`tools/verify/path_shape_folders.py`): ten folders, including the maintainer's letterlike-symbol
  folder in both spellings. `probe` shows, on this machine, which ones CE can inject from.

-----

## Session order

### S1 — exe-name shapes over the pipe (no UI, no CE)

**Setup:** `path_shape_live.py deploy <fixture>` copies `dist\proxy\version.dll` into the fixture.
**Run:** `path_shape_live.py run <fixture>`. It launches each of the five shapes in turn, checks it, and kills it
before the next.

| # | row | what the rig checks | status |
|---|---|---|---|
| 1 | `[PATH-MODULE-NAME-UTF8]` | `module_name` is the real UTF-8 name, for all five | ✅ PASS 2026-09-25, build 3555 |
| 2 | `[PATH-CE-MODULE-VIEW]` (DLL side) | `ce_base` names the module as CE does: `DumperTest51?-…` for ™, `夫-…` for `功夫-…` (the 0x5C cut) | ✅ PASS 2026-09-25, build 3555 |
| 3 | `[PATH-SEIN-TRAILING-SPACE]` (DLL side) | `… .exe` logs into `Logs\DumperTest51-Win64-Shipping\init-0.log` (this launch's) | ✅ PASS 2026-09-25, build 3555 |

### S2 — the ™-folder question (CE)

**Setup:** `path_shape_folders.py host letterlike <fixture>`, with `version.dll` still deployed. Launch the plain exe.
Attach CE.

| # | row | check | status |
|---|---|---|---|
| 4 | `[PATH-ES2-CE-SYMBOLS]` | In CE's Lua Engine, `print(getAddress('UE5_Init'))` returns an address for our proxy under the letterlike folder (CE loads symbols from the ANSI `EVERSPACE?`-style path). Record the answer either way: it settles the question. | ✅ answered 2026-09-25, build 3556: NOT a finding -- `UE5_Init` resolves although CE's path for the proxy reads `? ? ? ? Ω ? ? ? K A …` |

**Teardown:** `unhost letterlike`.

### S3 — UI + CE on the exe-name shapes (proxy still deployed)

| # | row | check | status |
|---|---|---|---|
| 5 | `[PATH-CE-MODULE-VIEW]` (UI side) | Connected to `功夫-…`, a CE address the UI copies (Pointer panel / Live Walker) reads `"夫-Win64-Shipping.exe"+RVA` and resolves when pasted into CE | ✅ PASS 2026-09-25, build 3557 (the real name gives nil) |
| 6 | `[PATH-AOBMAKER-ANSI-MATCH]` | Connected to `DumperTest51遊戲-…`, "Register GWorld symbol" through AOBMaker registers; `getAddress` of it resolves in CE | ✅ PASS 2026-09-25 (matched by name, no fallback) |
| 7 | `[PATH-TRAINER-APOSTROPHE]` | Connected to `Tony's&Jerry-…`: the standalone trainer, pushed to CE, enables Setup | ✅ PASS 2026-09-25, build 3557 |
| 8 | `[PATH-CEXML-AMP]` | The same game: an AOB-wrapped CE XML export pastes into CE and its script enables | ✅ PASS 2026-09-25, build 3557 (module-rooted AA via AOBMaker; clipboard XML by xUnit) |
| 9 | `[PATH-SEIN-TRAILING-SPACE]` (UI side) | Connected to `… .exe`: the UI's own log mirror lands in the DLL's folder | ✅ PASS 2026-09-25, build 3557 |
| 10 | `[PATH-MODULE-NAME-UTF8]` / confirmed record | After the confirm dwell, `ui-options.json` holds the real `DumperTest51遊戲-…` name, not `?` | ✅ PASS 2026-09-25, build 3556 |

### S4 — CE inject from folder shapes (proxy REMOVED from the fixture)

**Setup:** `path_shape_live.py undeploy <fixture>`. `path_shape_folders.py stage-dist big5`, `big5-5c`, `letterlike`.

| # | row | check | status |
|---|---|---|---|
| 11 | `[PATH-CE-INJECT-ANSI]` | The UI run from `工具` and from `功夫`: "Inject CE bootstrap", pushed to CE, injects; `load_mode` is `loaded:…ue5dumper.dll`, not a manual map | ✅ PASS 2026-09-25, build 3557 (`load_mode` = `injected`: the DLL's own name) |
| 12 | `[PATH-CE-INJECT-ANSI]` (refusal) | The UI run from the letterlike folder (D:, no 8.3 names): the record refuses before `injectDLL`, says why, and unticks | ✅ PASS 2026-09-25, build 3557 |
| 13 | `[PATH-CT-INJECT-ANSI]` | `UE5CEDumper.CT` opened from `工具`: it finds the DLL and injects; from the letterlike folder it refuses and clears the path | ✅ PASS 2026-09-25, build 3557 |

### S5 — Proxy Deploy panel (the fixture found by "Scan drives")

| # | row | check | status |
|---|---|---|---|
| 14 | `[PROXY-DOUBLE-GUARD]` | With `version.dll` deployed, Deploy `winmm.dll`: skipped, with the reason on the row | ✅ PASS 2026-09-26, build 3558 (the other way round: winmm.dll there, version.dll deployed) |
| 15 | `[PROXY-USE-CONFIRMED]` | A clean folder with a confirmed record deploys the recorded type, and says so || ✅ PASS 2026-09-26, build 3558 |
| 16 | `[PROXY-RISKNOTE-WIPED]` | A deploy's import-risk note is still on the row after the refresh (Deploy and Update All) | ✅ PASS 2026-09-25, build 3558 |
| 17 | `[PROXY-PRODUCTNAME-UNREADABLE]` | `hold_exclusive.py` on a proxy-named file: Deploy skips (said once), Update All says "Not updated: <name>.", Undeploy leaves it and fails the row || ✅ PASS 2026-09-26, build 3558 |
| 18 | `[PROXY-CONFIRM-SHARED-EXE]` | Two folders shipping the same exe name: neither uses the record, and the row says so. Needs a second detected copy; if drive scan cannot find one on this machine, record that it is unit-only || ✅ PASS 2026-09-26, build 3558 (the second copy made by `tools/verify/shared_exe_twin.py`) |

**Teardown:** undeploy everything from the fixture. Restore `ui-options.json` and `dll-path.txt`. Run
`path_shape_folders.py clean`.
