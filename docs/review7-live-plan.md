# Review 7 — live-verification plan and per-row status (planned 2026-09-25)

⭐ **Open this before running any live check of a `docs/todo.md` `[REVIEW7-2026-09-24]` row.** It holds the
session order (which rows share one host launch), the pre-fix binaries each red arm needs, and each row's
status. The per-row planner recipes (host, steps, green/red strings, risks) are kept verbatim in
[`review7-live-recipes.json`](review7-live-recipes.json); this file is the operator's script.

⛔ **Rules that travel with this file** (as for `fixpass-low-live-plan.md`):
1. **After every verified row, update BOTH records in the same commit:** the row in `docs/todo.md` and its status
   here. One row per commit, then push.
2. **A red arm needs a pre-fix binary identified by its SHA**, never by its build number: every staged DLL reports
   the build it was staged from.
3. One injected game at a time; kill the game, CE and the UI the moment a session is done. `ListAgents` + `git log`
   before each session: a peer session may share `dist\`.

⚠ **What this is.** The read-only output of a 6-agent planning workflow (`wf_482b7e72-14c`) at HEAD `1c514220`.
No step was live-run by the planners, and the synthesis stage already corrected them in several places (listed
under *Corrections*). Treat each step as a starting point to re-check against the source. Where a step turns out
wrong on a live host, fix the step here in the same commit as the row's record.

## Status

| row | sessions | live? · status |
|---|---|---|
| `[R7-B-01]` | 1-4 | yes · ✅ PASS red→green on 5.1 (Shipping + Development); 5.3 blocked by `[R7-X4]` |
| `[R7-A-01]` | 5 | partial · ✅ no-regression PASS on 5.8 (compact branch unreachable) |
| `[R7-B-02]` | 6-7, 9-10 | yes · ✅ PASS red→green, both halves (4.27 delegate; 5.4 TOptional) |
| `[R7-S2]` | 6-7, 11 | yes · ✅ arm A PASS red→green (4.27); arm B PASS (5.4, green only) |
| `[R7-X2]` | 8 | yes · ✅ PASS red→green (4.27 editor; pipe + AOT UI) |
| `[R7-S1]` | 9-10 | yes · ✅ PASS red→green (path / GUID shown) |
| `[R7-B-04]` | 9-10 | partial · ✅ PASS red→green (TOptional + sparse element; two sub-cases unit-only) |
| `[R7-C-05]` | 11, 13-14 | yes · ✅ PASS red→green (pokes wait the fence out; red served 296 early) |
| `[R7-S4]` | 11-12 | yes · ✅ PASS red→green (5.4; red walked 0x100001 for 31 s) |
| `[R7-S9]` | 15-16 | partial · ✅ PASS red→green (first ordering; the arg-order race is unit-only) |
| `[R7-D-02]` | 17, 21 | yes · ✅ PASS red→green (Dump All 3/3; Explorer race not reproduced, 0/3) |
| `[R7-D-08]` | 17, 21 | yes · ✅ PASS red→green on the reconnect arm (the relaunch arm does not discriminate) |
| `[R7-D-06]` | 17, 21 | yes · ✅ PASS red→green (cap + low disk; real cuts still marked) |
| `[R7-S8]` | 17, 24 | yes · ⏳ open |
| `[R7-D-01]` | 17, 21 | yes · ⏳ open |
| `[R7-D-04]` | 18, 22 | yes · ⏳ open |
| `[R7-S12]` | 18, 25 | yes · ⏳ open |
| `[R7-S7]` | 18, 24 | yes · ⏳ open |
| `[R7-C-02]` | 18, 22 | yes · ⏳ open |
| `[R7-C-04]` | 18-19, 22-23 | yes · ⏳ open |
| `[R7-C-01]` | 19, 23 | yes · ⏳ open |
| `[R7-C-03]` | 19, 23 | yes · ⏳ open |
| `[R7-S3]` | 19, 23 | yes · ⏳ open |
| `[R7-S13]` | 20, 25 | yes · ⏳ open |
| `[R7-S14]` | 20, 25 | yes · ⏳ open |
| `[R7-S11]` | 20, 24 | partial · ⏳ open |
| `[R7-S6]` | 20 | partial · ⏳ open |
| `[R7-D-07]` | 20-21 | yes · ⏳ open |
| `[R7-S10]` | 20, 24 | yes · ⏳ open |
| `[R7-D-03]` | — | no · ✅ recorded as no live check (todo.md) |
| `[R7-S5]` | — | no · ✅ recorded as no live check (todo.md) |
| `[R7-A-03]` | — | no · ✅ recorded as no live check (todo.md) |
| `[R7-D-05]` | — | no · ✅ recorded as no live check (todo.md) |
| `[R7-X1]` | — | no · ✅ recorded as no live check (todo.md) |
| `[R7-X3]` | done | ✅ live PASS 2026-09-25 (before this plan; `068c590e`) |
| `[R7-X4]` | 4 (found there) | yes · ✅ PASS red→green on stock 5.3; the stripped-5.4 function arm not run (Elliot) |

-----

## Corrections

1. **R7-X3 was missing from the planners' set.** It is already a live PASS: 2026-09-25, UE 4.27 editor `-game`, DLL sha 6a1b299a (built before release), evidence in `out/review7/x3_427/`. There is an optional re-confirm on the 3550 DLL as session 8b. It is listed under No live check.

2. **R7-B-01 host.** The claim that 5.3 is not reachable is wrong.
   - A complete staged stock UE 5.3 Third Person DebugGame build exists: `D:\Unreal Projects\ThirdPerson53\Saved\StagedBuilds\Windows\ThirdPerson53\Binaries\Win64\ThirdPerson53-Win64-DebugGame.exe`. Its paks are present and dated 07-29. It has never been injected: there is no Logs folder for it. Its Config does not override `gc.PendingKillEnabled`, so it runs with 5.3's default True.
   - The corpus copy under `D:\UE_Analyze_data\Varies Version builds\5.3` has an empty Content folder and cannot be launched.
   - Session 4 adds a check at the gate's upper edge, UE version 503. Detection is part of what this measures: if the build does not report 503, that is a new finding, not a B-01 failure.
   - 5.0 and 5.2 stay unreachable.
   - B-01 red: use `reverse_commit 1d0cfaba`. It reverses cleanly at HEAD and touches only Grimoire.h plus a comment in Ubel.h. It gives the same tree as the planner's hand-written sub with less room for error. The planner's warning against building `1d0cfaba^` as a whole tree still stands.
   - The planner's premise is confirmed in UE_5.1 source. `MarkAsGarbage` goes to `MarkPendingKillOnlyInternal` when pending kill is enabled. The only `SetUpdatedComponent(NULL)` is in `TickComponent`, so the capsule's two bindings do survive the destroy.

3. **One red DLL for B-02, B-04, S1 and S2** (`r7-mixed-red`). This replaces three stagings and saves two launches. In it:
   - S1's red is `reverse_commit 9ea4bb2f`, which reverses cleanly. Do not hand-copy the hunk.
   - B-02 (a) and (b), B-04 (a) and (b), and S2's era500 gate are subs.
   - Every field each row reads depends on exactly one reverted piece. The attribution is listed under Pre-fix binaries.
   - B-02 sub (b) should become `fv.typedValue = optWeakRead ? "(stale)" : DescribeUnreadableField("optional", fi.Offset);`. That keeps `optWeakRead` referenced, so no unused-variable warning can fail the build.

4. **Switch syntax.** `launch_dumpertest.py` needs `--extra=-DumperTestWeakGarbage`, with the `=`. Written with a space, argparse reads `-DumperTestWeakGarbage` as an option.

5. **R7-S9 staging spec lacked `"name"`.** `stage_dll.py` requires it and raises a KeyError without it. Added.

6. **R7-C-05 staging made deterministic.** The planner hoped `ExtraScanGObjects`' `.data` heuristic would find GObjects on DumperTest. Instead:
   - sub (a2) in FindAll stashes init's real result in a file-static, then zeroes `out.GObjects`;
   - sub (a1) makes `ExtraScanGObjects` return the stash first.
   The row tests the apply fence, not the heuristic. The same subs go into all three fence DLLs.

7. **Repackage DumperTest 5.4 once, first**, with both the R7-OPT fields and a NestedBag for S11.
   - With a late repackage, S11's red would need one more prefix-UI swap.
   - What it costs: a new PE hash, so a fresh snapshot DB (D-06 already derives `<PE>` at Connect). The L63 counts (about 573, 53,799, 61,870 and 797 entries) move by the few new entries. Treat them as approximate; A4's margin of about 2k over the cap does not change.
   - D-06's premise must be re-derived on the new package: the first game-only capture must still be about 717 objects, with nothing in chunks 1–2.
   - `S11_SetNestedBag(Count)` must have SET semantics (empty, then fill), so all S11 arms fit in one launch.

8. **R7-S11 corrections.**
   - Drop the engine-struct pre-flight (`s11_nested_scan.py`); NestedBag gives deterministic arms: disclosure at 300 pairs and limit 256, truncation at 20000 pairs and limit 16384.
   - Fix the expected names. `ExportStatus` names a nested container by its flattened field name, so `PairsA` / `PairsB` (the S11 test asserts `Tunes`, not `Tune.Tunes`), never `NestedBag.PairsA`.
   - **The F-16000 arm is not a no-lever case.** A 16000-pair map is not "bound" at limit 16384, so the notice says "no toolbar setting shrinks this export". But lowering the limit to 8192 does shrink it, to about 49k entries, under the cap. The planner's expected green is itself a defect in R7-S6's branch.
   - Run F-16000 as a finding probe. If the copy at 8192 comes out untruncated, file a new row; do not record a PASS.
   - R7-S6's real no-lever case (truncation by scalars) stays unit-only.

9. **Prefix UIs: 3 JIT builds instead of 7.**
   - `c88ca562` (build 3549's UI): reds for D-02, D-01, D-06, D-08, D-04, D-07, C-02 and C-04. `git log c88ca562..HEAD` shows only `d8b855fb` and `511ceb35` touch the CSX and Fly/Foreground/SeeThrough/CeLuaHygiene files, so this build replaces `09896ca5`.
   - `6b11b861`: reds for S7, S8, S10 and S11.
     - For S7: `fc92cc06..6b11b861` touches no AOBMaker code.
     - For S8: it is inside the never-shipped `07a4b8ac..1627fdf7` range.
     - For S10: it has D-07 but not S10.
     - For S11: it has S6 but not S11.
   - `e2f23e4b`: reds for S12, S13 and S14. It has S7–S11 but not S12, S13 or S14; its Instance Finder code is identical to `4e6e374c` (`bc1839fd^`).
   - Run `dist_swap.py restore` between two installs: `install` copies over dist\ and leaves extras behind.
   - The UI embeds `scripts/ue5_*_helper.lua` (EmbeddedResource). Each prefix UI therefore streams its own commit's Lua. Keep each CE process on one commit's Lua.

10. **R7-D-07 / R7-S10: no Spawn_Holders.**
    - Greens reuse S6's inflated actor: Arr_Churn 16,388 shows a 64-element preview at limit 64 and is capped by the DLL at 4,096 at limit 16384.
    - Reds use `l63_instexport.py grow`. It only grows Arr_Churn and Map_Churn and creates no game objects, so it cannot disturb the snapshot premise (holders can).

11. **R7-C-01 / C-03 / S3 Lua loading.**
    - `invokeUFunction` is guarded by `if not invokeUFunction`, and the invoke helper is version '1.3' at every commit. So a CE process that ever loaded HEAD's helpers silently keeps them, and a red run in it would come out green. Each red must run in a fresh CE process.
    - S3's red can run in the same fresh CE as C-01/C-03's reds. The c88ca562 invoke helper is functionally identical to 511ceb35's (the diff is one example inside a header comment). After the C-01/C-03 reds, load 511ceb35's freeze helper (1.6) over 1.5; the version gate allows newer over older.
    - This keeps S3's freeze half attributable. With c88ca562's freeze helper, the refusal would be C-03's defect, not S3's.
    - `R7.load` must log `UE5_INVOKE_HELPER_VERSION` and `UE5_FREEZE_HELPER_VERSION`.
    - The freeze helper's timeout text is lowercase: `mailbox timeout after …`.

12. **R7-C-04 record placement.** The records must sit in the same CE address list as `dist\UE5CEDumper.CT`'s inject record. Open the CT first, then push. Arm A and the CT-injected S3/C-01/C-03 then share one un-injected relaunch.

13. **R7-A-01** is a no-regression check only. A pre-fix DLL shows the same green on a stock engine.

14. **Staging anchors checked at HEAD.** Each B-02/B-04/era500 anchor matches exactly once.
    - `git apply -R --check` at HEAD: `621e9ef1`, `126a726a`, `bc46fea8`, `1d0cfaba`, `9ea4bb2f` and `479b39fb` reverse cleanly. `5b00729d`, `b12d0811`, `0d2efda7` and `aee085f9` do not.
    - Mimic.cpp is CRLF in the work tree; `stage_dll.py` handles that. Run every spec with `--dry-run` first.

15. **Checks that confirmed the planners** (no change):
    - Every pipe command name exists in Renge.h.
    - Parameter names: `find_instances` takes `limit` / `exact_match` and returns CDOs; `walk_instance` takes `addr` / `class_addr` / `array_limit` / `lean`; `invoke_function` takes `instance_addr` / `parms_size` / `params_hex` / `str_params{off,wide,text}`.
    - Inherited `K2_DestroyActor` resolves (`ResolveFunctionInChain`).
    - Log strings match: X2 at Aura.cpp:4257, S4 at :4136, S2 at :4146, the UI gap text at LiveWalkerViewModel:2895/2931/2935, and D-07/S10 in ContainerTruncation.
    - The disk guard counts GiB, and a percent of 0 disables the percent term.
    - `fetchedAll` is read after `WhenAll`, so D-06's green is deterministic.

## Pre-fix binaries

Build everything in Phase 0. No game, UI or CE may be running. Do not run `build.ps1` at any point: it would overwrite the AOT `dist\` with the non-trimmed build. `stage_dll.py` and `build_dll.py` never bump the build number or touch `dist\`.

**A. Staged DLLs**
Command: `py tools/verify/stage_dll.py tools/verify/staging/<name>.json --dry-run`, then the same without `--dry-run`. Output: `out\staged\<name>\UE5Dumper.dll` plus STAGED.txt with the sha. Every staged DLL reports build 3550, so identify it by that sha, compared with `inject.py --pid <p> --modules`.

1. **r7-b01-red**: `reverse_commit` 1d0cfaba, paths `dll/src`. Used by B-01 (sessions 2, and optionally 4).
2. **r7-mixed-red**: `reverse_commit` 9ea4bb2f (S1), plus these subs:
   - B-02(a), Ubel.h: `    (void)objIdx;\n    if (!named && serial == 0) return "(unbound)";` becomes `    if (!named && objIdx == 0 && serial == 0) return "(unbound)";`
   - B-02(b), Ubel.cpp: the two-line `fv.typedValue = optWeakRead ? UnresolvedWeakLabel(optWeakIdx, optWeakSerial)` / `: DescribeUnreadableField("optional", fi.Offset);` becomes `fv.typedValue = optWeakRead ? "(stale)" : DescribeUnreadableField("optional", fi.Offset);`. Copy the exact indentation from the file.
   - B-04(a): `elem.value = DescribeDelegateBinding(b.targetObj, b.targetName,` becomes `elem.value = DescribeScriptDelegate(b.targetObj != 0, b.targetName,`
   - B-04(b): `: fv.ptrName + " (" + fv.ptrClassName + ")") + optWeakTag;   // [R7-B-04]` becomes `: fv.ptrName + " (" + fv.ptrClassName + ")");`
   - S2, Aura.cpp: `const bool sparseEra = ::g_cachedUEVersion == 0 || ::g_cachedUEVersion >= 423;` becomes `const bool sparseEra = ::g_cachedUEVersion >= 500;   // STAGING`

   Which reverted piece each reading depends on:

   | Reading | Host | Reverted piece |
   |---|---|---|
   | CDO Del_Unicast | UE 4.27 | B-02(a) |
   | Find References sparse hits | UE 4.27 | era500 |
   | Opt_Weak_Null, Opt_Weak_Stale | 5.4 | B-02(b) |
   | Opt_Weak_Garbage | 5.4 | B-04(b) |
   | OnActorHit sparse element | 5.4 | B-04(a) |
   | Opt_Soft, Opt_SoftClass, Opt_Lazy (the path/GUID is missing; the text reads "(stale)" because of B-02(b)) | 5.4 | S1 |

   era500 has no effect at version 504. The S1 reversal has no effect on 4.27, which has no TOptional.
   Used by sessions 7, 7b and 10.
3. **r7-s4-red**: `reverse_commit` 621e9ef1. Session 12.
4. **r7-x2-host** (the X2 GREEN host): `reverse_commit` 126a726a. It reverses X3, so the 4.27 editor's storage is refused. Session 8.
5. **r7-fence-green**: four subs.
   - (a1) Genau.cpp: insert `static uintptr_t s_r7StagedGObjects = 0;   // STAGING` before `uintptr_t ExtraScanGObjects() {`, and make the function's first statement `if (s_r7StagedGObjects) { Sein::Info("SCAN:GObj","STAGING: Extra Scan returns init's GObjects"); return s_r7StagedGObjects; }`
   - (a2) Genau.cpp FindAll: after the two-line `out.GObjects = FindGObjects(hints.gobjectsPatternId.empty() ? nullptr\n                                : hints.gobjectsPatternId.c_str());` append `s_r7StagedGObjects = out.GObjects; out.GObjects = 0;   // STAGING init miss`
   - (b) Fern.cpp: after `Aura::Init(g_cachedGObjects);` add a 2000 ms Sleep, gated on `%TEMP%\ue5dump_apply_widen.flag` via GetTempPathA + GetFileAttributesA, that logs `STAGING apply-widen`.
   - (c) Mimic.cpp: after `LOG_INFO("Mailbox: auto-initializing (UE5_Init)...");\n    UE5_Init();` add a 1500 ms Sleep gated on `%TEMP%\ue5dump_ensure_widen.flag`.
   Sessions 13 and 15.
6. **r7-c05-red**: subs (a1), (a2), (b), (c), plus the 4-line `struct ApplyFence { … } applyFence;` replaced by `// STAGING: R7-C-05 fence removed`. Session 14.
7. **r7-s9-red**: `{"name":"r7-s9-red","reverse_commit":"bc46fea8","paths":["dll/src"],"subs":[(a1),(a2),(b),(c)]}`. Anchor (c) still exists after the reversal. Session 16.
8. **Existing**: `out\review7\dll\UE5Dumper.dll`, build 3549, sha 8919074b. This is X2's recorded red, for the optional re-run. `assert_build` expects 3549 for it.

**B. Prefix UIs (JIT)**
For each commit C in `c88ca562 6b11b861 e2f23e4b`, run
`git archive C | tar -x -C out/r7_prefix/C/src` and then
`dotnet build out/r7_prefix/C/src/ui/UE5DumpUI/UE5DumpUI.csproj -c Release -o out/r7_prefix/C/bin`.
Only ever install them through `dist_swap.py`. Never hand one over: they are JIT builds.

**C. Lua prefixes (no build)**
- `git show c88ca562:scripts/ue5_invoke_helper.lua` and `…:scripts/ue5_freeze_helper.lua` into `out/review7/latch/c88ca562/`.
- `git show 511ceb35:scripts/ue5_freeze_helper.lua` into `out/review7/latch/511ceb35/`.

**D. Fixture repackage (DumperTest 5.4)**
Edit `tools/ue-sample/DumperTest/Source/DumperTest/DumperTestActor.{h,cpp}`. Append only, after the last UPROPERTY of ADumperTestActor.

- TOptional fields, set in BeginPlay unless noted:
  - `Opt_Weak_Null`: `TOptional<TWeakObjectPtr<AActor>>`, `Emplace(nullptr)`.
  - `Opt_Weak_Live`: `Emplace(this)`.
  - `Opt_Weak_Garbage`: re-pointed together with WeakToGarbage inside the `-DumperTestWeakGarbage` block.
  - `Opt_Weak_Stale`: set once, to the first garbage target.
  - `Opt_Soft`: `TOptional<TSoftObjectPtr<UStaticMesh>>` from `FSoftObjectPath("/Game/R7S1/NotCooked.NotCooked")`. Nothing ever calls Get() on it.
  - `Opt_SoftClass`: `TOptional<TSoftClassPtr<AActor>>` from `"/Script/Engine.Pawn"`.
  - `Opt_Lazy`: `TOptional<TLazyObjectPtr<AActor>>` set to `FUniqueObjectGuid(FGuid(0x11111111,0x22222222,0x33333333,0x44444444))`.
  - `Opt_Soft_Unset`: left unset.
- `USTRUCT FDumperTestNestedBag { UPROPERTY() TMap<int32,int32> PairsA, PairsB; }`, plus `UPROPERTY() FDumperTestNestedBag NestedBag;` and `UFUNCTION() void S11_SetNestedBag(int32 Count)`. The function empties both maps and then fills each to Count; it is unclamped.
- Commit first. Then run `py tools/ue-sample/repackage.py --engine 5.4 --project DumperTest --compile-only --sync-mirror` (the UHT gate). Any field UHT refuses is dropped, and its row is recorded as having no host.
- Then run `… --configs Shipping,Development,DebugGame --sync-mirror`.
- Update `tools/ue-sample/README.md` and `package-identity.json` (`capture_package_identity.py`), and commit.

**E. New rigs (commit before first use, because of Bitdefender)**
- `tools/verify/r7_destroy_then_walk.py`: `--process --class --out [--walk-field]`. It finds a live non-CDO target, walks it and its components before the destroy, reads +0x08, invokes K2_DestroyActor, then walks 3 times 2 s apart plus the +0x08 reads, and tees everything to `--out`.
- `tools/verify/r7_sparse_refs.py`: promoted from `out\review7\live_check.py`. Arguments: `--build`, `--refs-class` / `--refs-name`, `--walk-class`, `--out`, `--poke-header INT32`, `--poke-keys`. Pokes go through `mailbox_poke.Mem`; each one records the original, restores it in a `finally`, and reads it back.
- `tools/verify/r7_apply_fence.py --mode c05|s9 --process --dll`.
- `tools/verify/ce/r7_mailbox_latch.lua`: `R7.load(dir)`, `R7.loadFreeze(dir)`, `R7.state`, `R7.freeze(cls,keep)`, `R7.invoke`, `R7.check`, `R7.stop`. It logs the helper versions and `getTickCount` to `out/review7/latch/lua.log`.
- `tools/verify/snapshot_db_pad.py` with verbs `rows`, `pad` and `unpad`. `rows` also prints the min/max object index of the newest snapshot if the schema stores it.
- `tools/verify/disk_squeeze.py <SnapshotsDir>` with verbs `plan`, `arm --need-gb N`, `drop` and `release`; it refuses a filler over 3 GiB.
- The staging JSONs above.

## Sessions

**Rules for every session:**
- Before each session, run `ListAgents` and `git log` (peer sessions share `dist\`), and `list_granted_applications`. `dist\UE5DumpUI.exe` and Cheat Engine need grants; request them in batches of 7 or fewer.
- Only one injected game at a time. Kill the game, the UI and CE when the session ends.
- Evidence goes to `out\r7live\<row>\`, which is gitignored. The todo.md R7 row gets the DLL sha, the object count and the green/red strings verbatim; commit after each closed row.
- Run `front_window.py front <app>` before every click.
- With the UI connected, `pipe_client` uses the third pipe slot. Run one pipe rig at a time.
- Resume a suspended game even when a step fails.

**0. Preparation (no game).** About 2 h.
1. Check `dist\`: build_number.txt reads 3550, `UE5DumpUI.exe` is 57,771,520 B, `UE5Dumper.dll` is 3,005,440 B dated 09-25 01:11. Record its sha256 (a32cb03d…).
2. Write and commit rigs E. Build A, B and C. Do the repackage in D.
3. Back up `%LOCALAPPDATA%\UE5CEDumper\ui-options.json` and `experimental.json`. Set `experimental.json` to enabled=true, snapshotQuotaMb=0.
4. Run `py tools/verify/dist_swap.py backup`. It refuses if `out\dist.swapbak` already exists: restore that one first.

**1. DumperTest51 Shipping, dist DLL. Rows: B-01 (green).** About 25 min.
1. `py tools/verify/fixture_smoke.py shipping51 --out out\r7live\b01\ship --keep`. Require about 19k objects, ue_version 501 and `sparse_delegates` not 0x0.
2. `py tools/verify/r7_destroy_then_walk.py --process DumperTest51-Win64-Shipping --class BP_ThirdPersonCharacter_C --out out\r7live\b01\ship`. It takes P, then M = CharacterMovement and C = CapsuleComponent from P's walk, never by name.
   - **GREEN:** Before the destroy, C.OnComponentBeginOverlap names `CharMoveComp::CapsuleTouched` and C.PhysicsVolumeChangedDelegate names `CharMoveComp::PhysicsVolumeChanged`, with no `[garbage]`. After it, M+0x08 has 0x20000000 set and 0x40000000 clear, and both binding elements (and the preview) read `… [garbage]` on all 3 walks. A weak field that still resolves to a live object stays untagged.
   - **RED (session 2):** The same flags, but both bindings are untagged on all 3 walks.
   - If a GC purge lands inside the window (the walk fails or the names change), relaunch.
3. Kill the game.

**2. DumperTest51 Shipping, r7-b01-red. Rows: B-01 (red).** About 20 min.
1. `fixture_smoke.py shipping51 --dll out\staged\r7-b01-red\UE5Dumper.dll --out out\r7live\b01\red --keep`. Check the sha.
2. Repeat session 1, step 2. Expect the RED. Kill.

**3. DumperTest51 Development, dist DLL. Rows: B-01 (32-byte FUObjectItem).** About 20 min.
1. Session 1 with the `dev51` flavour and `--process DumperTest51`. Expect the same GREEN. Kill.

**4. ThirdPerson53 DebugGame, staged stock 5.3 build, dist DLL. Rows: B-01 at the gate's top (503).** About 30 min.
1. Launch with `py -c "import subprocess;subprocess.Popen([r'D:\Unreal Projects\ThirdPerson53\Saved\StagedBuilds\Windows\ThirdPerson53\Binaries\Win64\ThirdPerson53-Win64-DebugGame.exe','-windowed'],creationflags=0x208)"`. Wait 60 s, then `inject.py --name ThirdPerson53-Win64-DebugGame`.
2. Run `pipe_client.py get_pointers`. Require more than 10k objects, ue_version **503** and sparse storage located. If the version is not 503, or init fails, record it as a new finding (the gate is keyed on the detected version) and stop.
3. `r7_destroy_then_walk.py --process ThirdPerson53-Win64-DebugGame --class BP_ThirdPersonCharacter_C --out out\r7live\b01\ue53`. Expect session 1's GREEN.
4. Optional RED: relaunch with r7-b01-red and expect session 1's RED. Kill.

**5. DumperTest58 Shipping, dist DLL. Rows: A-01 (no-regression only).** About 20 min.
1. `fixture_smoke.py shipping58 --out out\r7live\a01 --keep`.
2. `py tools/verify/r7_sparse_refs.py --build 3550 --walk-class CapsuleComponent --refs-class CharacterMovementComponent --out out\r7live\a01`.
   - **GREEN:** `sparse_delegates` is not 0x0. At least 1 MulticastSparseDelegateProperty hit (owner CollisionCylinder; field PhysicsVolumeChangedDelegate or OnComponentBeginOverlap). `sparse_skipped` false, `sparse_unlocated` 0. No capsule field reads "storage layout not decoded".
   - `grep -i compact` over `Logs\DumperTest58-Win64-Shipping\*.log` finds nothing.
   - No RED exists. A `sparse_unlocated` above 0 is its own finding.
3. Kill.

**6. UE427_3rdPerson Shipping, dist DLL. Rows: B-02 (delegate half), S2 arm A (green).** About 25 min.
1. `py tools/verify/sw9_ue4_delegate_read.py --engine 427 --config Shipping --keep`. Its own verdict belongs to another row.
2. B-02: `find_instances {"class_name":"DelegatePadFixture","limit":16}`. `walk_instance` both `Default__DelegatePadFixture` and the summoned `DelegatePadFixture_N`, with `array_limit` 8. Then `read_mem.py UE427_3rdPerson-Win64-Shipping <CDO+Del_Unicast offset> 16`. Optionally do the same for `Default__TextBlock`'s DelegateProperties.
   - **GREEN:** The CDO's Del_Unicast reads `(unbound)` and its raw bytes are `FFFFFFFF 00000000`. The summoned instance reads `DelegatePadFixture_N::DPad_OnPingProbe`.
   - **RED (session 7):** The CDO field reads `(stale)` (as do the TextBlock fields, if checked); the bound control is unchanged.
3. S2 arm A: `r7_sparse_refs.py --build 3550 --refs-class CharacterMovementComponent --walk-class CapsuleComponent --out out\r7live\s2\427_green`.
   - **GREEN:** At least 1 hit (2 expected) of type MulticastSparseDelegateProperty, owner CollisionCylinder, fields PhysicsVolumeChangedDelegate and OnComponentBeginOverlap. `sparse_skipped` false, `sparse_unlocated` 0. No "key does not look like a raw pointer" in walk-0.log.
   - **RED (session 7):** Zero sparse hits, `sparse_skipped` false, and the other hits identical.
4. Kill by PID.

**7. UE427_3rdPerson Shipping, r7-mixed-red. Rows: B-02 and S2 arm A (red).** About 20 min.
1. `sw9_ue4_delegate_read.py --engine 427 --config Shipping --keep --dll out\staged\r7-mixed-red\UE5Dumper.dll`. Check the sha.
2. Repeat session 6, steps 2–3. Expect the REDs. Kill.
- **7b (optional).** UE423_Flying Shipping, first with the dist DLL, then with the mixed red.
  1. `sw9 --engine 423 --config Shipping --keep`.
  2. `invoke_function {instance_addr:<non-CDO CheatManager>, func_name:"Summon", parms_size:16, str_params:[{off:0,wide:true,text:"DefaultPawn"}]}`.
  3. `r7_sparse_refs.py --refs-class FloatingPawnMovement --walk-class SphereComponent`, and the B-02 CDO walk.
  4. GREEN is a sparse hit on CollisionComponent.PhysicsVolumeChangedDelegate. An owner that cannot be found is a locator finding: 4.23's storage has only ever validated empty.

**8. UE 4.27 editor `-game`, r7-x2-host, plus the AOT UI. Rows: X2 (green; also covers A-01's UI gap).** About 45 min.
1. Launch: `py -c "import subprocess;subprocess.Popen([r'C:\Program Files\Epic Games\UE_4.27\Engine\Binaries\Win64\UE4Editor.exe',r'D:\Unreal Projects\UE427_3rdPerson\UE427_3rdPerson.uproject','-game','-windowed'])"`. Wait about 60 s.
2. `inject.py --list`, then `inject.py --pid <UE4Editor> --dll out\staged\r7-x2-host\UE5Dumper.dll`, then check with `--modules`.
3. Precondition in `Logs\UE4Editor\`: `UE5_Init: Complete` with about 24k objects, the scan log says the validator "rejected — 1 live element(s) but none has a UObject-shaped key", and `sparse_delegates` is 0x0.
4. `r7_sparse_refs.py --build 3550 --refs-class CharacterMovementComponent --walk-class CharacterMovementComponent --out out\r7live\x2`.
   - **GREEN:** Every Find References reply has `sparse_skipped` true and `sparse_unlocated` 0. walk-0.log has the WARN "the sparse-delegate storage was not located (UE=427) -- not read, so bindings held there are MISSING".
   - **RED:** Already recorded in `out\review7\live_427` (DLL 8919074b): `sparse_delegates` 0x0 but `sparse_skipped` false. An optional re-run uses `out\review7\dll` and `assert_build` 3549.
5. Start `dist\UE5DumpUI.exe`, front it, Connect. In Live Walker, paste a CharMoveComp address, click Go, then Find Refs.
   - **GREEN:** The header reads `References to CharMoveComp (N)  [sparse-delegate bindings not read — their storage was not located or decoded on this build]`, with the same suffix in the status bar. A target with no references reads "No references found in what was read — … That is not evidence that nothing points here."
6. Close the UI. Kill the editor.
- **8b (optional).** The same launch with the dist DLL: X3 re-confirm. **GREEN:** `sparse_delegates` is not 0x0, the scan log says "has a module vtable, accepted", and Find References on CharMoveComp gives 2 MulticastSparse hits.

**9. DumperTest Shipping (repackaged), `--extra=-DumperTestWeakGarbage`, dist DLL, no UI. Rows: B-02 (TOptional half), S1, B-04 (green).** About 25 min.
1. `launch_dumpertest.py shipping --extra=-DumperTestWeakGarbage`, then `inject.py --pid <out\host.pid>`, then `get_pointers`: 504, `sparse_delegates` not 0.
2. Wait at least 75 s from launch (the first purge comes at about 61 s).
3. `find_instances DumperTestActor`, then `walk_instance` 3 times, 2 s apart. Then `read_mem.py DumperTest-Win64-Shipping <actor+Opt_Soft offset> 8`.
   - **GREEN:**
     - Opt_Weak_Null reads `null`; Opt_Weak_Stale reads `null (stale)`; Opt_Weak_Live reads `DumperTestActor_… (DumperTestActor)`.
     - Opt_Weak_Garbage reads `Actor_N (Actor) [garbage]`, with the same N as WeakToGarbage in the same walk. Only those two fields are tagged.
     - Opt_Soft reads `/Game/R7S1/NotCooked.NotCooked`; Opt_SoftClass reads `/Script/Engine.Pawn`; Opt_Lazy reads `{11111111-22222222-33333333-44444444}`; Opt_Soft_Unset reads `(unset)`. The raw weak cache is 8 zero bytes.
     - A GUID printed in a different word order is to be noted, not failed.
   - **RED (session 10):** Opt_Weak_Null and Opt_Weak_Stale read `(stale)`. Opt_Weak_Garbage is untagged while WeakToGarbage is tagged. Opt_Soft, Opt_SoftClass and Opt_Lazy show no path or GUID (they read `(stale)`).
4. `r7_destroy_then_walk.py --process DumperTest-Win64-Shipping --class DumperTestActor --out out\r7live\b04\green`.
   - Before the destroy: OnActorHit reads `(1 sparse binding) [DumperTestActor::D4_OnActorHitProbe]`, untagged.
   - **GREEN:** Afterwards +0x08 has 0x40000000 set, and the OnActorHit element and preview read `DumperTestActor::D4_OnActorHitProbe [garbage]`. Del_Unicast, the Multicast_Inline preview, Arr_Delegates[1] and Arr_MulticastDelegates[1] are tagged too.
   - **RED (session 10):** The four controls are tagged but OnActorHit is not.
   - B-04's soft-preview fallback and its `(set)` label have no engine path; they stay unit-only.
5. Kill.

**10. The same host and switch, r7-mixed-red. Rows: B-02, S1, B-04 (red).** About 20 min.
1. Session 9 with `inject.py --dll out\staged\r7-mixed-red\UE5Dumper.dll`. Expect the REDs. Kill.

**11. DumperTest Shipping, dist DLL, no UI. Rows: C-05 step 0, S2 arm B, S4 (green).** About 40 min.
1. Launch, inject.
2. C-05 smoke: `pipe_client.py apply_rescan`, then `mailbox_poke.py DumperTest-Win64-Shipping --repeat 5`, then `apply_rescan` again.
   - **GREEN:** Both replies are `applied:false` in under 1 s, and QUERY_PTR is still served. This is the first execution of BeginApply/EndApply.
3. S4: `r7_sparse_refs.py --build 3550 --refs-name DumperTestSparseListener` as a baseline. Then `--poke-header -1`, baseline, `--poke-header 0x100001`, baseline.
   - **GREEN:** Under either poke, `sparse_skipped` is true, the listener hit is absent, `duration_ms` is close to the baseline, and walk-0.log has the WARN "sparse-delegate storage header at 0x… is unreadable or implausible -- not read". Every baseline has 1 hit with `sparse_skipped` false, and each readback matches the original.
   - **RED (session 12):** With -1, `sparse_skipped` is false, there is no WARN and no hit.
4. S2 arm B: `--refs-name DumperTestSparseListener --walk-class DumperTestActor` as a baseline, then `--poke-keys` (keys become 0x0000000500000003 while the fixture is idle), then the baseline again.
   - **GREEN:** Under the poke, `sparse_skipped` is true, the listener hit is gone, the WARN "…key does not look like a raw pointer (UE=504, possibly FObjectKey-keyed)" is present, and OnActorBeginOverlap reads `(sparse, bound — storage layout not decoded on this build)`. That last string is also A-01's relabel.
   - No RED exists (`b12d0811` does not reverse at HEAD).
5. Discard the process.

**12. DumperTest Shipping, r7-s4-red, no UI. Rows: S4 (red).** About 20 min.
1. Baseline, `--poke-header -1`, baseline. Expect session 11's RED.
2. Optional, and last: `0x100001`. Expect `sparse_skipped` false and a `duration_ms` far above the baseline, maybe `deadline_hit`. Kill immediately afterwards.

**13–16. C-05 and S9.** DumperTest Shipping, one fresh launch per arm, no UI. Never run `ensure_scanned`. About 20 min each.
- Precondition for every arm: init-0.log has `UE5_Init: Complete (UE504, GObjects=0x0`, `gnames` is not 0, and a baseline QUERY_PTR returns -10.
- Launch 13: r7-fence-green. Run `r7_apply_fence.py --mode c05` (rescan, poll to phase 3, `found_gobjects`, touch `apply_widen.flag`, then `apply_rescan` in parallel with a QUERY_PTR poke loop).
  - **GREEN:**
    - The first poke inside the window takes at least 1 s and finishes at or after t_reply − 50 ms.
    - No poke is served between `STAGING apply-widen` and t_reply.
    - pipe-0.log has `Mailbox: auto-initializing`, and init-0.log has `UE5_Init: Already initialized` after the apply.
    - The reply is `applied:true` with more than 20k objects.
    - There is exactly one `ValidateAndFixOffsets: Starting`.
- Launch 14: r7-c05-red. **RED:** Pokes inside the window are served in 5–20 ms, and finish at least 1 s before t_reply.
- Launch 15: r7-fence-green, `--mode s9` (both flags; poke at t0, apply at t0 + 200 ms).
  - **GREEN:** Served at or after t_reply − 50 ms, a second `Already initialized` after the apply, and no hang.
- Launch 16: r7-s9-red. **RED:** Served at about t0 + 1.5 s, roughly 0.7 s before t_reply.
- For every launch: a 10 s timeout or a stuck reply means a deadlock. Run `hang_dump.py` before killing. Delete the flag files afterwards.

**17. DumperTest Shipping, dist DLL, AOT UI (G1a). Rows: D-02, D-08, D-06, S8, D-01 (green).** About 90 min.
Setup: launch, inject, start the UI, front it, Connect, and confirm the object count from the status bar. Read `<PE>` from view-0.log.
1. D-02: toolbar Export → Dump All → `d02_C1.jsonl`. Wait for `DumpAll exported to … (C classes, E errors)` and read the tooltip. Restart the UI and export `C2`, then `C3` in the same session. Disconnect, then Dump Explorer → Last export, 3 times.
   - **GREEN:** `Dumped C classes (X MB) to d02_C#.jsonl` with C equal to the logged count. Dump Explorer ends on `Loaded. Connect a game and click "Re-check live"…`.
   - **RED (session 21):** `Done — C classes`, expected 3 out of 3. Dump Explorer's `Parsing dump… N rows` is a race, so record it as k out of 3.
2. D-08: reconnect. Property Search: TickCount → Force field → 7.
   - Disconnect; `pipe_client.py get_forced_fields` shows it held. Connect and take a screenshot of the strip.
   - Close the UI; get_forced_fields again. Relaunch, Connect, take a screenshot.
   - Clear all; get_forced_fields returns [].
   - **GREEN:** The strip shows `DumperTestActor :: TickCount (N held)` after both the reconnect and the relaunch.
   - **RED (session 21):** The strip stays hidden while the DLL still reports the hold. Release it with `reset_all_fields`.
3. D-06 setup: close the UI and run `snapshot_db_pad.py rows <PE>`. If the DB is missing, relaunch, take one plain capture labelled G0, and close the UI. Then `pad <PE> --mb 600`, check `rows` shows at least 600 MB, relaunch and Connect. Keep Auto snapshot off.
4. G1: Max size 512 MB, Game objects only ON, Capture.
   - **GREEN:** `Captured ≈717 objects, ≈16,9xx fields` with no cap clause; the row has partial_reason '' and is usable. The object count also re-derives the premise: about 717 means chunks 1–2 carry no game objects. If it is far off, stop.
   - **RED (session 21, R1):** `… stopped at ~600 MB cap (partial: first N of N objects)`, stored as 'cap'.
5. C1 control: 512 MB, Game objects only OFF, noise ON. Expect `… stopped at … cap (partial: first 16,384 of …)`, stored 'cap'.
6. Set Max size to Off. G2 uses sequence Q:
   - `disk_squeeze.py plan` gives N. Set Min free to 0 % and N GB.
   - `arm --need-gb N`, then `suspend.py suspend DumperTest-Win64-Shipping`, then click Capture.
   - `drop`, then `suspend.py resume`. Wait for `Capture done`.
   - Click Capture again (it must be refused), then `release`.
   - **GREEN (S8):** `… ⚠ low disk space at …; the capture is complete, but free space before the next one`, with no "kept partial". The row is '' and usable. The second click reads `Low disk space on C:\: … Capture skipped.`
   - **RED:** Session 24's S8R reads just `Captured …`. Session 21's R2 reads `… kept partial (first N of N objects)`, stored 'disklow'.
7. C2 control: sequence Q with Game objects only OFF and noise ON. Expect `… kept partial (first 16,384 of …)`, stored 'disklow'.
8. D-01: Snapshot grid, then SPC Query → Refresh, in both single and group mode.
   - **GREEN:** C1 and C2 carry `(partial: stopped at the size cap)` / `(partial: stopped on low disk)` in both SPC grids, the same as the Snapshot grid. G1 and G2 have no marker.
   - **RED (session 21):** The SPC grids show bare labels while the Snapshot grid marks them.
9. Restore Min free to 10 % / 50 GB. Kill.

**18. DumperTest Shipping, dist DLL, AOT UI, then CE (G1b). Rows: D-04, S12, S7, C-02, C-04 arm B (green).** About 60 min.
Start with no CE running: `tasklist | findstr /I cheatengine` must be empty.
1. D-04: System tab.
   - Absent: reads `○ AOBMaker not connected — open Cheat Engine…`.
   - Start `aobmaker_pipe_busy.py --seconds 90`, switch tabs and come back: reads `○ AOBMaker pipe busy — …(starting another Cheat Engine will not help)`.
   - After the rig exits: back to not connected.
   - Optional `ac3_denied_pipe.py`: reads `○ AOBMaker pipe refused this app (access denied)`.
   - **RED (session 22):** `○ Not reachable — check CE plugin installation` at every step.
2. S12:
   - Live Walker, DumperTestActor_0, Functions: with the holder off and then on, press ⟳ each time; also check its own probe after more than 5 s away from the tab. Teleport's Standalone Trainer: ⟳ with the holder off and on.
   - **GREEN:** Both notes switch to the `AOBMaker pipe busy — …` text.
   - **RED (session 25):** Live Walker's note stays "not connected" after both ⟳ and its own probe; Teleport's note stays "not connected" after ⟳.
3. S7:
   - a1, staying on System: ⟳ with the holder off, then on. **GREEN:** the line changes to busy.
   - b, Interesting Funcs: Load it once, wait at least 5 s, then ⟳ with the holder off and on. **GREEN:** `AOBMaker pipe busy — … — AA Script export will fall back to clipboard`.
   - a2: launch CE with AOBMaker once and press ⟳ (reads `● Connected`, SYM buttons enabled). `taskkill` CE, start the holder, and press ⟳ while on System. **GREEN:** busy, and the SYM buttons are disabled.
   - **RED (session 24):** a1's line stays "not connected"; a2's line is stale and the SYM buttons stay enabled; b reads "AOBMaker plugin not found".
4. Start CE with AOBMaker. File → Open `dist\UE5CEDumper.CT` (answer Yes to running the table script), then attach to the game.
5. C-02:
   - Options → String Len 16.
   - Open DumperTestActor_0 in Live Walker. Export CSX (Pre-CE 7.7 Byte) to `out\r7live\c02\green.CSX`.
   - Read `Bytesize` offline. Import the file into CE Structure Dissect at the actor address and expand Str_Even22_TwoNull, InvokeGate_LogPath and PayloadString. As a control, paste Live Walker's Copy CE XML into CE.
   - **GREEN:** Bytesize is 32, CE shows 16 characters (`統一言語日本語テスト日本語テスト`), and the CSX and CE XML agree.
   - **RED (session 22):** Bytesize is 16 and CE shows 8 characters while the CE XML shows 16.
6. C-04:
   - Teleport tab: Keep Foreground → Add to CE; See-through → Add to CE; Add action records to CE (this adds both Fly records). Close the UI.
   - In CE's Lua Engine set `UE5_DEBUG = 1` and wrap `print` to append to `out\r7live\c04\lua.log`.
   - Arm B: tick Fly (WASD), Keep Foreground and See-through from the address list. For each record: suspend the game, untick it, wait 10 s without clicking, note any dialog, resume.
   - **GREEN:** No dialog, every record ends unticked, and lua.log has `[Fly] timed out and the DLL never saw this command…` (and the same for KeepForeground and SeeThrough).
   - **RED (session 22):** A modal over the frozen game.
7. Kill the game. Keep CE open with its list.

**19. DumperTest Shipping, un-injected relaunch, the same CE (CE-L1). Rows: C-04 arm A, C-01, C-03, S3 (green).** About 60 min.
1. `launch_dumpertest.py shipping` (no inject). Re-attach CE and answer Keep list: Yes.
2. C-04 arm A: tick each of the 4 records.
   - **GREEN:** Exactly one dialog per record (`[Fly] g_invokeMailbox not found -- is UE5Dumper.dll injected?`, and the same for the others). After OK the record unticks, and lua.log has `… nothing to turn off`.
   - **RED (session 23):** A second, identical dialog.
   - Then set `UE5_DEBUG = nil`.
3. S3 setup: `R=getAddressList().getMemoryRecordByDescription('Inject DLL + Start Pipe Server'); R.Active=true`. The CT log shows `DLL_PATH …\dist\UE5Dumper.dll`, and `pipe_client.py get_pointers` shows 3550 with objects. Then `dofile` the rig and run `R7.load([[D:\Github\UE5CEDumper\scripts]])`.
4. C-01:
   - `R7.state('pre')`, then suspend the game.
   - `R7.freeze('WorldSettings')` times out after about 5 s. `R7.state` shows busy=true, stale=<mailbox>, cmd=6.
   - `R7.invoke()` returns at once with `the previous invoke timed out and the DLL is STILL holding the mailbox`, and the mailbox is unchanged.
   - Resume. pipe-0.log shows `received cmd=6` and `LIST_INSTANCES class='WorldSettings'`. `R7.invoke()` then gives ok and `INVOKE_BY_NAME complete, result=0`.
   - That sequence is the GREEN.
   - **RED (session 23):** The invoke blocks about 10 s, the mailbox ends with cmd=4 / Spawn_CountHolders, and there is no LIST_INSTANCES line.
5. C-03:
   - Suspend, then `R7.invoke()` (10 s timeout). Resume and wait for `INVOKE_BY_NAME complete`. Call no helper.
   - `R7.freeze('WorldSettings', true)`, wait 10 s, `R7.check()`, `R7.stop()`.
   - **GREEN:** Start returns ok, the latch is released, LIST_INSTANCES runs about every 2 s, isAbandoned is false and there is no dialog.
   - **RED (session 23):** `mailbox busy (concurrent invoke or rescan)`, then after 3 failures `…freeze STOPPED writing` with a modal (copy it with Ctrl+C, then click OK).
6. S3:
   - Suspend, `R7.invoke()` (timeout), resume, wait for completion (status 1, cmd 0). Call no helper.
   - `R.Active=false`. The logs show `UE5_Shutdown: Cleaning up`; `R7.state` shows cmd=0, status=0 and busy still true.
   - `R.Active=true`. `get_pointers` answers again.
   - `R7.invoke()` twice, then `R7.freeze('WorldSettings')`.
   - **GREEN:** Both invokes return ok, the freeze starts ok, and pipe-0.log has the commands.
   - **RED (session 23):** Every invoke refuses with the "STILL holding" text, and the freeze returns `mailbox busy`.
   - If the untick logs "This record did not start the pipe server", the check is vacuous: redo it on a fresh game.
7. Kill the game and CE (taskkill).

**20. DumperTest Shipping, dist DLL, AOT UI (G1c). Rows: S13, S14, S11, S6, D-07, S10 (green).** About 2 h. Array Limit 64 and Live Walker auto-refresh off.
1. S13:
   - Instances: `DumperTest`, Exact OFF. Select A (the live DumperTestActor) and note a holder B.
   - Put the clipboard sentinel in place. Suspend; Copy CE XML; click B; resume. Then `l63_instexport.py clip --save out\r7live\s13\green.xml`.
   - **GREEN:** `CE XML copied … for instance DumperTestActor_…`, and the root is A's address with about 573 entries.
   - **RED (session 25):** `… DumperTestHolder_…`, with B's root carrying A's layout.
2. S14:
   - Arm A: select A, suspend, click B, Copy CE XML. **GREEN:** at once, `The fields shown are not this selection's — copy again once they have loaded…`, no copied line in the log, and the sentinel is still on the clipboard. After resume, a copy exports the holder.
   - Arm B: select A, suspend, move the limit 64 → 128, copy. **GREEN:** the same refusal; after resume the export is normal.
   - **RED (session 25, arm A only):** The export goes out under the holder's root with the actor's entries.
3. S11 (NestedBag). Invoke with `pipe_client.py invoke_function --args '{"instance_addr":"<A>","func_name":"S11_SetNestedBag","parms_size":4,"params_hex":"<LE>"}'`, where 300 = `2c010000`, 16000 = `803e0000`, 20000 = `204e0000` and 0 = `00000000`.
   - (1) Count 300 at limit 256, copy. **GREEN:** `Copied; only part of these containers was exported: PairsA (256 of 300), PairsB (256 of 300)` with no TRUNCATED in the log. **RED (session 24):** blank.
   - (2) Count 20000 at limit 16384, copy. **GREEN:** `⚠ … TRUNCATED …; lower the Array Limit to shrink it (PairsA, PairsB then export only their first elements)`. **RED (session 24):** `… no toolbar setting shrinks this export …`.
   - (3) Finding probe: Count 16000 at 16384, copy (expect the "no toolbar setting" text). Then set the limit to 8192 and copy. If the second copy is not truncated, file the S6 no-lever defect.
   - Finally, Count 0.
4. S6 (as L63), reading each status from a zoomed screenshot:
   - A1, limit 64: `Copied; only part … Set_Big (64 of 199)…`.
   - A2, limit 256: blank.
   - A3: limit 16384, `grow`, re-select, copy: about 53.8k entries, `Map_Churn (16,384 of 16,390), Arr_Churn (4,096 of 16,388)`.
   - A4: `spawn`, and `counts` must show SpawnedHolders at 4,096 of 4,096. Copy: `⚠ … TRUNCATED …; lower the Array Limit to shrink it (Map_Churn, Arr_Churn, Children …)`, with no Collapse or DropDown; `clip` gives about 61.9k. This also closes D-03.
   - A5, limit 64: about 797 entries, 3 names plus `+2 more`.
   - REDs are already recorded by L63. A1's red is optional in session 21.
5. D-07, at limit 64:
   - Live Walker DumperTestActor_0, Refresh. Suspend, set the limit to 256 (its refresh fails after about 10 s), click Arr_Churn's drill, wait 11 s.
   - **GREEN:** `Showing the first 64 of 16,388 entries — re-open this container to re-read it. — ⚠ Could not re-read 'Arr_Churn'…` with no "per fetch"; view-0.log has `Drill: re-read … timed out`.
   - **RED (session 21):** `… elements — this view is capped at 64 per fetch.`
   - Resume, Back, drill again as a control.
6. S10:
   - With the game running, set the limit to 16384 and Refresh; Arr_Churn shows 4,096 of 16,388. Suspend, drill, wait 11 s.
   - **GREEN:** `Showing the first 4,096 of 16,388 entries — re-open…`, with neither "Array Limit" nor "per fetch".
   - Resume, Back, drill: `… capped at 4,096 per fetch.`
   - **RED (session 24):** `… raise the "Array Limit" slider…`.
7. Restore the Array Limit. Kill.

**21. c88ca562 UI (R1a).** `dist_swap.py install out\r7_prefix\c88ca562\bin` with nothing running. Check that `dist\UE5Dumper.dll` still has the 3550 sha. Rows: D-02, D-08, D-06 (R1, R2), D-01, D-07, optional S6 A1 (red). About 90 min.
1. Launch, inject, Connect. The build-mismatch badge is expected.
2. Session 17, steps 1–2, with the REDs.
3. R1: session 17, step 4 with label R1. R2: step 6's sequence Q with Game objects only ON, label R2. D-01's RED: SPC Refresh shows bare labels. Restore Min free to 10 / 50.
4. D-07: `l63_instexport.py grow --count 200` (Arr_Churn becomes 204), then session 20, step 5. RED.
5. Optional S6 A1: blank status. Kill.

**22. c88ca562 UI, then a fresh CE process (R1b). Rows: D-04, C-02, C-04 arm B (red).** About 45 min.
1. With no CE running: session 18, step 1. RED.
2. Start a **new** CE process that has never loaded HEAD Lua. Open the CT, attach.
3. Session 18, steps 5–6 with the pre-fix records. REDs.
4. Kill the game. Keep CE.

**23. Un-injected relaunch, the same CE (CE-L2). Rows: C-04 arm A, C-01, C-03, S3 (red).** About 50 min.
1. Session 19, step 1, then step 2 (RED: a second dialog). Then the S3 setup, using `R7.load([[…\out\review7\latch\c88ca562]])`; the log must show invoke helper 1.3 and freeze helper 1.5.
2. Session 19, steps 4–5 (REDs).
3. `R7.loadFreeze([[…\latch\511ceb35]])`; the log must show freeze 1.6.
4. Session 19, step 6 (RED, including a third try a minute later).
5. Kill the game and CE.
6. `dist_swap.py restore`.

**24. 6b11b861 UI (R2).** Install it (UI and game closed). Rows: S8, S7, S11, S10 (red). About 60 min.
1. Launch, inject, Connect.
2. S8R: sequence Q, Game objects only ON. RED: just `Captured …`, the row is '' and usable, and the second click is still refused. Restore Min free.
3. S7: session 18, step 3. REDs.
4. S11: session 20, step 3 arms (1) and (2). REDs. Then Count 0.
5. S10: `grow --count 5000` (Arr_Churn becomes 5,004), limit 16384, Refresh (4,096 of 5,004), suspend, drill. RED.
6. Kill. `dist_swap.py restore`.

**25. e2f23e4b UI (R3).** Install it. Rows: S12, S13, S14 (red). About 30 min.
1. Session 18, step 2 (no CE). Session 20, steps 1–2 (arm A). REDs.
2. Kill. `dist_swap.py restore`: it must print mismatches: 0 and the AOT sha.

**26. Close-out.** About 15 min. UI closed:
1. `snapshot_db_pad.py unpad <PE>`.
2. `disk_squeeze.py release`, and confirm no filler is left on C:.
3. Restore `ui-options.json` and `experimental.json`.
4. Delete the `%TEMP%\ue5dump_*_widen.flag` files.
5. Final todo.md commit. The S11 finding-probe result, if positive, becomes a new row.

Rough total: about 2 h of preparation plus 17–18 h of sessions.

## No live check

- **R7-S5**: test-only (`dll_core_test.cpp` WEAKLABEL restores `SOFTPTR_PATH`). No shipped byte changed. Evidence: core 448/0.
- **R7-A-03**: comment-only (Grimoire.h headers). The layout fact behind it was measured live on DumperTest51 at build 3547.
- **R7-D-05**: test-only (the ClassListCapTests persist-symmetry checker).
- **R7-X1**: test-only flake fix (ClassPivotViewModelTests); the flake cannot be reproduced on demand.
- **R7-D-03**: superseded in the shipped build by R7-S6. Its surviving claim (never Collapse or DropDown) is closed by session 20's S6 A4; its red is already recorded by L63 (2026-09-23).
- **R7-X3**: already a live PASS on 2026-09-25 (UE 4.27 editor, DLL 6a1b299a, `out/review7/x3_427/`). Session 8b is an optional re-confirm on 3550.

Parts of live-checked rows that stay unit-only, to record in each row:
- **A-01**: the compact-set branch needs a `UE_USE_COMPACT_SET_AS_DEFAULT` source-engine build.
- **B-01**: 5.0 and 5.2 have no engine or package here.
- **B-02**: UE 5.0's `{-1,0}` has no engine here.
- **B-04**: the soft-preview fallback and the resolved-but-unnamed `(set)` have no engine path that produces them.
- **S2**: the refusal has no red (`b12d0811` does not reverse, and the pre-fix code had no probe).
- **S3**: the freeze helper's "mailbox moved" branch.
- **S6**: a truncation with no lever at all (scalars only) has no host; the F-16000 arm is a finding probe, not this.
- **S9**: window (2), argument evaluation order and the seq_cst fence.
- **S11**: the secondary snapshot-before-await claim.
- **S14**: a walk started during the export keeps its loading flag (a millisecond window).
- **C-02**: the narrow Utf8Str/AnsiStr control (no fixture has such a UPROPERTY).
- **C-04**: the contract-check bail and the result-error bails.
- **D-02**: the error-count and "wrote no classes" wordings (DumperTest dumps cleanly); the Dump Explorer half gets a rate only.
- **S1, B-02 (TOptional half), B-04 (TOptional part)**: if UHT refuses a TOptional field at `--compile-only`, that field has no host and the row stays unit-only.
