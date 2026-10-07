r"""Live check of the Live Funcs call trace: the ring on a running game, through the pipe.

    py tools/verify/livefuncs_trace_live.py --label <game> [--record-s 10] [--mb 32] [--skip-scope]
    py tools/verify/livefuncs_trace_live.py --label <game>-full --mb 128 --record-s 200 --plain-s 10 --traced-only

`[LIVEFUNCS-TIMELINE-2026-10-04]` On the game whose DLL is serving the pipe (one game at a time, never while the UI
holds the pipe), this records with and without the trace and checks what only a running game can show:

  1. rate: a plain recording's calls per second (total_calls over window_ms) -- the number the trace slider's
     estimate is made from, and the plan's first measurement;
  2. every call: a traced recording writes an entry and a return per call, so the ring's records are about twice
     the table's total_calls; every kept record is where its sequence number says, every return points at an
     earlier entry, entries and returns pair up per thread;
  3. paging: pe_trace_get hands the kept window back page by page in order, `next` moves forward, and paging ends
     at `written`; how long that takes, in MB/s;
  4. names: pe_trace_names names every function the ring holds -- live, or unloaded since it fired and named from
     its first call (build 3634+) -- and says which objects are still live;
  5. release: pe_trace_release frees the ring, and a later read finds nothing;
  6. ticked scope (unless --skip-scope): a function that calls other UFunctions is ticked; the scoped trace holds
     only that function's calls (each flagged a scope root) and calls nested under them;
  7. per-frame exclusion: a traced Start with exclude_per_frame leaves the previous table's per-frame functions out
     of the entries;
  8. refusal: a buffer that is not a power of two in 32..512 MB is refused and starts nothing;
  9. Start cost: how long a traced Start takes to allocate and touch the ring, for the sizes asked.

--traced-only stops after 5: it is for one long recording that fills the ring (seconds = bytes / (rate x 80)), to
measure what a full ring costs to read and name; --plain-s keeps the rate recording short meanwhile.

Every recording is stopped in a `finally`, and the trace released. Output: out/livefuncs-trace/<label>.json and a
summary. Exit 0 when every check holds; 1 otherwise; 2 when the pipe or the game is not usable.
"""
from __future__ import annotations

import argparse
import base64
import collections
import json
import pathlib
import struct
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient, PipeError  # noqa: E402

OUT_DIR = HERE.parents[1] / "out" / "livefuncs-trace"
REC = struct.Struct("<QQQQII")       # Linie::TraceRecord, 40 bytes
RET_BIT = 1 << 63
SCOPE_ROOT = 1
PAGE = 262144


def say(s: str) -> None:
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    sys.stdout.flush()


def data_of(reply: dict) -> dict:
    return reply.get("data", reply)


def ok_of(reply: dict) -> bool:
    return bool(reply.get("ok", True)) and "error" not in reply


def addr(s: str) -> int:
    return int(s, 16)


def record(c: PipeClient, seconds: float, trace: dict | None = None) -> tuple[dict, dict]:
    """One recording: Start (with `trace` when given), wait, Stop. Returns (start reply data, stop reply data)."""
    params = {"trace": trace} if trace is not None else {}
    t0 = time.perf_counter()
    start = c.request("pe_profile_start", **params)
    start_s = time.perf_counter() - t0
    if not ok_of(start):
        raise PipeError(f"pe_profile_start refused: {start}")
    try:
        time.sleep(seconds)
    finally:
        stop = c.request("pe_profile_stop")
    d = data_of(start)
    d["_start_s"] = start_s
    return d, data_of(stop)


def read_ring(c: PipeClient) -> tuple[dict, list[tuple], float, int]:
    """Every kept record, in order. Returns (trace info, records, seconds, bytes moved)."""
    # Not data_of: pe_trace_get carries its own `data` field (the records), so the reply is read as it is.
    head = c.request("pe_trace_get", **{"from": 0, "max": 1})
    frm, written = head.get("first_valid", 0), head.get("written", 0)
    recs: list[tuple] = []
    moved = 0
    t0 = time.perf_counter()
    while frm < written:
        page = c.request("pe_trace_get", **{"from": frm, "max": PAGE})
        moved += c.last_reply_bytes
        raw = base64.b64decode(page.get("data", ""))
        recs.extend(REC.iter_unpack(raw))
        nxt = page.get("next", frm)
        if nxt <= frm:
            break
        frm = nxt
    return head, recs, time.perf_counter() - t0, moved


