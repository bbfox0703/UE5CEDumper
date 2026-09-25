"""Set a volume's free space to just above / just below a guard threshold with ONE filler file.

    py tools/verify/disk_squeeze.py plan    <dir>                 # volume, free, suggested N = floor(free GiB) - 1
    py tools/verify/disk_squeeze.py arm     <dir> --need-gb N     # free = N GiB + margin
    py tools/verify/disk_squeeze.py drop    <dir>                 # free = N GiB - margin (the N armed)
    py tools/verify/disk_squeeze.py release <dir>                 # delete the filler

For Review 7 rows R7-S8 / R7-D-06: the snapshot capture checks free space BEFORE it starts (Min free disk) and again
after every chunk it writes. Arm leaves the pre-capture guard passing; drop, while the capture is parked, makes the
post-write poll trip. <dir> is any path on the volume (the Snapshots folder). The filler lives on that volume in
%LOCALAPPDATA%\\Temp (same volume as the snapshots on this machine) and its state in out/r7live/disk_squeeze.json.

The filler is extended with SetEndOfFile, which on NTFS allocates real (non-sparse) clusters without writing them, so
free space drops at once. Every verb prints free - need. It REFUSES a filler over 3 GiB, and `release` is safe to run
first thing after a crash.
"""
import argparse
import ctypes
import json
import os
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
GIB, MIB = 1 << 30, 1 << 20
FILLER = os.path.join(os.environ["LOCALAPPDATA"], "Temp", "r7_disk_squeeze.fill")
STATE = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
                     "out", "r7live", "disk_squeeze.json")
MAX_FILLER = 3 * GIB


def free_bytes(path):
    fb = ctypes.c_ulonglong()
    if not ctypes.windll.kernel32.GetDiskFreeSpaceExW(ctypes.c_wchar_p(path), ctypes.byref(fb), None, None):
        raise SystemExit(f"GetDiskFreeSpaceExW({path}) failed")
    return fb.value


def same_volume(a, b):
    return os.path.splitdrive(os.path.abspath(a))[0].lower() == os.path.splitdrive(os.path.abspath(b))[0].lower()


def set_free(path, target):
    cur = os.path.getsize(FILLER) if os.path.exists(FILLER) else 0
    want = cur + (free_bytes(path) - target)
    if want < 0:
        raise SystemExit(f"cannot reach {target / GIB:.3f} GiB free: it is already below that with no filler")
    if want > MAX_FILLER:
        raise SystemExit(f"REFUSED: the filler would be {want / GIB:.2f} GiB (> 3 GiB). Pick a larger N.")
    with open(FILLER, "ab") as f:
        f.truncate(want)
    for _ in range(50):          # the free-space counter can lag the allocation by a moment
        fr = free_bytes(path)
        if abs(fr - target) < 4 * MIB:
            break
        time.sleep(0.1)
    fr = free_bytes(path)
    print(f"filler {want / MIB:.0f} MiB  free {fr / GIB:.4f} GiB  target {target / GIB:.4f} GiB  "
          f"free-target {(fr - target) / MIB:+.1f} MiB")


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("verb", choices=("plan", "arm", "drop", "release"))
    ap.add_argument("dir")
    ap.add_argument("--need-gb", type=int, default=None)
    ap.add_argument("--margin-mb", type=int, default=64)
    a = ap.parse_args()
    if not same_volume(a.dir, FILLER):
        raise SystemExit(f"REFUSED: {a.dir} is not on the filler's volume ({FILLER})")
    fr = free_bytes(a.dir)
    if a.verb == "plan":
        n = int(fr // GIB) - 1
        print(f"volume {os.path.splitdrive(os.path.abspath(a.dir))[0]}  free {fr / GIB:.3f} GiB  "
              f"filler {'present' if os.path.exists(FILLER) else 'absent'}  suggested N = {n}")
    elif a.verb == "arm":
        if a.need_gb is None:
            raise SystemExit("arm needs --need-gb N")
        os.makedirs(os.path.dirname(STATE), exist_ok=True)
        json.dump({"need_gb": a.need_gb, "margin_mb": a.margin_mb}, open(STATE, "w"))
        set_free(a.dir, a.need_gb * GIB + a.margin_mb * MIB)
    elif a.verb == "drop":
        st = json.load(open(STATE))
        set_free(a.dir, st["need_gb"] * GIB - st["margin_mb"] * MIB)
    else:
        if os.path.exists(FILLER):
            os.remove(FILLER)
        print(f"filler released; free {free_bytes(a.dir) / GIB:.3f} GiB")
    return 0


if __name__ == "__main__":
    sys.exit(main())
