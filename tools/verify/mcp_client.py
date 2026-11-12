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
    py tools/verify/mcp_client.py holdsmore --exe dist/UE5DumpUI.exe [--pid N]
        Register items 24 and 20: the holds no other mode gives the AI, on a FRESH DumperTest Shipping (UE 5.4, DLL
        3705+), beside a foreground pipe client standing in for the UI, nothing held at start. (S) the stand-in spawns
        Holders, Derived and Decoys; (G) the AI holds gravity direction at (0, 0, -1), equal to the field, over a -0.0
        byte witness the stand-in writes first, so its reset and its exit release are seen writing the captured bytes
        back; the pawn must not drift; (M) god mode; (F) Keep Foreground on, off, on; (FF) the force field by
        derivation (Holder + Derived, never Decoy), reset to each instance's own value, the stand-in's job refused
        naming it, a second job kept; (C) the cursor: changed, no hold; (X) the session ends: each kept hold is released
        with a (code 0) line and its module's own OFF line, values and witness bytes back, the cursor left alone. Sends
        no gravity vector but (0, 0, -1). The UI's experimental switch is on for the run (its file's bytes put back);
        the clean-up undoes what the run changed and destroys the spawned holders.
    py tools/verify/mcp_client.py olddll --exe dist/UE5DumpUI.exe [--pid N]
        Register item 24, the old DLL: DumperTest Shipping freshly injected with out/stageA/copy3695/UE5Dumper.dll
        (build 3695: the per-process pipe, no if_free). Every write_* tool is refused with the build text (older than
        3705; 3706 for write_memory, write_invoke, write_debug_camera) before anything is sent, and the 3695 pipe log
        shows none of their commands since the start. Keep the UI closed: 3695 serves three connections.
    py tools/verify/mcp_client.py invokeparams --exe dist/UE5DumpUI.exe [--pid N]
        Register item 27, write_invoke's parameters by name, on DumperTest Shipping (DLL 3706+), no game-thread freeze.
        (K) K2_SetActorLocation moves the pawn 100 uu along its facing through a Vector struct and back, and returns
        false on a rootless holder; (O) an object parameter given a field's address is refused by the server's liveness
        check, nothing sent; (S) SendToConsole is refused by name. The EchoSoftSpec struct-field pair is recorded apart
        from the exit code.

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
import struct
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


def _log_line(category: str, pattern: str, since: str) -> str | None:
    """The newest line of any game's DLL `<category>-0.log` (pipe, walk, init, ...) that matches `pattern` and is
    stamped at or after `since` (local "YYYY-MM-DD HH:MM:SS", the DLL log's own stamp). Bounded by time because pids
    are reused and every game's log is read: a pid alone could take an earlier run's line. The glob is exact, so the
    UI's own ui-<category>-0.log beside it is never read."""
    sys.path.insert(0, str(pathlib.Path(__file__).parent))
    from pipe_client import LOGROOT   # noqa: E402 -- the rig beside this one

    rx = re.compile(pattern)
    lines = []
    for path in LOGROOT.glob(f"*/{category}-0.log"):
        try:
            text = path.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        lines += [line.strip() for line in text.splitlines()
                  if line.startswith("[") and line[1:20] >= since and rx.search(line)]
    return max(lines, key=lambda line: line[1:24]) if lines else None


def _pipe_log_line(pattern: str, since: str) -> str | None:
    """_log_line over the DLL's pipe log, where the server's commands and its holds' releases are written."""
    return _log_line("pipe", pattern, since)


def _now() -> str:
    """The DLL log's own stamp, to the second: every log read here is bounded by one."""
    return time.strftime("%Y-%m-%d %H:%M:%S")


def _fresh_stamp() -> str:
    """A stamp in a second of its own. The log is matched to the second, so a line an earlier step wrote in the same
    second would read as written after it: an absence check would then fail on the wrong line."""
    time.sleep(1.1)
    return _now()


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


# Constants.McpMinDllBuildForWrites / McpMinDllBuildForRawWrites: the DLL builds the server's write tools need.
MCP_MIN_WRITES, MCP_MIN_RAW_WRITES = 3705, 3706
FIXTURE_DERIVED, FIXTURE_DECOY = "DumperTestDerivedHolder", "DumperTestHolderDecoy"   # solide_l3's derivation pair
FIXTURE_FLAG = "bHolderFlag"
FLAG_HOLD = f"force_field:{FIXTURE_CLASS}::{FIXTURE_FLAG}"
# GravityDirection as the field holds it (an FVector of doubles; the field exists from UE 5.3). The witness is the same
# vector with signed zeros: the AI's set writes +0.0 over it, and a restore of the captured base writes -0.0 back, so
# the bytes tell a restore from none where the numbers cannot.
GRAVITY_DOWN = struct.pack("<ddd", 0.0, 0.0, -1.0).hex().upper()
GRAVITY_WITNESS = struct.pack("<ddd", -0.0, -0.0, -1.0).hex().upper()
# read_class's name_filter when only the class's own name and super are wanted: an engine class's fields stay out.
_NO_FIELDS = "no-field-is-named-this"


def _i32(n: int) -> str:
    return int(n).to_bytes(4, "little", signed=True).hex()


def _as_float(value) -> float | None:
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def _attempt(label: str, step) -> None:
    """One clean-up step. A failure is said and the next step still runs, so one dead read cannot leave the rest of the
    game changed."""
    try:
        step()
    except Exception as e:  # noqa: BLE001 -- the clean-up goes on
        print(f"  !! clean-up: {label} failed ({e}): check the game by hand", flush=True)


