"""Drive the injected UE5Dumper DLL over its named pipe, from Python, with no UI.

This is the rig `docs/working-lessons.md` §2.6 describes: the UI is a *client*, not the subject,
so every DLL-side row on the verification register can be driven straight over the game's pipe.
It is also STRONGER evidence for a DLL-side claim than a UI session,
because it removes Avalonia as a variable -- and correspondingly proves nothing about the panel's
own bindings, which must be said explicitly when a result is recorded.

Which game ([PIPE-PER-PROCESS-2026-11-06]). Each game serves `\\\\.\\pipe\\UE5DumpBfx_<pid>`. The
client dials, in this order: the `pid=` it was given (`--pid` on this script's command line), then
the `UE5DUMP_PID` environment variable (it reaches every rig and the children they start), then the
only game listed under `\\\\.\\pipe\\`. With several games it takes the lowest pid, prints them all
and warns. When only the legacy `\\\\.\\pipe\\UE5DumpBfx` is there, a game runs an old DLL (a proxy
never refreshed): it refuses and says so. A rig that already knows the game's pid passes it.

The three traps from §2.6 are enforced here rather than left to the caller, because each one
produces a *confident wrong answer* rather than an error:

1. **A deployed proxy ignores a fresh injection.** `injectDLL` returns true, the DLL loads, and the
   OLD proxy keeps serving the pipe. `assert_build()` refuses to proceed unless the DLL's reported
   `build_number` matches `dist/build_number.txt`.
2. **Proxy mode does not scan on load, and `init` does not make it.** `ensure_scanned()` sends
   `trigger_scan` and polls until the pointers actually resolve.
3. **A result cap turns an absence claim into a sample.** `check_complete()` fails loudly on
   `deadline_hit` / `truncated`, so "I saw none" cannot be reported as "there are none".

Usage as a library:

    from pipe_client import PipeClient
    with PipeClient() as c:
        c.assert_build()                 # trap 1
        c.ensure_scanned()               # trap 2
        r = c.request("find_instances", class_name="DumperTestActor", max_results=1000)
        c.check_complete(r)              # trap 3

Usage from the shell:

    py tools/verify/pipe_client.py get_pointers
    py tools/verify/pipe_client.py get_pointers --pid 12345
    py tools/verify/pipe_client.py find_instances --args '{"class_name":"DumperTestActor"}'
"""

from __future__ import annotations

import argparse
import ctypes
import json
import os
import pathlib
import sys
import time

# [PIPE-PER-PROCESS-2026-11-06] Each game serves \\.\pipe\UE5DumpBfx_<pid>; the legacy name is what every DLL served
# before, and an old DLL (a stale proxy) still does.
PIPE_PREFIX = "UE5DumpBfx_"
LEGACY_PIPE = "UE5DumpBfx"
PID_ENV = "UE5DUMP_PID"
PIPE_ROOT = "\\\\.\\pipe\\"
DIST_BUILD = pathlib.Path(__file__).resolve().parents[2] / "dist" / "build_number.txt"

# Seams for the selftest: the listing of \\.\pipe\ and the environment.
_listdir = os.listdir
_environ = os.environ


class PipeError(RuntimeError):
    pass


def pipe_name(pid: int) -> str:
    """The pipe a game process serves, bare (no \\\\.\\pipe\\ path)."""
    return PIPE_PREFIX + str(int(pid))


def pipe_path(pid: int) -> str:
    """The full path a client opens for game `pid`."""
    return PIPE_ROOT + pipe_name(pid)


def parse_pid(name: str) -> int | None:
    """The pid in a per-process pipe name, or None for any other name. Exact: the prefix (without case, as Windows
    compares pipe names), then only ASCII digits, a positive pid. A rig's own "UE5DumpBfx-probe-..." pipe is no game."""
    if not name.lower().startswith(PIPE_PREFIX.lower()):
        return None
    digits = name[len(PIPE_PREFIX):]
    if not digits or not all("0" <= ch <= "9" for ch in digits):
        return None
    pid = int(digits)
    return pid if 0 < pid <= 0xFFFFFFFF else None


def list_dumper_pipes() -> tuple[list[int], bool]:
    """The game pids serving a pipe (ascending), and whether the legacy pipe is there. Listing opens no pipe."""
    names = _listdir("//./pipe/")
    pids = sorted({p for p in (parse_pid(n) for n in names) if p is not None})
    legacy = any(n.lower() == LEGACY_PIPE.lower() for n in names)
    return pids, legacy


