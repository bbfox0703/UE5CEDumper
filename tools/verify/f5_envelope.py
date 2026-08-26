"""F5: prove the pipe's two-WriteFile split never truncates or interleaves a reply.

    py f5_envelope.py step1        # big replies: one JSON object per line, envelope intact
    py f5_envelope.py step2        # interleave control: watch events vs ordinary commands
    py f5_envelope.py all

WHAT F5 CHANGED (audit L11, build 3263). `Fern::WriteLine` no longer materialises a
`line + "\\n"` copy: the payload and the terminator now go out as TWO `WriteFile`
calls under the same `writeMutex` on a BYTE-MODE pipe. And `MakeResponse`/`MakeEvent`
assign envelope keys individually instead of splicing with nlohmann's `merge_patch`,
so a payload can no longer delete an envelope key or replace the whole object.

WHY IT NEEDS A LIVE CHECK AT ALL. `Renge::ApplyPayload` is pinned by 16 assertions in
`dll_helpers_test` because that header IS compiled there -- but **no test target
compiles `Fern.cpp`**, so the two-write split has never executed anywhere. If a
reader can observe the gap between the two writes, or another thread's write can land
between them, the failure is a TRUNCATED or INTERLEAVED line.

WHY THIS RIG READS RAW BYTES AND NOT PARSED OBJECTS. `pipe_client.PipeClient.request`
silently skips any line it cannot parse (`except json.JSONDecodeError: continue`) --
correct for driving the DLL, fatally wrong for THIS test, whose entire subject is
whether a malformed line ever appears. A garbled line would be dropped and the run
would report a clean pass. So this rig keeps every byte and judges the lines itself.

WHAT COUNTS AS A FAILURE
  * a line that does not parse as JSON            -> truncation or interleaving
  * a line holding more than one JSON object      -> a missing terminator
  * a reply missing `id` / `ok` / `game_thread_stalled` -> the envelope regression
    ⚠ `game_thread_stalled` is only present when the DLL can MEASURE it, i.e. once a
    ProcessEvent hook is installed (STALLDEFAULT-2026-08-26). The rig ARMS the detector
    with `pe_profile_start` before probing and keeps the three-key assertion. If arming
    fails it drops that key FOR THAT RUN ONLY and says so -- it does not weaken the
    assertion to "absent or boolean", which would make a future deletion of the stamp
    undetectable.
  * a `\\n` inside a JSON string that splits a line -> the split writing out of order
"""
import json
import pathlib
import sys
import threading
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient, PIPE            # noqa: E402

ENVELOPE_FULL = ("id", "ok", "game_thread_stalled")
ENVELOPE_UNARMED = ("id", "ok")


def arm_liveness(c):
    """Install the ProcessEvent hook so `game_thread_stalled` is a MEASUREMENT.

    Returns the envelope keys to assert. Absence of the key is legitimate when no hook
    is installed, so a failed arm must not report as an envelope regression -- but it
    must be said out loud, or a run that asserted two keys instead of three looks
    identical to one that asserted three. (STALLDEFAULT-2026-08-26)
    """
    try:
        c.request("pe_profile_start")
        time.sleep(1.5)
        probe = c.request("get_pointers")
        if "game_thread_stalled" in probe:
            print("  liveness detector ARMED (game_thread_stalled = %r)"
                  % probe["game_thread_stalled"])
            return ENVELOPE_FULL
    except Exception as e:
        print("  liveness arm FAILED: %s" % e)
    print("  detector NOT armed -- game_thread_stalled is legitimately absent this run; "
          "asserting id/ok only")
    return ENVELOPE_UNARMED


