"""Does `walk_class` (singular) give the same field table as `walk_class_batch` for the same class?

    py tools/verify/walkclass_singular_vs_batch.py              # 300 classes, singular before and after the batch
    py tools/verify/walkclass_singular_vs_batch.py --sample 1000

Headless, through the pipe (the UI must NOT be connected). [LEAD-WALKCLASS-SINGULAR-ZERO]: on a UE 4.23 fixture a
singular walk_class was once seen returning 0 fields for a class walk_class_batch walked to 76. Both handlers call
the same Ubel::WalkClassEx, so a real difference would have to come from the call or from time: each class is walked
singly FIRST (before anything else has walked it), then in batches, then singly again, and every count is compared.

⚠ The two replies are not shaped alike: the singular puts its class under `class` (`reply["class"]["fields"]`), the
batch under `classes` (`reply["classes"][i]["fields"]`). A singular reply read as `reply["fields"]` counts 0 for every
class -- this rig reads each where it is, and also prints that misreading's count so the two can be told apart.
"""
import argparse
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--sample", type=int, default=300, help="how many loaded classes to compare")
    ap.add_argument("--batch", type=int, default=60)
    a = ap.parse_args()

    with PipeClient(timeout=300) as pc:
        print("build:", pc.request("get_pointers").get("build_number"))
        pc.ensure_scanned()

        listing = pc.request("list_classes", limit=a.sample, game_only=False)
        addrs = [c["class_addr"] for c in listing.get("classes", []) if c.get("class_addr")]
        print(f"classes offered: {len(addrs)}")
        if not addrs:
            print("BAD: no classes listed")
            return 1

        def singular(addr):
            r = pc.request("walk_class", addr=addr)
            cls = r.get("class") or {}
            return bool(r.get("ok")), cls.get("name", "?"), len(cls.get("fields") or []), len(r.get("fields") or [])

        first = {addr: singular(addr) for addr in addrs}

        batch = {}
        for i in range(0, len(addrs), a.batch):
            chunk = addrs[i:i + a.batch]
            r = pc.request("walk_class_batch", addrs=chunk)
            classes = r.get("classes") or []
            if not r.get("ok") or len(classes) != len(chunk):
                print(f"BAD: batch at {i} ok={r.get('ok')} returned {len(classes)} of {len(chunk)}")
                return 1
            for addr, cls in zip(chunk, classes):
                batch[addr] = len(cls.get("fields") or [])

        again = {addr: singular(addr) for addr in addrs}

        differ, misread_zero, with_fields = [], 0, 0
        for addr in addrs:
            ok1, name, n1, top1 = first[addr]
            ok2, _, n2, _ = again[addr]
            nb = batch[addr]
            if nb:
                with_fields += 1
                if top1 == 0:
                    misread_zero += 1
            if not (ok1 and ok2) or not (n1 == nb == n2):
                differ.append(f"  {name} @ {addr}: singular first {n1} (ok={ok1}), batch {nb}, singular after {n2} "
                              f"(ok={ok2})")

        print(f"classes with fields (batch): {with_fields}")
        print(f"singular read where the reply puts it (`class.fields`) vs batch vs singular again: "
              f"{len(addrs) - len(differ)} of {len(addrs)} agree")
        print(f"the same singular replies read as top-level `fields`: 0 fields for {misread_zero} of the "
              f"{with_fields} classes that have fields")
        for line in differ[:40]:
            print(line)
        print("OK" if not differ else f"BAD: {len(differ)} class(es) differ")
        return 0 if not differ else 1


if __name__ == "__main__":
    sys.exit(main())