def process_image(pid: int) -> str:
    """The exe file name of `pid`, or "?" when it cannot be read: for the several-games warning only."""
    if sys.platform != "win32":
        return "?"
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.OpenProcess.restype = ctypes.c_void_p
    h = k32.OpenProcess(0x1000, False, int(pid))   # PROCESS_QUERY_LIMITED_INFORMATION
    if not h:
        return "?"
    try:
        buf = ctypes.create_unicode_buffer(1024)
        size = ctypes.c_ulong(len(buf))
        if not k32.QueryFullProcessImageNameW(ctypes.c_void_p(h), 0, buf, ctypes.byref(size)):
            return "?"
        return os.path.basename(buf.value) or "?"
    finally:
        k32.CloseHandle(ctypes.c_void_p(h))


def pipe_is_busy(path: str) -> bool:
    """True when the pipe exists and every instance is taken (ERROR_PIPE_BUSY). open() reports that as EINVAL
    (errno 22), like several other failures (measured 2026-11-10); WaitNamedPipeW tells it apart, ERROR_SEM_TIMEOUT
    (121), without connecting."""
    if sys.platform != "win32":
        return False
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.WaitNamedPipeW.argtypes = [ctypes.c_wchar_p, ctypes.c_ulong]
    if k32.WaitNamedPipeW(path, 1):   # 1 ms; 0 would be NMPWAIT_USE_DEFAULT_WAIT
        return False                  # an instance is free now
    return ctypes.get_last_error() == 121


# Seam for the selftest.
_pipe_is_busy = pipe_is_busy


def resolve_pipe(pid: int | None = None, *, warn=None) -> str:
    """Which game's pipe to dial: `pid`, else $UE5DUMP_PID, else the one game listed. Several games: the lowest pid,
    with every game printed and a warning (to `warn`, stderr by default). Raises PipeError when no game serves a
    pipe, and says so when only the legacy pipe is there."""
    if pid is None and _environ.get(PID_ENV, "").strip():
        try:
            pid = int(_environ[PID_ENV])
        except ValueError:
            raise PipeError(f"{PID_ENV}={_environ[PID_ENV]!r} is not a pid")
    if pid is not None:
        return pipe_path(pid)
    pids, legacy = list_dumper_pipes()
    if not pids:
        if legacy:
            raise PipeError(
                f"only the legacy pipe {PIPE_ROOT}{LEGACY_PIPE} is up: a game runs an OLD UE5Dumper DLL, which this "
                f"client does not drive. Refresh its proxy (py tools/verify/proxy_refresh.py refresh \"<title>\") or "
                f"inject the current DLL into a freshly started game.")
        raise PipeError(f"no {PIPE_ROOT}{PIPE_PREFIX}<pid>: no game is running UE5Dumper (or its pipe is not up yet)")
    if len(pids) > 1 or legacy:
        out = warn if warn is not None else sys.stderr
        print(f"# WARNING: {len(pids)} game(s) serve a UE5Dumper pipe"
              + (", and an old DLL serves the legacy one" if legacy else "") + ":", file=out)
        for p in pids:
            print(f"#   {pipe_name(p)}  {process_image(p)}", file=out)
        print(f"# using the first, PID {pids[0]}. Pass --pid, or set {PID_ENV}, to choose.", file=out)
    return pipe_path(pids[0])


class StaleBuildError(PipeError):
    """The DLL answering the pipe is not the build we just produced (§2.6 trap 1)."""


class TruncatedError(PipeError):
    """The reply hit a cap, so it is a sample and cannot support an absence claim (§2.6 trap 3)."""


