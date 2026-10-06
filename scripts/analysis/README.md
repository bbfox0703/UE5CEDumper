# scripts/analysis/

Offline-analysis tooling that consumes the JSONL dumps produced by
**Export → Dump All Metadata (.jsonl)** in UE5DumpUI.

Two scripts share the same dump corpus:

- **`analyze_dumps.py`** — cross-game keyword + class-bonus calibration.
  Aggregates property / function names across N dumps from different
  games to derive empirically-grounded scoring tables. Feeds
  `PropertyScoringTable.cs` and `ClassLocationScorer.cs` (the engines
  behind the Interesting Properties / Interesting Funcs tabs).
- **`diff_dumps.py`** — same-game patch comparison. Diffs two dumps
  taken before/after a game update; surfaces moved field offsets,
  added/removed classes and functions, and function signature
  changes. Saves cheat-table maintainers from binary-searching offsets
  by hand when a patch silently breaks their working table.

A third script needs no dump corpus either — it reads the UI's own pipe logs:

- **`walk_payload_audit.py`** — payload-utilisation audit. Byte-accounts every
  JSON key of the `walk_instance` / `walk_instance_batch` responses in a
  `pipe-*.log` against a key-by-key classification of what the CE XML / CSX
  exporters actually read, and reports the used / conditional / CSX-only /
  **unused** split plus the worst offenders by bytes. Answers "how much of the
  payload would a lean export mode be allowed to drop" with measured bytes
  instead of reasoning — the follow-on question left open by
  [multipipe-eval.md §10.5](../../docs/multipipe-eval.md), answered in §10.6.
  Run `--self-test` after editing it. Logged bodies are capped at 1024 chars, so
  a default run samples prefixes; set `UE5DUMP_PIPE_LOG_FULL=1` before launching
  UE5DumpUI for an uncapped (unbiased) capture.

A fourth script needs no dump corpus at all — it reads installed games directly:

- **`scan_proxy_imports.py`** — proxy-viability census. Walks every installed
  UE game and reports which hijackable system DLLs (`version` / `dinput8` /
  `dxgi` / `winmm` / `dsound` / `xinput*`) each one imports, then summarises
  coverage and lists any game the current proxy set cannot reach. **Run this
  before adding a new proxy flavour** — it is what turned the winmm proposal
  from "clear win" into "adds nothing" (see the 4th-proxy section in
  [todo.md](../../docs/todo.md)). Handles three traps a naive per-exe scan gets
  wrong: non-UE games in the same library, modular UE builds whose bootstrap exe
  imports nothing (union over `*-Win64-Shipping.dll` instead), and
  launcher/real-exe pairs. Edit `ROOTS` to match the machine.

## Workflow: cross-game calibration (`analyze_dumps.py`)

1. Launch a UE4/5 game, attach UE5DumpUI as usual.
2. **Export → Dump All Metadata (.jsonl)** — saves
   `<game>-dump-<timestamp>.jsonl`. Its size grows with the game.
3. Repeat for 3–6 games spanning UE versions and genres for cross-game
   signal.
4. Run the analyzer:
   ```bash
   python scripts/analysis/analyze_dumps.py dump1.jsonl dump2.jsonl dump3.jsonl
   ```
   Produces `analysis-report.md` with four sections:
   - **Dump summary** — UE version / object count / BPGC count per game.
   - **Top N property names** — exact field names, ranked by total hits.
   - **Top N property tokens** — KeywordTokenizer-tokenized buckets.
     Cross-referenced against existing category buckets so you spot
     candidates not yet in any keyword table.
   - **Candidate 'Unusual Location' class tokens** — class tokens
     hosting cheat-relevant properties but not in ClassLocationScorer
     yet (the build 671 `LocalPlayer`/`GameViewportClient`/`HUD`
     entries were hand-picked from this style of analysis).
5. Read the report. Cherry-pick the high-confidence rows back into the
   C# tables. Add a unit test per change so the addition is
   regression-proof.

## Dump file format (JSON Lines)

Each line is a self-contained JSON object with a `kind` discriminator:

| `kind` | Notes |
|---|---|
| `meta` | Always first. UE version, module name, object count, dumper build, options snapshot. |
| `class` | One per class-like UObject (`Class` + BPGC variants). Embeds `props[]` + `funcs[]`. |
| `error` | One per class walk failure. Iteration continues. |
| `summary` | Always last. Counters: classes_emitted / skipped / errors / scanned. |

Per-class record (excerpt):
```json
{
  "kind": "class",
  "name": "AnimMan_Player_C",
  "addr": "0x16FC4BA250",
  "path": "/Game/AdvancedLocomotionV4/.../AnimMan_Player_C",
  "meta": "BlueprintGeneratedClass",
  "super": "AnimMan_Enemy_C",
  "is_bpgc": true,
  "props_size": 2120,
  "instance_count": 1,
  "props": [
    {"name":"Health","type":"FloatProperty","offset":1724,"size":4},
    {"name":"Max_Health","type":"FloatProperty","offset":1728,"size":4},
    {"name":"IsDead","type":"BoolProperty","offset":1732,"size":1}
  ],
  "funcs": [...]
}
```

