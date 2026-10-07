# Live Funcs step 2 — the build ledger `[LIVEFUNCS-TIMELINE-2026-10-04]`

The items that build step 2, in order, one red commit and one green commit each, every test mutation-checked. The design is the plan's "Step 2 design" ([live-funcs-timeline-plan.md](live-funcs-timeline-plan.md)). Written 2026-10-07 from two design workflows (three code maps, three designs, a merge and a critic; then name matching, a revision and a second critic), with the second critic's fixes applied. **Update an item's status in the same commit that closes it.**

Fern.cpp and Stark.cpp are compiled by no test target: their items are proven by building the DLL target (`py tools/verify/build_dll.py --targets UE5Dumper`) and by the rig. Anchors (`file:line`) were right at `49578bd1` and drift; re-read before relying on one.

| # | Layer | Item | Depends on | Status |
|---|---|---|---|---|
| N0 | DLL | Linie: NameKey, ArmHint and the pure arm rules (copy bytes, after copy, truncation, ring cap) with the shared snapshot constants | — |✅ c7dcb18f red |
| N1 | DLL | Linie: ArmState (sorted keys, preallocated arm log) installed by StartRecording; RecordCall arms at first sight and returns the hint | N0 | ✅ 11db9c55 red |
| N2 | DLL | Linie: arm upkeep on a key change (disarm, reload = new arm), the class FName checked for armed addresses only, a failed key read | N1 | ✅ 388b7ee5 red |
| N3 | DLL | Linie: arm-log capacity and per-key counts (arms_full, distinct addresses, never called) via ArmsSummary | N2 | ✅ 18669e41 red |
| N4 | DLL | Linie: TakePendingArms / PublishArmLayout / SealArms / CopyArms; StartTrace stamps the gen; the arm state is freed with the trace | N3 | ✅ 659ea1e7 red |
| T1 | DLL | Linie: the trace's scope by name (TraceEnter takes the hint; TraceConfig.scoped; TraceInfo.scoped / tickedNames / snapOnly) | N1 | ✅ d3c11335 red |
| S1 | DLL | Linie: snapshot rings per choice in one allocation (same K, cap per ring), TraceInfo.snap, SnapTooSmall (with the 64n guard), SnapNoMemory, freed with the trace; SnapRings() introduced here | T1 | ⬜ |
| S2 | DLL | Linie: the entry slot (header {seqKind, entrySeq, len, flags, arm}), kTraceSnapTaken, CopySnaps; ring bound; copy clamped; step-1 equivalence | S1 | ⬜ |
| S3 | DLL | Linie: the after-return slot in TraceReturn(params) when the hint has kArmAfter, under the gen check | S2 | ⬜ |
| S4 | DLL | Linie: lone calls outside every scope, snapshots-only (scoped with no ticks), kTraceSnapExcluded inside an open scope | S2 | ⬜ |
| S5 | DLL | Linie: the per-ring and total budgets (one CAS word each; the first calls of each second kept) | S4 | ⬜ |
| S6 | DLL | Linie: null params, copy faults, the truncated flag, and TR2 with snapshots | S3 | ⬜ |
| S7 | DLL | Linie: SnapRings / CopySnaps windows, ring isolation, out-of-turn and orphan filtering; benchmark lines | S5, S6 | ⬜ |
| B1 | DLL | Ubel: the pure ParamKindOf | — | ⬜ |
| B2 | DLL | Ubel: ReadNameKey (the function's and its Outer's FName ints), ReadObjectNameKey, NameKeyMatches through Serie::GetString | N0 | ⬜ |
| B3 | DLL | Ubel: CaptureParamLayout over the function's own chain (FField and UProperty), CPF_Parm filter, refusals; object/class/struct read at the mode's subclass start | B1 | ⬜ |
| B4 | DLL | Ubel: layout enrichment by value (struct sub-layouts, enum tables, object class, soft-path / lazy / delegate / optional facts) | B3 | ⬜ |
| B5 | DLL | Ubel: the checked arm capture and RunArmCapturePass (injected live/key/capture checks; reuse by the 5-field key; states; latency) | N4, B2, B4 | ⬜ |
| B6 | DLL | Ubel: DecodeParamSnapshot, value types | B4 | ⬜ |
| B7 | DLL | Ubel: DecodeParamSnapshot, pointer family and containers (soft-path facts in ctx, shared delegate/weak helpers, FText header) | B6 | ⬜ |
| P0 | pipe | Rig skeleton tools/verify/livefuncs_snap_live.py with --fixture-check (paths as arguments; committed before it runs) | — | ⬜ |
| P1 | pipe | Fixture: DumperTest58 probes (SnapProbe_Call, SnapProbe_RetOnly, SnapNest_Outer host, SnapProbe_PerFrame through ProcessEvent, SnapLate_Begin / SnapLate_Call); README rows; scripted Shipping repackage | P0 | ⬜ |
| P2 | pipe | Rig tools/verify/livefuncs_snap_live.py (paths as arguments; committed before it runs) | P1 | ⬜ |
| F1 | pipe | Fern: fname_key and per_frame on pe_profile_get rows | B2, P2 | ⬜ |
| F2 | pipe | Fern: pe_profile_start ticks by name (verify keys, ArmState, scoped, trace.names reply); free the previous trace first; D1's unloaded-tick refusal kept for legacy address ticks only; Start time logged | T1, N4, F1 | ⬜ |
| F3 | pipe | Fern: trace.snapshots at Start (ring caps from parms_size, bytes 8-128 MB, budgets clamped, copier, snap keys in the ArmState); snap in TraceInfoToJson | S5, F2 | ⬜ |
| K1 | pipe | Stark (DLL, no unit target): RecordCall(&hint), TraceEnter(..., params, hint), TraceReturn(..., params) | F3, S3 | ⬜ |
| F4 | pipe | Fern: the layout-capture worker (a thread per traced Start with snapshot keys, polling every 50 ms, first enum detection kicked at Start); Stop = StopTrace, StopRecording, final pass with a 2 s deadline, seal; names summary before ReleaseIfEmpty | B5, K1 | ⬜ |
| F5 | pipe | Fern/Renge: pe_snap_layouts (paged by arms, ~1 MB, layouts de-duplicated per page) and pe_snap_get (copy under the lock, decode outside); pipe count 102 -> 104; pipe-protocol.md flags table and 'does not hold' | F4, B7, S7 | ⬜ |
| F6 | pipe | Fern: code_addr on pe_trace_names funcs (Live, native) | F2 | ⬜ |
| U1 | UI | Models and DumpService: NameKey (fname_key), per_frame, TraceStartOptions.TickedNames / Snapshots, trace.names / scoped / snap parsed, pe_snap_layouts / pe_snap_get, code_addr | F1, F2, F3, F5, F6 | ⬜ |
| U2 | UI | LaneRoutingPipeClient: pe_snap_layouts and pe_snap_get on the bulk lane | U1 | ⬜ |
| U3 | UI | Helpers/FunctionTickSet extracted from _ticked, holding per Class::Func the live addresses, a set of NameKeys and the largest ParmsSize | U1 | ⬜ |
| U4 | UI | Live Funcs ticks by name: unloaded rows with a key tick; the Start sends ticked_names; T7 split; keyless rows keep D1's rule; status by name count; the old-DLL guard | U3 | ⬜ |
| U5 | UI | Live Funcs: snapshot choice (needs a key, Trace on, parameters), Start options (snapshots-only skips T7), refusals and status notes, disconnect | U4 | ⬜ |
| U6 | UI | Live Funcs: stored WindowMs, IsPerFrame, the pure snapshot estimate (2-slot rule, K, K<8 warning, OD5 orange, grey budget note), D3 game figure | U5, N0 | ⬜ |
| U7 | UI | Live Funcs: 'Snapshot shown rows' bulk choice and Clear, through the estimate | U6 | ⬜ |
| U8 | UI | Live Funcs view: Snapshot column, visibility loop, per-frame marker, Snapshot row (OD4 slider 8-128 MB default 32, estimate, memory), strings, persistence | U7 | ⬜ |
| U9 | UI | Call Trace and Live Funcs say what was traced: scoped by name, snapshots-only, unscoped; the T8 copy marks names as waiting or never called | U4 | ⬜ |
| U10 | UI | Call Trace list: resizable, remembered column widths and detail-pane width; Object tooltip; CallTraceUiOptions on the settings root | — | ⬜ |
| U11 | UI | Call Trace detail: object-address wording, the Address setting, native entry as a CE address, script / not found / unknown lines | U1 | ⬜ |
| U12 | UI | Call Trace: snapshot load before the release (bulk lane), CallTraceSnapshots stored on the CallTrace, join by entrySeq, per-arm layouts, Summary counters | U1, U2 | ⬜ |
| U13 | UI | Call Trace: detail TabControl (Call \| Parameters), view C rows, reasons (Lone / Excluded / budget / overwritten / raw-only arm and why / null / fault / truncated), Changed, tree markers, 'Only calls with parameters' (works with no text) | U12, U11 | ⬜ |
| U14 | UI | Export: JSONL snapshot keys, layout lines per arm, scoped / snapshots_only in the header; a separate params CSV | U13 | ⬜ |
| X1 | docs | Plan 'Step 2 built' (decisions, TR6 / copy-size / after-copy deviations, name matching), dev-log entry, verification-register and todo rows (D1's tick refusal row rewritten; the Int8 preview quirk as its own row); comment pass | F5, U14, U8, U9, U10 | ⬜ |
| X2 | docs | AOT publish and live checks: DumperTest58 rig and UI walkthrough, livefuncs_trace_live.py re-run, Avowed reload acceptance, UE418 if packaged; measurements and the machine recorded | X1 | ⬜ |

-----

## N0 — Linie: NameKey, ArmHint and the pure arm rules (copy bytes, after copy, truncation, ring cap) with the shared snapshot constants

- **Red:** dll_core_test Linie block: ArmCopyBytes(40, 0x400, 64)==40; (100, 0x400, 64)==64 with ArmTruncated true; (0, 0, 64)==64 (unknown size: the ring's cap); (0, 0x400, 64)==0 (known parameterless). ArmTakesAfter(0) and (0x00400000) are true, (0x400) is false. RingCapFor(0)==kSnapUnknownCopy (256), (3000)==kSnapMaxCopy (2048), (41)==48. NameKey orders over its 4 ints. Red against stubs that return 0.
- **Mutation:** ArmTakesAfter returns false for flags 0 (the unknown-flags check fails); RingCapFor without round8 (41 gives 41).
- **Files:** `dll/src/Linie.h`, `dll/tests/dll_core_test.cpp`

## N1 — Linie: ArmState (sorted keys, preallocated arm log) installed by StartRecording; RecordCall arms at first sight and returns the hint

- **Red:** New block 'LIVEFUNCS-STEP2: arming by name' with the stub reader of dll_core_test.cpp:7083-7093. ArmState has a tick key {0xA,0,7,0} and a snap key {0xB,0,7,0} (ring 0, cap 64), with gen set to 5. StartRecording(stub, nullptr, arms). RecordCall(0xA, t, &h) gives h.gen==5, kArmTick, ring -1. RecordCall(0xB, t, &h) gives ring 0, arm 0, copy 16, and an arm log of size 1. A second call of 0xB gives the same hint and no second arm. Stub identities differing only in classIndex (8) or nameNumber (1) give a default hint. Without an ArmState the hint is default, and the D1 block (dll_core_test.cpp:7076-7345) stays green.
- **Mutation:** Compare nameIndex only in the key search (classIndex 8 arms); set the hint only on the first sight (the second call's hint is default).
- **Files:** `dll/src/Linie.h`, `dll/src/Linie.cpp`, `dll/tests/dll_core_test.cpp`

## N2 — Linie: arm upkeep on a key change (disarm, reload = new arm), the class FName checked for armed addresses only, a failed key read

- **Red:** The occupant pattern of dll_core_test.cpp:7165-7207, with arms and a counting ObjectNameReader stub. 0xF0 is armed. (1) Another, non-matching function takes the address: the next hint is default, and the table still marks reused. (2) The Outer changes under the same names: hint arm 1, same ring, 2 log records with distinct outers, not reused. (3) The class FName changes while the function FName and Outer stay: re-read, disarmed. (4) 10 calls of an unarmed address make 0 class reads. (5) The key reader returns false: default hint for that call, armed again on the next.
- **Mutation:** Keep s.arm across a key change (1 fails); skip the class read for armed addresses (3 fails); read the class on every call (4 fails); pass s.arm through a failed key read (5 fails).
- **Note:** ObjectNameReader's contract is FuncKeyReader's: loads only, no lock, no string (Macht::ReadSafe at outer + the UObject name offset, never Ubel::GetName). Arms are installed only with a key reader.
- **Files:** `dll/src/Linie.h`, `dll/src/Linie.cpp`, `dll/tests/dll_core_test.cpp`

## N3 — Linie: arm-log capacity and per-key counts (arms_full, distinct addresses, never called) via ArmsSummary

- **Red:** An ArmState with capacity 1 and one key that both ticks and snapshots. 0xB1 is armed (arm 0). 0xB2 (same names) gets ring -1 with kArmTick still set, arms_full==1 for that key, and the log stays at size 1. ArmsSummary: addresses==2 for that key and 0 for a key never called.
- **Mutation:** Drop the tick bit when the log is full; count arm records instead of distinct addresses (addresses==1).
- **Files:** `dll/src/Linie.h`, `dll/src/Linie.cpp`, `dll/tests/dll_core_test.cpp`

## N4 — Linie: TakePendingArms / PublishArmLayout / SealArms / CopyArms; StartTrace stamps the gen; the arm state is freed with the trace

- **Red:** TakePendingArms(64) returns arms 0 and 1 once, in order, then none. PublishArmLayout(0, Read, p) shows in CopyArms. SealArms leaves arm 1 at NotReadBeforeStop and refuses a later Publish(1). StartTrace(cfg.arms) stamps arms->gen == GetTraceInfo().gen. FreeTraceIfGen(gen) expires a weak_ptr to the ArmState. The stuck-hook pattern (dll_core_test.cpp:6945-6970) with arms: Free returns false and the weak_ptr is alive.
- **Mutation:** Publish after seal; FreeTraceLocked keeps the recording's same-gen reference (the weak_ptr does not expire).
- **Note:** Arm snapshot keys only while the ArmState's gen is the tracing gen (StopTrace marks it), so the Stop handler arms nothing after the trace stopped. The recording's ArmState is moved out under g_mu and destroyed after unlocking; Reset nulls it and the ObjectNameReader unconditionally. Assertions use FreeTraceIfGen (FreeTrace is void).
- **Files:** `dll/src/Linie.h`, `dll/src/Linie.cpp`, `dll/tests/dll_core_test.cpp`

## T1 — Linie: the trace's scope by name (TraceEnter takes the hint; TraceConfig.scoped; TraceInfo.scoped / tickedNames / snapOnly)

- **Red:** TraceConfig{scoped=true} with no ticked addresses. TraceEnter(0xF0, sp 900, hint{gen, kArmTick}) writes one ScopeRoot record. A nested 0xF2 at sp 800 with a default hint is recorded; a later call at sp 1000 is not. With every tick waiting (all hints default), nothing is recorded. A hint from gen-1 writes nothing. The legacy scope tests (dll_core_test.cpp:6780-6848) are unchanged and green. GetTraceInfo(): scoped, tickedNames==1, snapOnly false.
- **Mutation:** Take the scope mode from ticked.Empty() (every call is recorded while every tick waits); drop the gen comparison.
- **Note:** TraceConfig carries a plain tickedNames count, so T1 needs no ArmState (review 2, MED 6).
- **Files:** `dll/src/Linie.h`, `dll/src/Linie.cpp`, `dll/tests/dll_core_test.cpp`

## S1 — Linie: snapshot rings per choice in one allocation (same K, cap per ring), TraceInfo.snap, SnapTooSmall (with the 64n guard), SnapNoMemory, freed with the trace; SnapRings() introduced here

- **Red:** Two rings (caps 16 and 40) and bytes for K=8: allocated, slotsPerRing==8. K<8 gives SnapTooSmall and frees the ring too. bytes < 64*n gives SnapTooSmall with no underflow. FreeTrace, FreeTraceIfGen, ReleaseIfEmpty and Reset each leave !snap.allocated. The stuck-hook case with rings keeps snap.allocated, and Start is Busy.
- **Mutation:** Drop the 64*n guard (K wraps huge and the Start passes); free the rings before StopTraceLocked (the stuck-hook check fails).
- **Note:** SnapRings() (the per-ring windows and counters) is introduced in S1, so S6 can refuse it while stuck.
- **Files:** `dll/src/Linie.h`, `dll/src/Linie.cpp`, `dll/tests/dll_core_test.cpp`

## S2 — Linie: the entry slot (header {seqKind, entrySeq, len, flags, arm}), kTraceSnapTaken, CopySnaps; ring bound; copy clamped; step-1 equivalence

- **Red:** hint{gen, ring 0, arm 3, copy 16} with params=&buf: the entry record has Taken; CopySnaps(0) returns one slot with entrySeq == the record's seq, arm==3, len 16 and bytes == buf. hint.ring 5 with 2 rings: no slot, no Taken. hint.copy 100 into a 64-byte ring: len 64. With no snapshots, a fixed call script gives records equal to the pinned step-1 values (no flag above bit 0).
- **Mutation:** Store the ring index as entrySeq; drop the ring bound; copy hint.copy unclamped (len 100).
- **Files:** `dll/src/Linie.h`, `dll/src/Linie.cpp`, `dll/tests/dll_core_test.cpp`

## S3 — Linie: the after-return slot in TraceReturn(params) when the hint has kArmAfter, under the gen check

- **Red:** buf changes between Enter and Return: the entry slot holds the old bytes and the after slot the new, with the same entrySeq and the after bit. A hint without kArmAfter writes no after slot. A TraceReturn after Stop+Start writes nothing into the new rings.
- **Mutation:** Copy at entry only; drop the gen check.
- **Files:** `dll/src/Linie.h`, `dll/src/Linie.cpp`, `dll/tests/dll_core_test.cpp`

## S4 — Linie: lone calls outside every scope, snapshots-only (scoped with no ticks), kTraceSnapExcluded inside an open scope

- **Red:** Tick by name A1, choose A7. A7 at sp 900 outside any scope: Taken|Lone, and a nested 0xF2 at sp 800 is not recorded. A7 inside A1's scope: no Lone. Scoped with no ticks: 0xF1 not recorded, A7 recorded Lone. exclude={A7} with A7 inside A1's scope: Taken|Excluded, not Lone, and its nested callee recorded. exclude={A9}, unchosen, inside the scope: not recorded (today's rule). The Linie.h exclusion comment is updated.
- **Mutation:** Open a scope for a lone call (the nested 0xF2 is recorded); flag the exclusion override Lone (the 'not Lone' check fails).
- **Files:** `dll/src/Linie.h`, `dll/src/Linie.cpp`, `dll/tests/dll_core_test.cpp`

## S5 — Linie: the per-ring and total budgets (one CAS word each; the first calls of each second kept)

- **Red:** A test clock advancing by qpcFreq per second, with a per-ring budget of 3. Five in-scope calls in one second give 3 slots and 2 records flagged Budget; the next second admits again. A total of 4 across two rings caps the sum. A lone call over the budget writes no record and is counted.
- **Mutation:** No reset on a new second; >= instead of > at the cap (an off-by-one in the admitted count).
- **Files:** `dll/src/Linie.cpp`, `dll/tests/dll_core_test.cpp`

## S6 — Linie: null params, copy faults, the truncated flag, and TR2 with snapshots

- **Red:** params=0 gives kSnapNullParams with len 0. A copier stub returning false gives kSnapCopyFault with len 0, and Stop quiesces at once. kArmTruncated gives kSnapTruncated. A copier blocking on an event makes Stop wait. A stuck copier: CopySnaps and SnapRings are refused, Free keeps everything, Start is Busy. A throwing copier leaves the in-flight count at 0 (the pattern of dll_core_test.cpp:6972-6990).
- **Mutation:** Run the copy after the InflightGuard's scope (Stop no longer waits for the blocking copier).
- **Files:** `dll/src/Linie.cpp`, `dll/tests/dll_core_test.cpp`

## S7 — Linie: SnapRings / CopySnaps windows, ring isolation, out-of-turn and orphan filtering; benchmark lines

- **Red:** A busy ring laps K=8 with 20 calls while a rare ring's 3 slots all survive. CopySnaps returns [written-K, written) in order. A slot whose entrySeq is below the trace's first_valid is dropped and counted as an orphan. `next` pages correctly. Printed, not checked: RecordCall with 1,000 keys (first sight and steady), TraceEnter with a non-chosen hint, a 64-byte entry plus after copy. The CPU line of dll_core_test.cpp:7033-7044 is printed too.
- **Mutation:** Use one shared ring (the rare ring's slots are lapped); drop the orphan filter.
- **Files:** `dll/src/Linie.h`, `dll/src/Linie.cpp`, `dll/tests/dll_core_test.cpp`

## B1 — Ubel: the pure ParamKindOf

- **Red:** ParamKindOf(0x0010000008000182)==ConstRef, (0x180)==Out, (0x08000180)==InOut, (0x580)==Return, (0x80)==In.
- **Mutation:** Test OutParm before ReturnParm; ignore ConstParm.
- **Files:** `dll/src/Ubel.h`, `dll/src/Ubel.cpp`, `dll/tests/dll_core_test.cpp`

## B2 — Ubel: ReadNameKey (the function's and its Outer's FName ints), ReadObjectNameKey, NameKeyMatches through Serie::GetString

- **Red:** A new pool-faking block that installs its own pool and joins the legal-exceptions list (dll_core_test.cpp:1092-1099). Function 'Fire' (21) in class 'Actor_C' (22): ReadNameKey gives {21,0,22,0}. NameKeyMatches(key, 'Actor_C', 'Fire') is true; with class 'Other_C' it is false. Number 3 renders 'Fire_2', and 'Fire' is refused. An unreadable address returns false.
- **Mutation:** Compare the function name only; call GetString without the Number.
- **Files:** `dll/src/Ubel.h`, `dll/src/Ubel.cpp`, `dll/tests/dll_core_test.cpp`

## B3 — Ubel: CaptureParamLayout over the function's own chain (FField and UProperty), CPF_Parm filter, refusals; object/class/struct read at the mode's subclass start

- **Red:** A pool-faked UFunction in both property models (the UFUNCWALK pattern from dll_core_test.cpp:1631). A Blueprint local after the parameters is left out. A SuperStruct pointing at another function adds nothing. layoutEnd, the kinds, ArrayDim and a packed bool's mask are read. A UE 4.18-4.24 UProperty function's object parameter gets its PropertyClass from UPropertySubclassStart, not FSTRUCTPROP_STRUCT. A non-Function meta-class gives 'not a UFunction'; no CPF_Parm entry gives 'no parameters'.
- **Mutation:** Use WalkClass (the super's parameters appear); read FSTRUCTPROP_STRUCT on a UProperty engine (the MetaClass is named).
- **Files:** `dll/src/Ubel.h`, `dll/src/Ubel.cpp`, `dll/tests/dll_core_test.cpp`

## B4 — Ubel: layout enrichment by value (struct sub-layouts, enum tables, object class, soft-path / lazy / delegate / optional facts)

- **Red:** A pool-faked struct parameter: its sub-fields are copied by value with offsets and bool masks, depth capped at 4. An enum parameter carries its entry table (UProperty and FProperty enum slots both). An object parameter carries its class name. The copied layout survives tearing the fake pool down.
- **Mutation:** Copy the layout, then corrupt the source sub-layout and enum table: the captured layout must not change (a pointer into the memo would).
- **Note:** Struct sub-layouts and enum tables come from address-keyed caches that are never erased (WalkClassEx's memo, s_enumCache). Before using an entry, CaptureParamLayout checks the struct's or enum's FName ints and Outer against a witness; on a mismatch it walks fresh without publishing to the cache. Red: a fake struct and a fake enum whose address is reused under another name are walked fresh.
- **Files:** `dll/src/Ubel.cpp`, `dll/tests/dll_core_test.cpp`

## B5 — Ubel: the checked arm capture and RunArmCapturePass (injected live/key/capture checks; reuse by the 5-field key; states; latency)

- **Red:** With stub ops: a live function gives state Read with latency recorded. A dead slot gives UnloadedBeforeRead. The key changed by the capture stub gives ReplacedBeforeRead. A tail mismatch (numParms 3 against 2 parameters) gives Doubtful. A second arm with the same (addr, outer) but another class or function key captures again (counter 2); one with the identical 5-field key reuses the layout (counter 1). After SealArms nothing is captured. maxArms 1 takes one arm per pass.
- **Mutation:** Skip the post-capture key check; key the reuse by (addr, outer) without the name key.
- **Note:** A doubtful arm (tail mismatch) still decodes, marked doubtful; the tail check applies only when the flags offset is decided; kSnapTruncated is set when the captured layoutEnd exceeds the copy.
- **Files:** `dll/src/Ubel.h`, `dll/src/Ubel.cpp`, `dll/tests/dll_core_test.cpp`

## B6 — Ubel: DecodeParamSnapshot, value types

- **Red:** Stub ctx, no game memory. Float and double. Int8 -1 reads '-1'. A packed mask 0x04 against an unresolved bool. A uint8 enum: 255 gives 'MAX (255)', plus an unknown value. An FName with Number gives 'Tag_2'. An LWC struct gives {X=1.5, Y=2, Z=3} from doubles. ArrayDim 3. A parameter past len gets mark 3. A Return at entry gives '—' with mark 3. The after copy fills only Out, InOut and Return.
- **Mutation:** Use PreviewScalarValue's unsigned path for Int8 (gives 255).
- **Files:** `dll/src/Ubel.h`, `dll/src/Ubel.cpp`, `dll/tests/dll_core_test.cpp`

## B7 — Ubel: DecodeParamSnapshot, pointer family and containers (soft-path facts in ctx, shared delegate/weak helpers, FText header)

- **Red:** Stub ctx. A live object gives mark 1 with its name; a gone one gives mark 2. An unresolved weak value gives UnresolvedWeakLabel's text. The soft path FNames are read at the captured offset, honouring ctx's FName size and TopLevelAssetPath choice. A delegate goes through DescribeDelegateBinding. FString and TArray give 'Num=N (Data 0x...)' with mark 4. FText gives a TextData header (null = empty) with no Num. An optional is set or unset by its captured layout. An unknown type gives hex with mark 5.
- **Mutation:** Decode FText as a TArray header (gives Num); ignore ctx's FName size (the soft-path read is misaligned on a case-preserving build).
- **Files:** `dll/src/Ubel.cpp`, `dll/tests/dll_core_test.cpp`

## P0 — Rig skeleton tools/verify/livefuncs_snap_live.py with --fixture-check (paths as arguments; committed before it runs)

- **Red:** Against the current package: the probes are absent from pe_profile_get (the rig says so and exits 1).
- **Mutation:** A fixture check that passes on an empty table.
- **Files:** `tools/verify/livefuncs_snap_live.py`

## P1 — Fixture: DumperTest58 probes (SnapProbe_Call, SnapProbe_RetOnly, SnapNest_Outer host, SnapProbe_PerFrame through ProcessEvent, SnapLate_Begin / SnapLate_Call); README rows; scripted Shipping repackage

- **Red:** py tools/verify/livefuncs_snap_live.py --fixture-check against the current package: the probes are absent from pe_profile_get. TraceNest_* is untouched, so README.md:653 and the plan's 60-for-20 figure still hold.
- **Mutation:** Dispatch SnapProbe_PerFrame directly from C++ (never reaches the hook: the fixture-check finds no row).
- **Note:** The rig's --fixture-check mode is P0's, so P1's red can run first.
- **Note:** Lone calls are dispatched by name through ProcessEvent from the timer (a timer bound to a C++ method never reaches the hook). SnapProbe_ConstRefOnly(const FString&) pins whether a const-ref-only function takes an after copy. The fixture is C++ only: native classes never unload, so the reload path is unit-tested with fake readers and proven live on Avowed.
- **Files:** `tools/ue-sample/DumperTest58/Source/DumperTest58/DumperTest58Actor.h`, `tools/ue-sample/DumperTest58/Source/DumperTest58/DumperTest58Actor.cpp`, `tools/ue-sample/README.md`

## P2 — Rig tools/verify/livefuncs_snap_live.py (paths as arguments; committed before it runs)

- **Red:** Against the 3638 DLL every new check fails: rows have no fname_key or per_frame; the Start reply has no trace.names or trace.snap; names have no code_addr; pe_snap_layouts is an unknown command.
- **Mutation:** A rig whose checks pass on an absent key (for example .get('names', {}) treated as success) would go green on 3638. The 3638 run is the rig's own red, recorded in its output.
- **Files:** `tools/verify/livefuncs_snap_live.py`

## F1 — Fern: fname_key and per_frame on pe_profile_get rows

- **Red:** Rig: every row carries a 4-int fname_key. Two fetches give the same key for the same function. SnapProbe_PerFrame has per_frame and SnapProbe_Call has not, with or without skip_per_frame. All absent on 3638. Builds with py tools/verify/build_dll.py --targets UE5Dumper.
- **Mutation:** Emit per_frame only when skip_per_frame is asked (the 'without skip' check fails).
- **Note:** Read the four ints once and derive both the row's strings (GetString) and fname_key from them.
- **Files:** `dll/src/Fern.cpp`

## F2 — Fern: pe_profile_start ticks by name (verify keys, ArmState, scoped, trace.names reply); free the previous trace first; D1's unloaded-tick refusal kept for legacy address ticks only; Start time logged

- **Red:** Rig. ticked_names with TraceNest_Outer only (no addresses): trace.names.ticks==1 and trace.scoped. A never-called tick (Opt_ResetObject) is accepted, where 3638 refuses or traces every call. A key with an altered class string goes to names.refused, and alone refuses the Start. After that refused Start, pe_trace_get reports allocated:false. TraceInfoToJson carries scoped, ticked_names and snap_only. The DLL target builds.
- **Mutation:** Skip the key verification (the altered key is accepted). D1's refusal kept for name ticks is killed by a rig case: a name tick whose last-seen address now holds another function, with a valid key, is accepted (by name), where the legacy check would refuse or misclassify it.
- **Note:** All items refused refuses the Start; so does every tick refused when ticks were asked (it never silently turns into a snapshots-only trace). A keyless tick is refused in the UI with a status.
- **Files:** `dll/src/Fern.cpp`, `dll/src/Linie.h`

## F3 — Fern: trace.snapshots at Start (ring caps from parms_size, bytes 8-128 MB, budgets clamped, copier, snap keys in the ArmState); snap in TraceInfoToJson

- **Red:** Rig. SnapProbe_Call and SnapProbe_PerFrame chosen by name: trace.snap.allocated, rings==2, and slots_per_ring equal to K computed by the rig from the caps. 512 choices of 2,048 B into 8 MB are refused (K = 7; the rig computes K with the UI's formula) with the snapshot-buffer message naming the count, and nothing stays allocated. bytes 4 MB is refused. The DLL target builds.
- **Mutation:** Compute K without the 64*n term (the rig's K differs); accept bytes outside 8-128 MB.
- **Files:** `dll/src/Fern.cpp`, `dll/src/Linie.h`

## K1 — Stark (DLL, no unit target): RecordCall(&hint), TraceEnter(..., params, hint), TraceReturn(..., params)

- **Red:** Build with py tools/verify/build_dll.py --targets UE5Dumper. Rig, with F2/F3 built and Stark unchanged: a name tick of TraceNest_Outer gives 0 roots, and no SnapProbe_Call entry record has Taken. After this change: roots == rounds; the Stop reply's snap.taken counts every in-scope call; null_params == 0. (The after copy's values are checked in F5, which reads slots.)
- **Mutation:** Pass a default ArmHint (roots stay 0); pass params at entry only (the after-copy check fails).
- **Files:** `dll/src/Stark.cpp`

## F4 — Fern: the layout-capture worker (a thread per traced Start with snapshot keys, polling every 50 ms, first enum detection kicked at Start); Stop = StopTrace, StopRecording, final pass with a 2 s deadline, seal; names summary before ReleaseIfEmpty

- **Red:** Rig. SnapLate_Call is chosen by name only and first called by SnapLate_Begin after Start: at Stop its arm's layout is read and latency_ms is logged. A never-called choice is listed as not_called in the Stop reply even when the trace was empty and released. 3638 has no names in the Stop reply. The DLL target builds.
- **Mutation:** Seal before the final pass: the rig fires SnapLate_Begin and sends Stop with no gap, so the late arm's layout is read only by the final pass (sealed first, it reads not_read_before_stop). Build the summary after ReleaseIfEmpty (the empty trace's names are missing).
- **Note:** Review 2, MED 1: not MonitorLoop (the disconnect and cancel monitor) and never the interactive pipe thread. The worker holds its own ArmState reference; Stop signals it and waits with a deadline; a worker stuck past the deadline (the first DetectUEnumNames) leaves its arms sealed raw-only and is joined at Free / Reset / shutdown. Each pass logs its time. Rig: Stop returns within about 2.5 s.
- **Files:** `dll/src/Fern.cpp`

## F5 — Fern/Renge: pe_snap_layouts (paged by arms, ~1 MB, layouts de-duplicated per page) and pe_snap_get (copy under the lock, decode outside); pipe count 102 -> 104; pipe-protocol.md flags table and 'does not hold'

- **Red:** Rig. Every SnapProbe_Call entry decodes to its Round's values. The after copy gives OutTwice=2R, InOut+R and ReturnValue=3R. SnapProbe_RetOnly has an after copy, which pins FUNC_HasOutParms. Label's kind is const_ref. orphans==0. py tools/check_derived_counts.py fails until the seven claims say 104. The DLL target builds. An after copy leaves In parameters blank (mark 3). The per-arm decode is a Fern-free helper (Ubel::DecodeSlot over the arms' layouts), unit-tested in dll_core_test with two arms of one name.
- **Mutation:** Decode an after copy's In parameters too (the blank-In check fails); decode every slot with arm 0's layout (the two-arm unit test fails).
- **Files:** `dll/src/Fern.cpp`, `dll/src/Renge.h`, `dll/src/Fern.h`, `docs/pipe-protocol.md`, `CLAUDE.md`, `docs/README.md`, `docs/architecture.md`, `docs/dll-spec.md`, `docs/naming-convention.md`

## F6 — Fern: code_addr on pe_trace_names funcs (Live, native)

- **Red:** Rig: TraceNest_Outer's code_addr lies inside the module's range (absent on 3638), and a script function's code_addr is "". The DLL target builds.
- **Mutation:** Emit code_addr for unloaded functions (the dead-address check fails).
- **Note:** A C++-only fixture has no script function: the rig reports that check as not run there, never as passed; Avowed (Blueprint functions) is its subject.
- **Files:** `dll/src/Fern.cpp`

## U1 — Models and DumpService: NameKey (fname_key), per_frame, TraceStartOptions.TickedNames / Snapshots, trace.names / scoped / snap parsed, pe_snap_layouts / pe_snap_get, code_addr

- **Red:** DumpServiceTests (MockPipeClient). fname_key parses to a NameKey, and its absence to null. The request carries ticked_names and snapshots.funcs with the keys as 4-int arrays (through the (JsonNode?) cast of DumpService.cs:2850-2853). trace.names, scoped and snap are parsed, and their absence reads as an older DLL. Layouts and pages parse (nested values, marks, phase, arm). code_addr parses.
- **Mutation:** Read the key's class ints into the function slots (the round-trip test fails).
- **Note:** TraceStartOptions.Ticked's doc ('empty = trace every call') changes with ticks by name.
- **Files:** `ui/UE5DumpUI/Models/CallTraceModels.cs`, `ui/UE5DumpUI/Models/PeProfileResult.cs`, `ui/UE5DumpUI/Services/DumpService.cs`, `ui/UE5DumpUI/Core/IDumpService.cs`, `ui/UE5DumpUI.Tests/DumpServiceTests.cs`

## U2 — LaneRoutingPipeClient: pe_snap_layouts and pe_snap_get on the bulk lane

- **Red:** A new internal static IsBulk(cmd) is true for pe_snap_layouts, pe_snap_get, pe_trace_get and pe_trace_names, and false for pe_profile_start.
- **Mutation:** Remove pe_snap_get from BulkCommands.
- **Files:** `ui/UE5DumpUI/Services/LaneRoutingPipeClient.cs`, `ui/UE5DumpUI.Tests/DumpServiceTests.cs`

## U3 — Helpers/FunctionTickSet extracted from _ticked, holding per Class::Func the live addresses, a set of NameKeys and the largest ParmsSize

- **Red:** New FunctionTickSetTests. After a refresh with only unloaded rows for a name, the name keeps its keys with an empty address set. A name the page does not show keeps its keys. A by-name toggle covers every class of that name. LiveFuncsTraceTests stay green unchanged. The new test file updates the pinned count of 240 in CLAUDE.md and docs/architecture.md.
- **Mutation:** Clear the keys on a refresh that does not show the name.
- **Files:** `ui/UE5DumpUI/Helpers/FunctionTickSet.cs`, `ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs`, `ui/UE5DumpUI.Tests/FunctionTickSetTests.cs`, `CLAUDE.md`, `docs/architecture.md`

## U4 — Live Funcs ticks by name: unloaded rows with a key tick; the Start sends ticked_names; T7 split; keyless rows keep D1's rule; status by name count; the old-DLL guard

- **Red:** LiveFuncsTraceTests. :526-547 becomes 'an unloaded keyed row ticks by name; ticked_names carries its key; 0x100 is never sent', and its keyless half stays as its own test. :549-578 becomes 'a ticked function that unloaded: the Start runs with ticked_names and an empty ticked, and T7 is not asked' (red on today's refusal). :598-612 splits: keyed unloaded rows count as something to tick, so T7 asks; keyless, unchanged. ASM stays refused on unloaded rows. A reply without trace.names after names were sent stops and releases (str.LF.Trace.NamesNotSupported).
- **Mutation:** Keep LiveFuncsViewModel.cs:409's IsUnloaded refusal for keyed rows; drop the guard (the fake old DLL keeps recording).
- **Files:** `ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs`, `ui/UE5DumpUI/Resources/Strings/en.axaml`, `ui/UE5DumpUI.Tests/LiveFuncsTraceTests.cs`

## U5 — Live Funcs: snapshot choice (needs a key, Trace on, parameters), Start options (snapshots-only skips T7), refusals and status notes, disconnect

- **Red:** New LiveFuncsSnapshotTests. ToggleSnapshot is refused with Trace off, while recording, on keyless rows, on rows from an earlier connection, and on rows with NumParms 0 and nonzero flags. An unloaded keyed row is choosable. The Start sends snapshots.funcs with keys and parms_size. Nothing ticked and something chosen: ConfirmTraceAllCalls is not called. ResetOnDisconnect clears the choices. The pinned test-file count is updated.
- **Mutation:** Ask T7 when only snapshots are chosen.
- **Note:** The SnapshotBufferExponent view-model property (8-128 MB, default 32) is introduced here; U8 persists it and binds the slider.
- **Files:** `ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs`, `ui/UE5DumpUI/Resources/Strings/en.axaml`, `ui/UE5DumpUI.Tests/LiveFuncsSnapshotTests.cs`, `CLAUDE.md`, `docs/architecture.md`

## U6 — Live Funcs: stored WindowMs, IsPerFrame, the pure snapshot estimate (2-slot rule, K, K<8 warning, OD5 orange, grey budget note), D3 game figure

- **Red:** On fixed rows and window: rate per key; per-function and total clamps; slot size (ParmsSize 0 gives 256, cap 2,048); slots per call 2 when flags are 0 or have 0x00400000, else 1; MB a minute; K and the busiest's seconds; orange when they fall below the trace estimate; the K<8 refusal predicted. TraceGameMb includes the snapshot MB only while something is chosen. A test reads dll/src/Linie.h and pins kSnapMaxCopy, kSnapUnknownCopy, kSnapMinSlots and the 24-byte header to the UI's constants.
- **Mutation:** Count 1 slot for after-copy functions; change kSnapUnknownCopy on one side only.
- **Note:** T13's comparison in scoped and snapshots-only mode uses the trace window estimated from the ticked and chosen rows' rates (lone records are 80 B a call), not the every-call estimate.
- **Files:** `ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs`, `ui/UE5DumpUI.Tests/LiveFuncsSnapshotTests.cs`

## U7 — Live Funcs: 'Snapshot shown rows' bulk choice and Clear, through the estimate

- **Red:** A bulk choice over a filtered view takes the shown rows only, per-frame rows included. It skips keyless rows and rows with NumParms 0 and nonzero flags. The status says what was left out. Disabled while recording.
- **Mutation:** Choose from _allEntries instead of the shown rows.
- **Files:** `ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs`, `ui/UE5DumpUI.Tests/LiveFuncsSnapshotTests.cs`

## U8 — Live Funcs view: Snapshot column, visibility loop, per-frame marker, Snapshot row (OD4 slider 8-128 MB default 32, estimate, memory), strings, persistence

- **Red:** Source pins. The visibility loop (LiveFuncsPanel.axaml.cs:80-87) also matches str.LF.Col.Snapshot. The column is CanUserSort=False with no SortMemberPath (DataGridSortWiringTests green). SnapshotBufferExponent is in LiveFuncsPersist, as LiveFuncsTraceTests pins the trace slider. check_axaml_strings and check_vm_status_literals are green.
- **Mutation:** Leave the Snapshot header out of the visibility loop (the column shows with the experimental tabs off).
- **Files:** `ui/UE5DumpUI/Views/LiveFuncsPanel.axaml`, `ui/UE5DumpUI/Views/LiveFuncsPanel.axaml.cs`, `ui/UE5DumpUI/Models/UiOptionsSettings.cs`, `ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs`, `ui/UE5DumpUI/Resources/Strings/en.axaml`, `ui/UE5DumpUI.Tests/LiveFuncsSnapshotTests.cs`

## U9 — Call Trace and Live Funcs say what was traced: scoped by name, snapshots-only, unscoped; the T8 copy marks names as waiting or never called

- **Red:** Summary with Info{Scoped, TickedNames 2, Ticked 0} names the scope, not str.CT.Status.Unscoped (red at CallTraceViewModel.cs:352-353). SnapOnly gives the snapshots-only sentence. An older DLL without 'scoped' falls back to Ticked>0. The Live Funcs Start status for name-only ticks is not RecordingAll (LiveFuncsViewModel.cs:599-601). The Stop note lists not_called names.
- **Mutation:** Keep the Ticked==0 test for Unscoped.
- **Files:** `ui/UE5DumpUI/ViewModels/CallTraceViewModel.cs`, `ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs`, `ui/UE5DumpUI/Views/CallTracePanel.axaml`, `ui/UE5DumpUI/Resources/Strings/en.axaml`, `ui/UE5DumpUI.Tests/CallTraceViewModelTests.cs`, `ui/UE5DumpUI.Tests/LiveFuncsTraceTests.cs`

## U10 — Call Trace list: resizable, remembered column widths and detail-pane width; Object tooltip; CallTraceUiOptions on the settings root

- **Red:** CallTraceViewModelTests: the widths clamp to their minimums and round-trip through CallTraceUiOptions, which is a property of UiOptionsSettings so the source-generated context reaches it. Source pins: every row cell binds the view-model width, and the Object cell has a ToolTip. Red on today's fixed ColumnDefinitions (CallTracePanel.axaml:124, :139).
- **Mutation:** Keep CallTraceUiOptions outside the root (the round-trip loses it).
- **Note:** The round-trip test serializes through the source-generated UiOptionsJsonContext.
- **Files:** `ui/UE5DumpUI/Views/CallTracePanel.axaml`, `ui/UE5DumpUI/Views/CallTracePanel.axaml.cs`, `ui/UE5DumpUI/ViewModels/CallTraceViewModel.cs`, `ui/UE5DumpUI/Models/UiOptionsSettings.cs`, `ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs`, `ui/UE5DumpUI.Tests/CallTraceViewModelTests.cs`

## U11 — Call Trace detail: object-address wording, the Address setting, native entry as a CE address, script / not found / unknown lines

- **Red:** Detail(i) under HexNoPrefix has no '0x' on the function-object or object lines (red: CallTraceViewModel.cs:460-466 hard-codes 0x). A native function with code_addr inside the module shows "Game.exe"+RVA. FUNC_Native set with code_addr "" says 'native entry not found'. Nonzero flags without FUNC_Native say script. Flags 0 say unknown. An unloaded native says 'not read'.
- **Mutation:** Say 'script' when the flags are 0.
- **Files:** `ui/UE5DumpUI/ViewModels/CallTraceViewModel.cs`, `ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs`, `ui/UE5DumpUI/Resources/Strings/en.axaml`, `ui/UE5DumpUI.Tests/CallTraceViewModelTests.cs`

## U12 — Call Trace: snapshot load before the release (bulk lane), CallTraceSnapshots stored on the CallTrace, join by entrySeq, per-arm layouts, Summary counters

- **Red:** Fake call log: names, then pe_snap_layouts pages, then pe_snap_get pages, then the release; ReleasedGens gets the gen only after the last snapshot page. A stale reply or a gen change mid-paging drops the load with str.CT.Status.Changed. A trace without snap reads no snapshot pages and shows no parameters after one that had them. SnapOf(call) finds the entry and after copies by entrySeq, each with its arm's layout.
- **Mutation:** Release before the snapshot pages; keep the previous trace's snapshots on a load without snap.
- **Files:** `ui/UE5DumpUI/ViewModels/CallTraceViewModel.cs`, `ui/UE5DumpUI/Models/CallTraceSnapshots.cs`, `ui/UE5DumpUI/Models/CallTrace.cs`, `ui/UE5DumpUI.Tests/CallTraceViewModelTests.cs`

## U13 — Call Trace: detail TabControl (Call | Parameters), view C rows, reasons (Lone / Excluded / budget / overwritten / raw-only arm and why / null / fault / truncated), Changed, tree markers, 'Only calls with parameters' (works with no text)

- **Red:** Params(i) gives its reason for taken, budget, overwritten, lone, excluded, no-after, raw-only (unloaded_before_read / not_read_before_stop), fault and truncated. Changed is true only where the raw bytes differ. A Return at the call shows '—'. The checkbox alone, with an empty filter, lists only calls with parameters. Detail(i) is unchanged for an unchosen call.
- **Mutation:** Return to the tree on empty text even with the checkbox on (CallTraceViewModel.cs:384-391's path).
- **Files:** `ui/UE5DumpUI/Views/CallTracePanel.axaml`, `ui/UE5DumpUI/ViewModels/CallTraceViewModel.cs`, `ui/UE5DumpUI/Services/CallTraceTree.cs`, `ui/UE5DumpUI/Resources/Strings/en.axaml`, `ui/UE5DumpUI.Tests/CallTraceViewModelTests.cs`

## U14 — Export: JSONL snapshot keys, layout lines per arm, scoped / snapshots_only in the header; a separate params CSV

- **Red:** CallTraceTreeTests: params, params_hex_in/out, snapshot, lone and excluded appear only on chosen calls. One snapshot_layout line per arm follows the header. The header has scoped and snapshots_only. The params CSV column list is pinned and armoured. CsvColumns' last two stay func_unloaded / func_reused (ui/UE5DumpUI.Tests/CallTraceTreeTests.cs:177-178).
- **Mutation:** Append the params columns to the calls CSV (the last-two pin fails).
- **Files:** `ui/UE5DumpUI/Helpers/CallTraceExport.cs`, `ui/UE5DumpUI/ViewModels/CallTraceViewModel.cs`, `ui/UE5DumpUI.Tests/CallTraceTreeTests.cs`

## X1 — Plan 'Step 2 built' (decisions, TR6 / copy-size / after-copy deviations, name matching), dev-log entry, verification-register and todo rows (D1's tick refusal row rewritten; the Int8 preview quirk as its own row); comment pass

- **Red:** py tools/check_all.py (check_comment_refs, check_derived_counts) and comment_impact.py --staged report nothing. The plan's status line no longer says step 2 is not started. The D1 live row 'A Start with only an unloaded function ticked: Refused' is superseded by the T10 row.
- **Mutation:** Leave a stale pipe count or test-file count (check_derived_counts fails).
- **Files:** `docs/live-funcs-timeline-plan.md`, `docs/dev-log.md`, `docs/verification-register.md`, `docs/todo.md`

## X2 — AOT publish and live checks: DumperTest58 rig and UI walkthrough, livefuncs_trace_live.py re-run, Avowed reload acceptance, UE418 if packaged; measurements and the machine recorded

- **Red:** The walkthrough's first check (a Snapshot column with the experimental tabs on) fails on 3638. After build.ps1 -Mode Publish the exe is about 54 MB, with its SHA recorded. Avowed: inventory functions ticked and chosen while '(unloaded)'; the inventory opened and closed twice. Accepted by D1's unloaded count plus arms: each chosen name is armed, its layouts are read and its calls decode; whether the addresses changed between openings is recorded (it depends on the game's GC), not required. latency_ms, skipped_budget and the Start and load times are recorded.
- **Mutation:** Hand over the non-trimmed build (the size and SHA check fails).
- **Files:** `docs/live-funcs-timeline-plan.md`, `docs/verification-register.md`, `docs/dev-log.md`
