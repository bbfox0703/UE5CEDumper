# G12 — the `ValidateAndFixOffsets` heuristic branch, and one run that contradicts itself

**Supports:** `[G12G3-CLOSE-2026-09-06]` in [`../../verification-register.md`](../../verification-register.md).
**Preserved 2026-09-06**, when the files were 13–14 days old against a 21-day sweep — i.e. roughly a
week from deletion. Copied verbatim; `.gitattributes` marks this tree `-text` so the CRLF endings the
DLL wrote are the CRLF endings committed.

**Redaction:** none was needed, and that was verified rather than assumed — these are `offsets-*`
logs, which carry only `[DYNO]` probe output. The executable path lives on `init-*.log`'s `DllMain`
line, which is why no `init` log is here. `check_no_local_paths` gates it either way.

-----

## What they show

The register row they support had recorded, from a sweep on **2026-08-20**, that *"NO FIXTURE
EXISTS — 19 of 19 hosts take the validated path, 0 take the heuristic branch"*. That was true when
written. All four files below are **later**, and all four take the heuristic branch.

| file | build | pool | what it is |
|---|---|---|---|
| `offsets-20260822-120505.log` | 3313 | real (`votes standard=20`) | ⭐ the useful one — a **populated** pool that still went heuristic, producing real scores (`CHILDPROPS +0x40: score=50 (tested 30 objects)`) |
| `offsets-20260822-120815.log` | 3313 | empty (`tested 0 objects`) | injected before the pool populated; every probe scores 0 |
| `offsets-20260822-123340.log` | 3313 | empty | as above |
| `offsets-20260823-222542.log` | 3338 | real, healthy (25,212 objects, `ItemSize=32`) | ⚠ **the contradiction** — see below |

So the branch **is** reachable, and reachable on demand: inject into DumperTest before the engine
finishes populating `GObjects`. The "no fixture" conclusion must not be inherited from the old row.

-----

## ⚠ The contradiction, in `offsets-20260823-222542.log`

Three consecutive lines, same millisecond, in a single healthy run (one `Logger started`, one
`ValidateAndFixOffsets: Starting`):

```
[2026-08-23 22:25:09.993] [INFO] [DYNO] FindStructByName: Found 'Guid' at 0x1DF1A5C9280 (index=4118)
[2026-08-23 22:25:09.993] [INFO] [DYNO] FindStructByName: Found 'Vector' at 0x1DF1A5C9100 (index=4124)
[2026-08-23 22:25:09.993] [WARN] [DYNO] ValidateAndFixOffsets: Cannot find Guid or Vector struct — trying heuristic fallback
```

The guard is `if (!guidStruct && !vectorStruct)` with **nothing between it and the two calls**, and
`FindStructByName` returns `obj` on the same line it logs `Found`. Both the call site and the whole
function body are **byte-identical** between `e4685592` (build 3338) and 2026-09-06 — diffed, not
assumed — and a build-3371 run on the same fixture, resolving the same struct indices (4118 / 4124),
takes the validated path. **The code cannot produce this sequence.**

**Leading hypothesis, held as a hypothesis:** cross-process log interleave. Log folders are keyed by
**process name**, not PID, so two `DumperTest` instances share one folder, and at build 3338 `Sein`
opened logs with `_wfopen` (`_SH_DENYNO`), which permits a second writer. ⭐ The `_SH_DENYWR` change
of `[SEINSHARE-2026-09-05]` would prevent it — a second writer now fails into `EmergencyNote` — so
the hazard is closed as a **side effect** of an unrelated fix, not by design.

⛔ **What would refute the hypothesis:** seeing a `Found …` / `Cannot find …` pair again on a build
≥ 3371. If that happens the guard itself needs re-examining, and this directory is where to start.