class PipeClient:
    def __init__(self, pipe: str | None = None, timeout: float = 120.0, pid: int | None = None):
        # `pipe`: a full path, used as given. Otherwise the game is chosen at connect time (resolve_pipe): `pid`, then
        # $UE5DUMP_PID, then the listing -- so a game whose DLL is still scanning is found once its pipe appears.
        self.pipe = pipe
        self.pid = pid
        self.timeout = timeout
        self._f = None
        self._id = 0
        self.events: list[dict] = []
        self._buf = bytearray()   # leftover bytes between block reads
        self.last_reply_bytes = 0

    # -- lifecycle ------------------------------------------------------------
    def connect(self, retries: int = 10, delay: float = 1.0) -> "PipeClient":
        # [RIG-PIPE-DOUBLE-CONNECT] Already open: a second handle would take a pipe instance the UI may need. A handle
        # closed behind the client's back is reopened instead of handed back.
        if self._f is not None and not self._f.closed:
            return self
        # A new connection starts with no bytes of the last one: a partial line left there would be glued to this
        # connection's first reply, which then fails to parse and is dropped.
        self._buf.clear()
        last = None
        path = self.pipe
        for _ in range(retries):
            try:
                if path is None:
                    path = resolve_pipe(self.pid)   # warns once, on the attempt that finds the games
                self._f = open(path, "r+b", buffering=0)
                self.pipe = path   # which game this client reached; a reconnect dials the same one
                return self
            except PipeError as e:  # no game's pipe listed yet
                last = e
                time.sleep(delay)
            except OSError as e:  # pipe not up yet, or busy
                last = e
                time.sleep(delay)
        # [PIPE-PER-PROCESS-2026-11-06] Stage B: say when every connection was taken, which open() cannot.
        hint = ""
        if path is not None and _pipe_is_busy(path):
            hint = (" -- every connection it takes is in use. A UE5Dumper DLL older than this tree may take only 3 "
                    "clients at once, and the UI holds 2: disconnect the UI or another rig, or refresh the game's DLL")
        raise PipeError(f"could not open {path or 'a UE5Dumper pipe'}: {last}{hint}")

    def close(self) -> None:
        if self._f:
            try:
                self._f.close()
            finally:
                self._f = None

    def __enter__(self):
        return self.connect()

    def __exit__(self, *exc):
        self.close()

    # -- core request/response ------------------------------------------------
    def request(self, cmd: str, **params):
        """Send one command and return its reply.

        Async events interleave with replies on this pipe, so a blind readline hands back the
        wrong message. Match on `id`, and keep anything else in `self.events`.
        """
        if not self._f:
            raise PipeError("not connected")
        self._id += 1
        rid = self._id
        msg = {"id": rid, "cmd": cmd}
        msg.update(params)
        self._f.write((json.dumps(msg) + "\n").encode("utf-8"))

        deadline = time.time() + self.timeout
        # Read in BLOCKS, not bytes. The byte-at-a-time version was quadratic on a
        # large reply (a per-byte pipe read, `buf += chunk`, and a clock call each
        # time). The DLL finishes list_all_functions on a 281K-object title in 0.34 s
        # and then BLOCKS writing the multi-MB reply into a pipe this drains one byte
        # at a time -- which is the whole of the "~10 minute" stall, and it read as a
        # DLL hang. Leftovers live in self._buf so they survive across requests.
        while time.time() < deadline:
            chunk = self._f.read(65536)
            if not chunk:
                raise PipeError(f"pipe closed while waiting for id={rid} ({cmd})")
            self._buf.extend(chunk)
            while True:
                nl = self._buf.find(b"\n")
                if nl < 0:
                    break
                line = bytes(self._buf[:nl])
                del self._buf[:nl + 1]
                if not line.strip():
                    continue
                try:
                    obj = json.loads(line.decode("utf-8", "replace"))
                except json.JSONDecodeError:
                    continue
                if obj.get("id") == rid:
                    self.last_reply_bytes = len(line)   # the reply's size on the wire, without its newline
                    return obj
                self.events.append(obj)
        raise PipeError(f"timed out after {self.timeout}s waiting for id={rid} ({cmd})")

    # -- the three §2.6 guards ------------------------------------------------
    def assert_build(self, expected: str | None = None) -> str:
        """Trap 1. Refuse to run against a build other than the one on disk."""
        if expected is None:
            if not DIST_BUILD.exists():
                raise PipeError(f"no {DIST_BUILD}; pass expected= explicitly")
            expected = DIST_BUILD.read_text(encoding="utf-8").strip()
        got = str(self.request("get_pointers").get("build_number", "")).strip()
        if not got:
            raise PipeError("get_pointers returned no build_number")
        if not got.endswith(expected):
            raise StaleBuildError(
                f"the DLL answering the pipe reports build {got!r}, but dist is {expected!r}. "
                "A deployed proxy is probably still serving the pipe -- replace it in the game's "
                "Binaries\\Win64 and restart the game (working-lessons 2.6)."
            )
        return got

    def ensure_scanned(self, timeout: float = 180.0, poll: float = 2.0) -> dict:
        """Trap 2. Proxy mode starts the pipe only; `init` returns ok and scans nothing."""
        p = self.request("get_pointers")
        if p.get("gobjects_method") not in (None, "", "not_found"):
            return p
        self.request("trigger_scan")
        deadline = time.time() + timeout
        while time.time() < deadline:
            time.sleep(poll)
            p = self.request("get_pointers")
            if p.get("gobjects_method") not in (None, "", "not_found"):
                return p
        raise PipeError(
            f"pointers still unresolved {timeout}s after trigger_scan; last reply: {p}"
        )

    @staticmethod
    def check_complete(reply: dict, what: str = "result") -> dict:
        """Trap 3. An absence claim is only valid over a reply that actually finished."""
        for flag in ("deadline_hit", "truncated"):
            if reply.get(flag):
                raise TruncatedError(
                    f"{what} has {flag}=true, so it is a SAMPLE, not the population. "
                    "Re-run with a higher cap and confirm the flag is false before asserting "
                    "that something is absent (working-lessons 1.6 / 2.6)."
                )
        return reply


