# `docs/evidence/` — runtime artifacts a committed claim rests on

⭐ **WHY THIS DIRECTORY EXISTS.** Log retention in this project is by **AGE, not generation count** —
`Grimoire::LOG_RETENTION_DAYS = 21` / `Constants.LogMaxAgeDays` — so anything under
`%LOCALAPPDATA%\UE5CEDumper\Logs\` is gone three weeks after it was written. That is correct for
logs and wrong for **evidence**: a register row or a commit message that cites a log line outlives
the file it cites, and then nobody can re-examine it. `out/` is not a solution — it is gitignored
and does not travel to the second machine.

So: when a claim in `docs/` depends on a specific runtime artifact, the artifact is copied here
**at the moment it is read**, and the decisive lines are ALSO quoted into the doc. Both, not either:
the quote is what a reader sees, the file is what a sceptic re-checks.

⛔ **This is not a log archive.** Do not copy sessions here because they might be useful. A file
belongs here only when a *specific committed claim* would become unfalsifiable without it. Everything
else is allowed to purge — that is the retention policy working, not a loss.

-----

## Rules for anything added here

1. **Verbatim bytes.** `.gitattributes` marks `docs/evidence/** -text`, so git does **not** normalise
   line endings on the way in or out. A re-formatted artifact is a quotation, not evidence. `-text`
   rather than `binary` so a reviewer can still diff them.
2. **Redact machine identity, and let the gate prove it.** No `C:\Users\<name>\…`, no account names,
   no machine names. `py tools/check_no_local_paths.py` runs over every tracked file and fails CI on
   a leak, so the redaction is checked rather than promised. Use the env-var forms the gate
   documents — `%LOCALAPPDATA%\…`, `%USERPROFILE%\…`.
   ⚠ Check before copying: `init-*.log` carries the full executable path on its `DllMain` line, so
   an init log almost always needs redacting. `offsets-*.log` / `scan-*.log` are usually clean, but
   *check* — do not assume by category.
3. **Say what it proves, in a per-directory README**, with the claim it supports and the build it
   came from. An artifact with no stated claim is an orphan and should be deleted.
4. **Keep it small.** These are excerpts of a project's history, not a corpus.

-----

## Contents

| directory | claim it supports | build(s) |
|---|---|---|
| [`g12-heuristic-branch/`](g12-heuristic-branch/) | `[G12G3-CLOSE-2026-09-06]` in [`../verification-register.md`](../verification-register.md) — that the `ValidateAndFixOffsets` heuristic branch **is** reachable (refuting an earlier "no fixture exists" sweep), and one occurrence of it that the code cannot explain | 3313, 3338 |