def run_holds_more(exe: str, pid: int | None) -> int:
    """Register items 24 and 20: the holds no other mode gives the AI (see the module docstring). Each section keeps
    what X, the session's end, must see released; `did` records what this run changed, so the clean-up undoes that and
    never a hold someone else had."""
    sys.path.insert(0, str(pathlib.Path(__file__).parent))
    from pipe_client import PipeClient   # noqa: E402 -- the rig beside this one
    from ad4_contested import find_live_actor   # noqa: E402 -- the fixture's spawner actor, as solide_l3 finds it

    ck = Checks()
    base = {"pid": pid} if pid else {}
    sessions: list[McpClient] = []
    switch = _ExperimentalSwitch()
    me = os.getpid()
    did: dict = {"kept": []}
    c: McpClient | None = None
    ui = None

    def call(tool, args=None):
        r = c.call(tool, {**base, **(args or {})})
        data, note = payload(r)
        return r, (data if isinstance(data, dict) else {}), note

    def error_text(data) -> str:
        return data.get("error", "") if isinstance(data, dict) else str(data)

    def holds() -> dict:
        return {h["hold"]: h for h in ui.request("get_holds").get("holds", [])}

    def server_holds(label: str, hold: str) -> None:
        """Both views of a hold the AI took: read_holds says it is the server's, and the stand-in's get_holds names the
        server's process."""
        r, d, _ = call("read_holds")
        h = next((x for x in d.get("holds", []) if x.get("hold") == hold), {})
        ck.check(bool(h.get("active") and h.get("holder", {}).get("you") and h["holder"].get("background")),
                 f"{label}: read_holds: {hold} is the server's ('you'), marked background", h)
        h = holds().get(hold, {})
        holder = h.get("holder", {})
        ck.check(bool(h.get("active") and holder.get("kind") == "client" and holder.get("background") is True
                      and not holder.get("you") and holder.get("pid") == c.proc.pid),
                 f"{label}: get_holds (stand-in): {hold} is held by the server's process, pid {c.proc.pid}", h)

    def freed(hold: str, now: dict) -> bool:
        # A force-field job is listed only while it exists: released, it is gone from get_holds.
        if hold.startswith("force_field:"):
            return hold not in now
        h = now.get(hold)
        return bool(h) and not h.get("active") and h.get("holder", {}).get("kind") == "none"

    def ai_value(addr: str) -> float | None:
        r, d, _ = call("read_instance", {"addr": addr, "name_filter": FIXTURE_FIELD})
        f = next((f for f in d.get("fields", []) if f.get("name") == FIXTURE_FIELD), None)
        return _as_float(f.get("value")) if f else None

    def ui_value(addr: str) -> float | None:
        w = ui.request("walk_instance", addr=addr, array_limit=1)
        f = next((f for f in w.get("fields", []) if f.get("name") == FIXTURE_FIELD), None)
        return _as_float(f.get("value")) if f else None

    def misses(values: dict, want: dict) -> dict:
        return {a: (v, want[a]) for a, v in values.items() if v is None or abs(v - want[a]) > 0.01}

    def grav() -> dict:
        return ui.request("get_movement_params").get("gravity_direction", {})

    def grav_bytes() -> str:
        return str(ui.request("read_mem", addr="0x%X" % did["gaddr"], size=24).get("bytes", "")).upper()

    def witness_back(label: str) -> None:
        now = grav_bytes()
        if did.get("witness"):
            ck.check(now == GRAVITY_WITNESS, f"{label} wrote the captured -0.0 witness back (+0.0: no restore)", now)
        else:
            ck.check(False, f"{label}: released, restore not decidable",
                     "the -0.0 witness did not stick, so a restore and none read alike")

    def drift(p0: dict) -> float:
        p = ui.request("teleport_get_pose")
        if p.get("code") != 0:
            return math.inf
        return max(abs(p[k] - p0[k]) for k in ("x", "y", "z"))

    def fixture_invoke(name: str, params_hex: str = "") -> dict:
        addr, fns = did["spawner"]
        f = fns.get(name)
        if f is None:
            return {"ok": False, "error": f"{name} is not on the spawner's class"}
        return ui.request("invoke_function", func_name=name, instance_addr=addr,
                          parms_size=f.get("parms_size", 0), params_hex=params_hex)

    def section_s() -> dict | None:
        """The discriminating set (solide_l3_derivation's), spawned by the stand-in, and each instance's own value."""
        try:
            act = find_live_actor(ui)
        except (SystemExit, Exception) as e:  # noqa: BLE001 -- it exits when the level never started
            ck.check(False, "S: a live DumperTestActor to spawn from", e)
            return None
        fns = {f["name"]: f for f in ui.request("walk_functions", addr=act["class_addr"]).get("functions", [])}
        did["spawner"] = (act["addr"], fns)
        for name, params, what in (("Spawn_Holders", _i32(5) + "00", "5 Holders"),
                                   ("Spawn_Holders", _i32(3) + "01", "3 Derived"),
                                   ("Spawn_Decoys", _i32(2), "2 Decoys")):
            r = fixture_invoke(name, params)
            ck.check(bool(r.get("ok")) and r.get("result", 0) == 0, f"S: the stand-in spawns {what} ({name})", r)
        sets: dict = {}
        for key, cls in (("base", FIXTURE_CLASS), ("derived", FIXTURE_DERIVED), ("decoy", FIXTURE_DECOY)):
            r, d, _ = call("read_find_instances", {"class_name": cls, "exact_match": True, "limit": 200})
            rows = [i["addr"] for i in d.get("instances", [])
                    if i.get("class") == cls and not str(i.get("name", "")).startswith("Default__")]
            # A capped list would make held == Holder + Derived a sample, not the arithmetic that decides derivation.
            whole = not (r.get("isError") or d.get("truncated") or d.get("deadline_hit"))
            ck.check(bool(rows) and whole, f"S: read_find_instances {cls}: {len(rows)} live, the whole set",
                     {k: d.get(k) for k in ("total", "truncated", "deadline_hit", "error")})
            sets[key] = rows
        if not all(sets.values()):
            return None
        baseline = {a: ai_value(a) for rows in sets.values() for a in rows}
        unread = [a for a, v in baseline.items() if v is None]
        distinct = {baseline[a] for a in sets["base"]}
        ck.check(not unread and len(distinct) >= 2,
                 f"S: every HolderValue reads; the Holders hold {len(distinct)} distinct values (so a shared restore "
                 "would show)", unread)
        wrong = {a: baseline[a] for a in sets["decoy"] if baseline[a] is None or abs(baseline[a] + 1) > 0.01}
        ck.check(not wrong, "S: every Decoy reads -1", wrong)
        if unread:
            return None
        sets["baseline"] = baseline
        return sets

    def section_g() -> None:
        gd = grav()
        if not ck.check(bool(gd.get("resolved")), "G: gravity direction resolves (UE 5.3+). Unresolved on Shipping "
                        "FAILS this run: rerun G on launch_dumpertest.py dev and say so", gd):
            return
        if not ck.check(not gd.get("active") and bool(gd.get("owner_addr")) and gd.get("field_offset") is not None,
                        "G: nobody holds it, and its field is located", gd):
            return
        did["gaddr"] = int(gd["owner_addr"], 16) + int(gd["field_offset"])
        orig = grav_bytes()
        vec = struct.unpack("<ddd", bytes.fromhex(orig)) if len(orig) == 48 else None
        # The gate on every gravity write this rig makes: it only ever sends (0, 0, -1), so it starts only on a field
        # that already holds that, and the pawn has nothing to fall along.
        if not ck.check(vec is not None and all(abs(a - b) <= 1e-6 for a, b in zip(vec, (0.0, 0.0, -1.0))),
                        "G: the field reads (0, 0, -1) within 1e-6", vec):
            return
        did["orig"] = orig
        p0 = ui.request("teleport_get_pose")
        if not ck.check(p0.get("code") == 0 and "x" in p0, "G: the pawn's pose", p0):
            return
        did["p0"] = p0
        w = ui.request("write_mem", addr="0x%X" % did["gaddr"], bytes=GRAVITY_WITNESS, expect=orig)
        time.sleep(1.0)
        did["witness"] = bool(w.get("ok")) and grav_bytes() == GRAVITY_WITNESS
        print("        G: the stand-in's -0.0 witness " + ("sticks" if did["witness"] else
              f"did NOT stick ({w.get('error') or 'rewritten'}): the restore half is not decidable"))

        did["grav"] = True
        r, d, note = call("write_gravity_direction", {"x": 0, "y": 0, "z": -1})
        if not ck.check(not r.get("isError") and d.get("state") == 1 and d.get("active") is True,
                        "G: the AI's write_gravity_direction (0, 0, -1), equal to the field, is held (state 1)",
                        (d, note)):
            return
        now = grav_bytes()
        ck.check(now == GRAVITY_DOWN, "G: it wrote +0.0 over the witness (the set shows in the bytes)", now)
        server_holds("G", "gravity_direction")
        worst = 0.0
        for _ in range(6):
            time.sleep(0.5)
            worst = max(worst, drift(p0))
            if worst > 10:
                break
        if not ck.check(worst <= 10, f"G: the pawn stays put for 3 s under the held direction (drift {worst:.1f} uu, "
                                     "10 at most)"):
            ui.request("reset_gravity_direction")   # at once: DumperTest never respawns its pawn
            return
        r, d, note = call("write_gravity_direction", {"reset": True})
        ck.check(not r.get("isError") and d.get("code") == 0, "G: the AI's write_gravity_direction reset (code 0)",
                 (d, note))
        gd, now_h = grav(), holds()
        ck.check(not gd.get("active") and freed("gravity_direction", now_h),
                 "G: after the reset it is not held: inactive, holder none", (gd, now_h.get("gravity_direction")))
        witness_back("G: the AI's reset")
        r, d, note = call("write_gravity_direction", {"x": 0, "y": 0, "z": -1})
        ok = not r.get("isError") and d.get("state") == 1 and d.get("active") is True
        ck.check(ok, "G: the AI's (0, 0, -1) again, kept for the exit", (d, note))
        if ok:
            did["kept"].append("gravity_direction")

    def section_m() -> None:
        ps = ui.request("get_protect_state")
        if not ck.check(ps.get("want") == 0, "M: god mode is off at start (want 0)", ps):
            return
        did["god"] = True
        r, d, note = call("write_god_mode", {"enable": True})
        ok = ck.check(not r.get("isError") and d.get("state") == 1, "M: the AI's write_god_mode ON (state 1)",
                      (d, note))
        server_holds("M", "god_mode")
        ps = ui.request("get_protect_state")
        ck.check(ps.get("want") == 1 and ps.get("godmode") == 1, "M: get_protect_state: want 1, godmode 1", ps)
        if ok:
            did["kept"].append("god_mode")

    def section_f() -> None:
        if not ck.check(ui.request("get_foreground_lock").get("state") == 0, "F: Keep Foreground is off at start"):
            return
        did["fg"] = True
        since = _now()
        r, d, note = call("write_foreground_lock", {"enable": True})
        ck.check(not r.get("isError") and d.get("state") == 1, "F: the AI's write_foreground_lock ON (state 1)",
                 (d, note))
        ck.check(ui.request("get_foreground_lock").get("state") == 1, "F: the stand-in reads it on")
        server_holds("F", "foreground_lock")
        ck.check(_log_line("init", r"Foreground lock ENABLED", since) is not None,
                 "F: init log: Foreground lock ENABLED")
        r, d, note = call("write_foreground_lock", {"enable": False})
        now_h = holds()
        ck.check(not r.get("isError") and d.get("state") == 0 and freed("foreground_lock", now_h),
                 "F: the AI's OFF of its own lock (state 0): holder none", (d, now_h.get("foreground_lock")))
        r, d, note = call("write_foreground_lock", {"enable": True})
        ok = not r.get("isError") and d.get("state") == 1
        ck.check(ok, "F: the AI's ON again, kept for the exit", (d, note))
        if ok:
            did["kept"].append("foreground_lock")

    def section_ff(sets: dict) -> None:
        held_set, decoys, baseline = sets["base"] + sets["derived"], sets["decoy"], sets["baseline"]
        n = len(held_set)
        did["ff"] = True
        job_args = {"class_name": FIXTURE_CLASS, "field_name": FIXTURE_FIELD, "kind": "numeric"}

        def job_listed(field: str) -> dict:
            r, d, _ = call("read_forced_fields")
            return next((f for f in d.get("fields", [])
                         if f.get("class_name") == FIXTURE_CLASS and f.get("field_name") == field), {})

        r, d, note = call("write_force_field", {**job_args, "value": 777})
        ck.check(not r.get("isError") and d.get("held") == n and d.get("truncated") is False,
                 f"FF: write_force_field 777 holds {n} = Holder + Derived (Holder + Decoy would be "
                 f"{len(sets['base']) + len(decoys)}), not truncated", (d, note))
        values = {a: ai_value(a) for a in held_set + decoys}
        miss = misses({a: values[a] for a in held_set}, dict.fromkeys(held_set, 777.0))
        ck.check(not miss, "FF: every Holder and Derived reads 777", miss)
        miss = misses({a: values[a] for a in decoys}, dict.fromkeys(decoys, -1.0))
        ck.check(not miss, "FF: every Decoy still reads -1 (derivation, not a name match)", miss)
        job = job_listed(FIXTURE_FIELD)
        ck.check(job.get("kind") == "numeric" and abs((_as_float(job.get("value")) or 0.0) - 777) < 0.01
                 and job.get("held") == n, f"FF: read_forced_fields lists the job: numeric, 777, held {n}", job)
        server_holds("FF", FORCE_FIELD_HOLD)

        r, d, note = call("write_force_field_reset", {"class_name": FIXTURE_CLASS, "field_name": FIXTURE_FIELD})
        ck.check(not r.get("isError") and d.get("code") == 0, "FF: the AI's write_force_field_reset (code 0)",
                 (d, note))
        ck.check(not job_listed(FIXTURE_FIELD) and FORCE_FIELD_HOLD not in holds(),
                 "FF: the job is gone from read_forced_fields and from get_holds")
        back = misses({a: ai_value(a) for a in held_set + decoys}, baseline)
        ck.check(not back, "FF: every instance is back at its OWN baseline value", back)

        # A job the stand-in holds: the AI's reset is refused, naming it, and the job stays.
        since = _now()
        u = ui.request("force_field", class_name=FIXTURE_CLASS, field_name=FIXTURE_FLAG, kind="bool", on=True)
        ck.check(bool(u.get("ok")) and u.get("held", 0) > 0, f"FF: the stand-in's own job on {FIXTURE_FLAG} (held > 0)",
                 u)
        r, d, _ = call("write_force_field_reset", {"class_name": FIXTURE_CLASS, "field_name": FIXTURE_FLAG})
        text = error_text(d)
        ck.check(bool(r.get("isError")) and f"{FLAG_HOLD} is held by the UE5DumpUI window or another program" in text
                 and f"(pid {me})" in text, "FF: the AI's reset of the stand-in's job is refused, naming its pid", d)
        h = holds().get(FLAG_HOLD, {})
        ck.check(bool(job_listed(FIXTURE_FLAG)) and h.get("active") and h.get("holder", {}).get("you"),
                 "FF: and the stand-in's job stays, its own", h)
        ck.check(_log_line("pipe", rf"reset_field refused \(if_free\): {re.escape(FLAG_HOLD)} is held by another",
                           since) is not None, "FF: pipe log: reset_field refused (if_free) for the stand-in's job")
        u = ui.request("reset_field", class_name=FIXTURE_CLASS, field_name=FIXTURE_FLAG)
        ck.check(bool(u.get("ok")) and u.get("code") == 0, "FF: the stand-in ends its own job", u)

        r, d, note = call("write_force_field", {**job_args, "value": 555})
        ok = not r.get("isError") and d.get("held") == n
        miss = misses({a: ai_value(a) for a in held_set}, dict.fromkeys(held_set, 555.0))
        ck.check(ok and not miss, "FF: a second job, 555, holds them again (kept for the exit)", (d, note, miss))
        if ok:
            did["kept"].append(FORCE_FIELD_HOLD)

    def section_c() -> None:
        g = ui.request("get_mouse_cursor")
        if not ck.check(g.get("code") == 0 and isinstance(g.get("state"), bool), "C: the cursor's state reads", g):
            return
        did["s0"] = g["state"]
        print(f"        C: the cursor is {'shown' if g['state'] else 'hidden'} at start, and is put back so. Do not "
              "click into the game while it is hidden (its input mode captures the mouse)")
        ok = False
        for i, show in enumerate((True, False, True)):
            r, d, note = call("write_mouse_cursor", {"show": show})
            seen = ui.request("get_mouse_cursor").get("state")
            word = "true" if show else "false"
            ok = ck.check(not r.get("isError") and d.get("state") == show and seen == show,
                          f"C: write_mouse_cursor show={word}: the payload and the stand-in read {word}",
                          (d, note, seen))
            if i == 0:
                named = [k for k in holds() if "cursor" in k.lower() or "mouse" in k.lower()]
                ck.check(not named, "C: no hold is recorded for the cursor (nothing holds it)", named)
        did["cursor_shown"] = ok

    def section_x(sets: dict | None) -> None:
        server = c.proc.pid
        since = _fresh_stamp()
        code = c.close()
        did["closed"] = True
        ck.check(code == 0, "X: the AI session ends (the server exits 0)", code)
        kept = did["kept"]
        now: dict = {}
        end = time.monotonic() + 5.0   # the release runs after the disconnect, not with it
        while True:
            now = holds()
            if all(freed(h, now) for h in kept) or time.monotonic() > end:
                break
            time.sleep(0.2)
        for hold in kept:
            how = "the job is gone" if hold.startswith("force_field:") else "inactive, holder none"
            ck.check(freed(hold, now), f"X: the server's {hold} is released: {how}", now.get(hold))
            line = _release_line(server, hold, since)
            ck.check(line is not None and "(code 0)" in line, f"X: pipe log: its release of {hold} ends (code 0)", line)
        if "god_mode" in kept:
            ck.check(_log_line("walk", r"GodMode: set OFF -> rc=0 \(want=0\)", since) is not None,
                     "X: walk log: GodMode: set OFF -> rc=0 (want=0), after the exit")
            ps = ui.request("get_protect_state")
            ck.check(ps.get("want") == 0 and ps.get("godmode") == 0, "X: get_protect_state: want 0, godmode 0", ps)
        if "gravity_direction" in kept:
            ck.check(_log_line("walk", r"Movement: gravity dir reset \(any active left=", since) is not None,
                     "X: walk log: Movement: gravity dir reset, after the exit")
            ck.check(not grav().get("active"), "X: gravity direction reads inactive")
            witness_back("X: the exit's release")
            moved = drift(did["p0"])
            ck.check(moved <= 10, f"X: the pawn did not move during the whole hold (drift {moved:.1f} uu)")
        if "foreground_lock" in kept:
            ck.check(_log_line("init", r"Foreground lock DISABLED", since) is not None,
                     "X: init log: Foreground lock DISABLED, after the exit")
            ck.check(ui.request("get_foreground_lock").get("state") == 0, "X: Keep Foreground reads off")
        if FORCE_FIELD_HOLD in kept and sets:
            values = {a: ui_value(a) for key in ("base", "derived", "decoy") for a in sets[key]}
            back = misses(values, sets["baseline"])
            ck.check(not back, "X: every instance is back at its own baseline value", back)
        if did.get("cursor_shown"):
            g = ui.request("get_mouse_cursor")
            ck.check(g.get("state") is True, "X: the cursor is still shown: the exit left it alone (not a hold)", g)
            line = _pipe_log_line(rf"background client {server} left -- its hold \S*(cursor|mouse)", since)
            ck.check(line is None, "X: no release line names a cursor", line)

    def body() -> None:
        nonlocal c, ui
        switch.set(True)   # Keep Foreground and the force field are experimental: the server refuses them otherwise
        c = McpClient(exe)
        c.initialize()
        sessions.append(c)
        tools = {t["name"]: t for t in c.request("tools/list").get("result", {}).get("tools", [])}
        need = ("write_gravity_direction", "write_god_mode", "write_foreground_lock", "write_force_field",
                "write_force_field_reset", "write_mouse_cursor")
        unmarked = [n for n in need if tools.get(n, {}).get("_meta", {}).get("anthropic/requiresUserInteraction")
                    is not True or tools.get(n, {}).get("annotations", {}).get("destructiveHint") is not True]
        ck.check(not unmarked, "tools/list: the six write tools, each asking every time, destructive", unmarked)
        r, st, _ = call("read_status")
        build = int(st.get("build_number") or 0)
        print(f"        UE {st.get('ue_version')}, {st.get('object_count')} objects, DLL build {build}, server build "
              f"{st.get('mcp_build')}")
        if not ck.check(bool(st.get("scanned")) and build >= MCP_MIN_WRITES,
                        f"the game is scanned, DLL build {build} (the write tools need {MCP_MIN_WRITES}+)", st):
            return
        game = pid or st.get("pid")
        client = PipeClient(pid=game, timeout=120)
        client.connect()
        ui = client
        print(f"        game pid {game}; the server is pid {c.proc.pid}; the UI stand-in (foreground) is pid {me}")
        held = sorted(k for k, h in holds().items() if h.get("active"))
        if not ck.check(not held, "nothing is held when the run starts (the UI, CE, a peer or a half-done run)", held):
            return
        did["clean_start"] = True
        sets = section_s()
        section_g()
        section_m()
        section_f()
        if sets:
            section_ff(sets)
        section_c()
        section_x(sets)

    def cleanup() -> None:
        if c is not None and not did.get("closed"):
            _attempt("closing the AI session", c.close)
        if ui is not None:
            def cursor_back():
                ui.request("set_mouse_cursor", show=did["s0"])

            def gravity_off():
                if grav().get("active"):
                    ui.request("reset_gravity_direction")

            def gravity_bytes_back():
                if did["orig"] != GRAVITY_WITNESS and grav_bytes() == GRAVITY_WITNESS:
                    ui.request("write_mem", addr="0x%X" % did["gaddr"], bytes=did["orig"], expect=GRAVITY_WITNESS)

            def god_off():
                if ui.request("get_protect_state").get("want"):
                    ui.request("set_god_mode", enable=False)

            def foreground_off():
                if ui.request("get_foreground_lock").get("state") == 1:
                    ui.request("set_foreground_lock", enable=False)

            def destroy_holders():
                r = fixture_invoke("Spawn_DestroyHolders")
                if not r.get("ok") or r.get("result", 0) != 0:
                    raise McpError(f"Spawn_DestroyHolders answered {r}; run it from pipe_client before anything else")

            if "s0" in did:
                _attempt("the cursor back to its starting state", cursor_back)
            if did.get("grav"):
                _attempt("gravity direction reset", gravity_off)
            if "orig" in did:
                _attempt("GravityDirection's original bytes", gravity_bytes_back)
            if did.get("god"):
                _attempt("god mode off", god_off)
            if did.get("fg"):
                _attempt("Keep Foreground off", foreground_off)
            if did.get("ff"):
                for field in (FIXTURE_FIELD, FIXTURE_FLAG):
                    _attempt(f"reset_field {field}",
                             lambda f=field: ui.request("reset_field", class_name=FIXTURE_CLASS, field_name=f))
            if "spawner" in did:
                _attempt("Spawn_DestroyHolders", destroy_holders)
        _attempt("the experimental switch's file", switch.restore)
        if ui is not None:
            if did.get("clean_start"):
                try:
                    left = sorted(k for k, h in holds().items() if h.get("active"))
                    ck.check(not left, "clean-up: nothing is left held", left)
                except Exception as e:  # noqa: BLE001 -- said, not raised over the summary
                    ck.check(False, "clean-up: nothing is left held", f"get_holds failed: {e}")
            ui.close()

    try:
        body()
    finally:
        cleanup()
    return ck.done(sessions)


