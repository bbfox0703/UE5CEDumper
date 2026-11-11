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

The exe path is an argument, never assumed: a gate or CI has no build and no game (feedback: gates stay inside the
repo). Exit code 0 only when every check passed; the failures are listed.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import queue
import subprocess
import sys
import threading
import time

PROTOCOL = "2025-11-25"


class McpError(Exception):
    pass


class McpClient:
    """One server process. Requests are answered in any order; a reader thread files each line by id."""

    def __init__(self, exe: str, extra_args: list[str] | None = None):
        self.proc = subprocess.Popen(
            [exe, "--mcp", *(extra_args or [])],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, bufsize=0)
        self._next = 0
        self._lines: queue.Queue[bytes | None] = queue.Queue()
        self.framing_errors: list[str] = []
        self.stderr_lines: list[str] = []
        self._pending: dict[object, dict] = {}
        self._reader = threading.Thread(target=self._read_stdout, daemon=True)
        self._reader.start()
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

    def done(self, client: McpClient | None = None) -> int:
        if client is not None:
            for e in client.framing_errors:
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


def run_live(exe: str, pid: int | None) -> int:
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

    # A cancel of a running call: a heavy scan, cancelled at once; the server must not answer it, and the next call
    # must still work (it reconnects, and says that sessions went with the old connection).
    rid = c.request_async("tools/call", {"name": "read_value_scan_begin",
                                          "arguments": {**base, "data_type": "Int32", "value": "0",
                                                        "deadline_ms": 60000, "deep": True}})
    time.sleep(0.3)
    c.notify("notifications/cancelled", {"requestId": rid, "reason": "mcp_client.py cancel test"})
    try:
        c.wait(rid, timeout=5)
        ck.check(False, "a cancelled call gets no response", "it was answered")
    except McpError:
        ck.check(True, "a cancelled call gets no response")
    r, st2, note = call("read_status")
    ck.check(not r.get("isError") and st2.get("scanned"), "the next call works after a cancel", st2)
    ck.check(note is not None and "closed" in note, "and says the connection was closed", note)

    code = c.close()
    ck.check(code == 0, "exit code 0 after stdin closes", code)
    return ck.done(c)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("mode", choices=["probe", "call", "live"])
    ap.add_argument("--exe", required=True, help="path to UE5DumpUI.exe")
    ap.add_argument("--tool")
    ap.add_argument("--args", default="{}")
    ap.add_argument("--pid", type=int)
    a = ap.parse_args()
    exe = str(pathlib.Path(a.exe).resolve())
    if a.mode == "probe":
        return run_probe(exe)
    if a.mode == "call":
        if not a.tool:
            ap.error("call needs --tool")
        args = json.loads(a.args)
        if a.pid:
            args["pid"] = a.pid
        return run_call(exe, a.tool, args)
    return run_live(exe, a.pid)


if __name__ == "__main__":
    sys.exit(main())
