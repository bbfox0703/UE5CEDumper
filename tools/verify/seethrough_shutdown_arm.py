r"""M1 / M2 / M3 arm (d), substituted: See-through's restore set through UE5_Shutdown, on a live process.

    py tools/verify/seethrough_shutdown_arm.py

Arm (d) of seethrough_restoreset.py is "close the game; every hidden actor must become visible again" -- not
observable as written, since a dead process has no memory to read, and closing a game never calls UE5_Shutdown
(the register's B8 / M5 record). So this drives the teardown a window close would want, on a healthy game:
UE5_Shutdown through call_export.py (CE's Disable path), then re-reads THE SAME actors' own bHidden bits out of the
still-living process with ReadProcessMemory. Record it as the UE5_Shutdown path, not as "close the game".

  positive control: every actor the DLL reports hidden has its own bHidden bit set
  negative control: 2 s later, with nothing done, the bits are still set
  the arm:          after UE5_Shutdown every captured bit is clear, the process is alive, and the game's log folder
                    gains "UE5_Shutdown: Cleaning up" and "SeeThrough: disabled (N restored)" with N = the captured count
Host: DumperTest Development (the flavour seethrough_restoreset.py relaunches, so the same view hides an occluder).
"""
import ctypes
import pathlib
import re
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient   # noqa: E402
from seethrough_restoreset import PIERCE, hidden_bits, k32, relaunch, resolve_field   # noqa: E402

LOGDIR = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs/DumperTest"
STILL_ACTIVE = 259
results: list[tuple[str, bool, str]] = []


def check(name: str, ok: bool, detail: str = "") -> bool:
    results.append((name, ok, detail))
    print(f"  [{'PASS' if ok else 'FAIL'}] {name}" + (f" -- {detail}" if detail else ""))
    return ok


def log_marks() -> dict:
    return {p: p.stat().st_size for p in LOGDIR.glob("*.log")} if LOGDIR.exists() else {}


def log_since(marks: dict) -> str:
    text = []
    for p in LOGDIR.glob("*.log"):
        text.append(p.read_bytes()[marks.get(p, 0):].decode("utf-8", "replace"))
    return "\n".join(text)


def main() -> int:
    k32.GetExitCodeProcess.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_ulong)]
    pid, h = relaunch()
    print(f"DumperTest Development pid {pid}")
    marks = log_marks()
    # The connection stays OPEN until UE5_Shutdown has run: a dropped pipe client disables See-through by itself
    # (arm (c)'s path), so closing it first would restore the actors before the arm and credit the drop.
    c = PipeClient(pid=pid).connect()
    off, mask, cls = resolve_field(c)
    print(f"build {c.assert_build()}  witness {cls}.bHidden +{off} mask 0x{mask:02X}")
    c.request("seethrough_set", enable=False)
    time.sleep(0.5)
    c.request("seethrough_set", enable=True, count=PIERCE)
    addrs = []
    for _ in range(25):
        time.sleep(0.4)
        st = c.request("seethrough_get_state")
        addrs = [int(x, 16) if isinstance(x, str) else int(x) for x in (st.get("hidden_actors") or [])]
        if addrs:
            break
    marks = log_marks()   # after the enable's own disable(0): only the arm's lines count
    if not check("See-through hid at least one actor (the arm's subject)", bool(addrs), str([hex(a) for a in addrs])):
        return 1
    pre = hidden_bits(h, addrs, off, mask)
    check("positive control: every reported actor's own bHidden bit is set", all(v for _, v in pre), str(pre))
    time.sleep(2.0)
    still = hidden_bits(h, addrs, off, mask)
    check("negative control: still set after 2 s of doing nothing", all(v for _, v in still), str(still))

    out = subprocess.run([sys.executable, str(HERE / "call_export.py"), "UE5_Shutdown", "--process", "DumperTest"],
                         capture_output=True, text=True, errors="replace")
    print("  call_export:", (out.stdout + out.stderr).strip().replace("\n", " | ")[:300])
    time.sleep(1.5)
    try:
        c.close()
    except OSError:
        pass
    post = hidden_bits(h, addrs, off, mask)
    check("after UE5_Shutdown every captured actor is visible again", all(v is False for _, v in post), str(post))
    code = ctypes.c_ulong(0)
    k32.GetExitCodeProcess(k32.OpenProcess(0x1000, False, pid), ctypes.byref(code))
    check("the process is still alive (the bits were read from a living game)", code.value == STILL_ACTIVE,
          f"exit code {code.value}")

    text = log_since(marks)
    check("the log gains 'UE5_Shutdown: Cleaning up'", "UE5_Shutdown: Cleaning up" in text)
    m = re.findall(r"SeeThrough: disabled \((\d+) restored\)", text)
    check("the log gains 'SeeThrough: disabled (N restored)', N = the captured count",
          bool(m) and int(m[-1]) == len(addrs), f"{m} against {len(addrs)} captured")
    failed = [n for n, ok, _ in results if not ok]
    print(f"\n{len(results) - len(failed)}/{len(results)} passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