# The pipe-log line each write's command leaves on the 3695 DLL, which logs these on receipt. The first six are the
# acceptance's own; the rest witness the other commands that DLL logs. Its write_mem logs nothing, so write_memory has
# no witness here.
OLD_DLL_COMMAND_LINES = (
    r"set_god_mode:", r"set_foreground_lock:", r"set_gravity_direction:", r"force_field: class=", r"set_mouse_cursor:",
    r"invoke_function: ",
    r"reset_gravity_direction", r"reset_movement:", r"reset_time_dilation:", r"set_debug_camera:", r"fly_set:",
    r"seethrough_set:", r"reset_field:", r"teleport_recall_marker:",
)
OLD_DLL_RAW_TOOLS = ("write_memory", "write_invoke", "write_debug_camera")   # need MCP_MIN_RAW_WRITES, not 3705


def run_old_dll(exe: str, pid: int | None) -> int:
    """Register item 24, the refusal of an old DLL (see the module docstring). The arguments are no-ops for a DLL that
    ignores if_free and keep_undo -- OFF and reset forms, the current pose, the very bytes there, 1 + 1 -- so a refusal
    that regressed would change nothing worth undoing; the forced field and the cursor, which would, are put back."""
    sys.path.insert(0, str(pathlib.Path(__file__).parent))
    from pipe_client import PipeClient   # noqa: E402 -- the rig beside this one

    ck = Checks()
    base = {"pid": pid} if pid else {}
    sessions: list[McpClient] = []
    switch = _ExperimentalSwitch()
    did: dict = {}
    c: McpClient | None = None
    ui = None

    def call(tool, args=None):
        r = c.call(tool, {**base, **(args or {})})
        data, note = payload(r)
        return r, (data if isinstance(data, dict) else {}), note

    def error_text(data) -> str:
        return data.get("error", "") if isinstance(data, dict) else str(data)

    def body() -> None:
        nonlocal c, ui
        # The experimental tools check the switch before the DLL's build: off, they would refuse for that instead.
        switch.set(True)
        c = McpClient(exe)
        c.initialize()
        sessions.append(c)
        listed = sorted(t["name"] for t in c.request("tools/list").get("result", {}).get("tools", [])
                        if t["name"].startswith("write_"))
        r, st, _ = call("read_status")
        build = int(st.get("build_number") or 0)
        print(f"        UE {st.get('ue_version')}, DLL build {build}, server build {st.get('mcp_build')}")
        if not ck.check(0 < build < MCP_MIN_WRITES, f"the game's DLL is build {build}, older than {MCP_MIN_WRITES} "
                        "(else: inject an old DLL, out/stageA/copy3695/UE5Dumper.dll, into a fresh game)", st):
            return
        if not ck.check(bool(st.get("scanned")), "the game is scanned", st):
            return
        game = pid or st.get("pid")
        try:
            client = PipeClient(pid=game, timeout=60)
            client.connect()
            ui = client
            g = ui.request("get_mouse_cursor")
            if g.get("code") == 0 and isinstance(g.get("state"), bool):
                did["s0"] = g["state"]
        except Exception as e:  # noqa: BLE001 -- the stand-in is informational here
            print(f"  skip  the stand-in beside the server (informational): {e}")

        r, p, _ = call("read_pose")
        ck.check(p.get("code") == 0 and "x" in p, "the pawn's pose (write_teleport's target: where it is)", p)
        r, kml, _ = call("read_find_object", {"path": "Default__KismetMathLibrary"})
        ck.check(bool(kml.get("addr")), "KismetMathLibrary's class default (write_invoke's object)", kml)
        faddr, fbytes = "0x0", "00000000"
        r, live, _ = call("read_find_live_instance", {"class_name": FIXTURE_CLASS})
        field = None
        if live.get("addr") and live.get("class_addr"):
            r, klass, _ = call("read_class", {"addr": live["class_addr"], "name_filter": FIXTURE_FIELD})
            field = next((f for f in klass.get("class", {}).get("fields", []) if f.get("name") == FIXTURE_FIELD), None)
        if ck.check(field is not None, f"{FIXTURE_CLASS}.{FIXTURE_FIELD} and its bytes (write_memory's)", live):
            faddr = "0x%X" % (int(live["addr"], 16) + field["offset"])
            r, mem, _ = call("read_memory", {"addr": faddr, "size": 4})
            fbytes = str(mem.get("bytes", "")).upper() or fbytes

        args = {
            "write_god_mode": {"enable": False},
            "write_movement": {"knob": "walk_speed", "reset": True},
            "write_gravity_direction": {"reset": True},
            "write_time_dilation": {"target": "global", "reset": True},
            "write_debug_camera": {"enable": False},
            "write_mouse_cursor": {"show": False},
            "write_fly": {"enable": False},
            "write_foreground_lock": {"enable": False},
            "write_see_through": {"enable": False},
            # Before its reset: should both ever reach the old DLL, the reset ends the job.
            "write_force_field": {"class_name": FIXTURE_CLASS, "field_name": FIXTURE_FIELD, "kind": "numeric",
                                  "value": 777},
            "write_force_field_reset": {"class_name": FIXTURE_CLASS, "field_name": FIXTURE_FIELD},
            "write_teleport": {k: p.get(k, 0.0) for k in ("x", "y", "z", "yaw")},
            "write_memory": {"addr": faddr, "bytes": fbytes, "expect": fbytes},
            "write_invoke": {"instance_addr": kml.get("addr", "0x0"), "function": "Add_IntInt",
                             "params": {"A": 1, "B": 1}},
        }
        since = _fresh_stamp()
        for name, a in args.items():
            if name not in listed:
                ck.check(False, f"{name}: listed by tools/list", listed)
                continue
            r, d, _ = call(name, a)
            need = MCP_MIN_RAW_WRITES if name in OLD_DLL_RAW_TOOLS else MCP_MIN_WRITES
            text = error_text(d)
            if not r.get("isError") and name in ("write_force_field", "write_mouse_cursor"):
                did[name] = True
            ck.check(bool(r.get("isError")) and f"The game's DLL is build {build}, older than {need}" in text
                     and "nothing is changed through it" in text,
                     f"{name}: refused with the build text (build {build}, older than {need})", d)
        for name in sorted(set(listed) - set(args)):
            ck.check(False, f"{name}: a write tool this rig has no no-op arguments for", "add them to the table")
        for pattern in OLD_DLL_COMMAND_LINES:
            line = _log_line("pipe", pattern, since)
            ck.check(line is None, f"the old DLL's pipe log has no '{pattern.strip()}' line since the start", line)
        if ui is not None:
            for cmd in ("get_protect_state", "get_foreground_lock", "get_forced_fields"):
                reply = {k: v for k, v in ui.request(cmd).items() if k not in ("id", "ok", "game_thread_stalled")}
                print(f"        stand-in (informational): {cmd} -> {reply}")
        code = c.close()
        did["closed"] = True
        ck.check(code == 0, "the server exits 0 when stdin closes", code)

    def cleanup() -> None:
        if c is not None and not did.get("closed"):
            _attempt("closing the AI session", c.close)
        if ui is not None:
            if did.get("write_force_field"):
                _attempt("the field forced through the old DLL",
                         lambda: ui.request("reset_field", class_name=FIXTURE_CLASS, field_name=FIXTURE_FIELD))
            if did.get("write_mouse_cursor") and "s0" in did:
                _attempt("the cursor back", lambda: ui.request("set_mouse_cursor", show=did["s0"]))
            ui.close()
        _attempt("the experimental switch's file", switch.restore)

    try:
        body()
    finally:
        cleanup()
    return ck.done(sessions)


