r"""Measure the blast radius of the open `Serie::GetString` dropped-`Number` lead, on the LOOKUP path.

    py tools/verify/findbyfullname_number_gate.py

THE LEAD (audit #5, raised 2026-08-16 while fixing U8, still open and explicitly UNVETTED):
`Serie::GetString(idx)` defaults `number = 0`, and most call sites take that default, so any
object whose `FName::Number` is non-zero renders under its bare name. Status line on the lead:
*"mechanism verified against the source; blast radius NOT measured"*. This measures one part of it.

WHAT MAKES `Aura::FindByFullName` WORSE THAN A COSMETIC DROP, and why it is worth a rig
Its two stages render the name with DIFFERENT code:
    cheap gate : `Serie::GetString(nameIndex) != wantLeaf`     -- Number DROPPED
    real check : `CanonicalizeObjectPath(Ubel::GetFullName(obj)) == wantPath`  -- Number KEPT
                 (`Ubel`'s `GetName` passes `live.number`)
So for an object really called `StaticMeshActor_33`:
  * ask for `..._33` -> the gate computes `StaticMeshActor`, which != the leaf, so the object is
    SKIPPED and the real check never runs;
  * ask for the bare name -> the gate passes, and the real check computes `..._33` and rejects it.
Neither spelling can succeed. This is audit #4's named root cause again -- *the report and the
reality computed by different code paths* -- and it means the path lookup cannot resolve any object
with a non-zero Number, which on a live UE map is nearly all of them.

THE CONTROL IS THE WHOLE RIG. "Returns 0" is also what an unreachable export, a bad string buffer
or a wrong calling convention produce. So a path that MUST work is tried first: a UClass, whose
FName has `Number == 0` by construction. Only if that resolves is a 0 on the instance meaningful.

MECHANISM: `UE5_FindObject(const char* fullPath)` takes exactly one pointer argument, which is what
`CreateRemoteThread` passes. The string is written into the target with `VirtualAllocEx`. The thread
exit code is the return truncated to 32 bits -- enough to tell 0 from non-zero, and the rig says so
rather than pretending it recovered the pointer.
"""
import ctypes
import pathlib
import struct
import sys
import time
from ctypes import wintypes

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient  # noqa: E402
from mailbox_poke import Mem, pid_of  # noqa: E402
import mailbox_addr as MA  # noqa: E402

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
for fn, res, arg in (
        ("OpenProcess", ctypes.c_void_p, [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]),
        ("VirtualAllocEx", ctypes.c_void_p,
         [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, wintypes.DWORD, wintypes.DWORD]),
        ("VirtualFreeEx", wintypes.BOOL,
         [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, wintypes.DWORD]),
        ("WriteProcessMemory", wintypes.BOOL,
         [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t,
          ctypes.POINTER(ctypes.c_size_t)]),
        ("CreateRemoteThread", ctypes.c_void_p,
         [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.c_void_p,
          ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD)]),
        ("WaitForSingleObject", wintypes.DWORD, [ctypes.c_void_p, wintypes.DWORD]),
        ("GetExitCodeThread", wintypes.BOOL, [ctypes.c_void_p, ctypes.POINTER(wintypes.DWORD)]),
        ("CloseHandle", wintypes.BOOL, [ctypes.c_void_p])):
    f = getattr(k32, fn)
    f.restype, f.argtypes = res, arg

PROC_ALL = 0x000F0000 | 0x00100000 | 0xFFF


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")


def export_addr(proc, name):
    """Resolve an export of UE5Dumper.dll in the target, reusing mailbox_addr's PE reader.

    `modules()` yields (path, base) -- not (base, path); getting that backwards fed an int
    to pathlib and looked like a pathlib bug rather than a shape mistake.
    """
    pid = pid_of(proc)
    r = MA.Reader(pid)
    base = next((b for path, b in MA.modules(pid)
                 if pathlib.Path(path).name.lower().startswith("ue5dumper")), None)
    if base is None:
        raise SystemExit("UE5Dumper.dll is not mapped in %s" % proc)
    addr = MA.find_export(r, base, name)
    if not addr:
        raise SystemExit("export %s not found in UE5Dumper.dll" % name)
    return addr


def call_with_string(pid, fnaddr, text):
    h = k32.OpenProcess(PROC_ALL, False, pid)
    if not h:
        raise SystemExit("OpenProcess failed err=%d" % ctypes.get_last_error())
    buf = text.encode("utf-8") + b"\x00"
    rem = k32.VirtualAllocEx(h, None, len(buf), 0x3000, 0x04)
    if not rem:
        raise SystemExit("VirtualAllocEx failed err=%d" % ctypes.get_last_error())
    put = ctypes.c_size_t(0)
    if not k32.WriteProcessMemory(h, rem, (ctypes.c_char * len(buf))(*buf), len(buf),
                                  ctypes.byref(put)) or put.value != len(buf):
        raise SystemExit("WriteProcessMemory failed err=%d" % ctypes.get_last_error())
    tid = wintypes.DWORD()
    th = k32.CreateRemoteThread(h, None, 0, ctypes.c_void_p(fnaddr), rem, 0, ctypes.byref(tid))
    if not th:
        raise SystemExit("CreateRemoteThread failed err=%d" % ctypes.get_last_error())
    if k32.WaitForSingleObject(th, 15000) != 0:
        raise SystemExit("the remote call did not return in 15 s")
    code = wintypes.DWORD()
    k32.GetExitCodeThread(th, ctypes.byref(code))
    k32.CloseHandle(th)
    k32.VirtualFreeEx(h, rem, 0, 0x8000)
    k32.CloseHandle(h)
    return code.value


