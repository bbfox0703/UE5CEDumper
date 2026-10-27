# Handover passages retired on 2026-10-27

Lifted out of [`../handover.md`](../handover.md) on 2026-10-27, because each had stopped being true or had
become history rather than procedure. Each passage below is the live file's text **unedited**; the line
before it says why it left and what replaced it. The live file keeps its section numbering, so a
`handover §N` citation still finds the place each one came from.

⚠ Relative links inside a moved passage, where there are any, are not repaired (the convention in
[`README.md`](README.md)).

## §2 — the two grant readings of 2026-08-22

History, not procedure. The rule they support (check `list_granted_applications` first) stays in §2, with a one-sentence summary of these readings and the refutation. The paragraph also argued with a plan-doc claim that was itself removed on 2026-10-08.

Two measurements, hours apart on the same machine, and they point opposite ways:

* **2026-08-22 morning** — `list_granted_applications` returned **20 grants made on 2026-08-19
  19:31–19:36**, still valid three days and many sessions later. So a new session does **not** clear
  them: the plan doc's "grants do not survive a session" (removed from it on 2026-10-08) is FALSE
  here.
* **2026-08-22 18:20, same boot** — the list came back **empty**, and all 20 had to be re-requested
  (batches of 7 · 7 · 6; every one granted, same names, same tiers). `GetTickCount64` puts the
  last boot at **2026-08-21 14:31**, *before* the morning reading — so **no reboot happened
  between the two**.

⛔ **"A reboot is the real invalidation event" is therefore REFUTED** — do not re-assert it. What
actually clears them is unidentified; an MCP-server or app restart is the obvious suspect and is
**unmeasured**, so do not write that down as the cause either.

## §2 — "System key combos are not granted"

No longer true: `list_granted_applications` reported `grantFlags.systemKeyCombos: true` on 2026-10-27. §2 now says to read the flag, and keeps the `WM_CLOSE` recipe.

### System key combos are not granted

⚠ **`systemKeyCombos` is NOT granted.** `alt+F4`, `ctrl+alt+del` and friends are refused with
*"is a system-level shortcut"*. To close a window, post `WM_CLOSE` yourself:

## §2 — "Avowed is installed but ungrantable"

Not true: the 2026-10-27 list holds `Avowed` (it resolves to the launcher shim `Avowed.exe`) and `Avowed-win64-shipping`, and §3 already described the `Avowed` grant. Avowed now has a row in §2's game table.

* **Avowed is installed but ungrantable** — absent from the Start menu, so `request_access` cannot
  resolve it. Perfectly usable for headless pipe/log rows.

## §7 — the `NEW DEFECT` write-up count

It counts nothing: the last `### ⬜ NEW DEFECT` heading in `todo.md` became `✅ FIXED` on 2026-08-23 (`[TREERECLICK-2026-08-22]`), and the grep has printed 0 since. The blockquote warning stays in §7, shortened to the index count. The command line, then the warning:

```bash
grep -c '^### ⬜ NEW DEFECT' docs/todo.md         # defect WRITE-UPS (not the queue!)
```

⚠⚠ **The two defect greps answer different questions and the obvious one is wrong.** The
OPEN FIXES INDEX table near the top of `todo.md` sits **inside a blockquote** (`> ###`, `> |`), so it
is invisible to any `grep '^### '`; the `^### ⬜ NEW DEFECT` grep finds only the long write-ups.

## §8 — the invoke order, and Elliot

Both fixed. A profiler-first start no longer poisons the hook: `[PEHOOKONCE-2026-08-18]`, and its live check found that a scan followed by Start recovers inside the same process. Elliot's "intermittent" hook had two causes: that poisoning (`auto-verification-session-plan.md`), and no free memory near the game's code, fixed in build 3649 (`[MH-PRERESERVE-PROXY]`, whose dev-log entry names Elliot). Elliot is UE 5.6, and the tool reads 506 since build 3647 (`test-games.md`).

* ⚠ **Invoke order is `init → trigger_scan → invoke → pe_profile_start`.** Profiler-first used to
  poison the PE hook permanently.
* ⚠ **Elliot's PE hook is intermittent by title** ("sometimes yes, sometimes no"). Switch host to
  Lushfoil rather than retrying. Elliot also *detects* as UE 427 while really being 5.04 — honestly
  flagged `detected=no, lowConfidence=yes`, not a bug.

## §10 — the CE source clone's version

Out of date: on 2026-10-27 `git describe --tags` in the clone printed `7.5-196-g6b07a405`. The rest of the bullet stays. The phrase it had:

`(tag **7.5**) vs installed **7.7** docs`
