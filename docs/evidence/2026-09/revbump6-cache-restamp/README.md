# `kVersionDetectLogicRev` 5 → 6 — the re-detect fires, and changes no verdict

**Claim tag:** `[REVBUMP6-2026-09-06]` — cited from [`../../../dev-log.md`](../../../dev-log.md)
and [`../../../verification-register.md`](../../../verification-register.md).
**Build:** 3390 (`682f3f2b-dirty` — the rev-6 change was still uncommitted when this ran, which is
exactly what the `-dirty` in `scan-0-head.log` line 1 records).
**Fixture:** DumperTest Development, PE hash `6A9C1C8410F23000`, cached at `ueVersion=504`,
`versionDetectRev=5`.

-----

## ⭐ Why this had to be captured, and cannot be re-derived

This is a **one-shot observation**. The bump's whole effect is a single transition per cached
record: a rev-5 entry re-detects **once** and re-stamps itself rev 6. After that the transition is
unobservable — every record on this machine now reads rev 6, and producing the rev-5 → 6 crossing
again would mean hand-editing a `versionDetectRev` back down, i.e. manufacturing the evidence
rather than measuring it.

That is the `docs/evidence/` criterion in its purest form: the claim outlives the 21-day log
retention, and no later run reproduces it.

-----

## What the artifacts show

`scan-0-head.log` — the first 9 lines of the live `scan-0.log`, verbatim (CRLF preserved; the
`-text` attribute stops git normalising them). Two facts, in order:

```
HintCache: Loaded hints for PE=6A9C1C8410F23000 (... UE=504 detected)
FindAll: Cached version 504 was stamped by logic rev 5 (current 6) — re-detecting once and re-stamping.
DetectVersion: PE VERSIONINFO -> UE 5.4 -> 504
FindAll: UE Version = 504 (tier=1, detected=yes, lowConfidence=no, publisher=-)
```

1. **The invalidation fires.** The cache-reuse branch was *not* taken; the line names both revs.
2. **The verdict is unchanged.** A fresh Tier-1 detection returns the same 504 the cache held, so
   the bump costs a re-detect and moves nothing a user sees.

The measured cost on this binary is **2 ms** (`.520` → `.522`), not the ~0.35 s rev 4 recorded —
that figure is for the memory string-scan path, and a Tier-1 PE VERSIONINFO hit never reaches it.
⚠ Do not generalise the 2 ms either: a **stripped** title (SquareEnix) has no VERSIONINFO to read
and does pay the scan.

`hintcache-record.json` — the same record before and after, showing `versionDetectRev` 5 → 6 with
`ueVersion` steady at 504. A HintCache record carries only a PE hash, an image name and a verdict,
so there is nothing here to redact.

-----

## What would refute this

* **A value that moved.** If any record's `ueVersion` had changed across the bump on a binary whose
  detection logic did not change, the bump would have been doing something other than advertised —
  the point of rev 6 is that it re-derives, not that it re-decides.
* **A record that did not re-stamp.** Three sibling `DumperTest.exe` records (older builds, other PE
  hashes) are still at rev 5 in the "after" cache. That is correct — invalidation is per-record and
  happens on that binary's next launch — but a record that was *launched* and stayed at rev 5 would
  mean `Flamme::SaveResults` is not writing the stamp it reads.
* **The reason itself.** Rev 6 is justified by Avowed's cache having held a `504` that
  `DetectVersionDetailed` cannot produce for that binary (it produces 503; the 504 was the runtime
  `CMC::GravityDirection` raise). If that 504 turned out to be reachable by some detection path,
  the bump would be unjustified — the mechanism would still be correct, but nothing would have
  needed reaching.
