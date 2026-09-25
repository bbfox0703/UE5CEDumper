"""Inspect, pad and unpad a game's snapshot DB -- the fixture for the snapshot size-cap rows.

    py tools/verify/snapshot_db_pad.py rows  <PE>            # size, user_version, the last 8 snapshots
    py tools/verify/snapshot_db_pad.py pad   <PE> --mb 600   # grow the DB past a Max-size cap (UI closed)
    py tools/verify/snapshot_db_pad.py unpad <PE>            # drop the pad and VACUUM (UI closed)

<PE> is the hash in %LOCALAPPDATA%\\UE5CEDumper\\Snapshots\\snapshots.<PE>.db (view-0.log says `SnapshotStore: active
DB -> ...`). For Review 7 rows R7-D-06 / R7-D-01: the capture's size poll compares db + wal against Max size, so a DB
already past the cap trips it on the one and only chunk of a small capture -- the state R7-D-06 is about.

`rows` opens mode=ro (NOT immutable, which would ignore the -wal). `pad` refuses while UE5DumpUI runs, when the DB is
missing, or when user_version is not the current schema (4); it adds a foreign table `zz_verify_pad` of zeroblob rows
and checkpoints the WAL. `unpad` drops that table and VACUUMs. Never pad another game's DB.
"""
import argparse
import os
import sqlite3
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
SNAP = os.path.join(os.environ["LOCALAPPDATA"], "UE5CEDumper", "Snapshots")
SCHEMA = 4


def db_path(pe):
    return os.path.join(SNAP, f"snapshots.{pe}.db")


def size(p):
    return os.path.getsize(p) if os.path.exists(p) else 0


def ui_running():
    out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq UE5DumpUI.exe", "/NH"], capture_output=True, text=True,
                         errors="replace").stdout
    return "ue5dumpui.exe" in out.lower()


def rows(pe):
    p = db_path(pe)
    if not os.path.exists(p):
        raise SystemExit(f"no DB at {p}")
    db, wal = size(p), size(p + "-wal")
    con = sqlite3.connect(f"file:{p}?mode=ro", uri=True)
    uv = con.execute("PRAGMA user_version").fetchone()[0]
    pad = con.execute("SELECT count(*) FROM sqlite_master WHERE name='zz_verify_pad'").fetchone()[0]
    print(f"{p}\n  db {db / 1048576:.1f} MB + wal {wal / 1048576:.1f} MB = {(db + wal) / 1048576:.1f} MB  "
          f"user_version {uv}  pad table {'present' if pad else 'absent'}")
    for r in con.execute("SELECT id, label, object_count, field_count, is_usable, partial_reason "
                         "FROM snapshots ORDER BY id DESC LIMIT 8"):
        print(f"  id {r[0]:4}  label {r[1]!r:14} objects {r[2]:>7}  fields {r[3]:>8}  usable {r[4]}  partial {r[5]!r}")
    con.close()


def pad(pe, mb):
    if ui_running():
        raise SystemExit("REFUSED: UE5DumpUI is running")
    p = db_path(pe)
    if not os.path.exists(p):
        raise SystemExit(f"REFUSED: no DB at {p}")
    con = sqlite3.connect(p)
    uv = con.execute("PRAGMA user_version").fetchone()[0]
    if uv != SCHEMA:
        raise SystemExit(f"REFUSED: user_version {uv}, expected {SCHEMA}")
    con.execute("CREATE TABLE IF NOT EXISTS zz_verify_pad(b BLOB)")
    for _ in range(mb):
        con.execute("INSERT INTO zz_verify_pad VALUES (zeroblob(1048576))")
    con.commit()
    con.execute("PRAGMA wal_checkpoint(TRUNCATE)")
    con.close()
    rows(pe)


def unpad(pe):
    if ui_running():
        raise SystemExit("REFUSED: UE5DumpUI is running")
    con = sqlite3.connect(db_path(pe))
    con.execute("DROP TABLE IF EXISTS zz_verify_pad")
    con.commit()
    con.execute("VACUUM")
    con.execute("PRAGMA wal_checkpoint(TRUNCATE)")
    con.close()
    rows(pe)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("verb", choices=("rows", "pad", "unpad"))
    ap.add_argument("pe")
    ap.add_argument("--mb", type=int, default=600)
    a = ap.parse_args()
    {"rows": lambda: rows(a.pe), "pad": lambda: pad(a.pe, a.mb), "unpad": lambda: unpad(a.pe)}[a.verb]()
    return 0


if __name__ == "__main__":
    sys.exit(main())
