r"""L51 [A2-CRC-PATH-LS]: read a scan log AS BYTES and judge the CrashReportClient line.

    py tools/verify/l51_crc_line.py                       # Logs\ES2-Win64-Shipping\scan-0.log
    py tools/verify/l51_crc_line.py <scan log path>       # any other file (a copy, an archive)

WHY BYTES. The fix is about what Sein writes for a wide path with a character above 0xFF
(EVERSPACE(TM) 2's U+2122). The cp950 console mangles that character, and a text-mode read
with the wrong codec "fixes" or hides exactly the bytes under test. So the verdict comes from
the raw bytes: the CrashReportClient line must carry the path as UTF-8 (TM = E2 84 A2), and
no `[SCAN:Ver] ` record may be EMPTY -- the pre-fix shape, where `%ls` in a narrow formatter
emptied the whole record (ES2 scan-20260909-171715.log:9).

WHY THE HINT LINE IS CHECKED. A current-rev cached version skips DetectVersion entirely
(`FindAll: UE Version = … (cached, rev=…) — skipped DetectVersion`), and then the absence of an
empty record proves nothing. Drop the title's hint record first (`cold_detect.py drop`); this
refuses to call a skipped detection a pass.
"""
import os
import pathlib
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
DEFAULT = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs" / "ES2-Win64-Shipping" / "scan-0.log"
TM = "™".encode("utf-8")   # E2 84 A2


def main():
    path = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT
    raw = path.read_bytes()   # raises on a sharing violation -- never a vacuous zero
    lines = raw.split(b"\n")
    print("log        : %s (%d bytes, %d lines)" % (path, len(raw), len(lines)))

    skipped = [l for l in lines if b"skipped DetectVersion" in l]
    attempted = [l for l in lines if b"DetectVersion: Attempting to detect UE version" in l]
    crc = [l for l in lines if b"DetectVersion: CrashReportClient at '" in l]
    empty = [(i + 1, l) for i, l in enumerate(lines)
             if re.fullmatch(rb"\[[^\]]+\] \[[A-Z]+\] \[SCAN:Ver\] ?\r?", l)]
    agree = [l for l in lines if b"DetectVersion: CrashReportClient and the game exe AGREE" in l]

    for l in skipped:
        print("SKIPPED    : %s" % l.decode("utf-8", "replace").rstrip())
    print("attempted  : %d DetectVersion run(s)" % len(attempted))
    for l in crc:
        ok_utf8 = True
        try:
            l.decode("utf-8")
        except UnicodeDecodeError:
            ok_utf8 = False
        print("CRC line   : %s" % l.decode("utf-8", "replace").rstrip())
        print("             utf-8 valid=%s  contains U+2122 as E2 84 A2=%s" % (ok_utf8, TM in l))
    for n, l in empty:
        print("EMPTY rec  : line %d: %r" % (n, l))
    for l in agree:
        print("agree      : %s" % l.decode("utf-8", "replace").rstrip())

    if not attempted:
        print("\nL51: NOT MEASURED -- DetectVersion never ran (a cached hint skipped it). Drop the record and relaunch.")
        return 2
    fails = []
    if not crc:
        fails.append("no 'CrashReportClient at' line")
    if crc and not all(TM in l for l in crc):
        fails.append("a CrashReportClient line lacks the UTF-8 TM bytes")
    if empty:
        fails.append("%d EMPTY [SCAN:Ver] record(s) -- the pre-fix shape" % len(empty))
    if fails:
        print("\nL51: FAIL -- " + "; ".join(fails))
        return 1
    print("\nL51: PASS -- the CrashReportClient path is logged in full as UTF-8, and no [SCAN:Ver] record is empty.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
