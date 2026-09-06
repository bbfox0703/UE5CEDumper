# `CrashReportClient.exe` as a version source — both branches, on a manufactured case

**Claim tag:** `[CRCSOURCE-2026-09-06]` — cited from [`../../../dev-log.md`](../../../dev-log.md).
**Build:** 3393. **Rev:** `Genau::kVersionDetectLogicRev` 6 → 7.
**Fixture:** DumperTest Development (UE 5.4), with an Editor `CrashReportClient.exe` **planted** by
[`../../../../tools/verify/crc_source_live.py`](../../../../tools/verify/crc_source_live.py).

-----

## ⭐ Why this was preserved rather than left to the rig

The rig reproduces both runs on demand — but only **while the UE Editors are installed**. It plants
`UE_5.4`'s and `UE_5.7`'s `CrashReportClient.exe`, and the maintainer keeps those Editors only as
long as the SSD allows; they are tens of GB and are expected to go. Once they do, `crc_source_live.py`
cannot run and these two runs are the only record that both branches were exercised.

That is the same reasoning as [`../../../../tools/ue-crc-oracle.json`](../../../../tools/ue-crc-oracle.json),
harvested in the same session for the same reason.

-----

## The two branches

`agree-scan.log` — the planted CRC is UE_5.4's, matching the fixture's own engine:

```
DetectVersion: CrashReportClient ProductVersion -> UE 5.4 -> 504
DetectVersion: PE VERSIONINFO -> UE 5.4 -> 504
DetectVersion: CrashReportClient and the game exe AGREE on 504
FindAll: UE Version = 504 (tier=1, detected=yes, lowConfidence=no, publisher=-)
```

`disagree-scan.log` — **the negative control**, and the half that proves anything. The planted CRC
is UE_5.7's against a UE 5.4 fixture:

```
DetectVersion: CrashReportClient ProductVersion -> UE 5.7 -> 507
DetectVersion: PE VERSIONINFO -> UE 5.4 -> 504
[WARN] DetectVersion: SOURCES DISAGREE — CrashReportClient says 507, the game exe's own
       VERSIONINFO says 504. Taking 507 (engine-shipped beats game-authored).
FindAll: UE Version = 507 (tier=1, detected=yes, lowConfidence=no, publisher=-)
```

⚠ **The agree run alone would have proved nothing.** Detection already returned 504 for this
fixture, so that run passes whether or not the new code executes at all — §1.2's point exactly. Only
the disagree run distinguishes *"CrashReportClient was read and won"* from *"nothing happened"*.

⚠ Note `PE VERSIONINFO -> UE 5.4 -> 504` is **byte-identical** to the line this detector has always
emitted. That is deliberate: `tools/verify/sweep_title.py` greps the literal `PE VERSIONINFO`, and
[`../revbump6-cache-restamp/README.md`](../revbump6-cache-restamp/README.md) quotes the line
verbatim, so the refactor that split the reader in two preserved both wordings rather than
unifying them.

-----

## What would refute this

* **A cache hit.** Both runs must start from a MISS, or `FindAll` skips `DetectVersion` entirely and
  the log shows a `(cached, rev=…)` line instead. The rig drops the fixture's record first; more
  importantly its assertions require the `AGREE`/`DISAGREE` lines, which **only exist on the
  detection path** — so a silently-cached run fails rather than falsely passing.
* **The disagreement resolving the other way.** If `UE Version = 504` had followed a `SOURCES
  DISAGREE` line, the preference order would be the opposite of what is documented.
* **A real title contradicting the premise.** The claim is that a game never pairs its engine with a
  *different* engine's crash reporter. Across 66 installed folders nothing contradicted it: of the 8
  that ship one, CRC agreed with our detection 7 times, and both exceptions are explained (a stale
  cache entry for an uninstalled build; DragonSword's documented runtime raise). A title shipping a
  genuinely mismatched CRC would weaken the whole source — the `SOURCES DISAGREE` WARN exists to
  surface exactly that if it ever happens.
