"""Collect the version-detection evidence for G8/G9 step 3, G2 step 2, G11 step 1.

    py run_version_evidence.py <LogFolderName>

Drives the injected DLL over the pipe (no UI), then reads the version fields
back out. The scan is what makes DetectVersion run; `init` alone returns cached
values and never scans (working-lessons 2.6 trap 2), and assert_build() refuses
to proceed against a stale proxy (trap 1).
"""
import json
import os
import pathlib
import sys
import time

sys.path.insert(0, r"D:\Github\UE5CEDumper\tools\verify")
from pipe_client import PipeClient  # noqa: E402

VERSION_FIELDS = ("ue_version", "ueVersion", "is_low_confidence", "is_user_override",
                  "is_version_too_old", "module_name", "module_base", "load_mode",
                  "build_number", "object_count", "gobjects", "gnames", "gworld",
                  "gobjects_method", "gnames_method", "gworld_method",
                  "gobjects_pattern_id", "gnames_pattern_id", "gworld_pattern_id",
                  "item_size", "item_layout_mode")


def main(argv):
    with PipeClient(timeout=600.0) as c:
        c.assert_build()
        print("build asserted OK")
        t0 = time.time()
        c.ensure_scanned()
        print(f"ensure_scanned returned after {time.time() - t0:.1f}s")
        p = c.request("get_pointers")
        out = {k: p.get(k) for k in VERSION_FIELDS if k in p}
        print("\n=== pointers / version ===")
        print(json.dumps(out, indent=2, ensure_ascii=False))
        try:
            o = c.request("get_offsets")
            keep = {k: v for k, v in o.items()
                    if any(s in k.lower() for s in ("version", "probe", "cpn", "case"))}
            print("\n=== offsets (version-ish keys) ===")
            print(json.dumps(keep, indent=2, ensure_ascii=False))
        except Exception as exc:  # noqa: BLE001
            print(f"get_offsets failed: {exc}")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main(sys.argv[1:]))
