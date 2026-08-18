"""Drive the injected UE5Dumper DLL over its named pipe, from Python, with no UI.

This is the rig `docs/working-lessons.md` §2.6 describes: the UI is a *client*, not the subject,
so every DLL-side row on the verification register can be driven straight over
`\\\\.\\pipe\\UE5DumpBfx`. It is also STRONGER evidence for a DLL-side claim than a UI session,
because it removes Avalonia as a variable -- and correspondingly proves nothing about the panel's
own bindings, which must be said explicitly when a result is recorded.

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
    py tools/verify/pipe_client.py find_instances --args '{"class_name":"DumperTestActor"}'
"""

from __future__ import annotations

import argparse
import json
import pathlib
import sys
import time

PIPE = r"\\.\pipe\UE5DumpBfx"
DIST_BUILD = pathlib.Path(__file__).resolve().parents[2] / "dist" / "build_number.txt"


class PipeError(RuntimeError):
    pass


class StaleBuildError(PipeError):
    """The DLL answering the pipe is not the build we just produced (§2.6 trap 1)."""


class TruncatedError(PipeError):
    """The reply hit a cap, so it is a sample and cannot support an absence claim (§2.6 trap 3)."""


class PipeClient:
    def __init__(self, pipe: str = PIPE, timeout: float = 120.0):
        self.pipe = pipe
        self.timeout = timeout
        self._f = None
        self._id = 0
        self.events: list[dict] = []
        self._buf = bytearray()   # leftover bytes between block reads

    # -- lifecycle ------------------------------------------------------------
    def connect(self, retries: int = 10, delay: float = 1.0) -> "PipeClient":
        last = None
        for _ in range(retries):
            try:
                self._f = open(self.pipe, "r+b", buffering=0)
                return self
            except OSError as e:  # pipe not up yet, or busy
                last = e
                time.sleep(delay)
        raise PipeError(f"could not open {self.pipe}: {last}")

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


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("cmd")
    ap.add_argument("--args", default="{}", help="JSON object of extra request fields")
    ap.add_argument("--no-guards", action="store_true",
                    help="skip the build/scan guards (they are the point -- only for probing)")
    ap.add_argument("--timeout", type=float, default=120.0)
    a = ap.parse_args()

    with PipeClient(timeout=a.timeout) as c:
        if not a.no_guards:
            print(f"# build: {c.assert_build()}", file=sys.stderr)
        r = c.request(a.cmd, **json.loads(a.args))
        print(json.dumps(r, ensure_ascii=False, indent=1))
        if c.events:
            print(f"# {len(c.events)} async event(s) seen", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
