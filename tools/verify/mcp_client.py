"""Drive `UE5DumpUI.exe --mcp` the way an MCP client does: start it with stdin / stdout as pipes and speak JSON-RPC.

[PIPE-PER-PROCESS-2026-11-06] Stage D1. The server is a mode of the UI's exe, a GUI-subsystem program, so the first
thing this measures is that its standard handles carry the protocol at all. Then the protocol itself, and, with a
game running the DLL, every tool.

What it checks on every line the server writes: it is one JSON-RPC 2.0 message, UTF-8, ended by LF alone. Any other
byte on stdout (a stray log line, a CR) fails the run, because a real client drops the server for it.

    py tools/verify/mcp_client.py probe --exe dist/UE5DumpUI.exe
        handshake, tools/list, read_games, server/discover answered with an error, ping, EOF exit code. No game needed.
    py tools/verify/mcp_client.py call --exe dist/UE5DumpUI.exe --tool read_status [--args '{"pid": 1234}']
        one tool call, its result printed.
    py tools/verify/mcp_client.py live --exe dist/UE5DumpUI.exe [--pid N]
        every tool against a game running the DLL (a fixture: DumperTest), and a cancel of a running call.
    py tools/verify/mcp_client.py writes --exe dist/UE5DumpUI.exe [--pid N] [--toggle-experimental]
        Stage D2-2: the write tools against a fixture, beside a foreground pipe client standing in for the UI. The
        UI's holds are refused and named; free ones become the server's; a teleport leaves the marker slots and the
        undo point; See-through OFF is refused while the UI also claims it; everything the server held is released
        when it exits, and the UI's hold is not. --toggle-experimental also turns the UI's experimental switch off for
        one step (the file's exact bytes are put back) to see the experimental tools refuse. It MOVES the fixture's
        pawn by a few metres and back: run it on a fixture, never on a real game.
    py tools/verify/mcp_client.py raw --exe dist/UE5DumpUI.exe --suspend-name DumperTest-Win64-Shipping [--pid N]
        Stage D2-3: write_memory on the fixture's own int field (written, a changed-since refusal, put back), refused on
        a vtable (read-only) and on code; write_invoke of Add_IntInt (40 + 2), the deny list (ToggleDebugCamera), the
        UI's queued invoke refusing the AI's, and the AI's own queued invoke never resent and dropped when its session
        ends. It FREEZES the game thread for a few seconds at a time (suspend.py suspend-tid; always resumed): a
        fixture only.
    py tools/verify/mcp_client.py dbgcam --exe dist/UE5DumpUI.exe --suspend-name DumperTest [--pid N]
        Stage D2-3, register item 26: a pending debug-camera toggle and who holds it. Needs a live CheatManager --
        DumperTest Development (launch_dumpertest.py dev), not Shipping. With the game thread frozen: (A) the AI's ON
        queued, the UI stand-in joins it (-5), the AI session ends: the toggle is not dropped, the camera comes on, the
        UI's; (B) the UI's ON queued: the AI's write_debug_camera is refused naming the UI; (C) the UI's ON sent while
        the AI's toggle is still being dispatched waits for the hold lock, then joins as in (A). Turns the camera off
        again after each.
    py tools/verify/mcp_client.py dbgcamhold --exe dist/UE5DumpUI.exe --suspend-name DumperTest [--pid N]
        Register item 24, the debug camera: the AI holding it. Needs a live CheatManager, as dbgcam. (D) The AI's ON
        turns it on, read_holds names the server, and its session's end turns it off; (E) the AI's OFF of its own;
        (F) the UI's Force ON takes it over: the AI's OFF and ON are refused naming the UI, and its end leaves it on;
        (G) the UI's own ON, likewise. [DBGCAM-RELEASE-STALLED] (H) the AI session ends while the game thread is
        stalled: the release's OFF stays queued, the release is owed (get_holds' release_owed), and once the game runs
        the DLL sends the OFF again and the camera goes off; (I) the same, but the UI's Force ON during the stall takes
        the camera: the owed release is abandoned and the camera stays the UI's; (J) the AI leaves mid-command (a
        write_invoke waiting on the stalled game thread): its release still reads the camera, is owed, and ends it.
    py tools/verify/mcp_client.py dbgcamstop --exe dist/UE5DumpUI.exe --suspend-name DumperTest [--pid N] [--ce-desc D]
        [DBGCAM-RELEASE-STALLED] a CE Disable/Enable around the AI's camera release, on DumperTest Development with the
        DLL started by the CE record D (default the CT's "Inject DLL + Start Pipe Server"), CE unticking and
        re-ticking it through ce_lua_eval.py. (K) a release owed is dropped at the stop, and nothing turns the camera
        off after the re-enable; (K2) a stop while the release still waits ends it -1, never owed.

    --ssh HOST   start the server as `ssh -T HOST <exe> --mcp`, the way an MCP client on another machine would; --exe
                 is then the path on HOST. ssh's own prompts (a password) go to this console, never to the protocol:
                 its stderr is left on the console and only stdin / stdout are piped.
    --report F   also write everything printed to F: a run started elevated (Start-Process -Verb RunAs) prints into a
                 window that closes when it ends.

The exe path is an argument, never assumed: a gate or CI has no build and no game (feedback: gates stay inside the
repo). Exit code 0 only when every check passed; the failures are listed.
"""

from __future__ import annotations

import argparse
import contextlib
import json
import math
import os
import pathlib
import queue
import re
import subprocess
import sys
import threading
import time

PROTOCOL = "2025-11-25"

# --ssh HOST / --ssh-key KEY: every McpClient this run makes starts the server through ssh.
SSH_HOST: str | None = None
SSH_KEY: str | None = None
# Windows' own client reads a key's NTFS ACL as sshd expects; Git's ssh, often first on PATH, judges it by POSIX bits.
WINDOWS_SSH = pathlib.Path(r"C:\Windows\System32\OpenSSH\ssh.exe")


class McpError(Exception):
    pass


class _Tee:
    """stdout copied to a report file, flushed per write so a window that closes leaves the file whole."""

    def __init__(self, console, file):
        self._console, self._file = console, file

    def write(self, text):
        self._console.write(text)
        self._file.write(text)
        self._file.flush()

    def flush(self):
        self._console.flush()
        self._file.flush()


def _elevated() -> bool:
    """Whether this process runs elevated: the report says which side of the UAC boundary it measured."""
    try:
        import ctypes
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except (AttributeError, OSError):
        return False


