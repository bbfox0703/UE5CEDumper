r"""[DUMPER-SECOND-COPY-AUTOSTART] live check: two copies of the dumper in one game process, the later one took over.

    py tools/verify/second_copy_live.py --pid 1234 --earlier dxgi.dll --later UE5Dumper.dll --yields
    py tools/verify/second_copy_live.py --pid 1234 --earlier dxgi.dll --later UE5Dumper.dll     # an earlier copy built
                                                                                                # before the yield export

Run after the later copy was injected beside an earlier one (a proxy in the game folder, say) and the later copy's
auto-start finished. The game, its pid, the module names and the log folder are arguments, never written here.
Checks:
  - both copies are loaded and export g_invokeMailbox;
  - the pipe answers with the LATER copy's build (its module's FILEVERSION), and the log says the later copy took over
    from the earlier one by name, then that the earlier copy yielded (--yields) or shut down, and that its mailbox is
    served by the later copy -- with --yields, the earlier copy's own lines too;
  - the earlier mailbox reads READY: the later copy mirrors its state there;
  - a read-only command (CMD_QUERY_PTR, GWorld) written into the EARLIER copy's mailbox comes back DONE with result 0
    and the same pointer as the later copy's own mailbox gives. The earlier copy's poller stopped with its shutdown
    (its log says so), so the later copy answered it;
  - the object count through the pipe is at least --min-objects.
"""
import argparse
import ctypes
import pathlib
import re
import struct
import sys
from ctypes import wintypes

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import LOGROOT, PipeClient, process_image   # noqa: E402
import mailbox_addr   # noqa: E402  -- its PE export reader and module list
import mailbox_poke   # noqa: E402  -- the ordinary mailbox protocol, no CE

results: list[tuple[str, bool, str]] = []


def check(name: str, ok: bool, detail: str = "") -> bool:
    results.append((name, ok, detail))
    print(f"  [{'PASS' if ok else 'FAIL'}] {name}" + (f" -- {detail}" if detail else ""))
    return ok


def file_build(path: str) -> int:
    ver = ctypes.WinDLL("version")
    size = ver.GetFileVersionInfoSizeW(path, None)
    if not size:
        return 0
    buf = ctypes.create_string_buffer(size)
    if not ver.GetFileVersionInfoW(path, 0, size, buf):
        return 0
    p, n = ctypes.c_void_p(), wintypes.UINT()
    if not ver.VerQueryValueW(buf, "\\", ctypes.byref(p), ctypes.byref(n)) or not p.value:
        return 0
    fixed = ctypes.cast(p, ctypes.POINTER(ctypes.c_uint32 * 13)).contents
    return fixed[3] & 0xFFFF   # dwFileVersionLS low word: the build number (version.rc)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--pid", type=int, required=True)
    ap.add_argument("--earlier", required=True, help="the earlier copy's module file name, e.g. dxgi.dll")
    ap.add_argument("--later", required=True, help="the later copy's module file name, e.g. UE5Dumper.dll")
    ap.add_argument("--yields", action="store_true", help="the earlier copy carries UE5_YieldToLaterCopy")
    ap.add_argument("--min-objects", type=int, default=0x1000)
    ap.add_argument("--logdir", help="the game's folder under Logs\\ (default: its exe's stem)")
    a = ap.parse_args()
    logdir = LOGROOT / (a.logdir or pathlib.Path(process_image(a.pid)).stem)

    r = mailbox_addr.Reader(a.pid)
    mods = {pathlib.Path(p).name.lower(): (p, base) for p, base in mailbox_addr.modules(a.pid) if base}
    found = {}
    for role, name in (("earlier", a.earlier), ("later", a.later)):
        hit = mods.get(name.lower())
        box = mailbox_addr.find_export(r, hit[1], "g_invokeMailbox") if hit else None
        check(f"the {role} copy {name} is loaded and exports g_invokeMailbox", bool(hit and box),
              f"{hit[0]} mailbox={box:#x}" if hit and box else "missing")
        found[role] = (hit[0], box) if hit and box else None
    if not all(found.values()):
        return 1
    earlier_path, earlier_box = found["earlier"]
    later_path, later_box = found["later"]
    later_build = file_build(later_path)
    print(f"  earlier: {earlier_path} build {file_build(earlier_path)}; later: {later_path} build {later_build}")

    with PipeClient(pid=a.pid) as c:
        got = c.assert_build(str(later_build))
        check("the pipe answers with the later copy's build", got.endswith(str(later_build)), got)
        n = int(c.request("get_object_count").get("count") or c.request("get_pointers").get("object_count") or 0)
        check(f"the object count is at least {a.min_objects}", n >= a.min_objects, str(n))

    text = ""
    for p in sorted(logdir.glob("*-0.log")):
        text += p.read_text(encoding="utf-8", errors="replace")
    earlier_name = re.escape(pathlib.Path(earlier_path).name)
    check("the log: the later copy takes over from the earlier copy by name",
          re.search(r"Land: taking over from the earlier copy at [^\n]*" + earlier_name, text, re.I) is not None)
    how = "yielded" if a.yields else "shut down"
    check(f"the log: the earlier copy {how}, and its mailbox is served by the later copy",
          re.search(rf"Land: the earlier copy {how} in \d+ ms; its mailbox is served by this copy", text) is not None)
    check("the log: the later copy serves the earlier copy's mailbox",
          f"serving an earlier copy's mailbox at" in text)
    check("the log: the earlier copy's poller stopped", "Mailbox: polling thread stopped" in text)
    if a.yields:
        check("the log: the earlier copy was asked to yield",
              "UE5_YieldToLaterCopy: a later copy of the dumper is taking over this process" in text)
        check("the log: the earlier copy handed the process over",
              re.search(r"Land: this copy has handed the process to the copy at [^\n]*"
                        + re.escape(pathlib.Path(later_path).name), text, re.I) is not None)

    m = mailbox_poke.Mem(a.pid)
    state = m.i32(earlier_box + mailbox_poke.OFF_INITSTATE)
    check("the earlier mailbox reads READY (the later copy's state, mirrored)", state == mailbox_poke.INIT_READY, str(state))
    res_l, params_l, _, err_l = mailbox_poke.poke(m, later_box, mailbox_poke.QUERY_OP_GWORLD)
    check("control: the later copy's own mailbox answers GWorld", res_l == 0, err_l or f"result={res_l}")
    res_e, params_e, dt, err_e = mailbox_poke.poke(m, earlier_box, mailbox_poke.QUERY_OP_GWORLD)
    gw_l = struct.unpack_from("<Q", params_l)[0]
    gw_e = struct.unpack_from("<Q", params_e)[0]
    check("a command in the EARLIER copy's mailbox is answered, by the later copy", res_e == 0 and gw_e == gw_l and gw_e,
          f"result={res_e} GWorld={gw_e:#x} (later's {gw_l:#x}) in {dt * 1000:.0f} ms {err_e}")

    failed = [n for n, ok, _ in results if not ok]
    print(f"\n{len(results) - len(failed)}/{len(results)} passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