def selftest() -> int:
    """[RIG-PIPE-DOUBLE-CONNECT] `with PipeClient() as c:` connects, and a second `c.connect()` must not open again:
    it opened a second handle while the first was still held, which needs a second pipe instance at the same moment.
    The DLL served three then (six since [PIPE-PER-PROCESS-2026-11-06] Stage B) and the UI holds two, so with the UI up the rig failed after ten
    retries. A handle closed
    behind the client's back is reopened, not handed back, and the reopened connection starts with no bytes of the old
    one -- while a connect() on an open client keeps them, since they belong to the live connection. No pipe is
    opened -- `open` is replaced for the duration."""
    opened = []

    class FakeHandle:
        closed = False

        def close(self):
            self.closed = True

    def fake_open(*args, **kwargs):
        opened.append(args[0])
        return FakeHandle()

    g = globals()
    had, saved = "open" in g, g.get("open")
    g["open"] = fake_open
    try:
        # [PIPE-PER-PROCESS-2026-11-06] A client given a pid dials that game's pipe, and keeps dialling it.
        by_pid = PipeClient(pid=7)
        by_pid.connect()
        dialled = opened[-1] if opened else None
        by_pid.close()
        opened.clear()

        c = PipeClient(pipe=pipe_path(1))   # an explicit path: no listing of \\.\pipe\ in a selftest
        with c:
            c._buf.extend(b'{"id":3,"partial')   # the live connection's own unread bytes
            c.connect()
            twice = len(opened)
            kept = len(c._buf)
        c.connect()
        again = len(opened)
        c._f.close()   # closed directly, not through close(), so the attribute still holds it
        c._buf.extend(b'{"id":7,"partial')   # what a timed-out read leaves behind
        c.connect()
        reopened = len(opened)
        leftover = len(c._buf)
        c.close()
    finally:
        if had:
            g["open"] = saved
        else:
            del g["open"]
    bad = 0
    # [PIPE-PER-PROCESS-2026-11-06] Stage B: a pipe whose every instance is taken. open() reports it as EINVAL, like
    # other failures; the error must say the connections are all in use, and must not say so when they are not.
    def busy_open(*_a, **_k):
        raise OSError(22, "Invalid argument")

    def failure_text(busy):
        g["open"], g["_pipe_is_busy"] = busy_open, (lambda _p: busy)
        try:
            PipeClient(pipe=pipe_path(5)).connect(retries=1, delay=0.0)
            return "connected?!"
        except PipeError as e:
            return str(e)
        finally:
            g["open"], g["_pipe_is_busy"] = fake_open, pipe_is_busy

    g["open"] = fake_open
    busy_text, other_text = failure_text(True), failure_text(False)
    if had:
        g["open"] = saved
    else:
        del g["open"]

    for label, got, want in (("a busy pipe: the error says every connection is in use", "in use" in busy_text, True),
                             ("any other failure: it does not", "in use" in other_text, False),
                             ("a client given a pid dials its game", dialled, pipe_path(7)),
                             ("connect inside `with`", twice, 1), ("bytes kept by a connect on an open client", kept, 16),
                             ("connect after close", again, 2),
                             ("connect after the handle was closed directly", reopened, 3),
                             ("bytes a reopened connection inherits", leftover, 0)):
        ok = got == want
        bad += not ok
        print(f"{'OK ' if ok else 'BAD'}  {label}: {got}, want {want}")
    print("RESULT:", "PASS" if bad == 0 else f"FAIL ({bad})")
    return 0 if bad == 0 else 1