class RawClient(PipeClient):
    """PipeClient that KEEPS every line it sees, malformed ones included."""

    def __init__(self, *a, **kw):
        super().__init__(*a, **kw)
        self.raw_lines: list[bytes] = []
        self.bad: list[tuple[str, bytes]] = []

    def request(self, cmd, **params):
        if not self._f:
            raise RuntimeError("not connected")
        self._id += 1
        rid = self._id
        msg = {"id": rid, "cmd": cmd}
        msg.update(params)
        self._f.write((json.dumps(msg) + "\n").encode("utf-8"))

        deadline = time.time() + self.timeout
        while time.time() < deadline:
            chunk = self._f.read(65536)
            if not chunk:
                raise RuntimeError(f"pipe closed waiting for id={rid} ({cmd})")
            self._buf.extend(chunk)
            while True:
                nl = self._buf.find(b"\n")
                if nl < 0:
                    break
                line = bytes(self._buf[:nl])
                del self._buf[:nl + 1]
                if not line.strip():
                    continue
                self.raw_lines.append(line)
                obj = self._judge(line)
                if obj is not None and obj.get("id") == rid:
                    return obj
                if obj is not None:
                    # Keep it, exactly as the base class does. Dropping this line was a
                    # rig bug that made the interleave control report "0 events" while
                    # 186,463 event lines had in fact arrived.
                    self.events.append(obj)
        raise RuntimeError(f"timed out waiting for id={rid} ({cmd})")

    def _judge(self, line: bytes):
        """Parse and record any defect. Returns the object, or None if unparseable."""
        text = line.decode("utf-8", "replace")
        try:
            obj = json.loads(text)
        except json.JSONDecodeError as e:
            # Distinguish the two failure shapes -- they mean different bugs.
            try:
                dec = json.JSONDecoder()
                _, end = dec.raw_decode(text)
                if text[end:].strip():
                    self.bad.append(("TWO OBJECTS ON ONE LINE (missing terminator)", line))
                else:
                    self.bad.append((f"unparseable: {e}", line))
            except Exception:
                self.bad.append((f"TRUNCATED or garbled: {e}", line))
            return None
        if not isinstance(obj, dict):
            self.bad.append(("line is not a JSON object", line))
            return None
        return obj


def report(c, label):
    print(f"\n  {label}: {len(c.raw_lines)} line(s) seen, {len(c.bad)} malformed")
    for why, line in c.bad[:5]:
        print(f"    !! {why}\n       {line[:200]!r}")
    return not c.bad


def step1():
    """Big replies -- the ones whose second copy the fix removed."""
    print("=== F5 step 1: envelope + one-object-per-line on the BIG replies ===")
    ok = True
    with RawClient(timeout=300.0) as c:
        c.assert_build()
        c.ensure_scanned()

        probes = [
            ("list_all_functions", {}),
            ("list_classes", {}),
            ("find_instances", {"class_name": "Actor", "max_results": 5000}),
            ("begin_snapshot", {}),
        ]
        ENVELOPE = arm_liveness(c)
        snapshot_started = False
        for cmd, args in probes:
            t0 = time.time()
            try:
                r = c.request(cmd, **args)
            except Exception as e:
                print(f"  {cmd:<22} ERROR {e}")
                ok = False
                continue
            dt = time.time() - t0
            missing = [k for k in ENVELOPE if k not in r]
            size = len(json.dumps(r))
            print(f"  {cmd:<22} {dt:6.2f}s  {size:>9,} B  ok={r.get('ok')}  "
                  f"envelope={('ALL %d' % len(ENVELOPE)) if not missing else 'MISSING ' + ','.join(missing)}")
            if missing:
                ok = False
            if cmd == "begin_snapshot" and r.get("ok"):
                snapshot_started = True

        # snapshot_chunk is named explicitly by the row; it only exists after begin.
        if snapshot_started:
            for i in range(3):
                r = c.request("snapshot_chunk")
                missing = [k for k in ENVELOPE if k not in r]
                print(f"  snapshot_chunk[{i}]        {len(json.dumps(r)):>9,} B  "
                      f"ok={r.get('ok')}  envelope="
                      f"{('ALL %d' % len(ENVELOPE)) if not missing else 'MISSING ' + ','.join(missing)}")
                if missing:
                    ok = False
                if r.get("done") or not r.get("ok"):
                    break
        else:
            print("  snapshot_chunk         SKIPPED (begin_snapshot did not start one)")

        ok = report(c, "step 1 wire") and ok
    print(f"\nSTEP 1: {'PASS' if ok else 'FAIL'}")
    return ok