## Privacy

The dump contains class names, property names + offsets, and function
signatures — UE reflection metadata. It does **not** contain:
- Player save data
- Runtime UObject instance contents
- Memory snapshots
- Game asset content

Safe to share publicly for analysis purposes.

## Anti-bias: when the default keyword tables don't fit your games

The keyword tables shipped in `PropertyScoringTable.cs` and
`ClassLocationScorer.cs` are calibrated from a specific 15-game corpus
(JRPG / sim / action mix — see [docs/dev-log.md](../../docs/dev-log.md)
build 678 entry for the actual list and findings). If you play
games whose dominant genres aren't well represented (FPS, MMO,
racing, fighting, horror), you'll likely see false-negatives — real
cheat targets that don't surface in the Interesting Properties tab.

**The recommended fix is data-driven, not opinion-driven:**

1. Dump 3-5 games from your preferred genres
   (Export → Dump All Metadata).
2. Run `python analyze_dumps.py your-dumps/*.jsonl`.
3. Look at the **Top OWN property TOKENS (≥ 3 games)** section —
   tokens appearing across multiple of YOUR games but not in the
   existing category column are candidates the default table missed.
4. PR your additions to `PropertyScoringTable.cs`. Include the analysis
   output as evidence (`x games / y hits`).

This is how the build 678 additions
(effect / target / radius / ability / modifier / duration / item /
Weapon / Projectile / Battle) landed in the first place. The keyword
table grows by empirical contribution, not curator guesswork.

**If you really don't want to compile**: fork the repo, edit the
arrays in the two files, run `build.ps1 -Target UI` — that's the
entire change loop. ~10 minutes once the build env is set up. A
"runtime keywords.json" override is on the wishlist (see todo.md)
but not yet implemented.

## Workflow: same-game patch diff (`diff_dumps.py`)

When a game ships a patch, the cooker can shuffle UPROPERTY offsets and
add/remove fields silently — every cheat table that hard-codes an
offset breaks. The diff tool surfaces exactly what changed at
UClass / UProperty / UFunction granularity so you can fix tables in
seconds instead of binary-searching offsets by hand.

1. Dump the game **before** the patch (Export → Dump All Metadata).
   Save the JSONL somewhere stable (e.g. `work/dump/<game>-pre.jsonl`).
2. Apply the patch / verify the new version.
3. Dump again: `work/dump/<game>-post.jsonl`.
4. Run the diff:
   ```bash
   python scripts/analysis/diff_dumps.py <game>-pre.jsonl <game>-post.jsonl -o diff.md
   ```
5. Read `diff.md`. For cheat-table fixing, the **Moved fields** and
   **Function signatures changed** sections are usually all you need.
   Pass `--minimal` to suppress the added/removed lists and emit only
   those breaking-change sections:
   ```bash
   python scripts/analysis/diff_dumps.py pre.jsonl post.jsonl --minimal -o break.md
   ```

### Other flags

- `--include-engine` — by default the engine's own modules
  (`/Script/Engine`, `/Script/UMG` and the rest of the DLL's list of engine
  packages) are skipped: they rarely shift across game patches. Blueprint
  classes and the game's own C++ modules (`/Script/<GameModule>/`) are
  always compared. Add this flag to compare the engine's modules too.
- `--self-test` — runs the built-in synthetic-fixture test suite. Use
  this after editing the script to confirm the diff logic still
  matches its specification:
  ```bash
  python scripts/analysis/diff_dumps.py --self-test
  ```

### Match key + known limitations

- Classes match by `path` (UE's canonical identifier). `addr` is
  session-local and ignored. Paths with one or more leading slashes
  are normalized — `//Script/X/Y` and `/Script/X/Y` match.
- Properties match by `name` within a class. **Renamed fields appear
  as Removed + Added** — the tool doesn't auto-detect renames. If a
  field went from `Health` to `CurrentHealth` at the same offset, scan
  the report for a same-offset removed/added pair.
- Same applies to renamed classes.
- Function bodies aren't in the dump — only metadata
  (`return_type` / `num_parms` / `parms_size` / `flags`). A patch that
  changes function logic without changing the signature is **invisible**
  to this diff (covered by Live ProcessEvent Call Profiler instead — see
  `docs/todo.md`).

## Future expansions

- Runtime `keywords.json` override so users can customise without
  rebuilding. AOT-compatible JSON source-generator pattern.
- Cluster classes by property-name set similarity — surface
  "Inventory-like" classes that don't follow the naming convention.
- Auto-detect field renames in `diff_dumps.py` (same offset + size,
  different name) — currently requires manual report scan.

PRs welcome.
