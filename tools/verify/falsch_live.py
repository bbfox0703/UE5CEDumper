r"""[MH-TRAMPOLINE-PREFLIGHT] live: every hook the DLL enables goes through Falsch's pre-flight, and still installs.

    py tools/verify/falsch_live.py DumperTest-Win64-Shipping

The game or fixture is running with dist\UE5Dumper.dll injected (or a refreshed proxy), and no UI. The argument
names the folder under %LOCALAPPDATA%\UE5CEDumper\Logs\ that holds the DLL's log: the PROCESS name, so a Shipping
fixture's folder is `<Name>-Win64-Shipping`.

WHAT IT CHECKS, and the evidence each check needs:

  L1  ProcessEvent: FIRE `KismetStringLibrary::Conv_IntToString(42)` needs the game-thread hook. The reply must hold
      ReturnValue = "42", and this session's init log must hold "ProcessEvent hook installed at 0x...
      (trampoline pre-flight: N instruction(s) copied, Zydis agrees)" -- the line only a vetted install writes -- and
      no "trampoline pre-flight refused".
  L2  Grausam: set_foreground_lock on must answer state 1, the log must say "Foreground lock ENABLED" and must not
      say "refused by the trampoline pre-flight"; then off (state 0), so the session is left as it was found.

What it does NOT show: a refusal on a real target. Every target on the measured fixtures is safe (dll_helpers_test
pins the refusals); this proves the vetted path installs and the hooks still work.

Exit 0 = both pass; 1 = a failure; 2 = the fixture is not in the state the checks need.
"""
import argparse
import os
import pathlib
import re
import struct
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from pipe_client import PipeClient  # noqa: E402

LOGS = pathlib.Path(os.environ.get("LOCALAPPDATA", "")) / "UE5CEDumper" / "Logs"
INSTALLED = re.compile(r"ProcessEvent hook installed at 0x([0-9A-Fa-f]+) \(trampoline pre-flight: (\d+) "
                       r"instruction\(s\) copied, Zydis agrees\)")


def say(s):
    sys.stdout.write(str(s) + "\n")
    sys.stdout.flush()


def current_log(folder: pathlib.Path) -> pathlib.Path | None:
    logs = sorted(folder.glob("init*.log"), key=lambda p: p.stat().st_mtime)
    return logs[-1] if logs else None


def read(path: pathlib.Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("log_folder", help="the process-named folder under %%LOCALAPPDATA%%\\UE5CEDumper\\Logs")
    a = ap.parse_args()
    folder = LOGS / a.log_folder
    if not folder.is_dir():
        say(f"NOT READY: no log folder {folder}")
        return 2
    fails = []
    with PipeClient() as c:
        say("build: %s" % c.assert_build())
        c.ensure_scanned()
        log = current_log(folder)
        if not log:
            say(f"NOT READY: no init log in {folder}")
            return 2
        say(f"log: {log}")

        say("\n== L1 ProcessEvent: FIRE through the vetted hook ==")
        r = c.request("invoke_function", class_name="KismetStringLibrary", func_name="Conv_IntToString",
                      params_hex=struct.pack("<i", 42).hex().upper())
        d = r.get("data", r)
        ret = next((s for s in (d.get("str_results") or []) if s.get("name") == "ReturnValue"), None)
        text = read(log)
        m = INSTALLED.findall(text)
        say("   result=%s ReturnValue=%r installs=%s" % (d.get("result"), ret and ret.get("text"), m))
        if "trampoline pre-flight refused" in text:
            say("   FAIL: the log has a pre-flight refusal")
            fails.append("L1")
        elif d.get("result") == 0 and ret and ret.get("text") == "42" and m:
            say("   PASS: ProcessEvent 0x%s vetted, %s instruction(s) copied" % m[-1])
        else:
            say("   FAIL: expected ReturnValue \"42\" and a vetted install line")
            fails.append("L1")

        say("\n== L2 Grausam: the foreground lock's user32 hooks ==")
        on = c.request("set_foreground_lock", enable=True).get("data", {})
        text = read(log)
        refused = "refused by the trampoline pre-flight" in text
        enabled = "Foreground lock ENABLED" in text
        off = c.request("set_foreground_lock", enable=False).get("data", {})
        say("   on: state=%s  off: state=%s  ENABLED logged=%s  refused logged=%s"
            % (on.get("state"), off.get("state"), enabled, refused))
        if on.get("state") == 1 and off.get("state") == 0 and enabled and not refused:
            say("   PASS")
        else:
            say("   FAIL")
            fails.append("L2")

    say("\n%s" % ("ALL PASS" if not fails else "FAILED: " + ", ".join(fails)))
    return 1 if fails else 0


if __name__ == "__main__":
    raise SystemExit(main())
