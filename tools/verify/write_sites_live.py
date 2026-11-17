r"""[DENKEN-WRITE-SITES] live check: native write sites from walk_function_props are real write instructions.

    py tools/verify/write_sites_live.py --pid 1234
    py tools/verify/write_sites_live.py --pid 1234 --probe 2000 --check 60

On a scanned game: lists every native UFunction (list_all_functions), walks up to --probe of them
(walk_function_props), and keeps the rows that carry write_sites. For up to --check of those sites it reads the
instruction's bytes (read_mem) and asks whether they carry the field's offset as the memory operand's displacement --
a disp8 for an offset under 0x80, else a disp32 -- found WITHOUT a disassembler, so the check does not trust the code
it checks. Also: no read-only row (write_count 0) carries a site, a row's sites are distinct, and at most 8 (Denken's
kMaxWriteSites) unless write_sites_truncated says more were dropped.
The game, its pid and the limits are arguments, never written here.
"""
import argparse
import pathlib
import struct
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient   # noqa: E402

FUNC_NATIVE = 0x400
MAX_WRITE_SITES = 8   # Denken::kMaxWriteSites
results: list[tuple[str, bool, str]] = []


def check(name: str, ok: bool, detail: str = "") -> bool:
    results.append((name, ok, detail))
    print(f"  [{'PASS' if ok else 'FAIL'}] {name}" + (f" -- {detail}" if detail else ""))
    return ok


def carries_displacement(code: bytes, offset: int) -> bool:
    """The field offset as a displacement somewhere in the instruction's first bytes (after its first opcode byte)."""
    if offset < 0:
        return False
    needles = []
    if offset < 0x80:
        needles.append(bytes([offset]))
    needles.append(struct.pack("<i", offset))
    body = code[1:15]
    return any(n in body for n in needles)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--pid", type=int, required=True)
    ap.add_argument("--probe", type=int, default=1500, help="most native functions to walk")
    ap.add_argument("--check", type=int, default=40, help="most write sites to read back")
    a = ap.parse_args()

    with PipeClient(pid=a.pid) as c:
        print("build", c.assert_build())
        c.ensure_scanned()
        g = c.request("list_all_functions", limit=200000)
        rows = g.get("results") or g.get("functions") or []
        native = [r for r in rows if (r.get("function_flags") or 0) & FUNC_NATIVE and r.get("func_addr")]
        print(f"  {len(rows)} function(s), {len(native)} native; walking {min(len(native), a.probe)}")
        walked = disasm = 0
        writers, sites_rows, bad_rows = [], [], []
        for r in native[:a.probe]:
            rep = c.request("walk_function_props", func_addr=r["func_addr"])
            d = rep.get("data", rep)
            walked += 1
            if d.get("method") != "disasm":
                continue
            disasm += 1
            for p in d.get("props") or []:
                sites = p.get("write_sites") or []
                if (p.get("write_count") or 0) > 0:
                    writers.append(p)
                if sites:
                    sites_rows.append((r, p))
                if sites and (p.get("write_count") or 0) == 0:
                    bad_rows.append(("a read-only row carries sites", r.get("func_name"), p.get("name")))
                if len(set(sites)) != len(sites):
                    bad_rows.append(("repeated site", r.get("func_name"), p.get("name")))
                if len(sites) > MAX_WRITE_SITES or (len(sites) < MAX_WRITE_SITES and p.get("write_sites_truncated")):
                    bad_rows.append(("cap / truncation flag disagree", r.get("func_name"), p.get("name")))
        print(f"  {walked} walked, {disasm} by disassembly; {len(writers)} written field row(s), "
              f"{len(sites_rows)} with write sites")
        check("native rows that write carry write sites", bool(sites_rows), f"{len(sites_rows)} row(s)")
        check("every written native row carries at least one site",
              all((p.get("write_sites") or []) for p in writers), f"{len(writers)} written row(s)")
        check("no row breaks the site rules (read-only, repeats, the cap)", not bad_rows, str(bad_rows[:3]))

        checked = carried = 0
        misses = []
        for r, p in sites_rows:
            for site in p["write_sites"]:
                if checked >= a.check:
                    break
                rep = c.request("read_mem", addr=site, size=16)
                d = rep.get("data", rep)
                code = bytes.fromhex(d.get("bytes", ""))
                checked += 1
                if carries_displacement(code, int(p.get("offset", -1))):
                    carried += 1
                else:
                    misses.append(f"{r.get('class_name')}.{r.get('func_name')}:{p.get('name')}@{site} "
                                  f"off={p.get('offset')} bytes={code[:8].hex()}")
        check(f"each checked site's bytes carry the field's offset as displacement ({carried}/{checked})",
              checked > 0 and carried == checked, "; ".join(misses[:3]))
        if sites_rows:
            r, p = sites_rows[0]
            print(f"  e.g. {r.get('class_name')}.{r.get('func_name')} writes {p.get('name')} (+0x{p.get('offset', 0):X}) "
                  f"at {', '.join(p['write_sites'])}")

    failed = [n for n, ok, _ in results if not ok]
    print(f"\n{len(results) - len(failed)}/{len(results)} passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
