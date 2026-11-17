r"""U2 step 2: TArray<FName> / TMap<FName,V> / TSet<FName> elements on a case-preserving (CPN) host, over the pipe.

    py tools/verify/u2_cpn_names_live.py --pid 1234

The host is the UE 5.4 editor running the DumperTest project with -game (WITH_CASE_PRESERVING_NAME follows
WITH_EDITORONLY_DATA there, so sizeof(FName) is 12: ComparisonIndex, Number, DisplayIndex). DumperTestActor seeds
Arr_Name = NameA, NameBB, NameCCC; Map_NameToInt = Alpha 111, Beta 222, Gamma 333; Set_Name with four names. An 8-byte
FName walk gives strides 20 / 16 and garbled elements after [0]; the right ones are 12 / 24 / 20.
The independent check reads Arr_Name's 36 bytes with read_mem: three 12-byte FNames, each with a distinct non-zero
ComparisonIndex, a zero Number and a non-zero DisplayIndex. (No pipe command resolves an FName index, so the bytes are
checked for the layout, not decoded; an 8-byte walk would read element 1 from element 0's DisplayIndex.)
The game and its pid are arguments.
"""
import argparse
import pathlib
import struct
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient   # noqa: E402

results: list[tuple[str, bool, str]] = []


def check(name: str, ok: bool, detail: str = "") -> bool:
    results.append((name, ok, detail))
    print(f"  [{'PASS' if ok else 'FAIL'}] {name}" + (f" -- {detail}" if detail else ""))
    return ok


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--pid", type=int, required=True)
    a = ap.parse_args()
    with PipeClient(pid=a.pid, timeout=300.0) as c:
        print("build", c.assert_build())
        c.ensure_scanned(timeout=300.0)
        off = c.request("get_offsets")
        od = off.get("data", off)
        check("get_offsets: case_preserving true and the probe ran",
              od.get("case_preserving") is True and od.get("probe_ran") is True,
              f"case_preserving={od.get('case_preserving')} probe_ran={od.get('probe_ran')}")
        r = c.request("find_instances", class_name="DumperTestActor", limit=100, exact_match=True)
        live = [i for i in r.get("instances", []) if not i["name"].startswith("Default__")]
        if not check("a live DumperTestActor", bool(live), str(len(live))):
            return 1
        w = c.request("walk_instance", addr=live[0]["addr"], array_limit=64)
        fields = {f.get("name"): f for f in (w.get("data", w).get("fields") or [])}
        arr, mp, st = fields.get("Arr_Name") or {}, fields.get("Map_NameToInt") or {}, fields.get("Set_Name") or {}
        arr_v = [e.get("v") for e in (arr.get("elements") or [])]
        map_kv = {e.get("k"): str(e.get("v")) for e in (mp.get("map_elements") or [])}
        set_k = [e.get("k") for e in (st.get("set_elements") or [])]
        print("  Arr_Name:", arr.get("array_elem_size"), arr_v)
        print("  Map_NameToInt:", mp.get("map_count"), mp.get("map_stride"), map_kv)
        print("  Set_Name:", st.get("set_count"), st.get("set_stride"), set_k)
        check("Arr_Name = NameA, NameBB, NameCCC, in order", arr_v[:3] == ["NameA", "NameBB", "NameCCC"], str(arr_v))
        check("Arr_Name's element size is 12", arr.get("array_elem_size") == 12, str(arr.get("array_elem_size")))
        check("Map_NameToInt = Alpha 111, Beta 222, Gamma 333",
              map_kv.get("Alpha") == "111" and map_kv.get("Beta") == "222" and map_kv.get("Gamma") == "333",
              str(map_kv))
        check("map_stride 24", mp.get("map_stride") == 24, str(mp.get("map_stride")))
        check("Set_Name holds 4 distinct names, set_stride 20",
              len(set_k) == 4 and len(set(set_k)) == 4 and st.get("set_stride") == 20,
              f"{set_k} stride {st.get('set_stride')}")
        data_addr = arr.get("array_data_addr")
        m = c.request("read_mem", addr=data_addr, size=36) if data_addr else {}
        raw = bytes.fromhex(m.get("data", m).get("bytes", "")) if data_addr else b""
        if check("read_mem: Arr_Name's 36 bytes", len(raw) == 36, f"{data_addr} -> {len(raw)} bytes"):
            cmp_idx = [struct.unpack_from("<I", raw, 12 * i)[0] for i in range(3)]
            number = [struct.unpack_from("<I", raw, 12 * i + 4)[0] for i in range(3)]
            disp = [struct.unpack_from("<I", raw, 12 * i + 8)[0] for i in range(3)]
            print(f"  bytes {raw.hex()}  comparison {cmp_idx} number {number} display {disp}")
            check("each 12-byte element: a distinct non-zero ComparisonIndex, Number 0, a non-zero DisplayIndex",
                  len(set(cmp_idx)) == 3 and all(cmp_idx) and number == [0, 0, 0] and all(disp),
                  f"comparison {cmp_idx} number {number} display {disp}")
    failed = [n for n, ok, _ in results if not ok]
    print(f"\n{len(results) - len(failed)}/{len(results)} passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