class McpClient:
    """One server process. Requests are answered in any order; a reader thread files each line by id."""

    def __init__(self, exe: str, extra_args: list[str] | None = None, ssh: str | None = None):
        ssh = ssh if ssh is not None else SSH_HOST
        command = [exe, "--mcp", *(extra_args or [])]
        if ssh:
            # -T: no terminal, which would echo and translate newlines inside the protocol.
            client = str(WINDOWS_SSH) if WINDOWS_SSH.exists() else "ssh"
            auth = ["-i", SSH_KEY, "-o", "BatchMode=yes"] if SSH_KEY else []
            command = [client, "-T", *auth, ssh, *command]
        self.proc = subprocess.Popen(
            command, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=None if ssh else subprocess.PIPE, bufsize=0)
        self._next = 0
        self._lines: queue.Queue[bytes | None] = queue.Queue()
        self.framing_errors: list[str] = []
        self.stderr_lines: list[str] = []
        self._pending: dict[object, dict] = {}
        self._reader = threading.Thread(target=self._read_stdout, daemon=True)
        self._reader.start()
        if self.proc.stderr is not None:
            threading.Thread(target=self._read_stderr, daemon=True).start()

    def _read_stdout(self):
        buf = b""
        while True:
            # bufsize=0 makes stdout a raw file: one read returns what the pipe holds, up to the size asked.
            chunk = self.proc.stdout.read(65536)
            if not chunk:
                break
            buf += chunk
            while b"\n" in buf:
                line, buf = buf.split(b"\n", 1)
                self._lines.put(line)
        if buf:
            self.framing_errors.append(f"bytes after the last LF: {buf[:80]!r}")
        self._lines.put(None)

    def _read_stderr(self):
        for raw in self.proc.stderr:
            self.stderr_lines.append(raw.decode("utf-8", "replace").rstrip())

    def _parse(self, line: bytes) -> dict | None:
        if line.endswith(b"\r"):
            self.framing_errors.append(f"a CR before the LF: {line[:80]!r}")
            line = line[:-1]
        try:
            msg = json.loads(line.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as e:
            self.framing_errors.append(f"not JSON ({e}): {line[:120]!r}")
            return None
        if not isinstance(msg, dict) or msg.get("jsonrpc") != "2.0":
            self.framing_errors.append(f"not a JSON-RPC 2.0 message: {line[:120]!r}")
            return None
        return msg

    def send(self, msg: dict):
        self.proc.stdin.write((json.dumps(msg, ensure_ascii=False) + "\n").encode("utf-8"))
        self.proc.stdin.flush()

    def request_async(self, method: str, params: dict | None = None):
        self._next += 1
        rid = self._next
        msg = {"jsonrpc": "2.0", "id": rid, "method": method}
        if params is not None:
            msg["params"] = params
        self.send(msg)
        return rid

    def wait(self, rid, timeout: float = 60.0) -> dict:
        deadline = time.monotonic() + timeout
        while rid not in self._pending:
            left = deadline - time.monotonic()
            if left <= 0:
                raise McpError(f"no response to request {rid} within {timeout:.0f} s")
            try:
                line = self._lines.get(timeout=left)
            except queue.Empty:
                continue
            if line is None:
                raise McpError(f"the server closed stdout before answering request {rid}")
            msg = self._parse(line)
            if msg is not None and "id" in msg:
                self._pending[msg["id"]] = msg
        return self._pending.pop(rid)

    def request(self, method: str, params: dict | None = None, timeout: float = 60.0) -> dict:
        return self.wait(self.request_async(method, params), timeout)

    def notify(self, method: str, params: dict | None = None):
        msg = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            msg["params"] = params
        self.send(msg)

    def initialize(self) -> dict:
        r = self.request("initialize", {"protocolVersion": PROTOCOL, "capabilities": {},
                                        "clientInfo": {"name": "mcp_client.py", "version": "1"}})
        self.notify("notifications/initialized")
        return r

    def call(self, tool: str, args: dict | None = None, timeout: float = 180.0) -> dict:
        r = self.request("tools/call", {"name": tool, "arguments": args or {}}, timeout)
        if "error" in r:
            raise McpError(f"{tool}: JSON-RPC error {r['error']}")
        return r["result"]

    def close(self, timeout: float = 5.0) -> int | None:
        """Closes stdin, as a client does, and returns the exit code (None if it did not exit in time)."""
        try:
            self.proc.stdin.close()
        except OSError:
            pass
        try:
            return self.proc.wait(timeout)
        except subprocess.TimeoutExpired:
            self.proc.kill()
            return None

    def drain_unsolicited(self, seconds: float = 0.3) -> list[dict]:
        """Lines that arrived with no request waiting for them (a server must not answer a notification)."""
        end = time.monotonic() + seconds
        got = []
        while time.monotonic() < end:
            try:
                line = self._lines.get(timeout=max(0.01, end - time.monotonic()))
            except queue.Empty:
                break
            if line is None:
                break
            msg = self._parse(line)
            if msg is not None:
                got.append(msg)
        return got


def payload(result: dict) -> tuple[object, str | None]:
    """A tools/call result's JSON payload (first text block) and its note (second block), if any."""
    content = result.get("content") or []
    data = json.loads(content[0]["text"]) if content else None
    note = content[1]["text"] if len(content) > 1 else None
    return data, note


class Checks:
    def __init__(self):
        self.failures: list[str] = []
        self.passed = 0

    def check(self, ok: bool, what: str, detail: object = ""):
        if ok:
            self.passed += 1
            print(f"  ok    {what}")
        else:
            self.failures.append(f"{what}: {detail}")
            print(f"  FAIL  {what}: {detail}")
        return ok

    def done(self, client: "McpClient | list[McpClient] | None" = None) -> int:
        # A rig that starts several sessions passes them all: each one's stdout is checked, not only the last.
        for c in (client if isinstance(client, list) else [client] if client is not None else []):
            for e in c.framing_errors:
                self.failures.append("framing: " + e)
                print("  FAIL  framing:", e)
        print(f"\n{self.passed} passed, {len(self.failures)} failed")
        return 0 if not self.failures else 1


def run_probe(exe: str) -> int:
    ck = Checks()
    c = McpClient(exe)
    t0 = time.monotonic()
    init = c.initialize()
    res = init.get("result", {})
    ck.check(res.get("protocolVersion") == PROTOCOL, "initialize echoes 2025-11-25", init)
    ck.check(res.get("serverInfo", {}).get("name") == "ue5cedumper", "serverInfo.name", res.get("serverInfo"))
    ck.check("tools" in res.get("capabilities", {}), "capabilities.tools", res.get("capabilities"))
    ck.check(bool(res.get("instructions")), "instructions present")
    print(f"        (first answer {1000 * (time.monotonic() - t0):.0f} ms after start)")

    old = c.request("initialize", {"protocolVersion": "1999-01-01", "capabilities": {},
                                   "clientInfo": {"name": "x", "version": "1"}})
    ck.check(old.get("result", {}).get("protocolVersion") == PROTOCOL, "unknown revision -> newest", old)

    tools = c.request("tools/list").get("result", {}).get("tools", [])
    names = [t["name"] for t in tools]
    ck.check(len(tools) >= 20, f"tools/list returned {len(tools)} tools")
    bad_ro = [t["name"] for t in tools if t["annotations"]["readOnlyHint"] != t["name"].startswith("read_")]
    ck.check(not bad_ro, "readOnlyHint matches the read_ prefix", bad_ro)
    ck.check("read_games" in names and "scan_start" in names, "read_games and scan_start listed")

    disc = c.request("server/discover", {})
    ck.check(disc.get("error", {}).get("code") == -32601, "server/discover -> method not found", disc)
    ck.check(c.request("ping").get("result") == {}, "ping")
    unk = c.request("no/such/method")
    ck.check(unk.get("error", {}).get("code") == -32601, "unknown method -> -32601", unk)

    c.notify("notifications/whatever", {})
    ck.check(not c.drain_unsolicited(), "a notification gets no response")

    games = c.call("read_games")
    data, note = payload(games)
    ck.check(isinstance(data, dict) and "games" in data, "read_games payload", games)
    print(f"        games: {data.get('games') if isinstance(data, dict) else data}  note: {note}")

    bad = c.call("read_object", {"addr": "zz"})
    ck.check(bad.get("isError") is True, "a malformed address is a tool error the model sees", bad)

    code = c.close()
    ck.check(code == 0, "exit code 0 after stdin closes", code)
    if c.stderr_lines:
        print("        stderr:", c.stderr_lines[:5])
    return ck.done(c)


def run_call(exe: str, tool: str, args: dict) -> int:
    c = McpClient(exe)
    c.initialize()
    r = c.call(tool, args)
    data, note = payload(r)
    print(json.dumps(data, indent=2, ensure_ascii=False)[:20000])
    if note:
        print(note)
    c.close()
    for e in c.framing_errors:
        print("framing:", e)
    return 1 if r.get("isError") or c.framing_errors else 0


def run_live(exe: str, pid: int | None, suspend_name: str | None) -> int:
    ck = Checks()
    c = McpClient(exe)
    c.initialize()
    base = {"pid": pid} if pid else {}

    def call(tool, args=None, timeout=180.0):
        r = c.call(tool, {**base, **(args or {})}, timeout)
        data, note = payload(r)
        return r, data, note

    r, st, note = call("read_status")
    ck.check(not r.get("isError"), "read_status", st)

    # Another client, standing in for a UE5DumpUI window: a value-scan session of its own, begun before the server's
    # work and read back after it (the maintainer, 2026-11-11: an AI beside the UI must not disturb what the UI does).
    other = other_sid = other_total = None
    if st.get("scanned"):
        sys.path.insert(0, str(pathlib.Path(__file__).parent))
        from pipe_client import PipeClient   # noqa: E402 -- the rig beside this one
        try:
            other = PipeClient(pid=pid or st.get("pid"), timeout=120)
            other.connect()
        except Exception as e:  # noqa: BLE001 -- e.g. an elevated game seen from a non-elevated rig
            other = None
            print(f"  skip  another client beside the server: this rig cannot connect ({e})")
        if other is not None:
            began = other.request("begin_value_scan", data_type="Int32", scan_type="Exact", value="1", page_size=1)
            other_sid, other_total = began.get("session_id"), began.get("total")
            ck.check(bool(other_sid), f"another client's value scan ({other_total} hits)", began)
    if not st.get("scanned"):
        r, data, note = call("scan_start")
        ck.check(not r.get("isError"), "scan_start", data)
        for _ in range(90):
            time.sleep(2)
            r, st, note = call("read_status")
            if st.get("scanned"):
                break
    ck.check(bool(st.get("scanned")), "the game is scanned", st)
    print(f"        UE {st.get('ue_version')}, {st.get('object_count')} objects, DLL build {st.get('build_number')}, "
          f"server build {st.get('mcp_build')}")

    r, again, _ = call("scan_start")
    ck.check(again.get("already_scanned") is True, "scan_start on a scanned game does nothing", again)

    r, s, _ = call("read_search_objects", {"query": "PlayerController", "limit": 5})
    ck.check(not r.get("isError") and s.get("objects"), "read_search_objects", s)

    r, live, _ = call("read_find_live_instance", {"class_name": "PlayerController"})
    ck.check(live.get("pick") == "live" and live.get("addr"), "read_find_live_instance PlayerController", live)
    pc = live.get("addr")
    cls = live.get("class_addr")

    r, obj, _ = call("read_object", {"addr": pc})
    ck.check(obj.get("addr") == pc, "read_object", obj)

    r, inst, note = call("read_instance", {"addr": pc})
    ck.check(not r.get("isError") and inst.get("fields"), f"read_instance ({len(inst.get('fields', []))} fields)",
             inst if r.get("isError") else "")
    r, inst2, _ = call("read_instance", {"addr": pc, "name_filter": "pawn"})
    names = [f.get("name", "") for f in inst2.get("fields", [])]
    ck.check(names and all("pawn" in n.lower() for n in names), f"read_instance name_filter keeps {names[:6]}")

    r, klass, note = call("read_class", {"addr": cls})
    ck.check(not r.get("isError") and klass.get("class", {}).get("fields"), "read_class", note)
    r, funcs, _ = call("read_functions", {"addr": cls})
    ck.check(not r.get("isError") and "functions" in funcs, f"read_functions ({funcs.get('count')})")

    r, mem, _ = call("read_memory", {"addr": pc, "size": 64})
    ck.check(isinstance(mem.get("bytes"), str) and len(mem["bytes"]) == 128, "read_memory 64 bytes", mem)

    r, refs, _ = call("read_find_refs", {"addr": pc, "max_results": 8})
    ck.check(not r.get("isError") and "references" in refs, f"read_find_refs ({len(refs.get('references', []))})")

    r, path, _ = call("read_path_from_root", {"target": pc})
    ck.check(not r.get("isError") and "status" in path, f"read_path_from_root status {path.get('status')}")

    r, rel, _ = call("read_related", {"addr": pc, "max_results": 16})
    ck.check(not r.get("isError") and "related" in rel, "read_related")

    r, world, note = call("read_world", {"limit": 10})
    ck.check(not r.get("isError") and "actors" in world, f"read_world ({world.get('actor_total')} actors)", note)

    r, cp, _ = call("read_ce_pointer", {"addr": pc})
    ck.check(not r.get("isError"), "read_ce_pointer", cp)

    r, classes, _ = call("read_list_classes", {"name_filter": "controller", "limit": 5})
    ck.check(not r.get("isError") and classes.get("matched", 0) >= 1, f"read_list_classes filter "
             f"(matched {classes.get('matched')})", classes if r.get("isError") else "")

    r, enums, _ = call("read_list_enums", {"name_filter": "collision", "limit": 3})
    ck.check(not r.get("isError") and "enums" in enums, f"read_list_enums filter (matched {enums.get('matched')})")

    r, props, _ = call("read_search_properties", {"query": "Health", "limit": 10})
    ck.check(not r.get("isError") and "results" in props, f"read_search_properties ({len(props.get('results', []))})")

    r, clients, _ = call("read_pipe_clients")
    ck.check(not r.get("isError"), "read_pipe_clients", clients)

    r, offs, _ = call("read_offsets")
    ck.check(not r.get("isError"), "read_offsets")

    r, vs, note = call("read_value_scan_begin", {"data_type": "Int32", "value": "1", "page_size": 5})
    ck.check(not r.get("isError") and vs.get("session_id"), f"read_value_scan_begin ({vs.get('total')} hits)", vs
             if r.get("isError") else "")
    sid = vs.get("session_id")
    if sid:
        r, page, _ = call("read_value_scan_results", {"session_id": sid, "offset": 0, "limit": 3, "sort_key": "class"})
        ck.check(not r.get("isError") and len(page.get("candidates", [])) <= 3, "read_value_scan_results", page
                 if r.get("isError") else "")
        r, ref, _ = call("read_value_scan_refine", {"session_id": sid, "scan_type": "Unchanged", "page_size": 3})
        ck.check(not r.get("isError"), f"read_value_scan_refine ({ref.get('total')} left)", ref)
        r, end, _ = call("read_value_scan_end", {"session_id": sid})
        ck.check(not r.get("isError"), "read_value_scan_end", end)

    # A cancel of a running call. On a fixture every command answers in milliseconds, so a call is made to outlast its
    # cancel by suspending the game, which stops the DLL's pipe threads too: the call cannot be answered before the
    # cancel arrives. The server must not answer it, and after a resume the next call must work (it reconnects, and
    # says the sessions went with the old connection).
    if suspend_name:
        suspend = [sys.executable, str(pathlib.Path(__file__).with_name("suspend.py"))]
        subprocess.run([*suspend, "suspend", suspend_name], check=True, capture_output=True)
        try:
            rid = c.request_async("tools/call", {"name": "read_object", "arguments": {**base, "addr": pc}})
            time.sleep(0.5)
            c.notify("notifications/cancelled", {"requestId": rid, "reason": "mcp_client.py cancel test"})
            try:
                c.wait(rid, timeout=3)
                ck.check(False, "a cancelled call gets no response", "it was answered")
            except McpError:
                ck.check(True, "a cancelled call gets no response")
        finally:
            subprocess.run([*suspend, "resume", suspend_name], check=True, capture_output=True)
        r, st2, note = call("read_status")
        ck.check(not r.get("isError") and st2.get("scanned"), "the next call works after a cancel", st2)
        ck.check(note is not None and "closed" in note, "and says the connection was closed", note)
    else:
        print("  skip  the cancel check (--suspend-name not given)")

    if other is not None and other_sid:
        page = other.request("query_candidates", session_id=other_sid, offset=0, limit=1)
        ck.check(page.get("ok") is True and page.get("total") == other_total,
                 "the other client's session survived the server's scans, cancel and reconnect", page)
        names = [h.get("exe") for h in other.request("pipe_clients").get("clients", [])]
        print(f"        pipe clients now: {names}")
        other.request("end_value_scan", session_id=other_sid)
        other.close()

    code = c.close()
    ck.check(code == 0, "exit code 0 after stdin closes", code)
    return ck.done(c)


class _ExperimentalSwitch:
    """The UI's experimental opt-in, which the server reads on every call. Set for a step; restore() puts the file's
    exact bytes back (or removes it, when there was none)."""

    def __init__(self):
        self.path = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "experimental.json"
        self.original = self.path.read_bytes() if self.path.exists() else None

    def _settings(self) -> dict:
        return json.loads(self.original.decode("utf-8-sig")) if self.original else {}

    def enabled(self) -> bool:
        return bool(self._settings().get("enabled"))

    def set(self, enabled: bool):
        settings = self._settings()
        settings["enabled"] = enabled
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.path.write_text(json.dumps(settings), encoding="utf-8")

    def restore(self):
        if self.original is None:
            self.path.unlink(missing_ok=True)
        else:
            self.path.write_bytes(self.original)


FIXTURE_CLASS, FIXTURE_FIELD = "DumperTestHolder", "HolderValue"   # DumperTest's own int field (solide_l3_derivation)
FORCE_FIELD_HOLD = f"force_field:{FIXTURE_CLASS}::{FIXTURE_FIELD}"


def _ahead(pose: dict, distance: float) -> dict:
    """A point `distance` units along the pawn's facing, a little above it: DumperTest does not respawn its pawn, so it
    is displaced the way the UI's TP-facing does (handover, DumperTest), never along a fixed axis."""
    yaw = math.radians(pose.get("yaw", 0.0))
    return {"x": pose["x"] + distance * math.cos(yaw), "y": pose["y"] + distance * math.sin(yaw), "z": pose["z"] + 30}


def _near(a: dict, b: dict, tol: float = 50.0) -> bool:
    """Horizontally within tol units: a jump to a point above the floor settles a little lower."""
    try:
        return abs(a["x"] - b["x"]) <= tol and abs(a["y"] - b["y"]) <= tol
    except (KeyError, TypeError):
        return False


def run_writes(exe: str, pid: int | None, toggle_experimental: bool) -> int:
    sys.path.insert(0, str(pathlib.Path(__file__).parent))
    from pipe_client import PipeClient   # noqa: E402 -- the rig beside this one

    ck = Checks()
    switch = _ExperimentalSwitch()
    experimental = switch.enabled()
    c = McpClient(exe)
    c.initialize()
    base = {"pid": pid} if pid else {}

    def call(tool, args=None, timeout=180.0):
        r = c.call(tool, {**base, **(args or {})}, timeout)
        data, note = payload(r)
        return r, data, note

    def error_text(data) -> str:
        return data.get("error", "") if isinstance(data, dict) else str(data)

    # 1. The catalogue: every write tool asks every time.
    tools = c.request("tools/list").get("result", {}).get("tools", [])
    writes = [t for t in tools if t["name"].startswith("write_")]
    unmarked = [t["name"] for t in writes
                if t.get("_meta", {}).get("anthropic/requiresUserInteraction") is not True
                or t["annotations"].get("destructiveHint") is not True
                or t["annotations"].get("readOnlyHint") is not False]
    ck.check(len(writes) >= 12 and not unmarked,
             f"{len(writes)} write_* tools, each marked requiresUserInteraction and destructive", unmarked)

    r, st, _ = call("read_status")
    if not st.get("scanned"):
        call("scan_start")
        for _ in range(90):
            time.sleep(2)
            r, st, _ = call("read_status")
            if st.get("scanned"):
                break
    ck.check(bool(st.get("scanned")), "the game is scanned", st)
    build = st.get("build_number") or 0
    if not ck.check(build >= 3705, f"the game's DLL is build {build} (the write tools need 3705+)"):
        c.close()
        return ck.done(c)
    game = pid or st.get("pid")

    ui = PipeClient(pid=game, timeout=120)
    ui.connect()
    me = os.getpid()
    print(f"        game pid {game}, DLL build {build}; the foreground client (the UI's stand-in) is pid {me}; "
          f"experimental features {'on' if experimental else 'off'} here")

    def holds() -> dict:
        return {h["hold"]: h for h in ui.request("get_holds").get("holds", [])}

    held_at_start = sorted(k for k, h in holds().items() if h.get("active"))
    if not ck.check(not held_at_start, "nothing is held when the run starts", held_at_start):
        ui.close()
        c.close()
        return ck.done(c)

    exited = None
    ui_claimed_see_through = False
    try:
        # 2. A hold the UI took is never replaced or ended, and the refusal names the UI.
        ui.request("set_god_mode", enable=True)
        r, d, _ = call("write_god_mode", {"enable": False})
        ck.check(r.get("isError") and "god_mode is held by the UE5DumpUI window or another program" in error_text(d)
                 and f"pid {me}" in error_text(d), "write_god_mode OFF over the UI's god mode is refused, naming it", d)
        g = holds().get("god_mode", {})
        ck.check(g.get("active") and g.get("holder", {}).get("you"), "and god mode stays on, the UI's", g)

        # 3. Free holds become the server's.
        r, d, _ = call("write_time_dilation", {"target": "global", "value": 0.5})
        ck.check(not r.get("isError"), "write_time_dilation global 0.5", d)
        r, d, _ = call("write_movement", {"knob": "walk_speed", "multiplier": 1.5})
        ck.check(not r.get("isError"), "write_movement walk_speed x1.5", d)
        r, d, _ = call("read_holds")
        mine = {h["hold"]: h for h in (d or {}).get("holds", [])}
        for hold in ("time:global", "movement:walk_speed"):
            h = mine.get(hold, {})
            ck.check(h.get("active") and h.get("holder", {}).get("you") and h["holder"].get("background"),
                     f"read_holds: {hold} is the server's, marked background", h)
        r, d, _ = call("write_time_dilation", {"target": "global", "value": 0.75})
        ck.check(not r.get("isError"), "the server changes its own hold again (0.75)", d)

        # 4. Teleport. Control first: the UI's own explicit jump (no keep_undo) saves the undo point where it left.
        p0 = ui.request("teleport_get_pose")
        if ck.check(p0.get("code") == 0 and "x" in p0, "the pawn's pose", p0):
            ui.request("teleport_recall_marker", **_ahead(p0, 100))
            before = ui.request("teleport_get_markers").get("markers", [])
            last = next((m for m in before if m.get("slot") == -1), {})
            ck.check(last.get("valid") and _near(last, p0), "control: the UI's own jump saved the undo point it left",
                     last)
            p1 = ui.request("teleport_get_pose")
            target = _ahead(p0, 200)
            r, d, _ = call("write_teleport", target)
            ck.check(not r.get("isError") and _near((d or {}).get("from", {}), p1),
                     "write_teleport moves the pawn and returns the pose it left in 'from'", d)
            ck.check(_near(ui.request("teleport_get_pose"), target), "the pawn is at the coordinates given")
            after = ui.request("teleport_get_markers").get("markers", [])
            ck.check(after == before, "the marker slots and the undo point are unchanged",
                     [m for m in after if m not in before])
            if not r.get("isError"):
                back = d["from"]
                r, d, _ = call("write_teleport", {"x": back["x"], "y": back["y"], "z": back["z"]})
                ck.check(not r.get("isError") and _near(ui.request("teleport_get_pose"), back),
                         "write_teleport back to 'from'", d)
                ck.check(ui.request("teleport_get_markers").get("markers", []) == before,
                         "and the undo point is still the UI's")
            ui.request("teleport_recall_last")
            ck.check(_near(ui.request("teleport_get_pose"), p0), "the UI's Recall last returns where the UI left")

        # 5. The experimental switch binds the server.
        if toggle_experimental or not experimental:
            if toggle_experimental:
                switch.set(False)
            try:
                r, d, _ = call("write_fly", {"enable": True})
                ck.check(r.get("isError") and "experimental" in error_text(d),
                         "write_fly is refused while the UI's experimental features are off", d)
                ck.check(not holds().get("fly", {}).get("active"), "and fly stays off")
            finally:
                if toggle_experimental:
                    switch.restore()
                    switch = _ExperimentalSwitch()
                    switch.set(True)
        if experimental or toggle_experimental:
            r, d, _ = call("write_fly", {"enable": True})
            ck.check(not r.get("isError"), "write_fly on", d)
            r, d, _ = call("write_force_field", {"class_name": FIXTURE_CLASS, "field_name": FIXTURE_FIELD,
                                                 "kind": "numeric", "value": 777})
            ck.check(not r.get("isError") and (d or {}).get("held", 0) > 0,
                     f"write_force_field {FIXTURE_CLASS}.{FIXTURE_FIELD} = 777", d)
            r, d, _ = call("read_holds")
            job = {h["hold"]: h for h in (d or {}).get("holds", [])}.get(FORCE_FIELD_HOLD, {})
            ck.check(job.get("active") and job.get("holder", {}).get("you") and job["holder"].get("background"),
                     f"read_holds: {FORCE_FIELD_HOLD} is the server's, marked background", job)
            # See-through: the server may end its own claim alone, never one the UI shares.
            r, d, _ = call("write_see_through", {"enable": True})
            ck.check(not r.get("isError"), "write_see_through on", d)
            r, d, _ = call("write_see_through", {"enable": False})
            ck.check(not r.get("isError"), "write_see_through off over its own claim alone", d)
            call("write_see_through", {"enable": True})
            ui.request("seethrough_set", enable=True)
            ui_claimed_see_through = True
            r, d, _ = call("write_see_through", {"enable": False})
            ck.check(r.get("isError") and "also on" in error_text(d),
                     "write_see_through off is refused while the UI also has it on", d)
            s = ui.request("seethrough_get_state")
            ck.check(s.get("active") and s.get("claims") == 2, "and See-through stays on with both claims", s)

        # 6. The session ends: what the server held is released, what the UI holds is not.
        exited = c.close()
        ck.check(exited == 0, "the server exits 0 when stdin closes", exited)
        mine_then = ["time:global", "movement:walk_speed"]
        if experimental or toggle_experimental:
            mine_then += ["fly", FORCE_FIELD_HOLD]
        now = {}
        for _ in range(50):
            now = holds()
            if not any(now.get(h, {}).get("active") for h in mine_then):
                break
            time.sleep(0.1)
        for hold in mine_then:
            h = now.get(hold)
            # A force-field job is listed only while it exists: released, it is gone from get_holds.
            released = (h is None) if hold == FORCE_FIELD_HOLD else (
                h is not None and not h.get("active") and h.get("holder", {}).get("kind") == "none")
            ck.check(released, f"the server's {hold} is released when it exits", h)
        g = now.get("god_mode", {})
        ck.check(g.get("active") and g.get("holder", {}).get("you"), "the UI's god mode is untouched", g)
        if ui_claimed_see_through:
            s = ui.request("seethrough_get_state")
            ck.check(s.get("active") and s.get("claims") == 1, "See-through stays on with the UI's claim alone", s)
    finally:
        if exited is None:
            c.close()
        ui.request("set_god_mode", enable=False)
        if ui_claimed_see_through:
            ui.request("seethrough_set", enable=False)
        ui.request("reset_field", class_name=FIXTURE_CLASS, field_name=FIXTURE_FIELD)
        switch.restore()
        left = sorted(k for k, h in holds().items() if h.get("active"))
        ck.check(not left, "clean-up: nothing is left held", left)
        ui.close()
    return ck.done(c)


def _suspend(*args: str) -> tuple[int, str]:
    out = subprocess.run([sys.executable, str(pathlib.Path(__file__).with_name("suspend.py")), *args],
                         capture_output=True, text=True)
    return out.returncode, out.stdout


_GAME_THREADS: dict[str, int | None] = {}


def _game_thread(name: str) -> int | None:
    """The UE game thread: the process's oldest thread (suspend.py's own rule), looked up once per name -- it does not
    change while the game runs. A name that matches several processes (a launcher shim beside the game) raises; main()
    resolves it before any case runs, so that refusal comes first."""
    if name not in _GAME_THREADS:
        mains = [line for line in _suspend("threads", name)[1].splitlines() if "<-- main thread" in line]
        if len(mains) > 1:
            raise McpError(f"--suspend-name {name!r} matches {len(mains)} processes; pass the full image stem")
        _GAME_THREADS[name] = int(mains[0].split("tid=")[1].split()[0]) if mains else None
    return _GAME_THREADS[name]


class _FrozenGameThread:
    """The game thread suspended while the DLL's pipe threads keep answering: a game-thread call then times out (-5)
    and stays queued. A suspend that did not take raises, so no check runs against a game that was never frozen; a
    resume is checked and retried, and one that never took raises and stays recorded in `left_suspended`, so a caller
    sends nothing more to a frozen game."""

    left_suspended: str | None = None

    def __init__(self, name: str):
        self.name = name
        self.tid = _game_thread(name)
        self.suspend_line = ""

    def __enter__(self):
        if self.tid is None:
            raise McpError(f"no game thread found for {self.name}")
        rc, out = _suspend("suspend-tid", self.name, str(self.tid))
        if rc != 0 or f"tid {self.tid}: SUSPENDED" not in out:
            raise McpError(f"the game thread was not suspended (suspend.py exit {rc}): {out.strip()}")
        self.suspend_line = out.strip()
        return self

    def __exit__(self, exc_type, *exc):
        out = ""
        for _ in range(3):
            rc, out = _suspend("resume-tid", self.name, str(self.tid))
            if rc == 0 and f"tid {self.tid}: RESUMED" in out:
                return False
            time.sleep(0.5)
        _FrozenGameThread.left_suspended = (f"GAME THREAD LEFT SUSPENDED: tid {self.tid} in {self.name}; run: py "
                                            f"tools/verify/suspend.py resume-tid {self.name} {self.tid}")
        print("  !! " + _FrozenGameThread.left_suspended + f" (last answer: {out.strip()})", flush=True)
        if exc_type is None:
            raise McpError(_FrozenGameThread.left_suspended)
        return False


def _camera_force_off(ui, tries: int = 3) -> int:
    """The UI stand-in's debug-camera OFF, re-sent until the camera reads off: an OFF sent while an earlier toggle is
    still queued answers -5/-6 and sends nothing, and a queued OFF does not turn the camera off on stock UE 4.18-5.7.
    Returns the state it ends on."""
    def state() -> int:
        return ui.request("get_debug_camera_state").get("state", -99)

    for _ in range(tries):
        if state() != 1:
            break
        ui.request("set_debug_camera", enable=False)
        end = time.monotonic() + 6.0
        while time.monotonic() < end and state() != 0:
            time.sleep(0.25)
    return state()


def _camera_cleanup(ui) -> None:
    """A rig's last step: the camera off, unless the game thread was left suspended (an OFF then only queues)."""
    try:
        if _FrozenGameThread.left_suspended:
            print("  !! " + _FrozenGameThread.left_suspended + " -- the debug camera was not turned off", flush=True)
        elif _camera_force_off(ui) == 1:
            print("  !! the debug camera is LEFT ON in the game, held by this rig's process (a foreground client, so "
                  "it outlives it): turn it off by hand", flush=True)
    finally:
        ui.close()


def _pipe_log_line(pattern: str, since: str) -> str | None:
    """The newest line of any game's DLL pipe log that matches `pattern` and is stamped at or after `since` (local
    "YYYY-MM-DD HH:MM:SS", the DLL log's own stamp). Bounded by time because pids are reused and every game's pipe log
    is read: a pid alone could take an earlier run's line."""
    sys.path.insert(0, str(pathlib.Path(__file__).parent))
    from pipe_client import _read_pipe_logs   # noqa: E402 -- the rig beside this one

    rx = re.compile(pattern)
    lines = [line.strip() for text in _read_pipe_logs() for line in text.splitlines()
             if line.startswith("[") and line[1:20] >= since and rx.search(line)]
    return max(lines, key=lambda line: line[1:24]) if lines else None


def _release_line(server_pid: int, hold: str, since: str) -> str | None:
    """The DLL's log line for a background client's hold released on its exit; its code is what the release returned
    (-5: left queued)."""
    return _pipe_log_line(rf"background client {server_pid} left -- its hold {re.escape(hold)} is released "
                          r"\(code -?\d+\)", since)


def _le_hex(value: int, size: int) -> str:
    return (value & ((1 << (8 * size)) - 1)).to_bytes(size, "little").hex().upper()


def run_raw(exe: str, pid: int | None, game_name: str) -> int:
    sys.path.insert(0, str(pathlib.Path(__file__).parent))
    from pipe_client import PipeClient   # noqa: E402 -- the rig beside this one

    ck = Checks()
    c = McpClient(exe)
    c.initialize()
    base = {"pid": pid} if pid else {}

    def call(client, tool, args=None, timeout=180.0):
        r = client.call(tool, {**base, **(args or {})}, timeout)
        data, note = payload(r)
        return r, data, note

    def error_text(data) -> str:
        return data.get("error", "") if isinstance(data, dict) else str(data)

    tools = {t["name"]: t for t in c.request("tools/list").get("result", {}).get("tools", [])}
    for name in ("write_memory", "write_invoke"):
        t = tools.get(name, {})
        ck.check(t.get("_meta", {}).get("anthropic/requiresUserInteraction") is True
                 and t.get("annotations", {}).get("destructiveHint") is True, f"{name} listed, asks every time", t)
    ck.check(tools.get("write_invoke", {}).get("annotations", {}).get("idempotentHint") is False,
             "write_invoke is not idempotent")

    r, st, _ = call(c, "read_status")
    build = st.get("build_number") or 0
    if not ck.check(st.get("scanned") and build >= 3706, f"the game is scanned, DLL build {build} (D2-3 needs 3706+)",
                    st):
        c.close()
        return ck.done(c)
    game = pid or st.get("pid")
    ui = PipeClient(pid=game, timeout=120)
    ui.connect()
    me = os.getpid()
    print(f"        game pid {game}; the foreground client (the UI's stand-in) is pid {me}")

    def outstanding(background: bool | None = None) -> int:
        total = 0
        for e in ui.request("get_holds").get("invokes", []):
            h = e.get("holder", {})
            if background is None or bool(h.get("background")) == background:
                total += e.get("outstanding", 0)
        return total

    def settle(background: bool | None = None, seconds: float = 10.0) -> int:
        end = time.monotonic() + seconds
        while time.monotonic() < end:
            n = outstanding(background)
            if n == 0:
                return 0
            time.sleep(0.2)
        return outstanding(background)

    try:
        # 1. write_memory on the fixture's own int field.
        r, live, _ = call(c, "read_find_live_instance", {"class_name": "DumperTestHolder"})
        inst, cls = live.get("addr"), live.get("class_addr")
        r, klass, _ = call(c, "read_class", {"addr": cls})
        field = next((f for f in klass.get("class", {}).get("fields", []) if f.get("name") == "HolderValue"), None)
        if ck.check(inst and field, "DumperTestHolder.HolderValue found", (live, field)):
            faddr = "0x%X" % (int(inst, 16) + field["offset"])
            r, mem, _ = call(c, "read_memory", {"addr": faddr, "size": 4})
            old = mem.get("bytes", "").upper()
            new = _le_hex(int.from_bytes(bytes.fromhex(old), "little") + 1, 4)
            r, d, _ = call(c, "write_memory", {"addr": faddr, "bytes": new, "expect": old})
            ck.check(not r.get("isError") and d.get("before", "").upper() == old and d.get("after", "").upper() == new,
                     f"write_memory {old} -> {new}, before and after returned", d)
            r, d, _ = call(c, "write_memory", {"addr": faddr, "bytes": old, "expect": old})
            ck.check(r.get("isError") and d.get("code") == -23 and d.get("current", "").upper() == new,
                     "a write whose expect no longer matches is refused, with what is there now", d)
            r, d, _ = call(c, "write_memory", {"addr": faddr, "bytes": old, "expect": new})
            ck.check(not r.get("isError") and d.get("after", "").upper() == old, "and the field is put back", d)

            # Read-only and code pages, written with the very bytes they hold: harmless even if it went through.
            r, vt, _ = call(c, "read_memory", {"addr": inst, "size": 8})
            vtable = int.from_bytes(bytes.fromhex(vt.get("bytes", "00" * 8)), "little")
            r, slot, _ = call(c, "read_memory", {"addr": "0x%X" % vtable, "size": 8})
            slot_hex = slot.get("bytes", "").upper()
            r, d, _ = call(c, "write_memory", {"addr": "0x%X" % vtable, "bytes": slot_hex, "expect": slot_hex})
            ck.check(r.get("isError") and d.get("code") in (-22, -21), "a write to the vtable (read-only) is refused", d)
            code_addr = int.from_bytes(bytes.fromhex(slot_hex or "00" * 8), "little")
            r, code_bytes, _ = call(c, "read_memory", {"addr": "0x%X" % code_addr, "size": 4})
            cb = code_bytes.get("bytes", "").upper()
            r, d, _ = call(c, "write_memory", {"addr": "0x%X" % code_addr, "bytes": cb, "expect": cb})
            ck.check(r.get("isError") and d.get("code") == -21, "a write to code is refused as code", d)

        # 2. write_invoke: a computed value, by name.
        r, kml, _ = call(c, "read_find_object", {"path": "Default__KismetMathLibrary"})
        cdo = kml.get("addr")
        r, d, _ = call(c, "write_invoke", {"instance_addr": cdo, "function": "Add_IntInt", "params": {"A": 40, "B": 2}})
        ck.check(not r.get("isError") and d.get("return") == "42", "write_invoke Add_IntInt(40, 2) returns 42", d)

        # 3. The deny list: the debug-camera toggle points to write_debug_camera.
        r, cm, _ = call(c, "read_find_object", {"path": "Default__CheatManager"})
        r, d, _ = call(c, "write_invoke", {"instance_addr": cm.get("addr"), "function": "ToggleDebugCamera"})
        ck.check(r.get("isError") and "write_debug_camera" in error_text(d),
                 "write_invoke ToggleDebugCamera is refused, pointing to write_debug_camera", d)

        # 4. write_debug_camera ON on a Shipping fixture with no live CheatManager: an error now, not a success.
        r, d, note = call(c, "write_debug_camera", {"enable": True})
        # A camera that really came on means a live CheatManager (a Development build): the refusal is not this
        # fixture's to show. Only a state of 1 the game confirms counts, so a refusal read as success still fails.
        live_cheat_manager = (not r.get("isError") and isinstance(d, dict) and d.get("state") == 1
                              and ui.request("get_debug_camera_state").get("state") == 1)
        if not r.get("isError"):
            call(c, "write_debug_camera", {"enable": False})
        note = note or (d.get("note") if isinstance(d, dict) else None)   # an error carries its note in the payload
        if live_cheat_manager:
            print("  skip  write_debug_camera ON turned the camera on: this fixture has a live CheatManager; the "
                  "refusal is a Shipping check (dbgcam / dbgcamhold cover a live one)")
        else:
            ck.check(r.get("isError") and "CheatManager" in (note or ""), "write_debug_camera ON with no CheatManager "
                     "is reported as refused (state -17)", (d, note))

        # 5. The UI's queued invoke: the AI's is refused, naming it, until it has run.
        ck.check(settle() == 0, "no invoke is outstanding before the conflict check")
        with _FrozenGameThread(game_name):
            q = ui.request("invoke_function", class_name="KismetMathLibrary", func_name="Add_IntInt", parms_size=12,
                           params_hex="01000000" + "02000000" + "00000000")
            ck.check(q.get("result") == -5, "the UI's invoke times out (-5) and stays queued while the game thread is "
                     "frozen", q)
            ck.check(outstanding(background=False) >= 1, "get_holds lists the UI's outstanding invoke")
            r, d, _ = call(c, "write_invoke", {"instance_addr": cdo, "function": "Add_IntInt", "params": {"A": 1, "B": 1}})
            ck.check(r.get("isError") and f"(pid {me})" in error_text(d),
                     "the AI's write_invoke is refused, naming the UI's process", d)
        ck.check(settle(background=False) == 0, "the UI's queued invoke runs once the game thread resumes")
        r, d, _ = call(c, "write_invoke", {"instance_addr": cdo, "function": "Add_IntInt", "params": {"A": 1, "B": 1}})
        ck.check(not r.get("isError") and d.get("return") == "2", "and then the AI's goes through", d)

        # 6. The AI's own queued invoke: never resent, dropped when its session ends.
        with _FrozenGameThread(game_name):
            r, d, note = call(c, "write_invoke", {"instance_addr": cdo, "function": "Add_IntInt",
                                                  "params": {"A": 5, "B": 5}})
            ck.check(not r.get("isError") and d.get("queued") is True, "the AI's invoke times out and is reported "
                     "queued", (d, note))
            r, d, _ = call(c, "write_invoke", {"instance_addr": cdo, "function": "Add_IntInt",
                                               "params": {"A": 6, "B": 6}})
            ck.check(r.get("isError") and "do not send it again" in error_text(d),
                     "a second write_invoke is refused while the first is still queued", d)
            ck.check(outstanding(background=True) == 1, "get_holds lists the AI's one outstanding invoke")
            code = c.close()
            ck.check(code == 0, "the server exits while its invoke is still queued", code)
            ck.check(settle(background=True, seconds=10) == 0, "its queued invoke is dropped when it leaves, before "
                     "the game thread resumes")
        time.sleep(1.0)
        ck.check(settle() == 0, "nothing is left outstanding after the game thread resumes")
    finally:
        try:
            c.close()
        except Exception:  # noqa: BLE001 -- already closed
            pass
        ui.close()
    return ck.done(c)


def run_dbgcam(exe: str, pid: int | None, game_name: str) -> int:
    sys.path.insert(0, str(pathlib.Path(__file__).parent))
    from pipe_client import PipeClient   # noqa: E402 -- the rig beside this one

    ck = Checks()
    base = {"pid": pid} if pid else {}

    def call(client, tool, args=None, timeout=180.0):
        r = client.call(tool, {**base, **(args or {})}, timeout)
        data, note = payload(r)
        if note is None and isinstance(data, dict):
            note = data.get("note")
        return r, data, note

    def error_text(data) -> str:
        return data.get("error", "") if isinstance(data, dict) else str(data)

    ai = McpClient(exe)
    ai.initialize()
    sessions = [ai]
    r, st, _ = call(ai, "read_status")
    build = st.get("build_number") or 0
    if not ck.check(st.get("scanned") and build >= 3706, f"the game is scanned, DLL build {build} (needs 3706+)", st):
        ai.close()
        return ck.done(sessions)
    game = pid or st.get("pid")
    ui = PipeClient(pid=game, timeout=120)
    ui.connect()
    me = os.getpid()
    print(f"        game pid {game}; the UI stand-in (foreground, no if_free) is pid {me}")

    def cam() -> int:
        return ui.request("get_debug_camera_state").get("state", -99)

    def camera_hold() -> dict:
        return next((h for h in ui.request("get_holds").get("holds", []) if h.get("hold") == "debug_camera"), {})

    def outstanding(background: bool) -> int:
        return sum(e.get("outstanding", 0) for e in ui.request("get_holds").get("invokes", [])
                   if bool(e.get("holder", {}).get("background")) == background)

    def wait_cam(want: int, seconds: float = 15.0) -> int:
        end = time.monotonic() + seconds
        while time.monotonic() < end:
            s = cam()
            if s == want:
                return s
            time.sleep(0.25)
        return cam()

    def camera_off(label: str):
        ck.check(_camera_force_off(ui) == 0 and not camera_hold().get("active"), f"{label}: the camera is off again")

    if not ck.check(cam() in (0, 1), "the debug camera's state reads (a live CheatManager)", cam()):
        ui.close()
        ai.close()
        return ck.done(sessions)
    if cam() == 1:
        camera_off("start")
    try:
        # A. The AI's ON queued; the UI joins it; the AI session ends.
        with _FrozenGameThread(game_name):
            r, d, note = call(ai, "write_debug_camera", {"enable": True})
            ck.check(not r.get("isError") and d.get("state") == -5, "A: the AI's ON is queued (-5)", (d, note))
            h = camera_hold()
            ck.check(h.get("pending") and h.get("holder", {}).get("background"), "A: the pending ON is the AI's", h)
            j = ui.request("set_debug_camera", enable=True)
            ck.check(j.get("state") == -5, "A: the UI's ON joins it (-5, 'your toggle is queued')", j)
            h = camera_hold()
            ck.check(h.get("pending") and h.get("holder", {}).get("you"), "A: the pending ON is the UI's now", h)
            ck.check(outstanding(False) == 1 and outstanding(True) == 0,
                     "A: the queued toggle is counted under the UI, not the AI")
            ck.check(ai.close() == 0, "A: the AI session ends while the toggle is queued")
            time.sleep(1.0)
            ck.check(outstanding(False) == 1, "A: the AI session's end did not drop it")
        ck.check(wait_cam(1) == 1, "A: the game resumes and the camera comes on")
        h = camera_hold()
        ck.check(h.get("active") and h.get("holder", {}).get("you"), "A: the camera is on, held by the UI", h)
        camera_off("A")

        # B. The UI's ON queued: the AI's is refused, naming the UI.
        ai = McpClient(exe)
        ai.initialize()
        sessions.append(ai)
        with _FrozenGameThread(game_name):
            j = ui.request("set_debug_camera", enable=True)
            ck.check(j.get("state") == -5, "B: the UI's ON is queued (-5)", j)
            r, d, _ = call(ai, "write_debug_camera", {"enable": True})
            ck.check(r.get("isError") and f"(pid {me})" in error_text(d),
                     "B: the AI's write_debug_camera is refused, naming the UI's process", d)
        ck.check(wait_cam(1) == 1 and camera_hold().get("holder", {}).get("you"), "B: the camera comes on, the UI's")
        camera_off("B")

        # C. The UI's ON sent while the AI's toggle is still being dispatched. Every pipe command that changes a hold
        # runs under that hold's lock (Mimic's too), so it waits for the AI's dispatch to time out and then joins the
        # QUEUED toggle, as in A. Frieren's "being dispatched" join is reached only by a caller that takes no hold lock
        # (the UE5_SetDebugCamera export called directly): not from here.
        with _FrozenGameThread(game_name):
            rid = ai.request_async("tools/call", {"name": "write_debug_camera", "arguments": {**base, "enable": True}})
            time.sleep(1.5)
            t0 = time.monotonic()
            j = ui.request("set_debug_camera", enable=True)
            waited = time.monotonic() - t0
            ck.check(waited >= 2.0, f"C: the UI's ON waits for the AI's dispatch (the hold lock): {waited:.1f} s")
            ck.check(j.get("state") == -5, "C: then joins the AI's queued toggle (-5)", j)
            res = ai.wait(rid, timeout=60).get("result", {})
            d, _ = payload(res)
            ck.check(not res.get("isError") and d.get("state") == -5, "C: the AI's toggle then answers queued", d)
            h = camera_hold()
            ck.check(h.get("pending") and h.get("holder", {}).get("you"), "C: the pending ON is the UI's", h)
            ck.check(outstanding(False) == 1 and outstanding(True) == 0, "C: and counted under the UI")
            ck.check(ai.close() == 0, "C: the AI session ends while the toggle is queued")
            time.sleep(1.0)
            ck.check(outstanding(False) == 1, "C: the AI session's end did not drop it")
        ck.check(wait_cam(1) == 1, "C: the game resumes and the camera comes on")
        h = camera_hold()
        ck.check(h.get("active") and h.get("holder", {}).get("you"), "C: the camera is on, held by the UI", h)
        camera_off("C")
    finally:
        for c in sessions:
            try:
                c.close()
            except Exception:  # noqa: BLE001 -- already closed
                pass
        _camera_cleanup(ui)
    return ck.done(sessions)


def run_dbgcam_hold(exe: str, pid: int | None, game_name: str) -> int:
    sys.path.insert(0, str(pathlib.Path(__file__).parent))
    from pipe_client import PipeClient   # noqa: E402 -- the rig beside this one

    ck = Checks()
    base = {"pid": pid} if pid else {}
    sessions: list[McpClient] = []

    def session() -> McpClient:
        c = McpClient(exe)
        c.initialize()
        sessions.append(c)
        return c

    def call(client, tool, args=None, timeout=180.0):
        r = client.call(tool, {**base, **(args or {})}, timeout)
        data, note = payload(r)
        if note is None and isinstance(data, dict):
            note = data.get("note")
        return r, data, note

    def error_text(data) -> str:
        return data.get("error", "") if isinstance(data, dict) else str(data)

    ai = session()
    r, st, _ = call(ai, "read_status")
    build = st.get("build_number") or 0
    if not ck.check(st.get("scanned") and build >= 3706, f"the game is scanned, DLL build {build} (needs 3706+)", st):
        ai.close()
        return ck.done(sessions)
    game = pid or st.get("pid")
    ui = PipeClient(pid=game, timeout=120)
    ui.connect()
    me = os.getpid()
    print(f"        game pid {game}; the UI stand-in (foreground, no if_free) is pid {me}")

    def cam() -> int:
        return ui.request("get_debug_camera_state").get("state", -99)

    def camera_hold() -> dict:
        return next((h for h in ui.request("get_holds").get("holds", []) if h.get("hold") == "debug_camera"), {})

    def wait_cam(want: int, seconds: float = 15.0) -> int:
        end = time.monotonic() + seconds
        while time.monotonic() < end:
            s = cam()
            if s == want:
                return s
            time.sleep(0.25)
        return cam()

    def wait_hold(pred, seconds: float = 15.0) -> dict:
        end = time.monotonic() + seconds
        while time.monotonic() < end:
            h = camera_hold()
            if pred(h):
                return h
            time.sleep(0.25)
        return camera_hold()

    def is_free(h: dict) -> bool:
        return not h.get("active") and not h.get("pending") and h.get("holder", {}).get("kind") == "none"

    def held_by_server(h: dict, c: McpClient) -> bool:
        holder = h.get("holder", {})
        return (h.get("active") and holder.get("kind") == "client" and holder.get("background") is True
                and not holder.get("you") and holder.get("pid") == c.proc.pid)

    def held_by_ui(h: dict) -> bool:
        holder = h.get("holder", {})
        return bool(h.get("active") and holder.get("you") and not holder.get("background") and holder.get("pid") == me)

    def refused_naming_ui(r, d) -> bool:
        text = error_text(d)
        return bool(r.get("isError") and "debug_camera is held by the UE5DumpUI window or another program" in text
                    and f"(pid {me})" in text)

    def camera_off(label: str):
        if _camera_force_off(ui) == 0 and not is_free(camera_hold()):
            # Off, but the ledger still names a holder (the camera turned off outside the pipe): an OFF over an off
            # camera is a no-op toggle, and its hold recorder forgets that owner.
            ui.request("set_debug_camera", enable=False)
        ck.check(cam() == 0 and is_free(camera_hold()), f"{label}: the camera is off and free again", camera_hold())

    def stalled_exit(label: str, tmo: int, during=None) -> dict | None:
        """A new AI session turns the camera on, then ends while the game thread is frozen; `during()` runs while it is
        still frozen, after the release returned. What was seen, or None when the freeze did not take (a failed
        check: D-G stay counted)."""
        c = session()
        seen: dict = {"server_pid": c.proc.pid, "since": time.strftime("%Y-%m-%d %H:%M:%S"), "released_after": None}
        r, d, _ = call(c, "write_debug_camera", {"enable": True})
        ck.check(not r.get("isError") and d.get("state") == 1 and held_by_server(camera_hold(), c),
                 f"{label}: the AI's ON, the server's", d)
        with contextlib.ExitStack() as stack:
            try:
                frozen = stack.enter_context(_FrozenGameThread(game_name))
            except McpError as e:
                ck.check(False, f"{label}: the game thread is frozen", str(e))
                return None
            # game_thread_stalled turns true 500 ms after the game thread's last ProcessEvent.
            stalled, end = False, time.monotonic() + 3.0
            while not stalled and time.monotonic() < end:
                stalled = ui.request("get_holds").get("game_thread_stalled") is True
                if not stalled:
                    time.sleep(0.2)
            if not ck.check(stalled, f"{label}: the game thread reads stalled ({frozen.suspend_line})"):
                return None
            ck.check(c.close() == 0, f"{label}: the AI session ends while the game thread is stalled")
            t0 = time.monotonic()
            while time.monotonic() < t0 + tmo / 1000 + 3.0:
                if camera_hold().get("holder", {}).get("kind") != "client":
                    seen["released_after"] = round(time.monotonic() - t0, 1)
                    break
                time.sleep(0.25)
            time.sleep(0.5)
            seen["frozen"] = camera_hold()
            if during is not None:
                during()
        return seen

    if not ck.check(cam() in (0, 1), "the debug camera's state reads (a live CheatManager)", cam()):
        ui.close()
        ai.close()
        return ck.done(sessions)
    if cam() == 1 or not is_free(camera_hold()):
        camera_off("start")
    try:
        # D. The AI turns the camera on, read_holds names the server, and its session's end turns it off.
        r, d, note = call(ai, "write_debug_camera", {"enable": True})
        ck.check(not r.get("isError") and d.get("state") == 1, "D: the AI's write_debug_camera ON turns it on (1)",
                 (d, note))
        ck.check(cam() == 1, "D: the camera reads on")
        r, d, _ = call(ai, "read_holds")
        mine = next((h for h in (d or {}).get("holds", []) if h.get("hold") == "debug_camera"), {})
        ck.check(mine.get("active") and mine.get("holder", {}).get("you") and mine["holder"].get("background"),
                 "D: read_holds: debug_camera is the server's ('you'), marked background", mine)
        ck.check(held_by_server(camera_hold(), ai), "D: get_holds from the UI: held by the server's pid, not the UI",
                 camera_hold())
        r, d, note = call(ai, "write_debug_camera", {"enable": True})
        ck.check(not r.get("isError") and d.get("state") == 1 and held_by_server(camera_hold(), ai),
                 "D: the AI's ON again over its own camera: a no-op, still the server's", (d, note))
        ck.check(ai.close() == 0, "D: the AI session ends (the server exits 0)")
        ck.check(wait_cam(0) == 0, "D: the session's end turns the camera off")
        h = wait_hold(is_free)
        ck.check(is_free(h), "D: and the hold is free (no holder)", h)

        # E. The AI turns its own camera off; its end then has nothing to release.
        ai = session()
        r, d, _ = call(ai, "write_debug_camera", {"enable": True})
        ck.check(not r.get("isError") and d.get("state") == 1, "E: the AI's ON", d)
        r, d, note = call(ai, "write_debug_camera", {"enable": False})
        ck.check(not r.get("isError") and d.get("state") == 0, "E: the AI's OFF of its own camera turns it off (0)",
                 (d, note))
        h = camera_hold()
        ck.check(is_free(h), "E: the hold is free", h)
        ck.check(ai.close() == 0, "E: the AI session ends")
        time.sleep(2.0)
        ck.check(cam() == 0 and is_free(camera_hold()), "E: the camera stays off, free")

        # F. A camera the UI took over (Force ON over the AI's) outlives the AI session, and the AI cannot change it.
        ai = session()
        r, d, _ = call(ai, "write_debug_camera", {"enable": True})
        ck.check(not r.get("isError") and d.get("state") == 1 and held_by_server(camera_hold(), ai),
                 "F: the AI's ON, the server's", d)
        j = ui.request("set_debug_camera", enable=True)
        ck.check(j.get("state") == 1, "F: the UI's Force ON over it answers on (1, a no-op toggle)", j)
        ck.check(held_by_ui(camera_hold()), "F: the camera is the UI's now", camera_hold())
        r, d, _ = call(ai, "write_debug_camera", {"enable": False})
        ck.check(refused_naming_ui(r, d), "F: the AI's OFF is refused, naming the UI's process", d)
        r, d, _ = call(ai, "write_debug_camera", {"enable": True})
        ck.check(refused_naming_ui(r, d), "F: the AI's ON is refused too (the camera is someone else's)", d)
        ck.check(cam() == 1, "F: the camera stays on")
        ck.check(ai.close() == 0, "F: the AI session ends")
        time.sleep(2.0)
        ck.check(cam() == 1 and held_by_ui(camera_hold()), "F: the camera stays on, the UI's", camera_hold())
        camera_off("F")

        # G. A camera the UI turned on from off: the AI cannot change it, and its end leaves it alone.
        ai = session()
        j = ui.request("set_debug_camera", enable=True)
        ck.check(j.get("state") == 1 and held_by_ui(camera_hold()), "G: the UI's Force ON, the UI's", (j, camera_hold()))
        r, d, _ = call(ai, "write_debug_camera", {"enable": False})
        ck.check(refused_naming_ui(r, d), "G: the AI's OFF is refused, naming the UI's process", d)
        ck.check(ai.close() == 0, "G: the AI session ends")
        time.sleep(2.0)
        ck.check(cam() == 1 and held_by_ui(camera_hold()), "G: the camera stays on, the UI's", camera_hold())
        camera_off("G")

        # H. [DBGCAM-RELEASE-STALLED] The AI's session ends while the game thread is stalled. The release's OFF waits the
        # DLL's invoke timeout, times out and stays queued -- and on stock UE 4.18-5.7 a queued OFF does not turn the
        # camera off. The release is owed: once the queued OFF has run, the DLL sends it again, and the camera goes off.
        # The timeout is the live one (a per-game override or set_invoke_timeout can change it).
        tmo = int(ui.request("get_pointers").get("invoke_timeout_ms") or 5000)
        ue = int(ui.request("get_pointers").get("ue_version") or 0)
        print(f"        H, I: the DLL's invoke timeout is {tmo} ms; UE {ue}")
        if not ck.check(tmo <= 30000, f"H, I: an invoke timeout this rig can freeze the game for ({tmo} ms)"):
            return ck.done(sessions)
        seen = stalled_exit("H", tmo)
        if seen:
            f = seen["frozen"]
            ck.check(seen["released_after"] is not None,
                     f"H: the release returned during the stall (after {seen['released_after']} s)")
            ck.check(f.get("active") and f.get("holder", {}).get("kind") == "none" and f.get("release_owed") is True,
                     "H: during the stall the camera is on, with no holder and its release owed", f)
            ck.check(wait_cam(0) == 0, "H: the game resumes and the owed release turns the camera off")
            h = wait_hold(lambda x: is_free(x) and "release_owed" not in x)
            ck.check(is_free(h) and "release_owed" not in h, "H: the hold is free, nothing owed", h)
            pid_ = seen["server_pid"]
            ck.check("(code -5)" in (_release_line(pid_, "debug_camera", seen["since"]) or ""),
                     "H: the DLL log: the release's OFF was left queued (-5)")
            ck.check(_pipe_log_line(rf"background client {pid_}'s debug-camera release is done", seen["since"])
                     is not None, "H: the DLL log: the owed release is done")
            sent = _pipe_log_line(rf"background client {pid_}'s debug-camera release .* OFF sent again -> state 0",
                                  seen["since"])
            if ue < 508:
                ck.check(sent is not None, "H: the DLL log: the queued OFF did not turn it off (UE < 5.8), so the OFF "
                                           "was sent again -> 0")
        camera_off("H")

        # I. The same, but the UI takes the camera during the stall (Force ON): the owed release is abandoned, and the
        # camera is never turned off under the UI.
        def take_over():
            j = ui.request("set_debug_camera", enable=True)
            ck.check(j.get("state") == -6, "I: the UI's Force ON during the stall is not sent: the owed OFF is queued "
                                           "(-6)", j)
            h = wait_hold(lambda x: "release_owed" not in x, 3.0)
            ck.check(held_by_ui(h) and "release_owed" not in h,
                     "I: the camera is the UI's now, and the owed release is abandoned", h)

        seen = stalled_exit("I", tmo, during=take_over)
        if seen:
            time.sleep(5.0)
            if ue < 508:
                ck.check(cam() == 1 and held_by_ui(camera_hold()),
                         "I: after the resume the camera stays on, the UI's (the queued OFF is a no-op on UE < 5.8)",
                         camera_hold())
            ck.check(_pipe_log_line(rf"background client {seen['server_pid']}'s debug-camera release is abandoned",
                                    seen["since"]) is not None, "I: the DLL log: the owed release is abandoned")
            ck.check(_pipe_log_line(rf"background client {seen['server_pid']}'s debug-camera release .* OFF sent "
                                    r"again", seen["since"]) is None, "I: and no OFF was sent again")
        camera_off("I")

        # J. The AI leaves MID-COMMAND while the game thread is stalled: its write_invoke waits on the game thread when
        # its session ends, so the DLL raises that connection's cancel flag, and the release runs with it raised. Before
        # the review fix the release's camera read could give up under that flag ("unknown"): nothing released or owed.
        # Its reads are cancel-immune now. (read_pose cannot stand in: on this fixture it answers from memory at once.)
        c = session()
        since, server_pid = time.strftime("%Y-%m-%d %H:%M:%S"), c.proc.pid
        r, kml, _ = call(c, "read_find_object", {"path": "Default__KismetMathLibrary"})
        r, d, _ = call(c, "write_debug_camera", {"enable": True})
        ck.check(not r.get("isError") and d.get("state") == 1 and held_by_server(camera_hold(), c),
                 "J: the AI's ON, the server's", d)
        with _FrozenGameThread(game_name) as frozen:
            stalled, end = False, time.monotonic() + 3.0
            while not stalled and time.monotonic() < end:
                stalled = ui.request("get_holds").get("game_thread_stalled") is True
                if not stalled:
                    time.sleep(0.2)
            if ck.check(stalled, f"J: the game thread reads stalled ({frozen.suspend_line})"):
                c.request_async("tools/call", {"name": "write_invoke", "arguments": {
                    **base, "instance_addr": kml.get("addr"), "function": "Add_IntInt", "params": {"A": 1, "B": 2}}})
                time.sleep(1.5)   # the invoke is in flight, waiting on the game thread
                ck.check(c.close(timeout=15.0) == 0, "J: the AI session ends with its write_invoke still waiting")
                ck.check(_pipe_log_line(r"client gone mid-command", since) is not None,
                         "J: the DLL saw it leave mid-command (its connection's cancel raised)")
                # Its command returns after the invoke timeout; then the release waits as long again.
                t0 = time.monotonic()
                while time.monotonic() < t0 + 2 * tmo / 1000 + 5.0:
                    if camera_hold().get("holder", {}).get("kind") != "client":
                        break
                    time.sleep(0.25)
                time.sleep(0.5)
                f = camera_hold()
                ck.check(f.get("active") and f.get("holder", {}).get("kind") == "none" and f.get("release_owed") is True,
                         "J: during the stall the camera is on, with no holder and its release owed", f)
        ck.check(wait_cam(0) == 0, "J: the game resumes and the owed release turns the camera off")
        h = wait_hold(lambda x: is_free(x) and "release_owed" not in x)
        ck.check(is_free(h) and "release_owed" not in h, "J: the hold is free, nothing owed", h)
        ck.check("(code -5)" in (_release_line(server_pid, "debug_camera", since) or ""),
                 "J: the DLL log: the release read the camera and sent its OFF, left queued (-5)",
                 _release_line(server_pid, "debug_camera", since))
        camera_off("J")
    finally:
        for c in sessions:
            try:
                c.close()
            except Exception:  # noqa: BLE001 -- already closed
                pass
        _camera_cleanup(ui)
    return ck.done(sessions)


def _ce_schedule(desc: str, active: bool) -> str:
    """Tick or untick a CE record on CE's main thread from a timer (ce_lua_eval.py), so this returns at once."""
    chunk = ("local t=createTimer(nil,false) t.Interval=50 t.OnTimer=function(x) x.destroy() "
             f"local r=getAddressList().getMemoryRecordByDescription([[{desc}]]) if r then r.Active="
             f"{str(active).lower()} end end t.Enabled=true return 'scheduled'")
    out = subprocess.run([sys.executable, str(pathlib.Path(__file__).with_name("ce_lua_eval.py")), chunk,
                          "--timeout", "60"], capture_output=True, text=True)
    return out.stdout.strip().splitlines()[-1] if out.stdout.strip() else out.stderr.strip()


def run_dbgcam_stop(exe: str, pid: int | None, game_name: str, ce_desc: str) -> int:
    """[DBGCAM-RELEASE-STALLED] A CE Disable/Enable around an AI session's camera release. K: a release owed (the AI's
    session ended with the game thread stalled) is dropped at the stop, and nothing turns the camera off after the
    re-enable. K2: the stop lands while the release is still waiting (invoke timeout raised to 20 s): Stark::Shutdown
    answers its OFF -7, so it ends with code -1 and is never owed. Needs a live CheatManager (DumperTest Development)
    and the DLL started by the CE record `ce_desc`, whose untick runs UE5_Shutdown."""
    sys.path.insert(0, str(pathlib.Path(__file__).parent))
    from pipe_client import PipeClient   # noqa: E402 -- the rig beside this one

    ck = Checks()
    base = {"pid": pid} if pid else {}
    sessions: list[McpClient] = []

    def session() -> McpClient:
        c = McpClient(exe)
        c.initialize()
        sessions.append(c)
        return c

    def call(client, tool, args=None):
        r = client.call(tool, {**base, **(args or {})}, 180.0)
        data, note = payload(r)
        return r, data, note

    ai = session()
    r, st, _ = call(ai, "read_status")
    build = st.get("build_number") or 0
    if not ck.check(st.get("scanned") and build >= 3707, f"the game is scanned, DLL build {build} (needs 3707+)", st):
        ai.close()
        return ck.done(sessions)
    game = pid or st.get("pid")
    ai.close()

    def connect(retries: int = 10):
        c = PipeClient(pid=game, timeout=120)
        c.connect(retries=retries, delay=1.0)
        return c

    def camera_hold(c) -> dict:
        return next((h for h in c.request("get_holds").get("holds", []) if h.get("hold") == "debug_camera"), {})

    def cam(c) -> int:
        return c.request("get_debug_camera_state").get("state", -99)

    def is_free(h: dict) -> bool:
        return h.get("holder", {}).get("kind") == "none" and "release_owed" not in h and not h.get("pending")

    def server_holds(h: dict, c: McpClient) -> bool:
        holder = h.get("holder", {})
        return bool(h.get("active") and holder.get("background") is True and holder.get("pid") == c.proc.pid)

    def wait_stalled(c) -> bool:
        end = time.monotonic() + 3.0
        while time.monotonic() < end:
            if c.request("get_holds").get("game_thread_stalled") is True:
                return True
            time.sleep(0.2)
        return False

    def stop_and_restart(label: str, since: str) -> tuple[str | None, object]:
        """CE unticks (UE5_Shutdown, Fern::Stop), then re-ticks 1.2 s after Stopped; a new client returned."""
        print(f"        {label}: CE untick: {_ce_schedule(ce_desc, False)}", flush=True)
        stopped = None
        end = time.monotonic() + 60
        while time.monotonic() < end and stopped is None:
            stopped = _pipe_log_line(r"PipeServer: Stopped", since)
            time.sleep(0.25)
        return stopped, None

    ui = connect()
    if not ck.check(cam(ui) in (0, 1), "the debug camera's state reads (a live CheatManager)", cam(ui)):
        ui.close()
        return ck.done(sessions)
    _camera_force_off(ui)
    tmo = int(ui.request("get_pointers").get("invoke_timeout_ms") or 5000)
    print(f"        game pid {game}; invoke timeout {tmo} ms; CE record {ce_desc!r}", flush=True)
    ui2 = None
    try:
        # K. Owed, then the stop.
        ai = session()
        srv = ai.proc.pid
        r, d, _ = call(ai, "write_debug_camera", {"enable": True})
        ck.check(not r.get("isError") and d.get("state") == 1 and server_holds(camera_hold(ui), ai),
                 "K: the AI's ON, the server's", d)
        with _FrozenGameThread(game_name) as frozen:
            ck.check(wait_stalled(ui), f"K: the game thread reads stalled ({frozen.suspend_line})")
            ck.check(ai.close() == 0, "K: the AI session ends while the game thread is stalled")
            end = time.monotonic() + tmo / 1000 + 3.0
            while time.monotonic() < end and camera_hold(ui).get("holder", {}).get("kind") == "client":
                time.sleep(0.25)
            time.sleep(0.5)
            h = camera_hold(ui)
            ck.check(h.get("active") and h.get("release_owed") is True, "K: the release is owed", h)
            since = time.strftime("%Y-%m-%d %H:%M:%S")
            stopped, _ = stop_and_restart("K", since)
            ck.check(stopped is not None, "K: CE's untick stopped the server", stopped)
            dropped = _pipe_log_line(rf"background client {srv}'s debug-camera release is dropped at stop", since)
            ck.check(dropped is not None, "K: the owed release is dropped at the stop", dropped)
        time.sleep(5.0)
        acted = _pipe_log_line(rf"background client {srv}'s debug-camera release .*(OFF sent again|is done|is "
                               r"abandoned)", since)
        ck.check(acted is None, "K: nothing acted on it after the stop", acted)
        time.sleep(1.2)
        print(f"        K: CE re-tick: {_ce_schedule(ce_desc, True)}", flush=True)
        ui2 = connect(retries=60)
        started = _pipe_log_line(r"PipeServer: Started on", since)
        ck.check(started is not None, "K: the re-tick started the server", started)
        h, state = camera_hold(ui2), cam(ui2)
        ck.check(is_free(h) and state == 1, "K: after the re-enable the camera is on, free, nothing owed",
                 (state, h))
        time.sleep(15.0)
        h, state = camera_hold(ui2), cam(ui2)
        late = _pipe_log_line(r"debug-camera release", started[1:20] if started else since)
        ck.check(is_free(h) and state == 1 and late is None, "K: 15 s later still on, free, and no release line",
                 (state, h, late))
        ck.check(_camera_force_off(ui2) == 0 and is_free(camera_hold(ui2)), "K: the UI stand-in turns it off")

        # K2. The stop lands while the release still waits.
        ui2.request("set_invoke_timeout", timeout_ms=20000, persist=False)
        ai = session()
        srv = ai.proc.pid
        r, d, _ = call(ai, "write_debug_camera", {"enable": True})
        ck.check(not r.get("isError") and d.get("state") == 1, "K2: the AI's ON", d)
        since = time.strftime("%Y-%m-%d %H:%M:%S")
        with _FrozenGameThread(game_name) as frozen:
            ck.check(wait_stalled(ui2), f"K2: the game thread reads stalled ({frozen.suspend_line})")
            ck.check(ai.close() == 0, "K2: the AI session ends while the game thread is stalled")
            time.sleep(1.5)
            h = camera_hold(ui2)
            ck.check(h.get("holder", {}).get("kind") == "client" and h["holder"].get("pid") == srv,
                     "K2: the release is still under way (the server still named)", h)
            stopped, _ = stop_and_restart("K2", since)
            ck.check(stopped is not None, "K2: CE's untick stopped the server", stopped)
            rel = _release_line(srv, "debug_camera", since)
            ck.check(rel is not None and "(code -1)" in rel, "K2: the release ended -1 (its OFF answered -7)", rel)
            ck.check(_pipe_log_line(rf"background client {srv} left -- its debug-camera OFF is queued", since) is None
                     and _pipe_log_line(rf"background client {srv}'s debug-camera release is dropped", since) is None,
                     "K2: never owed, nothing dropped")
        time.sleep(1.2)
        print(f"        K2: CE re-tick: {_ce_schedule(ce_desc, True)}", flush=True)
        ui2 = connect(retries=60)
        h, state = camera_hold(ui2), cam(ui2)
        ck.check(is_free(h) and state == 1, "K2: after the re-enable the camera is on, free, nothing owed", (state, h))
        time.sleep(15.0)
        h, state = camera_hold(ui2), cam(ui2)
        ck.check(is_free(h) and state == 1, "K2: 15 s later still on and free", (state, h))
        ck.check(_camera_force_off(ui2) == 0 and is_free(camera_hold(ui2)), "K2: the UI stand-in turns it off")
        ui2.request("set_invoke_timeout", timeout_ms=0, persist=False)
    finally:
        for c in sessions:
            try:
                c.close()
            except Exception:  # noqa: BLE001 -- already closed
                pass
        for c in (ui, ui2):
            try:
                if c is not None:
                    c.close()
            except Exception:  # noqa: BLE001 -- a stopped server closed it
                pass
    return ck.done(sessions)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("mode", choices=["probe", "call", "live", "writes", "raw", "dbgcam", "dbgcamhold", "dbgcamstop"])
    ap.add_argument("--ce-desc", default="Inject DLL + Start Pipe Server",
                    help="dbgcamstop: the CE record that started the DLL (its untick runs UE5_Shutdown)")
    ap.add_argument("--exe", required=True, help="path to UE5DumpUI.exe")
    ap.add_argument("--tool")
    ap.add_argument("--args", default="{}")
    ap.add_argument("--pid", type=int)
    ap.add_argument("--suspend-name", help="live: the game's process name for suspend.py (e.g. DumperTest); "
                                           "enables the cancel check, which suspends the game for about a second")
    ap.add_argument("--ssh", metavar="HOST", help="start the server through `ssh -T HOST`; --exe is the path there")
    ap.add_argument("--ssh-key", metavar="KEY", help="with --ssh: log in with this private key, no prompt (BatchMode)")
    ap.add_argument("--report", metavar="FILE", help="also write everything printed to FILE")
    ap.add_argument("--toggle-experimental", action="store_true",
                    help="writes: turn the UI's experimental switch off for one step (its file is put back)")
    a = ap.parse_args()
    global SSH_HOST, SSH_KEY
    SSH_HOST, SSH_KEY = a.ssh, a.ssh_key
    if a.report:
        sys.stdout = _Tee(sys.stdout, open(a.report, "w", encoding="utf-8"))
        print(f"mcp_client.py {a.mode} -- {time.strftime('%Y-%m-%d %H:%M:%S')}, elevated: {_elevated()}, "
              f"ssh: {a.ssh or '-'}")
    # Through ssh the path is the remote machine's: taken as written.
    exe = a.exe if a.ssh else str(pathlib.Path(a.exe).resolve())
    if a.mode == "probe":
        return run_probe(exe)
    if a.mode == "call":
        if not a.tool:
            ap.error("call needs --tool")
        args = json.loads(a.args)
        if a.pid:
            args["pid"] = a.pid
        return run_call(exe, a.tool, args)
    if a.mode == "writes":
        return run_writes(exe, a.pid, a.toggle_experimental)
    if a.mode in ("raw", "dbgcam", "dbgcamhold", "dbgcamstop"):
        if not a.suspend_name:
            ap.error(f"{a.mode} needs --suspend-name (the game's image stem, for suspend.py's thread verbs)")
        # These freeze the game's thread from this machine and compare a holder's pid with the server's process here.
        if a.ssh:
            ap.error(f"{a.mode} runs the server on this machine, beside the game: --ssh does not apply")
        # Resolved before any case runs: an ambiguous or absent name is refused here, not at the first freeze.
        try:
            tid = _game_thread(a.suspend_name)
        except McpError as e:
            ap.error(str(e))
        if tid is None:
            ap.error(f"--suspend-name {a.suspend_name!r}: no running process matches it")
        if a.mode == "dbgcamstop":
            return run_dbgcam_stop(exe, a.pid, a.suspend_name, a.ce_desc)
        run = {"raw": run_raw, "dbgcam": run_dbgcam, "dbgcamhold": run_dbgcam_hold}[a.mode]
        return run(exe, a.pid, a.suspend_name)
    return run_live(exe, a.pid, a.suspend_name)


if __name__ == "__main__":
    sys.exit(main())
