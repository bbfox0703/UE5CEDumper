"""AF21 — a legitimately-placed HiDPI window must survive a restart, and a genuinely off-screen
one must still be reset.

    py tools/verify/af21_hidpi_placement.py            # run all three arms
    py tools/verify/af21_hidpi_placement.py --probe    # just print the machine's numbers

WHAT THE FIX WAS. `MainWindow.OnOpenedValidatePlacement` asks
`WindowPlacement.IsVisibleEnough(rx, ry, rw, rh, screens)` whether the restored rect is reachable.
`IsVisibleEnough` is pure and already unit-tested; the defect was one line ABOVE it —

    int rw = (int)Math.Round(_normalWidth * scale);      // <- the `* scale` is the fix

Without the conversion the guard measured the window at its DIP width. At this machine's 225%
scaling that is 1,124 px against a real 2,529, so a window hanging off the LEFT could be judged
off-screen while a third of it was plainly visible — and its saved position was then thrown away.

⭐ TWO THINGS THE ROW GETS WRONG, both re-derived here rather than inherited:

 1. **"Needs a real scaling change — the one row here a script cannot do."** False. The machine is
    already at **216 dpi = 225%**. The HiDPI condition is not something to arrange; it is the
    standing state of this desktop. Nothing needs changing.

 2. **"Move the window so roughly a third of it hangs off the RIGHT edge."** That step cannot
    expose this defect, and the arithmetic says so. Off the right, the overlap is
    `screenW - x` post-fix and `min(x+dipW, screenW) - x` pre-fix — the pre-fix value is the
    LARGER one, so the pre-fix code is *more* permissive there and both accept. The defect only
    appears off the LEFT, where the width is what carries the rect back onto the screen. A tester
    following the row would have seen a pass and learned nothing.

WHY NO WINDOW-DRAGGING. The persisted `window-state.txt` IS the input to the code under test, so
seeding it and observing where the window opens exercises the same path a drag would, without
`SetWindowPos` (whose result Avalonia may or may not observe) and without computer-use.

THE DISCRIMINATING BAND, derived not assumed, for width W_dip at scale S on a screen 0..SW:
    post-fix accepts while  x >= MinVisibleWidth - W_dip*S
    pre-fix  accepts while  x >= MinVisibleWidth - W_dip
so any x in [120 - W_dip*S, 120 - W_dip) is kept by the fixed build and discarded by the old one.
"""
import argparse
import ctypes
import os
import pathlib
import shutil
import subprocess
import sys
import time
from ctypes import wintypes

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

MIN_VISIBLE_W = 120          # WindowPlacement.MinVisibleWidth
STATE = pathlib.Path(os.path.expandvars(r"%LOCALAPPDATA%\UE5CEDumper\window-state.txt"))
BACKUP = pathlib.Path("out/af21/window-state.backup.txt")
UI = pathlib.Path("dist/UE5DumpUI.exe")

u32 = ctypes.WinDLL("user32", use_last_error=True)
try:
    ctypes.WinDLL("shcore").SetProcessDpiAwareness(2)
except Exception:
    u32.SetProcessDPIAware()


class RECT(ctypes.Structure):
    _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long),
                ("right", ctypes.c_long), ("bottom", ctypes.c_long)]


def ui_window():
    """(hwnd, RECT) of the main UE5DumpUI window, or (None, None)."""
    found = []

    @ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    def cb(h, _l):
        n = u32.GetWindowTextLengthW(h)
        if n and u32.IsWindowVisible(h):
            b = ctypes.create_unicode_buffer(n + 1)
            u32.GetWindowTextW(h, b, n + 1)
            if b.value.startswith("UE5 Dump UI"):
                found.append(h)
        return True

    u32.EnumWindows(cb, 0)
    if not found:
        return None, None
    r = RECT()
    u32.GetWindowRect(found[0], ctypes.byref(r))
    return found[0], r


def screen():
    return (0, 0, u32.GetSystemMetrics(78), u32.GetSystemMetrics(79))


def scaling():
    hdc = u32.GetDC(0)
    dpi = ctypes.windll.gdi32.GetDeviceCaps(hdc, 88)
    u32.ReleaseDC(0, hdc)
    return dpi / 96.0


def write_state(x, y, w, h, maximized):
    STATE.parent.mkdir(parents=True, exist_ok=True)
    STATE.write_text(
        "# UE5CEDumper main window state" + chr(10)
        + ("x=%d" % x) + chr(10) + ("y=%d" % y) + chr(10)
        + ("w=%s" % w) + chr(10) + ("h=%s" % h) + chr(10)
        + ("max=%d" % (1 if maximized else 0)) + chr(10),
        encoding="utf-8")


