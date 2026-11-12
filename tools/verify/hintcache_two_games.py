"""Register [PIPE-PER-PROCESS-2026-11-06] item 11: the hint-cache lock across processes.

    py tools/verify/hintcache_two_games.py --pid A --pid B [--rounds 50] [--expect-loss]

Two injected games of DIFFERENT exes (one cache record per PE hash) write the one machine-wide hint cache
(%LOCALAPPDATA%\\UE5CEDumper\\UE5CEDumper.<COMPUTERNAME>.json) at the same moment, every round: game A persists
invoke_timeout_ms 7000+i, game B 9000+i, released together by a barrier. After each round the file must parse and hold
BOTH values: a lost update (one writer's read-modify-write overwriting the other's) shows as a stale value. The old
values are put back at the end (0 clears one that was never set).

Exit 0 when no round lost a value (or, with --expect-loss, for a negative control with a DLL from before the lock,
when at least one did).
"""
from __future__ import annotations

import argparse
import json
import os
import pathlib
import sys
import threading

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient   # noqa: E402


def cache_file() -> pathlib.Path:
    return pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / f"UE5CEDumper.{os.environ.get('COMPUTERNAME', '')}.json"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--pid", type=int, action="append", required=True)
    ap.add_argument("--rounds", type=int, default=50)
    ap.add_argument("--expect-loss", action="store_true")
    a = ap.parse_args()
    if len(a.pid) != 2:
        raise SystemExit("give exactly two --pid")
    clients = []
    for pid in a.pid:
        c = PipeClient(pid=pid, timeout=60)
        c.connect()
        print(f"pid {pid}: build {c.assert_build()}", flush=True)
        clients.append(c)
    pes = [c.request("get_pointers").get("pe_hash") for c in clients]
    print(f"PE hashes {pes}", flush=True)
    if pes[0] == pes[1]:
        raise SystemExit("FAIL: both games have the same PE hash -- two different exes are needed")
    path = cache_file()
    games = json.loads(path.read_text(encoding="utf-8")).get("games", {})
    old = [games.get(pe, {}).get("invokeTimeoutMs", 0) for pe in pes]
    print(f"cache {path}; the old invokeTimeoutMs {old}", flush=True)

    lost = []
    bad_parse = 0
    for i in range(a.rounds):
        want = [7000 + i, 9000 + i]
        replies = [None, None]
        barrier = threading.Barrier(2)

        def send(k: int):
            barrier.wait()
            replies[k] = clients[k].request("set_invoke_timeout", timeout_ms=want[k], persist=True)

        threads = [threading.Thread(target=send, args=(k,)) for k in range(2)]
        for t in threads:
            t.start()
        for t in threads:
            t.join()
        try:
            games = json.loads(path.read_text(encoding="utf-8")).get("games", {})
        except (OSError, json.JSONDecodeError) as e:
            bad_parse += 1
            print(f"  round {i}: the file did not parse: {e}", flush=True)
            continue
        got = [games.get(pe, {}).get("invokeTimeoutMs") for pe in pes]
        persisted = [bool(r and r.get("persisted")) for r in replies]
        # A loss is read from the file alone under --expect-loss: a DLL from before Stage C has no `persisted` key.
        ok = got == want and (a.expect_loss or all(persisted))
        if not ok:
            lost.append(i)
            print(f"  round {i}: want {want}, file {got}, persisted {persisted}", flush=True)
    print(f"{a.rounds} rounds: {len(lost)} lost, {bad_parse} unparsable", flush=True)

    for k, c in enumerate(clients):
        r = c.request("set_invoke_timeout", timeout_ms=old[k] or 0, persist=True)
        print(f"restored pid {a.pid[k]} to {old[k] or 0}: persisted {r.get('persisted')}", flush=True)
        c.close()
    if a.expect_loss:
        ok = bool(lost)
    else:
        ok = not lost and bad_parse == 0
    print("PASS" if ok else "FAIL", flush=True)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