def run_invoke_params(exe: str, pid: int | None) -> int:
    """Register item 27, write_invoke's parameters by name (see the module docstring). The stand-in only reads:
    get_holds' outstanding invokes, and the pose at the end."""
    sys.path.insert(0, str(pathlib.Path(__file__).parent))
    from pipe_client import PipeClient   # noqa: E402 -- the rig beside this one

    ck = Checks()
    extra = Checks()   # the EchoSoftSpec pair: beyond the acceptance, so kept out of the exit code
    base = {"pid": pid} if pid else {}
    sessions: list[McpClient] = []
    did: dict = {}
    c: McpClient | None = None
    ui = None

    def call(tool, args=None):
        r = c.call(tool, {**base, **(args or {})})
        data, note = payload(r)
        return r, (data if isinstance(data, dict) else {}), note

    def error_text(data) -> str:
        return data.get("error", "") if isinstance(data, dict) else str(data)

    def outstanding() -> int:
        return sum(e.get("outstanding", 0) for e in ui.request("get_holds").get("invokes", []))

    def pose() -> dict:
        return call("read_pose")[1]

    def invoke(addr: str, function: str, params: dict | None = None):
        args = {"instance_addr": addr, "function": function}
        if params is not None:
            args["params"] = params
        return call("write_invoke", args)

    def vector(p: dict) -> dict:
        return {"X": p["x"], "Y": p["y"], "Z": p["z"]}

    def params_of(f: dict | None) -> dict:
        return {p.get("name"): p for p in (f or {}).get("params", [])}

    def first_declaring(class_addr: str | None, name: str) -> tuple[str, dict] | None:
        """The first class up the chain from `class_addr` that declares `name` itself, with that function: the one the
        server's own lookup (the super chain, most derived first) resolves, so an override in a subclass is the one
        checked, never the base's."""
        addr = class_addr
        for _ in range(64):
            if not addr or int(addr, 16) == 0:
                return None
            r, fs, _ = call("read_functions", {"addr": addr, "name_filter": name})
            f = next((f for f in fs.get("functions", []) if f.get("name") == name), None)
            r, k, _ = call("read_class", {"addr": addr, "name_filter": _NO_FIELDS})
            cls = k.get("class", {})
            if f is not None:
                return cls.get("name", "?"), f
            addr = cls.get("super_addr")
        return None

    def contained(label: str, owner: str, offset: int) -> int | None:
        """The address `offset` into `owner`, when find_by_address calls it inside that object ('contains'): the
        server's liveness check then refuses it as an object."""
        fa = int(owner, 16) + offset
        r, fb, _ = call("read_find_by_address", {"addr": "0x%X" % fa})
        print(f"        O: {label} at 0x{fa:X}: find_by_address answers {fb.get('match_kind')} "
              f"(object {fb.get('addr')} + {fb.get('offset_from_base')})")
        ok = (fb.get("found") is True and fb.get("match_kind") == "contains"
              and int(str(fb.get("addr") or "0x0"), 16) == int(owner, 16) and fb.get("offset_from_base") == offset)
        return fa if ok else None

    def soft_spec_pair(field: int, pawn: str) -> None:
        r, act, _ = call("read_find_live_instance", {"class_name": "DumperTestActor"})
        if not extra.check(act.get("pick") == "live" and bool(act.get("addr")), "O extra: a live DumperTestActor", act):
            return
        r, d, _ = invoke(act["addr"], "Vnd584_EchoSoftSpec", {"Spec": {"Icon": "0x%X" % field}})
        want = f"'Spec.Icon' must be a live object's address (or 0); 0x{field:X} is not one."
        extra.check(bool(r.get("isError")) and error_text(d) == want,
                    "O extra: Vnd584_EchoSoftSpec with Spec.Icon = that field is refused, naming the struct field",
                    d)
        r, d, note = invoke(act["addr"], "Vnd584_EchoSoftSpec", {"Spec": {"Icon": pawn}})
        try:
            got = int(str(d.get("return")), 16)
        except ValueError:
            got = None
        extra.check(not r.get("isError") and d.get("result") == 0 and got == int(pawn, 16),
                    "O extra control: Spec.Icon = the pawn returns the pawn's address", (d, note))

    def body() -> None:
        nonlocal c, ui
        c = McpClient(exe)
        c.initialize()
        sessions.append(c)
        r, st, _ = call("read_status")
        build = int(st.get("build_number") or 0)
        print(f"        UE {st.get('ue_version')}, DLL build {build}, server build {st.get('mcp_build')}")
        if not ck.check(bool(st.get("scanned")) and build >= MCP_MIN_RAW_WRITES,
                        f"the game is scanned, DLL build {build} (write_invoke needs {MCP_MIN_RAW_WRITES}+)", st):
            return
        game = pid or st.get("pid")
        client = PipeClient(pid=game, timeout=120)
        client.connect()
        ui = client
        t0 = _now()
        # A call left queued (the UI's, CE's, a half-done run's) makes every write_invoke refuse: nothing here decides.
        if not ck.check(outstanding() == 0, "no invoke is outstanding when the run starts"):
            return
        p0 = pose()
        if not ck.check(p0.get("code") == 0 and "x" in p0 and int(str(p0.get("pawn_addr") or "0x0"), 16) != 0,
                        "read_pose: the pawn and its pose", p0):
            return
        did["p0"] = p0
        pawn = p0["pawn_addr"]

        # Signature guards, before any call: what each function takes on this build.
        r, obj, _ = call("read_object", {"addr": pawn})
        hit = first_declaring(obj.get("class_addr"), "K2_SetActorLocation")
        ps = params_of(hit[1] if hit else None)
        nl = ps.get("NewLocation", {})
        k_ok = ck.check(hit is not None and hit[0] == "Actor" and nl.get("type") == "StructProperty"
                        and nl.get("struct_type") == "Vector" and nl.get("size") == 24 and "bSweep" in ps
                        and ps.get("SweepHitResult", {}).get("out") is True and "bTeleport" in ps
                        and ps.get("ReturnValue", {}).get("ret") is True,
                        "guard: the pawn resolves Actor's K2_SetActorLocation: NewLocation (Vector, 24 bytes), bSweep, "
                        "SweepHitResult (out), bTeleport, ReturnValue", hit)
        r, kml, _ = call("read_find_object", {"path": "Default__KismetMathLibrary"})
        cdo = kml.get("addr")
        eq = None
        if cdo:
            r, ko, _ = call("read_object", {"addr": cdo})
            r, fs, _ = call("read_functions", {"addr": ko.get("class_addr") or "0x0",
                                               "name_filter": "EqualEqual_ObjectObject"})
            eq = next((f for f in fs.get("functions", []) if f.get("name") == "EqualEqual_ObjectObject"), None)
        eps = params_of(eq)
        o_ok = ck.check(eq is not None and eps.get("A", {}).get("type") == "ObjectProperty"
                        and eps.get("B", {}).get("type") == "ObjectProperty",
                        "guard: KismetMathLibrary.EqualEqual_ObjectObject takes A and B as objects", eq)
        r, pc, _ = call("read_find_live_instance", {"class_name": "PlayerController"})
        stc = first_declaring(pc.get("class_addr"), "SendToConsole") if pc.get("pick") == "live" else None
        sps = params_of(stc[1] if stc else None)
        ck.check(stc is not None and not (int(stc[1].get("flags") or 0) & 0x40)
                 and sps.get("Command", {}).get("type") == "StrProperty",
                 "guard: the live PlayerController's SendToConsole has no FUNC_Net (0x40) and takes a string: refused "
                 "by name, not by flag", stc)

        # K. A struct parameter by name moves an actor; a rootless one answers false.
        if k_ok:
            target = _ahead(p0, 100)
            did["moved"] = True
            r, d, note = invoke(pawn, "K2_SetActorLocation",
                                {"NewLocation": vector(target), "bSweep": False, "bTeleport": True})
            ck.check(not r.get("isError") and d.get("result") == 0 and d.get("return") == "true"
                     and "SweepHitResult" in (d.get("out") or {}),
                     "K: write_invoke K2_SetActorLocation, 100 uu ahead, returns true, SweepHitResult decoded",
                     (d, note))
            time.sleep(0.5)
            p1 = pose()
            ck.check(p1.get("z", target["z"]) >= target["z"] - 200,
                     "K: the pawn did not fall away (z within 200 of the target; else it is moved back at once)", p1)
            moved = math.hypot(p1.get("x", p0["x"]) - p0["x"], p1.get("y", p0["y"]) - p0["y"])
            ck.check(_near(p1, target) and moved >= 80,
                     f"K: the pawn is at the target (within 50 uu), {moved:.0f} uu from where it was (80 at least)", p1)
            line = _log_line("pipe", r"invoke_function: \S*::K2_SetActorLocation inst=", t0)
            m = re.search(r"inst=(0x[0-9A-Fa-f]+)", line or "")
            ck.check(m is not None and int(m.group(1), 16) == int(pawn, 16),
                     "K: pipe log: its invoke line, on the pawn", line)
            r, d, note = invoke(pawn, "K2_SetActorLocation", {"NewLocation": vector(p0), "bSweep": False,
                                                              "bTeleport": True})
            time.sleep(0.5)
            pb = pose()
            if ck.check(not r.get("isError") and d.get("return") == "true" and _near(pb, p0),
                        "K back: K2_SetActorLocation to where it was; the pawn is back within 50 uu", (d, pb)):
                did["moved"] = False
            r, h, _ = call("read_find_live_instance", {"class_name": FIXTURE_CLASS})
            if ck.check(h.get("pick") == "live" and bool(h.get("addr")),
                        f"K control: a live {FIXTURE_CLASS} (an actor with no root component)", h):
                did["holder"] = h
                r, d, note = invoke(h["addr"], "K2_SetActorLocation",
                                    {"NewLocation": vector(p0), "bSweep": False, "bTeleport": True})
                ck.check(not r.get("isError") and d.get("result") == 0 and d.get("return") == "false",
                         "K control: on it the call runs and returns false (no root to move)", (d, note))

        # O. An object parameter given a field's address: the server's liveness check refuses it, nothing is sent.
        if o_ok:
            r, d, note = invoke(cdo, "EqualEqual_ObjectObject", {"A": pawn, "B": pawn})
            ck.check(not r.get("isError") and d.get("return") == "true",
                     "O control: EqualEqual_ObjectObject(pawn, pawn) returns true", (d, note))
            r, d, note = invoke(cdo, "EqualEqual_ObjectObject", {"A": pawn, "B": "0"})
            ck.check(not r.get("isError") and d.get("return") == "false",
                     "O control: EqualEqual_ObjectObject(pawn, 0) returns false", (d, note))
            field = None
            if p0.get("loc_owner_addr") and p0.get("loc_field_offset") is not None:
                field = contained("the pawn root's RelativeLocation", p0["loc_owner_addr"], int(p0["loc_field_offset"]))
            if field is None:
                # The plan's fallback when an object starts at that field: HolderValue inside a holder.
                h = did.get("holder") or call("read_find_live_instance", {"class_name": FIXTURE_CLASS})[1]
                r, k, _ = call("read_class", {"addr": h.get("class_addr") or "0x0", "name_filter": FIXTURE_FIELD})
                f = next((f for f in k.get("class", {}).get("fields", []) if f.get("name") == FIXTURE_FIELD), None)
                if h.get("addr") and f is not None:
                    field = contained(f"{FIXTURE_CLASS}.{FIXTURE_FIELD}", h["addr"], int(f["offset"]))
            if ck.check(field is not None, "O: find_by_address calls a field's address 'contains', not 'exact'"):
                since = _fresh_stamp()
                r, d, _ = invoke(cdo, "EqualEqual_ObjectObject", {"A": "0x%X" % field, "B": pawn})
                want = f"'A' must be a live object's address (or 0); 0x{field:X} is not one."
                ck.check(bool(r.get("isError")) and error_text(d) == want,
                         f"O: EqualEqual_ObjectObject with A = that field is refused: {want}", d)
                line = _log_line("pipe", r"invoke_function: \S*::EqualEqual_ObjectObject inst=", since)
                ck.check(line is None, "O: pipe log: no invoke line for it (refused before anything is sent)", line)
                ck.check(outstanding() == 0, "O: nothing is outstanding")
                soft_spec_pair(field, pawn)

        # S. A function refused by name for a background client, with no parameters (an empty FString).
        if ck.check(pc.get("pick") == "live" and bool(pc.get("addr")), "S: a live PlayerController", pc):
            since = _fresh_stamp()
            r, d, _ = invoke(pc["addr"], "SendToConsole")
            want = "Not invoked: SendToConsole runs a console command, which can run anything"
            ck.check(bool(r.get("isError")) and error_text(d) == want,
                     f"S: write_invoke SendToConsole is refused: {want}", d)
            ck.check(_log_line("pipe", r"invoke_function: SendToConsole refused for a background client", since)
                     is not None, "S: pipe log: SendToConsole refused for a background client")
            line = _log_line("pipe", r"invoke_function: \S*::SendToConsole inst=", since)
            ck.check(line is None, "S: pipe log: no invoke line for it", line)

        code = c.close()
        did["closed"] = True
        ck.check(code == 0, "end: the AI session ends (the server exits 0)", code)
        ck.check(outstanding() == 0, "end: nothing is outstanding")
        p = ui.request("teleport_get_pose")
        ck.check(_near(p, p0), "end: the pawn is where it started (within 50 uu)", p)

    def cleanup() -> None:
        if c is not None and not did.get("closed"):
            _attempt("closing the AI session", c.close)
        if ui is not None:
            def pawn_back():
                p0 = did["p0"]
                if not _near(ui.request("teleport_get_pose"), p0):
                    # keep_undo: the user's "Recall last" is not this rig's to move.
                    ui.request("teleport_recall_marker", x=p0["x"], y=p0["y"], z=p0["z"], keep_undo=True)

            if did.get("moved"):
                _attempt("the pawn back where it started", pawn_back)
            ui.close()

    try:
        body()
    finally:
        cleanup()
    if extra.passed or extra.failures:
        print(f"\nO extra, recorded apart from the exit code: {extra.passed} passed, {len(extra.failures)} failed")
        for f in extra.failures:
            print(f"  -  {f}")
    return ck.done(sessions)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("mode", choices=["probe", "call", "live", "writes", "raw", "dbgcam", "dbgcamhold", "dbgcamstop",
                                     "holdsmore", "olddll", "invokeparams"])
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
    if a.mode in ("holdsmore", "olddll", "invokeparams"):
        # A holder's pid is compared with the server's process here, and the stand-in reads this machine's DLL logs.
        if a.ssh:
            ap.error(f"{a.mode} runs the server on this machine, beside the game: --ssh does not apply")
        run = {"holdsmore": run_holds_more, "olddll": run_old_dll, "invokeparams": run_invoke_params}[a.mode]
        return run(exe, a.pid)
    return run_live(exe, a.pid, a.suspend_name)


if __name__ == "__main__":
    sys.exit(main())
