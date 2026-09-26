# Code-comment integrity — evaluation (2026-09-26)

**The maintainer's question:** comments drift from the code. The dev-log is kept in sync; comments are not.
Numbers in comments have disagreed with the code, and comments have outlived the code or the UE version they
described. Is it worth cleaning up, and how do we stop it recurring? Evaluated read-only on build 3560.

## 1. What was measured

Product code comes to 397 files (`dll/src`, `ui/UE5DumpUI`, `scripts`) with **56,738 comment lines**. In
`dll/src`, one line in three is a comment. **6,208 substantive comment blocks** (runs of 3 or more comment lines):
1,290 in the DLL core, 1,093 in DLL features, 3,825 in the UI and scripts.

**Mechanical** (`py tools/verify/comment_audit.py --blame`, also run over tests and tools):

| check | result |
|---|---|
| in-repo `File.cpp:NNN` line references | **97 of 141 (69%) point at a line that has moved or changed** since the comment was written. `git blame` gives the writing commit, and that commit's copy of the target is compared with today's. Product code: 57 of 77; tests and tools: 40 of 64. |
| references to UE engine / CE source lines (`UnrealNames.cpp:2017`, `CEFuncProc.pas:1346`) | 79 in total. These cannot be checked mechanically: they are right only for the version they were read from. |
| `Ns::Symbol` in our own namespaces (Frieren roster, `Sig`, UI classes) | **0 dead** of about 1,085. Earlier audits cleaned this class. |
| `[TAG]` references no doc records (archive included) | 17 references to 5 tags (`CLASSCAP-2026-08-21`, `B30-REOPEN-2026-09-10`, `BADGEPRIME-2026-09-10`, `SOLIDE-REFUSAL-2026-09-10`, plus one in tests) |
| `.md` references to files that do not exist | 18. `aot-pitfalls.md` alone is cited from 11 UI files. |
| derivable counts stated in comments | Few, and mostly contextual. Stale: `Frieren.cpp:3` says "~30 C ABI exports" (the real count is 63), and `Himmel.h`'s "35 of 158 entries" (160 today). |

**Semantic** (`py tools/verify/comment_sample.py` with seed 20260926, 30 blocks per stratum). Each stratum was
judged against today's code by one agent, run one at a time (workflow `wf_a31798bc-35d`; verdicts in
`out/comment_audit/sample_verdicts.json`):

| stratum | accurate | stale | of which |
|---|---|---|---|
| DLL core (Genau / Macht / Himmel / Aura / Ubel / Serie / Grimoire …) | 20 | **10 (33%)** | 6 behaviour, 4 number |
| DLL features (Fern / Frieren / Mimic / Radar / Sein / Laufen …) | 21 | **9 (30%)** | 7 behaviour, 2 dead reference |
| UI + CE Lua | 26 | **4 (13%)** | 2 behaviour, 1 number, 1 dead reference |
| **total** | **67** | **23 (26%)** | 15 behaviour, 5 number, 3 dead reference |

Extrapolated, that is on the order of **1,600 stale blocks** in product code, most of them in the DLL. A 90-block
sample gives about ±9 points on the overall rate. **No block in the sample was stale because of a UE-version
change.** Version claims were checked against the repo's own tables and held.

## 2. Why comments go stale here

The judges' recurring causes, in order of frequency:

1. **Additive drift in comments that ENUMERATE.** A later commit adds a caller, a field, a pipe key, a state or a
   feature, and the comment listing them is not touched. Examples:
   - `Aura.h:202` names 5 internal callers; there are 9;
   - `Renge.h:142` omits `noclip` from `fly_set`;
   - `Radar.h:776` omits `viewExclude` from a cache key;
   - `Genau.h:136` says "three values" for a 4-value enum;
   - `Himmel.h:2137` says "the 8th entry … do not append a 9th" over a 10-entry table.
2. **Uniqueness and role claims.** "used only for", "the one site", "used both for direct calls and the hook".
   They go stale the moment a second caller appears or a caller leaves (`Sein.cpp:317`, `Frieren.cpp:1847`,
   `ClassStructViewModel.cs:253`).
