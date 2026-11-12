"""Register [PIPE-PER-PROCESS-2026-11-06] item 16: the MCP read tools on a large game (300,000+ objects).

    py tools/verify/mcp_large.py --exe dist/UE5DumpUI.exe --pid P [--min-objects 300000]

One MCP session, each call timed: read_status (the object count must be at least --min-objects), read_list_classes
unfiltered and filtered, read_find_instances of Actor, read_list_functions filtered and unfiltered, and a value scan
(begin, then end with its session id). A reply that did not fit comes back cut, with a note in a second block (or,
cut to a stub, as an error naming why); the unfiltered calls at limit 2000 are expected to come back cut, the filtered
ones not. Exit 0 when nothing failed, nothing timed out, and at least one oversized reply came back cut and readable.
"""
from __future__ import annotations

import argparse
import json
import pathlib
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from mcp_client import McpClient, McpError, payload   # noqa: E402

sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--exe", required=True)
    ap.add_argument("--pid", type=int, required=True)
    ap.add_argument("--min-objects", type=int, default=300000)
    a = ap.parse_args()
    c = McpClient(str(pathlib.Path(a.exe).resolve()))
    c.initialize()
    fails: list[str] = []
    cut_seen = False

    def run(tool: str, args: dict, expect_cut: bool | None, timeout: float = 300.0):
        nonlocal cut_seen
        t0 = time.monotonic()
        try:
            r = c.call(tool, {"pid": a.pid, **args}, timeout)
        except McpError as e:
            fails.append(f"{tool}: {e}")
            print(f"  FAIL  {tool} {args}: {e}", flush=True)
            return None
        dt = time.monotonic() - t0
        data, note = payload(r)
        err = bool(r.get("isError"))
        if isinstance(data, dict) and note is None and data.get("note"):
            note = data.get("note")
        size = len(r["content"][0]["text"]) if r.get("content") else 0
        cut = note is not None and ("cut" in note.lower() or "fit" in note.lower() or "truncat" in note.lower())
        cut_seen |= cut and not err
        verdict = "ok"
        if err and not cut:
            verdict = "FAIL"
            fails.append(f"{tool} {args}: error {json.dumps(data)[:200]}")
        elif expect_cut is False and cut:
            verdict = "FAIL"
            fails.append(f"{tool} {args}: cut although filtered")
        print(f"  {verdict:4}  {tool} {json.dumps(args)}: {dt:6.2f} s, {size:,} chars, isError {err}, "
              f"cut {cut}{(' -- ' + note[:160]) if note else ''}", flush=True)
        return data

    st = run("read_status", {}, None)
    count = (st or {}).get("object_count") or 0
    print(f"        object_count {count:,}, build {(st or {}).get('build_number')}", flush=True)
    if count < a.min_objects:
        print(f"FAIL: {count:,} objects, below {a.min_objects:,}: not a large game (load a save?)")
        c.close()
        return 1
    run("read_list_classes", {"game_only": False, "limit": 2000}, None)
    run("read_list_classes", {"name_filter": "Character", "limit": 100}, False)
    run("read_find_instances", {"class_name": "Actor", "limit": 2000}, None)
    # Not held to "uncut": on EVERSPACE 2 "Health" matched 150 functions, more than one reply holds (measured
    # 2026-11-12), and the cut with its note is the right answer then.
    run("read_list_functions", {"name_filter": "Health", "limit": 200}, None)
    run("read_list_functions", {"game_only": False, "limit": 2000}, None)
    vs = run("read_value_scan_begin", {"data_type": "Int32", "value": "100", "page_size": 500}, None)
    if isinstance(vs, dict) and vs.get("session_id") is not None:
        print(f"        value scan: total {vs.get('total')}, {vs.get('duration_ms')} ms, deadline_hit "
              f"{vs.get('deadline_hit')}", flush=True)
        if vs.get("deadline_hit"):
            fails.append("read_value_scan_begin: deadline hit")
        run("read_value_scan_end", {"session_id": vs["session_id"]}, False)
    c.close()
    for e in c.framing_errors:
        fails.append("framing: " + e)
    if not cut_seen:
        fails.append("no oversized reply came back cut")
    print(f"\n{'PASS' if not fails else 'FAIL'}: {len(fails)} failed" + "".join("\n  " + f for f in fails), flush=True)
    return 0 if not fails else 1


if __name__ == "__main__":
    sys.exit(main())