def names(c: PipeClient, kind: str) -> tuple[list[dict], float]:
    out: list[dict] = []
    t0 = time.perf_counter()
    off = 0
    while True:
        d = data_of(c.request("pe_trace_names", kind=kind, offset=off, limit=20000))
        items = d.get("items", [])
        out.extend(items)
        off += len(items)
        if not items or off >= d.get("total", 0) or d.get("truncated"):
            break
    return out, time.perf_counter() - t0


def tree_children(recs: list[tuple]) -> tuple[dict, dict]:
    """For each entry seq: its function and its parent entry seq (None for a root), per thread."""
    func, parent = {}, {}
    stacks: dict[int, list[int]] = collections.defaultdict(list)
    for seq_kind, _t, a, _b, tid, _f in recs:
        st = stacks[tid]
        if seq_kind & RET_BIT:
            if a in func:
                while st and st[-1] != a:
                    st.pop()
                if st:
                    st.pop()
            else:
                st.clear()
            continue
        func[seq_kind] = a
        parent[seq_kind] = st[-1] if st else None
        st.append(seq_kind)
    return func, parent


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--label", required=True)
    ap.add_argument("--record-s", type=float, default=10.0)
    ap.add_argument("--mb", type=int, default=32, help="trace buffer for the traced recordings")
    ap.add_argument("--start-cost-mb", type=int, nargs="*", default=[32, 128, 512])
    ap.add_argument("--skip-scope", action="store_true")
    ap.add_argument("--plain-s", type=float, default=None, help="the rate recording's length (default: --record-s)")
    ap.add_argument("--traced-only", action="store_true", help="stop after the traced recording (checks 1-5)")
    args = ap.parse_args()

    checks: list[tuple[str, bool, str]] = []
    out: dict = {"label": args.label, "record_s": args.record_s, "mb": args.mb, "traced_only": args.traced_only}

    def check(name: str, cond: bool, got: str = "") -> None:
        checks.append((name, bool(cond), got))
        say(f"  {'ok  ' if cond else 'FAIL'}  {name}" + (f"   ({got})" if got else ""))

    try:
        c = PipeClient().connect()
    except PipeError as e:
        say(f"pipe not usable: {e}")
        return 2
    try:
        # 1. the plain rate
        _, _ = record(c, args.plain_s if args.plain_s is not None else args.record_s)
        plain = data_of(c.request("pe_profile_get", limit=32768, skip_per_frame=True, include_unloaded=True))
        total, window_ms = plain.get("total_calls", 0), plain.get("window_ms", 0)
        if total == 0 or not window_ms:
            say(f"no calls recorded (total {total}, window_ms {window_ms}): is the game running and the hook up?")
            return 2
        rate = total / (window_ms / 1000.0)
        per_frame = {addr(a) for a in plain.get("per_frame_funcs", [])}
        out["plain"] = {"total_calls": total, "window_ms": window_ms, "calls_per_s": rate,
                        "distinct": plain.get("distinct_funcs"), "per_frame": len(per_frame)}
        say(f"rate: {rate:,.0f} calls/s ({total:,} calls over {window_ms:,} ms; {len(per_frame)} per-frame)")
        if "unloaded_funcs" in plain:   # build 3634+: the table's own count of what is no longer at its address
            out["plain"]["unloaded"] = {k: plain.get(k) for k in
                                        ("unloaded_funcs", "unloaded_calls", "unnamed_funcs", "unnamed_calls")}
            say(f"table: {plain['unloaded_funcs']} functions unloaded since they fired ({plain['unloaded_calls']:,} "
                f"calls), {plain['unnamed_funcs']} with no name ({plain['unnamed_calls']:,} calls)")
        # What fired besides the per-frame functions: the first thing to read when a later check finds nothing.
        rest = [(f.get("count", 0), f"{f.get('class_name')}::{f.get('func_name')}") for f in plain.get("functions", [])]
        out["plain"]["not_per_frame"] = [{"count": n, "func": f} for n, f in rest]
        for n, f in rest[:15]:
            say(f"    {n:>8,}  {f}")
        check("window_ms is sent and the rate is positive", window_ms > 0 and rate > 0, f"{rate:,.0f}/s")

        # 2-5. every call, traced
        mb = args.mb << 20
        start, stop = record(c, args.record_s, {"bytes": mb, "ticked": [], "exclude_per_frame": False})
        table = data_of(c.request("pe_profile_get", limit=1))
        info = stop.get("trace", {})
        out["traced_start_s"] = start["_start_s"]
        out["traced_info"] = info
        # The DLL reports what it allocated: whole 40-byte records.
        check("the traced Start answered the trace object",
              start.get("trace", {}).get("bytes") == (mb // REC.size) * REC.size,
              str(start.get("trace", {}).get("bytes")))
        check("Stop answered the trace, quiesced", info.get("allocated") and info.get("quiesced"), json.dumps(info))
        written, first = info.get("written", 0), info.get("first_valid", 0)
        calls = table.get("total_calls", 0)
        lapped = first > 0
        ratio = written / (2 * calls) if calls else 0
        out["traced_table_calls"] = calls
        check("records ~= 2 x the table's calls (entry + return each)" if not lapped
              else "the ring lapped: written > its capacity",
              (0.95 <= ratio <= 1.05) if not lapped else written > info.get("capacity", 0),
              f"written {written:,}, calls {calls:,}, ratio {ratio:.3f}")

        head, recs, read_s, moved = read_ring(c)
        out["read"] = {"records": len(recs), "seconds": read_s, "wire_bytes": moved,
                       "mb_per_s": (moved / 1e6) / read_s if read_s > 0 else None}
        say(f"read {len(recs):,} records in {read_s:.2f} s ({moved / 1e6:.1f} MB on the wire)")
        check("the pages hand back the whole kept window", len(recs) == written - first, f"{len(recs):,}")
        seq_ok = all((r[0] & ~RET_BIT) == first + i for i, r in enumerate(recs))
        check("every kept record is where its sequence number says", seq_ok)
        entries = {r[0] for r in recs if not r[0] & RET_BIT}
        rets = [r for r in recs if r[0] & RET_BIT]
        ret_ok = all(r[2] < (r[0] & ~RET_BIT) for r in rets)
        check("every return points at an earlier record", ret_ok)
        paired = sum(1 for r in rets if r[2] in entries)
        check("returns pair with kept entries (all but the calls begun before the window)",
              paired >= len(rets) - 64 if lapped else paired == len(rets), f"{paired:,} of {len(rets):,}")

        fn, fn_s = names(c, "funcs")
        ob, ob_s = names(c, "objs")
        out["names"] = {"funcs": len(fn), "funcs_s": fn_s, "objs": len(ob), "objs_s": ob_s,
                        "funcs_live": sum(1 for x in fn if x.get("live")),
                        "funcs_unloaded": sum(1 for x in fn if x.get("unloaded")),
                        "funcs_named": sum(1 for x in fn if x.get("func_name")),
                        "objs_live": sum(1 for x in ob if x.get("live"))}
        ring_funcs = {r[2] for r in recs if not r[0] & RET_BIT}
        named = {addr(x["addr"]) for x in fn}
        check("names cover every function the ring holds", ring_funcs <= named,
              f"{len(ring_funcs)} in the ring, {len(named)} named")
        # Live, or unloaded since it fired and named from its first call: either way the tab shows a name. Before
        # build 3634 an unloaded function had none (Avowed, a 512 MB recording: 184 of 796).
        check("every function has a name (live, or unloaded and named from its first call)",
              out["names"]["funcs_named"] >= 0.99 * len(fn),
              f"{out['names']['funcs_named']} of {len(fn)}: {out['names']['funcs_live']} live, "
              f"{out['names']['funcs_unloaded']} unloaded")
        say(f"names: {len(fn)} functions in {fn_s:.2f} s, {len(ob)} objects in {ob_s:.2f} s "
            f"({out['names']['objs_live']} live)")
        # A function no longer at its address was unloaded before Stop (a closed UI or a map change frees its
        # Blueprint classes). Since build 3634 it keeps the name read at its first call; one with no name at all shows
        # as a bare address. How many calls each kind is, and when each last fired -- alive then; it went at some
        # point after, which a last call cannot date.
        for label, dead in (("unloaded, named", {addr(x["addr"]) for x in fn
                                                 if not x.get("live") and x.get("func_name")}),
                            ("unnamed", {addr(x["addr"]) for x in fn if not x.get("live") and not x.get("func_name")})):
            if not dead:
                continue
            freq = info.get("qpc_freq") or 1
            t0 = min((r[1] for r in recs), default=0)
            n_dead, last = 0, {}
            for seq_kind, ticks, a, _b, _tid, _f in recs:
                if not seq_kind & RET_BIT and a in dead:
                    n_dead += 1
                    last[a] = max(last.get(a, 0), ticks)
            n_all = sum(1 for r in recs if not r[0] & RET_BIT)
            lasts = sorted((t - t0) / freq for t in last.values())
            window = (max((r[1] for r in recs), default=0) - t0) / freq
            out["names"][label] = {"funcs": len(dead), "calls": n_dead, "of_calls": n_all,
                                   "last_fired_s": [lasts[0], lasts[-1]] if lasts else None, "window_s": window}
            say(f"{label}: {len(dead)} functions, {n_dead:,} of {n_all:,} calls ({n_dead / max(1, n_all):.2%}); "
                + (f"each last fired between {lasts[0]:.1f} s and {lasts[-1]:.1f} s of a {window:.1f} s window"
                   if lasts else "none of them in the kept window"))

        func_of, parent_of = tree_children(recs)
        nested = collections.Counter(func_of[p] for s, p in parent_of.items() if p is not None)
        out["nesting"] = {"entries": len(func_of), "with_parent": sum(1 for p in parent_of.values() if p is not None),
                          "callers": len(nested)}

        c.request("pe_trace_release")
        after = c.request("pe_trace_get", **{"from": 0, "max": 1})
        check("release frees the ring; a read finds nothing", not after.get("allocated") and after.get("count") == 0)

        # 6. the ticked scope: tick the function that calls the most other UFunctions
        if not (args.skip_scope or args.traced_only):
            if nested:
                root_func, _n = nested.most_common(1)[0]
                say(f"scope: ticking 0x{root_func:X} ({_n} nested calls in the unscoped trace)")
                _, stop2 = record(c, args.record_s, {"bytes": mb, "ticked": [f"0x{root_func:X}"]})
                _, srecs, _, _ = read_ring(c)
                sfunc, sparent = tree_children(srecs)
                roots = [s for s, p in sparent.items() if p is None]
                flags = {r[0]: r[5] for r in srecs if not r[0] & RET_BIT}
                check("scope: every root is the ticked function, flagged as the scope root",
                      roots and all(sfunc[s] == root_func and flags[s] & SCOPE_ROOT for s in roots),
                      f"{len(roots)} roots, {len(sfunc)} entries")
                check("scope: it holds calls nested under the ticked one",
                      any(p is not None for p in sparent.values()))
                check("scope: far fewer entries than the unscoped trace", len(sfunc) < len(func_of),
                      f"{len(sfunc):,} vs {len(func_of):,}")
                out["scope"] = {"root": f"0x{root_func:X}", "entries": len(sfunc), "roots": len(roots),
                                "info": stop2.get("trace")}
                c.request("pe_trace_release")
            else:
                check("scope: a function with nested calls exists to tick", False, "none in the unscoped trace")

        if not args.traced_only:
            # 7. per-frame exclusion: the previous table (the scope recording, or the traced one) decides
            prev = data_of(c.request("pe_profile_get", limit=1, skip_per_frame=True))
            excl = {addr(a) for a in prev.get("per_frame_funcs", [])}
            start3, stop3 = record(c, args.record_s, {"bytes": mb, "exclude_per_frame": True})
            _, erecs, _, _ = read_ring(c)
            efuncs = {r[2] for r in erecs if not r[0] & RET_BIT}
            check("exclude: the trace says how many it left out", start3.get("trace", {}).get("excluded") == len(excl),
                  f"{start3.get('trace', {}).get('excluded')} vs {len(excl)}")
            check("exclude: none of the previous table's per-frame functions is an entry",
                  excl and not (efuncs & excl), f"{len(excl)} excluded, {len(efuncs & excl)} still present")
            c.request("pe_trace_release")

            # 8. refusal
            bad = c.request("pe_profile_start", trace={"bytes": 3 << 20})
            check("a buffer outside 32..512 MB / not a power of two is refused", not ok_of(bad), str(bad)[:120])
            recording = data_of(c.request("pe_profile_get", limit=1)).get("recording")
            check("...and starts nothing", recording is False)

            # 9. Start cost per size
            cost = {}
            for m in args.start_cost_mb:
                s, _ = record(c, 0.2, {"bytes": m << 20})
                cost[m] = s["_start_s"]
                c.request("pe_trace_release")
            out["start_cost_s"] = cost
            say("traced Start: " + ", ".join(f"{m} MB {s * 1000:.0f} ms" for m, s in cost.items()))
    finally:
        try:
            c.request("pe_profile_stop")
            c.request("pe_trace_release")
        finally:
            c.close()

    out["checks"] = [{"name": n, "ok": o, "got": g} for n, o, g in checks]
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    (OUT_DIR / f"{args.label}.json").write_text(json.dumps(out, indent=2), encoding="utf-8")
    failed = [n for n, o, _ in checks if not o]
    say(f"\n{len(checks) - len(failed)} of {len(checks)} checks hold" + (f"; FAILED: {failed}" if failed else ""))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