def kill_ui():
    for hwnd, _ in [ui_window()]:
        if hwnd:
            u32.PostMessageW(hwnd, 0x0010, 0, 0)   # WM_CLOSE
    for _ in range(30):
        if ui_window()[0] is None:
            return True
        time.sleep(0.5)
    subprocess.run(["taskkill", "/IM", "UE5DumpUI.exe", "/F"],
                   capture_output=True, text=True)
    time.sleep(2)
    return ui_window()[0] is None


def launch_and_read(timeout=40):
    subprocess.Popen([str(UI)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    deadline = time.time() + timeout
    while time.time() < deadline:
        hwnd, r = ui_window()
        if hwnd:
            time.sleep(2.5)                        # let OnOpenedValidatePlacement run
            _, r = ui_window()
            return r
        time.sleep(0.5)
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--probe", action="store_true")
    a = ap.parse_args()

    sw = screen()[2]
    s = scaling()
    W_DIP, H_DIP = 1124.0, 991.1111111111111
    w_phys = round(W_DIP * s)
    band_lo = int(MIN_VISIBLE_W - w_phys)          # fixed build still accepts at/above this
    band_hi = int(MIN_VISIBLE_W - W_DIP)           # old build stopped accepting below this

    print("screen width      : %d px" % sw)
    print("scaling           : %.2fx (%d dpi)" % (s, round(s * 96)))
    print("window            : %.0f DIP -> %d physical px" % (W_DIP, w_phys))
    print("post-fix accepts x >= %d" % band_lo)
    print("pre-fix  accepts x >= %d" % band_hi)
    print("DISCRIMINATING BAND: %d <= x < %d" % (band_lo, band_hi))
    if band_lo >= band_hi:
        print("⛔ no band at this scaling — the arms below cannot discriminate. NOT RUNNABLE.")
        return 2
    if a.probe:
        return 0
    if not UI.exists():
        print("⛔ dist/UE5DumpUI.exe missing")
        return 2

    x_witness = (band_lo + band_hi) // 2           # deep inside the band, clear of both edges
    x_offscreen = band_lo - 400                    # genuinely off-screen: both builds must reset
    x_onscreen = 200                               # plainly visible: both builds must keep

    arms = [
        ("B  WITNESS   (in band)", x_witness, "keep"),
        ("C  NEGATIVE  (off-screen)", x_offscreen, "reset"),
        ("A  POSITIVE  (on-screen)", x_onscreen, "keep"),
    ]

    had = STATE.exists()
    BACKUP.parent.mkdir(parents=True, exist_ok=True)
    if had:
        shutil.copy2(STATE, BACKUP)
        print("backed up window-state.txt -> %s" % BACKUP)

    results = []
    try:
        kill_ui()
        for label, x, expect in arms:
            write_state(x, 146, W_DIP, H_DIP, False)
            r = launch_and_read()
            if r is None:
                results.append((label, x, expect, None, "VOID (window never appeared)"))
                kill_ui()
                continue
            kept = abs(r.left - x) <= 4
            got = "kept at %d" % r.left if kept else "moved to %d" % r.left
            ok = kept if expect == "keep" else (not kept)
            results.append((label, x, expect, ok, got))
            print("  %-28s x=%-7d expect %-5s -> %-22s %s"
                  % (label, x, expect, got, "PASS" if ok else "FAIL"))
            kill_ui()
    finally:
        if had:
            shutil.copy2(BACKUP, STATE)
            print("restored window-state.txt")
        elif STATE.exists():
            STATE.unlink()

    print()
    if any(r[3] is None for r in results):
        print("RESULT: VOID — the window did not appear for at least one arm.")
        return 2
    witness = [r for r in results if r[0].startswith("B")][0]
    neg = [r for r in results if r[0].startswith("C")][0]
    pos = [r for r in results if r[0].startswith("A")][0]
    if not pos[3]:
        print("RESULT: VOID — even a plainly on-screen position was moved; something else is wrong.")
        return 2
    if not neg[3]:
        print("RESULT: INCONCLUSIVE — a genuinely off-screen window was KEPT, so the guard is not")
        print("        rejecting anything and the witness arm cannot mean what it looks like.")
        return 2
    print("RESULT: %s — the witness position is inside the band the OLD code discarded, it survived,"
          % ("PASS" if witness[3] else "FAIL"))
    print("        and the same guard still resets a genuinely off-screen one.")
    return 0 if witness[3] else 1


if __name__ == "__main__":
    raise SystemExit(main())