def selftest_resolve() -> int:
    """[PIPE-PER-PROCESS-2026-11-06] Which game a client dials, from a fake \\\\.\\pipe\\ listing and environment: no
    pipe is opened and no game needs to run."""
    import io
    g = globals()
    saved = g["_listdir"], g["_environ"], g["process_image"]
    bad = 0

    def case(label, listing, env, pid, want, want_warn=None):
        nonlocal bad
        g["_listdir"] = lambda _root: list(listing)
        g["_environ"] = dict(env)
        g["process_image"] = lambda p: f"game{p}.exe"
        warn = io.StringIO()
        try:
            got = resolve_pipe(pid, warn=warn)
        except PipeError as e:
            got = "PipeError: " + str(e)
        ok = (got.startswith(want) if want.startswith("PipeError") else got == want)
        if want_warn is not None:
            ok = ok and ((want_warn in warn.getvalue()) if want_warn else warn.getvalue() == "")
        bad += not ok
        print(f"{'OK ' if ok else 'BAD'}  {label}: {got!r}" + (f" warn={warn.getvalue()!r}" if not ok else ""))

    root = "\\\\.\\pipe\\"
    try:
        case("one game: its pipe, no warning", ["UE5DumpBfx_4242", "AOBMakerCEBridge"], {}, None,
             root + "UE5DumpBfx_4242", "")
        case("several games: the lowest pid, and every game named",
             ["UE5DumpBfx_300", "UE5DumpBfx_100"], {}, None, root + "UE5DumpBfx_100", "UE5DumpBfx_300  game300.exe")
        case("a game beside the legacy pipe: the game, with a warning",
             ["UE5DumpBfx", "UE5DumpBfx_7"], {}, None, root + "UE5DumpBfx_7", "old DLL")
        case("the legacy pipe alone: refused, and why", ["UE5DumpBfx"], {}, None, "PipeError: only the legacy pipe")
        case("no game", ["AOBMakerCEBridge", "UE5DumpBfx-probe-1-ab"], {}, None, "PipeError: no ")
        case("a pid given: its pipe, listed or not", [], {}, 55, root + "UE5DumpBfx_55", "")
        case("the environment names the game", ["UE5DumpBfx_1", "UE5DumpBfx_2"], {PID_ENV: "2"}, None,
             root + "UE5DumpBfx_2", "")
        case("a pid given beats the environment", [], {PID_ENV: "2"}, 9, root + "UE5DumpBfx_9", "")
        case("a bad environment value is refused", [], {PID_ENV: "abc"}, None, "PipeError: UE5DUMP_PID=")
        case("names are compared without case", ["ue5dumpbfx_12"], {}, None, root + "UE5DumpBfx_12", "")
    finally:
        g["_listdir"], g["_environ"], g["process_image"] = saved

    for name, want in (("UE5DumpBfx_1234", 1234), ("UE5DumpBfx", None), ("UE5DumpBfx_", None), ("UE5DumpBfx_0", None),
                       ("UE5DumpBfx_12a", None), ("UE5DumpBfx_-5", None), ("UE5DumpBfx-probe-1-ab", None),
                       ("UE5DumpBfx_99999999999", None), ("xUE5DumpBfx_12", None)):
        got = parse_pid(name)
        ok = got == want
        bad += not ok
        print(f"{'OK ' if ok else 'BAD'}  parse_pid({name!r}) = {got}, want {want}")
    return bad


def main() -> int:
    if sys.argv[1:] == ["--selftest"]:
        resolve_bad = selftest_resolve()
        rc = selftest()
        if resolve_bad:
            print(f"RESULT: FAIL ({resolve_bad} resolution case(s))")
        return 1 if (rc or resolve_bad) else 0
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("cmd")
    ap.add_argument("--args", default="{}", help="JSON object of extra request fields")
    ap.add_argument("--no-guards", action="store_true",
                    help="skip the build/scan guards (they are the point -- only for probing)")
    ap.add_argument("--timeout", type=float, default=120.0)
    ap.add_argument("--pid", type=int, default=None,
                    help="the game to drive (its pipe is UE5DumpBfx_<pid>); default $UE5DUMP_PID, else the only game")
    a = ap.parse_args()

    with PipeClient(timeout=a.timeout, pid=a.pid) as c:
        if not a.no_guards:
            print(f"# build: {c.assert_build()}", file=sys.stderr)
        r = c.request(a.cmd, **json.loads(a.args))
        print(json.dumps(r, ensure_ascii=False, indent=1))
        if c.events:
            print(f"# {len(c.events)} async event(s) seen", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
