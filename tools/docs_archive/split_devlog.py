"""Split the oldest entries of docs/dev-log.md into an archive file, byte-identical, on a DATE boundary.

    py tools/docs_archive/split_devlog.py split <dev-log> <cut YYYY-MM-DD> <archive file> <header file> [--dry-run]
    py tools/docs_archive/split_devlog.py verify <dev-log> <archive file>

The log is newest first: a header, then entries headed `## YYYY-MM-DD ...` separated by `-----` lines, then an
`## Older entries` section. `split` moves every entry dated before the cut -- a contiguous run just above
`## Older entries`, so a day is never split -- into the archive file, after the header file's text and a `-----`
line. It refuses an entry out of date order, a cut that moves nothing or everything, and an existing archive file,
and asserts before writing that the kept text plus the moved text rebuild the original exactly.

The live file's header list and `## Older entries` pointer still have to name the new file: edit them afterwards.
`verify` then checks, from git's HEAD copy of the log, that the live entries plus the archive's entries are exactly
the original entries in order, so only the header and the `## Older entries` section may differ.
"""
import pathlib
import re
import subprocess
import sys

ENTRY = re.compile(r"^## (\d{4}-\d{2}-\d{2})\b")


def parts(text):
    """(header, entries, trailer) as line lists; entries run from the first entry heading to `## Older entries`."""
    lines = text.split("\n")
    first = next(i for i, l in enumerate(lines) if ENTRY.match(l))
    older = next(i for i, l in enumerate(lines) if l.startswith("## Older entries"))
    assert first < older, "no entry before ## Older entries"
    return lines[:first], lines[first:older], lines[older:]


def entry_starts(entries):
    return [i for i, l in enumerate(entries) if ENTRY.match(l)]


def read(p):
    raw = pathlib.Path(p).read_bytes().decode("utf-8")
    return raw.replace("\r\n", "\n"), "\r\n" in raw


def split(log, cut, arch, head, dry):
    text, crlf = read(log)
    header, entries, trailer = parts(text)
    starts = entry_starts(entries)
    dates = [ENTRY.match(entries[i]).group(1) for i in starts]
    assert all(a >= b for a, b in zip(dates, dates[1:])), "entries are not newest first"
    k = next((n for n, d in enumerate(dates) if d < cut), None)
    assert k is not None and k > 0, "the cut moves nothing or everything"
    kept, moved = entries[:starts[k]], entries[starts[k]:]
    assert header + kept + moved + trailer == text.split("\n"), "kept + moved does not rebuild the original"
    assert not pathlib.Path(arch).exists(), f"{arch} exists already"
    builds = [int(b) for l in moved if ENTRY.match(l) for b in re.findall(r"\bbuilds? (\d{3,5})", l)]
    size = lambda ls: sum(len(x.encode("utf-8")) + 1 for x in ls)
    print(f"moving {len(starts) - k} entries ({dates[-1]} .. {dates[k]}, {size(moved):,} bytes), keeping "
          f"{k} ({dates[k - 1]} .. {dates[0]}, {size(kept):,} bytes); builds named in moved headings: "
          f"{min(builds) if builds else '-'}..{max(builds) if builds else '-'}")
    if dry:
        return
    hdr = pathlib.Path(head).read_text(encoding="utf-8").replace("\r\n", "\n").rstrip("\n").split("\n")
    live = "\n".join(header + kept + trailer)
    pathlib.Path(log).write_bytes((live.replace("\n", "\r\n") if crlf else live).encode("utf-8"))
    pathlib.Path(arch).write_bytes(("\n".join(hdr + ["", "-----", ""] + moved).rstrip("\n") + "\n").encode("utf-8"))
    print(f"wrote {log} and {arch}")


def verify(log, arch):
    rel = pathlib.Path(log).resolve().relative_to(pathlib.Path.cwd().resolve()).as_posix()
    orig = subprocess.run(["git", "show", f"HEAD:{rel}"], capture_output=True, check=True).stdout.decode("utf-8")
    _, orig_entries, _ = parts(orig.replace("\r\n", "\n"))
    _, live_entries, _ = parts(read(log)[0])
    alines = read(arch)[0].split("\n")
    a0 = next(i for i, l in enumerate(alines) if ENTRY.match(l))
    body = alines[a0:]
    while body and body[-1] == "":
        body.pop()
    joined = live_entries + body
    tail = orig_entries[len(joined):]
    assert orig_entries[:len(joined)] == joined and all(not x.strip() for x in tail), \
        "the live entries plus the archive's entries are not the original entries"
    print(f"OK: {len(entry_starts(live_entries))} live + {len(entry_starts(body))} archived entries == the "
          f"{len(entry_starts(orig_entries))} original entries, byte for byte")


if __name__ == "__main__":
    a = sys.argv[1:]
    if a and a[0] == "split" and len(a) >= 5:
        split(a[1], a[2], a[3], a[4], "--dry-run" in a)
    elif a and a[0] == "verify" and len(a) == 3:
        verify(a[1], a[2])
    else:
        sys.exit(__doc__)
