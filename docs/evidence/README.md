# `docs/evidence/` — runtime artifacts that committed claims rest on

⭐ **WHY THIS DIRECTORY EXISTS.** Log retention here is by **AGE, not generation count** —
`Grimoire::LOG_RETENTION_DAYS = 21` / `Constants.LogMaxAgeDays` — so anything under
`%LOCALAPPDATA%\UE5CEDumper\Logs\` is gone three weeks after it was written. That is right for logs
and wrong for **evidence**: a register row or a commit message that cites a log line outlives the
file it cites, and then nobody can re-examine it. `out/` is not a solution — it is gitignored and
does not travel to the second machine.

So when a claim in `docs/` depends on a specific runtime artifact, the artifact is copied here **at
the moment it is read**, and the decisive lines are ALSO quoted into the citing doc. Both, not
either: the quote is what a reader sees, the file is what a sceptic re-checks.

⛔ **This is not a log archive.** A file belongs here only when a *specific committed claim* would
become **unfalsifiable** without it. Everything else is allowed to purge — that is the retention
policy working, not a loss.

-----

## ⭐ Age is NOT the criterion for removal — orphanhood is

The natural instinct with a growing directory is to expire the old. That is exactly wrong here:

* a 2026 artifact whose row is **still open** must be kept **forever** — it is the only surviving
  proof of a claim the project still makes;
* an artifact added **yesterday** whose claim was deleted or rewritten is **already garbage**, and
  the longer it sits the less anyone dares remove it, because nobody remembers what it supported.

So nothing here expires on a timer. `tools/check_evidence_index.py` asserts that **every artifact
still points at a live claim** and fails CI when one does not — turning "someone should tidy this
up" into a red gate with a name on it. The `YYYY-MM/` layer is for **human navigation and growth
review**, not for expiry.

-----

## Layout, and the rules for adding to it

```
docs/evidence/
  README.md                 <- this index; every directory below MUST be listed
  YYYY-MM/
    <slug>/
      README.md             <- the claim, the builds, what would REFUTE it
      <artifact files>
```

1. **Verbatim bytes.** `.gitattributes` marks `docs/evidence/** -text`, so git does **not** normalise
   line endings in or out. A re-formatted artifact is a quotation, not evidence. `-text` rather than
   `binary` so a reviewer can still diff it.
   ⚠ `.gitignore` has a global `*.log`; `!docs/evidence/**/*.log` re-admits these. Without it
   `git add docs/evidence/` stages the READMEs, **silently skips every log, and exits 0**.
2. **Redact machine identity, and let the gate prove it.** No `C:\Users\<name>\…`, no account or
   machine names. `tools/check_no_local_paths.py` runs over every tracked file. Use the env-var forms
   it documents (`%LOCALAPPDATA%\…`, `%USERPROFILE%\…`).
   ⚠ Check per file, never by category: `init-*.log` carries the full executable path on its
   `DllMain` line; `offsets-*` / `scan-*` are usually clean — *usually*.
3. **Name the claim.** The per-directory README must cite at least one claim tag
   (`[SOMETHING-YYYY-MM-DD]`) that appears in a doc **outside** `docs/evidence/`. The gate enforces
   this; a tag that exists only here is an orphan by definition.
4. **State what would refute it.** Evidence that cannot be argued with is decoration.
5. **Keep it small.** Excerpts, not corpora. The gate prints the running total on every CI run so
   growth is visible before it is a problem.

-----

## Index

| directory | claim | citing doc | builds | what it proves |
|---|---|---|---|---|
| [`2026-09/g12-heuristic-branch/`](2026-09/g12-heuristic-branch/) | `[G12G3-CLOSE-2026-09-06]` | [`../verification-register.md`](../verification-register.md) | 3313, 3338 | the `ValidateAndFixOffsets` heuristic branch **is** reachable — refuting an earlier "NO FIXTURE EXISTS, 19 of 19" sweep — plus one run of it that the code, byte-identical today, cannot explain |
