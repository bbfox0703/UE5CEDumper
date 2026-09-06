# `docs/` — the full documentation index

[`../CLAUDE.md`](../CLAUDE.md) lists only the handful of documents a session needs **before it
knows what it is doing**. Every other document in this repository is indexed here, one row each.

⛔ **A row here is a POINTER, not a summary.** One or two sentences answering *"when would I open
this?"* — target ≤300 bytes, and keep a ⛔/⚠ only when it changes whether someone opens the doc at
all. **Findings, counts, `file:line`, derivation commands and traps belong in the document
itself**; if a fact is not there yet, put it there rather than here.

⭐ **Why the rule is this strict, and it is not about length.** A row saying *what a doc contains*
has to be re-verified every time that doc changes, and nobody does — so it rots silently. A row
saying *when you would open it* does not. Two audits (2026-08-27, 2026-09-06) found the same thing
both times: near-entirely duplicated prose, **plus claims refuted by the very doc they pointed at**.
The full account is [`working-lessons.md` §7.2](working-lessons.md).

⚠ This file is **not** loaded every turn — that is the point of splitting it out — so it may grow.
`CLAUDE.md` may not: it is capped at 40,960 bytes and pays for every byte on every turn. **A new
document gets a row HERE**, not there.

| Document | Contents |
|----------|----------|
| [toolchain.md](toolchain.md) | **What a machine needs, and why** — the reasoning behind `bootstrap.cmd`. ⚠ Read before installing anything on a new machine: tiers, the VS2026 winget id + `.vsconfig` list, the do-NOT-install pairs, how to prove the env works. |
| [roadmap.md](roadmap.md) | **Current state** — capability matrix, per-game configuration, tested games, long-running concerns. ⚠ Rows are stale past build 797; read its own banner before trusting one. |
| [tips.md](tips.md) | **User-facing how-to recipes** — goal → which panel/button. First recipe: forcing camera rotation in fixed-view (2.5D/45°) games. |
| [pending-verification_zh-TW.md](pending-verification_zh-TW.md) | 🇹🇼 繁中 operational checklist — the how-to steps for verification items that genuinely need a human. The English register is canonical. |
| [log-verification-checklist.md](log-verification-checklist.md) | **How to sweep a real session's logs** — the procedure companion to the verification register: which file holds which marker, what to grep, what to do in-game first, and which absences prove nothing. |
| [auto-verification-session-plan.md](auto-verification-session-plan.md) · [auto-verification-classification-2026-08-23.md](auto-verification-classification-2026-08-23.md) | **Running the register unattended** — the plan owns §3's grant mechanics and §4's authorised out-of-tree writes (ask first); the classification says which rows Auto + Computer Use could close. ⛔ §5's already-run batches are SPENT, and both docs are agent-produced — re-derive before planning off either. |
| [audit-2026-08-26-dxgi-appcompat-crash.md](audit-2026-08-26-dxgi-appcompat-crash.md) | ⚠ **Read before changing a proxy DLL export or resolver.** Why dxgi stopped OCTOPATH booting while the same binary as winmm worked: an AppCompat shim calls our export before our CRT exists. Fix, rig, and the WER-reading traps. |
| [audit-2026-09-05-vendor-ue582.md](audit-2026-09-05-vendor-ue582.md) | **Vendor audit #6** — UE 5.8.2 changed nothing for us; the value is the 6 defects of ours it found. ⛔ Read its top block before touching the ProcessEvent vtable table. |
| [audit-2026-08-13-early-code-findings.md](audit-2026-08-13-early-code-findings.md) | Audit #5 register — the pre-June-2026 "early" code. ⛔ Scanning AND fixing are both DONE; open it only to look up a named finding, or to record a new one. |
| [audit-2026-08-04-findings.md](audit-2026-08-04-findings.md) | **Bug/leak/refactor audit #4** (build 2554) — working tracker, all items shipped. Open for the per-finding detail, the refuted "do not re-raise" list, and the two cross-cutting root causes worth fixing as patterns. |
| [audit-2026-07-14-findings.md](audit-2026-07-14-findings.md) | **Bug/leak audit #3** (build 2168) — working tracker for the Solide/Hemmung/Linie/Schlacht/Grausam + Auto-Snapshot/Dump-Explorer/Live-Funcs findings, each with failure scenario, fix shape and effort/risk. |
| [pipe-protocol.md](pipe-protocol.md) | Named Pipe JSON IPC protocol (99 commands incl. Value Search + Group Scan begin/refine/query/end, Request/Response/Event) |
| [multipipe-eval.md](multipipe-eval.md) | **Multi-pipe IPC evaluation — verdict: do NOT add more pipes.** Read before any pipe/IPC concurrency change. §10 measured and refuted the original head-of-line-blocking reason; §8 is the Phase 0/1 revert postmortem. |
| [group-value-scan-spec.md](group-value-scan-spec.md) | **Multiple Values Group Scan** — the `Orden` matcher architecture and, more usefully, §3's **extension points**: how a future feature plugs into group matching. |
| [snapshot-group-match-spec.md](snapshot-group-match-spec.md) | **Snapshot Group Match** (shipped, in-game verified) — N-value group matching over captured snapshots via a C# `Orden` port: Mode A absolute, Mode B temporal, Deep. Read before touching snapshot multi-value. |
| [native-c-value-scan-spec.md](native-c-value-scan-spec.md) | **Native-C Value Scan** (shipped; P3 in-game verify pending) — opt-in scan of the raw non-`UPROPERTY` bytes in a UObject for native HP/MP, across Value Search / Group Scan / Snapshot→SPC→Pivot. |
| [ui-spec.md](ui-spec.md) | Avalonia UI tech stack (versions from the .csproj, never from here), AOT compatibility, component specs |
| [export-formats.md](export-formats.md) | CE XML, CSX, SDK Header, USMAP export rules, pointer chain model, type mappings. ⚠ Read its Coverage section first: a whole-pool export (USMAP / SDK / .jsonl) sees only the classes LOADED at that moment. |
| [technical-notes.md](technical-notes.md) | UE version differences, FField vs UProperty, FNamePool, DynOff, Property Type Layouts (Phases B-K), Address Finder layered lookup |
| [lessons-learned.md](lessons-learned.md) | Hard-won lessons from cross-game debugging (20+ games) |
| [reference-builds.md](reference-builds.md) | The stock-engine samples we package ourselves as PDB-bearing AOB oracles: the inventory, why each exists, which are deliberately not swept, and how to make another. Answers "what does the ENGINE do at version X", not what a game does. |
| [test-games.md](test-games.md) | 30+ test games with UE versions, GWorld status, stride info |
| [aobmaker-integration.md](aobmaker-integration.md) | AOBMaker CE Plugin pipe bridge (HEX / ASM / SYM / CreateAAScript) |
| [mindseye-fork-notes.md](mindseye-fork-notes.md) | **Read first if MindsEye breaks after a game update** — the three things this UE 5.4.4 licensee fork changes, which constants a patch can move, and how to re-derive each offline with capstone + `.pdata` (no Ghidra). |
| [reversing-nonstandard-ue-games.md](reversing-nonstandard-ue-games.md) | Playbook for forked/repacked engines where AOB + heuristics fail: patternsleuth → capstone → Ghidra → caller LEA → encode the fix (the Avowed case). Plus why we do not vendor Dumper-7/RE-UE4SS. |
| [corpus-preservation.md](corpus-preservation.md) | ⛔ Read before deleting anything under the Ghidra corpus root or an archive root, or uninstalling a corpus Steam title: what to keep, reinstall or drop, the PDB checklist, the drop order, and the never-drop set. |
| [aob-block-library-eval.md](aob-block-library-eval.md) | ⚠ Not just an eval — the block library and the n-gram specificity index are BUILT and CI-gated, so this doc is load-bearing. Read before touching either, or for the one decision still open. |
| [../tools/README.md](../tools/README.md) | Offline RE helpers — Ghidra scripts (`find_gobjects`/`decompile_functions`/`find_callers` Java + the pyghidra symbol/AOB exporters) and a capstone PE disassembler (`pe/disasm_function.py`). |
| [../scripts/analysis/README.md](../scripts/analysis/README.md) | Offline analysis tooling — `analyze_dumps.py` (cross-game keyword calibration) + `diff_dumps.py` (same-game patch diff, build 780). |
| [CE-Bugs-Minesweeper.md](CE-Bugs-Minesweeper.md) | **CE bugs and undocumented behaviours we actually hit** — open when CE misbehaves, and ⚠ before trusting `celua.txt` or the plugin SDK header: both describe behaviour the shipping binary does not have. |
| [ce-plugin-api-reference.md](ce-plugin-api-reference.md) | CE Plugin SDK C ABI reference — every `ExportedFunctions` member, the plugin types, enums, `pluginsync` threading. ⚠ A mirror: edit the external master first. |
| [ce-plugin-sdk-notes.md](ce-plugin-sdk-notes.md) | **CE pitfalls companion** — what CE's Pascal does that its C header does not admit. Read before emitting or changing a CE artifact: `TVariableType` ordering, opcode-nav return types, Lua-state threading, §13 `executeCodeEx`. |
| [ce-ccode-eval.md](ce-ccode-eval.md) | ⛔ CE `{$CCODE}` — EVALUATED, DO NOT ADOPT: the repo emits no injection hook sites, so there is nothing for it to attach to. Read before re-proposing it, or if a hook site ever appears. |
| [ce-ccode-reference.md](ce-ccode-reference.md) | CE `{$CCODE}` / `{$C}` manual — the native-code alternative to the `{$lua}` blocks this repo emits. Syntax, the parameter/register layout `{$LUACODE}` shares, the LUACODE comparison, and CE's own defects to avoid. |
| [ce-memory-scanning-internals.md](ce-memory-scanning-internals.md) | How CE's scanner actually goes fast — the reference implementation our own scanners (`Radar` / `Aura` / `Macht`) are measured against. Buffers, nibble wildcards, the numeric-scan path, AOBMaker's SIMD anchor scan. |
| [ce-disassembler-navigation.md](ce-disassembler-navigation.md) | Driving CE's Memory Viewer from outside — the verified Lua `SelectedAddress` route (Pascal-property-backed, not just `celua.txt`), reusable from our `{$lua}` blocks; and where the Type 6 pointer-write works. |
| [teleport-spec.md](teleport-spec.md) | **Teleport / Wirbel design contract** — markers, POV, coord TP, cursor forcing, the `CMD_TELEPORT` op table. |
| [teleport-coord-library-spec.md](teleport-coord-library-spec.md) | **Teleport Coordinate Library** design contract — the coord list, its CE-Lua + CSV export/import, and the locked decisions (file key, Map filter, character policy, size budget). |
| [godmode-spec.md](godmode-spec.md) | **GodMode / Solitar design contract** — the invincibility-bool scan + re-assert model, and the **locked Non-Goal** (no universal detection bool; surface per-game via Property Search) that also governs `Solide`. |
| [output-monitor-pin-eval.md](output-monitor-pin-eval.md) | **Pinning a game to one monitor when it has no monitor-select UI — EVALUATED, NOT BUILT.** Read before re-proposing it: UE reflection has no monitor concept, and the hard part is the game drifting back. |
| [ue-perf-counters-eval.md](ue-perf-counters-eval.md) | **Surfacing UE's own `stat` counters in the UI — EVALUATED, tiered.** Why the literal ask is impossible from an injected DLL, what shipped instead, and the dispatch/IPC measurements it produced. |
| [log-compression-eval.md](log-compression-eval.md) | **Log compression — SHIPPED.** Why `compact /c /exe:LZX` in place and not gz/zip, the two triggers, the `-0.log` liveness rule, and the traps a change here must not re-break. |
| [text-translation-eval.md](text-translation-eval.md) | **In-game S2T conversion + local-LLM translation — EVALUATED, in-memory rewrite REJECTED.** Read before re-proposing live text rewrite: the UE-source walls, the font-glyph risk, why offline `.locres` wins. |
| [experimental-snapshot-spc-pivot.md](experimental-snapshot-spc-pivot.md) | Snapshot / SPC / Class Pivot — the experimental-tab family: capture model, intersection queries, pivot aggregation. |
| [ce-export-drilldown-spec.md](ce-export-drilldown-spec.md) | CE-export pointer drill-down spec (depth model, cascade resolution) — the companion to [export-formats.md](export-formats.md). |
| [avowed-gobjects-fix.md](avowed-gobjects-fix.md) | The Avowed case study — static `FUObjectArray` + 20-byte packed `FUObjectItem` + the GWorld decoy. Read alongside [mindseye-fork-notes.md](mindseye-fork-notes.md) for non-standard-layout work. |
| [archive/](archive/) | Superseded docs, older `dev-log` halves and closed `todo.md` sections. Its [README](archive/README.md) says what each file holds, which build ranges, why it moved, and which are not byte-identical. |
