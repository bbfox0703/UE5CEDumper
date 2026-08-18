"""Force a window to the foreground by owning process name, and REPORT what won.

    py front_window.py list
    py front_window.py front UE5DumpUI
    py front_window.py front cheatengine "Lua Engine"   # 2nd arg = window-title filter
    py front_window.py minimize explorer

computer-use refuses to click while a non-allowlisted app is frontmost, and
`open_application` will not re-front an already-running single-instance app. The
AttachThreadInput + SetForegroundWindow dance is what actually moves focus on
this machine.

It always prints the foreground window AFTER the attempt rather than trusting the
return value -- SetForegroundWindow fails silently under Windows' focus-stealing
rules, and "I called it so it worked" is how a click lands in the wrong app.
"""
import ctypes
import ctypes.wintypes as w
import sys

u32 = ctypes.WinDLL("user32", use_last_error=True)
k32 = ctypes.WinDLL("kernel32", use_last_error=True)

SW_MINIMIZE, SW_RESTORE, SW_SHOW = 6, 9, 5

WNDENUMPROC = ctypes.WINFUNCTYPE(w.BOOL, w.HWND, w.LPARAM)


def proc_name(pid):
    PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
    h = k32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
    if not h:
        return "?"
    buf = ctypes.create_unicode_buffer(1024)
    size = w.DWORD(1024)
    ok = k32.QueryFullProcessImageNameW(h, 0, buf, ctypes.byref(size))
    k32.CloseHandle(h)
    return buf.value.rsplit("\\", 1)[-1] if ok else "?"


def top_windows():
    out = []

    def cb(hwnd, _):
        if not u32.IsWindowVisible(hwnd):
            return True
        length = u32.GetWindowTextLengthW(hwnd)
        if length == 0:
            return True
        buf = ctypes.create_unicode_buffer(length + 1)
        u32.GetWindowTextW(hwnd, buf, length + 1)
        pid = w.DWORD()
        u32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        out.append((hwnd, pid.value, proc_name(pid.value), buf.value))
        return True

    u32.EnumWindows(WNDENUMPROC(cb), 0)
    return out


def foreground():
    hwnd = u32.GetForegroundWindow()
    pid = w.DWORD()
    u32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    length = u32.GetWindowTextLengthW(hwnd)
    buf = ctypes.create_unicode_buffer(length + 1)
    u32.GetWindowTextW(hwnd, buf, length + 1)
    return hwnd, pid.value, proc_name(pid.value), buf.value


def front(match, title_match=None):
    targets = [t for t in top_windows() if match.lower() in t[2].lower()]
    # One process can own several top-level windows -- Cheat Engine owns its main
    # window, the Lua Engine, and the Memory Viewer at once, and EnumWindows hands
    # back the same one first every time. Without this the caller silently keeps
    # fronting the wrong one and then clicks coordinates read off the other.
    if title_match:
        targets = [t for t in targets if title_match.lower() in t[3].lower()]
    if not targets:
        extra = f" with title containing {title_match!r}" if title_match else ""
        print(f"no visible window owned by a process matching {match!r}{extra}")
        return 1
    hwnd, pid, name, title = targets[0]
    print(f"target: hwnd={hwnd} pid={pid} {name}  {title!r}")

    fg_hwnd = u32.GetForegroundWindow()
    fg_tid = u32.GetWindowThreadProcessId(fg_hwnd, None)
    my_tid = k32.GetCurrentThreadId()
    u32.AttachThreadInput(my_tid, fg_tid, True)
    # SW_RESTORE un-maximizes a maximized window, which silently undoes the layout
    # every screenshot's coordinates were read against. Only un-minimize.
    if u32.IsIconic(hwnd):
        u32.ShowWindow(hwnd, SW_RESTORE)
    u32.BringWindowToTop(hwnd)
    u32.SetForegroundWindow(hwnd)
    u32.AttachThreadInput(my_tid, fg_tid, False)

    fh, fp, fn, ft = foreground()
    print(f"foreground NOW: pid={fp} {fn}  {ft!r}")
    print("RESULT:", "OK" if fh == hwnd else "DID NOT TAKE")
    return 0 if fh == hwnd else 2


def minimize(match):
    n = 0
    for hwnd, pid, name, title in top_windows():
        if match.lower() in name.lower():
            u32.ShowWindow(hwnd, SW_MINIMIZE)
            print(f"minimized {name}  {title!r}")
            n += 1
    print(f"{n} window(s) minimized")
    fh, fp, fn, ft = foreground()
    print(f"foreground NOW: pid={fp} {fn}  {ft!r}")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    cmd = sys.argv[1] if len(sys.argv) > 1 else "list"
    if cmd == "list":
        for hwnd, pid, name, title in top_windows():
            print(f"  {name:32s} pid={pid:<7} {title[:70]!r}")
        print()
        fh, fp, fn, ft = foreground()
        print(f"foreground: pid={fp} {fn}  {ft!r}")
    elif cmd == "front":
        sys.exit(front(sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else None))
    elif cmd == "minimize":
        sys.exit(minimize(sys.argv[2]))
    else:
        print(__doc__)
