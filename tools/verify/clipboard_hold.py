"""Hold the Windows clipboard open so every other process's clipboard call FAILS.

    py clipboard_hold.py 120        # hold it for 120 seconds, then release

Used by the `[PASTECRASH]` check, whose step 2 needs the clipboard to *genuinely*
fail -- the row names exactly this method ("hold the clipboard open from another
process (`OpenClipboard` without `CloseClipboard`)").

Windows allows ONE owner at a time: while this process holds it, another app's
`OpenClipboard` returns FALSE with `ERROR_ACCESS_DENIED` (5), which is what drives
Avalonia's clipboard task to fault. That fault used to terminate the UI.

FAILS LOUDLY: if `OpenClipboard` does not succeed here, nothing is being held and any
downstream "the UI survived" result would be meaningless -- so that is an error, not a
warning. Always releases in a `finally`, and the timeout is a hard cap so a crashed
driver cannot leave the machine's clipboard wedged.
"""
import ctypes
import sys
import time
from ctypes import wintypes

u32 = ctypes.WinDLL("user32", use_last_error=True)
u32.OpenClipboard.argtypes = [wintypes.HWND]
u32.OpenClipboard.restype = wintypes.BOOL
u32.CloseClipboard.restype = wintypes.BOOL


def main(seconds):
    if not u32.OpenClipboard(None):
        raise SystemExit(f"clipboard_hold: FAILED -- OpenClipboard returned FALSE "
                         f"(Win32 {ctypes.get_last_error()}); nothing is being held, so "
                         f"any 'the UI survived' result downstream would be meaningless")
    print(f"clipboard HELD (pid {__import__('os').getpid()}) for {seconds}s", flush=True)
    try:
        time.sleep(seconds)
    finally:
        u32.CloseClipboard()
        print("clipboard released", flush=True)


if __name__ == "__main__":
    main(int(sys.argv[1]) if len(sys.argv) > 1 else 60)
