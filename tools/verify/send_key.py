r"""Send one virtual key to the FOREGROUND window through SendInput, from Python.

    py tools/verify/send_key.py esc            # also: enter, tab, f5
    py tools/verify/send_key.py esc --front UE5DumpUI   # bring that process's window forward first

WHY. L6 step 2 (edit, Escape, reopen, Enter) was recorded NOT RUN on 2026-09-12 because the
computer-use `key` action's Escape never reached the Live Walker cell editor. The editor stayed open,
still holding the typed text, with the caret verifiably inside it. This sends the same key as a
plain SendInput keyboard event from this process instead, so the row does not need a human at the
keyboard. It prints the foreground window's title before and after, so a key that went to the wrong
window shows up in the output rather than as a silent no-op.
"""
import argparse
import ctypes
import os
import subprocess
import sys
import time
from ctypes import wintypes

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
HERE = os.path.dirname(os.path.abspath(__file__))
VK = {"esc": 0x1B, "enter": 0x0D, "tab": 0x09, "f5": 0x74}
INPUT_KEYBOARD, KEYEVENTF_KEYUP = 1, 0x0002

user32 = ctypes.WinDLL("user32", use_last_error=True)


class KEYBDINPUT(ctypes.Structure):
    _fields_ = [("wVk", wintypes.WORD), ("wScan", wintypes.WORD), ("dwFlags", wintypes.DWORD),
                ("time", wintypes.DWORD), ("dwExtraInfo", ctypes.c_size_t)]


class _INPUTUNION(ctypes.Union):
    # MOUSEINPUT is the largest member; pad so sizeof(INPUT) matches the OS (40 bytes on x64).
    _fields_ = [("ki", KEYBDINPUT), ("pad", ctypes.c_byte * 32)]


class INPUT(ctypes.Structure):
    _fields_ = [("type", wintypes.DWORD), ("u", _INPUTUNION)]


def foreground_title():
    h = user32.GetForegroundWindow()
    n = user32.GetWindowTextLengthW(h)
    buf = ctypes.create_unicode_buffer(n + 1)
    user32.GetWindowTextW(h, buf, n + 1)
    return buf.value


def send(vk):
    down = INPUT(type=INPUT_KEYBOARD, u=_INPUTUNION(ki=KEYBDINPUT(wVk=vk, wScan=0, dwFlags=0, time=0, dwExtraInfo=0)))
    up = INPUT(type=INPUT_KEYBOARD, u=_INPUTUNION(ki=KEYBDINPUT(wVk=vk, wScan=0, dwFlags=KEYEVENTF_KEYUP, time=0,
                                                              dwExtraInfo=0)))
    arr = (INPUT * 2)(down, up)
    sent = user32.SendInput(2, arr, ctypes.sizeof(INPUT))
    if sent != 2:
        raise SystemExit("SendInput sent %d of 2 events (error %d)" % (sent, ctypes.get_last_error()))


def windows_of(proc):
    """Visible, titled top-level windows owned by processes whose image name is `proc` (.exe optional)."""
    want = proc.lower().removesuffix(".exe")
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    found = []

    @ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    def cb(h, _):
        if not user32.IsWindowVisible(h) or user32.GetWindowTextLengthW(h) == 0:
            return True
        pid = wintypes.DWORD()
        user32.GetWindowThreadProcessId(h, ctypes.byref(pid))
        hp = k32.OpenProcess(0x1000, False, pid.value)   # PROCESS_QUERY_LIMITED_INFORMATION
        if hp:
            buf = ctypes.create_unicode_buffer(1024)
            n = wintypes.DWORD(1024)
            if k32.QueryFullProcessImageNameW(hp, 0, buf, ctypes.byref(n)):
                if os.path.basename(buf.value).lower().removesuffix(".exe") == want:
                    t = ctypes.create_unicode_buffer(512)
                    user32.GetWindowTextW(h, t, 512)
                    found.append((h, t.value))
            k32.CloseHandle(hp)
        return True

    user32.EnumWindows(cb, 0)
    return found


def post(hwnd, vk):
    """WM_KEYDOWN + WM_KEYUP straight into the window's queue: no global input, no hooks, no focus change."""
    scan = user32.MapVirtualKeyW(vk, 0)
    down = 1 | (scan << 16)
    up = down | (1 << 30) | (1 << 31)
    ok1 = user32.PostMessageW(hwnd, 0x0100, vk, down)
    ok2 = user32.PostMessageW(hwnd, 0x0101, vk, up)
    if not (ok1 and ok2):
        raise SystemExit("PostMessage failed (error %d)" % ctypes.get_last_error())


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("key", choices=sorted(VK))
    ap.add_argument("--front", help="process name whose window to bring forward first (front_window.py)")
    ap.add_argument("--post", metavar="PROC",
                    help="instead of SendInput, PostMessage WM_KEYDOWN/UP to PROC's top-level window. "
                         "Bypasses the global input queue, so another app's hook or overlay cannot take it "
                         "(measured 2026-09-24: an SendInput Escape moved the foreground to 'NVIDIA GeForce Overlay')")
    a = ap.parse_args()
    if a.front:
        subprocess.run([sys.executable, os.path.join(HERE, "front_window.py"), "front", a.front], check=True,
                       capture_output=True)
        time.sleep(0.3)
    print("foreground before: %r" % foreground_title())
    if a.post:
        wins = windows_of(a.post)
        if len(wins) != 1:
            raise SystemExit("--post %s: expected ONE visible titled window, found %r" % (a.post, wins))
        post(wins[0][0], VK[a.key])
        print("posted %s to %r" % (a.key, wins[0][1]))
    else:
        send(VK[a.key])
    time.sleep(0.2)
    print("sent %s; foreground after: %r" % (a.key, foreground_title()))
    return 0


if __name__ == "__main__":
    sys.exit(main())