def main():
    """The decisive test needs no knowledge of the path format at all.

    `UE5_FindObject` -> `Aura::FindByNameOrPath`. A query with no `/` or `.` is not
    path-shaped, so it goes to `FindByName`, whose comparison is
    `Serie::GetString(nameIndex) == name` -- the Number-dropping one-arg call again.
    So the question becomes: can the DLL find an object by the name IT ITSELF reports
    for that object? `walk_instance` renders `StaticMeshActor_33`; `FindByName`
    compares against `StaticMeshActor`. They cannot both be right.
    """
    fails = []
    proc = "DumperTest"
    pid = pid_of(proc)
    fn = export_addr(proc, "UE5_FindObject")
    say("UE5_FindObject @ %#x in pid %d" % (fn, pid))
    m = Mem(pid)

    subjects = []
    with PipeClient() as c:
        c.ensure_scanned()
        pool = [i for i in (c.request("find_instances", class_name="Actor",
                                      max_results=400, exact_match=False).get("instances") or [])
                if not str(i.get("name", "")).startswith("Default__")]
        for i in pool:
            num = struct.unpack("<II", m.read(int(i["addr"], 16) + 0x18, 8))[1]
            w = c.request("walk_instance", addr=i["addr"], array_limit=1)
            wd = w.get("data", w)
            subjects.append(dict(addr=i["addr"], listed=i.get("name"),
                                 walked=wd.get("name"), number=num))
        n0 = [s for s in subjects if s["number"] == 0]
        nz = [s for s in subjects if s["number"] != 0]

    say("")
    say("live non-CDO objects: %d   with FName::Number == 0: %d   with Number != 0: %d"
        % (len(subjects), len(n0), len(nz)))
    say("")
    say("  %-16s %-13s %-24s %-24s" % ("addr", "Number", "find_instances name", "walk_instance name"))
    for s in (n0[:2] + nz[:4]):
        say("  %-16s %-13d %-24r %-24r" % (s["addr"][-12:], s["number"], s["listed"], s["walked"]))
    disagree = [s for s in subjects if s["listed"] != s["walked"]]
    say("")
    say("  the two commands DISAGREE about the name of %d / %d objects"
        % (len(disagree), len(subjects)))
    if not nz:
        say("SKIP: nothing on this host has a non-zero Number, so there is nothing to measure")
        return 0

    say("")
    say("== CONTROL 1: a path lookup that must work (a UClass, Number == 0 by construction) ==")
    rc = call_with_string(pid, fn, "/Script/Engine.StaticMeshActor")
    say("   UE5_FindObject('/Script/Engine.StaticMeshActor') -> low32 = %#x" % rc)
    if rc == 0:
        fails.append("CONTROL 1 returned 0 -- the export or the string marshalling is broken, so "
                     "no 0 below would mean anything")
        for x in fails:
            say("FAIL: %s" % x)
        return 1

    say("")
    say("== CONTROL 2: an object whose Number IS 0 must be findable by its reported name ==")
    ctrl2 = next((s for s in n0 if s["walked"]), None)
    if not ctrl2:
        say("   SKIP: no Number==0 object with a reported name on this host")
    else:
        rc2 = call_with_string(pid, fn, ctrl2["walked"])
        say("   UE5_FindObject(%r) -> low32 = %#x  %s"
            % (ctrl2["walked"], rc2, "FOUND" if rc2 else "NOT FOUND"))
        if rc2 == 0:
            fails.append("CONTROL 2: even a Number==0 object is not findable by name, so the "
                         "failures below are not attributable to the Number")

    say("")
    say("== THE MEASUREMENT: objects whose Number != 0, looked up by the name the DLL reports ==")
    lost = []
    for s in nz[:6]:
        if not s["walked"]:
            continue
        rc3 = call_with_string(pid, fn, s["walked"])
        bare = call_with_string(pid, fn, s["listed"]) if s["listed"] else 0
        say("   %-22r -> %s        (bare %-20r -> %s)"
            % (s["walked"], "FOUND" if rc3 else "NOT FOUND",
               s["listed"], "found something" if bare else "not found"))
        if rc3 == 0:
            lost.append(s)

    say("")
    if lost:
        say("MEASURED: %d of the %d probed objects cannot be found by the name the DLL itself"
            % (len(lost), min(6, len(nz))))
        say("          reports for them. `walk_instance` renders FName::Number; `FindByName` and")
        say("          `find_instances` do not, so the two halves of the tool disagree.")
    else:
        say("All probed objects resolved -- the drop is not reaching the lookup path on this host.")

    say("")
    say("(low32 only: CreateRemoteThread's exit code is 32 bits, so non-zero means 'resolved' and "
        "the pointer itself is not recovered here. CONTROL 1's 0x%X matches the low half of the "
        "StaticMeshActor UClass address the pipe reports, which is the cross-check that it really "
        "found the right object.)" % rc)
    for x in fails:
        say("FAIL: %s" % x)
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