def step2(seconds=60):
    """Interleave control: EVT_WATCH pushed on one connection, commands on another.

    Two writers, one writeMutex. If the mutex does not actually cover BOTH WriteFile
    calls, an event lands inside a response and the line is garbage.
    """
    print(f"=== F5 step 2: interleave control, {seconds}s, two connections ===")
    stop = threading.Event()
    results = {}

    def watcher():
        try:
            with RawClient(timeout=30.0) as w:
                p = w.request("get_pointers")
                # Watch a region that actually CHANGES, or no events are pushed and the
                # whole interleave control passes vacuously. The GWorld *slot* is static;
                # the UWorld it points at holds ticking floats (TimeSeconds and friends).
                gw = int(p["gworld"], 16)
                deref = w.request("read_mem", addr=hex(gw), size=8)
                uworld = deref.get("data") or deref.get("bytes") or ""
                target = None
                if isinstance(uworld, str) and len(uworld) >= 16:
                    raw = uworld.replace(" ", "")[:16]
                    try:                       # little-endian hex byte string
                        target = int.from_bytes(bytes.fromhex(raw), "little")
                    except ValueError:
                        target = None
                addr = hex(target) if target else p["gworld"]
                # NOTE the parameter is `addr`, not `address` (Fern.cpp:4961). Passing the
                # wrong name is silently accepted as addr="" -- a watch on nothing.
                r = w.request("watch", addr=addr, size=1024, interval_ms=50)
                results["watch_started"] = bool(r.get("ok")) and addr not in ("", "0x0")
                results["watch_addr"] = addr
                t_end = time.time() + seconds
                while time.time() < t_end and not stop.is_set():
                    try:
                        w.request("get_pointers")   # keeps the reader draining
                    except Exception:
                        break
                # Count from the RAW lines, not from a parsed list: the raw record is the
                # thing under test, and it cannot be thrown off by a bookkeeping slip.
                # Renge::EVT_WATCH is literally "watch" (Renge.h:183).
                evts = sum(1 for ln in w.raw_lines if b'"watch"' in ln)
                results["watch"] = (len(w.raw_lines), list(w.bad))
                results["events"] = evts
        except Exception as e:
            results["watch"] = (0, [("watcher died: %s" % e, b"")])

    th = threading.Thread(target=watcher, daemon=True)
    th.start()

    with RawClient(timeout=120.0) as c:
        t_end = time.time() + seconds
        n = 0
        while time.time() < t_end:
            c.request("get_object_count")
            c.request("get_pointers")
            c.request("find_instances", class_name="Actor", max_results=200)
            n += 3
        print(f"  main connection issued {n} commands")
        ok_main = report(c, "step 2 main")
    stop.set()
    th.join(timeout=30)

    lines, bad = results.get("watch", (0, []))
    evts = results.get("events", 0)
    print(f"  watch connection: {lines} line(s) seen, {len(bad)} malformed, "
          f"{evts} watch event(s) on addr {results.get('watch_addr')}")
    for why, line in bad[:5]:
        print(f"    !! {why}\n       {line[:200]!r}")

    # A control that produced no concurrent traffic proves nothing. Say so rather
    # than printing PASS -- "no malformed line" is trivially true of no lines.
    if not results.get("watch_started"):
        print("\nSTEP 2: INCONCLUSIVE -- the watch never started, so nothing was "
              "interleaved and 'no malformed line' is vacuous.")
        return False
    if evts == 0:
        print("\nSTEP 2: INCONCLUSIVE -- watch started but pushed 0 events, so no second "
              "writer ever competed for writeMutex. Pick an address that actually changes.")
        return False

    ok = ok_main and not bad
    print(f"\nSTEP 2: {'PASS' if ok else 'FAIL'}")
    return ok


if __name__ == "__main__":
    what = sys.argv[1] if len(sys.argv) > 1 else "all"
    good = True
    if what in ("step1", "all"):
        good &= step1()
    if what in ("step2", "all"):
        good &= step2(int(sys.argv[2]) if len(sys.argv) > 2 else 60)
    sys.exit(0 if good else 1)
