# todo.md — closed verification rows, archived 2026-08-23 (build 3337)

> **What this is.** 84 `###` sections lifted **verbatim** out of
> [todo.md](../todo.md)'s `## Pending live-game verification` register. Nothing was edited —
> the archive convention in this folder is *moved, not rewritten* — so every evidence block,
> finding tag and measurement reads exactly as it did in the register.
>
> **Why.** The register had grown to **14,554 lines, 75% of a 19,162-line file**, and the size was
> already causing real harm: the 2026-08-23 classification pass partitioned todo.md to line 16669
> and therefore analysed **~12 already-closed rows as open**, burning a whole round of agent
> analysis. That doc's own recommendation was *"the cheapest work in the whole queue: a bookkeeping
> pass"*. This is that pass.
>
> ⭐⭐ **The selection rule, and the trap it exists for.** A `✅` heading does **NOT** mean a section
> is finished. Of the 103 `✅` sections in the register, **13 carried explicit remaining work in
> their bodies** — *"Still owed a live check"*, *"The SCROLL half is still open"*, *"Steps 1 and 3
> are still open"*. Archiving on the marker alone would have buried them. So a section moved here
> only if **all three** held:
>
> 1. its heading is `✅`;
> 2. its body contains no *"still open / still owed / remains open / 尚未 / 仍未"* sentence;
> 3. its heading is not a pointer stub whose write-up lives in the register.
>
> The check was deliberately **conservative**: 3 of the 16 body matches were false positives (one
> says *"Nothing here is still owed"*, one is struck through and superseded, one is *"while the
> editor is still open"*) and they were kept in the register anyway. Keeping a finished row costs
> a few hundred lines; burying an unfinished one costs a verification.
>
> **What stayed:** 62 sections — 43 not-closed, 16 whose body admits open work, 3 pointer stubs.
>
> ⚠ **`check_live_verification.py` is the only tool that parses todo.md.** It requires the
> `## Pending live-game verification` heading plus the keys `V1a`, `V1c`, `NumericAll` and
> `FreezeOutcome` to appear inside that section's body. After this move they survive **3 / 4 / 13 /
> 1** times respectively — note **`FreezeOutcome` is carried by exactly ONE section, and it is 🟡**.
> Never archive that one without moving the key.
>
> ⚠ **Line numbers cited elsewhere against todo.md are stale from 2026-08-23.** Several docs cite
> `todo.md:NNNNN`. The repo rule already applies: **re-grep the identifier, never trust the line
> number.**

---

### ✅ BUILT + LIVE-VERIFIED 2026-08-21 `[PROPSEARCHCAP-2026-08-19]` — Property Search has a Max now

**The panel had no cap control, so every search was pinned to the wire default of 200** — very low
for a query like `Health` on a real game, and a wanted field could sit past the cap with nothing
the user could do. Audit #5 **Z10** had already fixed the *dishonest* half (a status line advising
"raise Max" on a panel with no Max) by deleting the advice; this adds the lever, so the advice
comes back.

**What shipped**: `PropertySearchCap` on the VM (+ the `decimal?` `SNAPINTERVAL` façade), a
`NumericUpDown` clamped 100–50000 with `ClipValueToMinMax`, `limit:` finally passed to
`SearchPropertiesAsync`, persistence in `PropertySearchUiOptions`, a **`Math.Clamp` on LOAD**, and
the matching **DLL-side clamp** in `CMD_SEARCH_PROPERTIES`.

⚠ **The default stays 200 and is NOT raised to Instance Finder's 5000.** A property-search row is a
match per property per class *with a resolved preview value*, so it is far heavier than an instance
address. The point of the work is that the user can raise it, not that everyone pays for it.

⚠ **"raise Max" is offered only while the cap can actually go up.** At the ceiling it would be the
same lie Z10 removed, just in a new place. Two tests pin both halves.

⚠ **The UI clamp is a convenience, not the guarantee** — any pipe client can put an integer on the
wire, so the ceiling has to exist server-side too. The **wire default stays 200** deliberately: an
older client that sends no `limit` must not change behaviour on a DLL upgrade alone.

⭐ **The deferral reason was "cannot be visually verified in an unattended session". It was wrong** —
computer-use drove all of it, on DumperTest (UE 5.04, 25,179 objects):

| # | step | result |
|---|---|---|
| 1 | Properties tab after connect | **`Max [200]`** present in the toolbar, defaulted to 200 |
| 2 | query `a`, Search | `Found 200 properties in 254 classes` — capped |
| 3 | spinner ×8 → `Max 1000`, Search | **`Found 1000 properties in 471 classes … ⚠ STOPPED at th…`** |
| 4 | close, reopen | `Max` reads **1000**; `ui-options.json` holds `propertySearchCap: 1000` |
| 5 | hand-edit that file to **`0`**, reopen | `Max` reads **100** and the ▼ spinner is greyed — clamped on load |

⚠ Step 3 is the one that matters: 200 → 1000 rows is the control reaching the DLL, not just
repainting. Step 5 is a value **no control can produce** — the file is plain text a user can edit,
and an unclamped `0` there would make every search return nothing with no visible cause.

**DLL clamp, over the pipe with the UI closed** (`kMaxPipeInstances=3`, the UI holds 2):

```
limit omitted -> 200      limit 0  -> 1      limit 1000   -> 1000
limit 1       -> 1        limit -5 -> 1      limit 999999 -> 9781 (the whole result; ceiling unreached)
```

`0` and `-5` returning **1** is the new clamp; before it they reached `Aura::SearchProperties`
unchanged and returned zero rows — a silent total absence indistinguishable from "the field is not
there", which is [the exact triage trap](working-lessons.md) §5 exists for.

**Tests**: `PropertySearchCapTests` (5) + the rewritten Z10 pair. ⚠ The Z10 test
`Cap_advice_never_mentions_a_Max_control_this_panel_does_not_have` **failed on this change, exactly
as designed**, and was **rewritten rather than deleted** — its invariant ("never name a lever the
user cannot reach") did not change, only which side of it the panel is on. `PropertySearchCapTests`
reads the AXAML and `Fern.cpp` back rather than trusting that three files in three languages were
kept in step by hand.

### ✅ FIXED + LIVE-VERIFIED 2026-08-21 `[RELAUNCHPIPE-2026-08-19]` — a deferring instance now WATCHES instead of surrendering

**There were TWO copies of this defect, not one, and the live check is what found the second.**

Both asked the same single question — *does `CreateFileW(PIPE_NAME, OPEN_EXISTING)` succeed?* — and
on a yes gave up **permanently**. On a title that relaunches itself the holder is the process that
is busy dying, so the survivor is left with the DLL mapped, the game running fine, and nothing able
to connect, for the rest of its life.

| copy | entry point | what it skipped | who hits it |
|---|---|---|---|
| `Frieren.cpp` `UE5_StartPipeServer` | proxy `ProxyStart`, CE Lua | the pipe server | **the reported OCTOPATH case** — its log line is this one's wording |
| `Heiter.cpp` `DllMain AutoStart` | CE / manual inject | the **whole** auto-start: `UE5_Init` *and* the server | found 2026-08-21 by running the repro |

⭐ **The second copy was invisible to reading and obvious to running.** Reproducing with two
DumperTest instances, the second logged
`DllMain AutoStart: pipe already exists (another UE5Dumper instance running) — skipping auto-start`
— a *different message from a different guard*, reached before `UE5_StartPipeServer` is ever called.
Fixing only the reported one would have left the manual-inject path exactly as broken.

**The fix.** The three-way decision is `Voll::DecideStart(pipeExists, holderPid, selfPid)` →
`StartNow` / `AlreadyOurs` / `DeferAndWatch`, with the holder identified by
`GetNamedPipeServerProcessId` — so "the pipe answers" is no longer conflated with "someone else owns
it". On `DeferAndWatch` each site starts a bounded watcher (`Voll::kClaimWatch*`, 500 ms × 600 =
5 min, shared so the two cannot drift) that polls until the pipe is genuinely gone and then does its
own recovery: Frieren claims the server, Heiter runs the auto-start it deferred.

⚠ **`ERROR_PIPE_BUSY` is not "free"** — it means the holder is alive with every instance in use, so
the watcher keeps waiting. Only *not found* counts.

⚠ **The return value is deliberately still `true` on the defer path.** The finding asked for a
distinguishable return, and that is the one part not done: both callers map it straight onto
`g_invokeMailbox.initState`, so a `false` would publish `INIT_FAILED` for the perfectly ordinary
case of a second game running — a louder regression than the bug being fixed. What changed is that
the deferral is no longer permanent.

⚠ **Heiter deliberately does NOT just fall through** to let Frieren's watcher handle the pipe. That
would make the deferring process pay a full GObjects scan while another instance owns everything,
which is the cost that guard exists to avoid. It waits first and scans only if the pipe frees.

**LIVE-VERIFIED**, two DumperTest instances, the second injected while the first held the pipe:
```
DllMain AutoStart: pipe is held by PID 36448 — deferring auto-start, and watching for it to free
        …first instance killed…
AutoStartWatcher: pipe freed by PID 36448 after 63500 ms — running the auto-start that was deferred
UE5_AutoStart: pipe server started, init complete -> initState=2
AutoStartWatcher: deferred auto-start returned
```
Then proven to actually serve, not merely to log: a pipe client connected to the survivor,
`ensure_scanned` succeeded and `find_instances` returned **7** `CharacterMovementComponent`
instances. ⭐ The control is the pre-fix run twenty minutes earlier on the same rig, where that same
second instance logged `skipping auto-start` and never recovered.

**Pinned by `Test_Voll_DecideStart` + `Test_Voll_DecideStart_SelfPidIsNotSpecialCasedAway`**
(1636 → 1644 assertions). The decision lives in `Voll.h` for the reason that header exists: no test
target compiles `Fern.cpp`, `Frieren.cpp` or `Heiter.cpp`. ⭐ **Shown able to fail**: restoring the
old skip-and-forget rule failed exactly the four assertions that distinguish it, while
`free pipe starts now` and `our own pipe is a no-op` stayed green — they do not depend on the change.

📌 **One clean OCTOPATH run 2026-08-21, and it did NOT race** — launched with
`steam.exe -applaunch 921570`, the log shows **one** `UE5Dumper DLL loaded`, **one** PID (42768),
**zero** hits on either skip guard, and the pipe served normally. That is **1 run, and it does not
refute the original 3-of-3**; what it suggests is that the *launch route* decides whether the title
relaunches itself, which would explain why `docs/test-games.md` could record OCTOPATH as winmm-proxy
LIVE-VERIFIED on 2026-08-18 while the race reproduced reliably a day later. ⚠ It also means **this
title cannot be used as the regression fixture for the fix** unless the failing route is identified
first — a clean run here proves nothing either way.

⚠ **Still true, and worth keeping:** each process start rotates `init-0.log`, so two instances write
to *different files* and a survivor reads as one process contradicting itself. That is why the
evidence above is quoted from the survivor's own `init-0.log` after the rotation.

### ✅ FIXED + LIVE-VERIFIED 2026-08-21 `[CDOSCOPE-2026-08-20]` — the Preview picked its sample class by a PROXY, not by having a live instance

*Found because it misled me into choosing the wrong fixture for `AA12`/`AA13` step 3 — which is
exactly how it would mislead a user. **LOW-MED**: nothing is corrupted, but the row tells you there
is nothing live and the action on that same row then reports live instances.*

**Measured on DumperTest.** Property Search row
`NiagaraComponent · WarmupTickCount · IntProperty · 0x624` previews as:
```
0 (CDO default)
```
Freezing that same row and ticking it reports (with `UE5_DEBUG=1`):
```
[Freeze] Started: NiagaraComponent::WarmupTickCount = 9999 (int32@0x624) on 2 instance(s)
```

**Both are internally correct; the two halves just use different scopes.**

| | scope | source |
|---|---|---|
| the `(CDO default)` marker | **exact class** — the preview walk skips any object whose `ClassPrivate` is not the row's class *exactly* (`Aura.cpp`: `if (!needPreviewClasses.count(cls) …) continue;`), then falls back to the CDO and marks the row | `Aura.cpp` ~4708-4742 |
| **Force** and **Freeze** on that row | **derived** — `FindInstancesDerivedFrom`, every live instance of the class *and every subclass* | `Solide` / the freeze helper |

⇒ **A row can read "CDO default" and still have live instances the action will hit.** The Force
dialog even says so in its own caveat (*"every live X and every subclass"*), so the scopes are
deliberate — it is the **marker** that is silently narrower than everything else on the row.

**Fix shape (not applied):** either compute the marker with the same derivation test the actions
use, or word it for what it actually means — *"no instance of exactly this class; subclasses not
checked"* — rather than the bare `(CDO default)`, which reads as "nothing is live".

⚠ **This is the same exact-vs-derived split as `[FREEZESCOPE-2026-08-18]` and audit #5 `A6`**, one
layer up: those fixed the *action's* scope, this is the *preview's*. Worth fixing together so the
row cannot disagree with itself again.

*Not fixed — found during a verification pass.*


**FIXED 2026-08-21. ⚠ The filed diagnosis was wrong, and finding that out took a diagnostic build —
worth recording, because the wrong diagnosis produces a fix that changes nothing.**

**What the entry said:** the marker is exact-scoped while the actions are derived-scoped, so a row
can read `(CDO default)` while live *subclass* instances exist.

**What was actually true.** A `Sein::Info` diagnostic in the preview walk printed, for the reported
`NiagaraComponent · WarmupTickCount` row:
```
CDOSCOPE-DIAG: needPreview=2 exact=0 derived=0 cdo=2 cnt=25179
CDOSCOPE-DIAG:   pc=0x19F02797000 exact=0 derived=0 cdo=1
```
while the row itself reported `class_addr=0x19F02797800`. **Different classes** — and
`find_instances` showed the live pair `NiagaraComponent0 ×2` sitting on `0x…797800`, the row's own
class. So the two live instances were **exact-class**, not subclass, and exact-vs-derived cannot
explain the row at all. A first attempt built purely on the entry's diagnosis — adding a
descendant search under `previewClassAddr` — was **live-tested and changed nothing**, which is what
sent me to the diagnostic.

**The real cause** is at `Aura.cpp:4488`, where the preview's sample class is chosen:

```cpp
// Update preview-source if THIS subclass is more derived
// (bigger PropertiesSize) than the previous best -- bias
// toward leaf classes that actually have live instances.
if (ci.PropertiesSize > existing.previewPropertiesSize) { ... }
```

⭐ **The comment states the intent and the code substitutes a proxy for it.** "Bias toward leaf
classes that *actually have live instances*" is tested by comparing `PropertiesSize` — a stand-in
for having instances, never a test of it. Here it picked `0x…797000`, which has only a CDO, over
`NiagaraComponent`, which had two live objects. This is audit #4's own root-cause pattern 4b
verbatim: *a cheap proxy signal substituted for a predicate a sibling in this repo already computes
correctly.*

**The fix.** The preview is now keyed on the **defining class** (`m.classAddr`) — the same class
Force (Solide) and Freeze target — and looks for a live instance of it **or any subclass**, in the
order **exact → derived → class default** (`Aura::ChoosePreviewSource`). Sampling anywhere in that
hierarchy is sound for the reason the original code already gives: the property sits at the same
offset on every subclass. The `std::swap(classAddr, previewClassAddr)` around
`ResolvePropertyPreviews` is **gone** — it existed only to make the lookup use the size-picked
subclass. `previewClassAddr == 0` still means "skip this row" for the deep-descent nested leaves;
that zero is load-bearing and is preserved.

The subclass walk is a per-UClass verdict cache over the super chain, the same shape (and for the
same reason) as `FindInstancesDerivedFrom`'s `derivedCache`, bounded at 64 levels with a self-loop
guard like `Ubel::ResolveFunctionInChain`.

**A row now says which kind of sample it got:** unmarked = a live exact-class instance;
`(subclass instance)` = live, but of a subclass; `(CDO default)` = nothing live **anywhere in the
hierarchy**. ⚠ That last wording is unchanged but its **claim is stronger** than before — it now
means the actions will find nothing either, which is precisely what the entry asked for.

**LIVE-VERIFIED on DumperTest**, freshly built DLL, before → after:

| row | before | after | why |
|---|---|---|---|
| `NiagaraComponent · WarmupTickCount` | `0 (CDO default)` | **`0`** | 2 live exact `NiagaraComponent0` |
| `NiagaraSystem · WarmupTickCount` | `0 (CDO default)` | `0 (CDO default)` | 0 exact-live, 0 derived-live — honest |
| `Engine · MaxPixelShaderAdditiveComplexityCount` | — | **`2000 (subclass instance)`** | 0 exact-live, 6 derived incl. `GameEngine` |

Across a 93-row `Count` search the tally moved `71 CDO / 22 unmarked / 0 subclass` →
`65 CDO / 21 unmarked / 7 subclass`, totals matching. Each of the three example rows was
cross-checked against `find_instances` for exact-live and derived-live counts, so the marker is
confirmed against the pool rather than just against itself.

**Pinned by `Test_Aura_ChoosePreviewSource` + `Test_Aura_PreviewSourceSuffix`** (1626 → 1636
assertions). The ordering rule lives in `Aura.h` deliberately: no test target compiles `Aura.cpp`,
so a rule left in the walk would be unpinnable. ⭐ **Shown able to fail**: flipping the
derived/CDO preference back to the defective order failed exactly `derived beats the CDO` and
nothing else.

⚠ **What is NOT covered by a test:** the walk itself — the cache, the 64-level bound, the
exact/derived/CDO classification of each object — is in `Aura.cpp` and therefore uncompiled by any
test target. Only the decision rule and the marker strings are pinned. The live table above is the
evidence for the walk.

### ✅ FIXED 2026-08-21 `[FREEZEINJECT-CRLF-2026-08-20]` — "Inject Freeze Helper" reports FAILURE on a write that SUCCEEDED

*Found immediately after `[FREEZEUNTICK-2026-08-20]`, by following the advice that defect's own
message gives. **MED** — the write is fine, but the tool tells the user the setup step failed, and
that is the step everything else in the Freeze feature depends on.*

**Repro:** UI → **Tools → Inject Freeze Helper into Current CE Table**, with CE running and
AOBMaker Connected. Status bar: `Inject freeze helper failed: Stream size mismatch: wr…`, and the
log has it in full:
```
[WARN] AOBMaker InjectTableFile 'ue5_freeze_helper.lua' failed:
       Stream size mismatch: wrote 58345, stream has 57208
```

⭐ **The arithmetic identifies the cause exactly, with nothing left over:**

| | |
|---|---|
| `scripts/ue5_freeze_helper.lua` on disk | **58,345 bytes** |
| CRLF line endings in it | **1,137** |
| 58,345 − 1,137 | **57,208** — *precisely* the stream size CE reports |

So the file is written with **CRLF**, CE's table-file stream stores it **LF-normalised**, and the
post-write check compares *bytes written* against *stream size*. It will therefore fail for **any**
file with CRLF endings — which is every file in this repo on Windows.

✅ **And the write really did succeed** — queried from CE's own Lua Engine, not inferred:
```lua
local tf = findTableFile('ue5_freeze_helper.lua')
--> FOUND, stream size=57208
```
The freeze then worked end to end (see the AA12/AA13 block), so **nothing is broken except the
verification and the message it produces**.

**Fix shape:** compare against the LF-normalised length, or re-read the stream and compare content,
rather than comparing a CRLF byte count to an LF stream. Effort S, risk low.
⚠ **Do not "fix" it by writing LF from the UI** without checking what else reads that file — the
mismatch is in the *check*, and the stored content is already correct.

*Not fixed — found during a verification pass.*


> ### ✅ CONFIRMED 2026-08-20 with a BEFORE/AFTER control `[FREEZEINJECT-CE-2026-08-20]`
>
> The original report inferred "the write succeeded" from the arithmetic plus a single
> `findTableFile` → FOUND. **The state has now been shown to CHANGE across the operation**, which is
> the control that was missing — an already-present file would have read FOUND either way.
>
> Measured from CE's own Lua Engine, same table, same session:
>
> | when | `findTableFile('ue5_freeze_helper.lua')` |
> |---|---|
> | before the inject | returns **no value at all** (`tostring()` errors with *"value expected"*; a second run guarded with `h~=nil` printed `helper_present=false`) |
> | the inject | UI toolbar: *"Inject freeze helper failed: Stream size mismatch: wr…"* |
> | after the inject | `helper_present=true`, `stream_size=**57208**` |
>
> 58,345 − 1,137 CRLF endings = 57,208 exactly, so the stream holds the **LF-normalised** content and
> the check is comparing a CRLF byte count against it. **And the stored content is not merely
> present but usable**: ticking the record immediately afterwards produced
> `[Freeze] Started: DumperTestActor::TickCount = 9999 (int32@0x6A8) on 1 instance(s)` and the value
> held — the helper the "failed" write installed is what ran.
>
> ⚠ **Two rig traps worth carrying into the fix.** The failure branches write to
> `MainWindow.StatusText` **only** — `_log.Info` sits on the success path
> ([MainWindowViewModel.cs:3312](ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:3312)) — so a failed
> inject leaves **no log line at all**. And `StatusText` is the *toolbar* text, capped at
> `MaxWidth="360"` with `CharacterEllipsis`, so the message is truncated on screen with the full text
> only in its tooltip. Reading the **panel's** status line instead (a different property that still
> showed the previous action's text) made one attempt here look like a silent no-op. Whoever fixes
> this should consider logging the failure too, not just the success.


**FIXED 2026-08-21** (commit `1ab753cf`).

**The arithmetic, verified on disk:** the freeze helper is **58,345 bytes** with exactly **1,137**
CRLF endings, and `58345 − 1137 = 57208`. CE stores a table file LF-normalised and the post-write
check compares against that, so a CRLF payload made a **successful** write report
`wrote 58345, stream has 57208`.

**Fixed in CODE, not by rewriting the `.lua` — and that distinction is the whole point.** Both
helpers are `i/lf` in the index with `core.autocrlf=true` and (until now) no `.gitattributes`, so
the working tree's line endings are a property of the **checkout**: on this machine the freeze
helper happened to be CRLF and the invoke helper LF, which is the *only* reason the invoke helper
ever injected cleanly. A fresh clone can flip either. New
`CeLuaHygiene.NormalizeTableFilePayload` folds CRLF and lone CR, applied in **both** resource
readers. `.gitattributes` (`*.lua`, `*.CT` `eol=lf`) was added as belt-and-braces so the tree stops
disagreeing with the index — the code no longer depends on it.

⚠ **The status line will now report 57,208 rather than 58,345.** That is the correction — the old
number over-reported by 1,137 — not a shrunken payload.

**Tests**: fold/idempotence/the 1,137-byte identity, plus **both** inject commands asserted CR-free.
The invoke one is the test that catches the fresh-clone case; it passes today only because of how
this checkout happened to land, which is exactly why it is asserted. ⭐ **Shown able to fail**:
reverting the freeze normaliser failed `InjectFreezeHelperLua_SendsLfOnly` at line 232; restoring it
went green.

### ✅ FIXED + CE-VERIFIED END-TO-END 2026-08-21 `[FREEZEUNTICK-2026-08-20]` — an in-`[ENABLE]` untick never survives, in **36** places

*Found while running `AA12`/`AA13` with Cheat Engine 7.7 + the AOBMaker plugin, DumperTest attached.
This is the exact failure mode `AA12`/`AA13` exists to prevent — **"the freeze script must stop
lying about success"** — and CLAUDE.md's rule states it outright: "**A bail-out that applied NOTHING
must untick the record** … otherwise CE leaves the row ticked and the user is told a cheat is active
when nothing was set."*

**What was measured, in order:**
1. Property Search → `DumperTestActor.TickCount` (IntProperty, `0x6A8`, live value ticking ~1 Hz) →
   **Freeze** → *"Freeze script created in CE: Freeze: DumperTestActor::TickCount = 9999"*.
2. CE attached to `DumperTest.exe`, record ticked. It bails out with a correct, actionable message:
   `[Freeze] ue5_freeze_helper.lua not found in this table.` / `Setup: UE5DumpUI -> Tools -> Inject
   Freeze Helper into Current CE Table`.
3. ✅ **Nothing was applied** — `TickCount` kept climbing (`430` → `440` across two refreshes), so the
   bail-out really did abort before writing.
4. ⛔ **But the record is left ACTIVE.** Read from CE's own Lua Engine, not from an icon:
   ```lua
   local r = getAddressList().getMemoryRecord(0)
   print(r.Description); print('Active=' .. tostring(r.Active))
   --> Freeze: DumperTestActor::TickCount = 9999
   --> Active=true
   ```
5. **The generator is not at fault — it emits the untick.** `FreezeScriptGenerator.cs`
   `AppendHelperLoader` produces exactly:
   ```lua
   if not tf then
     showMessage('[Freeze] … not found in this table.…')
     if memrec then memrec.Active = false end
     return
   end
   ```
6. **And the property is writable at that moment** — assigning it from the Lua Engine *externally*
   unticks the row immediately (`r.Active = false` → checkbox empties). So this is not a broken API
   or a read-only property.

⇒ **The in-script `memrec.Active = false` does not survive**, while the same assignment from outside
does. **Leading hypothesis, explicitly NOT yet measured:** CE finalises activation *after* running
the ENABLE section, overwriting a value the script set during it — in which case the fix is the
**deferred** untick this repo already uses for the momentary-action shape (CLAUDE.md distinguishes
the two), not the immediate one. Confirming that needs a minimal record whose ENABLE does nothing
but `memrec.Active=false`.

⚠ **Scope — and it has since been WIDENED by measurement, not by inference.** First reproduced on
the *helper-not-found* bail-out. `AA12`/`AA13` step 2's own scenario, **DLL-not-injected**, was then
run explicitly (see that batch's step-2 block) and shows the **same result**:
```
[Freeze] nothing was frozen:
[ue5_freeze] g_invokeMailbox symbol not found -- is UE5Dumper.dll injected?
--> getMemoryRecord(0).Active = true
```
So this is not one stray bail-out: **both** paths emit the untick and **neither** takes effect.
⇒ The `AA12`/`AA13` fix is **half-landed** — the honesty half (an accurate message instead of a
silent false success) works; the untick half does not.

📌 **Icon semantics confirmed by direct measurement, not memory:** `Active=true` displayed a **red ✗**
in the checkbox and `Active=false` displayed an **empty box**. This matches the maintainer's
2026-08-18 note and is worth having measured, because reading the ✗ as "failed" would have inverted
this entire finding.

*Not fixed — found during a verification pass.*


> ### ✅ REPRODUCED, and the LEADING HYPOTHESIS IS NOW MEASURED 2026-08-20 `[FREEZEUNTICK-CE-2026-08-20]`
>
> The block above proposed — as a *"leading hypothesis, explicitly NOT yet measured"* — that CE
> finalises activation after `[ENABLE]` and overwrites the in-script assignment, and that the fix is
> the **deferred** one-shot-timer untick this repo already uses for the momentary-action shape.
> **Both paths were exercised on the same record, in one CE session, and they disagree exactly as the
> hypothesis predicts:**
>
> | untick path | result, read from `getAddressList().getMemoryRecord(0).Active` |
> |---|---|
> | **in-`[ENABLE]`** (helper-missing bail-out) | `active=true` — the record stays ticked, nothing was applied |
> | **deferred one-shot timer** (the `[FREEZESTUCK]` abandonment, 3 failed rescans) | `active=false` — the record really unticks |
>
> The second row is `[FREEZESTUCK-CE-2026-08-20]`'s step 3, measured in full there. **So the defect
> is specific to the in-ENABLE assignment, the deferred mechanism demonstrably works in real CE, and
> the proposed fix is the right shape** — it is not a guess any more.
>
> Reproduction detail for the bail-out half, on a **fresh** CE table (so nothing carried over):
> ticking the record with the helper absent raises
> `[Freeze] ue5_freeze_helper.lua not found in this table. Setup: UE5DumpUI -> Tools -> Inject Freeze
> Helper into Current CE Table`, and immediately afterwards Lua reports `active=true`. The checkbox
> agreed (red ✗). Nothing was written to the game.
>
> ⚠ **Incidental, and it matters for anyone testing this**: the abandonment's `showMessage` is
> **modal** and swallows both the `Del` key and clicks on the Lua Engine's **Execute** button, so any
> step that has to act *during* the abandonment must be pre-armed (a CE Lua timer) rather than
> clicked. That is what defeated three attempts at `[FREEZESTUCK]` step 7.


> ### ⚠ SCOPE WIDENED 2026-08-20 — this is NOT Freeze-only `[UNTICK-INVOKE-2026-08-20]`
>
> Measured while running `Y10`'s CE half (`[Y10-Y13-CE-2026-08-20]`): the **baked-invoke** script has
> the same defect. With CE attached to a process that has no `UE5Dumper.dll`, ticking an
> `Invoke (baked): …` record correctly refuses — the contract check fires first, names
> `g_mailboxContract`, and **no `writeByte` runs** — and then leaves the record at
> **`active=true`** (read from CE's Lua Engine).
>
> So three scripts have now been measured in real CE and they agree:
>
> | script | untick mechanism | survives? |
> |---|---|---|
> | Freeze, helper-missing bail-out | in-`[ENABLE]` | ❌ `active=true` |
> | Baked invoke, contract-check bail-out | in-`[ENABLE]` | ❌ `active=true` |
> | Freeze, 3-failed-rescan abandonment | **deferred one-shot timer** | ✅ `active=false` |
>
> **Implication for the fix:** it does not belong in `FreezeScriptGenerator` alone. Every emitted
> script that can bail out of `[ENABLE]` needs the deferred form, so the change belongs in the shared
> `CeLuaHygiene` emitter — which is also what CLAUDE.md's own rule demands ("Hand-rolling any of
> these is how build 2743's three defects reached all seven copies of the mailbox wait at once").
> ⚠ **Whoever fixes this should grep for every in-`[ENABLE]` `memrec.Active = false` rather than
> fixing the two sites named here** — two were found by running two unrelated register rows, which is
> a poor way to establish a blast radius.


**FIXED 2026-08-21. The leading hypothesis was right in outcome but wrong in mechanism, and the
real one is worse: this was never two stray bail-outs — it was 32.**

**Mechanism, read out of CE's own source** (`Cheat Engine/memoryrecordunit.pas:2573`,
`TMemoryRecord.setActive`), which needed no minimal-record experiment after all:

```pascal
if state = fActive then exit;                  // (1) no-op when already in that state
if processingThread <> nil then exit;          // (2) no-op while processing
...
if autoassemble(script, ..., state, ...) then  // (3) OUR [ENABLE] BLOCK RUNS HERE
  fActive := state;                            // (4) and only NOW does it become true
```

While the script runs at **(3)**, `fActive` is still **`false`**. So `memrec.Active = false` hits
**(1)** — `state = fActive`, both false — and returns having changed nothing. Then **(4)** ticks the
row regardless.

⚠ **The filed hypothesis said CE "overwrites a value the script set".** It does not: the write never
lands at all. The distinction matters, because "overwritten" suggests racing the assignment (write
it later in the block, write it twice), and none of that could ever work — the assignment is a
**no-op**, not a loser. Only deferral past (4) works. Recording this because the wrong model
suggests wrong fixes.

**Scope, measured by classifying every emitted untick rather than by inference.** 43 sites emit
`memrec.Active = false`; a classifier tagged each by whether it sits inside an `OnTimer` callback or
a `[DISABLE]` block:

| kind | count | verdict |
|---|---|---|
| **immediate, inside `[ENABLE]`** | **32** | **broken — all fixed** |
| already deferred (`OnTimer`) | 4 | correct; untouched |
| inside `[DISABLE]` | 6 | nothing to untick; untouched |
| a comment | 1 | — |

So it was **every stateful toggle in the app** — Movement ×4, Fly ×2, GodMode ×2, SeeThrough ×2,
Foreground ×2, DebugCamera ×2, TimeDilation ×2, Freeze ×2, PointerQuery ×4, CeInject ×5,
Teleport ×2 — plus `CeLuaHygiene`'s own three shared emitters (`AppendBail`,
`AppendMailboxWait`'s `UntickAndReturn`, `AppendFailedEnable`).

⭐ **And the momentary shape was right for the wrong reason.** CLAUDE.md distinguishes *stateful
toggle* (untick-and-return) from *momentary action* (deferred timer) as two shapes that are "not
interchangeable". They are not — but it was never the *momentary* part that made the timer work,
only the **deferral**. The stateful bail-outs needed it exactly as much.

**The fix.** One shared emitter, `CeLuaHygiene.DeferredUntickLua(indent)`, and all 32 sites call it:

```lua
if memrec then local _u=createTimer(nil,false) _u.Interval=50 _u.OnTimer=function(x) x.destroy() memrec.Active = false end _u.Enabled=true end  -- deferred: CE sets Active AFTER this block, so an immediate untick is a no-op
```

By the time the timer fires, (4) has run: `state(false) ≠ fActive(true)` clears (1) and activation
is finished so (2) is clear. Emitted as **one line** deliberately — users read these inside CE and
this appears 32 times; the `local` is scoped by the surrounding `if`, so repeats cannot collide.
Spelled `memrec.Active = false` with spaces so the 11 pre-existing `CeMailboxBailoutTests`
assertions keep matching and stay meaningful.

**Pinned three ways, because text alone provably could not catch this:**
1. `EveryEnableUntick_IsDeferred_NotImmediate` — no `[ENABLE]` untick line may lack `createTimer`.
2. `EveryEnableBlock_HasAtLeastOneDeferredUntick` — guard the guard; without it a script that lost
   its untick entirely would pass (1) vacuously, which is this defect's own failure mode.
3. `EveryDeferredUntick_IsTheSharedEmittersText` — every emitted line must be byte-identical to the
   shared emitter's, per CLAUDE.md's "hand-rolling any of these is how build 2743's three defects
   reached all seven copies".

⚠ **The 11 existing "the bail-out unticks" assertions were all TRUE and all USELESS** — every one
passed throughout the two years the untick did nothing. Text assertions structurally cannot tell a
working untick from a no-op, because the difference lives in CE's lifecycle. That is why the fix
also ships a rig that RUNS it.

**`scripts/tests/untick_bailout_test.lua`** (new) models `setActive` from the source and executes
both shapes — **10/10, exit 0**:

* *immediate*: the script **did** attempt the untick (1 attempt) but it **changed nothing**
  (0 effective) and the record ends **ACTIVE** — the reported defect, reproduced offline;
* *deferred*: the record ends **unticked**, 1 effective change, timer self-destroyed at a real 50 ms
  interval;
* two model controls, so neither result can be an artefact: an **external** untick after activation
  **does** work (the model is not simply read-only, which would have made the first case pass for
  the wrong reason), and CE's `processingThread` guard **(2)** is reachable too.

Not in CI, matching `freeze_helper_test.lua`'s reasoning (no declared `lua` dependency; a step that
skips when its tool is missing is worse than a documented manual one). `UntickRigMatchesTheEmitter`
asserts the rig still contains the emitter's exact line **and** still contains the immediate shape,
so its defect-reproduction case cannot quietly become a second copy of the fix.

⭐ **Shown able to fail**: reverting one Protection site to the immediate form failed both
`EveryEnableUntick_IsDeferred_NotImmediate` and `EveryDeferredUntick_IsTheSharedEmittersText`, each
naming `Protection`.

**✅ VERIFIED IN CHEAT ENGINE 7.7, 2026-08-21 — the version the defect was observed on, not the
7.5 source the fix was reasoned from.**

Run as a **two-record A/B inside one CE session**, built and read entirely from CE's own Lua Engine
(so the verdict comes from `memrec.Active`, never from the checkbox icon — reading that ✗ backwards
is what would invert the whole result). Both records are `vtAutoAssembler`, attached to DumperTest,
identical except for the untick shape, and `B_DEFERRED` carries the **byte-identical** line
`CeLuaHygiene.DeferredUntickLua()` emits:

```
--- FREEZEUNTICK A/B, CE 7.7 ---
A_IMMEDIATE   Active=true      <- the defect, reproduced
B_DEFERRED    Active=false     <- the shipped fix, working
```

⭐ **The control is what makes this conclusive.** `A_IMMEDIATE` reproducing the defect in the same
table, same session and same process proves the rig can express the failure — so `B_DEFERRED`
coming back `false` is the fix working, not the experiment being too weak to fail. It also
confirms, on the real 7.7 binary, both things the fix was built on: CE's `setActive` really does
ignore an in-`[ENABLE]` untick, and a deferred one really does land.

Table cleared afterwards (`0 record(s) left`); CE and the game shut down.


---

#### ROUND 2, 2026-08-21 — the first sweep MISSED both generators this defect is NAMED after

⭐ **Read this one for the method lesson.** The original sweep classified call sites by reading each
generator's C# **top to bottom**, tracking whether the last emitted literal was `"[ENABLE]"` or
`"[DISABLE]"`. That is wrong, and the way it is wrong is not obvious: **a private helper method
defined below the `[DISABLE]` emission still emits into `[ENABLE]`** when the enable path calls it.

`FreezeScriptGenerator.AppendHelperLoader` and `BakedScriptGenerator.AppendHelperLoader` are exactly
that — both sit at the bottom of their files, both were read as "DISABLE, nothing to untick", and
both kept the immediate untick. So the sweep missed **the two generators this defect's own title
names**: *"BOTH the Freeze and the baked-INVOKE generators"*. Four more sites, now fixed — **36
total, not 32**.

⚠ And the guard added with round 1 could not have caught it: `MailboxScripts()` lists eleven
generators and **Freeze is not one of them**.

**What replaced the flawed method.** `EveryGeneratedEnableBlock_UnticksOnlyViaTheSharedDeferredEmitter`
generates a script from **every** generator that emits an `[ENABLE]` block and reads the **output** —
the only ground truth about which block a line lands in. Backed by
`TheEnableScriptList_CoversEveryGeneratorThatEmitsAnEnableBlock`, which globs `*ScriptGenerator.cs`
and fails if one of them is not mentioned in the test file at all, so the next generator cannot slip
past the way Freeze did.

⚠ **The detector needed teaching too.** A line-only rule ("an untick line must also say
`createTimer`") reported a FALSE POSITIVE on `BakedScriptGenerator`'s perfectly correct self-untick
timer, where the timer is created on one line and the untick sits inside the `OnTimer` body several
lines down. `ImmediateUnticksIn` now recognises both deferred shapes. A guard that cries wolf on
working code gets disabled, which would have cost more than the defect.

⚠ **One pre-existing test was pinning the DEFECT.**
`FreezeScriptGeneratorTests.Generate_HardFailure_StopsTimersUnticksAndReturns` asserted the literal
`if memrec then memrec.Active = false end` — it passed for as long as the untick did nothing. Now
asserts `CeLuaHygiene.DeferredUntickLua()`.

**END-TO-END IN CHEAT ENGINE 7.7, on the REAL generated script** — not the synthetic A/B above.
The Property Search **Freeze** button is gated on the AOBMaker plugin, which is offline here, so the
byte-identical script it copies was emitted through
`FreezeScriptGenerator.Generate(PropertySearchViewModel.BuildFreezeParams(...))` for the exact row
measured live minutes earlier: `DumperTestActor · TickCount · IntProperty · 0x6A8`, preview **64**.
5,495 chars, loaded into a `vtAutoAssembler` record from CE's Lua Engine and enabled:

1. it bailed out with the reported message, verbatim —
   `[Freeze] ue5_freeze_helper.lua not found in this table.`
2. `Freeze: DumperTestActor::TickCount = 9999  ->  Active=false` ✅ **(was `true`)**
3. and **nothing was applied**: re-running the search showed `TickCount` at **1497**, still climbing
   from the 64 it read before — not held at 9999.

⭐ Points 2 and 3 together are the whole of `AA12`/`AA13`: an accurate message, a record that does
not claim to be active, and a value that was genuinely left alone.

**A NEW defect fell out of the exhaustive guard — `[TRAINERUNTICK-2026-08-21]`, filed below.**

---

### ✅ FIXED + CE-VERIFIED 2026-08-21 `[TRAINERUNTICK-2026-08-21]` — the standalone trainer's stateful toggles never unticked on a bail-out

*Found by the exhaustive guard written for `[FREEZEUNTICK-2026-08-20]` — specifically by its
guard-the-guard clause, which asks whether each script even CONTAINS an untick to check. **LOW-MED**,
but it ships to end users as a `.CT`.*

**Measured, not inferred:** `StandaloneTrainerScriptGenerator.cs` emits **14** `showMessage(...)`
bail-outs and the string `memrec` appears in that file **zero** times
(`grep -c memrec` = 0). So every failure path — *"Config missing (no AOB / chain)"*, *"GWorld AOB
scan FAILED"*, *"Could not read RIP displacement"*, *"Enable Setup first"*, *"No protection bit was
resolved"*, *"Fly needs CE isKeyPressed"*, *"No saved position"* — shows a dialog, returns, and
**leaves the record ticked while nothing was applied**. That is precisely what CLAUDE.md's rule
forbids, and the Setup row is `AutoActivate: true`, so it is the first thing a user sees.

**Why it was NOT fixed in the same pass**, deliberately: the fix needs a decision this test cannot
make. Two of the sites (`TP Save position`, `TP Recall position`) have **no untick even on the
SUCCESS path**, so the question is whether a momentary trainer row is *meant* to stay ticked after a
good run. Guessing that wrong makes the trainer worse, and two speculative fixes were already
reverted today for shipping behaviour that measurement did not support.

⚠ **There is also a formatting hazard.** Ten of the fourteen are one-liners
(`if X then showMessage('…'); return end`), and `CeLuaHygiene.DeferredUntickLua()` ends in a `--`
comment — splicing it inline would comment out the rest of the line, including the `return end`.
A comment-free variant of the emitter is needed for those.

**The gap is kept visible rather than papered over**: the guard exempts `Trainer.*` with this tag in
the exemption comment, and `TheTrainerExemption_StillDescribesARealGap` fails the moment
`StandaloneTrainerScriptGenerator` mentions `memrec` — telling whoever fixes it to delete the
exemption, so a documented gap cannot decay into a forgotten one.


---

#### FIXED 2026-08-21 — ⚠ but FIRST, the filing above was WRONG about the scope

⭐ **Read this before trusting any `grep -c` in a finding, including one of mine.** The entry above
says the generator *"does not mention `memrec` even ONCE, so every failure path leaves the row
ticked"*, over **14** bail-outs. The `memrec` count was a **proxy used as the measurement**, and it
was wrong in the direction that would have made the fix touch working code — the very trap
`[CDOSCOPE]` was written up for two entries earlier.

`StandaloneTrainerScriptGenerator` already had its own `AppendUntick(desc)` helper, which unticks
via `getAddressList().getMemoryRecordByDescription(...)` and so never names `memrec` at all.
`BuildTpSave` and `BuildTpRecall` call it **before** their bail-outs (lines 380 / 407), so the timer
is armed whichever path returns — success or `Enable Setup first` alike. **Those five sites were
correct by design and are untouched.**

**The real defect was NINE sites**, all in the stateful toggles, which have no untick at all:
`BuildSetup` ×3 (config missing / AOB scan failed / RIP displacement), `BuildKnob`, `BuildJump`,
`BuildGodMode` ×2, `BuildFly` ×2. Those must stay ticked while the cheat is on, so they can only
untick on the paths that applied nothing — which is exactly what they never did.

**The fix** adds `CeLuaHygiene.DeferredUntickLua()` at each of the nine, rewriting the seven inline
one-liners as multi-line blocks.

⚠ **That rewrite is not cosmetic — the inline splice produces BROKEN LUA, and this was demonstrated
rather than asserted.** The emitter ends in a `--` comment, so
`if X then showMessage(...); <untick>  -- comment  return end` comments out the `return end`.
Doing exactly that to the Fly guard and parsing the output with the real `lua` interpreter gave:
```
FAIL 06.lua  block 1: 'end' expected (to close 'if' at line 11) near <eof>
```
— three scripts failing to parse. With the multi-line form, **all 18 `{$lua}` blocks across the 9
generated entries parse clean**.

**CE 7.7 A/B, on the real generated Setup script** (emitted with an empty config so it lands in the
`Config missing` bail-out), both records in one session, differing only by the three untick lines:

```
=== TRAINERUNTICK A/B, real generated Setup script, CE 7.7 ===
A_NO_UNTICK (pre-fix)   ->  Active=true
B_FIXED                 ->  Active=false
```

⭐ The control is what makes it mean something: `A_NO_UNTICK` reproduces the defect in the same
session and process, so `B_FIXED` is the fix working rather than the rig being unable to fail. Read
from CE's Lua Engine, never the checkbox icon.

**Guards.** `TheTrainerKeepsBothUntickShapes` pins 2 momentary + 9 stateful, and the exhaustive
`EveryDeferredUntick_IsTheSharedEmittersText` now runs over **every** generator — so a spliced
inline untick fails as an inequality, which is precisely the broken-Lua case. ⭐ Shown able to fail
three ways: removing one of the nine, reverting one guard to inline-without-untick, and
re-introducing the splice (which named the two Fly scripts).

⚠ **The detector had to learn a second untick MECHANISM** (`mr.Active = false` from
`getMemoryRecordByDescription`, not just `memrec.Active = false`) and a second deferred SHAPE
(`createTimer(50, function() … end)` with the callback inline, not just `_u.OnTimer = …`). Without
both it reported the trainer's correct TP rows as unguarded — the same false negative that produced
the wrong filing in the first place.

### ✅ FIXED 2026-08-21 `[TESTFLAKE-2026-08-21]` — it was NOT a flaky test. It was a PRODUCT race the test intermittently exposed

*Not a product defect — a test-suite flake, filed so the next person seeing a red run has prior art
instead of a mystery.*

**Seen ~3 times across ~15 full runs on 2026-08-21.** Named once:
`InterestingFunctionsViewModelTests.GameplayActions_ToggledTwiceWhileBusy_LeavesRowsAlone`. It
**passed twice in isolation** immediately afterwards, and eight consecutive full runs since have
been green (4590/4590).

⚠ **It is not caused by the day's changes** — it lives in Interesting Functions and touches nothing
in `LiveFieldValue`, the CE Lua emitters, or the NumericUpDown façades. The name itself
("ToggledTwiceWhileBusy") points at a timing/concurrency assertion, which is the usual shape.

⚠ **Two of the three sightings were lost** because the capture grepped the same command that re-ran
the suite, so the name never reached the log. If it fires again: run
`dotnet test … > /tmp/r.log 2>&1` and read `^failed ` out of the FILE — do not pipe a re-run.


---

#### ⭐ FIXED 2026-08-21 — and the filing was wrong about what it was

**I filed this as "a test-suite flake, not a product defect", on the grounds that the test passed in
isolation and touched nothing the day's changes had moved. Both observations were true and the
conclusion was wrong.** Reproducing it turned up a real, user-reachable race in
`InterestingFunctionsViewModel`.

**Reproducing it first.** The test starts `LoadCommand.ExecuteAsync(null)` and immediately toggles
`GameplayActions` twice, assuming the load is parked in the window between capturing
`_scoredWithGameplayActions` and comparing it back. Nothing enforced that: the load's tail resumes
on a pool thread and can slip **between the two toggles**. Forcing that interleaving —
toggle on, `await load`, toggle off — failed instantly and repeatably:
`Assert.Null() Failure: Value is not null`.

⭐ **Then the same repro was pushed one step further, and that is where the product defect
appeared.** Draining both reconciliation paths and checking the END state gave
`Assert.DoesNotContain() Failure: Filter matched in collection` — the pack's row still present with
the CheckBox reading off.

**The race.** Two `RescoreAsync` calls can be in flight at once — the setter starts one per toggle,
`LoadAsync` starts another to reconcile a toggle that arrived while it was busy. Each captures its
own mode, then suspends on `await Task.Run(() => ScoreEntries(...))`, then writes `_allRows`. The
write therefore went to **whichever SCORING finished last — completion order, not request order**.

⚠ **The resulting state is the worst shape available and it conceals itself.** `_allRows` scored
with the pack ON, `GameplayActions` OFF, and `_scoredWithGameplayActions` also OFF — because the
superseded run had set that field *before* its await. So the field whose entire job is to detect
"grid and CheckBox disagree" reads consistent, and **nothing will ever reconcile it**. That is
audit #5 `Z1`'s permanent-disagreement failure reached by a different route, and the user gets it
from a double-click on the CheckBox as a load lands.

**The fix** is a generation token: `RescoreAsync` bumps `_rescoreGeneration`, and after scoring
returns it publishes only if it is still the newest. `_scoredWithGameplayActions` moved to *after*
that check, so it is written together with the rows it describes rather than ahead of them —
without that, a superseded run still leaves the field claiming a mode the rows were never in.

**And the test was ALSO wrong**, independently: it raced the window instead of enforcing it. Both
it and its sibling `GameplayActions_ToggledWhileLoadIsBusy_IsNotSwallowed` (same latent race — it
simply never went red) now park the load with a `GatedEntryList`.

⚠ **The seam is worth understanding before touching these tests.** The window is bounded by the
capture and the comparison, with exactly one suspension between them —
`await Task.Run(() => ScoreEntries(...))` — and `ScoreEntries` reaches its entries by `foreach`.
Blocking that enumeration is the only hook that lands inside. The existing
`FakeAobMakerBridge.Gate` cannot: that probe is deliberately fire-and-forget and never holds the
load up at all. Gating the dump service cannot either: it parks *before* the capture, which is a
different and already-correct scenario, and it would silently invert the sibling's assertion.
`GatedEntryList` derives from `List<AllFunctionEntry>` and **re-implements**
`IEnumerable<AllFunctionEntry>`, because `AllFunctionsResult.Functions` is a concrete `List<T>`
whose `GetEnumerator()` is not virtual — re-naming the interface on a derived type re-maps it, so
no production type had to be widened for a test.

**Evidence.** ⭐ Shown able to fail: removing the generation guard makes
`ConcurrentRescores_SettleOnTheNewestMode_NotTheLastToFinish` — the repro, kept as a permanent
regression test — fail with the original `Filter matched in collection`. **Ten consecutive
full-suite runs** are green at 4,605 tests.

⚠ **What ten green runs do and do not prove.** At the observed ~1-in-8 rate, ten clean runs alone
would happen about a quarter of the time by luck, so they are corroboration, not proof. The load
is carried by the causal evidence: the exact interleaving was reproduced deterministically, the
mechanism read out of the code, and the fix shown to flip that reproduction.

### ✅ FIXED + LIVE-VERIFIED 2026-08-21 `[LWFILTERREVERT-2026-08-21]` — picking an autocomplete suggestion reverts to what was typed

**Reported by the maintainer with a screenshot** (`V6 / U8` step 1, second half).

In the Live Walker field-search `AutoCompleteBox`: type `re`, the dropdown offers **`RemoteRole`**,
click it. The box should read `RemoteRole`; it **reverts to `re`** (and the screenshot shows `re`
*selected/highlighted*, which is the signature of the control's inline auto-completion rewriting the
text rather than of a plain binding failure).

The box is wired the way CLAUDE.md's keyword-search rule requires —
`Text="{Binding SearchText, Mode=TwoWay}"`, `PlaceholderText` (not `Watermark`),
`FilterMode="Contains"`, `ItemsSource="{Binding SearchHistory}"`
([LiveWalkerPanel.axaml:478](ui/UE5DumpUI/Views/LiveWalkerPanel.axaml:478)) — so the wiring rule
alone does not explain it, and the fix must not be "add TwoWay", which is already there.

⚠ **The rule makes this wider than one box**: every keyword box in the app is required to behave
identically, so whatever is wrong here is likely wrong in all of them, and whichever sibling
*doesn't* revert is the strongest lead to the difference.

*Not fixed yet — cause not confirmed in source at time of filing.*



**FIXED 2026-08-21** (commit `71cf7e2b`). ⚠ **The entry's own warning was right: it was all 20
boxes, not one.**

**Cause.** Any `CollectionChanged` on an `AutoCompleteBox`'s `ItemsSource` makes Avalonia rebuild the
dropdown, which clears the inner `ListBox`, which drives `SelectedItem = null` **without** setting
the guard flag every other null-assignment site sets — and the unguarded path restores the text to
what the user typed. The 700 ms keyword-history debounce did a `RemoveAt` + `Insert` **even when the
list was already byte-identical**, so a mouse click on a suggestion raced a collection change that
undid it.

⭐ **The sibling that does NOT revert is what confirmed the mechanism**: the one keyword box bound to
a static `string[]` never raises `CollectionChanged` and never misbehaved.

**Fix**: one character in `SearchKeywordHistory` — `history[i].Length > k.Length` →
`>=` — so re-typing an existing keyword stops mutating the collection at all.

⚠ **A deliberate cost, worth knowing:** re-using a remembered keyword **no longer moves it to the
front**, so ordering is first-seen rather than LRU for entries that already exist. Preserving LRU
would mean not binding the live collection to the control at all, across 20 view models. Say so if
that trade is wrong.

**Tests**: `Remember_ReusingKeyword_DoesNotReorder_SoAnAutoCompletePickSurvives` replaces the old
`…MovesToFront`, and `Remember_Duplicate_RaisesNoCollectionChanged` counts **events**, not contents —
a contents-only assertion passes while the control is still being reset underneath.


**LIVE-VERIFIED 2026-08-21** on DumperTest, Live Walker / `CharacterMovementComponent`, using the
reported gesture: typed `GravityDirection`, then `MaxWalkSpeed` (to build history), then cleared and
typed `Max` — the dropdown offered **`MaxWalkSpeed`** — and clicked it.

**The box reads `MaxWalkSpeed`**, not `Max`, and the header moved **`10 matches` → `2 matches`**, so
the text and the filter both took. Before the fix the box reverted to what was typed.

### ✅ FIXED + LIVE-VERIFIED 2026-08-21 `[INVOKEINHERIT-2026-08-20]` — an INHERITED UFunction cannot be invoked on a derived instance

*Found while building a side-effect rig for `ST1` steps 4 and 6, when
`invoke_function(SetActorHiddenInGame)` on a `StaticMeshActor` came back **"Function not found"**
for a function the tool's own `list_all_functions` had just listed.*
**Reproducer: `tools/verify/invoke_inherited_function.py` — exits 1 while the defect stands.**

**Mechanism.** `Ubel::WalkFunctions(uclassAddr)` walks that UClass's **own** `UStruct::Children`
chain and never climbs `SuperStruct`. `UE5_FindFunctionByName` is just a filter over that list, so
it can only ever resolve a function the class **declares**.

**Measured on DumperTest, dist 3263 — three-way discriminator, both controls green:**

| # | case | function | instance | result |
|---|---|---|---|---|
| 1 | **inherited on a derived instance** | `SetActorHiddenInGame` | `StaticMeshActor` | ❌ `Function not found` |
| 2 | control — declared on the instance's own class | `SetActorHiddenInGame` | `ChaosDebugDrawActor` (class **is** `Actor`) | ✅ `ProcessEvent OK` |
| 3 | control — own function, same derived instance | `SetMobility` | `StaticMeshActor` | ✅ `ProcessEvent OK` |

2 rules out "the function is broken"; 3 rules out "that instance is broken". The only variable left
is inherited-vs-declared.

**Blast radius, counted rather than characterised.** `AActor` declares **140** functions, `APawn` 33,
`ACharacter` 48 — none reachable on any derived instance. Of 42 live non-CDO objects on this host,
**11 can invoke nothing at all** because their own class declares no function; `DumperTestActor`
is one of them, and `StaticMeshActor` can reach exactly **1 of 141**. On a real game the player pawn
is several levels below `AActor`, so `K2_TeleportTo` / `SetActorLocation` / `Jump` and ~221 others
are all unreachable by name.

**Three callers, and one of them is a shipped feature:**
* `Fern.cpp` `invoke_function` — and the handler reads only `class_name` / `func_name` /
  `instance_addr` / `params_hex` / `parms_size` / `direct_call`. **There is no `func_addr` input**,
  so nothing can bypass the resolver.
* `Mimic.cpp` `HandleFindFunction` (`CMD_FIND_FUNCTION`) — the CE Lua by-name lookup, same walker.
* ⚠ `Frieren.cpp` `UE5_SetDebugCamera` resolves `ToggleDebugCamera` off `UE5_GetObjectClass(cm)`,
  the **live** CheatManager's class. A game with a derived CheatManager (`BP_CheatManager_C` is the
  common case) logs `UE5_SetDebugCamera: ToggleDebugCamera UFunction not found` and returns -1 — the
  Debug Camera toggle failing with a message that reads as *the engine lacks the function*.

**NOT affected:** `CMD_INVOKE` when the caller already holds the `ufuncAddr`. The mailbox takes the
address directly and never re-resolves, which is why CE scripts with a baked address work, and why
this went unnoticed — the paths that are exercised most already have the pointer.

⚠ **Fix shape — do NOT simply make `WalkFunctions` climb the super chain.** That function is also
what LISTS a class's functions, and the UI attributes each function to its declaring class; making
it inherit would repeat all 140 `AActor` entries under every actor class and inflate
`list_all_functions` enormously. The change belongs in the **resolvers**: have
`UE5_FindFunctionByName` loop up `SuperStruct` (and `HandleFindFunction` with it), leaving listing
semantics untouched. Adding an optional `func_addr` input to `invoke_function` would independently
remove the dependency for callers that already have one.

*Not fixed — found during a verification pass. Effort S, risk low, but it needs the listing-vs-resolving
distinction above respected.*


**FIXED 2026-08-21, and verified on a running game rather than only in unit tests.**

**The fix respects the listing-vs-resolving distinction this entry insisted on.**
`Ubel::WalkFunctions` is untouched and still per-class, so the UI keeps attributing each function to
its declaring class. The climb went into a new header-inline
`Ubel::ResolveFunctionInChain(classAddr, name, listFuncs, readSuper, out, &levels)`, and the three
by-name resolvers now share it:

| call site | before | after |
|---|---|---|
| `Frieren.cpp` `UE5_FindFunctionByName` | own class only | chain (fixes the pipe `invoke_function` **and** `UE5_SetDebugCamera`) |
| `Mimic.cpp` `HandleFindFunction` | own class only | chain (fixes the CE Lua `CMD_FIND_FUNCTION`) |
| `Dunste.cpp` `FindFuncByName` | **already climbed**, privately | folded onto the shared one |

⭐ **`Dunste::FindFuncByName` held the only correct climb in the tree** — depth guard, self-loop
check and all. As with `ProcessPickerWindow` in `[GRIDRECYCLE]`, the fix was to *match the existing
sibling*, not to invent a rule; folding it in leaves one copy instead of three. ⚠ One deliberate
semantic change there: the shared resolver prefers an **exact** match anywhere in the chain over a
case-insensitive one, where Dunste took the first case-insensitive hit. That differs only when two
functions in one chain differ purely in case. Its `parmsSize` sanity gate stays with Dunste — it is
that caller's concern, not the resolver's.

**Why the traversal is in a header.** No test target compiles `Ubel.cpp`, `Frieren.cpp` or
`Mimic.cpp`, so a rule left in any of them cannot be pinned at all. Taking the per-class listing and
the super read as callables lets `dll_helpers_test` drive a synthetic class chain with zero memory
access. **1603 → 1626 assertions.**

⚠ **The ordering rule worth not breaking later:** both passes run over the **whole chain** before
the other begins. An exact match on a *base* class must beat a case-insensitive match on a *derived*
one; written as "try both at each level on the way up", the derived near-miss would win.
`Test_Ubel_ResolveFunctionInChain_ExactBeatsCaseInsensitive` exists solely to catch that rewrite.

⭐ **Shown able to fail.** Disabling just the climb (one `break;`) failed **exactly 7** assertions —
`inherited function resolves`, `...to the base class's copy`, `grandparent function resolves`,
`...to the grandparent's copy`, `the whole chain was searched`, `exact match on the base beat the
derived near-miss`, `cycle bounded by the depth guard` — while `own function still resolves` and
`override resolves` stayed **green**, which is right: neither needs the climb. A negative control
that reddened everything would have proved much less.

**LIVE VERIFICATION — DumperTest (dev), freshly built DLL injected.**
`tools/verify/invoke_inherited_function.py`, which "exits 1 while the defect stands", **exits 0**:

```
Actor declares 140 functions; StaticMeshActor declares 1 of its own (['SetMobility'])
  1  INHERITED on a DERIVED instance   SetActorHiddenInGame   -> ok=True  ProcessEvent OK
  2  control: DECLARED on own class    SetActorHiddenInGame   -> ok=True  ProcessEvent OK
  3  control: own function, same inst  SetMobility            -> ok=True  ProcessEvent OK
PASS: the inherited function resolved — the defect is fixed.
```

Row 1 was ❌ `Function not found` before; both controls were already green and stayed green, so the
one variable really is inherited-vs-declared. Four further live calls on the same instance:

| function | case | result |
|---|---|---|
| `SetActorHiddenInGame` | inherited, exact | ✅ `ProcessEvent OK` |
| `setactorhiddeningame` | inherited, **wrong case** | ✅ `ProcessEvent OK` — the CI fallback survived the chain change |
| `K2_TeleportTo` | inherited, a second one | ✅ `ProcessEvent OK` |
| `SetMobility` | own | ✅ `ProcessEvent OK` |
| `NoSuchFunctionAnywhere` | **absent** | ✅ `ok=False, Function not found` |

⭐ **That last row is the one that matters most** — a resolver that climbs must still *fail* on a
name that exists nowhere, or the fix would trade a false negative for a silent false positive.

⭐ **And listing did NOT inflate**, which was this entry's stated risk. Same session, after the fix:
`list_all_functions` returns **9,806** with `Actor` declaring **140**, `Pawn` **33**, `Character`
**48**, and `StaticMeshActor` still **1** — not 141. Had the climb gone into `WalkFunctions`,
`StaticMeshActor` would read 141 and the total would balloon.

**Not done, deliberately:** the optional `func_addr` input to `invoke_function`. The entry lists it
as an *independent* improvement, and the resolver fix closes the defect on its own; adding a pipe
input means a protocol-doc change for a path that now works by name. Filed here rather than done
silently.

### ✅ VERIFIED 2026-08-20 — audit L4 (D4b Mimic/Sein/Flamme): MB1 / MB2 / SE1 / FL1 / FL2 — all five rows PASS, all headless

*The pure decision rules of this batch are unit-pinned in `dll_helpers_test` and need NO live check:
**MB1**'s `ShouldRouteDirectInvoke` (10 assertions), **MB2**'s `CommandRequiresInit` (17), **FL1**'s
`ShouldPublishAtomicWrite` (11) — all five negative controls red exactly the predicted rows. **SE2 is
not listed below at all**: its trigger (`FindNextFileW` failing mid-enumeration) is not reproducible
on demand, the fix is structural, and the finding's own honest limit says so. What follows is only
what a running game can settle. ⚠ Note none of `Mimic.cpp` / `Sein.cpp` / `Flamme.cpp` is compiled by
any test target, so for those three files "green tests" means the HEADERS, never the handlers.*

⚠ **Rig trap found while verifying this batch, worth knowing before any future header-only change:**
`build/build.ninja` carries **no `msvc_deps_prefix`**, and this machine's MSVC emits `/showIncludes`
in Chinese, so ninja's English default prefix never matches and `ninja -t deps` reports **`#deps 0`**
for the test object. A header-only edit therefore yields `ninja: no work to do` and any check silently
measures the OLD binary — the first run of the negative-control rig reported `Fail: 0` for all four
breaks for exactly this reason. `build.ps1 -Clean` (what CI always passes) is unaffected; a bare
incremental `cmake --build` after editing only a `.h` is not.

> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | MB1 | on any game, generate an Invoke script for a **stateful, non-static** UFunction, enable it, FIRE once, then enable a **second** Invoke script for a `Native\|Static` Kismet helper and FIRE it — then go back and press FIRE on the FIRST form again | the first form's second FIRE still routes through GameThreadDispatch. `pipe-0.log` shows either no `INVOKE mailbox functionFlags=... is STALE` line, or that WARN naming the stale value and the re-read one | before the fix the second FIRE inherited the helper's `Native\|Static` from offset 0x024 and ran a stateful actor UFunction OFF the game thread; the WARN is the fix's own greppable evidence |
> | MB1 | same session: confirm a `Native\|Static` helper (e.g. a KismetMath call) still takes the fast path on an **idle** game (main menu) | `INVOKE -> static-native fast path` still logged; no -5 timeout | the re-read must not COST the fast path — a `ResolveFunctionInfo` that fails on some game would silently degrade every pure helper to a queued call that times out at the menu |
> | MB2 | with the DLL injected into a game whose GObjects scan **fails**, hit the CE `.CT` Keep-Foreground toggle | the toggle works (result 1/0), instead of the old `hook error -10` naming MinHook — a subsystem the command never reached | needs a genuinely failing scan; a healthy game only proves the exemption did not break the normal path (still worth doing as the regression check) |
> ### ✅ MB1 BOTH ROWS PASS 2026-08-20 `[MB1-HEADLESS-2026-08-20]` — headless, no CE; the ROUTE made observable
>
> `tools/verify/l4_mb1_stale_flags.py`. The row prescribes two generated Invoke forms in Cheat
> Engine and a FIRE-B-then-FIRE-A dance to make the staleness happen *by accident*. Writing the
> stale value straight into `functionFlags` (`+0x024`) is the same input with none of the ceremony,
> is exact — the poison `0x00002400` is chosen to be recognisable in the log — and needs no CE.
>
> ⭐ **The game thread is FROZEN throughout, which turns the route into an observable rather than a
> log line.** A WARN only proves the DLL *noticed*. With the UE game thread suspended the fast path
> still works (it never needed that thread) while `GameThreadDispatch` must time out at `-5`. So a
> stale `Native|Static` on a stateful function would return **0 in milliseconds**, and correct
> routing returns **-5 after 5 s**. The regression would be visible even if the WARN were deleted.
>
> | cell | target | `functionFlags` planted | result | fast-path lines | `is STALE` |
> |---|---|---|---|---|---|
> | **A** (row 2) | `KismetMathLibrary.Add_IntInt` (really `0x14022403`) | correct | **0**, `ReturnValue 7`, **5 ms** | **1** | 0 |
> | **B** (row 1) | `Actor.UserConstructionScript` (really `0x08020802`) | **`0x00002400` poison** | **-5**, 5006 ms | **0** | **1** |
> | **C** (control) | same as B | correct | **-5**, same | 0 | **0** |
>
> **B vs A isolates the ROUTE; B vs C isolates the WARN.** Neither pair alone would do it: A alone
> cannot show the poison was ignored, and B alone cannot show the `-5` came from routing rather than
> from the poison upsetting something else.
>
> The WARN is verbatim and names all three things the row asks for:
> `Mailbox: INVOKE mailbox functionFlags=0x00002400 is STALE — 'UserConstructionScript' really has
> 0x08020802; routing on the re-read value (MB1)`
>
> **Row 2's own concern is settled by A**: the re-read did **not** cost the fast path — 5 ms and a
> correct `7` with the game thread suspended, so a `ResolveFunctionInfo` failure is not silently
> degrading pure helpers into queued calls that time out at a menu.
>
> ⚠ **Two rig defects were found and fixed before this result was believed** — both recorded in
> [working-lessons.md](working-lessons.md) because both produce *confident wrong answers*:
> 1. a `strftime("…%H:%M:%S")` log watermark has **one-second** resolution, and these cells run
>    milliseconds apart, so cell A's fast-path line fell inside cell B's window and the rig printed
>    **`FAIL: the poisoned flags took the FAST PATH`** on a run whose own `result=-5` proved the call
>    had been queued. Replaced with before/after **counts**, which are exact at any timing.
> 2. `find_instances` without `exact_match` is a **name substring** match, so "the first live
>    instance of `Actor`" was a `UActorSequence`. The routing measurement survives that (the route is
>    decided from `ufuncAddr`'s flags alone, before any call), but a wrong-type invoke left **queued**
>    can drain later and run an `AActor` method against a non-actor. Re-run on `ChaosDebugDrawActor`,
>    whose reported `class` really is `Actor`.
>
> MB2's own row was already ✅ below; this closes MB1's two rows. FL1/FL2 remain.

> ### ✅ SE1 + MB2 PASS 2026-08-19 `[SEMB-HEADLESS-2026-08-19]` — both headless, no CE, no UI
>
> **SE1 — PASS, both halves.** `tools/verify/se1_log_reroute.py` takes a genuinely exclusive handle
> on `scan-0.log` via `CreateFileW` with `dwShareMode = 0` — **not** Python's `open()`, which shares
> the file and would let the DLL open it happily, passing the test vacuously. The rig proves the
> lock by attempting a second open that must fail with `ERROR_SHARING_VIOLATION`.
> * the announcement appears: `Logger: category 'scan' could not open 'scan-0.log' (errno=13) — its
>   lines are rerouted here for this run`
> * ⭐ and the half that actually matters — **597 `[SCAN*]` lines landed in `init-0.log`**, starting
>   at `FindAll: Starting global pointer scan...`. The announcement alone would not be the fix; the
>   LINES had to survive, and they did.
> * `init` was deliberately left writable: it is where the rerouted lines must land, so locking it
>   would have destroyed the evidence instead of producing it.
>
> **MB2 — PASS on the row's exact precondition.** Driven through the mailbox from Python
> (`tools/verify/mailbox_poke.py <pid> --cmd 12`), so no Cheat Engine was involved.
> * **Host with a genuinely failing scan** — `FindAll: Complete — GObjects=0x0 (not_found),
>   GNames=0x0 (not_found), GWorld=0x0 (not_found)`. The toggle sequence:
>   `GET → 0`, `SET 1 → 1`, `GET → 1`, `SET 0 → 0`. **No `hook error -10`**, i.e. the
>   `CommandRequiresInit` exemption holds and the command no longer blames MinHook, a subsystem it
>   never reached.
> * **Regression control on a second host** (Notepad++, `Partial init — GObjects=OK GNames=MISSING`):
>   identical `0 → 1 → 1 → 0`. So the exemption did not break the ordinary path either.
> * ⚠ Honest limit: `initState` was `READY` in both cases, because it reports whether the *pipe
>   server* started, not whether the scan succeeded. So this exercises "GObjects unresolved", which
>   is what the row asks for — not "init never finished", which no available host produces.
> * ⚠ Rig note: `pid_of` refuses to guess between same-named processes, and these rigs *run under
>   `python.exe`*, so the name "python" is permanently ambiguous with the rig's own interpreter.
>   `mailbox_poke.py` / `mailbox_addr.py` now accept a bare **PID**.

> | SE1 | before launching a game, open one of its `%LOCALAPPDATA%\UE5CEDumper\Logs\<Game>\*-0.log` files in a viewer that holds an exclusive-ish handle, then launch | `init-0.log` opens with `Logger: category '<name>' could not open ... its lines are rerouted here`, and that category's lines appear in `init-0.log` for the run | before, the category was dead for the process with **nothing logged anywhere** and its buffered early lines destroyed — a later grep read as "that code path never ran" |
> ### ✅ FL1 + FL2 PASS 2026-08-19 `[FLSWEEP-2026-08-19]` — headless, and the age guard is proven, not assumed
>
> Rig: `tools/verify/fl_staging_sweep.py`. It plants **two** files, because "the stale one is gone"
> would pass just as happily on a sweep that deletes EVERYTHING — which is the dangerous version of
> this code, given the UI writes its own `<file>.tmp.<pid>` concurrently.
>
> | planted | mtime | required | observed |
> |---|---|---|---|
> | `…json.tmp.99999` | 3 h ago | **deleted** | deleted ✅ |
> | `…json.tmp.88888` | now | **survives** | survived ✅ |
>
> Log line exactly as specified: `HintCache: removed 1 abandoned staging file(s) older than 1h`.
> FL1's production negative control also passes — `HintCache: Saved results for
> PE=67F515A70001A000 (python.exe, scan #2)` with **0** `staged write is incomplete` lines — and the
> real cache still parses with all **33** entries. Suffixes 99999/88888 are checked against the live
> PID list before planting so the plant cannot collide with a real staging write.
>
> ⚠ **THREE RIG TRAPS, each of which produced a FALSE FAIL of a working fix before being found.**
> 1. **The sweep is once-per-process.** `SweepOrphanTemps` holds
>    `static std::atomic<bool> s_swept` (`Flamme.cpp:136`), so in an already-injected game it has
>    *already run* — before you planted anything. It needs a **fresh process**.
> 2. **`trigger_scan` on an already-scanned process re-saves nothing.** Measured: after
>    `trigger_scan` the log gained only `FindGameEngine` lines and the last
>    `HintCache: Saved results` was still the injection-time one. So both the sweep line *and* the
>    save line are legitimately absent and the run reads as a total failure.
> 3. **`scan-0.log` is a SLOT NAME, not a file identity.** Each process start archives the previous
>    run, so a byte offset captured before the launch indexes into a different, shorter file and
>    discards the lines you are looking for. Read the whole fresh file.

> | FL1/FL2 | plant a stale `UE5CEDumper.<Machine>.json.tmp.99999` (mtime > 1 h old) in `%LOCALAPPDATA%\UE5CEDumper\`, then run any scan | after the scan the planted file is gone and `scan-0.log` has `removed 1 abandoned staging file(s) older than 1h`; the real cache is intact and a **fresh** temp from a live write is never touched | the age guard is what makes the sweep safe against the UI writing its own `<file>.tmp.<pid>` concurrently |
> | FL1 | ordinary regression: run two scans of the same game back to back | `HintCache: Saved results ... scan #2` and the cache file parses; **no** `staged write is incomplete` line | the refuse-on-failure gate must not refuse a legitimate write — this is the negative control for the production path, since the unit test only covers the predicate |

### ✅ VERIFIED 2026-08-21 — audit L3 (T1b): AD10 / AD12 / AD13 / AD15 / AD16 / AD18 — all three steps settled

*Most of L3 needs NO live check and is already machine-enforced offline: **AD12/AD13/AD15/AD16**'s
corrected geometry is now asserted by a compile-time `static_assert` AND by
`extract_patterns.py --check`, both negative-controlled 6-for-6 (**AD17**); **AD7/AD22** are pinned by
`check_derived_counts.py`; **AD20/AD21** were already fixed and are covered by `utf8_helpers_test` /
`dll_helpers_test`; **AD11/AD14** are proofs from the table's own text. What is listed below is only
what a running game can settle that a checker cannot.*

| # | 做什麼 | 預期 |

> ### ✅ STEP 2 PASSES 2026-08-21 `[L3-STEP2-CE-2026-08-21]` — the AOB really is in the pushed CE record
>
> The block above correctly refused to call this done off the **clipboard** export, which carries no
> AOB by design. This is the other route: **System tab → GWorld card → `SYM`**, with CE + the
> AOBMaker plugin connected. The UI reported
> `Registered CE symbol 'gworld_addr' — it re-scans on enable, so it survives a game restart.`
>
> **Checked in CE itself, not from the UI's word.** Reading the pushed record back with
> `getAddressList().getMemoryRecord(0).Script` (2,063 chars) shows a genuine AOB-scan export:
> ```lua
> local AOBs = {
>   {name='GWorld → gworld_addr',
>    aob='48 8B 1D ?? ?? ?? ?? 48 85 ?? 74 ?? 41 B0 01 33 ?? ?? 8B ?? E8',
>    pos=3, aoblen=7, symbol='gworld_addr'},
> }
> …
> local aob_addr_str = AOBScanModuleUE(module_name, entry.aob)
> …  registerSymbol(entry.symbol, final_addr)
> ```
> The triple matches what `[V11-SYM-2026-08-20]`'s log line reported (`pos=3, len=7`), so the AOB the
> UI *says* it sent is the AOB that *arrived*. **"Unchanged from before" — PASS.**
>
> ⇒ **With this, all of L3 is settled**: step 1's condition is never met (`[AD-PATTERNS-2026-08-20]`,
> swept), step 3's has never fired (437 files, 0 hits), and step 2 now passes in CE.

### ✅ ALL BUT THE MAINTAINER STEP DONE 2026-08-21 — audit L6 (U3 MainWindow VM): X5 / X6 / X7 / X8 / X10 / X11 / X12

> **Verified: X5 (both rows) · X6 · X7 · **X8** · X10 · X11.** X8 closed 2026-08-21 in a real CE
> session — `[L6-X8-2026-08-21]` below. Remaining: **X12** only, a maintainer step (CE installed
> under `%ProgramFiles%`, app run non-elevated), which no unattended session can stage.

*The pure logic is unit-tested: **X4** (`DumpCompletionFormatter` — floating size + honest zero-class
line + the `DumpResult` count round-trip), **X7** (`GameThreadStalledLevel` — the stuck-ON resume
case, dedup, reset), **X9** (`CompetingHostBanner` — self-exclusion by PID and by module name, the
two-instances control), **X12** (`FileWriteFault.IsPlacementDenied` classifier), plus **X5** at the
VM level for ValueSearch (both sessions forgotten with NO End pipe call) and Console (rows dropped).
X4 and X9 are fully settled by tests; the rows below are what only a running game / real CE can prove.*

> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | X5 | connect to game A, populate several panels (Instance Finder, Property Search, Live Walker, Value Search, Interesting Funcs), disconnect, then connect to a **DIFFERENT** game B | every panel is empty on reconnect — no rows, no addresses, no jump offers from game A; Live Walker's per-game **bookmarks survive** | before the fix only Teleport/DumpExplorer/LiveFuncs reset; the other ~13 kept stale rows offering jumps to dead addresses — bindings/timers only observable live |
> ### ✅ X5 (panel reset half) PASS 2026-08-20 `[L6-X5-2026-08-20]` — A→B, five panels, plus the bookmark half
>
> Game **A** = DumperTest **Development** (25,179 objects), game **B** = DumperTest **Shipping**
> (24,445 objects). Different builds, different module names, different PE hashes and a different
> object count, so a surviving row from A is unmistakable. A was **killed** before B was injected —
> two injected hosts at once is its own correctness bug (`working-lessons` §3.9), and the injector's
> ambiguity guard caught exactly that when a mangled `taskkill` left A alive.
>
> All five panels were populated on A first — otherwise "empty afterwards" proves nothing:
>
> | panel | on A | on B after reconnect |
> |---|---|---|
> | Instances | `Found 1 instances`, `Default__MockWorldMetricA` @ `0x23DD8498E90` | **grid empty**, no count line |
> | Properties | `Found 2 properties in 3,942 classes`, incl. `DumperTestActor.Health` @ `0x694` | **grid empty**, no count line |
> | Value Search | `First Scan: 836 candidates in 116 ms` | **grid empty**, and **Next Scan / New Scan are disabled** — the *session* is forgotten, not merely the rows hidden |
> | Interesting Funcs | full scored table (ClientCheatFly, CheatManager.God, …) | **empty**, back to "Click Load to scan all UFunctions" |
> | Live Walker | `GWorld → UWorld ThirdPersonMap`, ~20 rows with live addresses | **empty**, breadcrumbs gone, logo shown |
>
> The Object Tree also reloaded to B's own 24,438 named objects. ℹ️ The *query text* ("Health",
> "MockWorldMetricA") stays in the input boxes. That is input state, not a stale row — no address,
> no count and no jump offer from A survived anywhere.
>
> **Bookmark half — PASS, and per-game separation is positively demonstrated.** A bookmark was saved
> on A (★ → slot 1) before the switch, giving `Bookmarks\bookmarks.6A7EA60310F17000.json` with
> `slots [(0, "ThirdPersonMap")]`. After switching to B and walking GWorld there, **B's bookmark bar
> shows all eight slots empty** while A's file is still on disk unchanged. So the reset clears the
> *view* without touching the *per-game store*, which is the distinction the row is drawing —
> checking only that A's file survived would not have ruled out B displaying A's slots.
>
>  ℹ️ The **second X5 row** is now COMPLETE. Its *auto-refresh* clause — "both loops stop
> immediately on disconnect (no re-walk log spam against the dead pipe)" — was measured under
> `[AUTOREFRESH-LIVE2-2026-08-20]`: Auto ON and ticking at 10.0 s, game killed, **0 walk attempts
> and 5 log lines total** in the next 20 s, nothing for 121 s.
>
> ### ✅ THE AUTO-SNAPSHOT CLAUSE + CORPUS PRESERVATION 2026-08-20 `[L6-X5-AUTOSNAP-2026-08-20]`
>
> DumperTest + the real UI, Snapshot tab. ⚠ **The loop was made to actually TICK first**, because
> the shipped interval is 900 s and "no capture spam in the next 75 s" would otherwise be satisfied
> by a loop that was never going to fire — the same vacuity trap as elsewhere in this batch. Interval
> set to the **60 s minimum**, Auto snapshot ON, and the UI confirmed a real capture before anything
> was killed: `Auto: next snapshot in 50s · captured 1`, a new `Snapshot 2026-08-20 18:18:21`
> (644 objects / 12,155 fields), `Used: 2.7 MB → 5.5 MB`, and DumperTest's own db grown
> **2,867,200 → 5,726,208 bytes** on disk.
>
> **Then the game was killed. Over the following 75 s** (more than one interval, so a tick was due):
>
> | signal | result |
> |---|---|
> | new `capture` / `Capture` log lines | **0** |
> | new `snapshot` / `Snapshot` log lines | **0** |
> | new `re-walk` lines | **0** |
> | `view-0.log` / `init-0.log` growth | **0 bytes** |
> | `pipe-0.log` growth | **+360 bytes** — the disconnect notice, nothing else |
> | snapshot corpus | **29 files → 29 files, 0 lost, 0 changed** (SHA-256 per file) |
>
> The UI agrees with the logs: the **Auto snapshot toggle returned to `Off` by itself**, the panel
> says *"Auto stopped: capture failed (disconnected?)"*, and both saved snapshots are still listed.
> ⭐ The countdown label was checked twice 15 s apart and is **frozen at `13s`** — the timer really
> stopped rather than continuing to run against a dead pipe.
> ⚠ Minor cosmetic leftover, not filed: that frozen "Auto: next snapshot in 13s" line stays on screen
> beside a toggle reading `Off` on a disconnected app, which reads as though a capture were pending.
> ℹ️ The run leaves one extra DumperTest snapshot in the corpus (the `18:18:21` capture above); it is
> a legitimate sample capture and `KeepRecent`/10 will age it out.


> | X5 | before disconnecting, start Live Walker **auto-refresh** and (experimental) an **auto-snapshot** loop, then disconnect. ⛔ **Needs a build carrying `[AUTOREFRESH-2026-08-19]`** — on `dist` 1.0.0.3262 and earlier the countdown freezes at `0s` and auto-refresh issues nothing, so "start auto-refresh" cannot be satisfied and a green result would only mean *a loop that never ran did not run*. The auto-snapshot half is unaffected and can be run alone | both loops stop immediately on disconnect (no "re-walk"/"capture" log spam against the dead pipe); the snapshot **corpus is preserved** | the timer/loop teardown and corpus-preservation are not unit-testable |
> | X6 | start a **Dump All** (or Full SDK / USMAP) on a large game, then kill the game / disconnect mid-export | the export aborts promptly with "… cancelled (disconnected)" instead of hanging on dead-pipe round-trips; no truncated file at the chosen name | the ct now threads from a connection-linked CTS; before, `ct` was `default` and every service ct-check was dead code |
> | X7 | pause the game thread **during a long bulk-lane scan** (so only the bulk lane observed the pause), let the scan finish, then resume and browse via Live Walker (interactive lane) | the "game thread paused" banner **clears** on resume; before the fix it stuck ON until a bulk command ran | the pure latch is unit-tested, but the PipeClient per-response feed + banner is end-to-end |
> ### ✅ X7 PASS 2026-08-20 `[L6-X7-2026-08-20]` — bulk lane saw the pause, the INTERACTIVE lane cleared it
>
> ⚠ **Two preconditions this row does not state, either of which makes it silently unrunnable.**
> * `Stark::IsGameThreadResponsive` opens with `if (!s_hookActive) return true;` — with **no PE hook
>   installed the game can never be reported stalled**, so the banner cannot appear and the row
>   passes vacuously. The hook was installed first via Teleport → **Get POV** (an `invoke_function`),
>   confirmed by it returning a real pose (`Location 500…`, `FOV 90`).
> * Freeze the **thread**, not the process. A whole-process suspend stops Fern too, so nothing
>   answers and no envelope carries the flag. `tools/verify/suspend.py suspend-tid` on the UE game
>   thread (`tid 38780`, the main thread) leaves the pipe answering while ProcessEvent dispatch is
>   frozen — which is the state the row describes.
>
> | # | action | result |
> |---|---|---|
> | 1 | suspend `tid 38780` (threshold `kStallThresholdMs` = **500 ms**) | — |
> | 2 | **bulk** lane command — Instances `find_instances` | the banner appears: *"⏸ Game thread paused — the game isn't ticking…"*. The scan itself **still returned** `Found 1 instances`, exactly as the banner promises ("memory scans still work") |
> | 3 | resume `tid 38780` | banner still ON — correct, nothing has observed a fresh envelope yet |
> | 4 | **interactive** lane only — drill a `→` in Live Walker | **banner clears** |
>
> ⭐ **Step 4 is the whole row, and it was verified on the wire rather than by eye.** From the resume
> to the clear the UI's pipe log carried exactly two commands — **`walk_instance`** and
> **`walk_functions`**, both interactive; **zero** of the 33 `BulkCommands` ran. That is precisely the
> stuck case: the bulk lane observed `true`, went idle, and never fired its own `true→false` edge.
> One latch shared by both lanes is what clears it. Had a bulk command slipped in, the old two-latch
> code would have cleared the banner too and the run would have proven nothing — so the lane audit
> is not a nicety here, it is the evidence.
>
> ℹ️ Incidental: the banner is a **layout row**, so while it is up every tab shifts down ~22 px. A
> click scripted against banner-less coordinates lands on the wrong tab and looks like a dead button.
> | X8 | on the **Console** tab, with CE + AOBMaker **closed**, click a baked-exec / Debug-Camera "to CE" action → then **open** CE with the AOBMaker plugin and click again (no tab switch) | the second click now sends to CE (was "AOBMaker not connected" from the stale cached flag) | the path now calls `CheckAvailabilityAsync` first; needs a real CE toggled between clicks |
> | X10 | on Teleport, change the **World / Player time-dilation** sliders, wait >1 s, close the app, relaunch | the slider values are restored (they now schedule a save) | before, only OTHER Teleport options triggered a save, so a time-dilation-only change was lost |
> ### ✅ X10 PASS 2026-08-20 `[L6-X10-2026-08-20]` — and the restore is proven to come from DISK
>
> ⚠ **The obvious way to run this row cannot prove anything.** `UiOptionsSettings` says it plainly:
> *"the live DLL state, read back on connect, wins when a dilation is held"*. Set the sliders, restart
> the UI, reconnect to the still-running game — and the values come back **from the DLL**, which is
> exactly the source the row is not asking about. Both sources agree, so a pass is indistinguishable
> from the persistence being broken. **The game must be dead for this row to mean anything.**
>
> 1. Teleport → Time Dilation, on a connected DumperTest: World **2×**, Player **½×** (via the
>    presets, which also apply). Card read `State: ON`, `Current: 2× (held; natural 1×)` /
>    `Current: 0.5× (held; natural 1×)`, and **`Combined player speed: 1× (world 2 * pawn 0.5)`** —
>    the dual-lane multiply is right.
> 2. Within seconds `ui-options.json` carried **`teleport.worldTimeDilation = 2`** and
>    **`teleport.pawnTimeDilation = 0.5`**. This is the fix itself: a dilation-only change now
>    schedules a save.
> 3. **Killed the game, then closed the UI**, and confirmed the two values were still on disk with
>    both processes gone.
> 4. Relaunched `dist/UE5DumpUI.exe` and left it **disconnected**. The card reads `State: Unknown`
>    (nothing held, no DLL to ask) and the sliders show **200 %** and **50 %**, with
>    `Combined player speed: 1× (world 2 * pawn 0.5)` recomputed from the restored pair.
>
> With no game and no DLL in the picture, disk is the only place those numbers could have come from.
>
> 🔎 **Note for the next session:** the sliders are deliberately left at **200 % / 50 %** — that is
> this row's evidence sitting in `ui-options.json`, not a stray setting. Nothing is *held* on any
> game (`State: Unknown`); one click on either **Reset** returns them to 1×.
>
> ℹ️ Two keys worth knowing before grepping: `ui-options.json` is **nested by section**, so these live
> under `teleport`, not at the root, and they are **camelCase on the wire** (`worldTimeDilation`)
> while the C# properties are `WorldTimeDilation`. A root-level flat lookup returns "absent" and
> reads exactly like the save never happening.
> | X11 | start a **Dump All** and abort it (disconnect / cancel) mid-stream | there is **no** truncated `.jsonl` at the chosen name — only a `<name>.partial` (or nothing); a completed dump appears atomically at the final name | temp-then-rename is only observable against a real abort |
> ### ✅ X6 + X11 PASS 2026-08-20 `[L6-X6-X11-2026-08-20]` — both halves, on DumperTest / dist 3263

> ### ✅ X8 PASSES 2026-08-21 `[L6-X8-2026-08-21]` — the stale flag is still ON SCREEN at the moment the send succeeds
>
> The defect: a "to CE" action trusted the cached `IsAvailable`, so opening CE after a failed click
> did not help — the next click still said *"AOBMaker not connected"*. The fix makes the action path
> call `CheckAvailabilityAsync` itself. Run exactly as the row specifies, on the **Console** tab,
> **without switching tabs** between the two clicks (a tab switch would re-probe by another route and
> prove nothing).
>
> Subject: `AISystem::AIIgnorePlayers` **AA(B)**, chosen because it takes **0 params**, so the click
> is the whole action. DumperTest / dist 3263, 87 exec commands discovered.
>
> | | CE state | toolbar after the click |
> |---|---|---|
> | click 1 | **closed** | `AOBMaker not connected — script copied as CE XM…` |
> | *(CE opened; no tab switch, no ⟳)* | | |
> | click 2 | **open, plugin loaded** | **`AA Script created in CE: AIIgnorePlayers`** |
>
> ⭐ **The sharpest evidence is what the badge said while it worked.** Immediately before click 2 the
> toolbar badge still read **`AOBMaker Offline`** — the cached flag was *visibly still stale*, because
> nothing had refreshed it — and the click succeeded anyway. That is the fix demonstrated rather than
> inferred: the action re-probed on its own instead of believing the flag next to it.
>
> **The log times it to the millisecond**, and shows no other probe in between:
> ```
> 07:27:59.025 [DBUG] AOBMaker bridge: no server ... (Cheat Engine not running…)   <- click 1
> 07:29:19.171 [INFO] AOBMaker CE Plugin bridge: available                          <- click 2's OWN probe
> 07:29:19.205 [INFO] AOBMaker: created AA script 'exec (baked, no args): AISystem::AIIgnorePlayers'
> ```
> 34 ms between the probe and the send, and **nothing at all between 07:27:59 and 07:29:19** — so the
> `available` line cannot be a background refresh or a tab activation; it belongs to the click.
>
> **Four independent detectors agree**: the toolbar text, the log pair above, the still-`Offline`
> badge, and the record itself — `exec (baked, no args): AISystem::AIIgnorePlayers` visible in CE's
> address list afterwards.
>
> ℹ️ Click 1's fallback was verified rather than assumed: the clipboard was seeded with a marker
> first, and after the click held real CE XML (`<VariableType>Auto Assembler Script</VariableType>`,
> description `exec (baked, no args): AISystem::AIIgnorePlayers`). So "not connected" really did take
> the clipboard branch instead of doing nothing.
>
> ⚠ **Incidental, and it corroborates `[FREEZEUNTICK-2026-08-20]` from a third generator.** That
> clipboard XML is the baked-invoke script, and reading it shows the split plainly: the **success**
> path unticks via a deferred `createTimer(...)` → `memrec.Active = false`, while the **bail-out**
> path (`if not tf then … if memrec then memrec.Active = false end; return`) uses the in-`[ENABLE]`
> form that is measured not to survive. Same file, both shapes, a few lines apart.

>
> Same host, two runs: one **aborted** mid-stream and one allowed to **complete**. Both halves are
> needed — the abort alone cannot show that a finished dump publishes atomically, and the completion
> alone cannot show that an abort publishes nothing.
>
> **X6 — the abort is prompt, and it is reported.** Shared with `AC10` above: `taskkill /F` on the
> host with the `.partial` at **589,824 bytes and growing**.
> * `pipe-0.log` `ReadLine returned null (disconnected)` at **11:10:09.745**
> * `view-0.log` `DumpAll export cancelled` at **11:10:09.752**
> * → **7 ms** from dead pipe to abort. That number is the row: before the fix `ct` was `default`,
>   every service ct-check was dead code, and the export would have kept issuing per-class round
>   trips into a dead pipe. It did not hang, and the UI said **"Dump cancelled (disconnected)"**.
>
> **X11 — nothing at the final name on abort; the whole file at once on success.**
> * *abort run*: the `.jsonl` **never existed**, and the `.partial` was deleted.
> * *completion run*: polled at 50 ms throughout —
> ```
> t=13.28  partial 0 bytes            final -
> t=14.67  partial 3,145,728          final -
> t=17.14  partial 10,223,616         final -
> t=17.20  partial -                  final 10,484,429    <- one step, already full size
> ```
> The final name is **never observed at a partial size**: it goes from absent to complete inside a
> single 50 ms poll, which is the rename. The published file ends with its trailing
> `{"kind":"summary","classes_emitted":3942,…,"objects_scanned":25172}` line, and `view-0.log` agrees
> — `DumpAll exported to … (10484429 bytes, 3942 classes, 0 errors)`.
>
> ⚙ Operational note for whoever drives the rest of this table: **a game window steals focus back**,
> so a computer-use click on the UI behind it only re-activates the window and the button never
> fires — silently, with no error. Two Connect clicks were swallowed that way before
> `tools/verify/front_window.py front UE5DumpUI` was run first. The tell is the UI's `pipe-0.log`
> showing **no connect attempt at all**.
> | X12 | (maintainer) install CE under **%ProgramFiles%** (write needs elevation), run the app **non-elevated**, click **Install CE autorun** | it falls back to the manual save dialog ("… not writable — choose where to place it…") instead of failing | the denied-write branch needs a real non-writable CE folder |

### ✅ VERIFIED 2026-08-20 — audit L7 (T1d UI Services): AC3 / AC6 / AC10 / AC11 / AC12 — every row settled (AC11 step 2 found `[STAGELOCK]`)

*Ten findings closed (AC3–AC12); **five need nothing live**. AC4/AC5 (corrupt-cache quarantine) and
AC6's sweep policy are unit-tested end to end against a real temp folder, and AC7/AC8/AC9 are
comment-vs-code corrections in `ClassLocationScorer` with **no scoring change for any game** — AC9's
deleted `UCheatManager` row was strictly subsumed by the `CheatManager` row beneath it, proven by a
negative control that turns exactly one assertion red. The rows below are the parts a test cannot
reach: a real CE, a real Steam install, a real game dying mid-write, and a real game folder.*

> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | AC3 | with CE **closed**, use any "to CE" action (Teleport → AOBMaker push, Console → Debug Camera). Then open CE **without** the AOBMaker plugin and repeat. Then load the plugin and repeat. `rg "AOBMaker bridge" %LOCALAPPDATA%\UE5CEDumper\Logs\*\init-0.log` | three DIFFERENT lines: "no server on \\\\.\\pipe\\AOBMakerCEBridge within 2000 ms" (twice, Debug) then a success — and if CE is up but the pipe refuses, a **Warn** naming the exception type | before the fix the bare `catch` logged nothing at all, so all seven call sites produced the same blank "AOBMaker not connected" |
> | AC6 | plant `%LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.<MACHINE>.json.tmp.99999`, backdate its mtime >1 h, plant a second one with a **fresh** mtime, then launch the UI and connect to a game (any scan records) | the backdated one is gone, the fresh one **survives**, and `init-0.log` says "removed 1 abandoned staging file(s) older than 1 h" | the sweep is unit-tested, but the cross-process pairing with the DLL's identically-named `<file>.tmp.<pid>` is only observable with a game actually writing the same cache |
> | AC10 | start a long operation over the pipe (Dump All, or a big Value Search), then **kill the game process** mid-stream | the UI reports a **failure** ("disconnected"), not a silent cancel; a partial export is not finalised as complete | the write-vs-death race is a TOCTOU the classifier's unit test cannot drive; only a real kill hits it |
> ### ✅ AC10 PASS 2026-08-20 — killed 590 KB into a Dump All, reported as a failure, nothing published
>
> Dump All Metadata (.jsonl) over a connected DumperTest (25,179 objects), then `taskkill /F` on the
> host **while bytes were still moving**.
>
> ⚠ **A fixed sleep cannot stage this row and will pass without testing anything.** Fire early and
> the dump has not begun, so the disconnect is an ordinary idle one; fire late and it already
> published. Both look identical in the UI. `tools/verify/ac10_kill_midstream.py` triggers off the
> `.partial` instead — the dump streams to `<name>.jsonl.partial` and is renamed only after the
> trailing summary line, so a *growing* `.partial` is direct evidence of an in-flight stream.
>
> ```
> MID-STREAM: DumperTest-dump-20260820-110923.jsonl.partial is 589824 bytes -- killing DumperTest.exe NOW
> taskkill rc=0   (PID 6340 terminated)
> 3 s after the kill:  .partial still present : False      FINAL name published : False
> ```
>
> | assertion | result |
> |---|---|
> | the UI reports a **failure**, not a silent cancel | status → **"Dump cancelled (disconnected)"**; `view-0.log` `DumpAll export cancelled` |
> | the disconnect is detected, not hung | `pipe-0.log` `ReadLine returned null (disconnected)` → `Pipe disconnected`, 11:10:09.745 |
> | a partial export is **not** finalised as complete | the `.jsonl` **never existed**; the `.partial` was deleted (`TryDeletePartial`) |
> | the UI stays usable | reverted to `Connect`, Object Tree cleared, no error dialog, no hang |
>
> The temp-then-rename design (X11) is what makes the last row true by construction, and the
> `CreateLinkedTokenSource(_connectionCts.Token)` (X6) is what turns the dead pipe into the
> `OperationCanceledException` that produces the "(disconnected)" wording rather than a generic
> "Dump failed" — both were exercised on this path, 590 KB in.
> | AC11 | on an installed game: **Deploy** a proxy to a clean Binaries folder, then **Deploy again** over it, then **Undeploy**. Check the folder for any `*.ue5dump-stage` leftover | all three succeed exactly as before; no `.ue5dump-stage` file is ever left behind; the grid never shows "Other proxy" for a DLL we just wrote | staging changed the publish from a copy to a copy+rename — the first-time-deploy path and the locked-target path are the two that must not regress |
> ### ✅ AC11 step 1 PASS 2026-08-20 — deploy / re-deploy / undeploy leave nothing behind
>
> Driven through the real panel on **Light Maze** (`D:\SteamLibrary\…\LightMaze\Binaries\Win64`),
> picked because it was one of seven installed UE titles with **no** proxy of ours — so the
> first-time-deploy arm of `File.Move(overwrite: true)` is the one actually exercised. The folder was
> hashed to a 4-file baseline first.
>
> | # | action | grid | folder |
> |---|---|---|---|
> | 1 | Deploy | `NotDeployed` → **`DeployedCurrent`**, "Deployed: 1 success, 0 failed" | `dxgi.dll` 2,876,928 B appears; **no** `.ue5dump-stage` |
> | 2 | Deploy again, **Force Overwrite** | stays `DeployedCurrent`, "1 success, 0 failed" | same size, **no** `.ue5dump-stage` |
> | 3 | Undeploy | → **`NotDeployed`**, "Removed: 1 success, 0 failed" | **byte-identical to the baseline**, zero `*ue5dump*` residue |
>
> The status never once read **"Other proxy"** for a DLL we had just written — the truncated-PE
> failure mode the staging change exists to prevent.
>
> ⚠ **Force Overwrite is REQUIRED for step 2 to be a real test.** `PlanDeploy` returns
> `AlreadyCurrent` when the target is ours and the same version, so a plain second Deploy reports
> "1 success" **having written nothing**. The button says success either way.
>
> ⚠⚠ **`mtime` cannot witness a re-deploy, and it silently says "no write happened".**
> `File.Copy` copies the source's last-write time, and `File.Move` preserves it, so the deployed
> `dxgi.dll` carries `dist/proxy/dxgi.dll`'s timestamp **exactly** — identical before and after the
> second deploy. Use **`ctime`**, which does move on a rename-replace:
> `1787195042681345900 → 1787195091349982000`. The `view-0.log` pair confirms it independently
> (`Deployed dxgi.dll to Light Maze` at **11:04:02** and again at **11:04:51**).
>
> 📁 **ProxyDeploy logs to `view-0.log`, not `init-0.log`.** Worth knowing before grepping for this
> row's evidence — and note the category tag (`"ProxyDeploy"`) only *routes* the file, it is never
> printed, so grep the message text (`Deployed`, `Undeployed`) and not the category.
> | AC11 | with the game **running** (so the proxy is loaded and locked), click Deploy | still "File locked (game running?)", and the existing proxy is intact | the rename now raises the sharing violation the direct copy used to; the message must not change |
> ### ❌ AC11 step 2 FAILS — the message DID change `[STAGELOCK-2026-08-20]` **(new defect, build 3263)**
>
> The row's own premise is wrong, and that is the finding. "The rename now raises the sharing
> violation the direct copy used to" is **not true**: a locked target and a *mapped* target are
> different kernel states, and the two publish shapes hit them through different paths.
>
> **Measured at the OS level** (`tools/verify/ac11_locked_rename.py` — a reproducer, so it exits 1
> while this stands). Target mapped as a real image section by a live process, exactly as a running
> game's loader does it:
>
> | publish shape | Win32 error on a mapped target |
> |---|---|
> | **OLD** `File.Copy(src, target, overwrite)` — opens the target for write | `ERROR_SHARING_VIOLATION` (32) |
> | **NEW** `File.Move(stage, target, overwrite)` — renames over it | **`ERROR_ACCESS_DENIED` (5)** |
>
> A file carrying an image section refuses *deletion* with `STATUS_CANNOT_DELETE`, and the replacing
> rename has to delete the target. The negative control (nothing mapped) has **both** shapes
> succeeding, so this is the lock talking, not a broken path.
>
> **Confirmed one level up against the real `ProxyDeployService.CopyProxyStaged`**, called from a
> throwaway xunit test with the target mapped in-process:
> ```
> System.UnauthorizedAccessException   HResult = 0x80070005
> Message = "Access to the path is denied."      <- it does not even name the path
> DeployAsync's "File locked" filter catches it?  False
> stage file left behind?  False        target still intact?  yes
> ```
>
> **What the user sees.** `DeployAsync` filters on
> `catch (IOException ex) when (ex.HResult == 0x80070020 || ex.Message.Contains("being used"))`
> ([ProxyDeployService.cs:1152](ui/UE5DumpUI/Services/ProxyDeployService.cs:1152)).
> `UnauthorizedAccessException` is **not an `IOException`**, so it misses *both* arms and falls to
> the generic handler: the row goes to **`ErrorOther`** with **"Access to the path is denied."**
> instead of **`ErrorLocked`** / **"File locked (game running?)"**. That message names no path and
> reads as a permissions problem, so the natural user response is to re-run as administrator — which
> cannot help, because the file is not permission-denied, it is *in use*.
>
> **The other two halves of the row PASS**: no `.ue5dump-stage` survives (the `finally` fires on this
> path too) and the live proxy is left byte-intact.
>
> ⭐ **The fix is already written three lines away, twice.** `UndeployAsync`
> ([:1269](ui/UE5DumpUI/Services/ProxyDeployService.cs:1269)) and the orphan sweep
> ([:1813](ui/UE5DumpUI/Services/ProxyDeployService.cs:1813)) each carry an explicit
> `catch (UnauthorizedAccessException)` arm. `DeployAsync` is the only one of the three without it —
> and the only one whose write turned into a rename. So the fix shape is to add the same arm (or
> widen the filter to `0x80070005`), not to redesign staging. ⚠ Deliberately **not applied here**:
> this session verifies, it does not fix.
>
> #### ✅ CONFIRMED ON A REAL RUNNING GAME, 2026-08-20 — no inference left in the chain
>
> **The Adventures of Elliot**, launched with its `dxgi.dll` proxy loaded (build 3263, proxy mode),
> then Proxy Deploy → tick Elliot → **Force Overwrite** → **Deploy**. Force Overwrite is required:
> the deployed proxy is already current, so `PlanDeploy` would otherwise return `AlreadyCurrent` and
> never reach the write.
> ```
> [EROR] Deploy to The Adventures of Elliot_The Millennium Tales failed: Access to the path is denied.
> [INFO] Deployed: 0 success, 1 failed
> ```
> * grid status → **`ErrorOther`**, not `ErrorLocked`
> * log level → **`[EROR]`** from the generic handler, not the `[WARN] … file locked` line
> * message → **"Access to the path is denied."** — it names no path and never mentions the game
>
> The row's expected text — *"still 'File locked (game running?)'"* — **did not appear**. So the
> earlier `LoadLibraryEx` image-section probe was not an approximation of this case: it predicted it
> exactly, and a real game's loader reproduces it verbatim.
>
> The row's **other** two clauses hold: the live `dxgi.dll` is **byte-identical** to
> `dist/proxy/dxgi.dll` (2,876,928 B, SHA-256 match) and **no `.ue5dump-stage`** was left in the
> game's `Binaries\Win64`. Only the classification is wrong.
> | AC12 | on this machine (multi-library Steam install), open Proxy Deploy and let it scan | the same library folders as before are found; `proxy`/`init` log has **no** "libraryfolders.vdf is malformed" line | the parser is fully unit-tested but its input is a real Valve-written file — a rejected real VDF would silently halve game detection |
> ### ✅ AC6 + AC12 PASS 2026-08-20 `[L7-AC6-AC12-2026-08-20]`
>
> **AC6 — PASS, with the DLL's own sweep deliberately taken out of the way.** The DLL sweeps the
> *same* file family once per process, so the two bait files were planted **after** DumperTest was
> injected and had already swept — otherwise the DLL removes the bait, the UI legitimately logs
> nothing, and that reads as a failure of the UI sweeper. Then the UI was launched and connected.
>
> * backdated `UE5CEDumper.{COMPUTERNAME}.json.tmp.99999` (3 h old) → **gone**
> * fresh `UE5CEDumper.{COMPUTERNAME}.json.tmp.88888` → **survives** — this is the age guard, and it is the
>   half that matters: without it the sweeper would delete the DLL's in-flight write.
> * `init-0.log`: `AobUsageService: removed 1 abandoned staging file(s) older than 1 h`
> * the real cache still parses afterwards (28 entries)
>
> ⭐ **This is the C# twin of the DLL-side sweep, and the pair is now verified end to end**: the DLL
> logs `HintCache: removed 1 abandoned staging file(s) older than 1h` (see `[FLSWEEP-2026-08-19]`)
> and the UI logs `AobUsageService: removed …`. Two independent sweepers over one file family, both
> age-guarded, neither destroying the other's live write — which is exactly the cross-process
> pairing this row says is only observable with a game actually writing the same cache.
>
> **AC12 — PASS.** A live **Scan Steam** in the same session logged `Found 2 Steam library
> folder(s)` and then `Found 18 UE game(s)`, with **zero** library-related `[WARN]`/`[ERROR]` lines
> (no "malformed"). The 2 matches `libraryfolders.vdf` exactly — `C:\Program Files (x86)\Steam` and
> `D:\SteamLibrary` — so the real Valve-written VDF is still parsed and the multi-library install is
> not silently halved.


> ### ✅ AC3 PASS 2026-08-20 `[AC3-BRIDGE-2026-08-20]` — all three outcomes in ONE log, one variable changed at a time
>
> **The whole point of the AC3 fix is that a bare `catch` had collapsed five different reasons into
> one blank "AOBMaker not connected".** `ReconnectAsync`'s own doc comment
> ([AobMakerBridgeService.cs:474](ui/UE5DumpUI/Services/AobMakerBridgeService.cs:474)) names them:
> cancelled-by-caller and not-running stay at **Debug**, "anything else is a real fault and gets a
> **Warn** that names the exception type", success is **Info**.
>
> **A survey of all 107 UI `init-*.log` files first showed which arms ordinary use already covers:**
> `[DBUG] … no server …` **211×** and `[INFO] … bridge: available` **145×** — richly evidenced. The
> **Warn arm had fired 0 times**, and it is the arm that carries the fix's value, because it is what
> says *"CE is up, something else is wrong"* instead of the useless *"start Cheat Engine"*.
>
> It also cannot be staged the obvious ways. Starting/stopping CE only ever reaches the Timeout arm,
> and **saturating the pipe does not work either**: the `try` holds nothing but `ConnectAsync`, and a
> server at capacity makes ConnectAsync *wait*, ending in `TimeoutException` — the Debug arm again.
> `tools/verify/ac3_denied_pipe.py` therefore creates a named pipe **at the bridge's exact name with
> a deny-all DACL** (`D:(D;;GA;;;WD)`), so a server genuinely exists and genuinely refuses. Nothing
> is installed; the pipe is a kernel object that dies with the rig.
>
> **All three lines landed in the SAME `init-0.log`, in one UI process, inside four minutes:**
> ```
> 21:25:06 [WARN] AOBMaker bridge: connect to 'AOBMakerCEBridge' failed (UnauthorizedAccessException): Access to the path is denied.
> 21:27:46 [DBUG] AOBMaker bridge: no server on \\.\pipe\AOBMakerCEBridge within 2000 ms (Cheat Engine not running, or the AOBMaker plugin is not loaded)
> 21:29:02 [INFO] AOBMaker CE Plugin bridge: available
> ```
> Three texts, three levels, and the Warn names the exception **type** as specified. Only one thing
> changed between each: the denying pipe was held, then released (CE still closed), then CE was
> started with the plugin. **The middle line is the negative control** — it proves the Warn was
> caused by the deny and not by some ambient condition, which a Warn observed on its own could not.
>
> ⚠ **One clause is a code-level certainty rather than an observation, and is recorded as such.**
> The row asks for the no-server line *twice* — once with CE closed, once with CE open but the plugin
> not loaded. Only the first was staged. The second needs no run: both are the **same**
> `catch (TimeoutException)`, and the client cannot tell them apart — nothing is listening on the
> pipe either way. The source comment says exactly this ("Cheat Engine is not running, or it is but
> the AOBMaker plugin was never loaded"), and the emitted text names both causes in one sentence.
> Disabling the plugin would re-run the identical branch, so it was not worth changing the
> maintainer's CE configuration for.
>
> ℹ️ **Not a defect, but worth knowing:** the *user-visible* text stays "AOBMaker plugin not detected
> — open Cheat Engine…" even on the Warn path. That is the design the row describes — every public
> method turns a false into the same message — and the fix was to make the **log** discriminate. If
> the denied case should ever reach the user differently, that is a new request, not this row.
>
> ⚠ **Rig trap for whoever re-runs this:** the AOBMaker ⟳ refresh button **moves**, because the
> toolbar status text beside it is variable-width. It sits at ~(445, 36) with a short status and
> ~(658, 36) once the status reads "AOBMaker plugin not detected — open Cheat Engin…". Two clicks
> here landed on the Address dropdown instead and logged nothing; the 21:25:06 Warn came from the
> UI's own **startup** probe. Screenshot the toolbar before clicking rather than reusing a coordinate.
>
> **With this, every row of audit L7 has a verdict**: AC3 ✅, AC6 ✅, AC10 ✅, AC12 ✅, AC11 step 1 ✅ —
> and AC11 step 2 ❌, which is not an outstanding check but a *result*: it became
> `[STAGELOCK-2026-08-20]`.

### ✅ VERIFIED 2026-08-20 — audit L9 (T1c VMs/Core/DTOs): AE13 / AE20 / AE30 all three run live

*Seventeen findings closed (AE11–AE25, AE30, AE31); **fourteen need nothing live**. AE12 and AE22
turned out to be **already fixed** by `6fc00e4d` (X5's `ClearOnDisconnect` fan-out) and were closed
by reading the current code, not by re-fixing it. AE25 and AE31 are **doc-only, direction
re-derived**: AE25's comment claimed `Between` was excluded from the group scan-type picker while
listing it fourth — the CODE is right (three independent witnesses: the spec, the DLL's `value2`
parser, and this VM's own validator), so removing the option would have deleted a shipped feature to
satisfy a stale comment. Everything else is unit-pinned with one combined negative control:
reverting the behaviours turns **21** tests red across all five affected classes and leaves every
"must NOT change" control green.*

⚠ **AE13's half is DLL-gated** — the UI defaults `per_slot_cap_hit` to `false`, so a stale injected
DLL makes it look like a no-op rather than a failure. Compare against `dist/build_number.txt`, not
the repo's (`[STALEDLL]`).

> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | AE13 ⚠ DLL-gated | Value Search → **Group** mode, two slots, values chosen to be COMMON (e.g. `0` and `1`, or `100` and `100`) so one slot matches far more than 256 fields on some object; Group First Scan, then Group Next Scan | the status line gains `⚠ a slot matched more than 256 fields — only that many were kept, so "All fields" is a page …`, and the SAME clause survives the Next Scan | this fact was computed by `Orden::MatchGroup` and written only to `LOG_WARN` inside the DLL, so no user could ever see it. Distinct from `deadline_hit`: the result set is complete while a slot's field list is not. **Negative half worth doing:** repeat with DISTINCTIVE values (a real HP + a real MP) and confirm the clause does NOT appear |
> ### ✅ AE13 PASS 2026-08-20 `[AE13-DQ7R-2026-08-20]` — clause appears, survives the refine, and is absent on distinctive values
>
> **DQ7R** (UE427, 149,408 objects, `version.dll` proxy, build 3263 — so not DLL-gated). Value Search
> → **Multiple values (group)**, two `NumericNoByte`/`Exact` slots.
>
> **Positive — values `0` and `1`:**
> ```
> Group First Scan: 50000 matching objects in 1849 ms (scanned 83581 objects, 4387 classes)
>   ⚠ truncated (25s deadline / result cap) — raise the Timeout slider or refine
>   ⚠ a slot matched more than 256 fields — only that many were kept, so "All fields" is a page
>     and a later Changed/Decreased refine can re-read only what was kept; use more distinctive values.
> ```
> ⭐ Both warnings are present **and separate**, which is the distinction the row draws: `truncated`
> is about the result SET being partial, the new clause is about one slot's FIELD LIST being partial.
> Seeing them side by side shows they are independently emitted, not one message doing two jobs.
>
> **It survives the refine — Group Next Scan:**
> ```
> Group Next Scan: 49999 surviving objects in 234 ms
>   ⚠ the Group First Scan it refined was TRUNCATED — …
>   ⚠ a slot matched more than 256 fields — only that many were kept, so "All fields" is a page …
> ```
>
> **Negative — values `100` and `3`:**
> ```
> Group First Scan: 471 matching objects in 1427 ms (scanned 122413 objects, 4388 classes)
> ```
> **471 real matches and no clause at all** (and no truncation).
>
> ⚠ **A 0-match run is NOT a valid negative control, and one was rejected here.** `1337` + `100`
> returned `0 matching objects` — the clause was absent, but vacuously: with no matching object there
> is no slot whose field list could overflow. The control has to produce a healthy match set and
> still stay quiet, which `100` + `3` does.
> | AE20 | Proxy Deploy → **Find leftovers** on a machine with several leftover chains, tick 3+ rows, **Delete checked**, then click **Cancel operation** while it runs | the pass stops early and the result line reports what DID happen (`… cancelled`), with the un-processed rows still listed and unticked | the destructive Recycle-Bin delete accepted a `CancellationToken`, checked it between rows and carried a whole cancelled-reporting path that **nothing in the app could reach** — five of the panel's seven token-taking commands were in that state. ⚠ Needs several rows: with one row the loop finishes before a human can click |
> ### ✅ AE20 PASS 2026-08-20 `[AE20-2026-08-20]` — the cancel path is reachable and reports honestly … with one gap
>
> **Staged entirely synthetically.** The session plan's §4 authorises two writes outside our own
> files and this destructive step is not one of them, so `tools/verify/ae20_orphans.py` created **40**
> throwaway trees named `ZZAe20Orphan###\ZZOrphan\Binaries\Win64\version.dll` with no shipping exe
> beside them — which is precisely the scanner's definition of a leftover, so they are the real thing
> minus a game that never existed. Nothing on disk that predated the run could be touched.
>
> Find leftovers → **`Found 40 leftover proxy DLL(s)`** → ticked 4 → **Delete checked (4)** →
> confirmation dialog → **Move to Recycle Bin** → **Cancel operation** immediately after.
>
> ```
> Cleanup cancelled after 2 of 4 leftover(s) — 2 file(s) recycled, 8 folder(s) removed
> ```
> * the pass **stopped early** — 2 of 4 ✅
> * the line says **"cancelled"** and carries the tally of what DID happen ✅
> * the un-processed rows are **still listed** (`…Orphan002` … `005`), and the completed two are gone
>   from the list ✅
>
> That path was unreachable from the app before the fix, and it is now reachable on the first attempt.
>
> #### ✅ FIXED 2026-08-21 — the interrupted row was mis-reported — `[ORPHANCANCEL-2026-08-20]` (LOW)
>
> The `ProxyDeploy` log for the same run has **three** recycle lines, not two:
> ```
> 12:46:07.495 Recycled leftover proxy …ZZAe20Orphan000\…\version.dll
> 12:46:07.570 Recycled leftover proxy …ZZAe20Orphan001\…\version.dll
> 12:46:07.644 Recycled leftover proxy …ZZAe20Orphan002\…\version.dll   <-- not in the tally
> 12:46:07.645 Cleanup cancelled after 2 of 4 leftover(s) — 2 file(s) recycled, 8 folder(s) removed
> ```
> On disk afterwards: trees `000`/`001` fully gone, and **`002` still present with its `version.dll`
> already recycled and its folder chain un-pruned**.
>
> **Mechanism** ([ProxyDeployViewModel.cs:989-1008](ui/UE5DumpUI/ViewModels/ProxyDeployViewModel.cs:989)):
> the token is passed *into* `RemoveOrphanProxyAsync`, so on row 002 it recycled the file and **then**
> observed the cancel and threw. Everything after that `await` is skipped —
> `files += result.FilesRecycled`, `ok++`, `row.IsSelected = false`, `DropOrphanRow`. Hence all four
> symptoms at once: the tally under-counts by one, the row stays **ticked** (against the code's own
> comment *"Unchecked either way: a pass is over when it is over"*, which is also the row's
> "un-processed rows … unticked" expectation), the row still advertises *"Recycle version.dll, then
> remove up to 4 folder(s)"* for a DLL that is already gone, and the half-pruned chain is invisible.
>
> ⭐ This is **audit #4's root cause verbatim** — *the report and the reality are computed by
> different code paths* — and it lands on the one line whose comment says
> *"a cancel that discards the tally would hide a half-pruned chain"*. It hid exactly that.
>
> **Fix shape** (not applied — this session verifies): fold the interrupted row's partial result in
> before the exception escapes — wrap the single-row `await` so `OperationCanceledException` still
> contributes `FilesRecycled`/`DirsRemoved` and unticks the row, then rethrow. Severity LOW: bounded
> at one row per cancel, and nothing is lost, only mis-counted.
>
> **FIXED 2026-08-21, and the sweep found a second instance of the same hole with no cancel in it.**
>
> **Cancellation is now OBSERVED, not thrown.** Both loops in `RemoveOrphanProxyAsync` swapped
> `ct.ThrowIfCancellationRequested()` for `if (ct.IsCancellationRequested) { cancelled = true; break; }`,
> and the method returns `OrphanRemovalResult(..., Cancelled: true)` carrying the counts it reached.
> Throwing out of the middle of a row was what discarded them.
>
> `allFilesGone` now states `!cancelled` explicitly. It was already false after an interrupted file
> loop (`recycled + vanished` falls short of the total), so no folder was ever pruned under a
> half-emptied directory — but relying on that arithmetic to hold after a future edit is the kind of
> implicit guard worth making explicit.
>
> **The caller folds the partial row in.** `files += result.FilesRecycled` / `dirs +=
> result.DirsRemoved` moved OUT of the `if (result.Success)` branch, and an interrupted row is
> neither counted as a failure nor dropped — its half-pruned chain is exactly what the user needs
> left on screen — then the `OperationCanceledException` is rethrown so the outer handler prints the
> cancelled summary, which now includes what that row managed.
>
> ⭐ **Moving the tally out of the success branch fixed a second, unreported case**: a row that
> recycles one of its two DLLs and then hits a lock returns `Success == false`, so **everything it
> recycled was already going unreported** — no cancel required. The finding was the visible half of
> a wider accounting hole; `APartlyLockedRow_AlsoCountsWhatItRecycled` pins it.
>
> **Four tests**, driven through the real scan → delete path with a stub service (so the per-row
> `PropertyChanged` wiring the scan installs is exercised too), one per symptom the finding lists:
> the tally counts the partial row (`2 file(s) recycled`, not 1); the row stays on the list,
> **unticked**, with an `Interrupted — …` status replacing the future-tense promise about a file
> already in the Recycle Bin; a cancel is not reported as a failure; and the partly-locked case.
>
> ⭐ **Shown able to fail**: reverting only the accounting (tally back inside `if (result.Success)`)
> failed **three** of the four — and the fourth, `AnInterruptedRow_StaysOnTheList_Unticked_…`, kept
> passing, which is right: it targets the row-retention half, which that revert does not touch. Four
> tests failing together would have meant they were one test written four times.
>
> ⚠ **Not re-run on disk.** `AE20`'s rig builds real orphan trees and cancels mid-pass; this fix is
> pinned at the view-model seam with a stubbed service, so the DLL-recycling and folder-pruning
> behaviour under a real cancel is unchanged-by-inspection rather than re-measured. Re-running
> `AE20` would close that.

> | AE30 | Object Tree → pick any UObject → set the address format to **module+offset** → Copy address, and paste into CE. Then relaunch the game and paste the same string again | the copied text is now bare hex (e.g. `1E55C298D40`), NOT `"Game-Win64-Shipping.exe"+FFFF81…`; it resolves this run and plainly fails to resolve after a relaunch instead of silently pointing somewhere unrelated | a heap UObject sits BELOW a `0x7FF7…` image base, so the old unsigned subtraction WRAPPED. That string round-trips within one run, which is what made it dangerous: it looked like the ASLR-stable form the user picked the option for. **Control:** copy an address that IS inside the module (a GObjects/GNames pointer from the Pointers panel) and confirm it still formats as `"exe"+RVA` and still resolves after a relaunch |
> ### ✅ AE30 PASS 2026-08-20 `[AE30-DQ7R-2026-08-20]` — but the CONTROL as written cannot be run
>
> **DQ7R** (UE427, 149,408 objects, `version.dll` proxy). Toolbar **Address → `Module+Offset`**, then
> Object Tree → right-click a class → **Copy Address**:
> ```
> clipboard = 1FD85ABF4C0
> ```
> Bare hex — **not** `"DQ7R-Win64-Shipping.exe"+FFFF81…`. The heap UObject sits below the `0x7FF6…`
> image base, `TryGetModuleRva` refuses it, and the format falls back instead of wrapping. That is
> the fix, on the exact path it fixed: `ObjectTreeViewModel.CopyAddressAsync` does pass
> `ModuleName`/`ModuleBase`, so the fallback is a real decision and not a missing argument.
>
> ⚠ **The control the row prescribes does not test anything — do not re-attempt it.** Copying
> GObjects from the Pointers/System panel gave `7FF679997660`, bare hex, which looks like a failure
> and is not: those buttons call `StripHexPrefix` and **never consult the address format at all**
> ([PointerPanelViewModel.cs:1502-1533](ui/UE5DumpUI/ViewModels/PointerPanelViewModel.cs:1502)). They
> are a deliberate raw-address copy, so they cannot demonstrate anything about `FormatAddress`.
>
> 📌 **An observation the maintainer may want to act on (not a defect).** Every one of the app's
> `FormatAddress` call sites — Instance Finder ×5, Live Walker ×6, Object Tree, Class Pivot, SPC,
> Related — is handed a **UObject or a field address**, i.e. always heap. So after this fix the
> `Module+Offset` option can essentially **never** produce `"exe"+RVA` for anything the app copies;
> it silently formats as absolute hex every time. That is the correct behaviour (the old output was a
> stability promise the address could not keep), but the menu entry now advertises a form the app has
> no reachable path to emit. Worth either a tooltip or a re-think, and it is why the row's control
> could not be satisfied from any panel.

### ✅ FIXED 2026-08-23 (was NEW DEFECT) `[PROXYALTWINMM-2026-08-23]` — the proxy advisory offered the flavour NOTHING imports and hid the one almost everything does

Found while working the A6 offline bucket (the Lushfoil proxy-not-loading row), by reading the
import tables rather than the code.

`ProxyImportAnalyzer.DescribeImportable` built its "alt:" list from **dxgi and dinput8 only**. It
never mentioned **winmm**, although the analyzer has parsed `ImportsWinmm` since **2026-07-27**
(`a2c81a0c` — *"teach the analyzer winmm"*), winmm is one of the **four** proxies `-Target DLL`
builds, and the class's own remarks group `dxgi.dll`/`winmm.dll` together as the *"pure static-import
hijacks"* — i.e. the **deterministic** pair, the ones most worth suggesting.

⭐⭐ **Measured, not argued.** Every UE shipping `.exe` installed on this machine, parsed with
`tools/pe/pe_imports_exports.py`:

```
16 shipping exes:  14 import winmm   ·   14 import dxgi   ·   4 import version   ·   0 import dinput8
(14 of the 16 import BOTH dxgi and winmm; the other two are modular builds importing none of the four)
⚠ CORRECTED 2026-08-23: first written here and in dev-log as "13 import dxgi" / "13 games
importing both". Re-derived with `uniq -c` over the same 16 exes: it is 14 and 14. The dev-log
entry keeps the wrong figure because that file is append-only; THIS is the canonical count.
```

So the advisory listed **dinput8 (0 of 16)** and suppressed **winmm (14 of 16)**. On the 14 games
importing both (Lushfoil, DQ7R, Avowed, Elliot, Geri, Manor Lords, TQ2, Solarpunk, …) the user saw
`version · default · alt: dxgi` and was never told winmm was equally available. The empty-list
sentence was wrong too: it read `no dxgi/dinput8`, which enumerates two of the three non-version
flavours and omits the third.

⚠ **Root cause, and it is a lesson already in `working-lessons.md` §2.3.** `ImportsWinmm` was
**appended to the record with a default** (`bool ImportsWinmm = false`) — the only defaulted member.
So all four `Recommend` tests construct `ProxyImportInfo` with **three positional arguments** and
silently assert the no-winmm case. `DescribeImportable` was then edited *again* on **2026-08-10**
(`c28e3a78`, two weeks after winmm was taught) and still not updated: the default made the gap
invisible to the tests. The test file's own comment still says *"none of OUR three"*.

**Fix:** add winmm to the list (order `dxgi, winmm, dinput8` — deterministic first), and correct the
empty case to `no dxgi/winmm/dinput8`.

⭐ **The guard is structural, not another example.** `Recommend_EveryNonVersionFlavour_IsOfferedWhenImported`
enumerates `ProxyType` and asserts that a game importing only that flavour is told so — a **fifth**
flavour added the same way fails it.

⚠⚠ **The first draft of that guard was VACUOUS, and only the negative control caught it.** It
asserted `Display.Contains("winmm")` — and the fallback sentence `no dxgi/winmm/dinput8` *contains*
`winmm`, so with the fix removed the guard still passed while the two hand-written cases failed. It
now matches inside the `alt:` segment only. Control re-run: dropping the winmm line fails **all
three** tests; restoring it returns **51/51**. Full suite **4,712 / 0 failed**; **13/13** gates.

### ✅ VERIFIED 2026-08-20 `[PROXYLOAD-2026-08-17]` — `DeployedCurrent` no longer means "silently ignored" — step 1 run on OCTOPATH, the bypass is REAL on this pair

*Was: the Proxy Deploy panel's `DeployedCurrent` is computed from the file on DISK only; it does NOT
mean the game loads the proxy. Measured on **OCTOPATH TRAVELER**: `version.dll` byte-identical to
`dist/proxy`, panel said `DeployedCurrent 1.0.0.3262`, yet the log folder never appeared and only
`C:\WINDOWS\SYSTEM32\VERSION.dll` was in the module list — the app-dir proxy is silently ignored. The
correlation was 3 for 3: a title that STATICALLY imports the proxy's base name gets the loader
satisfying the import from an already-mapped module (an overlay/launcher such as Steam maps it early)
and never searches the app dir; titles that don't import it get ours. DQ I&II, same flavour/build,
had BOTH `VERSION.dll` (ours) and System32's mapped and worked. ⚠ The load-order mechanism FITS but
is **untested** — `KnownDLLs` was refuted (none of version/dxgi/winmm/dinput8 is a KnownDLL), so it is
treated as a HEURISTIC, not a law.*

**What shipped (both offline halves, 2026-08-19):**
1. **Static-import BYPASS screening** — a small AOT-safe managed PE import reader already existed
   (`ProxyImportAnalyzer.Analyze`, unit-tested against synthetic PEs). Added `DescribeImportBypassRisk`
   / `DescribeDeployAdvisory`: when the chosen flavour IS named in the exe's import table, the deploy
   note and the Suggested column warn it may be pre-empted by an already-mapped copy and suggest a
   flavour it does not import, or direct injection. Screens all four base names. **Worded as a
   heuristic** (it can false-positive — OCTOPATH imports winmm yet its winmm proxy WORKS, so the load
   signal below is what actually settles it per-game).
2. **A "did it actually load?" signal** — a new **"Loaded?"** grid column, refreshed on every scan/
   refresh from the per-process log folder the DLL creates on load
   (`%LOCALAPPDATA%\UE5CEDumper\Logs\<exe-base-name>`; join key mirrors `dll/src/Sein.cpp
   InitProcessMirror` exactly). States: **"loaded &lt;date&gt;"** (folder present & fresh),
   **"loaded &lt;date&gt; (stale)"** (present but > `LogMaxAgeDays` old — a previous run/build, never
   claimed as "loaded now"), **"not observed"** (absent → honest UNKNOWN, NOT a failure claim for a
   game that simply hasn't been launched). Disk `Status` + "not observed" is the OCTOPATH silent
   failure, now visible. Pure logic (`ClassifyLoad` / `ProcessLogFolderName`) is unit-tested; the
   folder lookup is exercised end-to-end via a temp-appdata service test.

> ### 📊 THE IMPORT-BYPASS HEURISTIC, MEASURED 2026-08-20 `[PROXYLOAD-CORR-2026-08-20]` — it false-positives 4 times out of 4
>
> The row states the mechanism "FITS but is **untested**" and cites a 3-for-3 correlation. It is now
> measured across every title on this machine that has a proxy deployed, cross-referencing two facts
> already on disk: the exe's **static import table** (the same `tools/pe/pe_imports_exports.py` the
> row names) and whether the DLL **actually loaded** there — i.e. whether
> `Logs\<exe-base-name>` exists, which is exactly the join the new "Loaded?" column uses.
> Rig: `tools/verify/proxyload_correlation.py`.
>
> | title | deployed | imports that name? | loaded? | |
> |---|---|---|---|---|
> | Avowed | `dxgi` | **YES** | **yes** | counter-example |
> | EVERSPACE | `version` | **YES** | **yes** | counter-example |
> | OCTOPATH TRAVELER | `winmm` | **YES** | **yes** | counter-example (the row already knew this one) |
> | Elliot | `dxgi` | **YES** | **yes** | counter-example |
> | DQ7R · Lushfoil · Manor Lords · Geri | `version` | no | yes | as expected |
> | EVERSPACE 2 | `version` | *(exe unreadable by the parser)* | yes | not counted |
>
> **4 titles import their deployed flavour; all 4 loaded our proxy anyway. 0 titles imported it and
> failed to load.** So on this machine the warning would fire four times and be wrong four times.
>
> ⚠ **This does NOT refute the row's own 3-for-3**, and must not be read as doing so: that was
> measured on *specific flavour/title pairs* (notably OCTOPATH with **`version`**, which genuinely is
> bypassed), whereas this samples **whatever flavour happens to be deployed now** (OCTOPATH currently
> has `winmm`, which works). Both are true. The point is narrower and still useful: **a static import
> is not a prediction of bypass**, and the screening is right to be worded as a heuristic rather than
> a verdict.
> ⚠ **Observational, not controlled** — nobody deployed each flavour to each title on purpose. Note
> also what is *absent*: **zero** "imports it, no log folder" cases, which is the only shape that
> would have supported the heuristic — and even that one would be ambiguous, since "never launched"
> explains a missing folder equally well. Keeping "not observed" distinct from "bypassed" is the
> whole point of the `Loaded?` column.
>
> ⇒ **Steps 1–3 below still need the UI** (the Suggested / Loaded? columns are what they assert).
> What is settled here is the *data* those columns render, and that the heuristic's false-positive
> rate on real titles is high rather than incidental.

> Needs a running game. No sample was captured for the code path, so this is a real live check.
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | 1 ⚠ THE ONE THAT MATTERS | deploy `version.dll` to a title that STATICALLY imports version.dll (**OCTOPATH**; confirm with `py tools/pe/pe_imports_exports.py imports <exe> --dll version`), launch it, then **Scan/Refresh** the panel | Suggested column WARNS ("imported, may be bypassed"); after launch the **Loaded?** column stays **"not observed"** next to `DeployedCurrent` | before the fix the panel said only `DeployedCurrent` and nothing flagged the silent failure |
> ### ✅ STEPS 2 + 3 PASS 2026-08-20 `[PROXYLOAD-UI-2026-08-20]` — the `Loaded?` column is real, and it separates "not observed" from "loaded"
>
> Proxy Deploy → Scan Steam, `Found 18 UE game(s)`, `Source DLL v1.0.0.3263`. Read straight off the
> grid (Status · **Loaded?** · Suggested proxy):
>
> | title | Status | **Loaded?** | Suggested |
> |---|---|---|---|
> | **Echoes of Aincrad Demo** | NotDeployed | **`not observed`** | version · default · alt: dxgi |
> | **DQ7R** | DeployedOtherType | `loaded 2026-08-1…` | **`version.dll · confirmed work…`** |
> | **OCTOPATH TRAVELER** | DeployedOtherType (winmm) | `loaded 2026-08-1…` | `version · default · **imported**,` |
> | EVERSPACE | DeployedOtherType | `loaded 2026-08-1…` | `version · default · **imported**,` |
> | Avowed | **DeployedCurrent** | `loaded 2026-08-1…` | dxgi.dll · confirmed working |
> | Satisfactory | NotDeployed | `loaded 2026-08-2…` | version · default · imported, |
> | Solarpunk · DragonSword | NotDeployed | `loaded 2026-08-1…` | `injection · no proxy deployed` |
>
> * **Step 2 — PASS.** DQ7R does not import `version.dll`, has it deployed, and reads
>   **`loaded`** with **no bypass warning** (`version.dll · confirmed work…`). So the signal is not a
>   blanket "not observed" — it reads the real log folder.
> * **Step 3 — PASS.** OCTOPATH runs the **winmm** flavour, reads **`loaded`**, *and* its Suggested
>   column still says **`imported`**. That is precisely the row's point: the warning is a heuristic
>   and **the load signal, not the import table, is the per-game source of truth**.
> * ⭐ **"not observed" is demonstrated by a real never-launched title**, `Echoes of Aincrad Demo` —
>   not staged. That is the honest UNKNOWN the fix adds, and it sits next to 17 rows that DO say
>   `loaded`, so the column is discriminating rather than defaulting.
> * ⭐ **Direct injection is reported correctly too**: Satisfactory and Solarpunk are `NotDeployed`
>   yet `loaded` (they were injected, not proxied), with `injection · no proxy deployed` as the
>   suggestion. A disk-only view could not have said that.
>
> ⚠ **Step 1 NOT run.** It needs `version.dll` specifically deployed to OCTOPATH, which currently
> carries `winmm`; changing that means a deploy plus a launch. Note also
> `[PROXYLOAD-CORR-2026-08-20]` above measured the import warning false-positiving **4 of 4** on the
> flavours deployed today — so step 1 should be run expecting to *test* the claim, not to confirm it.

> | 2 | deploy `version.dll` to a title that does NOT import it (**DQ7R** / **DQ I&II**), launch, Refresh | **Loaded?** shows **"loaded &lt;today&gt;"**; no bypass warning | proves the signal is not a blanket "not observed" — it reads the real folder |
> ### ✅ STEP 2 PASSES 2026-08-20 `[PROXYLOAD-STEP2-2026-08-20]` — DQ7R, with a before/after and a free negative control
>
> **The premise was checked first, not assumed.** `tools/verify/proxyload_correlation.py` reads the
> exe's real import table: **DQ7R does `no`t import `version.dll`** — so it is the right title for
> this step, which is about a *non-importing* game.
>
> | | Loaded? column |
> |---|---|
> | before launching (12:06) | `loaded 2026-08-1…` |
> | DQ7R launched 12:17:59, proxy log written **12:18:00**, then **Refresh** | `loaded 2026-08-2…` |
>
> ⭐ **The change is the evidence.** The column is clipped to `2026-08-2…`, so the rendered text alone
> would be weak — but it moved from the `-08-1` decade to `-08-2` as a direct result of the launch,
> and the source it reads (`Logs\DQ7R-Win64-Shipping\`) is stamped **2026-08-20 12:18:00**. A stale or
> blanket value cannot do that.
>
> **A negative control came free in the same view:** `Titan Quest II` reads **`not observed`**, and it
> has **no log folder at all** on disk. So the column distinguishes "never loaded" from "loaded on
> date X" from real evidence, which is exactly what the row asks.
>
> **No bypass warning.** DQ7R's Error cell reads only the informational `Deployed as version.dll`;
> the import advisory does not appear — correct, since it does not import the name.
>
> ℹ️ Step 3's assertion ("the warning is a heuristic and the LOAD signal is the source of truth") is
> already settled more strongly by `[PROXYLOAD-CORR-2026-08-20]` above, which found **4 of 9** deployed
> titles importing the proxy name **and loading ours anyway** — four counter-examples rather than the
> one OCTOPATH case the step describes.
> | 3 | on OCTOPATH, switch to the `winmm` flavour, deploy, launch, Refresh | **Loaded?** → "loaded" (winmm proxy works per `[OCTOPATH-G2T3]`) even though winmm may also be imported | proves the warning is a heuristic and the load signal, not the import table, is the source of truth |


> ### ✅ STEP 1 PASSES 2026-08-20 `[PROXYLOAD-STEP1-2026-08-20]` — OCTOPATH + `version.dll` really is bypassed, measured at the module level
>
> Run with the maintainer's explicit authorisation to modify a game install. **The install was
> restored byte-identically afterwards and that was verified, not assumed** (§restore below).
>
> **Precondition, checked with a control rather than assumed.** `pe_imports_exports.py` shows
> OCTOPATH statically imports `version.dll` (`VerQueryValueW`, `GetFileVersionInfoW`,
> `GetFileVersionInfoSizeW`) and imports **no** `dinput8.dll` — so the reader discriminates instead
> of echoing whatever it is asked.
>
> ⚠ **The row's literal PASS condition is unreachable as written, and that is a defect in the ROW,
> not in the product.** `ClassifyLoad` ([ProxyImportAnalyzer.cs:439](ui/UE5DumpUI/Services/ProxyImportAnalyzer.cs:439))
> emits `not observed` **only when the log folder is absent**. OCTOPATH's folder existed with 40
> files one day old — far inside `Constants.LogMaxAgeDays = 21` — so the cell read
> `loaded 2026-08-19` *before anything was launched* and would have read `loaded <today>` after any
> launch of any build. Worse, **the column is keyed to the EXE, never to the flavour**, so it
> structurally cannot say *which* proxy loaded; and both flavours stamp the same `FileVersion`
> `1.0.0.3263`, so neither the panel's version text nor the log's `build:` line can separate them
> either. The folder was therefore **parked aside** to make the row's own expectation reachable —
> and parked **outside `Logs\`** on the same volume, because `Sein::PruneStaleProcessFolders` runs
> on every DLL init in *any* process and `remove_all()`s aged/empty siblings under `Logs\`.
>
> **The decisive observable is the loaded-module list, not the column.** It is a positive fact in
> both directions and it is the same observation the original 2026-08-17 finding rested on.
>
> | # | staged / measured | result |
> |---|---|---|
> | 1 | Suggested cell read **BEFORE** any change | `version · default · imported,` — the rendered head of `version · default · imported, may be bypassed — try injection` ([ProxyImportAnalyzer.cs:250](ui/UE5DumpUI/Services/ProxyImportAnalyzer.cs:250)). **The row's first assertion. ✅** |
> | 2 | winmm taken out of play by an in-place **rename** | `winmm.dll` → `winmm.dll.holdback-step1`, sha unchanged `823d02b3…`. Panel re-read `NotDeployed`, so the panel agrees the flavour is gone |
> | 3 | radio moved **dxgi → version**, OCTOPATH ticked, **Deploy** | `Deployed: 1 success, 0 failed`; on disk `version.dll` sha `5b8d3a4b31ea7af5` = `dist/proxy/version.dll` exactly; Status → `DeployedCurrent` |
> | 4 | log folder parked | `not observed` now reachable; anything reappearing is attributable to this run |
> | 5 | launched via `steam.exe -applaunch 921570` | booted: window titled `OCTOPATH TRAVELER `, **128 modules mapped**, 1.35 GB, stable across 40 s, same PID (no self-relaunch this time). **No `0xC0000142`** — so the static import resolved fine |
> | 6 ⭐ | **module walk of the shipping exe (pid 45488)** | `version.dll` → **`C:\WINDOWS\SYSTEM32\VERSION.dll` ONLY**. Our app-dir copy is **not mapped at all**. **BYPASSED.** |
> | 7 | log folder after the launch | **none created** ⇒ `ClassifyLoad` would render **`not observed`**. **The row's second assertion. ✅** |
> | 8 | our `version.dll` still on disk, right sha | rules out "the deploy silently vanished" as the explanation for 6 and 7 |
>
> ⭐ **Step 6 is what makes this a result rather than a null.** Every failure mode of this experiment
> — bypass, a game that never booted, a failed deploy, a faulting `DllMain`, the passive-forwarder
> branch — produces the *same* "no new logs". The module list separates them: 128 mapped modules
> proves the loader completed, and `SYSTEM32\VERSION.dll` present with ours absent is a **positive**
> statement that the app-dir copy lost the resolution.
>
> ### ⚖ What this does and does NOT establish — the two halves point opposite ways
>
> **It CONFIRMS the row's original 3-for-3 on the exact pair it was measured on.** OCTOPATH +
> `version.dll` is genuinely bypassed. That pair had never been retested;
> `[PROXYLOAD-CORR-2026-08-20]` sampled *whatever flavour happened to be deployed*, which for
> OCTOPATH is **winmm** — and OCTOPATH's entire log history is winmm, **not one version-proxy line
> in any of its 40 files**.
>
> **It does NOT rescue the heuristic, and a zero-risk check settles that half without touching
> anything:** **EVERSPACE 1** (`RSG-Win64-Shipping.exe`) statically imports `version.dll` with the
> *same three-function shape*, has our `version.dll` deployed, and its `init-0.log` carries
> `DllMain ProxyStart: proxy DLL mode` + `[PROXY] Loaded real version.dll`. **Our proxy loads there.**
> `EVERSPACE 2` shows the same. And none of `version` / `winmm` / `dxgi` / `dinput8` is in
> `HKLM\…\Session Manager\KnownDLLs` (38 entries enumerated), re-confirming the row's own
> refutation of that mechanism.
>
> ⇒ **A static import is neither necessary nor sufficient for a bypass.** It true-positives on
> OCTOPATH and false-positives 4 of 4 elsewhere, so the screening is correct to be worded as a
> heuristic and **the `Loaded?` signal remains the per-game source of truth** — which is exactly
> what the shipped fix claims. Whatever bypasses OCTOPATH is title-specific and still unexplained;
> `KnownDLLs` is ruled out, and the load-order story remains a hypothesis.
>
> ### One inference, named rather than hidden
>
> Assertion 7 is **inferred**: the folder's absence is the *sole* condition under which
> `ClassifyLoad` returns `not observed`, but the panel was not re-read in that state before the
> restore. The column's `not observed` **rendering** is separately evidenced by a real
> never-launched title (`Echoes of Aincrad Demo`, in `[PROXYLOAD-UI-2026-08-20]` above). The two
> together give the row's claim; neither alone does.
>
> Also not obtained: a **rendered** title-screen screenshot. `request_access` for the shipping exe
> was declined, so the boot witness is process-level (titled window + 128 modules + 1.35 GB, stable)
> rather than visual. That is enough to exclude "never booted", which is the only thing it was for.
>
> ### Restore — verified, not assumed
>
> `RESTORED EXACTLY: True`. `winmm.dll` back at sha `823d02b358504e48` **and its original mtime**
> `2026-08-19 18:52:33`; the run's `version.dll` removed. **Restoring from `dist/proxy/winmm.dll`
> would NOT have worked** — same size, different sha (`6f13d87c…`, the 2026-08-20 rebuild), so only
> a byte backup of the original could put it back.
>
> `ui-options.json` restored byte-identically too, which matters more than it looks: **the deploy
> wrote `lastManualProxyByGame["OCTOPATH TRAVELER"]`, and `Recommend` checks LKG *before* the import
> heuristic — so the Suggested cell flipped to `version.dll · last used` and would have stayed that
> way forever, making step 1 un-re-runnable on this machine.** Verified reverted: the OCTOPATH LKG
> entry is gone, `selectedProxyType` is back to `2` (dxgi), and `confirmedProxyByExe` gained
> nothing — confirming the deliberate "never press Connect" rule held (a live pipe session past the
> dwell would have written it).
>
> ⚠ **Two hazards avoided, worth carrying forward.** The panel's **Undeploy** was *not* used:
> `UndeployAsync` sweeps every one of `AllProxyDllNames()` and calls `File.Delete` — a hard unlink,
> **not** the Recycle Bin. And the radio started on **dxgi**, which OCTOPATH also imports —
> [Sein.cpp:134](dll/src/Sein.cpp:134) records that hijacking dxgi on *this very title* faulted
> inside `__tzset` because dxgi loads before d3d11, so a Deploy pressed without moving the radio
> would have dropped a known-crashing proxy into the folder.
>
> Rigs: `tools/verify/octopath_proxy_swap.py` (hash-verified backup/restore, refuses to re-backup
> over the original) and `tools/verify/octopath_step1_rig.py` (baseline · holdback · park-logs ·
> **modules** · newlogs), both committed.

### ✅ VERIFIED 2026-08-20 `[SLOTSYM-2026-08-18]` — the slot `[DISABLE]` now actually unregisters, and says so honestly

> ### ✅ STEP 3 (THE NON-REGRESSION) PASSES 12/12 2026-08-20 `[SLOTSYM-GWORLD-2026-08-20]`
>
> Steps 1–2 were closed under `[SLOTSYM-LUA-2026-08-20]` on the **Get GameEngine** record — the one
> the defect was in. Step 3 asks the opposite question about **Get GWorld**, which always unregistered
> correctly: *did fixing the broken end break the working one?*
>
> ⭐ **That is not a formality here, and the reason is in the fix itself.** Both ends were moved onto
> the **same** shared emitters (`CeLuaHygiene.AppendSlotSymbolRegister` / `AppendSlotSymbolRelease`)
> precisely so they cannot drift — which is exactly what turns one regression into two. The working
> end had to be re-run.
>
> New rig `scripts/tests/slotsym_gworld_test.lua`, same method as its sibling (working-lessons §2.5):
> the **script the shipping UI emitted today**, captured from Teleport → Global Pointers →
> **Get GWorld** with AOBMaker offline, `<AssemblerScript>` extracted to
> `out/slotsym/get_gworld.lua.txt` (8,150 chars), then both `{$lua}` blocks executed over stubbed CE
> globals.
>
> | case | result |
> |---|---|
> | **1. enable → disable** | `UE_GWorld` registered, refcount 1; **one** DISABLE leaves `getAddressSafe('UE_GWorld')` **nil** and logs `UE_GWorld unregistered` |
> | **2. two ticked records** | refcount 2; the first DISABLE **keeps** the symbol and says `still held by 1 other record(s) -- left registered`; the second releases it |
> | **3. HONESTY (unregister neutered)** | reports `could NOT be unregistered after 8 attempt(s)`, does **not** claim success, and the retry loop is **bounded at 8** |
>
> **12 checks, 0 failures** — and the whole Lua suite is green alongside it: `contract_check` 15,
> `dissect` 83, `dll_size_text` 9, `freeze_helper` 154, `invoke_helper` 91, `slotsym_release` 12,
> `slotsym_gworld` 12.


*Was: on the `&GEngine` SLOT path the "Get GameEngine" record took the `mayFallBack` `[DISABLE]`
branch, where `unregisterSymbol` was nested inside the buffer-only `cur == mem` guard; with no
buffer, `mem` was nil, both arms were skipped, the symbol survived (a stale `UE_GameEngine` across a
game restart, resolving into the dead process's module base), and a trailing UNCONDITIONAL `dbg`
claimed it had been "unregistered". **Mechanism was read from the code, not either of the register's
two guesses:** it is NEITHER (a) a `getAddressSafe(sym)` guard returning falsy NOR (b) a
double-registration — ENABLE does a single `registerSymbol` on op 2, which matches the observed "one
manual `unregisterSymbol` sufficed". The register's `:256-258` cite was the GWorld branch (which
already unregistered correctly); the real code was the `mayFallBack` branch. Now: both slot ends
(GWorld + the GameEngine slot sub-path) go through shared
`CeLuaHygiene.AppendSlotSymbolRegister`/`AppendSlotSymbolRelease` emitters, so they cannot drift — a
per-symbol reference count in a CE Lua global keeps the symbol for a second still-ticked record, the
last holder unregisters it in a bounded loop, and the message re-reads `getAddressSafe` AFTER the
unregister so it claims success only when the symbol is actually gone. Also removed an accidental
duplicate `AppendContractCheck` (the block was emitted twice). Pinned by 6 new tests in
`PointerQueryScriptGeneratorTests` + a real-`lua` runtime simulation of the enable/disable sequence
(both cases below passed). Generator-only; contract surface untouched.*

> Needs a game whose `&GEngine` AOB validates so the record takes the SLOT path (DumperTest does).
> `UE5_DEBUG=1` in CE's Lua console to see the `dbg` lines.
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> ### ✅ THE RELEASE LOGIC PASSES 12/12 UNDER REAL `lua` 2026-08-20 `[SLOTSYM-LUA-2026-08-20]`
>
> New rig `scripts/tests/slotsym_release_test.lua`. It runs **the script the shipping UI emitted
> today**, not a fixture: captured from Teleport → Global Pointers → **Get GameEngine** with AOBMaker
> offline (which copies the CE-XML), `<AssemblerScript>` extracted to
> `out/slotsym/get_gameengine.lua.txt`, then both `{$lua}` blocks executed over stubbed CE globals
> with the mailbox faked so ENABLE takes the **SLOT** branch — the branch that was broken.
>
> | case | result |
> |---|---|
> | **1. enable → disable** | ENABLE registers the slot address with **no buffer** and refcount 1; **one** DISABLE leaves `getAddressSafe('UE_GameEngine')` **nil** — no manual `unregisterSymbol` — and reports `UE_GameEngine unregistered` |
> | **2. two ticked records** | refcount reaches 2; the first DISABLE **keeps** the symbol and says `still held by 1 other record(s) -- left registered`; the second releases it |
> | **3. HONESTY (unregister neutered)** | the symbol still resolves, and the script says **`could NOT be unregistered after 8 attempt(s)`** — it does **not** print `UE_GameEngine unregistered`, and the retry loop is **bounded at 8** |
>
> ⭐ **Case 3 is the one that matters most.** The original defect was not only that the symbol
> survived — it was that a trailing *unconditional* `dbg` claimed it had been unregistered. The
> emitted script now re-reads `getAddressSafe` **after** the attempt and picks the message from
> that, so a failure cannot be reported as a success. Neutering `unregisterSymbol` proves the
> honesty branch is reachable and correct, which no amount of reading the source establishes.
>
> ⚠ **Scope:** CE's globals are stubbed, so this does not exercise Cheat Engine's own
> register/unregister semantics. It does exercise the thing that was wrong — the script's control
> flow, where both arms used to be skipped when `mem` was nil on the slot path. Step 1's live
> `print(getAddressSafe('UE_GameEngine'))` in CE would add only that CE agrees with the stub.

> | 1 ⚠ THE ONE THAT MATTERS | tick the single "Get GameEngine" record, untick it, then in CE's Lua console `print(getAddressSafe('UE_GameEngine'))` | **nil on the FIRST call** (no manual `unregisterSymbol` needed); the `dbg` reads `UE_GameEngine unregistered` | before the fix it stayed `0x…` after untick and the log lied |
> | 2 | paste the CE-XML to make a SECOND "Get GameEngine" record, tick both, untick the OLDER, `print(getAddressSafe('UE_GameEngine'))` | still resolves (survivor keeps it); the older record's `dbg` reads `still held by 1 other record(s) -- left registered`. Untick the second → now nil | the refcount half — two records resolve the IDENTICAL slot, so an address marker cannot tell them apart |
> | 3 ⚠ NON-REGRESSION | GWorld: tick "Get GWorld", untick, `print(getAddressSafe('UE_GWorld'))` | nil after untick | GWorld already unregistered before; must still |

-----

### ✅ VERIFIED 2026-08-19 `[PIPEBUSY-2026-08-18]` — all three steps pass; — at-capacity logs ONCE, not an ERROR every second

*Was: at capacity (`kMaxPipeInstances=3`, UI holds 2 lanes) the accept loop's `CreateNamedPipe` fails
with `ERROR_PIPE_BUSY` (err=231) every second and logged `LOG_ERROR("PipeServer: CreateNamedPipe
failed …")` each time — **measured 1,826 ERROR lines in ~31.5 min on one Avowed session**, evicting
real diagnostics as the 8 MB pipe log rotated, and naming the wrong thing (busy ≠ broken). Now: a new
pure `Voll` policy (`dll/src/Voll.h`, header-only, Frieren roster) special-cases `ERROR_PIPE_BUSY` —
the accept loop logs **one INFO on the transition INTO** at-capacity ("all 3 pipe instances in use,
waiting for a free slot"), stays silent while it holds, and logs **one INFO on recovery** ("a pipe
slot freed, resuming accept"). Any OTHER errno still `LOG_ERROR`s every time — the capacity latch
never suppresses it. The retry/sleep is unchanged (it was correct), and `kMaxPipeInstances` is
unchanged (raising it would just move the spam to 4 clients). The state machine is unit-pinned in
`dll_helpers_test` (`Test_Voll_CapacityLoggingPolicy`, incl. the adversarial "a different-errno
failure during at-capacity still ERRORs and does not swallow recovery"). This is a DLL/pipe log fix,
NOT a mailbox-contract change.*

> ⚠ Reproducing this NEEDS a 3rd pipe client alongside the UI, which the register otherwise forbids
> ("never run `pipe_client.py` while the UI is connected", `[PIPEBUSY]`). That rule still stands as
> operational hygiene; the point of THIS check is only to observe that when it DOES happen the log is
> no longer a 1 Hz ERROR storm. Read the **pipe** log (`Logs/<game>/pipe-0.log`).
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> ### ✅ ALL THREE STEPS PASS 2026-08-19 `[PIPEBUSY-CAP-2026-08-19]` — and with NO UI involved
>
> `tools/verify/pipebusy_capacity.py` opens **three raw connections itself**, which fills the pool
> exactly as the UI's two lanes plus a client would — so the forbidden "pipe_client beside the UI"
> combination never had to be staged at all. DumperTest Development, dist 3263.
>
> * **Step 1 — PASS.** 75 s at capacity produced **exactly ONE** line:
>   `PipeServer: all 3 pipe instances in use, waiting for a free slot`, and **zero**
>   `CreateNamedPipe failed`. The pre-fix behaviour was ~1 ERROR/s, so the same window would have
>   yielded roughly **75** of them. *Exactly one* is the assertion — the defect was repetition, so
>   "at least one" would not have distinguished fixed from broken.
> * **Step 2 — PASS.** Releasing the clients produced **exactly ONE**
>   `PipeServer: a pipe slot freed, resuming accept`, i.e. the latch resets rather than sticking.
> * **Step 3 (NON-REGRESSION) — PASS, and broadly.** **23** other per-game log folders from this
>   machine were checked and **none** contains an at-capacity line. So "it only fires when actually
>   at capacity" is measured across 23 real sessions rather than asserted from one.

> | 1 ⚠ THE ONE THAT MATTERS | with the UI connected (2 lanes), start ONE extra `tools/verify/pipe_client.py` so the pool fills, leave it a minute, read `pipe-0.log` | **exactly ONE** `all 3 pipe instances in use, waiting for a free slot` INFO line — NOT `CreateNamedPipe failed` repeating once a second | before the fix this was ~1 ERROR/s forever (1,826 in 31 min) |
> | 2 | kill the extra client, watch the log | one `a pipe slot freed, resuming accept` INFO line, then normal `Waiting for client connection...` | proves the recovery transition fires exactly once |
> | 3 ⚠ NON-REGRESSION | during a normal single-UI session, grep `pipe-0.log` for `all 3 pipe instances` | absent | proves the at-capacity line only appears when actually at capacity |

-----

### ✅ BOTH DLL CLAMPS LIVE-VERIFIED ON A COMMERCIAL GAME 2026-08-21 `[CAPCLAMP-LIVE-2026-08-21]`

The `[PROPSEARCHCAP]` and `[CLASSCAP]` server-side clamps were pinned by tests that *read the
handler source*, and verified over the pipe against DumperTest. Here they are against a real title:
**Lushfoil Photography Sim, UE 5.6, 4,324 classes**, with **build 3308 injected directly** (its
deployed `version.dll` proxy did not load this run — see the note below — so `tools/verify/inject.py`
put the current DLL in, which is stronger evidence than a stale proxy would have been).

`list_classes(game_only=false)` — `[CLASSCAP]`:

| limit sent | rows | `total_classes` | `truncated` |
|---|---|---|---|
| omitted | 4,324 | 4,324 | false |
| **0** | **1** | 4,324 | true |
| **-5** | **1** | 4,324 | true |
| 1 | 1 | 4,324 | true |
| 3 | 3 | 4,324 | true |
| 50 | 50 | 4,324 | true |
| 999999 | 4,324 | 4,324 | false |

`search_properties` — `[PROPSEARCHCAP]`: omitted → **200** (the wire default, preserved), `0` → 1,
`-5` → 1, `1` → 1, `25` → 25.

⭐ **The `limit 3` row is an unplanned third-title proof of `[CLASSTOTAL-2026-08-18]`, and a sharper
one than either planned step.** Before that fix `totalClasses` was counted *inside* the capped loop
and so could never exceed the cap — at `limit 3` it would have read **3**. It reads **4,324**. The
two planned steps used caps of 5,000 against pools of 5,102 and 7,409, a 2 % and 45 % gap; this is a
**1,441×** gap, which no off-by-something could survive.

⚠ **Incidental, and worth a look later**: Lushfoil's deployed `version.dll` proxy (build 3262,
2026-08-19) **did not load** on this launch — `init-0.log`'s newest entry was still 2026-08-20, and
`inject.py` reported no stale module mapped, so nothing of ours was in the process. The same proxy
worked on 2026-08-20. Not chased here because injecting the current DLL was the better fixture
anyway, but "a deployed proxy silently not loading" is the exact shape `[PROXYLOAD]` is about.

### ✅ THE `[FREEZEUNTICK]` PAIR IS CLOSED 2026-08-22 `[UNTICKPAIR-2026-08-22]` — both generators, one CE session

`[FREEZEUNTICK-2026-08-20]` was the same defect in **two different generators**: a bail-out that
applied nothing left the CE record ticked, so the table said a cheat was running when none was. The
fix went into the **shared emitter**, so the only way to show it landed there rather than in
`FreezeScriptGenerator` alone is to run both. Both were run, back to back, CE 7.7 + DumperTest,
`dist` v1.0.0.3313. **`Active` was read from CE's Lua Engine every time, never from the checkbox
icon.**

**Y10 step 3 — the baked-invoke generator. Was 3 of 4 on 2026-08-20; now 4 of 4.**
Subject `KismetMathLibrary::MakeTransform`, Verify return ticked, pushed via AOBMaker, helper
injected. Ticked once against DumperTest — worked, and the Y13 half reproduced exactly:
`[Invoke] OK: … ReturnValue (fstruct@80, size=96B) -- complex return; see After: dump above`, with
`11 / 22 / 33` decodable in the After dump. Then CE was re-attached to a **sacrificial `python.exe`**
and the record ticked again:

| assertion | 2026-08-20 | now |
|---|---|---|
| the contract check fires FIRST | ✅ | ✅ |
| its message names `g_mailboxContract` | ✅ | ✅ verbatim, plus three ranked causes |
| **no `writeByte` may have run** | ✅ | ✅ — no second `[Invoke] Before:` dump appeared |
| **the record unticks itself** | ❌ `Active=true` | ✅ **`AFTER-CONTRACT-FAIL ACTIVE=false`** |

**AA12/AA13 step 2 — the freeze generator. Was "message half passes, untick half fails"; now both.**
Staged exactly as the row asks: script and helper created **while injected**, DumperTest killed and
relaunched **without** injection, CE re-attached, same record ticked.

| the row expects | result |
|---|---|
| a `showMessage` naming the reason | ✅ `[Freeze] nothing was frozen:` / `[ue5_freeze] g_invokeMailbox symbol not found -- is UE5Dumper.dll injected?` |
| **the record unticks itself** | ✅ **`1 ACTIVE=false`** (was `true`) |

⭐ **The pair is the evidence.** Either alone would be consistent with a fix in one generator; both,
from one session, is what shows the shared emitter carries it.

ℹ️ Incidental: `Tools → Inject Freeze Helper` reported **`Inject freeze helper OK`** with no
`Stream size mismatch` — `[FREEZEINJECT-CRLF-2026-08-20]` confirmed fixed from the other side.
ℹ️ Also incidental, and it shows the contract message is honest: re-injecting the DLL into a process
CE already had open did **not** make the freeze work — the message's own first listed cause is *"CE
having opened the process BEFORE the DLL was injected"*. Re-attaching CE fixed it.

-----

### ✅ FOUND + FIXED 2026-08-22 `[FREEZECFGNAME-2026-08-22]` — the freeze script tells you to edit `className`, then reports the one you replaced

⭐ **Found by running AA12/AA13 step 4, which is the step that asks you to edit the CFG.** The step
passes on every clause it states — and while it did, the message said something false.

`FreezeScriptGenerator` baked `p.ClassName` — the class at **generation** time — into the runtime
messages, while the freeze itself reads `CFG.className` at **run** time. Two sources for one fact.

**Measured in CE**, DumperTest injected, `CFG.className` edited from `DumperTestActor` to
`NoSuchClass_ZZZ` (one `gsub`, verified by reading the record's `Script` back):

```
[Freeze] armed: no live instances of DumperTestActor (or any subclass) right now
                                     ^^^^^^^^^^^^^^^^ names a class with plenty of live instances
```

The **behaviour** was right — it found 0 instances, because it looked up `NoSuchClass_ZZZ`. Only the
**report** was wrong. That is the same split as `[SOLIDEHELD]`, `[CADENCEGAP]` and `[PARAMSSORT]`,
and audit #4's named root cause, now in a fifth place.

⚠ **What makes it worse than a cosmetic slip: the product instructs the edit.** The Freeze dialog's
inherited-field warning says *"To target a single class, edit className in the generated CFG block"*,
and the script's own cap message says *"Narrow className in CFG to cover the ones you want."* Anyone
who follows either instruction gets messages naming the class they replaced.

**Fixed** at the three `[ENABLE]` sites (`FreezeScriptGenerator.cs:134/143/150`) to read
`tostring(CFG.className)`. ⚠ `[DISABLE]`'s Stopped line still bakes the name **and must** — it is a
separate Lua chunk with no `CFG` in scope, and it is `dbg`-gated.

⭐⭐ **AND THE DEFECT WAS PINNED BY A TEST, which is why it looked deliberate.**
`Generate_ArmedButEmpty_DoesNotUntick_AndKeepsTheWindowOpen` asserted
`"[Freeze] armed: no live instances of BP_Teammate_C"` — the baked literal. The class name was
**incidental to what that test is about** (that the armed branch exists and does not untick); it was
just a convenient anchor. Using the defect as an anchor is what made it look intentional, and it is
why the fix showed up first as a *failing existing test*. The anchor is now the phrase without the
name, with a comment saying why.

New guard: `Generate_ScopeMessages_ReadCfgClassName_NotTheGenerationTimeName` — every scope message
must contain `CFG.className` and must NOT contain the literal, **with the CFG line itself as the
control** (the literal belongs there and nowhere else, so a fix that deleted the name everywhere
fails). Negative control run and reverted byte-exact: re-baking the literal at one site fails
exactly that test. **4,648 / 4,648 pass.**

-----

### ✅ AA12/AA13 CLOSED 2026-08-22 `[AA12-STEP3-2026-08-22]` — step 3, and with it the whole row

The last step: freeze a class with **zero live instances**, then make one spawn, and the freeze must
take hold within ~5 s. The record must stay ticked throughout — the row calls an auto-untick here an
outright FAIL, because arming ahead of a spawn is the feature's advertised purpose.

⚠ **The fixture was the hard part, and three obvious routes were dead:**

| route | why not |
|---|---|
| `Summon <Class>` | **there is no `Summon`** in DumperTest's 9,806 UFunctions — checked, not assumed |
| a native `Spawn*` UFunction to invoke | **zero** with ≤ 4 parameters |
| `TargetPoint` / `Note` / `TriggerBox` / `AmbientSound` … | every "instance" `find_instances` reported is a `Default__` **CDO**, so they qualify as empty — but nothing can spawn one |

⭐ **What worked: a class the engine creates LAZILY, on a command the UI already has a button for.**
`ADebugCameraController` is instantiated by the PlayerController the first time the debug camera is
toggled. Before that it has **0 non-CDO instances** — and it owns four floats of its own.
`CheatManager` was the first candidate and is *not* usable: it already had a live instance and
exposes no numeric `UPROPERTY` at all.

**Fixture**: `DebugCameraController::SpeedScale`, `FloatProperty`, `0x9C8`.
⭐ **Its Property Search preview reads `1 (CDO default)` — that annotation IS the control.** It says
in the UI's own words that the value came from the CDO because no instance exists, so the frozen
value cannot be something that was already there.

**Run** (CE 7.7 + DumperTest, `dist` v1.0.0.3314):

| # | what | result |
|---|---|---|
| 1 | Freeze `SpeedScale = 12345`, push to CE, tick | `[Freeze] armed: no live instances of DebugCameraController (or any subclass) right now -- the freeze applies as they spawn.` — **and nothing else** |
| 2 | the Lua window | stayed **open** (the close is gated on `scount ~= 0`) |
| 3 | `ARMED-STATE ACTIVE` | **`true`** — read from the Lua Engine, not the icon |
| 4 | Console → Debug Camera → **Force On** | `✓ Debug Camera forced ON.`, strip reads **ON** |
| 5 | re-run the Property Search | preview `1 (CDO default)` → **`12345`** |
| 6 | `AFTER-SPAWN ACTIVE` | **`true`** — still ticked, still one line of output |

⭐ The before/after pair on one field is what makes it decisive: the same row, the same offset, read
by the same panel, going from "there is no instance, here is the class default" to the frozen value —
which can only happen if an instance appeared *and* the armed freeze reached it.

**The row is now complete**: step 1 (`[AA12-STEP1]`), 2 (`[UNTICKPAIR]`), 3 (here), 4 (which also
produced `[FREEZECFGNAME-2026-08-22]`), 5 (`[AA12-STEP5]`), 6 (`[AA12-STEP6]`), plus the bail-out
half (`[AA12-BAILOUT]`).

ℹ️ Incidental, and worth keeping for the next fixture hunt: **`find_instances` counts CDOs**, so a
class showing 1-3 "instances" may well have none that a freeze would touch — the freeze's own walk
skips them. Filter on the `Default__` name prefix before concluding a class is populated.

### ✅ AA12/AA13 step 5 PASSES 2026-08-22 `[AA12-STEP5-2026-08-22]` — the old-helper gate, seen in CE without rebuilding one

The row asks for a **pre-1.2** `ue5_freeze_helper.lua` embedded in the table. The helper ships as an
**EmbeddedResource** (`UE5DumpUI.csproj:150`), so genuinely embedding an old one means a rebuild and
a republish, then another to undo — and it risks leaving `dist` shipping a 1.1 helper.

⭐ **It did not need one, because the gate does not look at a version.** `FreezeScriptGenerator.cs:113`
is `if sok2 == nil then`, where `sok2` is the second value of `pcall(handleOrErr.start)`. There is no
version-string check anywhere: the gate's entire premise is *"a helper at ≤ 1.1 returns nothing from
`start()`"*.

**That premise was verified against the real historical file**, not assumed —
`git show 04d40803^:scripts/ue5_freeze_helper.lua` (the commit before *"report an OUTCOME from
start()"*) declares `UE5_FREEZE_HELPER_VERSION = '1.1'` and its `handle.start = function() … end`
has **no `return` statement** at all.

So the condition was staged directly, in CE's own Lua state, against the real generated script:

```lua
local real = freezeProperty
freezeProperty = function(cfg)
  local h = real(cfg); local rs = h.start
  h.start = function() rs() end      -- swallow the returns: exactly what <=1.1 does
  return h
end
```

⚠ Order matters and cost me a run: the helper is a table FILE, so `freezeProperty` does not exist
until the script has been enabled once. Wrapping too early wraps `nil` — the first attempt printed
`freezeProperty=nil version=nil` and installed a broken global that had to be cleared. Tick once,
untick, then wrap; the second attempt read `freezeProperty=function ver=1.4`.

**Result, ticking the real record with the simulation in place:**

```
[Freeze] this table has an older ue5_freeze_helper.lua: it cannot report whether anything
was frozen. Re-inject it via UE5DumpUI -> Tools -> Inject Freeze Helper into Current CE Table.
RECORD ACTIVE=true
```

| the row expects | result |
|---|---|
| "older ue5_freeze_helper.lua … re-inject it" | ✅ verbatim |
| the Lua window stays open | ✅ no auto-close — the close is gated on `sok2 == true` |
| the record stays ticked | ✅ `ACTIVE=true`, read from the Lua Engine |

⚠ **Scope, stated rather than glossed**: this staged the *condition the gate detects*, not a
genuinely old file. That is exact for what the code does — there is no version check — and the
premise linking the two was checked against the real 1.1 source. It does **not** exercise anything
else a 1.1 helper would do differently, and nothing in this branch depends on that.

ℹ️ Three independent pieces already covered the rest: the generated branch is unit-tested
(`FreezeScriptGeneratorTests.cs:612/:619`), and the CURRENT helper's `start()` returning
`(ok, err, count)` is covered by the executable Lua rig (`scripts/tests/freeze_helper_test.lua`,
AA12/AA13 cases). Together with this run, the branch is shown reachable with an old helper and
unreachable with the current one.

▶ **Step 3 is the last one open in this row** — freeze a class with 0 live instances, then make one
spawn, and the freeze must take hold within ~5 s. It needs a spawnable class on DumperTest;
CheatManager's `Summon` exec is the likely route.

### ✅ AA12/AA13 step 4 PASSES 2026-08-22 — a bogus `className` is armed, not called a typo

Same session, `CFG.className = 'NoSuchClass_ZZZ'`:

| the row expects | result |
|---|---|
| behaves exactly like step 3 (armed, 0) | ✅ `[Freeze] armed: … right now -- the freeze applies as they spawn.` |
| must NOT claim it is a spelling mistake | ✅ it does not |
| the record stays ticked | ✅ `1 ACTIVE=true` |
| the Lua window stays open | ✅ no `showMessage`, window up |

⚠ It passed **while printing the wrong class name** — see `[FREEZECFGNAME-2026-08-22]` above. A row
can pass every clause it states and still be watching something broken; that is why the step was
worth running rather than reasoning about.

### ✅ Y10 / Y13 CLOSED 2026-08-22 `[Y10-STEP4-2026-08-22]` — step 4, and with it the whole row

Step 4 is the **control**: run Verify against a UFunction whose `parmsSize` is larger than the
mailbox's 1024-byte `paramsData`, and show the pre-zero loop does not write past it. Before the fix
the loop used `parmsSize` raw, so it would have scribbled over `cmdFlags`, `cmdOutFlags` and the
live globals sitting past the struct end.

**Fixture** `SequenceCameraShakeTestUtil::GetCameraCachePOV` — `ParmsSize` **2064**, static, 2 params
(the size is a by-value `FMinimalViewInfo`), return `fstruct@16, size=2048B`.
Arithmetic: `0x328 + 2064 = 0xB38`, so an unclamped loop zeroes exactly **`0x728..0xB38` = 1040
bytes** — a region that held 31 non-zero bytes including two live pointers.

⭐ **Both controls were run, which is what makes the null result mean anything.**

| | measurement |
|---|---|
| baseline | two reads 12 s apart, no invoke: **0 bytes of natural churn**, 31 non-zero of 1040 |
| **positive control** | filled `0x328..0x727` with `0xCC` → the reader saw **1024 / 1024**; after the tick, **0 / 1024**. The loop demonstrably ran and the reader demonstrably sees it. |
| the claim | tail `0x728..0xB38`: **0 bytes changed**, 31 non-zero preserved |

Without the positive control the "0 changed" reading is equally consistent with the script never
running — which is exactly what happened on the first attempt (see below), so this is not a
hypothetical.

⭐ **A second, independent layer turned up that the row did not predict**: the helper refuses the
call outright rather than merely clamping the zeroing —
`[Invoke] FAILED: SequenceCameraShakeTestUtil::GetCameraCachePOV -- parmsSize 2064 out of range (0..1024)`,
with a matching `showMessage`, and `AFTER-FAIL ACTIVE = false` (read from the Lua Engine) so the
failed invoke unticks its own record. The artifact-level witness agrees: the emitted script reads
`for i = 0, 1024 - 1 do writeByte(_PD_dbg + i, 0) end`, not `2064`.

**Steps 1 and 2 are also satisfied, both live:**

- **Step 2** — verbatim, in this run: the hint read *"complex return at +16, past the 256-byte dump
  window above -- read it in CE's memory viewer at g_invokeMailbox + 0x328"*, i.e. it named the
  offset instead of pointing at a dump that cannot hold the value.
- **Step 1** — the `KismetMathLibrary::MakeTransform` run recorded under `[UNTICKPAIR-2026-08-22]`:
  return `fstruct@80, size=96B`, so the window widened to 176 (the old flat 32 stopped 48 bytes
  short of the return's first byte) and `11 / 22 / 33` were decodable **in the After dump** — the
  coverage claim, witnessed rather than asserted.

Step 3 closed earlier the same day (`[UNTICKPAIR-2026-08-22]`, 4 of 4). **The row is complete.**

-----

### ✅ FOUND + FIXED 2026-08-22 `[INVOKEHINTQUOTE-2026-08-22]` — the baked invoke script did not parse, and CE said nothing

⭐ **Found by running Y10 step 4, and it is the reason the first attempt measured nothing.**

`BakedScriptGenerator` interpolated the step-2 hint into a single-quoted Lua literal **without
`EscapeLua`**, while every other interpolated piece on the same line went through it. The
does-not-fit branch of that hint contains an apostrophe — *"read it in CE's memory viewer"* — which
closes the string early and makes the **whole `[ENABLE]` block** a syntax error:

```
Lua error in the script at line 2:119: ')' expected near 's'
```

⚠⚠ **Cheat Engine does not report this.** Ticking the record leaves `Active` at `false` with **no
dialog, no output and nothing in the log**. The user sees a checkbox that will not stay ticked.
That is also what fooled the first pass here: the memory diff came back "0 bytes changed" and looked
like a clean PASS, when in fact the script had never executed. It took `autoAssemble` on the
record's own `[ENABLE]` text to surface the error.

**Blast radius**: any invoke whose complex return does not fit the dump window — i.e. any large
by-value struct return. Not exotic.

⭐ **The branch was introduced by audit #5 Y13**, the fix that made the hint honest about whether the
dump can hold the value, and **Y13's own test generates this exact broken script and passes on it**
(`Y13_ComplexReturnHint_OnlyClaimsTheDumpWhenItReallyHoldsIt`) — every substring it looks for is
present. Nothing in 4,648 tests asked whether the emitted Lua compiles.

**Fixed**: `{EscapeLua(hint)}` at `BakedScriptGenerator.cs:355`.

**New guard `CeLuaQuotingTests`** — a Lua scanner reporting lines where a quoted string is left open,
skipping `--` comments and long brackets. ⚠ **It cannot be a naive quote count**: the generators
deliberately emit `-- ... when CE's resolver ...` in comments, which is not a defect. The scanner
was written against the **real broken artifact** first and shown to both fire (1 line, the print)
and clear (0 after escaping that one apostrophe); **both directions are pinned as tests**.

⭐ **Coverage is behavioural, not textual, on purpose.** The defect came from an interpolated
variable defined ten lines above its use, so a grep for an apostrophe on the emitting line
structurally cannot find its siblings — the first grep I ran came back empty and would have been
mistaken for a clean sweep. Running them is the only measurement: the guard now generates **17 other
generators + all 13 teleport actions + the freeze generator**, including a class name, a property
name and a DLL path carrying apostrophes (a Windows account named `O'Brien`). All parse — the baked
hint really was the only site.

Negative control: reverting the fix fails exactly 3 tests; restored byte-exact. **4,689 / 4,689.**

### ✅ MB3 CLOSED 2026-08-22 `[MB3-CT-2026-08-22]` — the CE half, through real `.CT` records

`[MB3-POKE-2026-08-19]` closed steps 2 and 3 headlessly (50 consecutive dispatches, 0 failures).
What was left was the row's actual worry: **the refactor touches the polling loop every `.CT`
command goes through, and no test target compiles `Mimic.cpp`**, so the general path had never been
exercised the way a user exercises it. Step 4's exception path cannot be triggered on purpose and
stays a watch item, not a step.

Run on CE 7.7 + a freshly relaunched, freshly injected DumperTest (`dist` v1.0.0.3314), CE
re-attached to the new PID and `reinitializeSymbolhandler()`d — `g_invokeMailbox` and
`g_mailboxContract` both resolved before anything was ticked.

⭐ **Every claim below is an EFFECT, not a return code.** A mailbox command that reports success and
does nothing is precisely the failure this row exists to catch, so the witness is the pawn's pose
read back through a different code path (the UI's pipe) between records.

| # | `.CT` record | witness |
|---|---|---|
| 1 | `Teleport: Save marker 1` | quiet (correct: `DEBUG=0`) |
| 2 | `Teleport: TP facing direction` | pose **900 → 1000**, X only, +100 uu on yaw 0. Log: `Teleport: relative 100.0 uu (horizontal) fwd=(1.000, 0.000, 0.000) -> (1000.0, 1110.0, 92.0)` |
| 3 | `Teleport: Recall marker 1` | pose back to **900.000 / 1110.000 / 92.013**, exactly. Log: `K2_SetActorLocation invoked OK ... -> (900.0, 1110.0, 92.0)`, `post-move cur=target`, `recall 0 -> rc=0` |
| 4 | `Invoke (baked): KismetMathLibrary::MakeTransform` | `[Invoke] OK: ... ReturnValue (fstruct@80, size=96B)`, and the After dump decodes `26 40` / `36 40` / `80 40 40` = **11.0 / 22.0 / 33.0** — the values typed into the dialog |

Record 3 is the one that matters: **Recall can only restore a pose that Save actually stored**, so
the pair proves both commands, and a quiet Save is not taken on trust.

**Step 2, re-confirmed from the CE side** (it had only been shown headlessly):
`Mailbox: tick threw` = **0** and `result=-11` = **0** across all eight logs, with
`Mailbox: INVOKE_BY_NAME complete, result=0` present.

ℹ️ Incidental, and it explains why all of this worked with the game window unfocused:
`Mailbox: INVOKE -> static-native fast path (flags=0x14822403, bypassing GameThreadDispatch)`.
The address list's own reminder row ("click back into the GAME window before ticking these") is
still right in general — the teleport records *do* go through the game thread — but a static native
UFunction does not.

⚠⚠ **A FIXTURE TRAP THAT COST A RESTART, worth writing down before it costs another.** The first
attempt used `Teleport: TP to coordinates` with the UI's default **0 / 0 / 0** as the displacement.
In `ThirdPersonMap` the origin is under the floor: the pawn fell, crossed KillZ and was
**destroyed**, and `Teleport: Recall marker 1` then returned `code -3` = `TP_ERR_NO_PAWN`. The error
was **completely honest** — the pawn really was null — but it reads exactly like "Save silently
failed", and I nearly filed it as one. **DumperTest does not respawn the pawn**, focused or not, so
the only way back is relaunch + re-inject + CE re-attach.
⭐ The general rule: **displace with `TP facing direction`, never with absolute coordinates**, when
the point of the step is the round trip rather than the coordinates.

### ✅ RETRACTED / RESCOPED 2026-08-22 `[AVOWEDCACHE-2026-08-22]` — the 504 is CORRECT; what is left is a small invariant

> ⛔ **I filed this as "the cache serves a WRONG UE version". That was wrong.** The isolating
> experiment the maintainer approved refuted it, and the retraction matters more than the original
> claim: a fix aimed at "make Avowed report 503" would have deleted working, deliberate behaviour.

**What the experiment did.** Backed up the machine hint file, deleted **only** the Avowed entry
(29 → 28 games, siblings verified intact), relaunched Avowed on build 3315 and re-scanned.
Result: **UE504 again** — so the cache was never the origin.

**Why 504 is right.** The DLL applies a deliberate *upward* correction, and it logged itself doing it:

```
[SCAN:Ver] DetectVersion: PE VERSIONINFO -> UE 5.3 -> 503        <- raw string detection
[INIT]     UE5_Init: property marker (CMC::GravityDirection)
           = UE5.4+ — raising version 503 -> 504.                <- structural correction
[INIT]     UE5_Init: Complete (UE504, ...)
```

`UCharacterMovementComponent::GravityDirection` is a reflected `FVector` added in **UE5.4**
(the post-DynOff correction in [Frieren.cpp](../dll/src/Frieren.cpp)). Avowed's binary carries it, so
504 is the honest answer for version-gated behaviour. ⭐ **`503` and `504` were never in conflict —
they are two different quantities**: the raw version string versus the structurally-corrected runtime
version. The docs saying "Avowed = UE 5.3" describe the engine release and do not contradict this.
That also explains the historical logs which showed both values side by side within one run.

**What actually remains — LOW, and it is an invariant, not a wrong value.** Frieren's own comment
states the design: *"Runtime-only: the cached RAW detection is left as-is and re-corrected here on
every init, so no cache delete is ever needed."* That invariant is **not upheld**, and a second
writer is why:

| time | writer | writes |
|---|---|---|
| `20:19:17.204` | DLL `Flamme::SaveResults` | `ueVersion: 503` — the raw detection, as designed |
| `20:19:17.878` | **UI `AobUsageService`** | `ueVersion: 504` — the CORRECTED runtime value; `scanCount` 1 → 2 |

⚠ The shared file is **deliberate, not a collision**: `AobUsageService` writes
`UE5CEDumper.{Machine}.json` in the app-data root — the same file `Flamme` owns — and its code says
so ("it must mirror it", "`VersionDetectRev` is DLL-authoritative — preserved here"). It mirrors
`EngineState.UEVersion`, which is necessarily the *corrected* value, because that is what the pipe
reports (`g_cachedUEVersion`). There is no 504 constant anywhere in the C#.

**Consequence, stated small because it is small:** the cache ends up holding the corrected value
flagged `versionDetected: true`, so the raw detection is lost. Runtime behaviour is unaffected — the
correction is idempotent and its `< 504` guard simply no-ops on the next launch. The only real cost
is that a later launch cannot tell "raw 504" from "raised to 504", which is exactly the distinction
the comment claims to preserve. ▶ **Decide whether the comment or the code is wrong; do not "fix"
the version.**

⛔⛔ **THIS WAS ALREADY DOCUMENTED AND I DID NOT LOOK.** The 2026-08-20 sweep in this same file
("**THREE DIFFERENT 'UE VERSION' QUANTITIES, and confusing them manufactures a G11 false alarm**")
names Avowed explicitly — *"cache 504 … vs detected 503 + documented raise — **not** a regression"* —
and even records that it "cost two contradictory readings of Avowed before it was pinned down". So
this is the **third** time the same confusion has been worked out from scratch. ▶ **Grep the docs
for the subject before filing a defect**; the answer was one `grep -n Avowed docs/todo.md` away.

⭐ **What the experiment nonetheless added, and it corrects the 2026-08-20 note.** That note explains
the cache's 504 as *"from an older run"* — i.e. leftover. It is not leftover: it is written **fresh,
every scan**, by the UI's `AobUsageService` 674 ms after the DLL wrote 503, as the table above shows
with both log lines. So the cache systematically holds the corrected value rather than the raw one,
which is what makes it contradict Frieren's comment. That mechanism was not previously identified.

ℹ️ Housekeeping from the experiment: the entry was re-created with `scanCount: 2` (it had been 5).
Harmless — it re-accumulates. A backup of the pre-experiment file is in the session scratchpad.

### ✅ FIXED 2026-08-23 (was NEW DEFECT 2026-08-22) `[TREERECLICK-2026-08-22]` — re-clicking the selected tree node is a no-op, and a handoff turns that into a stuck panel

Found while running **AE2/AE3 step 5** on DQ7R (`dist` AOT v1.0.0.3315, DLL 3315, UE427, 149,370
objects). The row's stated expectation — *select node P → push another class into Class/Struct via a
handoff → click P again → the panel reloads P* — **does not hold**.

**Reproduced twice, then isolated with a control, and confirmed on the wire.**

| # | action | `Pipe TX` | panel |
|---|---|---|---|
| 1 | `Classes` → filter `DOLLSoubiState` → **`Walk Class`** | `walk_class 0x28DF741CA00` | `DOLLSoubiState` (168) |
| 2 | click tree node **P** (`DOLLPlayerController`), which is still the highlighted row | ⚠ **nothing** | still `DOLLSoubiState` |
| 3 | click any *other* row | `get_object` + `walk_class 0x28DF73BF040` | recovers to `DOLLPlayerController` (2224) |

⭐ **The wire is the second witness, and it is what makes this unambiguous.** Between the handoff at
`19:26:15.620` and the recovery click at `19:27:00.173` the UI's `pipe-0.log` contains **no TX at
all** — the click did not merely fail to repaint, it never asked for anything.

**Isolating control (no handoff involved), same log:** three clicks — row 2 → `walk_class …9C0`
(`19:30:19`), row 1 → `walk_class …040` (`19:30:24`), row 1 **again** → **silence**. So the no-op is
plain `ListBox` semantics: clicking the already-selected item changes nothing, `SelectedNode` does
not change, `ObjectTreeViewModel.OnSelectedNodeChanged` never fires, and
`ClassStructViewModel.OnObjectSelected` is never called. The handoff is not the cause — it is only
what makes the no-op *visible*, by leaving a different class on screen.

⛔ **Do NOT fix this in `ClassStructViewModel` — that code is already correct, and I checked after
first assuming otherwise.** The obvious target is its `_shownNodeAddress` dedupe
([ClassStructViewModel.cs:358](ui/UE5DumpUI/ViewModels/ClassStructViewModel.cs:358)), and the
obvious story ("the handoff leaves the key naming P, so the re-click is deduped away") is **wrong**:
the cross-tab entry point already calls `BeginLoad(nodeAddr: null)`
([:250](ui/UE5DumpUI/ViewModels/ClassStructViewModel.cs:250)), which clears the key — that is
audit #5 **AE3's "third path"**, and it is precisely why step 3 of the table above recovers. Control
flow never reaches the dedupe. A fix aimed there would edit working code and change nothing.
*(working-lessons §2.4 — re-derive the premise, not just the location.)*

**Where a fix would go:** the view. Either handle item pointer-press on the tree so a click on the
already-selected node re-raises the load, or have the handoff clear the tree highlight so the UI
stops claiming P is selected while showing X.

**✅ FIXED 2026-08-23, build 3322 — by clearing the highlight, not by intercepting the click.**
`MainWindowViewModel.ShowClassInClassStructAsync(classAddr)` sets `ObjectTree.SelectedNode = null`
and then awaits the load; **all five** cross-tab handoffs (`:1132/:1216/:1270/:1430/:1816`) route
through it, and the command is now called from exactly one place.

⭐ **It fixes both halves with one write.** The tree stops asserting a selection that is not what is
on screen, *and* the next click on that node becomes a genuine change — so it loads. Order is
load-bearing: clear FIRST, because `OnObjectSelected(null)` returns at `ClassStructViewModel.cs:349`
before its first await and takes no load ticket, so it cannot supersede the load on the next line;
and if the load throws, the tree is left honestly unselected.

⛔ **The obvious fix — a pointer handler that re-raises the click — was designed, then rejected on
evidence.** Three independent designs were put through adversarial review; two converged on this one
and the third (a tunnel-phase `PointerPressed` handler) admitted its load-bearing premise, Avalonia's
tunnel-before-bubble ordering, **could not be verified from this tree**. Two further facts killed it
outright: re-raising `SelectionChanged(node)` lands back in `OnObjectSelected`, whose dedupe at
`ClassStructViewModel.cs:358` swallows it — so it has *exactly this fix's coverage* at far higher
risk — and bypassing that dedupe is pinned against by `RepeatedSelectionOfSameNode_WalksOnlyOnce`
(`ClassStructViewModelConcurrencyTests.cs:340`). The DataTemplate root is also a `StackPanel` with no
`Background`, which Avalonia does not hit-test, so the handler would have had dead zones.

⭐ **The test that matters is the wiring one, and its negative control was RUN.** Three tests pin the
*mechanism* (re-selecting the same node is silent; after a clear it fires; the clear is idempotent) —
but those are `[ObservableProperty]` semantics and would pass with or without the fix. The real
regression is a **sixth handoff added later that bypasses the helper**, so
`EveryCrossTabClassHandoff_GoesThroughTheHelperThatClearsTheTree` asserts on the source that exactly
one direct `LoadClassCommand.ExecuteAsync` remains and that the clear precedes the load. Simulating
that sixth site made it fail with the actionable message, then it was restored. Full suite green
(C++ 258 + 1644, C# all); `dist` republished AOT-trimmed at 54.7 MB.

✅ **LIVE-VERIFIED 2026-08-23 on DQ7R** (UE427, 149,370 objects, DLL + AOT `dist` both **3322**),
which also closes **AE2/AE3 step 5**:

| step | observed |
|---|---|
| select tree node `Actor` | panel shows `Actor` (544); tree row highlighted |
| `Classes` → `DOLLSoubiState` → **Walk Class** | panel shows `DOLLSoubiState` (**168**) **and the `Actor` highlight is GONE** — the tree no longer claims a selection that is not on screen |
| click `Actor` again | ✅ highlight returns **and the panel reloads `Actor` (544)** |

⭐ **The wire is the second witness, the same one that made the original finding unambiguous.**
`ui-pipe-0.log` now shows three TX where the defect showed a gap:

```
12:33:29  id=88  walk_class 0x1CCE36EB380   <- selecting Actor
12:35:16  id=89  walk_class 0x1CCE56CCA00   <- the DOLLSoubiState handoff
12:35:45  id=90  walk_class 0x1CCE36EB380   <- the re-click, SAME addr as id=88
```

id=90 is precisely the request that did not exist before (*"no TX at all"*), and its address being
identical to id=88 is what proves it re-walked the previously-selected node rather than something else.

ℹ️ Bookkeeping: this defect is **not** in `pending-verification_zh-TW.md` (grep = 0). Its 繁中 row is
todo.md's own AE2/AE3 step-5 cell.

**Severity: LOW–MED, and it is a design call, not an obvious bug.** The damage is a user-visible
disagreement between the tree highlight and the panel, fully recoverable by clicking any other row
and coming back. Whether a click on an already-selected item *should* force a reload is a UX
decision — most list UIs deliberately do nothing.

-----

### ✅ FIXED 2026-08-22 (was NEW DEFECT) 2026-08-22 `[FORCESTATUSCLIP-2026-08-22]` — the Force status line is right-clipped, and its tail is the part that matters

> **FIXED.** The row's diagnosis was right but its prescription would not have worked: the status
> line lived in a **horizontal `StackPanel`, which gives every child its DESIRED width**, so
> `TextTrimming` there is inert — nothing ever constrains the `TextBlock`, and the text is hard-cut
> at the panel edge with no ellipsis.
>
> The toolbar row is now a `DockPanel`: the fixed-width controls stay in a `Left`-docked
> `StackPanel`, `ClassFilterNote` docks `Right`, and **`StatusText` is the fill child** — so it
> receives exactly the leftover width and trims honestly at any window size. Added
> `TextTrimming="CharacterEllipsis"` (the truncation becomes visible) **and**
> `ToolTip.Tip="{Binding StatusText}"` (the tail becomes readable). Both are needed: the ellipsis
> only says *that* something was cut; the tooltip is what makes the cut clause recoverable — and the
> clause in question is the honesty one, `— cap reached, more exist unheld`.
>
> ⚠ The other long message matters as much and is longer: the `⏳ … ARMED but holding nothing yet —
> no live instance … It will apply automatically as soon as one spawns` explanation was clipped to
> roughly its first clause, i.e. the user saw "ARMED but holding nothing yet" and none of the part
> that says it is working as intended.
>
> Tag balance checked, `-Target UI` build SUCCESS, **4,693 C# tests pass**.

`PropertySearchPanel.axaml:55` puts `StatusText` in a horizontal `StackPanel` with **no
`TextTrimming`, no `TextWrapping` and no `ToolTip.Tip`**. A `StackPanel` hands a child its desired
width and clips at the panel edge, so a long status is cut with no ellipsis and no way to read the
rest.

Measured on a maximised window at 1389×868: about 30 characters survive —
`✓ Holding Actor::bIsEditorOnly` — cut mid-token off a property actually named
`bIsEditorOnlyActor`.

⚠ **Why it is not merely cosmetic here.** The longest message the panel produces is exactly the one
whose ending carries information nothing else states in words:

```csharp
// PropertySearchViewModel.cs:502-507
// A capped pool makes Held a floor, not a total. Say so — otherwise
// "on 256 instance(s)" reads as "all of them" when it means "the first
// 256 we walked, in ascending GObjects order, and there are more".
StatusText = r.Truncated
    ? $"✓ Holding {..} = {what} on {r.Held} instance(s) — cap reached, more exist unheld."
    : $"✓ Holding {..} = {what} on {r.Held} instance(s).";
```

The clause exists *because* the count misleads without it, and it is the first thing off the edge.
Same shape as the closed `Z10` family: **do not state something only in a place the user cannot
reach.**

ℹ️ Severity is LOW, not MED, and the reason is measured rather than assumed: `⚠ capped` in the
Forced-fields strip is bound to the **same** `r.Truncated`
(`PropertySearchPanel.axaml:134 IsVisible="{Binding Truncated}"`), so the fact reaches the user by a
second, unclipped route. This is a report-completeness defect, not a wrong report — the two paths
agree, which is the opposite of this repo's usual root cause.

**Fix shape**: `TextTrimming="CharacterEllipsis"` + `ToolTip.Tip="{Binding StatusText}"` on that
TextBlock. ⚠ **Sweep the siblings before fixing** — the interesting question is how many other
panels put a status string in a `StackPanel` the same way, and a grep for `StatusText` alone will
not answer it (the panel matters, not the binding).

### ✅ FIXED 2026-08-23 (was NEW DEFECT) `[DUMPHDRCLIP-2026-08-23]` — the Dump Explorer meta header, found by sweeping for `[FORCESTATUSCLIP]`'s siblings

> **This closes the classification doc's A6 item *"`[FORCESTATUSCLIP]` sibling `.axaml` sweep"*, and
> the sweep is now a CI gate rather than a one-off.**

`DumpExplorerPanel.axaml:28` had `TextTrimming="CharacterEllipsis"` on a `TextBlock` that is the
**last child of a horizontal `StackPanel`, behind four fixed-width buttons** — the identical
structure `[FORCESTATUSCLIP-2026-08-22]` was fixed for. A `StackPanel` measures children with
infinite available width and hands each its **desired** width, so the trimming can never fire: the
text is hard-cut at the panel edge with no ellipsis and no tooltip to recover it.

⚠ **The tail is the load-bearing part again.** `BuildHeader`
(`DumpExplorerViewModel.cs:531`) produces
`UE {ver} · {module} · {N} classes · {N} props · {N} funcs · {DumpedAt}` — so what gets cut first is
the **dump timestamp**, i.e. exactly the field that tells you whether the dump you are reading is
stale. A long game module name pushes it off sooner.

**Fixed** the same way as the precedent: the row is now a `DockPanel` with the buttons in a
`Left`-docked `StackPanel` and `HeaderText` as the fill child (so trimming becomes real), plus
`ToolTip.Tip="{Binding HeaderText}"` (so the cut tail stays readable). Published AOT-trimmed,
build 3336.

⭐ **The sweep found exactly one, and the narrowing is the interesting part.** A naive structural
query — *bound `TextBlock` inside a horizontal `StackPanel` with no tooltip* — returns **138 hits**,
nearly all short scalars (`PoseX`, `ArrayLimit`), which is `working-lessons.md` §2's "~52% wrong"
shape in miniature: a real pattern with no severity filter is noise. The objective discriminator is
**the author's own `TextTrimming`** — its presence means they expected clipping and asked for an
ellipsis, and the layout makes that impossible. That cuts 138 → 1.

⚠ **Two dropouts were re-checked by hand, and one had dropped for the WRONG reason.**
`ValueSearchPanel.axaml:694` has `Width="520"`, so it is self-constrained and trimming works —
correctly excluded. But `MainWindow.axaml:338/353` were excluded by an early draft that looked only
at **direct** children of the panel; their tooltips are on the wrapping `Border`, not the
`TextBlock`. The rule they satisfy is *"a tooltip anywhere up the ancestor chain"*, and the draft
would have hidden a genuine case nested one level deeper. The shipped check walks ancestors for
both `ToolTip.Tip` and `Width`/`MaxWidth`.

**Now gated**: `tools/check_inert_trimming.py`, registered in `check_all.py`. Negative-controlled —
run against the pre-fix file (`git show HEAD:…`) it reports the hit; against the fixed tree, none.
Its docstring records what is deliberately **not** flagged and why.

⚠ **The gate count is now 13.** It has been 4, then 12, then 13 — `check_all.py` prints
`N gate(s) run`; derive it, never quote it. The handover row and the memory index were both changed
from a hard number to that instruction.

### ✅ M1–M5 step 3 PASSES 2026-08-22 — close the game with a hold live and the UI connected

`ActorComponent::bIsEditorOnly` held (256, capped), UI connected, then a graceful `WM_CLOSE` to the
DumperTest window.

| the row expects | result |
|---|---|
| no crash | ✅ process exited, no crash dialog |
| no hang | ✅ the UI stayed fully interactive — tab switches worked immediately, and it re-titled itself from `UE5 Dump UI — DumperTest.exe` to `UE5 Dump UI` |
| nothing new in the Windows **Application** log | ✅ newest entry is still the pre-test 14:43:25 `Security-SPP`; **zero** new entries, no `Application Error`, no `.NET Runtime` |
| the Forced-fields strip | cleared itself — correct, the holds belonged to a dead process |

⚠ The row itself says the evidence here is "nothing happened", so the reader must be shown able to
report something: `wevtutil qe Application /c:3 /rd:true /f:text` was run first and returned real
rows. ⚠ Under Git Bash it needs `MSYS2_ARG_CONV_EXCL='*'` or MSYS rewrites `/c:3` into a path and
`wevtutil` fails with 「指定太多引數」 — which looks like "no events" if the exit code is ignored.

-----

### ✅ FOUND + FIXED 2026-08-22 `[SEETHRUTALLY-2026-08-22]` — See-through counted occluders it never hid

⭐ **Found by trying to run M1–M5 step 1, and it is the reason that row exists.**

`Schlacht::InvokeSetHidden` returns `bool` — false when the object is not an `AActor` (its own
`ClassDerivesFromAny(cls, {"Actor"})` guard) or when `SetActorHiddenInGame` is cooked out — and
**both call sites in `Tick()` discarded it**, then recorded `desired` wholesale. So `hiddenActors`,
`hiddenCount`, the pipe's `hidden_count`, the UI's *"Active — hiding the occluder in front of your
character"* and the log's `disabled (N restored)` all reported **intent**.

**Measured on DumperTest (UE 5.4, `ue_version 504`):**

| | before the fix | after |
|---|---|---|
| `hidden_count` | **1** | **0** |
| `hidden_actors` | `['0x272EBF08D40']` | `[]` |
| that object | `class=StaticMeshComponent`, **has no `bHidden` at all**; its own `bHiddenInGame` read `false` with See-through **both OFF and ON** | — |
| log | `disabled (1 restored)` | `disabled (0 restored)` |

So the feature was a **complete no-op on this build and every channel said it was working**. Audit
#4's named root cause — the report and the reality computed by different code paths — in a sixth
place, and the most consequential instance so far because the "reality" path did not exist at all.

**Fixed**: `Tick()` records only what was APPLIED — entries carried over from the previous set (known
to have taken) plus new ones whose `InvokeSetHidden` returned true.
⚠ **The unchanged half is NOT verified**: the two games `docs/` records as working (Tower of Mask,
DQ7R) are not runnable on this machine, so "behaviour where hiding works is unchanged" is an
argument from the diff, not a measurement.
⚠ **No test target compiles `Schlacht.cpp`**, so the live before/after above is the whole coverage.

-----

### ✅ ADDED 2026-08-22 `[SEETHRUSET-2026-08-22]` — `seethrough_get_state` now names the actors, not just a count

⭐ **This is what unblocked the finding above, and the cost of not having it is the lesson.**

The row demands a second detector that re-reads the actors' own hidden flags. That is impossible
when the DLL reports a **count**: an outside verifier cannot name the actors. The attempt without it
went: enumerate 33 candidate actors by class (`StaticMeshActor`, `Actor`), walk each one's `bHidden`,
find **none** hidden while the DLL insisted on 1 — and then be **unable to tell "my candidate set is
wrong" from "the hide silently failed"**, which are the two hypotheses the whole row is about.
`find_instances` matches a class-name **substring** and the class histogram caps at 40 entries, so
widening the net was guesswork; the answer appeared the moment the DLL said *who*.

`Schlacht` already held the set (`s_state.hiddenActors`); the change publishes it, filled **under the
same lock as `hiddenCount`** so a caller can never see a count that disagrees with its list. The rig
`tools/verify/seethrough_arms.py` asserts exactly that as its first check.

-----

### ✅ FIXED 2026-08-22 `[SEETHRUNOOP-2026-08-22]` — on UE 5.4 the hit resolved to a COMPONENT, so See-through hid nothing

**Severity: HIGH on affected builds** — the feature does nothing at all. Not fixed: the repair
depends on a layout fact I could not read from outside, and guessing at it is how a fix deletes
working code (working-lessons §2.4).

`Schlacht::ExtractHitActor` pulls the hit object out of the returned `FHitResult`: UE4's
`Actor` (`TWeakObjectPtr` → leading `int32` ObjectIndex → `Aura::GetByIndex`), else UE5's
`HitObjectHandle` (`FActorInstanceHandle`) read the same way. On DumperTest / UE 5.4 the object that
comes back is a **`UStaticMeshComponent`** — sampled repeatedly over ~10 s at pierce depth 3, and it
was the same object every tick, so this is systematic, not a torn read.

`InvokeSetHidden` then rejects it at its own `ClassDerivesFromAny(cls, {"Actor"})` guard and returns
false **silently** (the `SetActorHiddenInGame NOT FOUND` warning is never reached — it sits *after*
that guard). Before `[SEETHRUTALLY]` that silence was invisible; now it surfaces as
`hidden_count = 0`, which is honest but still a no-op.

⚠ **Do not assume this is universal.** `docs/` records See-through as VERIFIED in-game on Tower of
Mask and DQ7R, so the extraction works on those builds. What is measured is: **UE 5.4 / DumperTest
resolves to a component**.

**The diagnostic still needed** (all DLL-side — none of it is reachable over the current pipe):
1. Dump the reflected `structFields` of `LineTraceSingle`'s `OutHit` param — the field names and
   offsets actually present. The two candidate explanations are (a) `structFields` is flattened and
   a nested name matched at the wrong offset, and (b) the leading `int32` of `FActorInstanceHandle`
   is not the actor's weak-ptr index on this build.
2. Log the resolved object's class when it fails the `Actor` guard — one `LOG_WARN` behind a
   one-shot flag would have named this in the first session it ever ran.

**Fix shape, once (1) says which:** prefer `FHitResult.Component` and hop to its owner
(`UActorComponent::GetOwner`) when the direct actor read yields a non-Actor — a component hit is the
common case for world geometry anyway, and the owner is what `SetActorHiddenInGame` wants.
⚠ Whatever the fix, keep `[SEETHRUTALLY]`'s applied-not-intended rule: it is what makes the next
version of this failure visible instead of silent.

ℹ️ **M1–M5 step 1 stays open and is now BLOCKED on this**, not on tooling. The rig is written and
refuses to report a pass (`hidden_count stayed 0 … every assertion below would be vacuous`), which
is the correct answer while the feature does nothing here. Arms (a) and (b) additionally need a
human moving/stalling the game; arms (c) and (d) are ready to run the moment a fixture actually
hides something.

### ✅ FOUND + FIXED 2026-08-22 `[DISTCOPY-2026-08-22]` — `build.ps1` reported a successful publish over a copy that never happened

⭐ **Found while doing the mandatory `-Mode Publish` before hand-over** — i.e. by using the rule at
the top of CLAUDE.md, on the one step that rule entirely depends on.

Three things compounded, and each one hid the next:

1. `Copy-Item` is **non-terminating** and the `ForEach-Object` loop checked nothing, so a locked
   destination left `$exitCode` at **0**.
2. `Remove-Item $publishDir` then ran unconditionally, **destroying the binary that had just been
   built correctly** — the only good copy on disk.
3. `Write-Ok` printed `Get-FileSize` of the file **in `dist/`**, i.e. the stale one. ⭐ And a stale
   AOT exe is **54.7 MB exactly like a good one**, so the size check the memory index prescribes
   ("54.7 MB = AOT and shippable") cannot distinguish them.

**Measured**: a still-running `UE5DumpUI.exe` held `av_libglesv2.dll`; the run printed
`[OK] UE5DumpUI.exe (54.7 MB)` and exited 0, while `dist/` kept `sha 1b316b6a` and the freshly
published exe was `18e4112d`. **Nothing in the output said so** — only hashing `dist/` against
`dist/publish/` did, and that directory is normally deleted before anyone could.

**Fixed**: each copy is `-ErrorAction Stop` inside a try/catch that collects failures; the published
exe is compared to the one in `dist/` **by SHA256**; a mismatch is a loud `Write-Fail` with
`$exitCode = 1`; and `dist/publish/` is discarded **only** once `dist/` provably matches, so a
blocked run leaves the good binary on disk to copy by hand. The success line prints the hash.

⚠ `Short-Hash` exists because `$dstHash` can be the literal `"<missing>"` (9 chars) and
`Substring(0,12)` would have thrown **inside the very branch whose job is to report the problem**.

**Both directions demonstrated:**

| | with `dist/UE5DumpUI.exe` held open | after closing it |
|---|---|---|
| per-file | 5 × `[FAIL] copy failed — <name>: <reason>` | — |
| verdict | `[FAIL] dist\UE5DumpUI.exe is NOT the build that was just published (dist 89A8ADEE… vs published 66038833…)` + the remedy | `[OK] UE5DumpUI.exe (54.7 MB, sha 8CA03D81BAAB)` |
| exit code | **1** | 0 |
| `dist/publish/` | **left in place** | removed |

⭐ **The general rule this earns**: a step whose whole purpose is to make a file authoritative must
verify the file, not the operation. Checking the exit code of a copy, or the size of whatever ended
up at the destination, both answer a different question than "is the thing I am about to hand over
the thing I just built".

ℹ️ Two smaller facts worth keeping: `-Target DLL` fails the same way when an **injected game** holds
`dist/UE5Dumper.dll` (seen twice today), and Native AOT output is **not byte-reproducible** — four
publishes of the same source produced four different hashes, so a hash can confirm *this* copy but
never that two builds are "the same build".

### ✅ AE4–AE7 step 4 CLOSED 2026-08-22 `[ORPHANGATE-2026-08-22]` — the gate arm nothing covered, pinned deterministically

**Why it kept not getting tested.** The row asks you to start a delete and then race a scan against
it. It failed twice, and the maintainer wrote the reason down verbatim on 2026-08-21:
「執行時間太短無法測試」 — the delete finished before the next button could be pressed, so the gate
was never reached. ⭐ **That is a timing problem, not a logic problem**, and the row itself already
says a delete that outruns you is *not tested* rather than *passed*. Making the delete slow enough
means manufacturing a long leftover list, i.e. writing fake proxy DLLs into real game installs —
which is not pre-approved and would be a worse fixture than the thing it tests.

**What was actually missing.** `ProxyDeployConcurrencyTests` pins Refresh / UpdateAll / Deploy /
Undeploy / Scan against each other and **never mentions orphans** — so the panel's one *destructive*
operation was the only long operation with no gate coverage in either direction. The row was
chasing, by hand, the arm the suite had skipped.

**New `ProxyOrphanGateTests` (4 tests), both directions:**

| test | what it holds open | assertion |
|---|---|---|
| `AScan_IsRefused_WhileAnOrphanDeleteIsRunning` | the delete, at the top of a row's removal | the scan never reaches the service (`FindCalls` unchanged), the refusal names the running op, `IsBusy` true during and false after |
| `AnOrphanDelete_IsRefused_WhileAScanIsRunning` | the scan, parked on a `TaskCompletionSource` | `RemoveCalls == 0` — **nothing was recycled** — and the "Wait for the current operation" line shows |
| `TheGate_IsReleased_WhenNothingWasChecked` | — | an empty pass cannot wedge the panel: a scan straight afterwards is not refused |
| `TheConfirmDialog_IsNotTheGate_AndThatIsDeliberate` | the confirm delegate | `IsBusy` is **false** while the dialog is up |

⚠ The last one pins the row's own ⚠ (「對話框開啟不等於刪除正在跑」) **as correct, not as a hole**:
`IsRemovingOrphans` is set *after* `ConfirmOrphanRemovalAsync` returns, and that is right because the
dialog is modal (`OrphanCleanupConfirmDialog.ShowAsync` → `ShowDialog(owner)`, so no other panel
button is reachable) and because holding the exclusive gate across a prompt the user may sit on
indefinitely would lock the panel on something that can still be cancelled.

**Negative controls, one change at a time, restored byte-exact both times:**

| control | tests that failed |
|---|---|
| `TryBeginExclusive`: drop `\|\| IsRemovingOrphans` | **exactly** `AScan_IsRefused_WhileAnOrphanDeleteIsRunning` |
| delete's entry check → a never-true predicate | **exactly** `AnOrphanDelete_IsRefused_WhileAScanIsRunning` |

⚠ The first attempt at the second control used `if (false)`, which is **CS0162 unreachable-code** and
fails the build — a build error is not a negative control, it is a different experiment. Weakened to
`IsScanning && IsRemovingOrphans` (compiles, can never be true) instead. **4,693 / 4,693 pass.**

**What the tests do NOT cover, closed separately by inspection**: that the real buttons are wired to
these commands. `ProxyDeployPanel.axaml:149` binds `ScanOrphansCommand` and `:169` binds
`DeleteSelectedOrphansCommand`, so the gate the tests exercise is the one the UI drives.

### ✅ `[SEETHRUNOOP-2026-08-22]` FIXED, and M1–M5 step 1 arms (c)+(d) PASS — See-through actually hides now

**The root cause, measured.** Two one-shot diagnostics went in *before* any fix, because guessing at
a struct layout from outside is how a fix deletes working code (working-lessons §2.4). They printed
the answer directly:

```
SeeThrough: OutHit param off=88 size=248, 20 struct field(s): FaceIndex@0 Time@4 Distance@8
  Location@16 ImpactPoint@40 Normal@64 ImpactNormal@88 TraceStart@112 TraceEnd@136
  PenetrationDepth@160 MyItem@164 Item@168 ElementIndex@172 bBlockingHit@173
  bStartPenetrating@173 PhysMaterial@176 HitObjectHandle@184 Component@216 BoneName@232 …
SeeThrough: the traced hit is NOT an AActor — class 'StaticMeshComponent' at 0x22217C53400.
  … Owner hop: outer='StaticMeshActor_43'
```

UE 5.4's `FHitResult` has **no `Actor` member** (correct for UE5), so `ExtractHitActor` fell through
to `HitObjectHandle@184` and read its leading `int32` as an ObjectIndex — which resolves to the
hit's `UStaticMeshComponent`, not its owner. ⭐ And the answer was sitting two members along:
`Component@216`, whose **Outer is the owning actor**.

**Fix**: try `Actor`, then `HitObjectHandle`, then `Component`, and take the first that **resolves to
an actor** — `ResolveToActor` walks `Outer` (bounded to 4 hops), because a component's Outer is the
actor that owns it.

⭐⭐ **Why this cannot regress the builds where See-through already worked** (Tower of Mask, DQ7R —
neither runnable here, so this argument has to carry the weight): the first two members are still
tried **first, in the same order**, and `ResolveToActor` is the **identity at hop 0** for anything
that is already an `AActor`. The change can only turn a previous *failure* into a success.

**Verified end to end on DumperTest, two independent detectors:**

| | detector ① `hidden_count` | detector ② the actor's OWN `bHidden` |
|---|---|---|
| enabled | **1**, `hidden_actors=['0x28D0405DF00']` | **true** ← positive control: detector ② demonstrably fires |
| disabled | **0** | **false** — restored |

**M1–M5 step 1, arms (c) and (d):**

- **(c) pull the pipe connection** — the actor was confirmed hidden (`bHidden=true`) first, then the
  client dropped without sending a disable: `active=false`, `hidden_count=0`, `bHidden=false`, and
  the log's `disabled (1 restored)` now says **1** rather than the pre-fix lie.
- **(d) graceful `WM_CLOSE` while active** — clean exit, **no `tick threw`** in any log, and **zero**
  new Windows Application events. That is the arm's real target: `WorkerLoop`'s catch exists for the
  `std::terminate` / `0xC0000409` "See-through then close the game" crash, and none occurred.
  ⚠ A `taskkill /F` does **not** test this — it was tried first and the DLL's shutdown path never
  ran at all, so the arm was vacuous. The row means a graceful close.

Arms **(a)** and **(b)** stay open: they need a human moving the character / stalling the game.

ℹ️ **A rig defect the positive control caught**, worth keeping: `walk_instance` renders a bit-field
bool as `true (bit 7, mask 0x80)`, so testing `== "true"` read a **hidden** actor as not hidden. The
rig refused to pass rather than reporting a false negative. ⭐ A detector has to be right about the
**format** of what it reads, not merely about where to read it — the same shape as the display-tie
bug in the cadence rig.

ℹ️ Both diagnostics are kept: one-shot, and the OutHit dump is the first thing to read on the next
engine whose `FHitResult` differs.

### ✅ A6 step 3 PASSES 2026-08-22 `[A6-DERIVE-2026-08-22]` — the hold walks the super-chain, not the name

The row is explicit that a large held count settles nothing here: **a prefix match also holds
hundreds, and the two look identical from outside**. So the run is built on the two things a name
matcher could not survive at once — `tools/verify/a6_prefix_siblings.py`, on
`Actor::bIsEditorOnlyActor` (58 held).

| | measurement |
|---|---|
| ⭐ **positive** — a class a NAME match would MISS | `StaticMeshActor` **derives** from `AActor` and its name does **not** start with "Actor". While the hold was up: `0x1FE64129FC0 StaticMeshActor -> bIsEditorOnlyActor = true (bit 1, mask 0x02)`. A prefix matcher could not have reached it. |
| **negative** — objects a NAME match would HIT | 33 diffable objects re-walked field-by-field before and after, **0 touched**. One is the genuine same-prefix stranger `ActorSequence`; the rest are a broader non-derived sample. |

⭐ The pair is the point: the hold **reaches** what a name test would miss and **skips** what a name
test would catch. Either alone is consistent with the wrong matcher.

⚠ **The DLL's own log line is printed but is NOT the assertion, and reading it as one would be a bad
mistake**: `FindInstancesDerivedFrom base='Actor': 58 live instance(s) over 3941 distinct class(es)`
— that 3941 is `derivedCache.size()`, i.e. how many classes the derivation test was **evaluated
for** (the whole pool), not how many matched. Taken as a match count it looks like a catastrophic
over-hold.

ℹ️ Fixture note for whoever runs this next: **"Actor" is a thin prefix on DumperTest** — 8 same-prefix
strangers exist but 7 are interfaces with **no reflected fields**, so they cannot be diffed at all.
The rig drops them rather than counting them (a stranger that can never show a change would pad the
sample and make the result look stronger than it is) and tops up from a broader non-derived set.

-----

### ✅ A6 step 5 CLOSED 2026-08-23 `[A6-SPAWN-DQ7R-2026-08-23]` — the spawn half, on a live commercial game

The (A) mechanism half passed on DumperTest 2026-08-22. (B) — *"make genuinely new objects and check
they did not inherit a written CDO"* — was blocked there because `set_debug_camera` is **one-shot per
process**. Run here on **DQ7R** (UE427, proxy build 3315), which has real map loads.

**Four independent runs, every one a PASS**, using `tools/verify/a6_cdo_and_spawn.py --spawn manual`:

| # | game state at snapshot | baseline components | newly spawned | carrying the forced value |
|---|---|---|---|---|
| 1 | Title screen | 828 | 1 | **0** |
| 2 | Title → save menu | 828 | 5 | **0** |
| 3 | In-world 魚灣村 | 10,250 | 2 | **0** |
| 4 | In-world → map change to 艾斯塔德島 | 10,252 | 4 | **0** |

⭐ **Not vacuous, and the rig enforces that**: each run first proves the channel — `force_field ->
held=256`, then **12 of 12** sampled live components actually carry the value — and the run FAILS if
nothing new appeared. So "none of the new ones were forced" is a statement about objects that exist,
made while the write path is demonstrably working.

⭐⭐ **The gap that would have made this evidence hollow, closed separately.** Every newly-observed
object was an `AtomComponent`, and a new `AtomComponent` copies **`Default__AtomComponent`** — not the
`Default__ActorComponent` the rig checks. So a clean spawn would prove nothing unless the subclass CDO
was clean too. Checked directly, during a live hold:

```
                                         BEFORE      DURING hold
Default__ActorComponent @0x1DBAB2303E0   false       false
Default__AtomComponent  @0x1DBAB2C3220   false       false      <- the one spawns inherit
Default__SceneComponent @0x1DBAB230EC0   false       false
```

`force_field` on `ActorComponent` holds across every subclass (`Aura::FindInstancesDerivedFrom`), and
CDOs are objects in that set — so a missing CDO-skip **would** have written all three, and every new
`AtomComponent` would have carried it. It wrote none. That is what makes the spawn observation
discriminating rather than decorative.

⚠ **Honest scope.** All 12 observed spawns across the four runs were audio components. A map
transition's own wave was never captured: the settle window (15–25 s) closes before a level finishes
streaming, and the transition to 艾斯塔德島 completed after run 4 had already reported. So this
demonstrates *"objects created during play do not inherit a written CDO"* on a real game — not *"a
whole level's worth of actors"*. Given (A), and given the subclass-CDO check above, that is
sufficient; a longer `--settle` would harvest a bigger wave if anyone wants one.

⭐ **This run also validated the cap fix from earlier the same day, in the field.** DQ7R holds **828**
live components at its title screen and **10,270** once 魚灣村 is loaded — both above the rig's old
`limit=400`. The previous code would have diffed a 400-object *sample* and reported roughly 9,870
untouched survivors as "newly spawned", i.e. a confident, catastrophic false FAIL. `check_complete`
now makes a cap a loud error instead.

ℹ️ `--spawn manual` was also improved mid-run: its stdout is now line-buffered, because a captured run
began at the spawn banner with the whole (A) section — CDO address, `held=`, the channel proof — still
sitting in a block buffer. Evidence that never reaches the log is the same as not having it.

### ✅ A6 step 5 — the CDO half PASSES `[A6-CDO-2026-08-22]` (the spawn half, blocked here, CLOSED 2026-08-23 on DQ7R — see `[A6-SPAWN-DQ7R-2026-08-23]`)

Step 5 asks: force a bool on a base class, `reset_all_fields`, then check that **newly spawned**
objects do not carry the value — i.e. that the CDO was never written. `tools/verify/a6_cdo_and_spawn.py`
runs both halves, because they answer different questions.

**(A) The mechanism — PASSES, and not vacuously:**

```
CDO ActorComponent @ 0x1FE74854DE0 : bIsEditorOnly = false (bit 0, mask 0x01)
force_field -> held=256 truncated=True
  live sample of 12: 12 actually forced        <- channel proof
  CDO during hold : bIsEditorOnly = false
```

⭐ The channel proof is what makes it mean something: **12 of 12** sampled live components really do
carry the forced value, so "the CDO is clean" is a statement about the write path *deliberately
skipping* it, not about nothing having been written. CLAUDE.md's "CDO-skip happens INSIDE the walk,
before the cap" is confirmed live.

**(B) The consequence — NOT RUN, and the reason is measured rather than assumed.** Three attempts:

| attempt | result |
|---|---|
| toggle the debug camera on (AA12 step 3's lazy-instantiation trick) | `state` was **already 1** — `ADebugCameraController` and `DebugCameraHUD` had been instantiated earlier in the session. It is one-shot per process. |
| cycle it off → on | 295 live objects before, **295** after, **0 new** |
| find a console-command entry point to invoke (`ConsoleCommand`, `RestartLevel`) | neither is in the 3,142 functions `list_all_functions` returns here |

So DumperTest has **no repeatable in-process object-recreation lever**. ⚠ A relaunch does **not**
substitute: it reloads the CDOs from disk, so it could not detect an in-memory CDO write — which is
the entire question.

**What would unblock it**: a game where a level reload / enemy respawn can be triggered, or an
invoke path to `APlayerController::ConsoleCommand` (`RestartLevel` recreates every actor and
component in-process, inheriting from the in-memory CDOs). Given (A), this is corroboration rather
than the load-bearing half — but the row asks for it, so it stays open.

### ✅ AC13 step 1 PASSES 2026-08-22; steps 2–3 are UNOBSERVABLE as written `[AC13-2026-08-22]`

**Step 1 — close the UI while connected, no `Pipe: ReadLoop error`.** Run on the fresh AOT build
(v1.0.0.3315) against an injected DumperTest, `WM_CLOSE` sent while the header still read
`Connected — UE504 (25189 objects)`.

| | result |
|---|---|
| `ReadLoop` anywhere in any log | **0** |
| DLL side | `PipeServer: Client disconnected` **×2** (the UI holds two lanes), and **zero** ERROR/WARN in the whole `pipe-0.log` |
| UI side | no error, no exception |

⭐ **And the absence is not vacuous**, which took one extra check: the UI's `ui-pipe-0.log` stops at
16:23:43, well before the close, so on its own "no error line" would prove nothing. `ui-init-0.log`
settles it —

```
[2026-08-22 16:24:15.146] [INFO] UE5DumpUI shutting down...
[2026-08-22 16:24:15.150] [INFO] Mirror log stopped
```

— the logger was alive and flushing at 16:24:15.146, and the DLL logged the disconnect at
16:24:15.150. `Pipe: ReadLoop error` would have been written into exactly that window.

**Steps 2–3 cannot be run as written, and the reason is worth more than the steps.**

The row says 「看 System 分頁的 IPC 時間數字」. ⚠ **There is no IPC figure on the System tab.** What is
there is *Diagnostics — DLL dispatch cost* (per-command Count / Total ms / Avg / Max / % busy — the
DLL **dispatcher**, e.g. `get_object_list` 13 calls, 184.971 ms total, 99.3% busy) and a *Pipe
Activity* tail of round-trip times. The transport figure AC13 fixed lives in `PipeTransportStats`,
and its only consumer is `DiagnosticsProbe`, which wraps three specific operations (Copy CE XML,
Copy CE Field, Snapshot capture) and writes a `PERF` line to `view-0.log`.

⭐⭐ **And step 3's observable is destroyed by step 3's own action.** `DiagnosticsProbe.DisposeAsync`
takes a *closing* `GetDiagnosticsAsync` to compute its window:

```csharp
try { after = await _dump.GetDiagnosticsAsync(limit: 0); }
catch { return; }   // disconnected mid-operation: nothing to report
```

Close the game mid-request — which is precisely what the step asks for — and that call throws, the
probe returns, and **no PERF line is written at all**. The number the step wants to read cannot be
produced by the route that would report it.

**What the fix actually needs, and does not have.** `PipeTransportStats` appears in **no test
source** (only in compiled binaries under `bin/`). Its sibling on the same defect family,
`ClassifySendFailure`, *is* tested (`PipeClientSendFailureTests`) — because it was split out as a
pure function. AC13's fix is a `try`/`finally` **placement** and AC14's is capturing `_reader` into
a local; neither is reachable from a pure-function test, which is why both were left to a live row
that then turned out to be unobservable.

Two ways forward, both real work rather than a click:
1. **Surface the transport figure** where it survives a disconnect (the System tab already refreshes
   `DLL dispatch cost` on demand; `PipeTransportStats.Snapshot()` is monotonic and needs no pipe).
   That would make step 3 observable *and* give the metric a home outside three operations.
2. **Test the placement** with a real in-process `NamedPipeServerStream` disposed mid-write, and
   assert `Snapshot().Calls` incremented — the only seam that exercises where the timer sits.

ℹ️ Same shape as `[ORPHANGATE-2026-08-22]` earlier today: a live row that keeps not getting run, over
logic that has no test. There the answer was to write the test; here the pure-function seam does not
exist, so the honest recommendation is (1) first.

-----

### ✅ B10 CLOSED 2026-08-22 `[B10-2026-08-22]` — WalkClassEx memo: timing recorded, and all three field families decode

**Steps 1–2.** Snapshot capture on DumperTest (`NumericNoByte` / All numeric / Game objects only /
Auto-detect noise): **644 objects, 12,155 fields**, and `ui-view-0.log` has the line:

```
PERF Snapshot capture: wall 638.6 ms · dispatcher busy 189.7 ms (29.7%) · 6 dispatches
  · game WS +3.0 MiB · split dll 189.7 / ipc 34.3 / ui 414.6 ms
  (per call: dll 37.948 / ipc 6.850 / ui 82.914 ms)
  · top: snapshot_chunk 186.1ms/4x max 70.7ms, begin_snapshot 3.6ms/1x max 3.6ms
```

⚠ The only surviving prior figure is **5,256.2 ms** (2026-08-04), and it is **not comparable** — a
different target and scope. Per the row, this becomes the **new baseline**: *DumperTest ·
NumericNoByte · 644 objects / 12,155 fields · wall 638.6 ms*. Compare only against the same game and
the same capture settings.

⭐ **Bonus, and it partly unblocks `[AC13-2026-08-22]` step 2**: the transport figure that is missing
from the System tab is right here — **`ipc 34.3 ms` over 6 dispatches (6.850 ms/call)**. So AC13's
baseline can be taken from a DiagnosticsProbe-wrapped operation after all. Its **step 3** is still
unobservable, for the separate reason recorded there.

**Step 3 — struct type, enum name and bool mask, all populated.** Property grid on a live
`StaticMeshActor` (`0x1FE6412F7C0`):

| family | field | shown | raw `Hex` column |
|---|---|---|---|
| StructProperty | `PrimaryActorTick` | `{TickGroup=0, EndTick…}` | — |
| BoolProperty (bitfield) | `bHidden` / `bCanBeDamaged` / … | `false (bit 7, mask 0x…)`, **a different bit and mask per field** | `62`, `20` |
| EnumProperty | `SpawnCollisionHandling…` | `ESpawnActorCollisionH…` | `01` |
| ByteProperty-as-enum | `Role` / `NetDormancy` / `AutoReceiveInput` | `ROLE_Authority` / `DORM_Awake` / `EAutoReceiveInput::Di…` | `03` / `01` / `00` |

⭐ **What makes this discriminating rather than just "the columns are not blank":** the grid prints
the **raw byte beside the decoded name**, so `03 → ROLE_Authority` and `01 → DORM_Awake` are visibly
*two different enums each decoded correctly*. A single hard-coded mapping, or a name echoed from
somewhere else, could not produce both. Likewise the bool rows share a byte (`0x59`, `0x5A`) and
differ only in bit index — the masks are computed per field, not stamped.

No crash and no blanking, which is the row's stated FAIL condition.

### ✅ SPENT — V6/U8 step 1, the auto-refresh half `[V6-AUTO-2026-08-22]`

> **CLOSED 2026-08-22 `[V6U8-FNAMEPAIR-2026-08-22]` — BOTH steps pass.** The "NOT closed" in the old
> title outlived its cause by one session.

⭐ **First: the row's own blocker was stale.** It said to wait until `[AUTOREFRESH-2026-08-19]` was in
a *published* build. That fix was **VERIFIED 2026-08-20 (steps 1–7 all pass)** and `dist` here is now
1.0.0.3315, so the gate was already lifted — §2.13 again, a deferral reason outliving its cause.

**Established, on the AOT build against DumperTest (`UWorld ThirdPersonMap`, filter `SkyLight`,
2 matches, interval 6 s):**

- The countdown **runs and re-arms** (`sec · 5s → 4s → …`), i.e. the `[AUTOREFRESH]` freeze-at-0s is
  gone in a published build.
- Across **4 consecutive auto ticks** the filter text, the `2 matches` count and the **selected row**
  (`SkyLight.SkyLightComponent0`) all survived, and the grid did not move.
  ⚠ The "does not jump to the top" clause was **vacuous in that run** — the grid was already at the
  top, so there was nothing to lose. Not evidence.
- ⭐ **The auto path is not a separate path**: `OnAutoRefreshTick` (`:5738`) does nothing but
  `await RefreshAsync()`, which passes `clearFieldSearch:false` and then `RestoreSelectedField`. The
  manual half of this step is already ✅ (2026-08-17), and four tests pin the state across a refresh
  (`LiveWalkerSearchHighlightTests.Refresh_KeepsTheKeywordAndReMarksTheFreshRows` and siblings), plus
  `LiveWalkerSearchNavTests` for the ↑/↓ stepper.

**Not established, and deliberately NOT filed as a defect:** the match highlight was observed to
**disappear after an auto tick** while a manual `Refresh` six seconds earlier had **preserved it**,
with `2 matches` still displayed both times. An identical code path cannot produce both outcomes, so
**one of those two observations is unreliable** and neither is worth a finding on its own.

⚠⚠ **The instrument is what failed, and this is the part to carry forward.** The Live Walker toolbar
**reflows** — `Find Refs` and `Related` appear once an object is loaded — so button coordinates
captured earlier in the same session go stale *silently*:

| click | intended | actually hit |
|---|---|---|
| `(918,195)` "turn Auto OFF" | Auto | Auto — but the earlier ON/OFF bookkeeping was wrong, so a control ran with Auto **ON** |
| `(547,301)` ▼ step | ▼ | the **"2 matches" label** — the ▼ had moved to x≈521 |

So the "the ↑/↓ stepper stopped working" half is **unverified**: my own click detector was never shown
to fire in the post-auto state, and a detector that has not been shown to work cannot testify to a
failure (§1). Two of three coordinate assumptions went stale mid-run before I noticed.

**What this row needs next**: either a human at the keyboard, or an instrument that does not depend
on pixel positions in a reflowing toolbar — re-read the control's position from a fresh screenshot
immediately before every click, and assert the click landed before asserting anything about what
followed.

ℹ️ Step 2 (a `NameProperty` with a numeric suffix cross-checked against Value Search) was not
attempted.

### ✅ A3 step 3 CLOSES THE ROW 2026-08-22 `[A3-STEP3-2026-08-22]` — and the 2026-08-19 numbers reproduced exactly

`[A3-DOUBLE-2026-08-19]` closed steps 1, 2 and 4 and left step 3 (the *asymmetry corroboration*: the
same field must also be findable through Group Scan / Property Search **Deep**, a path that already
worked before build 3168).

**Step 3 — PASS.** From a `Double`/`Exact 0` scan, the vector leaf
`SparseVolumeTextureViewerComponent :: ComponentVelocity.X`; then
`search_properties(query="ComponentVelocity", deep=True)` → **3 rows**, the first of which is
**`SceneComponent`** — the class that *declares* the field, which is exactly what audit #5 A6's rule
predicts (a Property Search row for an inherited field is keyed to the declaring class, not the
instance's own class). Two different subsystems, same field.

⭐⭐ **And the run reproduced the 2026-08-19 measurement to the digit**, which is a stronger check on
that record than anything I set out to do:

| | 2026-08-19 | today |
|---|---|---|
| `Double`/`Exact 0` candidates | 3,450 | **3,450** |
| `TraceQueryTestResults` distinct vector parents | 72 | **72** |
| `RigVMMemory_Work` | 34 | **34** |
| `DumperTestCharacter` / `BP_ThirdPersonCharacter_C` | 19 / 19 | **19 / 19** |

Different session, different build (3263 → 3315), same fixture — the recorded statistic is stable and
was not a one-off.

⚠⚠ **I walked straight into step 1's documented trap before re-reading the record, which is the
lesson worth keeping.** Following the 繁中 step literally — *Value Search, **Float***, — gave **0**
vector components (not even `.Location`), i.e. a clean-looking FAIL of a working fix. Under **LWC an
`FVector` is a double-precision `FVector3d`**, so a Float scan structurally cannot see `.X`. The
warning was already written down in this file on 2026-08-19; I re-derived it the expensive way. ⭐
**Re-read the register entry for a row before running the checklist step** — todo.md is canonical and
the mirror can be a whole verification pass out of date.

**The mirror was exactly that**: the 繁中 section still had all four steps ⬜ and still said "Float",
three days after the row was recorded verified. Deleted now.

ℹ️ Step 4's grep is also no longer vacuous: four value scans ran this session (Float ×2, FVector,
Double over **25,172 objects / 1,415 classes**, `deadline_hit=false`) and `hit the … scan-field cap`
appears **0 times** in any log. The largest single class contributed 72 distinct vector parents —
hundreds of leaves, still far under the 4,000 cap. ⚠ The WARN's own channel was not separately
proven; the load-bearing evidence is that the scans demonstrably ran at that scale.

### ✅ CLOSED 2026-08-22 `[AF22-PAIR-2026-08-22]` — Freeze and Force seen SIDE BY SIDE, same row, same session

Step 2 was the last one open (1, 3 and 4 closed earlier today). It is step 1's **control**: the
ordinary Freeze flow must *still* say "Create freeze script" and *still* give the CFG-block advice,
so that "the Force dialog no longer mentions them" cannot be satisfied by having deleted the wording
everywhere.

**Run on the AOT build (v1.0.0.3313) against DumperTest, on `CharacterMovementComponent::MaxWalkSpeed`
— the SAME row for both dialogs, one after the other:**

| | **Freeze** | **Force** |
|---|---|---|
| title | `Freeze property value` | `Force property value` |
| field label | `Freeze value (float):` | `Force value (float):` |
| confirm button | **`Create freeze script`** | **`Hold this value`** |
| inherited-field warning, tail | *"…To target a single class, edit **className** in the generated **CFG block** (or set derived = false for that class only)."* | *"…There is **no per-class switch for Force** — it holds the field on the declaring class and every subclass until you release it from the **"Forced fields"** strip."* |

⭐ **Driving both from one row in one session is stronger than either half alone**, and stronger than
the offline guard: the guard reads `en.axaml`, so it cannot tell whether the dialog *binds* the key
it should. Here the same control produced two different sets of words because it was handed a
different `Purpose`, which is the whole mechanism.

⚠ **THE ROW IS MIS-FILED, and this is why it never got run.** It sits under **第 2 步 (needs an
injected game)**, but the Freeze button is
`IsEnabled="{Binding IsAobMakerAvailable}"` (`PropertySearchPanel.axaml:307`) — **it needs Cheat
Engine with the AOBMaker plugin**, which is 第 3 步. Nothing on screen explains that; the button is
simply grey. The Force submenu next to it has no such gate, so the two halves of one row need
different environments.

⚠ **And the enablement is ATTACH-scoped**: `PropertySearchPanel.axaml.cs:48` probes once when the
panel attaches, behind a 5 s cooldown (`PropertySearchViewModel.cs:31`). Start Cheat Engine *after*
the UI and the toolbar badge flips to **AOBMaker Connected** while the Freeze button stays **grey** —
the panel already probed and will not probe again. **Switching to another tab and back re-attaches
and fixes it.** Verified in that order: grey → tab away → tab back → enabled.

⛔ **I nearly filed that as a defect.** `RefreshAobMakerAvailabilityAsync` looked like it had exactly
one caller — itself, inside the very command the disabled button cannot invoke — which reads as an
unbreakable deadlock. It was an artefact of **my own grep**: I had excluded lines to "focus" the
output and the exclusion swallowed the fourth caller, the attach hook. Re-running the grep without
the filter found it immediately. *A filtered grep is not a measurement of what exists — it is a
measurement of what survived the filter.*

ℹ️ No script was created and no value was held: both dialogs were cancelled. The check is about the
words, and pushing a record into CE is an outward action the row does not ask for.

### ✅ CLOSED 2026-08-22 `[AF7-BUDGET-2026-08-22]` — `budget_hit` verified exhaustively over the pipe, and its UI consequence pinned because no host can reach it

Step 3 was the last one open in this section (steps 1 and 2 closed 2026-08-21 with
`[SOLIDEHELD-2026-08-21]`). It reads: *call `walk_function_props` on a NATIVE UFunction and check
the reply has `budget_hit`; if true, the Props dialog turns amber and says "hit its instruction
budget", and Interesting Functions' Uses column shows `⚠ partial`.*

⚠ **"THE KEY EXISTS" IS NEARLY A TAUTOLOGY, AND THAT IS THE TRAP THE ROW WALKS INTO.**
`Fern.cpp:4750` writes `data["budget_hit"] = res.budgetHit;` unconditionally, so *every* reply
carries it — including replies from the **bytecode** path, where `budgetHit` is structurally always
false because no disassembler ran. Asserting the key on a Blueprint function proves nothing at all.
`tools/verify/af7_budget_hit.py` therefore refuses to pass unless `method == "disasm"`.

**Measured, DumperTest / dist 3313 — and it is EXHAUSTIVE, not a sample:**

| | |
|---|---|
| functions on the host | 3,142 |
| carrying `FUNC_Native` (0x400) | **3,109** |
| walked with `walk_function_props` | **all 3,109** |
| `method` distribution | `{'disasm': 3109}` — Path 2 ran on every one |
| replies missing the key | **0** |
| replies with `budget_hit == true` | **0** |

**So the row's stated PASS is met, at the strongest strength available**: the key is present on
3,109 replies from the path where it is meaningful.

⚠ **AND `false` EVERYWHERE IS NOT EVIDENCE THE FLAG WORKS** — it is exactly what a hardwired
`false` produces. The mechanism is genuinely reachable (`Denken.cpp` follows up to `kMaxFollow = 6`
thunk→impl handoffs and budgets 8,192 decoded instructions across all of them), DumperTest simply
has no call graph that large. The log marker the row suggests grepping for
(`AnalyzeNativeFunctionProps … BUDGET`) appears **nowhere** in this host's logs.

▶ **So the consequence half was pinned where it CAN be pinned — offline**, rather than left waiting
for a game nobody has profiled yet:

| new test | asserts |
|---|---|
| `BatchXrefCancelVsDisconnectTests.Functions_props_batch_marks_a_budget_truncated_row_partial_in_the_cell` | a truncated walk writes the `⚠ partial` marker into the Uses cell |
| `…_leaves_an_untruncated_row_unmarked` | **the control** — an untruncated walk writes exactly `"0"` |
| `PartialResultNoticeTests.DisassemblyBudget_SaysWhatWasTruncatedAndWhatTheAbsenceMeans` | the dialog's sentence contains "hit its instruction budget", "PREFIX", and "not seen yet" |

⭐ **The pair matters more than the marker test.** "The marker appears" alone is satisfied by a
marker appended unconditionally, which would make every row look truncated and the column useless —
the same one-directional-assertion failure the Dump Explorer log check hit an hour earlier.

**Three negative controls run, each firing on exactly one test, all reverted byte-exact:**

| control | fails |
|---|---|
| `var partial = "";` (never appended) | the marks-partial test |
| `var partial = PartialResultNotice.CellMarker;` (always appended) | the control test |
| drop "not seen yet" from the notice | the notice test |

ℹ️ **What is still not shown**: that a real `budget_hit == true` ever occurs in the wild, and that
`FunctionPropsDialog` renders amber (`#E0A050`, `FunctionPropsDialog.cs:370`) — it is a code-built
dialog, not a VM. Both are opportunistic: anyone profiling a large commercial title can settle them
in one click with `py tools/verify/af7_budget_hit.py --attach`, which reports any function that
trips the budget and names it.

### ✅ CLOSED 2026-08-22 `[DUMPXGAME-2026-08-22]` — the Dump Explorer cross-game gate was already pinned; only its diagnostic was not

⭐ **The row asked for two games and an opportunistic wait for a patch. It needed neither, and
almost all of it was already done.** Filed under 第 2 步 as "export from game X, connect to game Y",
with step 3 marked *（機會性，等 X 真的更新版本後）* — wait for a real title to ship an update.
`DumpExplorerTests` already covers every branch of the gate, headless, with `FakeDumpService`
supplying the live identity:

| the row asks | already asserted by |
|---|---|
| step 2 — a different game is refused, both module names named, no row matched, nothing to jump to | `Vm_LiveMatch_RefusesWhenTheConnectedGameIsADifferentModule` — `LiveChecked` false, `Matched` empty, every row `IsMatched=false` **and** `LiveAddr==""`, status contains "refused" plus both names |
| step 3 — same game, different build: still matches, but says "Different build" | `Vm_LiveMatch_SameModuleDifferentBuild_StillMatchesButSaysSo` — **staged with a pe_hash, so no patch has to ship** |
| (the control the row does not ask for) | `Vm_LiveMatch_SameModuleAndBuild_MatchesWithNoCaveat` — matched, and *none* of "refused" / "Different build" / "identity unknown" |
| (two branches the row does not mention) | `..._NoPeHashInDump_...` (a pre-`pe_hash` file matches but must not claim identity was checked) and `..._IdentityProbeFails_SkipsRatherThanMatchingBlind` |

⚠ **THE ONE REAL GAP WAS THE LINE THE ROW NAMES BY NAME.** It requires
`DumpExplorer live match refused: dump module '…' != live module '…'` in the log, and the tests ran
on a `NoopLogger` that discarded everything — so the status text was pinned and the *diagnostic* was
not. That matters more than it looks: the status line is transient and the next action overwrites
it, so the log is the only record that survives long enough to explain a refusal after the fact.

**Added**: the test logger records warnings (both the 1-arg and the category overload — a routed
call would otherwise slip past), the refuse test asserts a single matching warning naming **both**
quoted modules, and a new paired test `Vm_LiveMatch_Accepted_LogsNoRefusal` asserts an accepted
match logs none.

⭐ **The pair is the point, and it is the failure mode of every "the log says X" check written in
one direction only**: a diagnostic emitted unconditionally satisfies the refuse test and is useless
in the field. **Both controls run and reverted byte-exact:**

| control | result |
|---|---|
| delete the `_log.Warn` line | refuse test fails — `Assert.Single()`: *the collection did not contain any matching items* |
| hoist the `_log.Warn` above the tier-1 branch so it always fires | **both** fail — refuse on `Assert.Single()`: *contained 2 matching items*, and the accepted test on `Assert.DoesNotContain()` |

16 / 16 in the class.

ℹ️ Step 1 ("export a dump from game X") is not separately pinned and does not need to be — the
reader has a real-world fixture (`Reader_ParsesRealWorldDumpFixture`) and every other test in the
file loads a produced file. What no unit test reaches is that the status *renders*; that is the
standard D0 residue and it is not specific to this row.

### ✅ B19 PASS 2026-08-22 `[B19-LOCKED-2026-08-22]` — a locked archive no longer stops the retention sweep

**The defect, in the fix's own words** (`Sein.cpp:268-274`): `PruneAgedLogs` shared **one**
`std::error_code` between the directory iteration and the per-file `fs::remove`. A failed remove set
`ec`, the loop's `if (ec) break` ended the sweep — and NTFS enumeration order is stable, so it ended
it at the **same entry on every launch**. One undeletable file switched the advertised 21-day
retention off for everything after it, permanently and silently.

⭐ **THE TEST HAD TO BE ABOUT ORDER, NOT ABOUT THE LOCKED FILE.** "The locked file survived" is true
under the fix, true under the defect, and true when the sweep never ran at all — it is the assertion
the row's own wording invites and it decides nothing. `tools/verify/b19_locked_log.py` stages three
archives, all aged 40 days against a 21-day window, in an **asserted** enumeration order:

| staged | expected | what it rules out |
|---|---|---|
| `b19a-…` aged, unlocked, **before** the lock | deleted | a sweep that never ran reading as a pass |
| `b19b-…` aged, **LOCKED** | survives | a lock that never held reading as one |
| `b19c-…` aged, unlocked, **after** the lock | **deleted** | ⭐ **the witness** — this is the file the old code abandoned |

⚠ Two preconditions the rig checks rather than assumes: it **attempts the delete itself** first and
requires a `PermissionError` (a lock that does not bite makes the whole run vacuous — Python's
`open()` on Windows omits `FILE_SHARE_DELETE`, which is why it works), and it lists the directory
through the same `FindNextFileW` walk `fs::directory_iterator` uses and **refuses to run** unless it
really sees a → b → c.

**Result** (DumperTest, dist 3313): `a` deleted, `b` still there, `c` **deleted**. PASS.

⭐ **NEGATIVE CONTROL RUN, and it is the good kind — it reproduces the historical defect rather than
a contrived edit.** Two lines in `Sein.cpp` restore the old *observable* behaviour (hoist `ec` out
of the per-file lambda, `if (ec) return;` at its top), rebuild, re-run:

```
a  deleted        <- the sweep still ran
b  STILL THERE    <- the lock still held
c  STILL THERE    <- FAIL, exactly and only arm (c)
rig exit code 1
```

The failure localises to the one arm that distinguishes the two implementations, which is what makes
the PASS mean something. Source restored **byte-exact** (working-lessons §2.11) and rebuilt;
`build_number.txt` untouched at 3313 via `-NoBumpBuildNumber`.

ℹ️ The three files are written into the real `Logs\DumperTest\` folder because that is the only
folder the DLL sweeps. They carry a `b19`-prefixed name nothing else produces and the rig removes
whatever survives in a `finally`, including on failure.

### ✅ MIRROR SWEEP 2026-08-22 `[ZHTW-SWEEP-2026-08-22]` — 50 → 43 sections, none of it new verification

⭐ **Seven sections closed and not one of them needed a game.** `AF21` had been left behind after its
own PASS was recorded; the other six were already settled by work this register had done and never
mirrored. This is the cheapest kind of progress on the list and it is invisible unless somebody
mechanically compares the two files — which is what produced it.

**The sweep.** For every section heading in the 繁中 file, extract its ids and look for a `✅` / PASS
/ VERIFIED heading in `todo.md` naming the same id. That is a *candidate* list, not an answer: most
hits are per-STEP (`Step 3 CLOSED`, `STEPS 4–8 PASS`, `HALF`), and the mirror's headings already say
`只剩步驟 X` for those. Each candidate was then checked step-by-step against its own row.

| section | why it closed |
|---|---|
| `AC17` | `[VOLUMEROOT-2026-08-19]` — fixed AND verified, names AC17, `VolumeRootTests.cs:161` |
| `AF21` | `[AF21-HIDPI-2026-08-21]` PASS; re-ran the rig on 3313, all three arms |
| `A11` | `[A11-MUTATE-2026-08-21]` — "STEPS 2, 3, 4, 5 ALL PASS", plus 1 and 6 |
| `A12` | `[A12-…-2026-08-21]` — "STEPS 1, 4a, 5, 6" + "STEPS 2, 3, 4 ALL PASS" |
| `V11` | `[V11-SYM-2026-08-20]` — all four card × outcome combinations, which is steps 1-3 |
| `Y12` | `[Y12-CLIP-2026-08-20]` covers step 1; step 2's wording verified at source — `InvokeParamDialog.cs:912/:919` say "AA Script copied as CE XML … paste into CE's address list" in both branches |
| `W8` | `[W8-USMAP-2026-08-20]` covers steps 1-2 (507 of 513 BP classes present). Step 3 is "if you happen to have FModel/CUE4Parse installed" — that parser check is `W1 / W7`'s own row, which stays open, so nothing is lost |

⚠ **Four candidates were REJECTED on inspection** and their sections stay: `A11`/`A12` looked
ambiguous at first because their entry headings say CLOSED while the entry text says *"the WIRING is
not [unit-pinned]"* — they only survived scrutiny because the same entry carries the later
mutation-harness results. `AE4`, `AF7/AF8`, `A6`, `AE2/AE3`, `G2`, `G11`, `MB3`, `AA2/AA3`, `D2`,
`U3/U17` and others all matched the grep and are all per-step. **A ✅ naming an id is a lead, not a
verdict.**

⚠ **The table was RECOUNTED from the file, not hand-adjusted** — the script walks the `## 第 N 步`
headings, counts the `###` under each, rewrites all five numbers and the total, then asserts the
derived total matches. 第 1 步 2 · 第 2 步 17 · 第 3 步 8 · 第 4 步 14 · 第 5 步 2 = **43**.
Hand-editing that table is how it drifted before.

▶ **Worth re-running whenever a batch of register work lands.** The mirror goes stale silently and
in one direction: a row that closes in `todo.md` leaves a section here that looks like open work
forever.

### ✅ FOUND + FIXED + LIVE-VERIFIED 2026-08-22 `[CADENCEGAP-2026-08-22]` — Linie dropped every same-millisecond gap, and that MANUFACTURED the "Timer" badge

⭐ **Found while establishing a PRECONDITION, not while hunting.** The row "Period must sort
numerically (16.7 ms above 1000 ms)" cannot be tested unless the profiler actually produces two
values whose numeric and string orders disagree, so `tools/verify/livefuncs_period_fixture.py` went
to ask what it produces. The answer was six functions all at 66.7 ms — and one of them with a fire
count that did not match its own period.

**What was happening.** `Linie::RecordCall` guarded the Welford update on `nowMs > s.lastMs`, and
`nowMs` is a `steady_clock` reading truncated to **milliseconds** (`Stark.cpp:103-107`). Two fires
inside one millisecond compare EQUAL, so the gap was dropped — **and `s.lastMs` was left pointing at
the first of the pair**, which is the half that does the damage: the *next* gap then spanned both
fires and read as a whole frame.

**Measured**, DumperTest at `t.MaxFPS 60`, 10.00 s window, `tools/verify/linie_cadence_gap.py`:

| | count | gap_samples | reported | implied | ratio |
|---|---|---|---|---|---|
| **before** `CameraModifier::BlueprintModify*` ×2 | 1202 | **602** | 16.61 ms | 8.33 ms | **1.99×** |
| **before** the four once-per-frame functions | 600 | 599 | 16.67 ms | 16.70 ms | 1.00× |
| **after** `CameraModifier::BlueprintModify*` ×2 | 1200 | **1199** | 8.33 ms | 8.34 ms | 1.00× |
| **after** the four once-per-frame functions | 600 | 599 | 16.67 ms | 16.69 ms | 1.00× |

⭐ **The control was free and it is what makes this solid.** Four of the six fire once per frame and
two fire twice. A clock error or a mis-measured window moves all six together; this moved exactly
the two, and `gap_samples` — which must be `count - 1` — sat at `count / 2` for precisely those two.
The four were exact before AND after, so the fix regressed nothing.

⚠ **The guard was DELIBERATE, which is why this is easy to misread as correct.** It is the
prescribed fix for audit #3 finding **L5** (`docs/audit-2026-07-14-findings.md:343`), about
REORDERED timestamps: `nowMs` is stamped before `RecordCall` takes the lock, so multi-thread PE can
deliver two fires out of order and an unsigned `nowMs - s.lastMs` would underflow to a ~1.8e19 gap
that poisons the mean for the rest of the window. **That hazard is real and the fix keeps it out.**
But a reorder is `nowMs < s.lastMs`; `nowMs == s.lastMs` is not a reorder. `>=` excludes the
underflow just as completely and keeps the sample. L5 said "strictly greater" because it was
reasoning about reordering only — dropping the equal case was collateral.

⭐⭐ **THE WRONG NUMBER WAS THE SMALLER HALF.** Dropping the ~0 ms gaps **manufactured** the
regularity the "Timer" badge keys on: the surviving gaps were all exactly one frame apart, so `cv`
read **0.007** and a twice-per-frame render callback scored as a textbook periodic timer — the exact
distinction Linie's cadence phase exists to draw. Predicted from the mechanism *before* measuring
(an alternating 0 / 16.7 ms sequence must give cv ≈ 1.0); measured after the fix: **cv = 1.002**,
and the DLL's own summary line went from `6 periodic-looking` to `0 periodic-looking` at 60 FPS,
which is correct — none of those six is a timer.

▶ **Same root cause as `[SOLIDEHELD-2026-08-21]` the day before, and as audit #4's headline: the
report and the reality computed by different paths.** `count` and `mean_period_ms` are the same fact
stated twice and were allowed to disagree by 2×. Worth a sibling sweep: any other place that
accumulates a derived statistic beside a raw counter without either being checked against the other.

ℹ️ **No test target compiles `Linie.cpp`**, so `-Target Test` measures nothing about this change —
and running it would have overwritten `dist/UE5DumpUI.exe` with the non-trimmed build for no gain.
`-Target DLL` proves it compiles; the rig proves it works.

-----

### ✅ CLOSED 2026-08-20 `[PEHOOKONCE-2026-08-18]` — a failed ProcessEvent detection must now be RE-ARMABLE

> **All five steps verified** — 1-4 headless (`[PEHOOKONCE-LIVE-2026-08-20]`), step 3's literal
> pre-scan form (`[PEHOOKONCE-3-2026-08-20]`), and step 5 in the UI (`[PEHOOKONCE-5-UI-2026-08-20]`).

*Was: a detection that failed because there was nothing to detect **yet** stored the same `-1` as a
hard failure, and every retry path in `Frieren.cpp` was gated against `-1` — so one
`pe_profile_start` before the first scan poisoned the PE hook for the whole process, and the message
told the user to retry the one thing that could not work. Now: three distinct sentinels
(`Stark::kPeOffsetNotDetected` = re-armable, `kPeOffsetFailed` = terminal, `>=0` = known), one
serialized detection entry point with its own bounded/rate-limited retry budget, separate from the
MinHook install-retry budget. **The rules are unit-pinned in `dll_helpers_test` (27 assertions across
`Test_Stark_PeOffsetSentinels` / `ShouldRetryPeDetection` / `PeValidationFailureVerdict`; the WIRING
is not — no target compiles `Frieren.cpp`).** Negative control run: forcing
`ShouldActOnValidationFailure` to `return true` — i.e. "act on every zero", the actual defect the
asymmetry prevents — failed exactly the 3 false-positive-guard assertions and nothing else. (Note for
anyone repeating it: *inverting* the predicate instead fails all 8 in that function, because
`PeOffsetAfterValidationFailure` gates on it too.) Step 2 is the whole point: it is the exact
order-swap that was permanently broken.*

> Needs a **proxy-mode** title (the DLL must start pipe-server-only, so GObjects is unset until a
> scan). Drive it headless with `tools/verify/` — no GUI needed for steps 1–4.
> Grep `init-0.log` by FORMAT STRING: `no UObject vtable available yet`, `offset resolved to
> vtable+`, `first-time init complete`.
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> ### ✅ STEPS 1–4 PASS 2026-08-20 `[PEHOOKONCE-LIVE-2026-08-20]` — headless, on Lushfoil (proxy mode)
>
> Rig: `tools/verify/pehookonce.py`. Proxy mode is required and was confirmed each run — GObjects
> read `not_found` before the scan, so the "nothing to detect yet" window genuinely existed.
>
> * **Step 1 — PASS.** `pe_profile_start` before any scan → `hook_active: false`, and the detail is
>   the new wording: *"ProcessEvent is not resolved yet, and detection is still **ARMED** — this
>   attempt changed nothing. … Run a scan, then Start again; init-*.log tells you which"*. The old
>   advice told the user to retry the one thing that could not work; this names the actual remedy.
> * **Step 2 ⚠ THE ONE THAT MATTERS — PASS.** In the **same process**, after the poisoning attempt:
>   `trigger_scan` (GObjects → `0x7FF79F039B40 aob`) → `teleport_get_pov` (`ok`, code 0) →
>   `pe_profile_start` again → **`hook_active: true`**. This is the exact order-swap that used to
>   poison the hook for the whole process; it now recovers.
> * **Step 4 — PASS (non-regression).** A **fresh** Lushfoil, normal order, → `hook_active: true`
>   and `ProcessEvent: offset resolved to vtable+**0x260** via the pattern scan` — the offset this
>   row names.
> * ⭐ **The two runs differ by exactly one retry, which is the fix expressed as a number:**
>
>   | order | log line |
>   |---|---|
>   | normal (step 4) | `offset resolved to vtable+0x260 … (**detection run 0/8**)` — first attempt |
>   | profiler-first (steps 1→2) | resolved at **`detection run 1/8`** — it re-armed **once** |
>
>   Under the defect the second case had no run 1 at all: the `-1` was terminal.
> * **Step 3 — partially covered, and stated honestly.** Only **one** `detection run N/8` line
>   exists per session, so there is no retry storm. ⚠ But the row asks to grep *after step 1 and
>   before any scan* (expecting **zero**); this grep was taken **after** the scan, where exactly one
>   run is the correct and expected result. The no-storm property holds; the literal pre-scan-idle
>   form was not run in isolation.
> * ### ✅ STEP 5 NOW RUN 2026-08-20 `[PEHOOKONCE-5-UI-2026-08-20]` — the UI path, on screen
>   Fresh **Lushfoil** (proxy mode) + the real UI. ⚠ The pre-scan window is real and visible in the
>   UI, not just over the pipe: on connect the status bar reads **"Connected — waiting for scan"**
>   with a **Start Scan** button still unpressed, so the UI does *not* auto-scan a proxy host.
>
>   **First Start, before any scan** — the recorder arms, and the detail is the actionable one:
>   > *ProcessEvent is not resolved yet, and detection is still ARMED — this attempt changed nothing.
>   > Either no scan has run in this process (a proxy DLL starts the pipe server only, so there is no
>   > UObject to read a vtable from), or a detected slot was rejected because the hook never fired.
>   > **Run a scan, then Start again**; init-\*.log tells you which — 'no UObject vtable available
>   > yet' vs 'VALIDATION FAILED'.*
>
>   **Then Start Scan** (UE506, **58,619 objects**) and **Start again — WITHOUT restarting the game**,
>   which is the whole point of the step:
>   ```
>   67 distinct functions, 98,236 total calls
>   TotemActor_C.ReceiveTick                         8,568   Event    18 ms
>   InteractibleItem_C.ExecuteUbergraph_Interacti…   8,103            18 ms
>   BrushBinding.GetValue                            5,604   native   21 ms
>   FirstPersonCharacter_C.ExecuteUbergraph_First…   5,036            18 ms
>   ```
>   ⇒ The order-swap that used to poison the PE hook **for the whole process** now recovers inside
>   one process, and the user-visible surface says so at each stage. Kind badges (`Event` / `native`
>   / `UI`) and the Period column are populated too, so the Phase-E cadence data is reaching the grid.
>
>   **`PEHOOKONCE` steps 1–5 are now all verified.**
> * ### ✅ STEP 3'S LITERAL PRE-SCAN FORM NOW RUN 2026-08-20 `[PEHOOKONCE-3-2026-08-20]`
>   The note above is right that the earlier grep was taken *after* the scan. Re-run in the form the
>   row specifies — `tools/verify/lushfoil_pehook_batch.py`, fresh Lushfoil:
>   * **precondition asserted, not assumed:** `gobjects='0x0'`, `objects=0` before anything — the
>     "nothing to detect yet" window genuinely existed, so the zero below is meaningful;
>   * `pe_profile_start` **before any scan** → `hook_active=false` with the ARMED wording;
>   * then **268 polls of `get_diagnostics` over 60 s** (the 10 Hz feature the row asks for);
>   * ⇒ **0** new `detection run N/8` lines and **1** `no UObject vtable available yet`
>     (`Detection stays ARMED`). Nothing to detect spent no retry, and said so exactly once.


> | 1 | fresh launch, proxy mode. `init` → `pe_profile_start` **before any scan** | `hook_active:false` and `hook_detail` starts **"ProcessEvent is not resolved yet, and detection is still ARMED"** and names BOTH causes (no scan / slot rejected). It must NOT say "do any invoke first" | the old text was unreachable advice by construction on this path. It must also not name only the no-scan cause — the same sentinel carries a re-armed rejection |
> | 2 ⚠ THE ONE THAT MATTERS — the negative control | in the SAME process, now `trigger_scan` → one invoke (`teleport_get_pov`) → `pe_profile_start` again | **`hook_active:true`** | this exact ordering returned `false` **permanently** before the fix; a live game is the only thing that can prove it converges |
> | 3 ⚠ NO STORM | after step 1, leave the process idle ~60 s with a 10 Hz feature running, then grep `init-0.log` for `detection run` | **zero** `detection run N/8` lines (nothing to detect ⇒ no run is spent), and **at most one** `no UObject vtable available yet` | ⚠ the single `no UObject vtable` line proves only the one-shot log guard (`s_loggedNoVtable`) and would still be 1 with the cooldown deleted — it is `detection run` that counts actual detection RUNS, so that is the line the anti-storm rule is measured on |
> | 4 ⚠ NON-REGRESSION | a known-good title (**Lushfoil**), normal order: `init` → scan → invoke → `pe_profile_start` | `hook_active:true`, and `ProcessEvent: offset resolved to vtable+0x260 via the pattern scan (detection run 1/8)` | one detection run, pattern path, unchanged behaviour |
> | 5 | UI path: Live Funcs → **Start** before running a scan, then run a scan and press Start again **without restarting the game** | first Start reports the "run a scan" detail; second Start records | this is the user-visible half; before the fix only a game restart recovered |

-----

### ✅ VERIFIED 2026-08-17 `[F9-PIPE-2026-08-17]` — F9: walk_world must list actors AND their components (build 3247)

**All six steps PASS**, on **DumperTest Development** — one of the two titles that originally
reproduced `actor_count: 0`, so it is a discriminating sample and not a fresh one. Driven over the
pipe with `tools/verify/pipe_client.py` on build **1.0.0.3262**; **no UI was involved, so this says
nothing about the Live Walker's own bindings** — only that the DLL now returns the right payload.

* **1 — PASS.** `walk_world` returns `actor_count: 58` on the stock ThirdPersonMap. The defect was
  `0` here.
* **2 — PASS.** `actor_count: 58` == `actor_total: 58`, `truncated: false`.
* **3 — PASS (the gate).** Zero rows whose name contains `ModelComponent` or `ActorCluster`, over
  all 58. Both ARE outered to the level, so their absence is what shows the is-Actor gate ran rather
  than a bare outer comparison.
* **4 — PASS (the half the finding did not mention).** 53 components across 47 of 58 actors.
  `BP_ThirdPersonCharacter_C` lists six — `PawnInputComponent0`, `CollisionCylinder`,
  `CharacterMesh0`, `CharMoveComp`, `CameraBoom`, `FollowCamera` — i.e. the non-reflected
  `OwnedComponents` TSet is now read correctly.
* **5 — PASS, and checked INDEPENDENTLY of the payload under test.** `walk_world`'s component
  entries carry only `addr`/`class`/`name`, so the Outer cannot be read off the same reply that is
  being verified. Asked the DLL separately via `get_related_objects` for each of the six: all six
  report `Outer -> BP_ThirdPersonCharacter_C_2147482479`, the actor they were listed under. 6/6.
* **6 — PASS, with a stated substitution.** No large streamed map was used; `limit=10` against this
  58-actor level gives `actor_count: 10`, `actor_total: 58`, `truncated: true`. That is the same
  count-past-the-cap path, but it is **not** a streaming-map test and must not be read as one.

*No defect found. One false alarm of my own is worth recording so it is not re-raised: the actor
rows looked like they had a null class until I noticed I was reading `class_name`; the field is
`class`, and all 58 carry it.*

> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | 1 | connect, Live Walker → **Load GWorld** | a **non-zero** actor list | `actor_count: 0` on 2 of 2 games was the defect; anything non-zero is new behaviour |
> | 2 | compare `actor_count` with `actor_total` on a small map | equal, and `truncated` false | `actor_total` is now the level, not the page |
> | 3 ⚠ THE GATE, observed in the wild | scan the list for `ModelComponent*` / `ActorCluster` rows | **none** | those ARE outered to the level. Seeing them means the is-Actor gate is not running and the list is outer-only |
> | 4 ⚠ THE HALF THE FINDING DID NOT MENTION | expand an actor that obviously has components (a Character) | **components listed** | this loop had never executed in production; `OwnedComponents` is a non-reflected TSet that was being read as an ArrayProperty |
> | 5 | check a listed component's Outer | it is the actor it is listed under | the one-hop ownership test is what keeps shared objects (the world, another actor) out |
> | 6 | on a big streamed map, set `limit` low | `actor_count` == limit, `actor_total` > it, `truncated` true | the count-past-the-cap path |

⚠ **A benign over-report is EXPECTED, not a failure**: the list is derived from the Outer
back-reference, so it can include an actor already destroyed but not yet garbage-collected, and it
is **not** in the engine's array order.

-----

### ✅ COMPLETE 2026-08-20 `[AA38-PYTHON-2026-08-17]` + `[AA38-MODULAR-2026-08-20]` — AA38: a GWorld must not be reported on a process with no object pool (build 3245)

**ALL FIVE STEPS PASS.** Steps 1/2/3/5 on 2026-08-19 (re-confirmed on 3263), and **step 4 on
2026-08-20 against Satisfactory** — see `[AA38-MODULAR-2026-08-20]` below. This row is complete.

Run on build **1.0.0.3262** (`05a9af58-dirty`), confirmed from the injected DLL's own
`Logger started` line rather than assumed — `dist/build_number.txt` agrees, so §2.6's stale-proxy
trap is excluded. Neither the sleeper nor DumperTest carries a proxy, so the injection genuinely
scanned.

* **5 (done first, or the rest proves nothing).** Deleted `67F515A70001A000` from
  `UE5CEDumper.{COMPUTERNAME}.json` — it cached `gWorld: aob/GWLD_V3` with `gObjects`/`gNames`
  `not_found`, i.e. exactly the hint that would let the run resolve MAIN-module *and be accepted by
  design*. File backed up, edited by a `json` round-trip; the Solarpunk and DumperTest control
  entries were left intact and re-checked afterwards. Step 1 was therefore a cold scan.
* **1 — PASS.** `python.exe` sleeper, PID 26292:
  `FindAll: Complete — GObjects=0x0 (not_found), GNames=0x0 (not_found), GWorld=0x0 (not_found)`.
  **The before/after pair is from the same host**: the archived 2026-08-15 runs of this same
  `python.exe` recorded `GWorld=0x7FFB4595D5A8 (aob)` alongside `GObjects=0x0` — the defect itself.
* **2 — PASS, and it is the *unanchored* wording**, which is the half that matters:
  `[GWorld] GWLD_V3: REFUSED 7 match(es) resolving to 0x7FFF47461760 in 'atcuf64.dll' — GObjects
  never validated this run, so nothing has confirmed this process is the UE process; a match in an
  arbitrary loaded module is not admissible`. The module is named, and this is the branch that
  asserts only what the run established — not the monolithic sibling, which would have claimed more.
* **3 — PASS.** Non-regression on DumperTest-Shipping (PID 38764), whose hint entry
  `E1AAB613081BC000` was left in place: all three resolve by `aob` and the winners are
  **`GOBJ_V13` / `GNAM_V8` / `GWLD_TQ_1`** — identical to the cached ids. Addresses differ from the
  prior run (ASLR), which is why the comparison is on pattern id + method, as this row instructs.
* **4 — NOT TESTED.** Needs a modular-build title (GNames in `CoreUObject.dll`). Satisfactory is
  installed and is the shaped candidate, but it was not scanned; do not read the ✅ as covering
  `AnchorState::ForeignDll`.

⚠ The second reproducing sample (the Solarpunk launcher shim, `C9E9551B0003D000`) was **not** run —
one sample plus the reverse control was judged sufficient. Its hint entry is untouched if anyone
wants it.

> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | 1 | inject into `python.exe` (or any small non-UE exe) and let the scan finish | `FindAll: Complete` shows `GObjects=0x0` **and** `GWorld=0x0` | before 3245 GWorld was published from an arbitrary loaded module on exactly this run |
> | 2 | grep `scan-0.log` for `GObjects never validated this run` | the REFUSED line appears, naming the module | the refusal must state the UNANCHORED reason, not the older monolithic-build text, which asserts something this run has not established |
> | 3 ⚠ NON-REGRESSION | re-scan a normal game that already resolves all three (DSClient / TQ2 / Elliot) | same winning pattern id and same method as its current `scan-0.log` | compare pattern id + method, **not** literal addresses — those are not stable across launches |
> | 4 ⚠ NON-REGRESSION, the modular case | if a modular-build title is available (GNames in `CoreUObject.dll`, Satisfactory-shaped), re-scan it | GNames still resolves out of the DLL | `AnchorState::ForeignDll` must still accept; this is the case multi-module scanning exists for |
> | 5 | clear the per-PE-hash hint cache entry for `python.exe` first | step 1's run does a cold scan | with a cached `GWLD_V3` hint the run can resolve MAIN-module and be accepted by design, which would not disprove anything |

> ### ✅ RE-CONFIRMED on the SHIPPING build `[AA38-3263-2026-08-19]`
>
> The ✅ above was earned on **1.0.0.3262**. Steps 1, 2 and 3 were re-run on **1.0.0.3263** — the
> build that actually ships — and all three still PASS. Step 5's cold-cache precondition was redone
> first: the `67F515A70001A000` entry (which the 2026-08-17 run had re-created, all `not_found`) was
> deleted again, machine JSON backed up beforehand, and the `C9E9551B0003D000` / `E1AAB613081BC000`
> control entries were re-checked present afterwards.
>
> * **1 — PASS.** `python.exe` sleeper, PID 62288, DLL `1.0.0.3263 10b00cf8-dirty`:
>   `FindAll: Complete — GObjects=0x0 (not_found), GNames=0x0 (not_found), GWorld=0x0 (not_found)`.
> * **2 — PASS, still the *unanchored* wording.** `[GWorld] GWLD_V3: REFUSED 7 match(es) resolving to
>   0x7FFF47461760 in 'atcuf64.dll' — GObjects never validated this run, so nothing has confirmed
>   this process is the UE process; a match in an arbitrary loaded module is not admissible`.
>   ⚠ **Note which module it names**: `atcuf64.dll` is **Bitdefender's own Active Threat Control
>   filter**, injected into every process on this machine. That is a better adversary than a random
>   DLL — it is present in *every* future run here, so this refusal is load-bearing on this PC and a
>   regression would reappear immediately rather than intermittently. `GWLD_V3` had **148** raw hits.
> * **3 — PASS (non-regression).** DumperTest-Shipping, PID 59460, hint entry left in place:
>   `GOBJ_V13` / `GNAM_V8` / `GWLD_TQ_1`, all method `aob` — **identical pattern ids and methods** to
>   the cached entry, which is the comparison this row mandates. Addresses differ from 2026-08-17
>   (ASLR), exactly as the row predicts. `scanCount` 11 → 12.
> * **4 — ✅ PASS 2026-08-20 `[AA38-MODULAR-2026-08-20]`, on Satisfactory. AA38 IS NOW COMPLETE.**
>   A genuine modular build: **184 engine DLLs** beside the exe and **607 loaded modules** at
>   runtime. Every global resolved by the **symbol/export** path, and — the assertion — **not one
>   of them lives in the main module**:
>
>   | target | address | method / pattern | owning module |
>   |---|---|---|---|
>   | GObjects | `0x7FFCCA033620` | `symbol` / `GOBJ_EXP` | `…-CoreUObject-Win64-Shipping.dll` |
>   | GNames | `0x7FFCCA8BD8C0` | `symbol_call_follow` / `GNAM_EXP_TOSTR` | `…-Core-Win64-Shipping.dll` |
>   | GWorld | `0x7FFCC88CCB88` | `symbol` / `GWLD_EXP` | `…-Engine-Win64-Shipping.dll` |
>   | GEngine | `0x7FFCC88CF768` | `symbol` / `GENG_EXP` | `…-Engine-Win64-Shipping.dll` |
>   | *(main)* | | | `FactoryGameSteam-Win64-Shipping.exe` — **holds none of them** |
>
>   So `AnchorState::ForeignDll` accepted matches across **three different foreign DLLs**. The DLL
>   says so itself: `Module anchor set to 'FactoryGameSteam-CoreUObject-Win64-Shipping.dll' — later
>   targets must resolve there **unless this build is modular**`, and the scan log contains
>   **0** `REFUSED` / `not admissible` lines.
>   ⭐ **This is the non-regression AA38 most needed.** The fix's whole job is to refuse a match in
>   an arbitrary module *when nothing has confirmed the process* — a blunt version would have
>   refused this legitimate cross-DLL case too. It discriminates: GObjects validated first, so the
>   foreign-module matches were admitted.
>   Fully functional afterwards: `probe_ran/validated` true, `use_fproperty=true`, `item_size=24`,
>   **137,372 objects**, `walk_world` ok with 200 entries, `find_instances Actor` = 500.
>
>   ⚠ **Launch note — the shipping exe cannot be started directly.** It dies with *"Failed to open
>   descriptor file ../../../FactoryGameSteam/FactoryGameSteam.uproject"* (that folder does not
>   exist; the game ships `FactoryGame\`). Launch the **top-level `FactoryGameSteam.exe`**, which
>   then relaunches into the shipping exe under a *different* PID — so resolve the PID by name
>   **after** the handoff, twice if necessary.
>   ⚠ **Second >5,000-class title, corroborating `[CLASSTOTAL]` beyond Avowed**: `list_classes`
>   returns page **5,000** / `total_classes` **5,171** / `truncated` **true**.
>   ⚠ **`AB12` precondition still unmet**: 607 loaded modules is the most on this machine, still
>   short of the **>1024** that row needs.
>
> * ~~**4 — STILL NOT TESTED.**~~ ⚠ Correcting the note above: **Satisfactory IS installed**
>   (`/d/SteamLibrary/steamapps/appmanifest_526870.acf`), so the modular-build case is *available*,
>   not merely "shaped". It needs a real game launch, so it belongs to a title group, not to the
>   headless batch — but it is no longer blocked on finding a host.
>
> Injected with the new **`tools/verify/inject.py`**, not `dist/inject-ue.ps1` — see that file's
> docstring for why (no ad-hoc PowerShell on this machine).

**Known residual, filed as AA39, not a failure here**: Pass 1 (main-module) is ungated. Injecting
into a LARGE non-UE monolithic exe can still publish a main-module GWorld.

-----

### ✅ CLOSED 2026-08-20 — ST1: our own direct calls must stop entering our own PE detour (build 3205)

> **All six steps verified, headlessly, on DumperTest Development / dist 3263 — no CE, no UI.**
> `1 + 2` `[ST1-PIPE-2026-08-20]` · `3` `[ST1-QUEUE-2026-08-20]` · `4 + 6` `[ST1-DRAIN-2026-08-20]` ·
> `5` `[ST1-FAILOPEN-2026-08-20]`. Rigs: `st1_direct_call.py`, `st1_queue_drain.py`,
> `st1_queued_drain_sideeffect.py`.
>
> ⭐ **The two things that made a "needs a played game" batch runnable with nobody present:**
> **suspending the UE game thread** is a scriptable, *stronger* form of every "paused / menu /
> idle game" precondition in the table (frozen means exactly 0 ProcessEvent fires, where
> backgrounded still ticks ~120/s), and where a log line could not decide a step, an **observable
> side effect in memory** could.
>
> One defect fell out of the work — `[INVOKEINHERIT-2026-08-20]`, filed separately.

*Needs a connected game. See dev-log build 3205. **The two predicates are unit-pinned (10 assertions,
two negative controls); the ROUTING is not** — nothing offline can observe which address a live
vtable actually holds, and no target compiles `Stark.cpp` or `Frieren.cpp`.*

> **The cheapest decisive check is the log line**, because the fix adds a distinguishable one.
> Grep by FORMAT STRING (never line number): `via trampoline — not re-entering our hook` vs the
> older `(caller-asserted safe)`.
>
> | step | do this | expect | why it is a real check |
> ### ✅ ST1 STEPS 1 + 2 PASS 2026-08-20 `[ST1-PIPE-2026-08-20]` — over the pipe, no UI and no CE
>
> Rig: `tools/verify/st1_direct_call.py`, DumperTest Development / dist 3263. The row says the
> cheapest decisive evidence is the **format string**, so that is what is matched.
>
> **Gate first (also `PEHOOK` step 8): `Add_IntInt(3,4)` = 7.** `result_hex` came back
> `030000000400000007000000` → ReturnValue **7**. Everything below is meaningless against a host
> whose invoke does not work, so the rig stops here on failure.
>
> **Step 1 — PASS.** The `direct_call: true` invoke logs
> ```
> UE5_CallProcessEventDirect: inst=0x243B6837C00 func=0x243AA704C00 pe=0x7FF69AF38CB0
>   (via trampoline — not re-entering our hook)
> ```
> and **0** `(caller-asserted safe)` lines — that wording belongs to step 5's fail-open path and must
> not appear here.
>
> **Step 2 — PASS, and the first two attempts at it were both unsound. This is the method that works.**
> ```
> game thread SUSPENDED  -> hook_fire_count 21264 -> 21264 over 2 s   (delta 0: background silent)
> direct_call invoke     -> ok = true                                  (the call really happened)
> hook_fire_count        -> 21264 -> 21264                             delta = 0
> ```
> ⭐ **Why suspend the game thread.** A running DumperTest fires ProcessEvent at **~121/s**, so the
> single `+1` this step is looking for is far inside the noise and `count_after > count_before` is
> guaranteed either way. Freezing the UE game thread drops the background to **exactly zero** — and a
> `direct_call` invoke does not need that thread, which is the entire point of the flag — so any
> movement at all is our call and nothing else. All three controls hold: the background is *shown*
> silent, the call is *shown* to have succeeded (so the zero is not "nothing happened"), and the
> delta is 0.
>
> ⚠ **Two rig defects found and fixed on the way; both would have produced a confident wrong answer.**
> * `hook_fire_count` lives on **`get_diagnostics`**, not `get_stats` — `get_stats` returns
>   `{ok:false}` here and its absent `game_thread` block reads as `None`, which made the first
>   version print **PASS having measured nothing**. The rig now aborts loudly on a `None` count or an
>   inactive hook.
> * The counter window must bracket the **counter reads too**. Timing only the invoke while
>   differencing across two `get_diagnostics` round trips charges those round trips to our call and
>   manufactured an "excess" of 6.6 fires — a **FAIL** that was purely the measurement. Bracketed
>   correctly, three consecutive runs gave 8 fires observed against ~23 expected.
> * ℹ️ `list_all_functions` defaults to **`game_only: true`**, so it returns 3,142 of 9,806 functions
>   and `KismetMathLibrary.Add_IntInt` is not among them — which reads as "this host has no invoke
>   target". Pass `game_only=false`.
> |---|---|---|---|
> | 1 | connect, run the Pointers-tab KismetMathLibrary self-test (`directCall: true`) | `pipe` log shows **`via trampoline`** | the ordinary path now bypasses the detour |
> ### ✅ ST1 STEP 3 PASSES 2026-08-20 `[ST1-QUEUE-2026-08-20]`; step 4 was not decidable from the log — SEE `[ST1-DRAIN-2026-08-20]` BELOW, which decides it
>
> Rig: `tools/verify/st1_queue_drain.py`. "A paused/menu game" is staged by **suspending the UE game
> thread** — nothing can service the queue, so the timeout is guaranteed, and the ~121 fires/s
> background drops to zero so the counter is readable at all.
>
> ```
> control  background while frozen      84632 -> 84632          (silent)
> 3a       ActorComponent.ReceiveBeginPlay (0x8020802: neither Static nor Native)
>          -> result -5 after 1.51 s against a 1500 ms timeout, and
>             "GameThreadDispatch: enqueued invoke inst=… , waiting..."   -> it IS queued
> 3b       direct (trampoline) invoke of KismetMathLibrary.Add_IntInt
>          -> ReturnValue = 7                      (the call really executed)
>          -> hook_fire_count 84632 -> 84632       delta = 0   -> the queue was NOT drained
> ```
> ⭐ **All three controls hold**, which is what makes the zero mean something: the background is
> *shown* silent, the first invoke is *shown* to have queued, and the second is *shown* to have
> executed. Pre-3205 that second call would have entered `HookedProcessEvent` on the pipe thread and
> run the abandoned request; it cannot now.
>
> ⚠⚠ **The second call MUST be `direct_call`, and an ordinary static-native invoke will mislead you.**
> Measured here: with the game thread frozen, `Add_IntInt` — `Native|Static`, the fast path the row
> names — returns **`-5 (game-thread dispatch timeout)`, `game_thread_stalled: true`**, not 7. So it
> never reaches ProcessEvent, never had the *opportunity* to drain, and its 0-delta would be
> **vacuous**. Only a `direct_call` reaches PE on the calling thread, which is the route the pre-3205
> drain actually took. (Note the contrast with `L4`'s MB1 row, which asserts the fast path survives on
> an **idle** game: an idle game still ticks; a *suspended* thread does not, and
> `IsGameThreadResponsive()` is false, so the two are not the same condition.)
>
> ⛔ **Step 4 is not decidable FROM THE LOG, and the reason is structural** (it *is* decided by the side-effect rig recorded below). After resume, "the queued
> request now runs" produces **no** `invoke completed` line — that line is written by the *waiting
> pipe thread*, which has already timed out and gone. What the same log does show is that the drain
> path works on a live game thread: `enqueued invoke … waiting...` → `invoke completed result=0` in
> **39 ms** when the thread is running. Deciding step 4 for the *abandoned* request needs a
> side-effect signal (a stateful UFunction whose result is readable afterwards), not this log line.
>
> ### ✅ STEPS 4 AND 6 NOW DECIDED 2026-08-20 `[ST1-DRAIN-2026-08-20]` — by the side-effect signal the note above asked for
>
> The paragraph above ends *"deciding step 4 for the abandoned request needs a side-effect signal
> (a stateful UFunction whose result is readable afterwards), not this log line."*
> `tools/verify/st1_queued_drain_sideeffect.py` is that rig.
>
> **Fixture, chosen so nothing about it is assumed:**
> * `Actor.SetActorHiddenInGame(bool)`, flags `0x04020402` — **Native but NOT Static**, and
>   `Mimic::ShouldRouteDirectInvoke` requires *both*, so it is guaranteed to take the queue rather
>   than the direct fast path. The rig asserts this and aborts if a future build changes it; a
>   `Native|Static` pick would have made the whole run vacuous.
> * `AActor::bHidden`, a packed bool at **+0x58 bit 7 (0x80)**, read with `ReadProcessMemory` — the
>   observable is memory, not a log line.
> * ⚠ the subject is **`ChaosDebugDrawActor`, whose class is literally `Actor`**, not a
>   `StaticMeshActor`. That is not a detail: `[INVOKEINHERIT-2026-08-20]` (filed from this very run)
>   means an inherited function cannot be resolved by name on a derived instance, and the first
>   attempt reported a false *"the drain WAS suppressed"* purely because the invoke had been
>   **rejected** rather than queued.
>
> ```
> bHidden before                      : False   (byte 0x62)
> [game thread FROZEN] invoke(true)   : ok=True result=-5 stalled=True   (5.0 s)  <- queued
> CONTROL: bHidden while still frozen : False                                     <- did NOT run
> [RESUMED] bHidden                   : True, within 0.0 s                        <- IT DRAINED
> new 'SEH exception during queued PE call' : 0
> new '0xC0000409'                          : 0
> restore invoke(false)               : result=0, bHidden False again
> ```
>
> **Step 4 — PASS.** The abandoned request executed on the game thread after the resume, witnessed
> by a memory bit rather than by an absent log line. The frozen-window control is what makes it
> attributable: the bit demonstrably did *not* move while the thread was suspended.
>
> **Step 6 — PASS, and now non-vacuous.** Zero SEH lines and zero `0xC0000409` *while a drain is
> proven to have occurred* — which is the whole point, since an absence of crash lines is equally
> satisfied by a drain that never happened. That is exactly the regression the step names
> (*"the `thread_local` guard did not suppress the legitimate drain"*).
>
> The restore leg doubles as a fixture control: `result=0` and the bit returns to `False`, so a
> hypothetical "it never flipped" could not have been blamed on an unwritable bit.

> ℹ️ The rig set `invoke_timeout_ms` to 1500 and the DLL **persists that to the hint cache**
> (`HintCache: Saved invoke timeout override … -> 1500ms`). It has been restored to **5000**.
> | 2 | with the game running, `get_pointers` → note `hook_fire_count`, run step 1 again, re-read | the count does **not** jump by our own call | our call no longer enters `HookedProcessEvent` at all |
> | 3 ⚠ THE ONE THAT MATTERS | set a short invoke timeout, fire a game-thread invoke on a **paused/menu** game so it times out and stays queued; then fire a CE static-native invoke | the queued request is **still queued** afterwards, not executed | before 3205 the second call drained it on the pipe thread |
> ### ✅ ST1 STEP 5 PASSES 2026-08-20 `[ST1-FAILOPEN-2026-08-20]` — the fail-open path, with a two-detector match
>
> **Finding the overriding class first, by reading the vtable rather than guessing.** This run's
> `init-0.log` records the slot: `DetectProcessEvent (pattern): match at **vtable+0x268** ->
> 0x7FF69AF38CB0`. Reading `*(vtable+0x268)` for instances of twelve classes gives exactly **two**
> distinct values:
> ```
> 0x7FF69AF38CB0  x69   the HOOKED base ProcessEvent   (ABP_Manny_C, SceneComponent, …)
> 0x7FF69FCA5540  x10   an OVERRIDE                    (Actor, Pawn, ChaosDebugDrawActor)
> ```
> `AActor` overrides `ProcessEvent`, so `ChaosDebugDrawActor` @ `0x2439D22A800` is a live subject.
>
> **The invoke.** `Actor.UserConstructionScript` (0 params) on that instance with `direct_call: true`:
> ```
> UE5_CallProcessEventDirect: inst=0x2439D22A800 func=0x243A9F2D400 pe=0x7FF69FCA5540
>   (caller-asserted safe)
> ```
> * the wording is **`(caller-asserted safe)`**, and **0** `via trampoline` lines — the exact inverse
>   of step 1 on a non-overriding class, so the discriminator is shown working in **both** directions;
> * the call **still works**: `"message": "ProcessEvent OK", result: 0`;
> * ⭐ and the `pe=` the DLL logged is **`0x7FF69FCA5540`** — byte-identical to the override slot read
>   out of the vtable by an independent process before the invoke. The DLL is not merely claiming to
>   have detected an override; the address it names is the one that is really in the slot.
>
> Fail-open is correct here, which is the point of the control: the trampoline would have run
> `UObject`'s BASE implementation instead of `AActor`'s.
>
> ⚠ The slot offset is per-build — an older run on this same host used **vtable+0x220**. Scanning
> with a remembered offset finds "two distinct values" that mean nothing. Read it from `init-0.log`.
> | 4 | resume the game | the queued request now runs, on the game thread | the drain still works where it should — the regression guard for step 3 |
> | 5 ⚠ control | a class that OVERRIDES ProcessEvent (a BP with its own slot), invoked directly | log shows **`(caller-asserted safe)`**, and the call still works | fail-open is correct here; the trampoline would have run the BASE implementation |
> | 6 | ordinary gameplay for a few minutes with an invoke queued | no `SEH exception during queued PE call`, no 0xC0000409 | the `thread_local` guard did not suppress the legitimate drain |

### ✅ 8-of-8 — AD4: the God Mode badge must now name WHY, not just on/off (build 3203)

> **The eighth and last step CLOSED 2026-08-23 `[AD4-CONTESTED-2026-08-23]`** — `(want=1, godmode=0,
> resolvable=true)` recorded 309/315 samples on DumperTest, with a 318-sample negative control and
> two further independent witnesses.

*Needs a connected game with a pawn. See dev-log build 3203. **The badge MAP is unit-pinned (11
tests, two negative controls); what is not pinned is that the DLL actually reports the three fields
honestly on a real pawn** — `Solitar.cpp` is compiled by no test target.*

> **Read this first, because one cell is expected to be WRONG on some games and that is not a
> regression.** `Solitar::GetState`'s `live` falls back to the *desired* value when the T2 scan
> matched no canonical `bCanBeDamaged`, while `GetGodMode` returns `PR_ERR_REFLECT` for the same
> pawn. That mismatch is **deliberately out of scope** for build 3203 (live-only, needs
> `Solitar.cpp`). If step 4 shows "ON" where you expected "ON (pending)", that is this known gap —
> file it against Solitar, not against the badge map.
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | 1 | connect with the game at a menu / no pawn, press ↻ | `Unknown` | baseline: nothing wanted, nothing readable |
> | 2 | still pawn-less, Force ON | **`ON (pending)`**, not `Unknown` | the toggle path — before 3203 this reported Unknown and looked like a failure |
> | 3 | enter gameplay so a pawn spawns, press ↻ | `ON` | the armed hold engaged on its own |
> | 4 | let the game damage-reset the flag, press ↻ repeatedly | `ON` mostly, occasionally **`ON (contested)`** | the drift race — the cell that used to read `OFF`. Rare by design; the re-assert worker wins quickly |
> | 5 | Force OFF, then ↻ | `OFF` | the unambiguous cell still works |
> | 6 ⚠ control | on a game whose pawn is immune for its OWN reasons, with nothing forced | **`ON (not held)`** | proves the badge distinguishes "we hold it" from "it happens to be true" |
> | 7 | Force ON, close the UI, reopen and reconnect | badge is `ON` **without pressing ↻** | the connect-time read; `want` lives in the DLL and survives a UI restart |
> | 8 ⚠ control | during step 7's reconnect, watch the status line | stays `Connected`, no button flicker | proves the connect read did not go through RefreshGodModeAsync (IsBusy / StatusText) |

> ### 🟡 SEVEN OF EIGHT CLOSED 2026-08-19 — by the maintainer, on their own machine
>
> The maintainer ticked rows **1, 2, 3, 5 and 6** of this item in the 繁中 checklist. That file
> compresses these eight steps into six, so five ticked rows discharge **seven** steps:
>
> | zh-TW row | steps here | verdict |
> |---|---|---|
> | 1 | 1 — menu / no pawn, ↻ → `Unknown` | ✅ |
> | 2 | 2 — pawn-less Force ON → **`ON (pending)`**, not `Unknown` | ✅ **the finding itself** |
> | 3 | 3 — pawn spawns, ↻ → `ON` | ✅ the armed hold engaged on its own |
> | 5 | 5 **and** 6 — Force OFF → `OFF`; and the ⚠ control, an own-reasons-immune pawn with nothing forced → **`ON (not held)`** | ✅ both |
> | 6 | 7 **and** 8 — reconnect shows `ON` with no ↻; status line stays `Connected`, no flicker | ✅ both |
>
> Step 2 is worth naming separately: it is the behaviour build 3203 exists to produce, and the
> pre-3203 output (`Unknown`, which read as a failure) is what it replaces. Step 6's control is the
> one that proves the badge distinguishes *"we hold it"* from *"it happens to be true"* — a badge that
> merely echoed the flag would read `ON` there.
>
> ⚠ **STEP 4 REMAINS, and it is the hardest one.** `ON (contested)` needs the game to damage-reset
> `bCanBeDamaged` while ↻ is pressed repeatedly — the drift race. It is **rare by design** (the
> re-assert worker wins quickly), so its absence proves nothing and it cannot be waved through: this
> is precisely the checklist's own rule 1 (a PASS defined by something *appearing* is not settled by
> not seeing it). It stays a `C_HYBRID` row — a human must take damage in combat mid-batch.
>
> ⚠ **Evidence class:** the maintainer's ticks, nothing more. No log line or screenshot from that run
> reached this repo. Recorded as reported, not re-observed here.

### ✅ CLOSED 2026-08-17 `[AC1-UI-2026-08-17]` — AC1: Force Overwrite must no longer be able to destroy a foreign DLL (build 3191)

*Needs **no game** — only the Proxy Deploy panel and one throwaway file. Same "free from an ordinary
session" shape as AE4–AE7, so it can ride along with those. See dev-log build 3191.*

> **The policy is unit-pinned (15 tests, negative-controlled); what is NOT pinned is that the two
> checkboxes are wired to the two halves.** `PlanDeploy` is pure and exhaustively tested, but nothing
> proves the AXAML binds `AllowForeignOverwrite` to `ForeignConsent` rather than to the persisted
> flag — that is exactly the kind of wiring a green build does not check.
>
> **Make the foreign DLL by copying any non-ours DLL** into a game's `Binaries\Win64` under a proxy
> name (e.g. `dxgi.dll`); it only has to lack our `ProductName`. Delete it afterwards.
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | 1 | place a foreign `dxgi.dll`, Refresh | row reads `Other proxy: <name>` | baseline: detection still works |
> | 2 | tick **Force Overwrite** only → Deploy | **refused**, file untouched, row still names the owner | **the regression this fix exists for** — before 3191 this destroyed the file |
> | 3 | check the byte size / version of the foreign DLL | unchanged | proves "refused" means *not written*, not merely *reported as refused* |
> | 4 | tick **both** boxes → Deploy | succeeds; `proxy` log carries a `Replacing another program's dxgi.dll (…)` warn line **naming the old product** | the capability is kept, and the only surviving record of what was destroyed is written |
> | 5 ⚠ control | restart the app | **Force Overwrite still ticked, "Replace other tools' DLLs" back to OFF** | the whole point: the destructive half must not persist. If both come back ticked, the fix is defeated |
> | 6 | with our proxy already deployed at the same version, tick Force Overwrite → Deploy | redeploys (no "already current" skip) | the benign half still works — guards against over-correcting into a refusal |
> | 7 | Update All against a game with a foreign DLL | skips it, as before | `UpdateAllAsync` passes `ForeignConsent: false`; its own pre-gate should make that unreachable |

> ### ✅ Step 5 CLOSED 2026-08-17 `[AE4-UI-2026-08-17]` — and it needed no foreign DLL at all
>
> Step 5's claim is about **persistence of two checkboxes**, not about deployment, so it can be
> settled before the rest of the batch is staged. Both boxes were ticked, the app was closed, and the
> app was relaunched. **Two independent detectors, and they agree:**
>
> * **The persisted file.** `%LOCALAPPDATA%\UE5CEDumper\ui-options.json` → `proxyDeploy` carries
>   `forceOverwrite: true` and **no `allowForeignOverwrite` / `foreignConsent` key exists at all**.
>   This is stronger than reading the UI: an absent key *cannot* come back ticked.
> * **The relaunched UI.** ☑ `Force Overwrite`, ☐ `Replace other tools' DLLs`.
>
> That is exactly the required asymmetry — the destructive half does not persist. `Force Overwrite`
> was returned to OFF afterwards so the app is left as found.
>

### ✅ ALL SEVEN STEPS CLOSED 2026-08-17 `[AC1-UI-2026-08-17]` — on a synthetic folder, no real game touched

§4.1's preferred route worked, so **Light Maze was never involved**. Staged under
`D:\SteamLibrary\steamapps\common\ZZSynthProxyTest\ZZSynth\Binaries\Win64\` with two deliberate risk
reductions: the `-Win64-Shipping.exe` is a **57-byte text stub** (the scanner pattern-matches the
filename and nothing there is ever executed, so real executable bytes serve no purpose), and the
foreign DLL is a copy of **Intel's `tbbmalloc.dll`** rather than a System32 binary — it only has to
carry a `ProductName` that is not ours. SHA-256 recorded before and after every step, per §4.1
condition 1. `Found 17 UE game(s)` confirmed the synthetic folder is detected.

| step | verdict | evidence |
|---|---|---|
| 1 | ✅ | row reads `OtherProxy` / **`Other proxy: oneAPI Threading Building Blocks`** — the panel really does read the foreign DLL's own `ProductName` |
| 2 ⚠ the regression | ✅ | `Force Overwrite` alone → **`Deployed: 0 success, 1 failed`**, row still `OtherProxy`, Error cell **`Refused: another program…`** |
| 3 | ✅ | SHA-256 **byte-identical** to the planted file afterwards — "refused" means *not written*, not merely *reported as refused* |
| 4 | ✅ **both halves** | both boxes → `Deployed: 1 success`, file now SHA-matches `dist/proxy/dxgi.dll`, and `view-0.log` carries `[WARN] Replacing another program's dxgi.dll in ZZSynthProxyTest (oneAPI Threading Building Blocks (oneTBB) 2021.13.0) — foreign overwrite was explicitly allowed`. It names the product **and its version** |
| 5 | ✅ | see the block above — two detectors, `ui-options.json` has no foreign-consent key at all |
| 6 | ✅ | our proxy already at 1.0.0.3262, `Force Overwrite` only → `Deployed: 1 success`, i.e. it **redeployed** rather than skipping as "already current" |
| 7 | ✅ **stronger than asked** | foreign DLL re-planted, then `Update All` with **BOTH boxes ticked** → `All 10 deployed proxy DLL(s) already up-to-date`, row untouched, SHA still the foreign one. The count of **10** shows the pre-gate excluded it before consent was ever consulted, so the hard-coded `ForeignConsent: false` beats the UI state |

**Cleanup asserted, not assumed:** both synthetic trees removed and
`D:\SteamLibrary\steamapps\common` re-counted back to its original 63 children with no `ZZ*` left.

> **✅ CORROBORATED 2026-08-19 by the maintainer's own run.** The maintainer worked the 繁中 checklist
> off a NAS copy and ticked **all six** of its AC1 rows (which fold this section's seven steps into
> six — zh-TW row 2 carries both "refused" and "bytes unchanged"). This is a **second, independent
> pass** over the same claims, and the row that matters is the same one: Force Overwrite alone
> refuses and leaves the foreign DLL byte-identical.
> ⚠ **Recorded honestly: not observed here.** No log, screenshot or hash from that run reached this
> repo — the evidence is the maintainer's ticks, nothing more. It does not change the verdict, which
> was already closed above on evidence that *was* observed; it only removes the last reason to keep
> the item in the 繁中 mirror. **AC1's section was deleted from `pending-verification_zh-TW.md`
> accordingly**, on the mirror's own rule (an item recorded closed here does not stay listed there).
> The audit register's `⚠ Live check owed` caveat on the AC1 row is retired in the same commit; its
> ✅ did not move, because the fix had already shipped in build 3191.

### ✅ AE4 step 4 — removal half CLOSED, gate half NOT OBSERVABLE `[AC1-UI-2026-08-17]`

The same staging gave AE4 step 4 the leftover it lacked: a `…\ZZSynthOrphan\ZZOrphan\Binaries\Win64\`
holding **only** our `version.dll` and no exe.

**Removal works, and three independent detectors agree** — which matters because this is the
Recycle-Bin-only policy (B13/B41) actually holding in practice rather than in unit tests:
1. Panel: `Cleaned 1 of 1 leftover(s) — **1 file(s) recycled, 4 folder(s) removed**`.
2. Disk: the four-level tree is gone and the ceiling `…\steamapps\common` survives untouched.
3. **Recycle Bin: the file is recoverable** — `D:\$RECYCLE.BIN\S-1-5-…-1001\$RG84NG7.dll`, 2,860,544 B.

The confirmation dialog is itself worth recording: it lists the exact folders it will try **in order,
each only if left empty**, prints `Not touched: D:\SteamLibrary\steamapps\common`, and explains *why*
this is judged a leftover (no Steam appmanifest names `ZZSynthOrphan`; no executable survives under
the tree). `Cancel` reports `Cancelled — nothing was removed`.

⚠ **The gate arm is NOT verified.** Pressing `Delete checked` while a scan ran opened the
confirmation dialog rather than refusing — but that proves nothing either way, because the scan
finished inside the 2 s before the click and because the dialog opening is not the delete *running*.
**Same measurement limit as steps 1 and 2**: every operation here completes inside one input
round-trip. The gate itself is proven to exist and to name its operation (see AE4 step 1), so what is
missing is specifically the `IsRemovingOrphans` arm.

⚠ **A trap for whoever re-checks the Recycle Bin:** the first probe reported *no* recycled file and
nearly became a filed defect. `shutil.copy2` preserves mtime, so the `$R…` entry carries the
**source's** timestamp, not the deletion time — filtering the Recycle Bin by "modified in the last
30 minutes" hides it. Match on **size**, never on time.

### ✅ CLOSED 2026-08-17 `[GRP4-UI-2026-08-17]` — U3 + U17 — struct previews: dropped members, then wrong widths (builds 3169, 3171)

**Verified on the vehicle this file already named, and it carries its own negative control.**
DumperTest Development, dist 3262, Live Walker → `DumperTestActor_0` → `Map_IntToVec3f` (`0x518`).

The map expands to three distinct entries, each rendering **all three components**:
```
[0] 1 → {X=6201, Y=6202, Z=…}      [1] 2 → {X=6211, Y=6212, …}      [2] 3 → {X=6221, Y=6222, …}
```
and drilling into `[0]` gives the whole struct with offsets, widths and addresses:
```
0x0  X  FloatProperty  6201   00C8C145   0x1B062E6A964
0x4  Y  FloatProperty  6202   00D0C145   0x1B062E6A968
0x8  Z  FloatProperty  6203   00D8C145   0x1B062E6A96C
```

* **U3 (dropped members) — fixed.** Three members, not one.
* **U17 (wrong widths) — fixed.** Offsets `0x0/0x4/0x8` and addresses exactly 4 bytes apart, i.e.
  read as `float`, and the hex round-trips (`6201.0f` = `0x45C1C800` → little-endian `00C8C145`).
* ⭐ **The negative control is free and exact.** The old defect displayed `f:[6203.0000]` — a single
  float, the **last** one, from skipping 8 bytes of a 12-byte struct. `6203` is precisely `Z` here, so
  the broken rendering is the current output with `X` and `Y` deleted. Nothing else in the sample
  makes the before/after that legible.

### ✅ A3 — VERIFIED 2026-08-19 `[A3-DOUBLE-2026-08-19]` (steps 1, 2, 4; build 3168)

**Steps 1, 2 and 4 done headless on DumperTest Development / dist 3263** with
`tools/verify/a3_struct_path.py`. Step 3 (Group Scan / Property Search Deep) not run — it is the
*asymmetry* corroboration, not the check.

⚠ **STEP 1'S INSTRUCTION IS WRONG ON A UE5 TITLE AND MUST NOT BE FOLLOWED LITERALLY.** It says
"Value Search, **Float** (or NumericAll)". Under **LWC an `FVector` is a double-precision
`FVector3d`**, so a Float scan *structurally cannot* see `RelativeLocation.X`. Measured side by
side on the same session: **Float/1.0 → 0 `*Scale3D*`, 0 `*Location*`; Double/0 → 177 and 114.**
Taken at face value the Float run reads as a clean FAIL and would have condemned a working fix.
Use **Double** (or `NumericAll`) on any UE5 game; Float remains right only for a UE4-era title.

* **Step 1 — PASS, and the statistic needs no baseline.** A vector leaf is a candidate path ending
  `.X`/`.Y`/`.Z`; strip the leaf and you have the struct field that produced it. Under the defect a
  class could contribute **at most one** distinct such parent, so the measurement is just *how many
  classes contribute two or more*. **151 classes do** (over all 3,450 candidates of a
  `Double`/`Exact 0` scan, `deadline_hit=false`, 25,172 objects / 1,415 classes):
  `TraceQueryTestResults` **72 distinct**, `RigVMMemory_Work` 34, `ArchVisCharMovementComponent` 26,
  `DumperTestCharacter` / `BP_ThirdPersonCharacter_C` / `ArchVisCharacter` 19 each — the last group
  spanning genuinely unrelated branches (`AttachmentReplication.LocationOffset`,
  `AttachmentReplication.RelativeScale3D`, `BasedMovement.Location`, `BaseTranslationOffset`),
  which is exactly the cross-branch suppression the whole-walk guard caused.
* **Step 2 (CONTROL) — recorded, unchanged in shape.** The `FVector` scan returns 45 rows whose
  names are bare struct fields (`RelativeScale3D`, `OffsetScale`), i.e. no `.X` expansion at all —
  consistent with the row's point that `acceptedStructNames` is non-empty for a vector scan so the
  recursion is skipped and the guard never fired there. ⚠ Honest limit: nobody captured this number
  *before* 3168, so this is a **baseline for the future**, not proof of no-change.
* **Step 4 — PASS.** `hit the 4000 scan-field cap` appears in **0** of the `scan-*.log` files
  anywhere under the log root, so the cap is unreachable in practice as intended.
* ⚠ **A pagination trap the rig now guards**: page size is server-side, so looping "until a short
  page" can stop on a full final page and silently under-count the population being measured. Drive
  the loop from `total` in the `begin_value_scan` reply and complain if the totals disagree.

> *(original row kept below for its steps)*

### ✅ CLOSED 2026-08-16 `[ELLIOT-PIPE-2026-08-16]` — AB4: the Aura half of the ordered-predicate width fix

*Needs a connected game. See dev-log build 3133. **The Radar half is unit-pinned (16 new assertions,
negative control 6 red); this batch is exactly the half that could not be** — no test target compiles
`Aura.cpp`, so the wiring from `Find()` to `FindEntry()` across the first-scan, native-C and refine
paths has never executed against a real object pool.*

> **✅ ALL SIX CHECKABLE STEPS PASS. Steps 2 and 4 are a PAIRED control and step 4 is EXHAUSTIVE.**
>
> **Conditions.** Elliot (`Elliot-Win64-Shipping.exe`, PE `6A577F4E1D91B000`), DLL build **3156**
> loaded as `proxy:dxgi.dll`, scan resolved by AOB — `GObjects 0x149BFC140` / `GNames 0x149B18600`
> (`GNAM_V8`) / `GWorld 0x149D8BDA0` / `GEngine 0x149D8E290` (`GENG_X1`), `ue_version=504`,
> `item_size=24`, **84,990 objects / 84,387 scanned**.
> **Driven straight over the named pipe, NOT through the UI** — a deliberate choice: this batch is
> about `Aura`'s `Find()`→`FieldDescriptor` wiring, and going pipe-direct removes the Avalonia layer
> as a variable. The trade is that it does **not** exercise the Value Search panel's own binding;
> that half rides on the separate 14-MED UI batch below.
>
> | step | request | result | verdict |
> |---|---|---|---|
> | 1 regression | `NumericNoByte` Exact `100` | **34,117 rows, `deadline_hit=false`** (complete, untruncated) — Float 24,361 / Double 9,695 / Int 61, **0 one-byte rows** | ✅ Exact unchanged, and correctly excludes 1-byte widths |
> | 2 the fix | `NumericAll` Smaller `500` | **81,547 one-byte rows** (`ByteProperty` 81,283 + `Int8Property` 264) out of 1,000,000 | ✅ these were structurally impossible before |
> | 3 sign leak | `NumericNoByte` Bigger `-5` | `UInt32Property` 240 + `UInt16Property` 26 = **266 unsigned rows** | ✅ a negative target no longer suppresses the unsigned parse |
> | 4 ⚠ control | `NumericAll` Bigger `500` | **total 367,401, `deadline_hit=false`, all 367,401 paged, one-byte rows = 0** | ✅ **the pruning half still prunes — over the COMPLETE set, not a sample** |
> | 5 refine | Next Scan, same predicate, on step 2's session | byte rows survive, tally identical (`ByteProperty` 5,238 + `Int8Property` 105 at the 40k cap) | ✅ the `cmpEntry` branch does not drop the new entries |
> | 6 native-C | step 2 + `native_c`, `native_align=4`, `newest_first` | **8,321 one-byte + 8,628 unsigned rows**; distribution flattens (`UInt16`=`UInt32`=3,205) exactly as hole-scanning at multiple widths predicts | ✅ the separately-wired `&e`/`&me` path took |
> | 7 `Between` | not run | known-unfixed by design | — |
>
> **Why 2-vs-4 is the real evidence and not two loose numbers:** same `data_type` (`NumericAll`, so
> 1-byte widths are in scope for *both*), same value, same object population — only the predicate
> direction differs. 81,547 → 0. That is precisely the shape a correct implementation must produce
> (no byte exceeds 500), and it is the negative control the batch asked for. Step 4 completing with
> `deadline_hit=false` is what upgrades it from "sampled 40k and saw none" to a claim over the pool.
> *Honest limit:* steps 2/3/6 hit their result cap, so their counts are lower bounds, not censuses.

1. **⚠ REGRESSION FIRST — an ordinary Exact scan is unchanged.** Value Search → `NumericNoByte` →
   Exact → a value you know exists → First Scan. `ScanType` now reaches `BuildNumericTargets` but
   defaults to `Exact`, and Exact must be byte-identical. Compare the row count against a pre-3133
   build if you can; any change here is a real finding.
2. **THE FIX, first scan.** `NumericAll` → **Smaller** → `500` → First Scan. The results must now
   contain **`ByteProperty` / `Int8Property` rows**, which they never did before — every 1-byte field
   holds a value below 500 by definition. If 1-byte rows are still absent, the Aura wiring did not
   take. **Record the row count and whether byte-width rows appear**; a count alone proves nothing.
3. **The sign leak, which the finding never mentioned.** `NumericNoByte` → **Bigger** → `-5`. Every
   unsigned field satisfies it, so `UInt16`/`UInt32`/`UInt64` rows must appear. They were dropped
   wholesale before, because a negative string suppresses the entire unsigned parse.
4. **⚠ The opposite direction must still PRUNE.** `NumericAll` → **Bigger** → `500`: 1-byte rows must
   be **absent** (no byte exceeds 500). That half of the old gate was correct and the fix must not
   have widened it into a false-positive machine. This is the control for step 2.
5. **Refine still works on the new entries.** After step 2's scan, do a **Next Scan** with the same
   predicate and confirm the byte rows survive and the count narrows sanely. The refine path takes a
   different branch (`cmpEntry` vs the prev-value `cmpTarget`) and is separately wired.
6. **Native-C scanning.** Repeat step 2 with **native-C enabled**. Those paths enumerate
   `multiTargets->entries` directly rather than resolving per member, and were wired separately
   (`&e` / `&me` instead of `e.bytes`) — a distinct code path with the same intent.
7. **`Between` is KNOWN-UNFIXED, do not report it as a bug.** Its two bounds are built by two
   independent calls, so `Between -100 100` still drops unsigned widths. A correct fix needs a joint
   builder; it is filed, not forgotten.

### ✅ VERIFIED 2026-08-19 — SkiaSharp/HarfBuzzSharp ABI alignment: the UI must stop crashing

> ### ✅ CLOSED 2026-08-19 `[SKIA-ABI-2026-08-19]` — all four steps, by the maintainer
>
> **Evidence: the maintainer's own runs, reported directly. Not observed by an agent** — nobody
> re-derived this from a log or a dump, so it is recorded on their authority and stated as such
> rather than dressed up as a measurement.
>
> All four steps below came back ticked: the tab-walk + 繁中 rendering regression check (1), the
> Elliot → Live Walker → `GameState` → Copy CE XML repro left running (2), no crash to symbolize (3),
> and page heap removed before judging performance (4).
>
> ⚠ **Step 3's "do not close on one clean session" was satisfied by accumulation, not by one run** —
> that judgement is the maintainer's, and it is the only way this item could ever close: its PASS
> condition is the *absence* of an event. If the UI ever dies at `libSkiaSharp` again, this reopens;
> the page-heap + x64 `llvm-symbolizer` recipe below is kept for exactly that.

*Needs the UI running for a while. See dev-log build 3127. **This is the one item on this whole
register where a PASS is "nothing happened for a few sessions"** — so it cannot be closed by a single
run, and a crash is worth more than a green session.*

**What was wrong.** `Avalonia.Skia 12.1.1` is built against **SkiaSharp 3.119.4** and
`Avalonia.HarfBuzz 12.1.1` against **HarfBuzzSharp 8.3.1.3**. Routine `chore(deps)` bumps
(`5346f907` and two before it) had carried the project to **SkiaSharp 4.151.1** (one major ahead) and
**HarfBuzzSharp 14.2.1.2** (six ahead). NuGet cannot warn about this — Avalonia's constraint is an
open-ended minimum, so a major jump *satisfies* it: no NU1608, no NU1605, and
`TreatWarningsAsErrors=true` never had anything to catch.

**How it was caught, and why the first dump was not enough.** The UI died with
`0xC0000374` **STATUS_HEAP_CORRUPTION** ~2.3 s after a Copy CE XML on Elliot. That dump named
nothing: heap corruption surfaces at the *next* heap operation, so its stack is the **detector, not
the culprit** — it showed only ntdll's heap-error path on the UI thread. Full **page heap**
(IFEO `GlobalFlag=0x02000000` + `PageHeapFlags=0x3`) converted the next occurrence into an immediate
`0xC0000005` at **`libSkiaSharp.dll+0x102B8D`** (WER event `AutoVerifierV2`, `verifier.dll` on the
stack, target address a guard page). That is the whole method: **a heap-corruption dump is worth
almost nothing; re-run it under page heap.**

1. **⚠ THE REGRESSION CHECK COMES FIRST, AND IT IS BROAD.** Skia and HarfBuzz are what draw and shape
   *everything*, so a downgrade touches every pixel. Open each tab in turn; look for missing glyphs,
   wrong metrics, clipped text, DataGrid rows that fail to paint, and check a 繁中 string renders
   (HarfBuzz went back **six** majors — text shaping is where breakage would show first).
2. **The original repro, now expected to survive.** Elliot → Live Walker → `GameState` → **Copy CE
   XML** with AOB on and depth 4, then leave the UI up for several minutes. Two crashes happened
   within ~14 minutes of each other on the old versions.
3. **⚠ Do not close this on one clean session.** The old build ran for many sessions before anyone
   saw it. A pass is several sessions of ordinary use. **A crash is a definitive FAIL** — capture the
   WER dump and say whether the faulting module is still `libSkiaSharp`.
   **Session 1 of N `[ELLIOT-2026-08-16]`: no crash.** Build **3127** (the aligned one — confirmed
   from `Logs\UE5DumpUI\init-0.log` `Version: 1.0.0.3127`, not assumed), Elliot, 20:49–20:50, a full
   connect + scan + walk. **This is one data point and closes nothing** — the old build survived
   many sessions before the first crash, and this one was shorter than the session that crashed.
4. **Turn page heap OFF before judging performance.** `reg delete "HKLM\SOFTWARE\Microsoft\Windows
   NT\CurrentVersion\Image File Execution Options\UE5DumpUI.exe" /f`. With it on, everything is slow
   and memory-hungry; that is the tool, not the build.
5. **What this does NOT prove — updated, the fault IS symbolized now.** `SkiaSharp.NativeAssets.Win32`
   **ships `libSkiaSharp.pdb`**, so the earlier "faulting function unknown" was an assumption, not a
   fact. `libSkiaSharp+0x102B8D` (4.151.1 win-x64, binary identity confirmed by an exact 12,272,440-byte
   match with `dist/`) resolves to `skia_private::TArray<SkPathVerb,1>::size` inlined through
   `SkSpan` → `SkPathBuilder::verbs` → **`SkPathBuilder::computeFiniteBounds`**. So the fault is
   **path geometry, not text shaping — HarfBuzz is exonerated for this crash** — and `SkPathBuilder`
   is precisely what Skia restructured across this major.
   What is still unproven is the **caller**: naming the callee does not name who handed it a
   mis-shaped path. If crashes continue at the aligned versions the ABI hypothesis is refuted and the
   next step is a Skia-side bug. If one does recur, capture a page-heap dump and symbolize the FULL
   stack — now known to be possible (use the **x64** `llvm-symbolizer` under
   `VC\Tools\Llvm\x64\bin`; a recursive search finds the ARM64 copy first and it will not run).

### ✅ VERIFIED 2026-08-20 `[FREEZESTUCK-2026-08-18]` — an abandoned freeze must untick its own record — STEPS 1-6 PASS in a live CE session

*Needs **any** connected game plus CE. The whole batch is one freeze record and one DLL re-injection.*

> **What is already pinned offline and must NOT be re-checked here:** 13 executable cases in
> `scripts/tests/freeze_helper_test.lua` (`lua scripts/tests/freeze_helper_test.lua`, **154 checks**
> — re-measured 2026-08-20; the `117` this row carried was stale, and audit L12's row already had
> the right figure in its `83 / 154 / 91` triple)
> drive the abandonment against a stubbed CE — including a memory-record stand-in whose
> `Active = false` dispatches the `[DISABLE]` chunk, so the untick really does run `stop()` and
> destroy both timers; a control proving a **transient** failure does NOT untick; a no-`memrec`
> case; and a deleted-record case. Plus `FreezeScriptGeneratorTests` for the `CFG.memrec = memrec`
> wiring and for the removal of the old unfollowable "Re-enable the record after fixing it".
> **What no offline test can reach** is whether CE's real `TMemoryRecord.Active = false`, driven
> from a Lua timer, behaves like the stand-in. That is step 3, and it is the only step that matters.
>
> ⚠ **Read the checkbox correctly**: in CE a big red ✗ on a record's checkbox means **ACTIVE**, not
> failed; an inactive record is an **empty box**. Reading it backwards inverts every step below.
>
> ⚠ **Open CE's Lua Engine window before step 2** — the abandonment message is printed there, and
> hygiene closes that window on a clean enable.
>
> | step | do this | expect |
> |---|---|---|
> | 1 | Property Search any supported field → row **Freeze** → create the script → tick the record | the record ticks (red ✗) and the value holds |
> | 2 | With it still ticked, **re-inject `UE5Dumper.dll`** (or kill the DLL host) and wait ~15 s (3 rescans × 5 s) | the Lua Engine prints `… consecutive rescans failed -- freeze STOPPED writing … This record has been unticked; re-enable it after fixing the cause.` |
> ### 🟡 OFFLINE HALF RE-CONFIRMED GREEN 2026-08-20 — steps 1-5 remain CE-only, deliberately
>
> All three Lua rigs were re-run today and are green: `dissect_test` **83**, `freeze_helper_test`
> **154**, `invoke_helper_test` **91** — 328 checks, 0 failures. So the abandonment logic this row
> says "must NOT be re-checked here" is confirmed still passing on the current tree.
>
> Nothing else here was attempted, and that is correct rather than a gap: the row states outright
> that what no offline test can reach is whether **CE's real `TMemoryRecord.Active = false`, driven
> from a Lua timer, behaves like the stand-in** — and that is step 3, the only step that matters.
> A CE session is genuinely required.

> | 3 ⚠ THE ONE THAT MATTERS | look at the record's checkbox | it is now an **EMPTY box**. Before the fix it stayed a red ✗ forever, claiming a cheat nothing was applying |
> | 4 | check CE is still responsive; look for an error dialog | none. The untick is deferred onto a one-shot timer precisely so `[DISABLE]` does not destroy a timer from inside its own handler |
> | 5 | re-inject a working DLL, then re-tick the record | the freeze arms again and holds — i.e. step 2's advice is followable, which it was not before |
> | 6 ⚠ control | with a healthy DLL, leave a freeze running untouched for a minute | the record **stays ticked** and keeps writing. One transient `mailbox busy` must not untick anything |
> | 7 ⚠ control, opportunistic | delete the memory record while its freeze is mid-abandonment | no Lua error dialog; the failure is still reported |


> ### ✅ STEPS 1-6 PASS IN A LIVE CE SESSION 2026-08-20 `[FREEZESTUCK-CE-2026-08-20]`
>
> **The row's own framing is that step 3 is "the only step that matters" — whether CE's real
> `TMemoryRecord.Active = false`, driven from a Lua timer, behaves like the offline stand-in. It
> does.** Run on DumperTest Development (build 3263 DLL, `dist` AOT UI, CE 7.7 +
> AOBMaker plugin auto-loaded), one freeze record on `DumperTestActor::TickCount = 9999` — a field
> whose HUD line reads *"TickCount climbs only if the 1 Hz timer runs"*, so a held value is
> self-evidently held.
>
> ⚠ **Every `Active` reading below is `getAddressList().getMemoryRecord(0).Active` printed from CE's
> own Lua Engine, never the checkbox icon** — per the row's own warning. The icon agreed every time
> (red ✗ = active, empty box = inactive), which is itself a small confirmation of that rule.
>
> | step | measured |
> |---|---|
> | 1 | `[Freeze] Started: DumperTestActor::TickCount = 9999 (int32@0x6A8) on 1 instance(s)`, record red ✗, and the Property Search preview read **9999 twice across 13 s** on a field that had been climbing ~1 Hz (it was `228` when the script was created). **PASS** |
> | 2 | host killed at 20:59:02; at 20:59:5x the Lua Engine **and** a CE dialog carried `[ue5_freeze] DumperTestActor: 3 consecutive rescans failed -- freeze STOPPED writing (last error: … the contract symbol could not be read -- the game process has most likely exited …). This record has been unticked; re-enable it after fixing the cause.` followed by `[Freeze] Stopped: DumperTestActor::TickCount`. **PASS** |
> | 3 ⭐ | checkbox is an **EMPTY box** both before and after dismissing the dialog, and `STEP3 active=false` from Lua. **The report and the reality agree.** **PASS** |
> | 4 | the only dialog is the abandonment *report*; no Lua error. CE answered a fresh Lua chunk afterwards (`records=0 pid=48100`). **PASS** |
> | 5 | relaunched + re-injected, re-attached CE to the new pid, re-ticked → `[Freeze] Started …` again and red ✗. Step 2's advice is followable. **PASS** |
> | 6 ⚠ control | left untouched **76 s** (21:04:21 → 21:05:37): still red ✗, **no** new abandonment line. And it was still *writing*, not merely ticked — the game's own HUD moved `frames=4345 → 4451` and `Health.CurrentValue=9 → 2` while `TickCount` sat at **9999** in both samples. **PASS, with the negative control built in** |
> | 7 ⚠ control, opportunistic | ⚠ **partially exercised — the strict ordering was not achieved.** Three attempts to delete the record *mid*-abandonment all lost the race: the abandonment's `showMessage` is **modal**, and it swallows both the `Del` key and a click on the Lua **Execute** button. The fourth attempt (a pre-armed CE Lua timer + a backgrounded delayed kill, so neither depended on a click) landed the destroy **just after** the untick and **while the modal was still up**: `[Freeze] Stopped …` → `STEP7 destroying, active=false` → `STEP7 destroyed count=0`, **no Lua error**, and the failure report survived on screen. That covers the substance (a record destroyed while the freeze machinery is tearing itself down) but **not** the literal window, so the row stays honest about it rather than claiming a pass. |
>
> ⭐ **The result that matters beyond this row: this is the DEFERRED untick working.**
> `[FREEZEUNTICK-2026-08-20]` records that the **in-`[ENABLE]`** `memrec.Active = false` does *not*
> survive, and its fix shape was a *"leading hypothesis, explicitly NOT yet measured"* — use the
> deferred one-shot timer instead. Both paths were exercised in this one session on the same record:
> the in-ENABLE untick left `active=true`, the deferred untick produced `active=false`. **That is the
> hypothesis measured, and it confirms the proposed fix.**
>
> **Two bail-out reproductions came free**, both from CE's Lua rather than an icon:
> * `[FREEZEUNTICK-2026-08-20]` **reproduced on a fresh table** — ticking with the helper absent gives
>   `[Freeze] ue5_freeze_helper.lua not found in this table.` and then `active=true`. Nothing was
>   applied; the user is told a cheat is running.
> * `[FREEZEINJECT-CRLF-2026-08-20]` **reproduced with a before/after control**, which the original
>   report lacked: `findTableFile('ue5_freeze_helper.lua')` returned **no value at all** before the
>   inject; the inject then reported *"Inject freeze helper failed: Stream size mismatch: wr…"*; and
>   after it `helper_present=true` with `stream_size=57208` — the LF-normalised length, exactly
>   58,345 − 1,137 CRLF endings. **The write succeeded and the check is wrong**, now shown by the
>   state changing across the operation rather than by arithmetic alone. The freeze armed
>   immediately afterwards, which is the practical proof the stored content is usable.
>
> **Rig notes.** The failure branches of `InjectFreezeHelperLuaAsync`
> ([MainWindowViewModel.cs:3312](ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:3312)) write to
> `MainWindow.StatusText` **only** — `_log.Info` is on the success path alone, so a failed inject
> leaves **no log line**. `StatusText` is the toolbar text, capped at `MaxWidth="360"` with
> `TextTrimming="CharacterEllipsis"`, so the message is truncated on screen and the full text is
> only in its tooltip. Reading the *panel's* status line instead (a different property) makes a
> failed inject look like a silent no-op — which is how one attempt here was first misread.

### ✅ EVERY RUNNABLE STEP DONE 2026-08-20 `[PASTECRASH-2026-08-18]` — a clipboard paste must no longer terminate the UI

> **Steps 1-6 pass** (`[PASTECRASH-LIVE-2026-08-20]`), **4b and 4c** pass
> (`[PASTECRASH-4BC-2026-08-20]`). **Steps 7 and 8 are marked "opportunistic" by the row itself** —
> they read a `crash.log` produced by *some future unrelated crash* and cannot be driven on demand.
> Nothing here is waiting on an action anyone can take.

*Needs the UI only — **no game, no DLL, no pipe**. Three halves now (a follow-up hardening pass
landed on 2026-08-19): a `Dispatcher.UIThread.UnhandledException` guard
(`Services/DispatcherFaultGuard.cs`) that marks **only** classifier-confirmed input-layer faults
handled, a guard on the clipboard **WRITE** path (`WindowsPlatformService.CopyGuardedAsync`), and a
crash.log headline that states the real phase + uptime instead of the hard-coded phrase "startup
crash".*

> **What is already pinned offline and must NOT be re-checked here:** the swallow SCOPE (92 unit
> tests — `InputLayerFaultClassifierTests`, `DispatcherFaultGuardTests`, `ClipboardWriteGuardTests`,
> `CrashReportFormatterTests` — including negative controls for a ViewModel
> `NullReferenceException`, a mixed our-code/clipboard stack, eight never-swallow exception types, an
> over-deep exception chain, an `AggregateException` that tries to smuggle an unrelated fault
> through, and a marker that must not match a longer method name), plus reflection tests that fail if
> Avalonia renames an allow-listed type or drops one of the async state machines. **What no offline
> test can reach** is whether Avalonia's dispatcher actually raises `UnhandledException` for a fault
> arriving via `Task.ThrowAsync`, which is the entire premise — that is step 2, and it is the only
> step that matters.
>
> ⚠ Step 2 needs the clipboard to genuinely fail. Two ways that both work: hold the clipboard open
> from another process (`OpenClipboard` without `CloseClipboard`), or copy from an app that uses
> delayed rendering and close that app before pasting.
>
> ⚠ **Everything the guard logs — swallowed AND refused — goes to `view`.** The two outcomes used to
> split across `view-0.log` and `init-0.log`, so grepping one file showed half the story. Grep
> `Logs\UE5DumpUI\view-0.log` only.
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> ### ✅ STEPS 1–6 PASS 2026-08-20 `[PASTECRASH-LIVE-2026-08-20]` — including the premise no offline test could reach
>
> UI only, no game. Clipboard made genuinely unusable with `tools/verify/clipboard_hold.py`
> (`OpenClipboard` without `CloseClipboard` from another process — the method this row names).
>
> * **Step 1 — PASS.** `PASTECRASH_BASELINE` pasted normally; the guard has not broken normal paste.
> * **Step 2 ⚠ THE ONE THAT MATTERS — PASS.** With the clipboard held, `Ctrl+V` left the app
>   **running** and the box **unchanged**. The discriminator was deliberate: the clipboard was
>   loaded with a *different* string (`SHOULD_NOT_APPEAR`) before it was locked, so a paste that had
>   silently succeeded would have been visible. It did not appear.
>   ⭐ **This demonstrates the fix's whole premise** — that Avalonia's dispatcher really does raise
>   `UnhandledException` for a fault arriving on the clipboard task. The log line names it exactly:
>   `Input-layer fault swallowed (#1) — the keystroke did nothing, the app is still running.
>   System.Runtime.InteropServices.COMException … [input-layer frame: Avalonia.Win32.ClipboardImpl]`
> * **Step 3 — PASS.** Three pastes gave **#1 → #2 → #3**, so the guard is **not one-shot**; after
>   releasing the clipboard a normal paste worked again (`RECOVERED_OK`), leaving nothing wedged.
> * **Step 4 — PASS.** `Ctrl+C` with the clipboard unusable → swallow **#4**, app alive.
> * **Step 5 — PASS.** A second UI instance rewrote `crash.log`, headed
>   `[2026-08-20 09:42:09] UE5DumpUI crash during STARTUP (uptime 0.16s)` — the real phase and a
>   real uptime, not the old hard-coded phrase. (Stack: `Cannot perform requested operation because
>   the Dispatcher shut down`.) ⚠ Note this **coexists with `AF10`**: the same launch exits with
>   code **1** as AF10 requires *and* writes this report — the two rows are not in conflict.
> * **Step 6 ⚠ CONTROL — PASS.** After recovery, pasting and then typing `_TYPED` both worked and
>   the swallow counter **stayed at 4**. So the guard is dormant when nothing fails; a fifth line
>   here would have meant it was swallowing healthy input.
>
> **Not done:** 7/8, which are opportunistic by construction.
>
> ### ✅ 4b + 4c NOW DONE 2026-08-20 `[PASTECRASH-4BC-2026-08-20]` — and they turn out to be TWO paths
>
> DumperTest connected, real UI, clipboard genuinely held by `tools/verify/clipboard_hold.py`.
>
> **4c — PASS, and it is NOT the same mechanism as 4b.** With the clipboard unusable, the System
> tab's **Copy** button (GObjects address) leaves the app running and logs its own message:
> ```
> [WARN] Clipboard copy FAILED — nothing was copied, the app is unaffected. System.Runtime.
>        InteropServices.COMException: Unexpected HRESULT …
> ```
> ⭐ **The `Input-layer fault swallowed (#N)` counter did NOT move** (still `#1` from 4b). So the
> keystroke guard and the Copy-button write path are **separate handlers with separate wording**,
> each phrased for its own context — *"the keystroke did nothing"* vs *"nothing was copied"*. Reading
> the counter alone would have missed that the button is covered at all.
>
> **4b — the safety half PASSES; the predicted residue did NOT occur.**
> `Ctrl+A` then `Ctrl+X` in the Object Tree filter with the clipboard held: the app is **still
> running**, the text is **unchanged** (`Metric` still in the box, count still 5,000 — Cut degraded
> to nothing visible), and `Input-layer fault swallowed (#1)` is logged.
>
> 🟡 **But the row's expected residue — "the first `Ctrl+Z` is a no-op, an extra undo entry" — was
> not observed.** Counted rather than eyeballed: `Metric` is 6 characters and it took **exactly 6**
> `Ctrl+Z` presses to empty the box (`Metric`→`Metri`→`Metr`→…→empty), with a 7th doing nothing.
> There was no extra step anywhere in the stack, and in particular the *first* undo removed a real
> character rather than being a no-op.
> ⇒ The likely reading is that the guard swallows **above** `TextBox.Cut`, so `SnapshotUndoRedo`
> never runs and the caveat the row was written to excuse does not arise. That is *better* than
> the documented expectation, not a failure — but the row's explanation of the residue should be
> treated as unconfirmed rather than as observed behaviour.
> ⚠ **Scope:** the text was typed synthetically, one character per key event, so every character is
> its own undo entry. That is what makes the count decisive here; a human typing burst might coalesce
> entries differently, and this run does not speak to that case.

> | 1 | start the UI, copy ordinary text, `Ctrl+V` into any filter box | the text pastes | baseline — the guard must not have broken normal paste |
> | 2 ⚠ THE ONE THAT MATTERS | make the clipboard unreadable (above), then `Ctrl+V` into a filter box | **the app is still running**, the box is unchanged, and `Logs\UE5DumpUI\view-0.log` gains `Input-layer fault swallowed (#1)` | this is the crash, reproduced; before the fix the process died here |
> | 3 | repeat step 2 a few times, then release the clipboard and paste again | counter climbs (`#2`, `#3`…), then a normal paste works | the guard is not one-shot and leaves nothing wedged |
> | 4 | `Ctrl+C` in a text box while the clipboard is unusable | same: alive, logged, no crash | `Copy` is the second allow-listed command, and it mutates nothing at all |
> | 4b ⚠ the residue is EXPECTED, not a defect | `Ctrl+X` in a text box while the clipboard is unusable, then press `Ctrl+Z` once | **the app is still running**; Ctrl+X degrades to nothing visible (text not removed, clipboard unchanged) and is logged; the **first `Ctrl+Z` is a no-op** — an extra undo entry is expected, press it again to reach the real previous edit | `TextBox.Cut`'s IL calls `SnapshotUndoRedo` BEFORE the await and `DeleteSelection` after, so a swallowed Cut leaves one undo snapshot with no edit behind it. Swallowed anyway as a judged trade: the snapshot is of text that never changed, so undoing it is a no-op, whereas the alternative is the process dying with a loaded session — and a busy clipboard fails all three keys for one reason, so Ctrl+X alone closing the app would be indefensible |
> | 4c ⚠ NEW, the WRITE half | with the clipboard unusable, press any **Copy** button (Pointer panel addresses, a Live Walker row, Property Search → Copy Offset) | **the app is still running**, nothing is copied, and `view-0.log` gains `Clipboard copy FAILED — nothing was copied` | ~60 Copy buttons went through an unguarded `SetTextAsync`; a faulted `AsyncRelayCommand` rethrows onto the dispatcher WITH our frames on it, so the read guard is structurally obliged to refuse it and the app died. Only the write guard can stop this one |
> | 5 | launch a SECOND copy of the UI (the known duplicate-launch crash) and read `%LOCALAPPDATA%\UE5CEDumper\crash.log` | headline reads `UE5DumpUI crash during STARTUP (uptime 0.??s)` — **not** the old fixed phrase | the honest-phase half, on the one crash that is reproducible on demand |
> | 6 ⚠ control | with an IME active, type into a filter box normally | text arrives as usual; **no** `Input-layer fault swallowed` line appears | proves the guard is dormant when nothing fails — a line here would mean it is swallowing healthy input |
> | 7 ⚠ control, opportunistic | the next time the UI crashes for any reason after startup, read `crash.log` | it says `while RUNNING` (or `during SHUTDOWN`) with a real uptime | proves the phase marker advances; cannot be forced, so tick it when it happens |
> | 8 ⚠ control, opportunistic | if a crash report ever opens `phase still says STARTUP after …` | read it as UNCERTAIN, not as a second defect | the stale-marker branch is a HEURISTIC over a 60 s threshold, and a genuinely slow cold start trips it honestly; the wording now names both possibilities instead of accusing the marker |

### ✅ G8/G9 step 3 corroborated on a SECOND title `[DQ7R-PIPE-2026-08-17]`

DQ I&II HD-2D Remake, build 3262, proxy mode:
```
23:24:39.073 [WARN] DetectVersion: PE VERSIONINFO Product=1.0 File=1.0 — unrecognised
23:24:39.073 [WARN] DetectVersion: PE resource failed, falling back to memory string scan
23:24:39.187 [INFO] DetectVersion: Tier 1 (utf16) '++UE4+Release-4.27' -> 427 at 0x42F5F30
23:24:39.187 [INFO] FindAll: UE Version = 427 (tier=1, detected=yes, lowConfidence=yes, publisher=SQUARE_ENIX)
```
`Product=1.0` is what the offline classifier predicted for this title — a second blind prediction
confirmed verbatim. `object_count` 104,867; patterns `GOBJ_ES53_1` / `GNAM_V8` / `GWLD_TQ_1`, i.e.
**identical to DQ7R's**, which is consistent with the two being the same engine-build family.

**Also from the same launch:** `case_preserving=false` with `probe_ran=true` and `validated=true` →
DQ I&II is a **fourth** confirmed non-CPN title (U2 sweep: TQ2 · Solarpunk · DQ7R · DQ I&II, still
zero CPN found), and another `validated`-clean host, so **G1/X3's amber-banner half still has no
host**.

### ✅ VERIFIED 2026-08-20 — G11: Tier 2 is alive; check it agrees with Tier 1

> ### ✅ ALL THREE STEPS PASS 2026-08-20 `[G11-CACHE-2026-08-20]` — headless, across the whole cache
>
> **Step 1 — no game's detected version moved, checked on 12 titles rather than the two or three
> asked for.** `UE5CEDumper.{COMPUTERNAME}.json` holds **28** game entries; **22** carry
> `versionDetectRev: 5`, i.e. they have already re-detected under the new rule (the other six —
> MindsEye, Solarpunk, TQ2 ×2, ff7rebirth ×2 — sit at rev 1/3/absent because they have not been
> scanned since). Each rev-5 entry's `ueVersion` was cross-checked against `docs/test-games.md`:
>
> ```
> agree = 12    differ = 0    not documented = 10
> ```
> The narrow, unambiguous matches are the ones that carry the result: `DSClient 504` (doc [504]),
> `ManorLords 505` ([505]), `SEED BATTLE 427` ([427]), `RSG 420` ([420,423]),
> `FactoryGameSteam 506` ([503,506]), `STVoyager 506` ([503,506]), `Geri 427` ([425,427,503]),
> `Elliot 504`, `Solarpunk 507`. **Not one title disagrees with its documented version.**
>
> ⚠ Precisely what this does and does not establish: the pre-rev-5 cached value is *overwritten* by
> the re-detect, so this is not a literal before/after — it is the stronger-in-breadth statement that
> after the rev 4→5 re-detect **every** re-detected entry still reports the version the docs record.
> Had rev 5 moved anything, a title would now disagree. Two matches (`ES2`, `Game.exe`) came from
> broad doc rows listing many versions and are weak; they are counted but carry no weight.
>
> **Step 2 — the packed title.** `Avowed-Win64-Shipping.exe` is at `ueVersion 503`,
> `versionDetected: true`, `versionDetectRev: 5`, and `test-games.md` records 503 for it. Unchanged,
> which is the population where mapped-vs-on-disk could have diverged.
>
> **Step 3 — the conditional, and the condition has never fired.** Sweeping **436** `scan-*.log` +
> `init-*.log` files: **0** `DetectVersion: … Tier 2 …` lines, against **71** Tier-1 / VERSIONINFO
> lines. So on every live MAPPED image Tier 1 answered first and masked Tier 2 — exactly what the
> offline 0/170 → 6/170 model predicted, now confirmed on the mapped bytes the model could not see.
> There is no Tier 2 line to cross-check because none has ever been produced.


*Needs the DLL injected. See dev-log build 3112. **Measured 0/170 → 6/170 Tier 2 hits offline, with
Tier 1 agreeing on all six and masking all six** — so live behaviour should be UNCHANGED. This batch
exists to catch the case the offline model cannot see: the DLL scans the MAPPED image, the model
scanned on-disk bytes, and for packed/obfuscated titles those differ.*

1. **🟡 REGRESSION — no game's detected version moves.** `kVersionDetectLogicRev` went 4 → 5, so every
   cached game re-detects once. Note `ueVersion` / `versionDetected` / `lowConfidence` for two or
   three titles before running the new build and compare after. **Identical expected.** Any change
   is a real finding — report the game and the before/after.
   **✅ PASS on TWO titles — see the identical step under G8/G9 below for Elliot.** The DSA half,
   whose "before" is on disk rather than from memory:
   [test-games.md](test-games.md) records DragonSword Awakening as *"PE: 503 → runtime-raised to
   **504** by the `CMC::GravityDirection` property marker"*, and build 3122 produced exactly
   `DetectVersion: PE VERSIONINFO -> UE 5.3 -> 503` → `UE Version = 503 (tier=1, detected=yes,
   lowConfidence=no)` → `raising version 503 -> 504`. **Identical. The batch asks for two or three
   titles, so this stays 🟡 until a second one is checked.**

   **A third title was run, and it does NOT discharge the step `[DQ7R-PIPE-2026-08-17]`.** DQ7R
   detected **427**, matching the `ueVersion: 427` already cached for it — but its PE hash changed
   under a game patch (`69BA4044185AB000` → `69BB84C7069E9000`), so this was a first-ever scan of a
   *different binary* (`scanCount=1`), not the re-detect of a cached entry this step is about. What it
   does establish is weaker and still worth recording: the same title, across a publisher patch,
   detects the same version under `rev=5`. **Left 🟡.**
2. **A packed title is the interesting one.** Avowed is the documented packed case. Confirm its
   detected version is unchanged; this is the population where mapped-vs-on-disk could diverge.
3. **If a `Tier 2` line ever appears in `scan-0.log`, cross-check it.** Grep for
   `DetectVersion: Tier 2 Release prefix -> NNN`. On every corpus image Tier 1 answered first, so a
   Tier 2 line means a stripped-tag title reached the new path — record the game, the version, and
   whether it matches what the game actually is. That is the first real evidence Tier 2 works.
   **⬜ Not reachable from an ordinary session, and `[DSA-2026-08-16]` shows why:** a title whose PE
   VERSIONINFO is intact answers at `DetectVersion: PE VERSIONINFO -> UE 5.3 -> 503` and **never
   enters the tier ladder at all** — no `Tier 1 (ascii|utf16)` line either. Steps 3–4 here, step 3 of
   G8/G9, and every step of the G2 batch need a title with a **stripped version resource** (Elliot).
   **⚠ Correction: a stripped version resource is necessary but NOT sufficient, and Elliot is the
   wrong example** — it also lacks the release tag, so it produces no tier line at all. The ladder
   needs *unrecognised PE VERSIONINFO* **and** *a findable tag*; the three installed titles that have
   both are listed under G8/G9 step 3.
   **🟡 The ladder has now been entered, and Tier 2 still did not fire `[DQ7R-PIPE-2026-08-17]`.**
   DQ7R reached the memory scan and **Tier 1 (utf16) answered first** (`'++UE4+Release-4.27' -> 427`),
   so no `Tier 2 Release prefix` line was produced. That is the offline model's prediction reproduced
   live — Tier 1 agreeing on and masking every Tier 2 hit — and it is the first time the ladder is
   known to be *reachable* on this machine rather than merely modelled. **It is still not evidence
   that Tier 2 works**, and per step 4's warning this must not be read as closing G11.
4. **⚠ REGRESSION — Tier 3 still behaves.** A title that previously reported `Tier 3 (low
   confidence)` must still report the same version. The bare-needle change touches Tier 2 only, and
   two unit rails assert that, but Tier 3 is what stripped-tag games actually land on today.

> ### ✅ STEP 3 RUN 2026-08-20 `[G11-AINCRAD-2026-08-20]` — Tier 2 still did not fire, and the BINARY says that is CORRECT
>
> **Echoes of Aincrad Demo** (UE, Steam), never swept before — its first scan on this machine.
> Injected directly (no proxy deployed), headless.
>
> ```
> DetectVersion: PE VERSIONINFO Product=1.0 File=1.0 — unrecognised     <- the precondition holds
> DetectVersion: PE resource failed, falling back to memory string scan
> DetectVersion: Could not detect UE version from PE or memory (pre-UE4 markers 0/4)
> FindAll: UE version detection failed — using default 504
> FindAll: UE Version = 504 (tier=0, detected=no, lowConfidence=yes, publisher=-)
> ```
> **No `Tier 1`, no `Tier 2 Release prefix`, no `Tier 3` line.**
>
> ⭐ **The offline control is what turns that from a suspicion into a result.** Scanning the 427 MB
> shipping exe directly, independently of the DLL:
>
> | needle | hits |
> |---|---|
> | `++UE4+Release-N.N` (ascii) | **0** |
> | `++UE5+Release-N.N` (ascii) | **0** |
> | bare `Release-N.N` (ascii) — *what Tier 2 looks for* | **0** |
> | `++UE[45]` (UTF-16) | **0** |
> | `UnrealEngine` | 2 |
>
> The title is **fully tag-stripped**, so no tier *could* fire. Tier 2's silence here is **correct
> behaviour, not a defect** — and without this control the run would have looked like a third
> failure of Tier 2.
>
> ⇒ **Third independent line of evidence that Tier 2 has never been exercised**, each failing for a
> different reason: the 25-game log sweep (step 4) found 0 across 65 runs; DQ7R reached the ladder
> but **Tier 1 answered first and masked it**; and this title reaches the ladder with **nothing for
> any tier to match**. Tier 2 remains unproven, and no installed title can prove it.
>
> **Truth, as the step asks to record it:** undeterminable by string scan — the binary carries no
> engine tag. And the DLL says so rather than guessing: `tier=0, detected=no, lowConfidence=yes`,
> with `504` labelled a **default**, not a detection.
> ℹ️ Worth noting for the roadmap: the scan **succeeded anyway** — `GObjects=0x147FD0B00`,
> `GNames=0x148675600`, **70,655 objects** — so an undetected version is not fatal on this title.

> ### 🟡 STEP 4 — NO SUBJECT EXISTS ON THIS MACHINE, and that is a measurement, 2026-08-20
>
> The step needs *"a title that previously reported `Tier 3 (low confidence)`"*. Swept **every**
> `DetectVersion:` line in `%LOCALAPPDATA%\UE5CEDumper\Logs` — **25 game folders, 65 detection
> runs**:
>
> | outcome | runs |
> |---|---|
> | `PE VERSIONINFO …` resolved before the memory scan | 31 |
> | `PE resource failed, falling back to memory string scan` | 35 |
> | `Tier 1 (ascii\|utf16) '++UE4+Release-4.xx' -> N` | **6** |
> | `Tier 2 Release prefix -> N` | **0** |
> | `Tier 3 candidate (deferred)` / `Tier 3 (low confidence)` | **0** |
> | `Could not detect UE version from PE or memory` | 28 |
>
> **The absence is not "we did not look", and it is not "Tier 3 never ran" either.** Two controls:
> * ⚠ **grep by FORMAT STRING, and check the string is one that is actually LOGGED** — most `Tier N`
>   text in `Genau.cpp` is *comments*. The four real emitters are `Tier 1 (%s) '%s%s' -> %u at 0x%zX`,
>   `Tier 2 Release prefix -> %u at 0x%zX`, `Tier 3 candidate (deferred) -> %u at 0x%zX` and
>   `Tier 3 (low confidence) -> %u`. **Tier 1 fires 6 times**, so the family demonstrably reaches the
>   log; the zero for Tier 2/3 is about those tiers, not about the grep.
> * ⭐ **The 28 terminal WARNs are positive evidence that Tier 3 EXECUTED and missed.** `Genau.cpp`'s
>   own comment at the terminal branch says it: *"Tier 2 and Tier 3 all found nothing, so 'no UE4/UE5
>   evidence' is a STRUCTURAL property"*. Reaching that line means the whole ladder was walked. So
>   Tier 3 has run **~28 times across this corpus and produced a candidate zero times**.
>
> ⇒ Step 4 is **not runnable here and cannot become runnable by trying harder** — it is a 第 5 步
> item (no sample exists anywhere), where the absence *is* the signal. It stays 🟡 rather than ✅:
> nothing regressed, but nothing was exercised either. The three candidate titles are the ones named
> under G8/G9 step 3 (*unrecognised PE VERSIONINFO* **and** *a findable tag*); if one is ever
> installed, this is the row to re-run.

### ✅ VERIFIED 2026-08-20 — G8 / G9: version detection after the tier-rule change

> ### ✅ STEP 1's "two or three titles" IS NOW TWELVE — 2026-08-20, see `[G11-CACHE-2026-08-20]`
>
> Step 1 sat at 🟡 only because it had two titles against a bar of "two or three". The same
> assertion — *"every game still detects the same version"* — was then checked across the **whole
> hint cache**: 28 entries, **22** already re-detected (`versionDetectRev: 5`), **12** cross-checked
> against `docs/test-games.md`, **0 disagreements**. Steps 2 and 3 were already ✅.
>
> ⭐ **That sweep covers G8/G9 as well as G11, and it is worth saying why rather than assuming it.**
> The rev counter went **3 → 4** for G8/G9 and **4 → 5** for G11. Every entry now stamped `rev: 5`
> has therefore re-detected *through both changes*, so a version moved by either one would be
> visible as a title disagreeing with its documented value today. None does. Steps 2 (`rev` written
> back, re-detect happens once) and 3 (Tier 1 untouched) were already discharged on Elliot and DQ7R.


*Needs the DLL injected. See dev-log build 3105. **Expect NO visible difference** — both fixes are
measured no-ops on all 85 PE images in the local corpus, so this batch is a REGRESSION check, not a
demonstration. Anything that does change is a finding.*

1. **🟡 REGRESSION — every game still detects the same version.** `kVersionDetectLogicRev` went
   3 → 4, so the first launch after this build **re-detects every cached game once** (~0.35 s).
   For two or three titles, note `ueVersion` / `versionDetected` / `lowConfidence` in
   `%LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.{Machine}.json` **before** running the new build, then
   compare after. **They must be identical.** A changed version is a real finding — report it with
   the game and the before/after values.
   **✅ PASS on TWO titles — the batch's own bar.** DSA `[DSA-2026-08-16]`: 503 → 504, matching
   test-games.md's build-2779 record. Elliot `[ELLIOT-2026-08-16]`: the cold `scan #1` produced
   `UE Version = 427 (tier=0, detected=no, lowConfidence=yes, publisher=SQUARE_ENIX)` and
   `UE5_Init: Complete (UE504…)` — **word for word** what test-games.md records for it ("PE version
   stripped → publisher fallback 427, upgraded via tagged FFieldVariant→503 + CMC::GravityDirection
   →504"). Two titles, two exact matches, under `rev=5`.
2. **✅ The re-detect happens once, not every launch.** Launch the same game twice more and confirm
   `scan-0.log` shows `skipped DetectVersion` on the later runs. If it re-detects every time, the
   rev stamp is not being written back.
   **PASS `[ELLIOT-2026-08-16]`** — three launches of Elliot in one evening: 20:12 ran the full
   `DetectVersion`, then **both** 20:26 and 20:49 logged
   `UE Version = 504 (cached, **rev=5**, detected=no, lowConf=yes) — skipped DetectVersion`. The rev
   stamp is written back and honoured.
3. **⚠ REGRESSION — a Tier 1 game is untouched.** G8/G9 only touch Tier 2/3, and Tier 1 returns
   first on nearly every real title. Confirm `DetectVersion: Tier 1 (ascii|utf16) …` still appears
   and still names the same version.
   **✅ PASS `[DQ7R-PIPE-2026-08-17]` — on DQ7R, over the pipe, build 3262.** `scan-0.log`:
   ```
   22:34:01.983 [WARN] DetectVersion: PE VERSIONINFO Product=1.1 File=1.1 — unrecognised
   22:34:01.983 [WARN] DetectVersion: PE resource failed, falling back to memory string scan
   22:34:02.299 [INFO] DetectVersion: Tier 1 (utf16) '++UE4+Release-4.27' -> 427 at 0x4BBC6D8
   22:34:02.299 [INFO] FindAll: UE Version = 427 (tier=1, detected=yes, lowConfidence=yes, publisher=SQUARE_ENIX)
   ```
   The Tier 1 line appears, in the `utf16` flavour, and names **427** — matching what
   [test-games.md](test-games.md) and the pre-existing cache entry both carry for this title. That is
   the whole of what this step asks.

   **`lowConfidence=yes` alongside `tier=1` is NOT a finding — it is documented intent.** Checked in
   source before filing: [Genau.cpp](../dll/src/Genau.cpp) `if (publisher && out.UEVersion >=
   Grimoire::MIN_SUPPORTED_UE_VERSION) out.bLowConfidence = true;`, whose comment says a publisher
   thumbprint flags low confidence *"even when detection produced a clean Tier 1 / Tier 2 hit, since
   those strings can come from bundled SDKs"*. DQ7R matches `SQUARE_ENIX`.

   ⚠ **The earlier note here named the wrong host, and the correction is worth keeping.** It said
   Elliot was the title for this step. It is not: Elliot's PE resource fails *and* it carries no
   `++UE[45]+Release-` tag at all, so it falls past every tier to the publisher fallback (`tier=0`)
   and can never produce a Tier 1 line. The requirement is **two** properties, not one —
   *unrecognised PE VERSIONINFO* **and** *a findable release tag*. An offline sweep of all 16
   installed UE titles (`version.dll` APIs for the PE half, the `ue_version.py` regexes for the tag
   half) found exactly **three** that qualify, and none of them had ever been tried:

   | title | PE VERSIONINFO | tag | image |
   |---|---|---|---|
   | **DQ7R** | `Product=1.1` unrecognised | 4.27 | 99 MB |
   | **DQ I&II HD-2D Remake** | `Product=1.0` unrecognised | 4.27 | 87 MB |
   | **OCTOPATH TRAVELER** | `Product=1.0` unrecognised | 4.18 | 43 MB |

   The other twelve — TQ2, DSA, Solarpunk, STVoyager, Lushfoil, Manor Lords, ES2, EVERSPACE, SEED,
   Geri, Light Maze — all short-circuit at the PE fast path and cannot reach the ladder. The
   classifier reproduced the exact wording of two independently known log lines before being trusted
   (Elliot's `Product=1.2 File=1.2 — unrecognised` and DSA's `PE VERSIONINFO -> UE 5.3 -> 503`), then
   predicted DQ7R's `Product=1.1 File=1.1` and was confirmed verbatim by the run above.

   *Incidental, and it matters for anyone diffing this title:* **DQ7R's PE hash has changed** —
   `69BA4044185AB000` (2026-06-06) → `69BB84C7069E9000`. So this was a first-ever scan of a patched
   binary (`scanCount=1`), not a re-detect of a cached one. `ueVersion` is 427 on both, but the
   winning GWorld pattern moved `GWLD_GH_1` → `GWLD_TQ_1` (gNames `GNAM_V8` and gObjects
   `GOBJ_ES53_1` unchanged). The stale entry was left in place.

**Step 1 additionally re-confirmed `[G89-PIPE-2026-08-17]`, and more strongly than the step asks.**
Rather than comparing across a scan, the cached entry was **deleted outright** and a cold re-detect
reproduced every value: `ueVersion` 504, `versionDetected` true, `lowConfidence` false,
`versionDetectRev` 5 — all identical to the recorded before-state. `scanCount` reset 3 → 1, which is
expected when the entry is removed. The two other DumperTest entries were untouched and also compared
identical across the session.

*Incidental, not a finding:* the cold scan's winning GObjects pattern was `GOBJ_ES53_1`, where the
hinted run earlier the same session reported `GOBJ_GH_4` (hits=99). A hint short-circuits the sweep,
so cold and hinted runs legitimately crown different patterns; both resolved in-module and both
worked. Worth knowing before anyone diffs pattern ids across a hint boundary and calls it a
regression.
4. **G11 context — do not misread a pass here.** Tier 2 has never fired on any binary we own (the
   trailing-dot defect), so a green result on steps 1–3 says these fixes did no harm; it says
   **nothing** about Tier 2 working. Do not close G11 on the strength of this batch.

### ✅ VERIFIED 2026-08-20 — G10 / MA1: the hint cache must stop destroying itself (steps 1-6 all pass)

*Needs the DLL injected. See dev-log builds 3091 / 3095. **Step 1's control already exists on disk**
and is decisive — this is the rare case where the regression was captured before the fix.*

1. **⚠ G10 — THE DECISIVE ONE, and it is a two-launch test.** Pick a title where a pattern has many
   matches (DumperTest is the documented case, PE `6A7EA60310F17000`). Delete that PE hash's
   `gNames` entry from `%LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.{Machine}.json`, launch and scan
   (run #1 writes the hint), then launch and scan **again**.
   **PASS** = run #2 shows `Hint HIT: 'GNAM_V1'`, or at worst a `Hint MISS` followed by a real
   winner. **FAIL (the shipped bug)** = `=== GNames: … NONE validated ===`, which is what
   `Logs/DumperTest/scan-0.log` recorded at 13:34 on 2026-08-14 while `scan-20260814-132936.log`
   found `winner: GNAM_V1` five minutes earlier on the same binary.
   **✅ PASS, decisively `[ELLIOT-2026-08-16]`.** Not DumperTest but a better subject: on Elliot
   (`PE=6A577F4E1D91B000`) the cold run (`scan #1`, 20:12) logged
   `[GObjects] GOBJ_ES53_1 hits=74 [WINNER]` — **74 matches**, i.e. 73 wrong candidates for a
   "stops at the first match" fast path to fall into. Both warm runs (`scan #4` 20:26 and `scan #7`
   20:49) answered `Hint HIT: 'GOBJ_ES53_1'`, plus `Hint HIT` on `GNAM_V8` and `GWLD_TQ_1`, and
   **`NONE validated` appears nowhere in any of the three logs.** That is the shipped bug's exact
   shape, exercised and clean.
> ### ✅ STEP 2 PASSES 2026-08-20 `[G10S2-DECIDED-2026-08-20]` — the missing ingredient was already on disk
>
> The 2026-08-19 attempt below stalled on one thing: *"a pattern with hits ≫ 1 that does NOT validate
> is the missing ingredient; neither Elliot nor DumperTest has one today."* **That was wrong about
> DumperTest, and the counter-evidence was already in its own logs.** Sweeping all **173** `scan-*.log`
> files for `hits>1` together with `(not validated)` returns **75** such patterns, four of them on
> DumperTest itself:
> ```
> DumperTest  [GNames] GNAM_V2  hits=1133  (not validated)
> DumperTest  [GNames] GNAM_V5  hits=1101  (not validated)
> DumperTest  [GNames] GNAM_V4  hits=619   (not validated)
> DumperTest  [GNames] GNAM_V3  hits=468   (not validated)
> ```
> The earlier attempt staged a **GWorld** hint and got a HIT; the answer was to stage a **GNames** one.
>
> **The run.** `gNames.patternId` set to `GNAM_V2` in `UE5CEDumper.{COMPUTERNAME}.json` (backup first, JSON
> load→edit→dump, never a text rewrite — plan §4.2's rule), then DumperTest launched and injected:
> ```
> [GNames] Hint: trying cached pattern 'GNAM_V2' first...
> [GNames] Hint MISS: 'GNAM_V2' (1133 matches, none validated; scan 124068 us) — falling back to full scan
> [GNames] GNAM_V1: 166 matches, validated -> 0x7FF6A8E668C0   [WINNER]
> ```
> ⭐ **`1133 matches`, not `1 match`** — which is the entire assertion. And the probe is **not**
> confounded this time: the same log's cold table independently prints
> `[GNames] GNAM_V2 hits=1133 (not validated)`, so 1133 is the true count computed by a *different*
> code path in the *same* run. The broken "always 1" implementation would have printed `1 match`
> against that same 1133.
>
> The fallback also behaved: the MISS fell through to the full scan, `GNAM_V1` won with 166 validated
> matches, and `NONE validated` appears nowhere. The cache then **self-healed** — `gNames.patternId`
> is back to `GNAM_V1` at `scan #25`, with all 28 games intact; the pre-edit backup is kept at
> `out/g10s2/`.

> ### ⬜ STEP 2 ATTEMPTED ON DumperTest 2026-08-19 `[G10S2-2026-08-19]` — superseded by the run above; kept for the reasoning
>
> The step needs a `Hint MISS` whose true match count is **large**, so a correct implementation and
> the broken "always 1" one print different lines. On DumperTest Development the cold table gives
> `[GObjects] GOBJ_ES53_1 hits=619 [WINNER]` and `[GNames] GNAM_V7 hits=1 (not validated)` — so
> GNames is confounded here exactly as it is on Elliot.
>
> The attempt: stage `gWorld.patternId = "GWLD_V3"` (the loose pattern — **5,140 matches** on this
> binary) hoping for a big-count MISS. **It HIT instead**: `Hint HIT: 'GWLD_V3' -> 0x7FF648148D40
> (skipping remaining patterns)`. So a many-match pattern that also *fails validation* is still not
> in hand, and the confound survives. ⇒ **A pattern with hits ≫ 1 that does NOT validate is the
> missing ingredient**; neither Elliot nor DumperTest has one today.
>
> ⭐ **Two things the attempt DID establish, both worth keeping:**
> 1. **The GWorld decoy recovery works, and caught this.** The poisoned hint pinned GWorld to
>    `0x7FF648148D40`, and `FindAll: Complete` published exactly that — then
>    `ExtraScanGWorld: Starting instance scan… GWorld at 0x7FF6483188A0 -> UWorld 0x215516240C0
>    (index=24970, 1 candidate(s), active)` **corrected it to the cold-scan address**, and
>    `walk_world` then returned 58 entries. The two slots hold genuinely different `UWorld*`
>    values, so this was a real decoy, not two names for one thing.
> 2. ⚠ **But the cache then saved the AOB winner, not the corrected result** — the entry was written
>    back as `gWorld.patternId = "GWLD_V3"`. A wrong pattern therefore **re-hints itself forever**,
>    paying the instance scan on every launch. Low real-world risk (priority ordering means
>    `GWLD_TQ_1` is reached first on a cold scan — only 2 GWorld patterns were tried), but it is a
>    genuine "the report and the reality are computed by different paths" shape. The poisoned entry
>    was **deleted** afterwards so the next DumperTest scan is cold and re-derives `GWLD_TQ_1`.
>
2. 🟡 **G10 — the count no longer lies. NOT DECIDABLE ON ELLIOT — use DumperTest.** In `scan-0.log`,
   a `Hint MISS` line must report the real match count (`(%zu matches, none validated; …)`) and never
   say `1 match` for a pattern the cold run logged with hundreds.
   **Why Elliot cannot answer it (measured 2026-08-18).** A `Hint MISS` *is* stageable here — writing
   `gNames.patternId = "GNAM_V7"` into the cache would produce one, because every cold run logs
   `[GNames] GNAM_V7 hits=1 (not validated)`. But its **true count is 1**, so a correct
   implementation and the broken "always 1" one print the *same line*. That is a confounded probe
   (working-lessons §1.10a) and would return PASS either way.
   The full per-pattern table from Elliot's cold scan, which is what rules it out:

   | target | pattern | hits |
   |---|---|---|
   | GObjects | `GOBJ_ES53_1` | **74** — but it **validates**, so hinting it gives a `Hint HIT`, not a MISS |
   | GNames | `GNAM_V7` | 1, *not validated* — a MISS, but a useless one |
   | GNames | `GNAM_V8` | 1 → WINNER |
   | GWorld / SparseDelegates / GEngine | `GWLD_TQ_1` / `SPARSE_ES2_1` / `GENG_X1` | 1 each → WINNER |

   **What the subject must have: a pattern with MANY matches, none of which validate.** Elliot has no
   such pattern — its only many-match pattern is the winner. Use the case this row already names,
   **DumperTest (PE `6A7EA60310F17000`)**, and hint `gNames` to a high-count non-validating pattern
   there.
3. **✅ REGRESSION — a warm launch is still FAST.** The hint path now scans all matches instead of
   stopping at the first, so a genuine `Hint HIT` costs slightly more. Confirm run #2 is still far
   faster than a cold scan (`[X] AOB scan total: %lld us`), not merely correct.
   **PASS `[ELLIOT-2026-08-16]`, and the run carries its own negative control** — same binary, same
   machine, cold `scan #1` (20:12) vs warm `scan #7` (20:49):

   | target | cold | warm | ratio |
   |---|---|---|---|
   | GObjects *(hinted)* | 1,199,277 µs | 275,868 µs | **4.3×** |
   | GNames *(hinted)* | 1,125,987 µs | 287,709 µs | **3.9×** |
   | GWorld *(hinted)* | 1,239,921 µs | 264,492 µs | **4.7×** |
   | **SparseDelegates *(NOT hinted)*** | 1,449,253 µs | 1,462,536 µs | **1.00×** |
   | **GEngine *(NOT hinted)*** | 1,086,361 µs | 1,111,045 µs | **1.02×** |

   The two unhinted targets are the control that makes this a measurement rather than a number: they
   sit in the *same* process, on the *same* warm page cache, and did **not** speed up. So the 4× is
   the hint path and not disk caching or machine warm-up. Conditions: `Elliot-Win64-Shipping.exe`,
   **482,390,784 bytes**, build 3122.
4. ✅ **DONE 2026-08-18 `[ELLIOT-MA1-2026-08-18]` — the cancel fires, and every guard with it.**
   Elliot through its `dxgi` proxy, hint entry dropped (`tools/verify/cold_detect.py drop
   6A577F4E1D91B000`) so the scan is cold: **8.0 s**, against **3.3 s** warm.

   ```
   13:34:43.834 UE5_Init: Starting initialization...
   13:34:46.459 UE5_Shutdown: Cleaning up...                       <- fired 2.5 s into the scan
   13:34:47.310 [GNames]          AOB scan CANCELLED after 0/4 batches (client gone / shutdown)
   13:34:47.311 [GWorld]          AOB scan CANCELLED after 0/7 batches
   13:34:47.311 [SparseDelegates] AOB scan CANCELLED after 0/2 batches
   13:34:47.311 FindSparseDelegateStorage: CANCELLED — not latching, the next scan will retry
   13:34:47.311 FindAll: scan was CANCELLED — NOT writing the hint cache
   ```
   Both required lines are present and land **851 ms** after the shutdown — inside the ~1 s bar.

   ### ⚠ The staging is the hard part — four routes were measured and DO NOT WORK
   | route | what happened | cause |
   |---|---|---|
   | Untick the CE record ~2 s in (**how this step is written**) | click took effect **8.5 s later**, after the scan ended | the `.CT` `init` script blocks **CE's GUI thread** for the whole scan. **Not performable as written.** |
   | Kill the UI mid-scan (landed 2.8 s into an 8.0 s scan) | scan **completed normally** | `trigger_scan` is **async**; no in-flight command for a disconnect to cancel. Same cause as B4's third trap. |
   | CE Lua `createThread` + fixed `sleep` | fired **before** the scan started | a GUI round trip is 2–6 s, and **each operator action costs ~10 s of wall clock**, so two consecutive actions cannot hit an 8 s window. ⚠ A leftover thread later shut down a *fresh* Elliot — CE keeps one Lua state, so **restart CE between attempts**. |
   | CE Lua thread polling `init-0.log` | printed `NEVER SAW SCAN START` | **CE Lua's `io.open` cannot read our live log** (writer share mode — [working-lessons.md](working-lessons.md) §3). Python's reader can. |

   ### ▶ What DOES work — a two-process chain, both halves pre-armed
   1. `py tools/verify/kill_on_marker.py <init-0.log> "Starting initialization" --touch <flagfile>
      --after-ms 2500` — Python watches the log (it *can* read it) and drops an ordinary flag file.
   2. In CE, **pre-armed before the scan**:
      `createThread(function() ... poll for <flagfile> ... executeCodeEx(0,60000,getAddress('dxgi.UE5_Shutdown')) end)`
      — CE Lua *can* read a file nobody holds open.

   Neither half is on the operator's critical path, so the 8 s window is hit every time. Give the CE
   poll loop a **generous** timeout: a mis-registered Start Scan click cost 3 minutes and the first
   loop (120 s) had already expired when the flag finally appeared.

5. ✅ **DONE 2026-08-18 — MA1's three guards, each checked separately.**
   * **(a) the hint cache is untouched.** After the cancelled run, Elliot's entry
     `6A577F4E1D91B000` was **still absent** from `UE5CEDumper.{COMPUTERNAME}.json` (28 games, none of them
     Elliot). The cancelled scan wrote nothing, exactly as `FindAll` promised.
   * **(b) a re-enable re-scans rather than short-circuiting.** `UE5_AutoStart` in the **same
     process** ran a full scan to `UE5_Init: Complete (UE504, …, Objects=85068)`.
     ⚠ **The obvious test is wrong and looks like a defect.** Calling **`UE5_Init` directly** instead
     re-scans and is **cancelled at `0/7` batches before doing any work**, every time — which reads
     exactly like a stale cancel flag. It is not: [`Tot.h`](../dll/src/Tot.h) states `g_shutdown` is
     *"cleared only by `Fern::Start()`"*, and [`Frieren.cpp:798-812`](../dll/src/Frieren.cpp:798)
     puts `Tot::ResetShutdown()` at the top of **`UE5_AutoStart`** precisely so a re-enable does not
     "rescan with `g_shutdown` still latched". **`UE5_AutoStart` is the re-enable entry point;
     `UE5_Init` alone is not.** Filed nothing — the header had already answered it
     (working-lessons §2.4).
   * **(c) the sparse latch does not stick.** `FindSparseDelegateStorage: Scanning` appears **3×** in
     `scan-0.log`: the cancelled run, the second cancelled attempt, and the healthy re-scan — so
     `CANCELLED — not latching, the next scan will retry` is literally true.

6. ✅ **DONE 2026-08-18 — REGRESSION: a healthy scan still completes and still saves.** The
   `UE5_AutoStart` re-scan above wrote the entry back with real AOB hints —
   `gObjects: GOBJ_ES53_1 (1 of 2 tried)`, `gNames: GNAM_V8 (2 of 5)`, `scanCount: 1` — and its run
   logged **no** `CANCELLED` line. So `bScanCancelled` is not over-broad.

7. **⚠ SUPERSEDED — original step 5 text, kept for the method** (a control that passes is how a bug in a fix gets
   found): after that cancelled run, (a) **diff `UE5CEDumper.{Machine}.json`** — it must be
   *unchanged* for that PE hash; (b) re-enable in the **same** process and confirm a full re-scan
   runs rather than short-circuiting (the `UE5_Init` latch guard); (c) drill into a
   `MulticastSparseDelegateProperty` and confirm `FindSparseDelegateStorage: Scanning` appears a
   **second** time rather than a latched 0 (the sparse latch guard).
8. **⚠ SUPERSEDED — original step 6 text, kept for the method.** Connect the UI, disconnect it
   mid-command, reconnect, and confirm a fresh scan resolves normally, writes the hint cache, and
   shows **no** `CANCELLED` line. This is what keeps `bScanCancelled` from being widened to
   `Tot::Requested()`, which would refuse the latch on a scan that finished fine.

**Not covered:** `Macht` still carries no poll (deliberate — see the comment above its AOB
declarations), and **MA2**, the `ScanRegionBatch` per-pattern underflow, is unreachable until
`AOBScanBatch` is given a `moduleBase`.

### ✅ AE2 / AE3 STEPS 1–3 PASS 2026-08-20 `[AE23-UI-2026-08-20]` — the race does not reproduce

> DumperTest + the real UI. The row's own warning is the design constraint: *"a list of only one
> kind cannot show it — **record what the filter was**"*.
>
> **The filter, recorded: `DumperTest` → 22 results**, and it is genuinely mixed —
> **10 class-like rows** (`Class` ×6, `Enum` ×1, `ScriptStruct` ×3) followed by **12 instance rows**
> (`Default__DumperTest*` and the live `DumperTestActor` / `GameMode` / `Subsystem`). ⚠ Note the two
> kinds are **blocked, not alternating**, so the instance→class-like adjacency the old failure needs
> is crossed by scrolling **UP**, not down. Scrolling down only ever gives class-like→instance.
>
> | # | what was done | result |
> |---|---|---|
> | **1** regression | clicked the instance row `DumperTestActor / DumperTestActor` | header `DumperTestActor`, `//Script/DumperTest/DumperTestActor`, Super Class `Actor`, **Properties Size 1760**, fields populated |
> | **2** the race, instance → class-like | **9 rapid ↑** from that row, crossing 7 instance rows into the class-like block | landed on `ScriptStruct DumperTestVec3f`; header reads **`DumperTestVec3f`**, Super Class blank, **Properties Size 12**, fields **X / Y / Z** FloatProperty at `0x0/0x4/0x8` |
> | **2b** the reverse crossing | **5 rapid ↓** back across the boundary | landed on `DumperTestHUD / Default__DumperTestHUD`; header **`DumperTestHUD`**, Super Class **`HUD`**, **Properties Size 928** |
> | **3** the spinner | after both bursts | no loading indicator left on; each panel settled with its own content |
>
> ⭐ **The three headers are mutually unmistakable — 1760 / 12 / 928 properties, and X-Y-Z vs a
> replication block** — so "the header matches the highlighted row" is checked against content that
> could not be confused, not against a name that might coincidentally agree.
>
> ⚠ **Scope, stated because the stimulus is not identical:** the fast scroll was a **burst of
> discrete synthetic key events** (`repeat: 9`), not an OS auto-repeat from a physically held key.
> It produces the same rapid succession of selection changes and async loads, which is the mechanism
> the race lives in, but a real held key repeats at the OS rate and this run does not reproduce that
> timing exactly.

### ✅ AE2 / AE3 — original checklist (kept for the steps; steps 1-3 verified, see the block above)

*Needs a game connected, but nothing else — the Object Tree is a permanent left pane beside the
Class/Struct panel, so every check is "do the two halves agree". See dev-log builds 3067 / 3068. The
11 new tests drive the ViewModel directly and therefore bypass Avalonia's ListBox entirely; what is
unproven is the real gesture under key repeat.*

1. **⚠ REGRESSION FIRST — ordinary selection still works.** Click a handful of tree nodes, both
   instances and class-like rows (`*_C`, `ScriptStruct`, `Function`). The header must track each
   click, and fields must populate. Everything below changed this path.
2. **AE2, the actual race.** Keyword-filter the tree so instances and class-like rows are
   **interleaved**, then hold ↓ to scroll through them fast and release on a class-like row. The
   Class/Struct header must match the highlighted row. The old failure needed exactly
   instance-then-class-like, so a list of only one kind cannot show it — **record what the filter
   was**, since a run over a homogeneous list proves nothing.
3. **AE2, the spinner.** During the same fast scroll the loading indicator must not stick on after
   the panel settles, and must not flicker off while a load is still running.
4. **AE3 — the retry that used to be refused.** Get a walk to fail on a node *after* a successful
   load (easiest: select a node, then travel/unload so its class address goes stale, then re-select
   it). The error line must appear, and **clicking the same row again must retry** — previously it
   was silently ignored and the panel stayed on the earlier class.
5. **AE3 — the cross-tab path, which needs no failure at all.** Select tree node P → use any
   handoff that pushes a class into Class/Struct (Interesting Funcs, Property Search, Dump Explorer)
   → click node P again. It must reload P. Before the fix the panel stayed on the handed-off class.
6. **The dedupe still holds.** Type in the tree's filter box while a node is selected (each
   keystroke nulls the selection) — this must NOT re-walk the class repeatedly, and must not blank
   the panel.

### ✅ DONE 2026-08-18 — U4 / U16 / U6 / F3: the three never-erased caches in `Ubel`

> **Ran on DumperTest Development, dist 3262, driven from CE's Lua Engine.** The exports resolve by
> name (`getAddress('UE5Dumper.UE5_WalkClassBegin')` → `0x7FFE762B6E90`), so the call sites — the
> thing no test target can reach — were exercised directly.
>
> ⚠ **Signature trap, recorded because it cost a round trip:** `executeCodeEx` is
> `(callmethod, timeout, address, …)`, **not** `(timeout, …)`. Passing the timeout first returns
> `nil, "Invalid callmethod:5000"`. That is also a live confirmation that CE's `nil, reason`
> channel carries a usable reason (cf. the `executeCodeEx` row and `ce-plugin-sdk-notes.md` §13).
>
> | step | verdict | evidence |
> |---|---|---|
> | 1 regression | ✅ | Object Tree loaded 25,172/25,179 named (100.0%); Live Walker drilled `DumperTestActor_0` and every container; Property Search returned hits on four different queries; enum fields render member names (`ROLE_Authority`, `EActorUpdateOverlapsMe…`), not raw ints. |
> | 2 **U4** | ✅ | `A = 0x1F144477910` (a UObject instance, not a UStruct), `size = 556035168`. Two calls produced **two** `WalkClass: … at 0x1F144477910` DEBUG lines and **two** `WALK:safe … refusing to cache 0x1f144477910 — PropertiesSize=556035168 (read ok); not a UStruct, or recycled memory`. Before the fix the second call was served from the poisoned entry and logged nothing. ⚠ *Conditions:* the raw bytes at `A+0x58` read `60 6C 24 21` (=556270688) a minute earlier — those are live `AActor` bitfield bytes and they move; the point is that any reading of them is garbage as a `PropertiesSize`, and both were refused. A **second, independent** witness came free: `0x1F1408E1200` (a mis-transcribed tree address, not a UClass) was walked **4** times and refused **4** times, logging all four. |
> | 3 **U4 honest half** | ✅ | `FDateTime` `UScriptStruct` @`0x1F159AA8F80`, visited **4** times → exactly **ONE** cold-walk pair (`WalkClass: DateTime (super=, size=8)` + `— 1 fields`) and silence for visits 2–4. The gate rejects garbage, not small/empty structs. ⚠ **Strictly-zero-field case NOT demonstrated**: `FDateTime` reports **1** field (`InjectIntrinsicStructFields` supplies `Ticks`), so "0 fields is still cached" remains unwitnessed — every 0-field walk seen this session was a *refusal*. Do not read this row as closing that. |
> | 4 **U6/F3** | ✅ *(deterministic alternative)* | `DumperTestActor_0` `+0x18` = `7C C0 08 00 | 01 00 00 00` (ComparisonIndex `0x0008C07C`, Number 1). Wrote `ChaosDebugDrawActor`'s index `0x00150570` from CE, pressed Refresh: the live header changed to **`ChaosDebugDrawActor_0`** while the class stayed `DumperTestActor` (correct — only the object's FName moved). Restored `0x0008C07C`. The name memo is keyed on the input bytes, so no stale decode survived. *(The level-travel flavour was not run — this sample has no second level.)* ⚠ The **breadcrumb** still read `DumperTestActor0`, which is a historical crumb, not a stale cache — exactly the surface the step warns not to judge from. |
> | 5 **U16** | 🟡 PARTIAL → ✅ **both gaps closed 2026-08-22**, see `[U16-ENUM65-2026-08-22]` | **138** `ResolveEnumValue` lines in `walk-0.log`, **0** with `N != M`, and **0** `GetEnumEntries: … truncated read` in *any* log in the folder. Healthy tables are still cached. ⚠ Two gaps: the largest table seen is **26** entries (no `EPhysicalSurface`-scale enum exists in this sample, so "large" is only exercised to 26), and the **CE DropDownList half was not checked**. |
>
> **Unchanged by this run** (as the note below already says): U5, and the class-cache-name panels.

<details><summary>Original U4 / U16 / U6 / F3 steps — kept for the method</summary>

### ✅ 5-of-5 CLOSED 2026-08-18 — AA14–AA20: the CE Lua invoke path in a real game

*Needs CE + a game + the DLL injected. See dev-log build 3039. The Lua rig (63 checks) covers the
logic against stubs; what it cannot cover is a real ProcessEvent.*

1. **⚠ REGRESSION FIRST — an ordinary invoke still works.** UE5DumpUI → Interesting Funcs → pick a
   no-arg or int-arg function → **Copy AA Script (Baked)** → paste into CE → enable. It must still
   fire. Everything below changed this path, so this is the check that matters most.
2. **The one that was impossible before.** Export a baked script for a function with a
   `TArray<...>&` OUT param — `GetAllActorsOfClass` or `GetOverlappingActors` is the easy one.
   Before this it failed with *"Unknown param type 'tarray'"* and never called the game at all;
   it must now invoke, with the array param left empty.
3. **An FText param is refused, clearly.** A function taking an `FText` (a UI/dialogue setter) must
   fail with a message naming `ftext` and saying an FText cannot be built from CE Lua — **not** a
   crash. This one is deliberately still a refusal.
4. **A negative return reads as negative.** Verify Return Value mode on a function returning a
   negative int32 (or invoke one you know returns -1). It must print `-1`, not `4294967295`.
5. **A timeout says something true.** Hard to stage deliberately — pause the game hard (a loading
   screen, or break in a debugger) and invoke. The message must not quote an error from an earlier
   command, and the NEXT invoke must refuse with *"the DLL is STILL holding the mailbox"* rather
   than firing. Once the game recovers, a further invoke must work again (the guard clears itself).

> ### ✅ 5-of-5 CLOSED — steps 1-4 `[ELLIOT-CE-2026-08-18]`, step 5 `[LUSHFOIL-CE-2026-08-18]`
>
> Elliot (`Elliot-Win64-Shipping.exe`, PID 3528), dxgi proxy, **DLL build 1.0.0.3262**, CE
> **7.7.0.10568** attached, game in a loaded save. UI reported `Connected — UE504 (355717 objects)`.
> Helper delivered as a CE table file (`Table -> Add File...` -> `scripts/ue5_invoke_helper.lua`);
> CE confirmed it as `TLuafile, len=33432`, **byte-identical to the repo file** (`stat` = 33432).
>
> ⚠ **Two setup facts that will cost the next session an hour if not carried forward:**
> * **`scripts/UE5CEDumper.CT` has NO `<Files>` section** (`grep -c '<Files>'` = 0 in both
>   `scripts/` and `dist/`). Without adding the helper by hand, every row below instead hits the
>   loader refusal and nothing under test ever runs.
> * **The mailbox symbol is NOT `UE5Dumper.g_invokeMailbox` under a proxy.** Elliot loads the DLL as
>   `dxgi.dll`, so that qualified name resolves to **nil**. The generated script is already correct —
>   it tries the **bare** `g_invokeMailbox` first (`BakedScriptGenerator.cs:193-194`), which resolved
>   to `0x7FFEDD72A5D0`. A probe that only tries the qualified name reports a false failure.
>
> | step | verdict | evidence |
> |---|---|---|
> | 1 | ✅ **PASS** | `KismetMathLibrary::Add_IntInt` A=3 B=4 via `AA(B)` -> `Copy AA Script` -> enable. `[Invoke] After : 03 00 00 00 04 00 00 00 07 00 00 00` / `[Invoke] OK: ... -> ReturnValue (int32@8) = 7`. DLL log: `INVOKE -> static-native fast path`, `INVOKE result=0` |
> | 2 | ✅ **PASS on its own assertion, with a stated limit** | `Actor::GetOverlappingComponents` — dialog shows `OverlappingComponents [Array, 16B, off=0, out]`, left empty. The invoke **reached the DLL** (`Mailbox: received cmd=4`, `INVOKE_BY_NAME starting...`), i.e. `writeParams` **accepted `tarray` and wrote nothing** instead of aborting the whole invoke with *"Unknown param type 'tarray'"* — which is exactly what AA16 fixed. ⚠ **The call itself did not execute**: `FIND_INSTANCE only CDO found for 'Actor'` -> `error=-3 'not found (0 functions walked)'`. Retried on `PrimitiveComponent`, which resolved a **real** instance (`0x1C1715F650`, no CDO warning) and still walked 0 functions. **So no TArray-OUT invoke has yet RUN to `result=0`** — the gap is target resolution, not AA16 |
> | 3 | ✅ **PASS** | `TextBlock::SetText` (`InText [FText, 16B, off=0]`). Export succeeded; the refusal fires at enable, verbatim: `[string "--[[..."]:307: [ue5_invoke] param 'InText' is an ftext -- an FText cannot be built from CE Lua (it holds a shared reference the engine allocates), and passing a zeroed one crashes the game. Invoke a wrapper that takes an FString instead.` Names `ftext` ✅, **not a crash** ✅ (game alive, PID 3528). Third witness: `grep -c SetText pipe-0.log` = **0** — it never reached the DLL, so the refusal really is client-side, before the `CMD` write |
> | 4 | ✅ **PASS** | `Subtract_IntInt(3,4)`: `[Invoke] After : 03 00 00 00 04 00 00 00 FF FF FF FF` / `-> ReturnValue (int32@8) = -1`. The **raw return bytes are `FFFFFFFF`** and the decode printed `-1`, not `4294967295` — AA20 witnessed on the bytes, not on the decoder's own word |
> | 5 | ✅ **PASS on all three assertions `[LUSHFOIL-CE-2026-08-18]`** | Retried on **Lushfoil** after Elliot's PE hook refused to install — the maintainer confirms Elliot's hook is **intermittent by title, "sometimes yes, sometimes no"**, so switching host is the correct response, not retrying. Lushfoil gave `hook_active: true`, `hook_fire_count` climbing. Vehicle: `CharacterMovementComponent::GetMaxJumpHeightWithJumpTime`, **non-static** (`flags=0x54020402`, and the DLL log shows **no** `static-native fast path` line), so it really queues on the game thread. **Baseline first**: `ReturnValue (float@0) = 89.99999237`. Then game thread frozen -> **(a)** `Mailbox timeout after 10000ms -- the DLL took the command but did not finish it (status=255, no message from the DLL)` — `status=255` is the **0xFF** branch (the one a whole-process suspend cannot reach) and *no message from the DLL* is AA18 holding **even though the immediately preceding command had succeeded**; **(b)** immediate retry -> `[ue5_invoke] the previous invoke timed out and the DLL is STILL holding the mailbox -- sending now would overwrite the class/function/params of a call that is mid-flight...` (AA19), returned at once rather than after another 10 s; **(c)** once the DLL reported done, the next fire was **allowed through** (timeout message again, not the refusal) — i.e. the guard cleared itself |
>
> **State left as found:** invoke timeout restored (`{"timeout_ms":0}` -> `invoke_timeout_ms: 5000`,
> `persisted: false`), no thread left suspended, no cache record modified, game/CE/UI all killed.
>
> ### ⚠ Two traps this retry paid for — both would silently invalidate a re-run
>
> 1. **The DLL's own invoke timeout must EXCEED the Lua's, or assertion (b) is untestable.** First
>    attempt used 30000 ms: the DLL released the mailbox at T+30 s and the retry click landed at
>    T+33 s, so the guard correctly stayed silent and the run *looked* like an AA19 failure. It was a
>    **mis-timed test, not a defect** — the DLL log settles it (`INVOKE_BY_NAME complete, result=-5`
>    at 15:39:52, next `received cmd=4` at 15:39:55). Raising it to **120000 ms** made the window
>    ~110 s and the guard fired first try. ⚠ A GUI round trip is ~5-10 s, so the window must be tens
>    of seconds, not the 20 s that 30000 leaves.
> 2. **`tools/verify/suspend.py` matches on a SUBSTRING and acts on the FIRST match.** Steam titles
>    have a launcher shim with the same stem (`LushfoilSim.exe` beside
>    `LushfoilSim-Win64-Shipping.exe`; likewise `Elliot.exe`), and the shim sorts first, so
>    `suspend-tid LushfoilSim <tid>` froze a **1-thread shim** while ProcessEvent kept firing.
>    **Always pass the full image stem.** The tell is the two-detector check doing its job:
>    `hook_fire_count` kept climbing under suspension.
>
> ⚠ **Also measured: creation order is NOT a reliable game-thread oracle here.** On Lushfoil **four**
> separate threads each took ProcessEvent to 0 when suspended (the frame pipeline stalls if any of
> them halts), and the earliest-created thread was not among the highest-CPU ones. Pick by EFFECT.
> ⛔ **And the game did not recover**: after `resume-tid` (suspend count 1 -> 0, verified)
> `hook_fire_count` stayed frozen at 2,070,966 for 5+ minutes while the process still reported
> `Responding: True`. That is the lock hazard the rig's own docstring warns about, now observed —
> **assume a suspended game thread is a one-shot and plan to restart the title afterwards.**

### ✅ 5-of-5 CLOSED (step 4 on 2026-08-18) — AA4–AA7: ue5_dissect.lua in a real Cheat Engine

*Needs CE + a game, and **step 2 needs no DLL at all** — it is the fastest check here. See dev-log
build 3037. The Lua rig (`lua scripts/tests/dissect_test.lua`, 40 checks) covers the logic against
stubs; what it cannot cover is CE's real dissect machinery.*

> **All five steps ✅ PASS.** Steps 1, 2, 3, 5 in two 2026-08-16 sessions, deliberately split;
> **step 4 closed 2026-08-18 on DumperTest** (`[DUMPERTEST-CE-2026-08-18]`, see it below):
> `[CE-NOTEPAD-2026-08-16]` = CE 7.7.0.10568 attached to `Notepad.exe` with **no DLL at all**
> (steps 2, 3); `[ELLIOT-CE-2026-08-16]` = the same CE against **Elliot** with the DLL injected
> (steps 1, 5). **The batch paid for itself**: step 1 failed on first run and turned out to be a
> real, shipped defect (**AU1**, fixed in build 3157) rather than a bad test.

1. **✅ PASS `[ELLIOT-CE-2026-08-16]` — but it FAILED FIRST, and that failure was a real defect.**
   CE → inject the DLL via `UE5CEDumper.CT` → Lua Engine →
   `local d = dofile("ue5_dissect.lua"); d.createFromPath("/Script/Engine.Actor")`. A structure
   appears in the Structure Dissect list with named fields at plausible offsets. **This is the
   regression half — `callDLL` now raises where it used to return nil.**
   **First run (build 3156) printed `[UE5Dissect WARN] Object not found: /Script/Engine.Actor`** and
   created nothing. The exports resolved fine — the DLL genuinely could not find the object. Root
   cause and fix: **AU1** below / dev-log build 3157; `UE5_FindObject` handed its `fullPath`
   argument to a bare-FName matcher, so no path ever resolved.
   **After the fix, same CE session, same command:** `STEP1 ok=true`, `structs before=1 after=2`,
   `name=Actor fields=129`. The before/after sits in one Lua Engine window.
5. **✅ PASS `[ELLIOT-CE-2026-08-16]` — No gap rows.** Walking the created `Actor` structure's 129
   elements: **`unnamed=0`, `unnamedPointer=0`**, and the header reads
   `0:VTable | 8:ObjectFlags | 12:ObjectIndex | 16:Class | 24:FNameIndex | 32:Outer` — the expected
   UE5 `UObject` layout, which also satisfies step 1's "named fields at plausible offsets". This is
   `addFieldsToStruct`'s own output (the DLL was present), so unlike the earlier no-DLL session it
   really does exercise the builder `fillGaps` was deleted from.
2. **✅ PASS `[CE-NOTEPAD-2026-08-16]` — ⚠ The one that needs NO DLL, and the one AA4 is about.**
   In a fresh CE with the DLL *not* injected: `local d = dofile("ue5_dissect.lua");
   d.enableAutoCallback()`, then open "Dissect data/structure" on **any ordinary address** (a plain
   allocation, not a UObject). CE must dissect it **normally**. Before this fix the callback raised,
   CE re-raised it as a Pascal exception, and its own `autoGuessStruct` never ran — so Structure
   Dissect was broken for every address until the user found `disableAutoCallback()`. Expect at most
   ONE `[UE5Dissect WARN] auto-dissect … failed` line, not one per node.
   **Conditions, because a number without them is not a measurement:** CE **7.7.0.10568** 64-bit
   attached to `Notepad.exe`, `UE5Dumper.dll` never injected, repo copy of `ue5_dissect.lua`
   (2026-08-16, 27,322 B). Target address was a **`allocateMemory(4096)` block in Notepad**
   (0x17A41C20000) pre-filled with `writeInteger(a+i*4, i*7+1)` so the readback is self-witnessing.
   **Result:** Structures → Define new structure → CE's own name/Guess-Field-Types dialog appeared,
   OK produced a fully populated `unnamed structure 1` — `0000/0004/0008/000C…` at a 4-byte stride
   reading back **1, 8, 15, 22, 29, 36, 43, 50, 57, 64, 71, 78, 85, 92, 99, 106, 113, 120, 127, 134,
   141, 148, 155, 162**, i.e. exactly the written pattern. **The whole operation emitted ONE
   `[UE5Dissect WARN] auto-dissect name lookup failed … (reported once; run
   dissect.disableAutoCallback() to unregister)` block** naming `UE5_GetObjectClass`, across 24+
   rows — so the per-node flood is gone and CE's own machinery ran to completion.
   *The warn line is also the proof the callback was REGISTERED and DID fire; without it, "CE
   dissected normally" would have been indistinguishable from not running the script at all.*
3. **✅ PASS `[CE-NOTEPAD-2026-08-16]`, and this batch named the WRONG export.** With the DLL not
   injected, `pcall(d.createFromPath, "/Script/Engine.Actor")` returned
   `false, …ue5_dissect.lua:80: [UE5Dissect] DLL function not found: UE5_FindObject` — the fix's
   whole point (a message naming the export, *not* `attempt to compare nil with number`).
   The expected name here used to read `UE5_WalkClassBegin`; that was never reachable first —
   [`ue5_dissect.lua:446`](../scripts/ue5_dissect.lua) resolves the path with **`UE5_FindObject`**
   before it ever walks a class, so `UE5_FindObject` is the correct expectation. Corrected in place.
   *Bonus, and it settles an open doubt:* the guard that fired is the `getAddress(name) == nil or 0`
   test at `:80`, which only exists because CE's `errorOnLookupFailure` really does default **FALSE**
   despite `celua.txt` claiming TRUE — see [CE-Bugs-Minesweeper.md](CE-Bugs-Minesweeper.md) §6. This
   is the first live confirmation of that; the guard is not dead code.
4. **✅ PASS `[DUMPERTEST-CE-2026-08-18]` — a mid-walk failure leaves nothing behind.** Staged
   exactly as written, on DumperTest Development (dist 3262) with the DLL injected.
   **Baseline:** CE's structure list at **0** (the table reload had cleared it), then
   `d.createFromPath('/Script/DumperTest.DumperTestActor')` →
   `[UE5Dissect] Struct created: DumperTestActor (193 elements, 1760 bytes)`, list at **1**.
   **Then `Stop-Process DumperTest -Force`, confirmed dead, and re-ran the same call.** It failed
   loudly — `Error:Failure to allocate memory` / `Script Error` (CE's own message; no invented
   wording) — and the structure list afterwards read:
   ```
   structure count now = 1
     [0] name=DumperTestActor elements=193
   ```
   **Still 1, still the intact 193-element structure from step 1: no half-built entry, no empty
   entry, and the good one was not damaged** — which is the whole of what this step asks.
   ⚠ **Precise about what was NOT shown.** The raise escaped a `pcall` wrapped around
   `createFromPath`, so it fired *before* that function — at the `dofile` re-load, where the script
   resolves exports against a process that no longer exists. So the *inside-`createFromClass`*
   unwind path is still unwitnessed; what is proven is the row's actual claim, that the attempt
   fails and leaves no debris.

### ✅ ALL 5 CLOSED 2026-08-18 — AB3/AB5: the vector scan on a UE5 (LWC) game

*Needs a **UE5** game — this is the one check a UE4 title structurally cannot make. See dev-log
build 3035.* Until then the DLL's LWC vector scan is **shipped but unproven on a real target**.

> **🡒 A suitable target was live and the scan type was wrong `[DSA-2026-08-16]`.** DragonSword
> Awakening is **UE5.4**, i.e. exactly the LWC population this batch needs, and the session ran both
> a `begin_value_scan` and a `begin_group_scan` — but **both used `"data_type":"NumericNoByte"`**, so
> the vector decode path never executed. Nothing here is settled. Next time that title is up,
> **one `FVector` Exact scan closes steps 1–3.** (Its `Rotator` / `Vector_NetQuantize100` struct
> fields already render correctly in the walker — `"value":"{X=0, Y=0, Z=0}"` — but that is the
> *walker's* struct decoder, a different code path from the scan predicate this batch is about.)

1. **A UE5 world-position scan returns real hits.** Value Search → data type **FVector** → Exact →
   type the player's current X,Y,Z (read them off the Teleport panel's POV/marker readout, which is
   already width-aware) → First Scan. Before this fix a UE5 game returned **zero** plausible hits
   because every 24-byte `Vector` was compared as three floats; it must now return the player pawn's
   location among the candidates. **This is the whole point of the fix — if it still returns nothing,
   stop and report, do not "narrow the search".**
2. **The value column reads back as the coordinates you typed**, not a huge/tiny number. That proves
   the *display* decoder agrees with the *compare* decoder about the width (they were one hardcoded
   12 before, and are now one canonical 3-double form).
3. **Next Scan (refine) survives.** Move the character, then Changed → the surviving candidates must
   include the pawn location. This is the half that needs `FieldDescriptor::vectorWidth`: refine has
   no access to the class index, so a session that lost the width would drop every candidate here.
4. **A UE4 game still works.** Same scan on any UE4 title (12-byte `Vector`) — this is the
   regression half; the width gate must not have narrowed what UE4 accepts.
5. **A `Vector3f` field on a UE5 game** (float-backed, 12B, in the same process as 24B `Vector`
   fields) also matches. That is the case a version-keyed fix would have got wrong, and the reason
   the width is read per field rather than per game.

> ### ✅ STEP 5 PASSES `[ELLIOT-AB3-2026-08-18]` — one scan matched BOTH widths in one process
>
> **Elliot**, `Elliot-Win64-Shipping.exe`, **UE 504**, DLL **1.0.0.3262**, dxgi proxy, 84,388 objects
> scanned per pass. A second UE5 title for this batch (steps 1–3 were DSA), which is itself worth
> having.
>
> **Target found over the pipe, not by clicking.** `search_properties` returns a `struct_type` per
> field, so sweeping ~18 vector-ish name queries and grouping by `struct_type` gives the width census
> directly: this process holds `Vector`=24 B, `Vector2D`=16 B, `Vector4`=32 B, **`Vector2f`=8 B** and
> **`Vector3f`=12 B**. `ChaosClothConfig`'s CDO is the ideal specimen — it carries **both widths in
> the SAME object**: `Gravity` / `LinearVelocityScale` as 24 B `Vector`, `MaxLinearVelocity` /
> `MaxLinearAcceleration` as 12 B `Vector3f`. Exact bytes from `walk_instance`:
> `MaxLinearVelocity.hex = 00007A44 00007A44 00007A44` = three **floats** of 1000.0 at
> `0x7FF4DE864B44`.
>
> **One `FVector` Exact scan for `1000,1000,1000` returned 3 hits spanning both widths:**
>
> | address | class::field | struct | width |
> |---|---|---|---|
> | `0x7FF4DE6877F0` | `BoxComponent::RelativeScale3D` | `Vector` | **24 B** |
> | `0x7FF4DE7345A0` | `NiagaraDataChannel_Islands::InitialExtents` | `Vector` | **24 B** |
> | `0x7FF4DE864B44` | `ChaosClothConfig::MaxLinearVelocity` | **`Vector3f`** | **12 B** |
>
> ⇒ **Exactly the case a version-keyed fix gets wrong**: a single UE5 process where one predicate has
> to accept 24 B and 12 B fields in the same pass. The width is demonstrably per field, not per game.
> Widths were confirmed from the class layout (`walk_instance` `size=`), not inferred from the hit.
>
> **Two controls, because a scan that matched everything would also "pass":**
> * A value present ONLY in a 12 B field — `60000,60000,60000` → **exactly 1** hit,
>   `ChaosClothConfig::MaxLinearAcceleration` (`Vector3f`, `0x7FF4DE864B54`). A 24 B-only compare
>   finds nothing here.
> * An implausible triple `1234.5,6789.25,-4321.75` → **0** hits over the same 84,388 objects.
>
> ⚠ **Scope, stated plainly:** driven over the pipe (`begin_value_scan`), which exercises the same
> `Radar` predicate the UI calls — that predicate *is* what step 5 is about. The **display** half was
> already covered by step 2 on DSA; this run does not re-cover the UI rendering.
> ⚠ `game_only` must be **false** — every specimen above is an engine class, and the default `true`
> hides all of them, which would read as "no Vector3f in this game".
> ⚠ The reply puts hits under **`candidates`**, not `results`; reading the wrong key reports a clean
> pass as a failure.

> ### ✅ STEPS 1-3 PASS `[DSA-2026-08-18]` — the LWC vector scan works on a real UE5.4 target
>
> **DragonSword Awakening**, `DSClient-Win64-Shipping.exe` PID 49612, **UE 504**, DLL build
> **1.0.0.3262**, CE-injected (no proxy deployed for this title), 275,612 objects, map
> `World_01_Main_WP`, pawn `0x18F21068040`.
>
> | step | verdict | evidence |
> |---|---|---|
> | 1 | ✅ **PASS** | Value Search → **FVector** / **Exact** / Deep on / Native-C **off** → `41342,110645,1641` → `First Scan: 3 candidates in 766 ms (scanned 257500 objects, 722 classes with matching fields)`. **The player pawn is among them**: `DsPC_Lute_V2_C.ActiveLocation` at `0x18F21068F10` = pawn `0x18F21068040` **+ 0xED0**, the row's own reported offset. The other two are `CapsuleComponent.RelativeLocation` (`CollisionCylinder`) and `DsPCMovementComponent.LastUpdateLocation` (`CharMoveComp`) — both plausible, neither noise |
> | 2 | ✅ **PASS, witnessed on the RAW BYTES not on the UI cell** | The Value column truncates to `41342, 11064…` and would not widen, so the check was made independently: `py tools/verify/read_mem.py DSClient-Win64-Shipping 0x18F21068F10 24` → `00 00 00 00 C0 2F E4 40  00 00 00 00 50 03 FB 40  00 00 E0 06 5E A2 99 40` → as **3 doubles** = `(41342.0, 110645.0, 1640.5918231010437)`, matching the Teleport pose exactly. **The same 24 bytes read as 3 floats give `(0.0, 7.13, 0.0)`** — i.e. the pre-fix decode really is garbage on this target, so the fix is doing visible work rather than being a no-op here |
> | 3 | ✅ **PASS** | Moved the character (verified by re-reading the same address: `(42260.337…, 110719.078…, 1773.512…)`), then **Changed** → `Next Scan (Changed): 3 surviving candidates in 0 ms` — **all three survived, pawn included**, and the Value column re-rendered as `42260.3, 110…`. This is the half that needs `FieldDescriptor::vectorWidth`: a session that lost the width would have dropped every candidate |
> | 4 | ✅ **PASS `[DQ7R-2026-08-18]`** | **DQ7R**, UE **427**, 199,196 objects, `version.dll` proxy (build 3262) — the UE4 half. Pose `29.115 / -103.393 / 133.344` (pawn `0x20544680010`), scanned as **`29,-103,133`** → `First Scan: 4 candidates in 673 ms (scanned 154900 objects, 580 classes with matching fields)`: `SceneComponent.RelativeLocation` (`RootSceneComponent`), `DOLLPlayerMovementComponent.LastUpdated…`, `DOLLPlayableCharacterCapsuleComponent…`, `AtomComponent.RelativeLocation`. **Independently witnessed on the bytes, mirroring the UE5 half**: `read_mem … 0x20463ED831C 12` → `30 EC E8 41 3C C9 CE C2 F6 57 05 43` = **3 floats** `(29.115325927734375, -103.39303588867188, 133.34359741210938)`. So **24B doubles on DSA and 12B floats on DQ7R both match under the same predicate** — the width really is read per field, and the gate did not narrow what UE4 accepts |
> | 5 | ⬜ not run | `Vector3f` (12B) beside 24B `Vector` in the same process — no known field to aim at yet |
>
> ### ⛔ THE TRAP, AND THIS ROW'S OWN INSTRUCTIONS WALK INTO IT
>
> **The first scan returned `0 candidates` — and it was the INPUT, not the scanner.** This step says
> to "read them off the Teleport panel's POV/marker readout", which prints **three decimals**
> (`Z: 1640.592`). Pasting that verbatim is a **guaranteed zero-hit**, because
> `Radar::CompareFloatScalar` (`Radar.cpp`) branches on whether the TYPED target is a whole number:
>
> ```cpp
> case ScanType::Exact:  return IsWhole(a) ? (rc == a) : (cur == a);
> ```
>
> A whole target compares the **rounded** current value (tolerant); a fractional target compares
> **bit-exact doubles**. The true Z is `1640.5918231010437`, so `1640.592` can never match — and the
> raw bytes above are what proves that rather than a guess. Re-running with `41342,110645,1641`
> (all three axes whole) hit on the first try.
>
> ▶ **Round every axis to a whole number before an Exact vector scan**, or the row reports a defect
> that is not there. ⚠ This is exactly the failure the step warns about in the opposite direction —
> *"if it still returns nothing, stop and report, do not narrow the search"* — so the instruction as
> written would have produced a **false FAIL**. Worth fixing at source: either the Teleport panel
> should offer a whole-number copy, or the step should say to round.

### ✅ 5-of-5 CLOSED 2026-08-18 — install the plugin into a REAL Cheat Engine (audit #5 AB1, build 2913)

We were crashing CE by leaving a 1 ms-poll thread running in an image CE unloads. The fix stops
creating threads in a CE host and pins the module elsewhere. **The unload paths were read out of CE's
published 7.5 source; the shipping binary is 7.7.0.10568, and nobody has run this.**

This is the one verification in the register that needs **no game at all**.

1. Copy `dist/UE5Dumper.dll` into Cheat Engine's `plugins\` folder (or anywhere), open CE →
   **Settings → Plugins → Add** and select it. **Before the fix this is the crash.** Success = the
   dialog accepts it and CE is still running.
2. Tick the plugin to enable it, then **close CE normally**. Success = clean exit — and re-open CE and
   confirm your settings survived, since the unload runs *before* CE writes them.
3. Check `%LOCALAPPDATA%\UE5CEDumper\Logs` for the new line
   *"host is Cheat Engine — NOT starting the mailbox poller or the auto-start thread"*. Its absence
   means the guard did not fire and the rest of the check proves nothing.
4. **Then prove the fix did not break the feature**: with the plugin enabled, use its
   *"UE5CEDumper: Inject & Connect"* menu item against a running game and confirm the DLL still
   injects, the pipe opens, and the CE Lua mailbox works. The poller is *supposed* to run in the
   game — only CE's own process is refused.

   **⚠ This step also verifies AB2 (build 2932), so do it deliberately.** `UE5_AutoStart` now spawns
   and returns instead of running the scan on CE's remote thread, which CE frees after a hard 10 s
   (`CEFuncProc.pas:1346-1360`) or, with Settings' **`cbInjectDLLWithAPC`** ticked, after 1 s
   (`:1332-1343`) — the `ret` onto that freed page crashed the **game**. Measured async
   (`py tools/probe_autostart_async.py` → 2.3 ms, vs 3486 ms with the spawn reverted), but never run
   against a real CE + game. Check:
   - The menu item returns **immediately** and the dialog says the scan started *in the background*;
     CE's own window should not freeze for the scan any more.
   - The game does **not** crash a few seconds later — that was the AB2 symptom.
   - **Tick `cbInjectDLLWithAPC` in CE's Settings and repeat.** That is the near-certain-crash path
     before the fix and the strongest single check here.
   - The dialog now reports what it **observed** (is our module mapped?) rather than CE's `InjectDLL`
     BOOL, which is inverted for the common failures. A "success" dialog must mean the pipe really
     comes up; an "injection failed" dialog must mean the module really is absent.
5. Worth one negative case: a game whose folder is named e.g. `...\Cheat Engine 7.7\Game.exe` must
   still get its poller. Only the executable leaf is tested, and there is a unit test for it.

> ### ✅ STEPS 1-3 PASS `[CE-PLUGIN-2026-08-18]` — the crash is gone, on the SHIPPING 7.7 binary
>
> **Cheat Engine 7.7.0.10568**, DLL build **1.0.0.3262**, **no game involved** (as the row promises).
> The DLL was staged at `out\ce-plugin-test\UE5Dumper.dll` rather than pointed at `dist\` on purpose:
> once CE loads it the file is locked, and a later `-Mode Publish` would fail to overwrite.
>
> | step | verdict | evidence |
> |---|---|---|
> | 1 | ✅ **PASS** | Settings → Plugins → **Add new** → selected the DLL. The dialog accepted it and the list gained `UE5Dumper.dll:UE5CEDumper`. **CE's PID was 34984 before the Add and 34984 after** — same process, so it neither crashed nor silently restarted. This is the exact operation the finding says used to take CE down |
> | 2 | ✅ **PASS, all three halves** | Ticked it → OK: CE's menu bar gained a **`Plugins`** menu, i.e. the CE entry points ran. **Closed CE normally → clean exit**: process gone, and `Get-WinEvent` over the Application log for the preceding 10 minutes returned **no** `cheatengine` entry. **Settings survived**: `HKCU\Software\Cheat Engine\Plugins64` gained `00000002 A = …\UE5Dumper.dll`, `00000002 B = 1` — written *at exit*, which is the point, since the unload runs before CE writes them. **Re-opened** (new PID 36608) → the plugin auto-loaded enabled and logged `CEPlugin: InitializePlugin pluginid=2 menuItemId=1 ef_size=1272` |
> | 3 | ✅ **PASS** | A `Logs\cheatengine-x86_64\` folder appeared, and `init-0.log` carries the guard verbatim: `[WARN] [INIT] DllMain: host is Cheat Engine — NOT starting the mailbox poller or the auto-start thread. CE FreeLibrary's plugin DLLs (on Settings→Add and on exit), and a thread left running in an unmapped image takes CE down with it. …` It fired on **both** loads (the Add, and again on the re-open) |
> | 4 | ✅ **PASS on the injection half; the APC half is UNREACHABLE — see below** | Elliot, **proxy temporarily moved aside** so the injection is genuine (see the trap below). `Plugins → UE5CEDumper: Inject & Connect` → dialog inside 3 s: *"DLL injected — GObjects/GNames scan started in the background."* **AB2's async is measured, not assumed**: `Injecting into PID=12780` 17:05:54.151 → `InjectDLL returned` .248 (**97 ms**), while the scan only finished at **17:05:58.596** — 4.4 s later, i.e. well past the 1 s APC / 10 s normal window CE frees the stub in. **Game did not crash**: alive and `Responding: True` **6 minutes** later, no Application event-log entry. **Pipe opened** (`get_pointers` returned live pointers). **Mailbox poller started IN THE GAME** (`Mailbox: polling thread started (poll=1ms)`) while CE's own log carries the refusal — the exact contrast AB1 is about. **CE Lua reaches it**: `g_invokeMailbox = 7FFE944CC610` |
> | 5 | ✅ **PASS, with a discriminating control** | Two hosts, **same DLL, same injector, same minute**, differing only in the executable leaf. **A** = `out\Cheat Engine 7.7\Game.exe` → `Mailbox: polling thread started (poll=1ms)` and **no** guard line, i.e. a folder literally named *Cheat Engine 7.7* does **not** cost the poller. **B (control)** = `out\plainhost\cheatengine-x86_64.exe` → the guard fired (`DllMain: host is Cheat Engine — NOT starting the mailbox poller…`) and **zero** poller starts. Both hosts were copies of `cmd.exe`, so B also shows the match is on the **name**, not on really being CE |
>
> ### ⭐ The BOOL-vs-observation fix, demonstrated live — this is the strongest single result here
>
> CE's `InjectDLL` returned **FALSE**, and the DLL was **mapped and working anyway**:
> ```
> CEPlugin: InjectDLL returned FALSE
> CEPlugin: post-inject module check: D:\…\out\ce-plugin-test\UE5Dumper.dll (ok=0)
> ```
> ⚠ **Read that second line carefully — it is easy to get backwards.** `ok=` is CE's BOOL (0 = FALSE);
> the `%s` slot prints the **found path** when present and the literal `NOT PRESENT` when not. So this
> says *module IS there, CE said it failed*. The dialog reported success because it trusts its own
> module walk, which is precisely the inversion step 4 asks about — and the pipe really did come up,
> so the "success" dialog was honest.
>
> ### ⛔ THE APC HALF CANNOT BE RUN ON A PUBLIC CHEAT ENGINE — it needs a private build
>
> Step 4 calls ticking `cbInjectDLLWithAPC` *"the strongest single check here"*. It is **not reachable
> on the shipping binary**, and this is not a UI-hunting failure — two independent signals:
> * **Source** (`D:\Github\cheat-engine`, tag 7.5): `formsettingsunit.pas` guards
>   `cbInjectDLLWithAPC.visible := true` with `{$ifdef privatebuild}`, and `MainUnit2.pas` reads
>   `useapctoinjectdll` from the registry **inside the same ifdef** — its `{$else}` branch hardcodes
>   `useapctoinjectdll := false`. So on a public build the checkbox is hidden **and** the flag is
>   forced off; **setting the registry value achieves nothing.**
> * **Observation**: the checkbox is absent from 7.7.0.10568's Settings (General Settings and Extra
>   both checked).
>
> ▶ **Rewrite the step**: the APC path needs a `privatebuild` Cheat Engine. Everything else in step 4
> is done. (⚠ Source is 7.5 while the binary is 7.7 — but the two signals agree, and the doc's own
> rule is that the public source lags the release, not that it invents ifdefs.)
>
> ### ⚠ Two traps for whoever re-runs this
>
> 1. **A deployed proxy makes step 4 vacuous.** `Methode.cpp` checks `IsAlreadyLoadedInTarget` *before*
>    injecting and bails with *"UE5CEDumper is already loaded in this process as '…'"* — its comment
>    even names the proxy case. Elliot ships `dxgi.dll`, so the menu would never reach `InjectDLL`.
>    It was moved to `dxgi.dll.ab1-bak` for the run and **restored afterwards**.
> 2. **CE attached BEFORE the injection has a stale symbol list.** `getAddressSafe('g_invokeMailbox')`
>    returned **nil** until `reinitializeSymbolhandler()`, after which it resolved. With a proxy the
>    DLL is present before CE attaches, which is why the earlier invoke rows resolved it immediately.
>
> ### ⭐ Why step 5 needed a control, and the staging that made it cheap
>
> `Grimoire.h:441` `HostAllowsBackgroundThreads` takes the **full** host path and `IsCheatEngineExeName`
> matches a **prefix of the LEAF** — so "path contains Cheat Engine" and "exe is named cheatengine*"
> are different questions, and only a **pair** of hosts separates them. Host A alone could pass simply
> because the guard never fires for anything; host B is what shows the check can fail.
>
> ⚠ **Staging note for a re-run: `notepad.exe` and `charmap.exe` from System32 DO NOT WORK as hosts.**
> Copied elsewhere they exit immediately (Notepad is a Store stub). `cmd.exe` copied to the target name
> and launched as `Start-Process … -ArgumentList '/k','timeout /t 900'` stays alive and is enough — the
> guard only reads the host path, so the host does not need to be a UE game at all. The UE scan then
> fails in that host, which is expected and irrelevant to this step.
>
> **State left exactly as found**, verified against a baseline captured before the run: the plugin was
> deleted, and `Plugins64` is byte-identical to `out\ce-plugin-test\plugins64-before.txt`
> (`AOBMaker_CEPlugin.dll` = 1, `CE-Handwire.dll` = 0, nothing else). CE exited cleanly a **second**
> time on the way out, with the plugin being removed — an incidental repeat of step 2's unload path.

### ✅ CLOSED IN FULL 2026-08-23 — keep a freeze running across deaths/respawns (audit #5 AA2/AA3, build 2926)

> **All five steps done.** 2 (2026-08-21) · 3 (2026-08-20) · 4 `[AA2-STEP4-CHURN-2026-08-23]` ·
> **1 and 5 `[AA2-CONTRACT-AA3-STOP-2026-08-23]`**. The churn and the old-DLL staging came from
> the DumperTest spawner and `out/proxy-backups/`, not from gameplay.

The freeze tick used to write to cached pointers guarded only by "is qword 0 non-zero", which a
recycled or pooled block passes — so between two rescans it could write into an object of a
different class. It now re-reads `ClassPrivate` before every write and refuses a foreign class, using
a `(UClass*, offset)` witness the DLL publishes on `CMD_LIST_INSTANCES` (**mailbox contract 1 → 2**).

**The behaviour is covered by an executable harness** (`lua scripts/tests/freeze_helper_test.lua`,
23 checks, negative-controlled one break at a time), so what is unproven here is the *live* half:
that a real game's `CMD_LIST_INSTANCES` fills the witness and that the guard does not reject valid
instances.

> ### VERIFIED 2026-08-20 - step 3 PASSES headlessly, and the row's assertion needed narrowing
>
> `tools/verify/aa2_class_witness.py` drives `CMD_LIST_INSTANCES` **from Python**, by writing the
> mailbox struct directly (`mailbox_poke.py` already had the driver). No Cheat Engine involved, so
> this is category A, not B. Against DumperTest, dist 3263:
>
> ```
> EXACT   DumperTestActor  returned 1/1    classWitness=0x243B98C2E00  classOffset=0x10
> DERIVED Actor            returned 58/58  classWitness=0x0            29 distinct classes
> EXACT   Actor            returned 1/1    classWitness=0x243B622AE00
> ```
>
> **The witness is checked against the objects, not taken from the DLL.** For every returned
> object the rig reads `*(obj + classOffset)` with `ReadProcessMemory` and compares:
> **0 mismatches** on the exact page, and **0 mismatches + 0 zero-witnesses** across all 58
> derived entries spanning **29 different concrete classes**. A wrong offset cannot make 29
> unrelated classes each read back their own correct pointer.
>
> **The zero is a CLEAR, not a leftover.** Both witness fields are poisoned with
> `0xDEADBEEFCAFEF00D` before the trigger, so a `0` read back is proof the DLL wrote it. Without
> that control, "the guard fell back" and "nobody touched the field" are the same observation.
>
> ⚠ **The step as written would emit a FALSE FAIL on a derived listing.** `Mimic.cpp` publishes
> the page-wide witness for the **exact** scope only; under contract 3's derived scope
> `instanceAddr` is deliberately **0** and the witness travels **per entry** in `paramsData`
> (16-byte entries), because one page-wide class would make the caller refuse every instance that
> is not that class -- the AA2 defect inverted. So `classWitness=0x0` is a defect on an exact
> listing and *correct* on a derived one, and **the log line alone cannot tell them apart**: the
> scope has to be read off the same line (`scope=exact` / `scope=derived`). Anyone re-running
> step 3 by grepping for a non-zero witness will report a bug that is not there.
>
> Also settled in the same run, neither of which the row asked for:
> * **`cmdFlags` is genuinely cleared by the handler** (read back `0x0`), and an immediately
>   following unflagged call returns the exact shape again -- the whole contract-1/2 backward
>   compatibility story, checked two ways rather than asserted.
> * **`cmdOutFlags` is rewritten, not accumulated** (poisoned `0xFFFFFFFF`, came back a real value).
>
> 🔗 **Corroborates `[FREEZESCOPE-2026-08-18]` at the mailbox layer**: an exact `Actor` pool holds
> **1** live object on this host while the derived sweep holds **58**. That 1-vs-58 is precisely the
> "held one incidental debug actor while the player's pawn went untouched" story the finding tells.
>
> All five steps are now closed — see the parent heading.

1. **Contract first.** With an **old** DLL injected and a freshly-injected helper, the freeze must
   refuse with *"the DLL is older than this script"*. If it runs anyway, the contract check is not
   firing and nothing below means anything.
2. With the new DLL: start a class-wide freeze on something with many live instances (enemies, pickups).
   **Success = the value actually holds.** A silently-refusing guard looks exactly like a freeze that
   does nothing — that is the main risk of this change, and it fails in that direction by design.
3. Check `init-0.log` for `LIST_INSTANCES ... classWitness=0x...`. **A zero witness means the guard
   fell back** and the fix is inert.
4. **Now cause churn**: kill/respawn the frozen actors, or cross a level-streaming boundary, with the
   freeze still enabled. Success = the freeze re-acquires within one rescan (~5 s) and nothing
   unrelated changes. Watch for any *other* object's fields changing — that is the old bug.
5. **AA3**: with the freeze running, unload/re-inject the DLL so rescans fail permanently. Expect the
   Lua console to print `... consecutive rescans failed -- freeze STOPPED writing` **once** within
   ~15 s, and no further writes.


> ### ✅ STEP 2 PASSES 2026-08-21 `[AA2-STEP2-2026-08-21]` — 145 live instances, and the value really moves
>
> The row names the exact trap: *"a silently-refusing guard looks exactly like a freeze that does
> nothing — that is the main risk of this change, and it fails in that direction by design."* So the
> only acceptable evidence is a value that **changes and then holds**, not an absence of errors.
>
> Subject chosen for scope, not convenience: **`PrimitiveComponent::bVisibleInSceneCaptureOnly`**
> (`BoolProperty` @ `0x261`). Property Search previewed it as **`false`** with **no `(CDO default)`
> marker**, i.e. live instances exist, and the Freeze dialog stated the scope itself:
> *"every live PrimitiveComponent and every subclass (81 inherit th…)"*, with its own warning that
> this holds the value on **every** live instance at once. Freeze value `true` — so a successful
> write is a visible change rather than a no-op.
>
> **"Many live instances" is measured, not assumed.** The DLL's own log for the run:
> ```
> [PIPE] Mailbox: LIST_INSTANCES class='PrimitiveComponent' scope=derived
> [PIPE] Mailbox: LIST_INSTANCES returned 64/145 (page 1/3) scope=derived classWitness=0x0
> [PIPE] Mailbox: LIST_INSTANCES returned 64/145 (page 2/3) scope=derived classWitness=0x0
> [PIPE] Mailbox: LIST_INSTANCES returned 17/145 (page 3/3) scope=derived classWitness=0x0
> ```
> **145 live instances**, enumerated in three pages — this is the class-wide case the row asks for,
> not a one-instance stand-in. (`classWitness=0x0` on a **derived** listing is *correct* and is
> already settled by step 3's block above; it is not a zero-witness failure.)
>
> | | Property Search preview of `bVisibleInSceneCaptureOnly` |
> |---|---|
> | before the freeze | **`false`** |
> | record ticked (red ✗), no error dialog | |
> | immediately after | **`true`** |
> | **+25 s later** | **`true`** — still held |
>
> ⭐ **The change IS the control.** `false → true` cannot be produced by a guard that refuses every
> write, which is precisely the failure mode this row exists to catch; and the 25-second re-read
> excludes a single opportunistic write that then stopped. The guard admits valid instances.
>
> ⚠ **Scope of this result, stated so it is not over-read.** This shows the class-wide freeze writes
> and holds across a 145-instance derived pool **on a quiescent pool**. It does **not** cover churn —
> steps 4 and 5 (kill/respawn, streaming boundaries, and AA3's permanent-rescan-failure case) are
> what test the re-read-`ClassPrivate`-before-every-write guard against *recycled* blocks, and they
> still need gameplay. The representative value read here is the panel's, one instance's; the
> 145-instance claim comes from the DLL's enumeration, not from reading 145 values.

### ✅ DONE 2026-08-18 — freeze a PACKED bitfield bool and check its 7 siblings survive (audit #5 AA1, build 2922)

> **Ran on DumperTest Development, dist 3262, CE 7.7 attached.** All five steps pass, and the
> DLL→UI half the box was waiting on is now witnessed: a real packed bool's mask reached the UI.
>
> | step | result |
> |---|---|
> | 1 | Property Search `bAlwaysRelevant` → **`Actor / BoolProperty / 0x58 / size 1`**. Row's **Freeze** button (visible only after collapsing the Object Tree — it lives in a per-row cell, not the toolbar) → dialog reads `Type: BoolProperty -> bool`, `Offset: 0x58`, value pre-filled **`true`**, hint *"Accepts: true / false / 1 / 0"*. → `Freeze script created in CE: Freeze: Actor::bAlwaysRelevant = true`. |
> | 2 | ✅ **The mask arrives.** Generated CFG: `boolMask = 0x08,  -- packed bitfield: only this bit is written`. `0x08` = bit 3, which is `bAlwaysRelevant`'s bit as Live Walker independently reports it. |
> | 3–5 | ✅ **Only the masked bit ever moves**, shown by *two* transitions rather than one baseline — the freeze had already been running, so a single before/after could not prove the neighbours predated it. Editing the CFG's `value` and re-arming gave, at `ChaosDebugDrawActor+0x58` (read with `tools/verify/read_mem.py`, not from a panel): **`0x6A` → (false) `0x62` → (true) `0x6A`**. `b1`, `b5`, `b6` are set throughout and never move; only `b3` follows the frozen value. The pre-fix whole-byte write produces `0x01`/`0x00`, and step 5's specific trap — a non-`0x01` mask leaving the target bool unset — is excluded because `b3` tracks the value in both directions. |
>
> ⚠ **Which instance it held is the incidental finding below** — not `DumperTestActor_0`.

### ✅ DONE 2026-08-18 — freeze a 1-byte enum and check its neighbours survive (audit #5 Y15, build 2904)

> **Ran on DumperTest Development, dist 3262.** Steps 1–5 pass; **step 6 not run** (needs a 4-byte
> `enum`; every EnumProperty reachable here reports size 1).
>
> **Target choice is the whole result, so it is recorded first.** The obvious candidate —
> `Actor::PhysicsReplicationMode` @`0x17C` — reads `00 00 00 00` with its three neighbours, and on
> **all-zero neighbours a 4-byte write is indistinguishable from a 1-byte one**: freezing to 3 gives
> `03 00 00 00` either way. That probe can only return "pass" (working-lessons §1.10a). Rejected it
> and swept for an enum with a **non-zero** neighbour, which is what makes the read decisive:
>
> | offset | field | baseline |
> |---|---|---|
> | `0x5E` | `Actor::UpdateOverlapsMethodDuringLevelStreaming` (EnumProperty, size 1) | `00` |
> | `0x5F` | `Actor::DefaultUpdateOverlapsMethod` (EnumProperty, size 1) | **`02`** |
> | `0x60`, `0x61` | — | `00`, `00` |
>
> | step | result |
> |---|---|
> | 1 | Baseline at `ChaosDebugDrawActor+0x5E` = **`00 02 00 00`** (`tools/verify/read_mem.py`). |
> | 2 | ✅ Dialog reads **`Type: EnumProperty -> uint8`** — not `-> int32` — with the field labelled `Freeze value (uint8):` and pre-filled **`255`**, not `9999`. |
> | 3 | ✅ Entering `9999` produces exactly **`uint8 holds 0 to 255 — 9999 would be written as 15`** and **no script is created** (the dialog stays open). Re-entered `3` → `Freeze script created in CE: Freeze: Actor::UpdateOverlapsMethodDuringLevelStreaming = 3`. |
> | 4 | ✅ **`00 02 00 00` → `03 02 00 00`.** The enum took the value and `DefaultUpdateOverlapsMethod` **kept its `02`**. The pre-fix 4-byte `writeInteger(3)` writes `03 00 00 00` and silently resets a *named, adjacent enum property* — which is the damage the finding is about, now stated as a field name rather than "the following bytes". |
> | 5 | ✅ CFG: `propOffset = 0x5E`, **`valueType = 'uint8'`**, `value = 3`, and **no `boolMask`** (correct — the mask line is bool-only, cf. AA1 above). |
> | 6 | ⬜ **NOT RUN** — no 4-byte enum in this sample; skipped per the run plan. |
>
> ⚠ Same scope caveat as AA1: the record held `ChaosDebugDrawActor`, the only non-CDO exact-`Actor`
> instance — see `[FREEZESCOPE-2026-08-18]`.

### ✅ CLOSED 2026-08-17 `[Y9-UI-2026-08-17]` — freeze a byte-wide property and try to overflow it (audit #5 Y9, build 2895)

**All five steps PASS** on DumperTest Development, dist 3262, using `U8_Max` (`ByteProperty`,
`0x63A`), `F32` (`FloatProperty`, `0x650`) and `F64` (`DoubleProperty`, `0x658`).

| step | evidence |
|---|---|
| 1 | Dialog opens headed `Type: ByteProperty -> uint8` with **`Freeze value (uint8):` pre-filled `255`** — the width is named in the label, not just enforced |
| 2 | `9999` → inline error **`uint8 holds 0 to 255 — 9999 would be written as 15`**, verbatim, and the dialog **stays open**. (9999 mod 256 = 15, so the number in the message is the truth, not a placeholder) |
| 3 | `200` → `✓ Holding DumperTestActor::U8_Max = 200 on 1 instance(s).` The ordinary path is intact |
| 4 | **This is how 1–3 were run** — see the ⚠ below |
| 5 | `1e300` on `F32` → **`Too large for a 4-byte float (max ±3.4028235E+38) — it would be written as infinity`**; the *same* value on `F64` → **accepted**, `✓ Holding DumperTestActor::F64 = 1E+300`. The narrowing check did not leak into the 8-byte path |

⚠ **A precondition this checklist does not state, and it inverts steps 1–4.** The **Freeze button** is
bound `IsEnabled="{Binding IsAobMakerAvailable}"`
([PropertySearchPanel.axaml:294](../ui/UE5DumpUI/Views/PropertySearchPanel.axaml)), and the toolbar
read `AOBMaker Offline`, so that button is greyed and **steps 1–3 cannot be run through it without
the CE plugin installed** (GROUP 5). Everything above therefore went through **step 4's** route —
row context → *Force field (hold across instances)* → *Force value…* — which opens the *same*
`Freeze property value` dialog, exactly as step 4 says. So the dialog and its arithmetic are fully
verified; what remains unexercised is only the **Lua-helper consumer** reached from the button.
Rewrite the step order accordingly: Force first, button only once AOBMaker is up.

*Incidental, both confirming Solide end to end:* the **`Forced fields:` strip** appears with
`DumperTestActor U8_Max (1 held)` and a `Clear all` that empties it; and the float pre-fill is the
generic `9999.0`, i.e. the 255 pre-fill is specific to byte-width targets rather than a blanket
change.

### ✅ VERIFIED 2026-08-21 — Y9: the width check and the pre-fill, all five steps

The freeze / force value dialog now rejects values wider than the target property instead of letting
them wrap. The arithmetic is measured against the writers' own masking in unit tests, **but nobody
has run the dialog against a real property** — and the pre-fill change is only observable in the UI.

Needs any connected game with a `ByteProperty` (Property Search → `byte`, or any `bEnabled`-style
flag stored as one).

1. **Property Search** → find a `ByteProperty` row → **Freeze**. The value box must open pre-filled
   with **`255`**, not `9999`. That is the pre-fill half of the fix and nothing else surfaces it.
2. Type `9999` and press OK. Expect the inline error
   *"uint8 holds 0 to 255 — 9999 would be written as 15"*, and the dialog must **stay open**.
3. Correct it to `200`, confirm the script generates as before — the check must not have broken the
   ordinary path.
4. Repeat step 2 via the **Force** submenu on the same row (Property Search → row context → Force →
   value…), which reuses this dialog. Same error expected; that consumer is Solide, not the Lua
   helper.
5. Worth one **float** case: on a `FloatProperty`, `1e300` should now be refused with
   *"would be written as infinity"*, while the same value on a `DoubleProperty` must still be
   accepted. If the double case is refused, the narrowing check leaked into the 8-byte path.


> ### ✅ Y9 STEPS 1, 2, 3, 5 PASS 2026-08-21 `[Y9-FREEZE-2026-08-21]` — through the **Freeze button**, the consumer that had never been exercised
>
> Y9 was closed once before **through the Force dialog**, because the Freeze button is bound to
> `IsAobMakerAvailable` and the toolbar read `AOBMaker Offline` at the time. With CE + the plugin up
> that button is live, so this run drives the consumer the earlier pass could not reach. DumperTest,
> dist 3263.
>
> | step | measured |
> |---|---|
> | 1 | `DumperTestActor::U8_Max` (`ByteProperty -> uint8`, `0x63A`) → Freeze → value box opens pre-filled **`255`**, not `9999`. **PASS** |
> | 2 | typed `9999` → inline red **`uint8 holds 0 to 255 — 9999 would be written as 15`**, word for word, and the dialog **stayed open**. **PASS** |
> | 3 | corrected to `200` → `AOBMaker: created AA script 'Freeze: DumperTestActor::U8_Max = 200'`. The ordinary path is unbroken. **PASS** |
> | 5 float | `WorldMetricsSubsystem::UpdateRateInSeconds` (`FloatProperty -> float`) + `1e300` → **`Too large for a 4-byte float (max ±3.4028235E+38) — it would be written as infinity`**, dialog stayed open. **PASS** |
> | 5 double | `DumperTestActor::F64_Ticking` (`DoubleProperty -> double`, `0x6B8`, size 8) + the **same** `1e300` → **accepted**: `created AA script 'Freeze: DumperTestActor::F64_Ticking = 1E+300'`. **PASS** — this is the half that would catch the narrowing check leaking into the 8-byte path |
>
> ⚠ **Step 1 had a confound, and it is worth recording because the obvious subject hides it.**
> `U8_Max`'s **live value is also 255**, so a pre-fill of 255 is consistent with both "derived from the
> type" (the fix) and "derived from the current value" (not the fix) — and `U8_Max` is the **only
> `ByteProperty` in the entire game** (`Type filter = ByteProperty` → 1 result), so no other subject
> can separate them.
>
> Settled two ways rather than left ambiguous:
> * **From source** — `FreezeValueDialog.SuggestedDefault` is
>   `Math.Min(SuggestedMagnitude /* 9999 */, IntegerRange(helperType).Max)`, i.e. the **type table**,
>   never the property's value.
> * ⭐ **Demonstrated** — the same dialog on a **float** whose live value is `0` pre-filled **`9999.0`**,
>   and on the **uint8** it pre-filled `255`. Two types, two different pre-fills, neither equal to the
>   live value in the float case. That is the type-derivation shown, not just read.
>
> Step 4 (the **Force** submenu consumer) was already closed by `[Y9-UI-2026-08-17]`, so with these
> the row is complete.

### ✅ DONE 2026-08-18 `[ELLIOT-Y1c-2026-08-18]` — run a generated CE invoke against a live game (audit #5 Y1, build 2862)

The invoke form passed **0** for every `UObject*` / `FName` argument since the feature shipped;
`tonumber(s, 16)` was handed a string still carrying its `0x`. Fixed, and the Lua semantics are
measured in three independent interpreters (CE's own `lua53-64.dll`, a 5.4 CLI, and CE's bundled
`lbaselib.c`).

**What that does NOT prove is that the corrected value reaches the function.** The measurement stops
at the Lua expression; everything after it — the mailbox write, the DLL's `CMD_INVOKE`, `ProcessEvent`
— is untested end-to-end.

1. In Live Walker, pick a UFunction taking an object parameter (`K2_AttachToActor`, or any
   `BlueprintCallable` with an `AActor*`), and use **Copy AA Script** / push to CE.
2. Paste an instance address from any panel — i.e. the app's own `0x`+uppercase-hex format — into the
   `[UObject*: …]` field and FIRE.
3. Success is **not** `INVOKED OK`: that was printed by the broken version too. Confirm the *effect*
   in-game, or set `UE5_DEBUG=1` and read the decoded return.
4. Worth one negative case: FIRE with the untouched `0x0` default and confirm it behaves as a null
   argument — that path was the only one that ever worked, so it should be unchanged.

> ### 🟡 PATH PROVEN, ARGUMENT NOT — and BOTH of this row's staging instructions are wrong
> `[ELLIOT-Y1-2026-08-18]`
>
> Elliot, UE **504**, DLL **3262**, CE 7.7, AOBMaker connected, PE hook installed this launch
> (`hook installed at 0x141596890`). Target: `BP_PlayerCharacter_C::FireBirdLaserOngoing` via the
> Live Walker **`INV`** button — the button that actually reaches `InvokeScriptGenerator`, which is
> where the `tonumber` fix lives.
>
> **What IS established (the transport):** `INV` pushed the script (`Invoke script created in CE: …`),
> the record popped the CE form titled `BP_PlayerCharacter_C::FireBirdLaserOngoing | 0x134FE8040`
> — i.e. it resolved a live instance by itself — the `0x`+uppercase address was accepted into the
> `[UObject*: Actor, 8 B]` edit, and FIRE reached the DLL: `Mailbox: received cmd=1` →
> `INVOKE inst=0x134FE8040 func=0x3779D5900` → **`INVOKE result=0`**, with `UE5_DEBUG=1` printing
> `Invoking via mailbox… INVOKED OK`.
>
> ⛔ **That is exactly as far as it goes, and step 3 predicted it: `INVOKED OK` is not the result.**
> The pointer's arrival is still **unwitnessed**, for a reason worth writing down:
>
> ### ⚠ TRAP 1 — the form's `[UObject*: …]` rows can be BP LOCALS, outside `parmsSize`
> The DLL logged **`parmsSize=8 numParms=1`**: the function's only real parameter is
> `ElapsedSeconds [double, 8B, off=0]`. Every object row the form offered —
> `CallFunc_Conv_SoftObjectReferenceToObject_ReturnValue [UObject*: Object, off=8]` and
> `K2Node_DynamicCast_AsActor [UObject*: Actor, off=16]` — is a **Blueprint frame local past the
> parameter block**, not an argument. Reading the mailbox params buffer after FIRE
> (`g_invokeMailbox 0x7FFEDC3AA5D0` + `0x328`, 24 bytes) returned **all zeros**.
> ⇒ **Picking any `[UObject*: …]` row the form happens to show cannot decide Y1 — and it will look
> like it did, because the invoke returns `result=0` and prints `INVOKED OK` either way.** The target
> must be a function whose ObjectProperty is a *parameter* (offset < `parmsSize`).
>
> ### ⚠ TRAP 2 — Live Walker lists only the class's OWN functions, so this row's own example is unreachable
> The row says to pick `K2_AttachToActor`, which is declared on `Actor`. On the walked pawn
> (`BP_PlayerCharacter_C`, **105 functions**), filtering the function list for `K2_` returns **0**, and
> `Owner` returns **0** — the list is own-class only. Nor can `Actor` itself be walked. Confirmed from
> the other side too: `KismetSystemLibrary` (which *does* declare `GetObjectName(UObject*)`) is refused
> by the UI with *"No live instance … LiveWalker has nothing to walk because there is no instance."*
>
> ▶ **What the next attempt needs:** a class that (a) has live instances and (b) **declares** a
> function taking an `ObjectProperty` **within `parmsSize`**. Screen candidates in Interesting Funcs
> by the `Param` column, then confirm the type and offset in the `AA(Baked)` dialog — it prints
> `[UObject*: …, 8B, off=N]` — and only then drive `INV`. **Steps 3 and 4 remain unrun**: with no
> argument-carrying target, neither the effect-confirmation nor the `0x0` null control is meaningful.
>
> ### 🔁 SECOND ATTEMPT — a qualifying target WAS found, and the witness turned out to be invalid
> `[ELLIOT-Y1b-2026-08-18]`
>
> **Finding the target is solved and cheap** — do it over the pipe, not by clicking:
> `walk_functions {addr: <class_addr>}` returns each function's `num_parms` **and** a `params[]` list
> with `type`/`offset`/`ret`; a real parameter is one of the **first `num_parms` entries**. Screening
> the classes that had live non-CDO instances produced **38** functions with a genuine object
> parameter. Script kept at `out/ce-plugin-test/find_y1_target.py`.
> ⚠ `walk_function_props` is **not** the parameter list — it is Denken's property-xref walk
> (`scope`, `occurrences`, `offset:-1`). Using it here returns nothing and looks like "no candidates".
>
> **Target used:** `AttackCollisionData::SetOwnerClass` — `num_parms=1`, the single param
> `OwnerClass [ObjectProperty, off=0]`, **native** (`flags=0x4020401`), on a live instance
> `0x1C1FED3200` whose own `OwnerClass` field (`+0x1F8` → `0x1C1FED33F8`) read **all zeros** first,
> in both `read_mem` and the Live Walker grid. A perfect-looking witness: baseline zero, so any
> non-zero could only come from the typed value.
>
> **What ran:** `INV` → CE form with exactly one field `OwnerClass [UObject*]` → typed
> `0x3C8940A30` (a real `UClass`) → FIRE → `Mailbox: INVOKE inst=0x1C1FED3200
> func=0x7FF4DE4B6A48`, `result=0`.
>
> ⛔ **And the witness is INVALID — which is the actual result of this attempt.** Both `OwnerClass`
> and `paramsData` read back zero, which *looks* exactly like the old bug (`tonumber('0x…',16)` →
> nil → 0). It is not. Four checks, in order:
> 1. **The emitted script is the FIXED form.** Dumped from the CE record itself
>    (`out/ce-plugin-test/inv_script.txt`, 11,900 chars), line 251:
>    `writeQword(PD + 0, (function() local s = edits[1].Text or ''; … local h = s:match('^0[xX](%x+)$'); if h then return tonumber(h,16) or 0 end; …)())`
>    with `local PD = mb + 0x328` — the prefix IS stripped before `tonumber(h,16)`.
> 2. **That expression parses correctly in CE's own Lua**: `PARSE of 0x3C8940A30 -> 16250047024 (0x3C8940A30)`.
> 3. **My reader and the address are sound** — negative control: `writeQword(mb+0x328, 0xDEADBEEF)`
>    then `read_mem` shows `0x00000000DEADBEEF`; a plain write of `0x3C8940A30` reads back
>    `0x00000003C8940A30`. The address `mb+0x328` is confirmed against `Mimic.h` (`paramsData` @ 0x328),
>    and the header survived the call (`instanceAddr`/`ufuncAddr`/`parmsSize=8`/`numParms=1` all correct).
> 4. ⇒ **`paramsData` is cleared by the invoke path**, so reading it *after* the call cannot witness
>    what was passed — and `SetOwnerClass` does not store into the `OwnerClass` field either.
>
> ▶ **Next attempt needs a witness that SURVIVES the call**, and the two that would:
> * **Freeze the game thread and read `paramsData` while the DLL is still blocked** — exactly the
>   AA14-AA20 step-5 staging (`set_invoke_timeout` well above the Lua's 10 s, `suspend.py suspend-tid`
>   on a thread picked **by fire-rate**). ⚠ This needs `hook_active: true`; on this launch the hook
>   again failed with `MH_CreateHook failed: MH_ERROR_MEMORY_ALLOC`, so **restart until it installs**.
> * Or a function that **persists** its object argument somewhere readable (verify by reading the
>   field back, not by assuming a setter stores it — `SetOwnerClass` did not).

> ### ✅ THIRD ATTEMPT — CLOSED, and the previous attempt's diagnosis was WRONG
> `[ELLIOT-Y1c-2026-08-18]`
>
> Elliot, UE **504**, DLL **3262**, CE 7.7 attached, AOBMaker connected, **`hook_active: true`**.
> Target `DropItemSpawner::Setup` — chosen because its two parameters are *exactly* the two types
> this bug affected: `InOwner [ObjectProperty, off=0]` and `NameLotteryID [NameProperty, off=8]`,
> `parmsSize=16 numParms=2`, flags `0x04020401` (Final|Native|Public|BlueprintCallable — **not**
> `FUNC_Static`, so it routes through GameThreadDispatch). One FIRE settles both halves.
>
> **Both values were typed WITH the `0x` prefix**, which is the whole point: a bare-hex string goes
> down `tonumber(s,16)`, the path that always worked. Distinct values so they cannot be confused.
>
> | witness | pre-FIRE | post-FIRE | typed |
> |---|---|---|---|
> | `paramsData+0x00` (InOwner) | `0x0` | **`0x1078919D0`** | `0x1078919D0` |
> | `paramsData+0x08` (NameLotteryID) | `0x0` | **`0x1234ABCD`** | `0x1234ABCD` |
> | instance `Owner` field `+0xE0` | `0x0` | **`0x1078919D0`** | — reached the function |
>
> ⇒ **Step 3 satisfied by the EFFECT, not by `INVOKED OK`**: `Owner` is a stored field that survives
> the call, and it holds the typed pointer. ⇒ **Step 4 (null control) run first, deliberately**, so
> the positive case started from a known zero: FIRE with the untouched `0x0` gave `result=0`,
> `status=1`, `Owner=0`, no crash. The check is demonstrably able to fail in both directions.
>
> ### ⚠ The Y1b conclusion "`paramsData` is cleared by the invoke path" is REFUTED
> It is not cleared on **any** path, and the code says so: the game-thread path copies `ownedParams`
> back over the caller's buffer ([`Stark.cpp:430`](../dll/src/Stark.cpp)), the timeout path
> deliberately performs no copy-back, and both the static-native and the no-hook fallback pass
> `&g_invokeMailbox.paramsData` straight to ProcessEvent. Two things that DO produce the observed
> zeros, and both applied to Y1b: the hook was **inactive** on that launch, and `Mimic.cpp` contains
> **eight** `memset(g_invokeMailbox.paramsData, 0, …)` calls in *other* command handlers — so any
> later mailbox traffic wipes the buffer. **Read it immediately after FIRE, with no mailbox command
> in between, and it is a valid witness.** Generalisation worth keeping: *a shared buffer is only a
> witness for as long as nothing else is entitled to write it.*
>
> ### ⚠ The script picks its OWN instance — witness THAT one
> The form resolved `inst=0x7FF4DE7EE190` (first live instance) while Live Walker was walking
> `0x7FF4DE81F970`. Reading `Owner` on the walked instance shows **no change** and looks like a
> clean FAIL. The mailbox header names the instance actually invoked — read it from there.
> Rig: [`tools/verify/mailbox_addr.py`](../tools/verify/mailbox_addr.py) resolves `g_invokeMailbox`
> by parsing the injected DLL's export table (no CE involved — CE's own `getAddress` is part of the
> path under test), and `tools/verify/y1_witness.py` prints both witnesses.
>
> ### ⚠ Two rig traps this cost, both worth not repeating
> * **A reader that returns 0 for "read failed" is useless here**, because 0 is also what the *bug*
>   looks like. A screener that dropped the `ReadProcessMemory` return check reported
>   `PERSISTS = False` for a store that had actually happened — the UI was showing the stored value
>   at the same moment. Every reader in `tools/verify/` now fails loudly instead.
> * **The IME eats typed hex.** This machine's default input is Chinese; typing `0x1078919D0` into a
>   CE form produced Han characters and a candidate window that also swallowed `Ctrl+A`/`Ctrl+V`.
>   `shift` toggles the IME to English; `End` + repeated `BackSpace` is the reliable clear, since
>   triple-click-to-replace silently left the old text in place.

### ✅ UI HALF NOW CLOSED 2026-08-17 `[SDKHDR-UI-2026-08-17]` — all three checks pass

UE5DumpUI **is** grantable once the shortcut lives in the all-users Start Menu (see
`docs/auto-verification-session-plan.md` §1), so the export was run for real: DumperTest Development
injected via the panel's own **Inject into running game…**, `Connected — UE504 (25179 objects)`,
`v1.0.0.3262 DLL 3262` on screen, then **Export → SDK Header (.h)** → 3.48 MB / 75,342 lines.

```cpp
struct DumperTestActor : public Actor
{
    FText Text_Even2_OneNull; // 0x02A0 (0x0010) TextProperty      <- FIRST member
    ...
    uint8_t bFlagA : 1; // 0x0670 (0x0001) BoolProperty [Mask: 0x01]
    uint8_t bFlagB : 1; // 0x0670 (0x0001) BoolProperty [Mask: 0x02]
    uint8_t bFlagC : 1; // 0x0670 (0x0001) BoolProperty [Mask: 0x04]
    bool bPlainBool;    // 0x0671 (0x0001) BoolProperty
}; // Size: 0x06E0
```

1. ✅ **Opens at the super's size.** The first member sits at **0x02A0 = 672** — the exact
   `super_props_size` the headless half measured — with no filler ahead of it.
2. ✅ **Declares none of the base's properties.** Zero `AActor` members in the block: no
   `PrimaryActorTick`, no `bNetTemporary`/`bOnlyRelevantToOwner`/`bAlwaysRelevant`, no
   `RootComponent`. All of those **are** in the `walk_class` reply this header was built from
   (`PrimaryActorTick` at 40, the replication bools at 88), so the filter demonstrably ran on data
   that contained them — an absence with a witness, not a bare absence.
3. ✅ **Bitfield runs match the gap.** `bFlagA/B/C` all at **0x0670** with masks 1/2/4, and the next
   member starts at **0x0671** — the run consumed exactly one byte. `bPlainBool` is emitted as a full
   `bool`, correctly *not* as a bitfield.

`Size: 0x06E0` = **1760** = the headless `props_size`, which is a fourth cross-check for free.

Both fixes are unit-verified end-to-end against the real emitters, with separate negative controls.
What no unit test can cover is the **boundary value itself**: `super_props_size` is a new
`walk_class` field read off a live `UStruct`, and the tests supply it by hand.

**Cheapest check — headless, no UI**, using the pipe recipe in
[audit-2026-08-13-early-code-findings.md](audit-2026-08-13-early-code-findings.md#the-reusable-win-from-today--headless-in-game-verification):

1. Inject into any game, then `walk_class` a **derived** class (anything `*_C`, or `AActor` itself).
2. Assert `super_props_size` is **non-zero, less than `props_size`**, and equal to the `props_size`
   the same command reports for `super_addr` when walked directly. That last equality is the real
   check — it is the only one that would catch the offset being read off the wrong struct.
3. Confirm the lowest-offset field in `fields` is **below** `super_props_size` (i.e. the reply really
   does carry inherited properties, so the filter has something to do). A run where every field is
   already ≥ the boundary proves nothing — it is the absence-shaped result
   [working-lessons.md](working-lessons.md) §1.2 warns about.

**Then the UI half**: export an SDK header for that class and check the struct opens at the super's
size, declares none of the base's properties, and that a class with packed bools (`AActor` has a
replication-flag block) emits `uint8_t bX : 1` runs whose byte count matches the gap to the next
field.

-----

### ✅ VERIFIED 2026-08-20 `[SDKHDR-2026-08-18]` — the exported SDK header COMPILES again

> ### ✅ STEPS 4 + 5 PASS 2026-08-20 `[SDKHDR-CL-2026-08-20]` — the real export, in front of a real compiler
>
> Steps 1–3 were closed under `[SDKHDR-REALEXPORT-2026-08-20]`; these are the last two, and they are
> the ones that put the artifact through `cl.exe`. Both run on the **real 75,342-line export**
> `out/DumperTest_SDK.h` (3,476,025 B), not on a fixture.
>
> **Step 4 — `rg "\[0x0\];"` → 0 matches.** With both of its own controls holding in the same pass, so
> the zero is not vacuous: extents *preceding* an identifier = **0** (was **5** pre-fix), and
> `OptionalProperty` declarations = **5**, all of the fixed shape:
> ```
> uint8_t CellBounds[0x40];   // 0x0088 (0x0040) OptionalProperty   (WorldPartitionRuntimeCellData)
> uint8_t Opt_Int_Set[0x8];   uint8_t Opt_Float_Set[0x8];
> uint8_t Opt_Str_Set[0x18];  uint8_t Opt_Int_Unset[0x8];           (DumperTestActor)
> ```
>
> **Step 5 — the excerpt compiles.** New rig `tools/verify/sdkhdr_step5.py` (distinct from
> `compile_sdk_header.py`, which compiles the C#-generated *fixture*): it lifts the **two**
> OptionalProperty-owning blocks out of the real header **verbatim**, reuses the stub prelude already
> shipped in `out/sdk-smoke/sdk_smoke.cpp`, adds `struct X {};` forward stubs for the 8 types the
> excerpt references and the prelude lacks (`Actor`, `Box`, `Object`, `EDumperTestGrade`, …), and runs
> `cl /Zs /TP /permissive- /utf-8`:
> ```
> cl exit code: 0
> ```
> The excerpted declarations are never rewritten — only surrounded — so what the compiler sees is the
> emitter's own text.
>
> ⭐ **Negative control (`--negative-control`), and it lands on the documented number.** Rewriting
> those same declarations back to the pre-fix spelling (extent before the identifier) makes cl reject
> them with **`error C2059: syntax error: '['` on 5 lines** — the same **5** malformed declarations
> this row records for the pre-fix export. So the check is demonstrably able to fail, and fails with
> the exact defect rather than some other error.
>
> ⚠ Scope is the excerpt, deliberately — see the row's own warning about `GenerateFullSdkAsync`
> emitting in GObjects order with no topological sort. A first run of the rig stopped on
> `EDumperTestGrade Grade;` with `C3646`; that was a gap in the rig's type-scanner (bare enum members
> carry no `struct`/`class` keyword), **not** a header defect, and is fixed in the rig.


*This is the OPEN FIXES INDEX's one **untagged** row ("SDK header does not compile"), surfaced by the
`[SDKHDR-UI-2026-08-17]` export above and worth more than the step that found it. It now has a tag, a
fix and a batch, and the index row is gone.*

*Was: `OptionalProperty` and any unresolved `StructProperty` baked the array extent into the **type**
string, so the field writer's `{type} {name};` emitted `uint8_t[0x40] CellBounds;` — not valid C++.
Measured over the whole 75,342-line export: **5 malformed declarations, every one an
`OptionalProperty`**, against **7,543 well-formed** `uint8_t Pad_0000[0x0028];` padding declarations —
the padding emitter was always right and only the two fallbacks were wrong. `CellBounds` is an engine
(World Partition) property, so **any** real UE5 title with a `TOptional` UPROPERTY exported a header
that could not be compiled. Now: `SdkExportService.MapCppDecl` returns a `CppDecl(Type, ArraySuffix)`
pair and the extent is written **after** the identifier, exactly as the padding path always did.
`null` out of the type switch is the ONLY route into the raw-byte fallback, so an extent cannot be
smuggled back into a type string by a future branch.*

> **Why nothing caught it**: the emitters were unit-covered, but never over a `TOptional` field — and
> a generated header is only ever *read* in this repo's checks, never compiled. Both gaps are closed
> offline now. `ui/UE5DumpUI.Tests/SdkHeaderDeclaratorTests.cs` walks **every** emitted member and
> rejects an extent that precedes the identifier *whatever produced it*, and
> `tools/verify/compile_sdk_header.py` puts the real emitter's output in front of `cl.exe`
> (`/Zs`, no dev shell needed — the artifact includes nothing). Negative control: re-inserting the
> pre-fix spelling gives `error C2059: syntax error: '['` on that exact line, and the shape oracle
> flags exactly one bad declarator.
>
> **What the offline half cannot cover** is the *corpus*. The fixture has the branches; a real title
> has the distribution — and an unresolved `StructProperty` did not occur even once in the
> 75,342-line export, so that fallback has still never been seen on live data.
>
> | step | do this | expect |
> |---|---|---|
> ### ✅ ALL THREE STEPS PASS 2026-08-20 `[SDKHDR-REALEXPORT-2026-08-20]` — on a real export, matching the row's own numbers
>
> UI connected to DumperTest Development (`Connected — UE504 (25179 objects)`), **Export → SDK
> Header (.h)** → `out/DumperTest_SDK.h`.
>
> ⭐ **The export is 75,342 lines — the exact figure this row cites for the PRE-FIX export of this
> same sample.** So it is the same corpus, and the comparison is like-for-like:
>
> | | pre-fix (per this row) | now |
> |---|---|---|
> | extent PRECEDING an identifier (step 2) | **5** | **0** |
> | `OptionalProperty` declarations (step 3) | 5 | **5** |
>
> Step 3 is satisfied, so step 2 is **not vacuous**: the same five fields are present and every one
> now emits the extent *after* the identifier —
> `uint8_t CellBounds[0x40]; // 0x0088 (0x0040) OptionalProperty` (the World Partition property this
> row names), plus DumperTest's `Opt_Int_Set` / `Opt_Float_Set` / `Opt_Str_Set` / `Opt_Int_Unset`.
>
> ⭐ **Confirmed by a real compiler, not only by grep.** `cl /Zs /TP /permissive-` over the whole
> 3.48 MB header produces **zero `C2059`** — the `syntax error: '['` the negative control produces
> when the pre-fix spelling is re-inserted. The defect is gone from the artifact, not just from the
> emitter.
>
> ⚠ **The unresolved-`StructProperty` fallback still has no live sample** — as the row predicted, it
> did not occur even once in this export either. That branch remains fixture-only.
>
> ### 📌 And this settles the separately-tracked `G4`-followup, with evidence
>
> The handover records that the header "still will not compile as one translation unit" because
> `GenerateFullSdkAsync` emits classes in **GObjects order with no topological sort**. **Confirmed.**
> Compiling the real export as one TU fails with **152+ errors** (`C1003` stops the count, so that is
> a floor), and the mix is diagnostic:
>
> | code | n | meaning |
> |---|---|---|
> | `C4430` | 50 | missing type specifier |
> | `C3646` | 32 | unknown override specifier |
> | `C2079` | 31 | uses undefined struct |
> | `C2143` / `C2238` | 19 / 19 | syntax / unexpected token before `;` |
> | **`C2059`** | **0** | **the SDKHDR defect — absent** |
>
> Every populated code is a **use-before-declaration** symptom; none is a malformed declarator. So
> the two problems are cleanly separated: `[SDKHDR]` is fixed, and what remains is ordering.

> | 1 | connect to a UE5 title with a `TOptional` UPROPERTY (**DumperTest** has `Opt_Int_Set`; any World Partition title has `CellBounds`), then **Export → Dump All** and **Export → SDK Header (.h)** | both complete without error |
> | 2 ⚠ THE ONE THAT MATTERS | grep the header for an extent that PRECEDES an identifier: `rg "^\s+\S*\[0x[0-9A-Fa-f]+\]\s+\w+;" out.h` | **0 matches**. It was **5** on the pre-fix export of this same sample |
> | 3 ⚠ NOT AN ABSENCE-SHAPED RESULT | `rg "OptionalProperty" out.h` | **≥1**, each of the shape `uint8_t Name[0xN]; // … OptionalProperty`. A header containing no `TOptional` at all makes step 2 vacuous ([working-lessons.md](working-lessons.md) §1.2) |
> | 4 | `rg "\[0x0\];" out.h` | **0** — MSVC rejects a zero-length array (C2466), so that would not compile either |
> | 5 | cut the struct(s) that own the `OptionalProperty` members into a small `.cpp`, prepend the stub prelude from `out/sdk-smoke/sdk_smoke.cpp`, and run `cl /Zs /TP /permissive- /utf-8` on it | **exit 0** |
>
> ⚠ **Do not over-read step 5.** It is deliberately scoped to an EXCERPT. `GenerateFullSdkAsync`
> emits classes in GObjects order with no topological sort, so the full 75,342-line header very
> likely does not compile as one translation unit regardless of this fix (a `struct X : public Y`
> whose `Y` is declared later is an incomplete base). That ordering question is **untested and
> separate** — if step 5 on the whole file fails with "undefined base class" rather than a syntax
> error at a `[`, that is the ordering gap, not this defect, and it is worth opening as its own item.

Shipped as the first fix batch of [audit #5](audit-2026-08-13-early-code-findings.md) cluster ①.

-----
