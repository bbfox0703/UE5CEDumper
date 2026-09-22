#!/usr/bin/env python3
r"""L50 `[A2-LAZY-LATCH-GUESS]`: TArray<TLazyObjectPtr> stride = the engine's own ElementSize, even when the UE
version is (mis)read as 5.0-5.2.

    py tools/verify/l50_lazy_override.py --log-dir "%LOCALAPPDATA%\UE5CEDumper\Logs\DumperTest-Win64-Shipping" --pe <PE hash>

The literal host (a real UE 5.0-5.2 title) does not discriminate: correctly resolved, its guess (0x1C) and its
raw ElementSize (0x1C) agree. The defect is a version MIS-resolved across the 5.2/5.3 line, and DumperTest 5.4
(raw 0x18) is pushed there IN-PROCESS with `set_ue_version_override {version: 502, persist: false}`: pre-fix,
InferScalarSize's <=5.2 guess 0x1C then overrode the raw 0x18 and Arr_LazyPtr read at a 28-byte stride.

  CONTROL  at 504: Arr_LazyPtr = 3 elements, array_elem_size 24, three distinct GUIDs with resolved holder names.
  ARM      after the override: the SAME -- 24, the same GUIDs and names -- and offsets-0.log after the override
           marker carries exactly ONE "TLazyObjectPtr payload envelope measured: +0x08 (ElementSize 0x18 ...
           UEver=502)". The hint cache's entry for this PE must show no persisted override, before and after.

⚠ persist:false only: a persisted override is re-applied on every launch. Kill the game afterwards -- the
override also leaves every other version-derived path thinking 502. Run with the UI closed.
"""
import argparse
import json
import os
import re
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pipe_client import PipeClient   # noqa: E402

fails = []
CACHE = os.path.join(os.environ["LOCALAPPDATA"], "UE5CEDumper", "UE5CEDumper.%s.json" % os.environ.get("COMPUTERNAME", ""))


def cache_entry(pe):
    try:
        g = json.load(open(CACHE, encoding="utf-8")).get("games", {})
    except Exception as e:
        return "unreadable: %s" % e
    e = g.get(pe)
    return None if e is None else {k: e.get(k) for k in ("ueVersion", "ueVersionUserOverrideAt", "versionDetected")}


def lazy(c, actor):
    w = c.request("walk_instance", addr=actor, array_limit=8)
    f = next((x for x in (w.get("fields") or []) if x.get("name") == "Arr_LazyPtr"), None)
    if not f:
        return None, None, []
    return f.get("array_elem_size"), f.get("count"), [e.get("v") for e in (f.get("elements") or [])]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--log-dir", required=True)
    ap.add_argument("--pe", required=True, help="the package's PE hash as the hint cache keys it")
    a = ap.parse_args()
    offlog = os.path.join(os.path.expandvars(a.log_dir), "offsets-0.log")
    before = cache_entry(a.pe)
    print("cache entry for %s before: %s" % (a.pe, before))
    with PipeClient() as c:
        print("build %s" % c.assert_build())
        c.ensure_scanned()
        ini = c.request("init")
        print("init: ue_version=%s is_user_override=%s" % (ini.get("ue_version"), ini.get("is_user_override")))
        act = next((i["addr"] for i in (c.request("find_instances", class_name="DumperTestActor", limit=10,
                                                  exact_match=True).get("instances") or [])
                    if not i["name"].startswith("Default__")), None)
        if not act:
            print("FAIL: no live DumperTestActor")
            return 2
        es0, n0, v0 = lazy(c, act)
        print("\nCONTROL (504): Arr_LazyPtr elem_size=%s count=%s" % (es0, n0))
        for v in v0:
            print("   " + str(v))
        guids0 = [re.search(r"\{[0-9A-F-]+\}", str(v)).group(0) if re.search(r"\{[0-9A-F-]+\}", str(v)) else None for v in v0]
        if es0 != 24 or n0 != 3 or len(set(guids0)) != 3 or None in guids0:
            fails.append("control: not 3 distinct GUIDs at a 24-byte stride -- the arm would be vacuous")

        mark = os.path.getsize(offlog) if os.path.exists(offlog) else 0
        r = c.request("set_ue_version_override", version=502, persist=False)
        print("\nset_ue_version_override 502 persist:false -> ue_version=%s is_user_override=%s persisted=%s ok=%s"
              % (r.get("ue_version"), r.get("is_user_override"), r.get("persisted"), r.get("ok")))
        if r.get("ue_version") != 502 or r.get("persisted"):
            fails.append("override reply is not {502, not persisted}")
        es1, n1, v1 = lazy(c, act)
        print("ARM (502): Arr_LazyPtr elem_size=%s count=%s" % (es1, n1))
        for v in v1:
            print("   " + str(v))
        if es1 != 24:
            fails.append("arm: elem_size %s, not the engine's 24" % es1)
        if v1 != v0:
            fails.append("arm: the elements differ from the control")
        time.sleep(1.0)
        txt = open(offlog, "rb").read()[mark:].decode("utf-8", "replace") if os.path.exists(offlog) else ""
        lines = [l.strip() for l in txt.splitlines() if "TLazyObjectPtr payload envelope measured" in l]
        print("offsets-0.log after the override: %d lazy line(s)" % len(lines))
        for l in lines:
            print("   " + l[:170])
        if len(lines) != 1 or "ElementSize 0x18" not in lines[0] or "UEver=502" not in lines[0]:
            fails.append("arm: want exactly one lazy line with 'ElementSize 0x18' and 'UEver=502'")
    after = cache_entry(a.pe)
    print("\ncache entry for %s after: %s" % (a.pe, after))
    if isinstance(after, dict) and after.get("ueVersionUserOverrideAt"):
        fails.append("a persisted override appeared in the hint cache")
    for f in fails:
        print("FAIL: " + f)
    print("L50: %s" % ("PASS" if not fails else "FAIL"))
    return 0 if not fails else 1


if __name__ == "__main__":
    sys.exit(main())