3. **Plan-phase language left in shipped code.** For example, `Laufen.h:3` still says "P1 wires WALK_SPEED; P2 /
   P3 reuse …" with all three knobs shipped.
4. **Line numbers.** 69% drift, measured above.
5. **Bulk claims about the build.** "No test target compiles X.cpp" is false for Aura / Genau / Macht / Radar /
   Serie / Ubel / Denken / Flamme since `dll_core_test` began #including them. It survives in at least 9 places
   (`Radar.h:623`, `:1110`, `:1399`, `Radar.cpp:957`, `Ubel.h:291`, `Macht.h:199`, `Genau.cpp:1420`,
   `Grimoire.h:828`, `Aura.h:828`).
6. **Doc references that moved or never existed.** `CLAUDE.md`'s trimmed rule is cited from `Frieren.cpp:944`;
   `godmode-spec.md §5.2a` never existed; `aot-pitfalls.md` is cited 11 times.

Comments tied to a finding tag, whose record still exists, stayed accurate. What rots is the "what" comment that
restates code, and above all the one that lists things.

## 3. Cost of cleaning up

The 90-block semantic sample cost about 550 k sub-agent tokens and 32 minutes with 3 agents. A full semantic pass
over 6,208 blocks is about **69 times that**, far past a session quota. So a full sweep is not viable.
- **Mechanical findings: cheap.** About 200 targeted edits, scriptable, with no agents.
- **Semantic cleanup: must be targeted and incremental.**

## 4. Recommendation

**Prevention first: it is cheap, and it stops the inflow.**
1. **Gate: no in-repo line numbers in comments.** Cite a function, a constant or a `[TAG]` instead. Convert the
   141 existing references once. External engine / CE references must name their version (e.g.
   `UnrealNames.cpp@5.4:2017`). The 69% drift is the strongest single number here.
2. **Gate: "no test target compiles X" claims** checked against the `#include "../src/*.cpp"` list in
   `dll/tests`.
3. **Gate: references in comments must resolve.** `docs/*.md` files, `§` headings, and `[TAG]`s recorded
   somewhere in `docs/` (the archive counts).
4. **Numbers beside code become asserts.** Examples:
   - `static_assert(std::size(SPARSE_PATTERNS) == N)` where a comment relies on a table size;
   - a `Count` sentinel where a comment says "three values";
   - the derived-count registry for counts stated in comments.
5. **C# doc-comment lint:** `</summary>` directly followed by `<summary>` means a comment attached to the wrong
   type (`ISnapshotStore.cs:5`).
6. **Change-time aid (the real cure for causes 1 and 2):** a script that takes a diff and lists every comment
   elsewhere that names a changed symbol (function, field, pipe key, enum). Wire it into the skeptic-review
   prompt and the commit routine, so the enumerating comment is found at the moment it goes stale.
7. **Comment-style rule** in working-lessons:
   - say *why* and state invariants;
   - do not enumerate callers / fields / keys (write "every non-pipe caller");
   - no uniqueness claims unless an assert enforces them;
   - no plan-phase wording in shipped code;
   - history and measurements live in `todo.md` / `dev-log.md` under a tag, and the comment cites the tag.

**Cleanup, second:**
- **Phase 1 (mechanical, no agents):** fix everything the gates above report, about 200 edits.
- **Phase 2 (semantic, targeted):** review only the blocks that match the rot patterns (enumerations,
  "only" / "both" / "the one", number words, `P1`-`P3`), DLL core first. Run one module per session with at
  most 3 agents.
- **Boy-scout rule afterwards:** a file's comments are checked when the file is touched, using the change-time aid.
- **Track the rate:** re-run `comment_sample.py` with a new seed at each release. The 26% baseline is the number
  to push down.

**Lead to check, found by the sample (not verified):** `Fern.cpp:2992` still documents the two-state bool rule
("absent mask = native") after `[A3-BOOL-NATIVE-NOWRITE]` made it three-state. The judge noted that
`search_properties` may emit no `bool_native`. If so, that is a code gap, not only a stale comment.
