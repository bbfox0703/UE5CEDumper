r"""Shared harness for every register step that must MUTATE a live game to observe a fix.

    from mutate_guard import Mutation, assert_channel_carries

A11 steps 2-5 and A12 steps 2-4 all have the same shape: read some bytes, write different bytes,
issue one pipe command, assert what changed, and put the original bytes back. Six near-identical
rigs written six times is how build 2743's three defects reached all seven copies of the mailbox
wait at once, so the risky parts live here once.

WHAT THIS GUARANTEES, and why each guarantee exists:

  * **Restore is not best-effort.** `Mutation` is a context manager: the original bytes are captured
    before the first write and written back in `finally`, then **read back and compared**. A restore
    that silently failed would leave a synthetic `TArray` header installed, and `TArray`'s destructor
    would later call `FMemory::Free` on a pointer it does not own — a crash attributed to the game,
    minutes later, by someone who has forgotten the rig ran.
  * **The poke is witnessed.** `apply()` re-reads and asserts the bytes are what was asked for
    BEFORE the caller draws any conclusion. `write_mem` returning ok is not the same as the memory
    holding the value: it is the DLL's report about its own SEH-guarded write.
  * **Only what was named changes.** `expect_unchanged` regions are captured with the target and
    re-checked after the poke. A rig that intends to flip one allocation bit and in fact rewrites a
    count has produced a result about a different experiment.
  * **An absence is only evidence once the CHANNEL is shown to carry the thing.**
    `assert_channel_carries` exists because A11 step 6 passed for a year against `scan-0.log`, a
    file `Refine re-anchor:` can never reach — the marker is `LOG_CAT "OARR"`, which `Sein.cpp`
    routes to `offsets-0.log`. Grepping the wrong file returns zero for both "the code did not do
    it" and "I am reading the wrong channel". [A11-LOGPATH-2026-08-21]

⚠ A SYNTHETIC MUTATION IS NOT THE SAME EVENT AS THE GAME DOING IT. Writing a new `{dataPtr,count}`
reproduces what the SCANNER OBSERVES about a realloc, not a realloc: no allocator ran, the old
buffer is still mapped, nothing was freed. That is enough for a re-anchor rule that reads the
header and compares — and it is NOT enough for anything that depends on the old memory becoming
invalid. Every caller must say which of the two it is relying on.
"""
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))


def _say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + chr(10))
    sys.stdout.flush()


def read_bytes(client, addr, n):
    """Hex-decoded bytes at addr, or None. Accepts int or str addresses."""
    a = addr if isinstance(addr, str) else ("0x%X" % addr)
    r = client.request("read_mem", addr=a, size=n)
    hexs = r.get("bytes") or r.get("hex") or ""
    hexs = hexs.replace(" ", "").replace("0x", "")
    if not hexs:
        return None
    try:
        return bytes.fromhex(hexs)
    except ValueError:
        return None


def write_bytes(client, addr, data):
    a = addr if isinstance(addr, str) else ("0x%X" % addr)
    r = client.request("write_mem", addr=a, bytes=data.hex().upper())
    return bool(r.get("ok"))


def assert_channel_carries(log_path, marker, label="the marker"):
    """An absence is evidence only if this channel demonstrably carries that traffic.

    `marker` should be the CATEGORY tag (e.g. "[OARR]"), not the message — the message is the
    thing whose absence is being claimed, so requiring it here would defeat the purpose.
    """
    p = pathlib.Path(log_path)
    if not p.exists():
        _say("DETECTOR CHECK FAILED: %s does not exist — an absence of %s proves nothing."
             % (p, label))
        return False
    if marker not in p.read_text(encoding="utf-8", errors="replace"):
        _say("DETECTOR CHECK FAILED: %s carries no %s line, so it is not the channel that "
             "category is routed to (Sein.cpp's table is the authority). An absence of %s "
             "proves nothing." % (p.name, marker, label))
        return False
    _say("detector OK: %s carries %s traffic — an absent %s is a real absence"
         % (p.name, marker, label))
    return True


class Mutation:
    """Capture, poke, witness, restore — with the restore verified by read-back.

        with Mutation(c, "Arr_Int header", header, 16,
                      expect_unchanged={"CDO header": (cdo_hdr, 16)}) as m:
            m.apply(struct.pack("<Qii", buf2, 7, 16))
            ...                       # observe
        # original bytes are back, and that has been checked
    """

    def __init__(self, client, label, addr, size, expect_unchanged=None):
        self.c, self.label, self.addr, self.size = client, label, addr, size
        self.expect_unchanged = dict(expect_unchanged or {})
        self.original = None
        self._witness = {}
        self.ok = True

    def __enter__(self):
        self.original = read_bytes(self.c, self.addr, self.size)
        if self.original is None or len(self.original) != self.size:
            raise RuntimeError("%s: could not read %d bytes at 0x%X before mutating — refusing "
                               "to write without a restore point" % (self.label, self.size, self.addr))
        for name, (a, n) in self.expect_unchanged.items():
            b = read_bytes(self.c, a, n)
            if b is None or len(b) != n:
                raise RuntimeError("%s: could not snapshot the unchanged-region %r" % (self.label, name))
            self._witness[name] = b
        _say("captured %s: %d bytes at 0x%X = %s" % (self.label, self.size, self.addr,
                                                     self.original.hex().upper()))
        return self

    def apply(self, data):
        """Write, then PROVE the memory holds it. write_mem's ok is the DLL's report, not a read."""
        if len(data) != self.size:
            raise ValueError("%s: apply() got %d bytes for a %d-byte region — a partial write "
                             "would leave a restore point that no longer describes the region"
                             % (self.label, len(data), self.size))
        if not write_bytes(self.c, self.addr, data):
            self.ok = False
            _say("WRITE REFUSED at 0x%X (%s)" % (self.addr, self.label))
            return False
        back = read_bytes(self.c, self.addr, self.size)
        if back != data:
            self.ok = False
            _say("WRITE NOT WITNESSED at 0x%X: wanted %s, read %s"
                 % (self.addr, data.hex().upper(), (back or b"").hex().upper()))
            return False
        _say("poked %s -> %s (witnessed by read-back)" % (self.label, data.hex().upper()))
        return True

    def assert_others_unchanged(self):
        """Only what was named may have moved. A rig that also rewrote a count measured something
        other than the experiment it describes."""
        good = True
        for name, before in self._witness.items():
            a, n = self.expect_unchanged[name]
            now = read_bytes(self.c, a, n)
            if now != before:
                good = False
                _say("COLLATERAL CHANGE in %r: %s -> %s"
                     % (name, before.hex().upper(), (now or b"").hex().upper()))
        if good and self._witness:
            _say("no collateral change in %d guarded region(s)" % len(self._witness))
        return good

    def __exit__(self, exc_type, exc, tb):
        # Restore FIRST, whatever happened above, and prove it.
        restored = write_bytes(self.c, self.addr, self.original)
        back = read_bytes(self.c, self.addr, self.size)
        if not restored or back != self.original:
            _say("⛔⛔ RESTORE FAILED at 0x%X (%s). The process is now holding synthetic bytes; "
                 "KILL IT rather than letting it exit cleanly — a synthetic TArray header makes "
                 "the destructor free a pointer it does not own."
                 % (self.addr, self.label))
            self.ok = False
        else:
            _say("restored %s (verified by read-back)" % self.label)
        return False    # never swallow an exception
