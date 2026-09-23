#!/usr/bin/env python3
r"""L63 `[W5-INSTEXPORT-TRUNC]`: inflate the live DumperTestActor past the 60,000-entry CE XML cap, then count it.

    py tools/verify/l63_instexport.py counts                  (pipe, one slot: Map/Arr_Churn, SpawnedHolders, Children)
    py tools/verify/l63_instexport.py grow  [--count 16384]   (pipe: V1a_GrowContainers(count) -- Map_Churn + Arr_Churn)
    py tools/verify/l63_instexport.py spawn [--count 4096]    (pipe: Spawn_Holders(count, false) -- SpawnedHolders + Children)
    py tools/verify/l63_instexport.py clip  [--save out\x.xml] (no pipe: count <CheatEntry> in the clipboard text)

Both UFUNCTIONs are UNCLAMPED on DumperTest (DumperTestActor.cpp V1a_GrowContainers / Spawn_Holders), and
Spawn_Holders spawns with Owner = this, so it feeds AActor::Children as well as SpawnedHolders. The recipe's
source arithmetic at Array Limit 16384: Map_Churn 1 + 3 x 16,384, each 4,096-capped array 1 + 4,096, plus ~651 other
entries, so ~62.1k after both steps and ~53.9k after `grow` alone (the near-miss control). `counts` prints what the
DLL itself walks at array_limit=16384 so the prediction is checked BEFORE the export, not inferred after it.

`grow`/`spawn`/`counts` take ONE pipe slot: the UI holds two of three, so never run two of these at once.
`clip` reads CF_UNICODETEXT through user32 (the payload is ~15-20 MB, far too big to print) and reports the
<CheatEntry> count, the byte size and the first/last 200 characters; it never opens the pipe.
"""
import argparse
import ctypes
import os
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

WATCH = ("Map_Churn", "Arr_Churn", "SpawnedHolders", "Children")


def counts(c, act):
    w = c.request("walk_instance", addr=act["addr"], array_limit=16384, preview_limit=0)
    seen = {}
    for f in w.get("fields") or []:
        if f.get("name") in WATCH:
            d = {k: f.get(k) for k in ("type", "count", "map_count") if f.get(k) is not None}
            for k in ("elements", "map_elements"):
                if isinstance(f.get(k), list):
                    d[k + "_returned"] = len(f[k])
            seen[f["name"]] = d
    for n in WATCH:
        print("   %-15s %s" % (n, seen.get(n, "<not walked>")))
    return seen


def pipe_phase(a):
    from pipe_client import PipeClient
    from ad4_contested import find_live_actor, invoke
    with PipeClient() as c:
        print("build %s" % c.assert_build())
        c.ensure_scanned()
        act = find_live_actor(c)
        print("actor %s %s" % (act["addr"], act.get("name")))
        if a.phase in ("grow", "spawn"):
            fns = {f["name"]: f for f in c.request("walk_functions", addr=act["class_addr"])["functions"]}
            fn = "V1a_GrowContainers" if a.phase == "grow" else "Spawn_Holders"
            ps = fns[fn]["parms_size"]
            hexs = a.count.to_bytes(4, "little").hex() + "00" * (ps - 4)
            print("%s(%d) parms_size %d params_hex %s -> result %s"
                  % (fn, a.count, ps, hexs, invoke(c, act["addr"], fn, parms_size=ps, params_hex=hexs).get("result")))
            time.sleep(1.5)
        counts(c, act)
    return 0


def clip(a):
    u32 = ctypes.WinDLL("user32", use_last_error=True)
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    u32.GetClipboardData.restype = ctypes.c_void_p
    k32.GlobalLock.argtypes = [ctypes.c_void_p]
    k32.GlobalLock.restype = ctypes.c_void_p
    k32.GlobalUnlock.argtypes = [ctypes.c_void_p]
    if not u32.OpenClipboard(None):
        raise SystemExit("FAIL: OpenClipboard -> Win32 %d" % ctypes.get_last_error())
    try:
        h = u32.GetClipboardData(13)   # CF_UNICODETEXT
        if not h:
            raise SystemExit("FAIL: no CF_UNICODETEXT on the clipboard")
        p = k32.GlobalLock(h)
        try:
            text = ctypes.wstring_at(p)
        finally:
            k32.GlobalUnlock(h)
    finally:
        u32.CloseClipboard()
    n = text.count("<CheatEntry>")
    print("clipboard: %d chars, <CheatEntry> x %d" % (len(text), n))
    print("head: %r" % text[:200])
    print("tail: %r" % text[-200:])
    if a.save:
        with open(a.save, "w", encoding="utf-8", newline="") as f:
            f.write(text)
        print("saved -> %s" % a.save)
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("phase", choices=("counts", "grow", "spawn", "clip"))
    ap.add_argument("--count", type=int)
    ap.add_argument("--save")
    a = ap.parse_args()
    if a.phase == "clip":
        return clip(a)
    if a.count is None:
        a.count = 16384 if a.phase == "grow" else 4096
    return pipe_phase(a)


if __name__ == "__main__":
    sys.exit(main())
