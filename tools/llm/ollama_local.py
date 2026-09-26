#!/usr/bin/env python3
r"""Local Ollama helper for Claude Code sessions: opt-in per machine, a no-op everywhere else.

    py tools/llm/ollama_local.py status [--json]      # disabled | absent | no-model | ready
    py tools/llm/ollama_local.py warm                 # load the model now (the cold load is the slow part)
    py tools/llm/ollama_local.py ask --prompt TEXT [--file PATH ...] [--chunked] [--think]
    py tools/llm/ollama_local.py unload               # free the VRAM now
    py tools/llm/ollama_local.py setup --model TAG    # opt THIS machine in (two gitignored files)
    py tools/llm/ollama_local.py setup --remove       # opt it back out
    py tools/llm/ollama_local.py hook                 # the PreToolUse hook body (hook JSON on stdin)
    py tools/llm/ollama_local.py --selftest           # pure-logic controls: no network, no processes

WHEN to use it is the skill's business: .claude/skills/local-llm/SKILL.md. This file is the
mechanism and the guard.

Exit codes: 0 done / ready, 2 not available here (disabled, absent, no model), 3 refused (a
commercial game holds the GPU, or too little VRAM is free), 4 input too large, 1 anything else.

⭐ OPT-IN, BECAUSE THE REPO IS PUBLIC. Nothing machine-specific is committed. A machine opts in with
`setup`, which writes `.claude/local-llm.json` (the model tag) and a PreToolUse hook into
`.claude/settings.local.json` -- both gitignored by `.claude/*`. Without that file every subcommand
answers `disabled` WITHOUT touching the network, so a clone on the other PC, or a stranger's, is
unaffected even if it happens to run Ollama with the same model.

⛔ GAMES AND VRAM. A 12B Q8 model holds ~14 GB of VRAM; a commercial game under test must not share
the card with it. Three layers, because a rule the model has to remember is not a rule:
  1. `ask` / `warm` REFUSE while a commercial game process is running -- and unload on the way out.
  2. `hook` runs before every Bash / PowerShell tool call. It unloads before an explicit launch
     (steam -applaunch, a *-Win64-Shipping.exe path in the command) and whenever the model is
     loaded while a commercial game is already running -- which also catches rigs and manual
     launches, one tool call late.
  3. KEEP_ALIVE is short, so an idle model leaves on its own.
  The DumperTest fixtures (DumperTest, DumperTest51, DumperTest58, ...) are exempt: the maintainer's
  rule. ⚠ THE KNOWN GAP: a rig that LAUNCHES a commercial game inside one tool call starts it after
  the hook has already run. The skill says to `unload` before such a rig.

MEASURED 2026-09-25 on the first machine that opted in (the numbers are hardware-bound; the
behaviours are Ollama's own):
  * cold load ~20 s; unload (keep_alive 0) ~0.35 s, VRAM back within 1 s.
  * ⚠ a DIFFERENT num_ctx RELOADS the model (another cold load), so num_ctx is fixed per machine
    (config key `num_ctx`, default NUM_CTX) and oversized input is refused or chunked, never "fixed"
    with a bigger window.
  * THE WINDOW IS CHEAP FOR THIS MODEL, and the GGUF header says why. Of its 48 layers, 40 are
    sliding-window (8 KV heads x 256, capped at 1024 tokens whatever num_ctx is) and only 8 are
    global -- with ONE KV head of 512. So each extra token of window costs 8 layers x 1 head x
    (512 K + 512 V) x 2 bytes (f16) = 16 KiB of KV cache. Measured (VRAM over idle, 2026-09-26):
        num_ctx    8192   16384   32768   65536   131072
        MiB       14287   14447   14767   15407    16081
    i.e. ~20 KiB/token (KV + compute buffer) up to 64k. From 64k to 128k it grew by half that,
    so Ollama's memory fitting changes beyond 64k: measure, never extrapolate. To re-derive for
    another model, read `<arch>.attention.*` from the GGUF header (head_count_kv, key_length,
    value_length, sliding_window, sliding_window_pattern).
  * ⚠ /api/ps `size_vram` reported 1.09 GB for a model nvidia-smi measured at 13.9 GB. Never judge
    VRAM from it; "unloaded" means "absent from /api/ps".
  * ⚠ on Windows a connect to a CLOSED loopback port waits out the whole timeout (a 1.5 s timeout
    took 1.51 s) instead of failing fast, so PROBE_TIMEOUT is short.
  * ⛔ BY DEFAULT OLLAMA TRUNCATES AN OVER-WINDOW PROMPT SILENTLY. A ~134k-token request to a 32k
    window came back HTTP 200 with prompt_eval_count 16387: the question at the top was cut away and
    the model confidently answered a DIFFERENT question. Every request therefore sends
    `truncate: false`, which makes the same request an HTTP 400 carrying the server's exact count
    ("request (134020 tokens) exceeds the available context size (32768 tokens)") in 0.8 s. An Ollama
    too old to know the field would ignore it and truncate again -- measured on 0.34.4.
  * ⚠ THIS MODEL'S TOKENIZER SPLITS NUMBERS DIGIT BY DIGIT: numeric log text measured 1.5-1.7 chars
    per token (the estimate had assumed 2.8 and was 1.8x short). So the estimate only PLANS slices;
    the server's count decides, and --chunked re-splits an overflowing slice at the MEASURED rate.

⚠ NOT OLLAMA_HOST. That variable is the SERVER's bind address; on a machine that exposes Ollama to
the network it is `0.0.0.0`, which a client cannot connect to on Windows. The URL comes from the
config file or DEFAULT_URL.
"""
from __future__ import annotations

import argparse
import csv
import io
import json
import os
import pathlib
import re
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.stderr.reconfigure(encoding="utf-8", errors="replace")

# ── Magic values, all in one place ───────────────────────────────────────────────────────────────
SCRIPT = pathlib.Path(__file__).resolve()
ROOT = SCRIPT.parents[2]
CONFIG_REL = pathlib.Path(".claude") / "local-llm.json"
SETTINGS_REL = pathlib.Path(".claude") / "settings.local.json"
DISABLE_ENV = "UE5CE_LLM"              # =off disables this machine, opt-in file or not
DEFAULT_URL = "http://127.0.0.1:11434"
PROBE_TIMEOUT = 0.4                    # s -- a closed loopback port costs the whole timeout
UNLOAD_TIMEOUT = 10
SETUP_TIMEOUT = 3
GENERATE_TIMEOUT = 600                 # a cold load plus a long answer
LOAD_RETRY_DELAY_S = 2                 # before retrying a load that died in CUDA init (HTTP 500)
NUM_CTX = 32768                        # fixed: a different value reloads the model
OUTPUT_RESERVE = 4096                  # tokens of the window kept for the answer
KEEP_ALIVE = "10m"
TEMPERATURE = 0.2
VRAM_MARGIN_MB = 1536                  # KV cache + runtime on top of the weights
HOOK_TIMEOUT_S = 15
HOOK_MATCHER = "Bash|PowerShell"
ASCII_CHARS_PER_TOKEN = 2.0            # planning only; numeric logs measured 1.5-1.7, prose ~4
HOPELESS_CHARS_PER_TOKEN = 8           # no real text averages more, so len/8 > window can't fit
CHUNK_SLACK_TOKENS = 256
RESPLIT_SAFETY = 0.85                  # re-split an overflowing slice at 85% of its measured rate
MAX_RESPLIT_DEPTH = 6
OVERFLOW_RE = re.compile(r"request \((\d+) tokens\) exceeds", re.I)

# A UE packaged-game process: <Stem>-<Platform>-<Config>.exe. A launcher shim (Elliot.exe) is not
# matched; the shipping exe beside it always runs while the game does.
EXE_TAIL = r"-(?:Win64|WinGDK|WinGRDK)-(?:Shipping|Test|DebugGame)\.exe"
PROCESS_EXE_RE = re.compile(r"(.+)" + EXE_TAIL, re.I)
COMMAND_EXE_RE = re.compile(r"([^\\/\"'\s]+)" + EXE_TAIL + r"\b", re.I)
EXEMPT_STEM_PREFIX = "dumpertest"
# UE-shaped helper processes that are never the game. EOSOverlayRenderer is the Epic Online Services
# overlay: measured 2026-09-26 running under the Epic Games Launcher with NO game open, which made the
# guard refuse the LLM for as long as the launcher was up. An EOS game still has its own shipping exe
# alive beside it, so excluding the helper loses nothing.
NON_GAME_STEMS = frozenset({"eosoverlayrenderer"})
# DumperTest is never a Steam app, so any Steam launch is a commercial one. `[\\"']*` and not `["']?`:
# in raw (unparsed) hook JSON the closing quote after steam.exe arrives escaped, as `\"`.
STEAM_LAUNCH_RE = re.compile(r"steam(?:\.exe)?[\\\"']*\s+(?:\S+\s+)*?-applaunch\s+\d+"
                             r"|steam://(?:rungameid|run)/\d+", re.I)

SYSTEM_PROMPT = (
    "You are a careful assistant working on an Unreal Engine reverse-engineering codebase. "
    "Answer only from the provided input. If the input does not contain the answer, say so plainly. "
    "Never invent function names, offsets, addresses, APIs or file paths. Be concise."
)


# ── Pure logic (every function here is covered by --selftest) ────────────────────────────────────
def normalize_model(name: str) -> str:
    """Ollama lists `foo` as `foo:latest`; compare case-insensitively on the full name:tag."""
    n = (name or "").strip().lower()
    return n if ":" in n.rsplit("/", 1)[-1] else n + ":latest"


def find_model(tags: dict, wanted: str):
    w = normalize_model(wanted)
    for m in (tags or {}).get("models") or []:
        if normalize_model(m.get("name") or m.get("model") or "") == w:
            return m
    return None


def _is_game_stem(stem: str) -> bool:
    s = stem.lower()
    return not s.startswith(EXEMPT_STEM_PREFIX) and s not in NON_GAME_STEMS


def is_commercial_process(image: str) -> bool:
    m = PROCESS_EXE_RE.fullmatch((image or "").strip())
    return bool(m) and _is_game_stem(m.group(1))


def command_launches_commercial(command: str) -> str | None:
    """What in this shell command looks like a commercial game launch, or None.

    Errs toward yes: `taskkill /IM Elliot-Win64-Shipping.exe` also matches. That costs nothing
    unless the model is loaded, and then only a re-warm."""
    m = STEAM_LAUNCH_RE.search(command or "")
    if m:
        return m.group(0)
    for m in COMMAND_EXE_RE.finditer(command or ""):
        if _is_game_stem(m.group(1)):
            return m.group(0)
    return None


def hook_command(raw: str) -> str:
    """The shell command from the hook's stdin JSON; the RAW text if that is not parseable JSON.

    A guard that goes blind on malformed input is the wrong way to fail: found 2026-09-25 when a
    hand-built test payload (a `\\` collapsed by the shell) was silently a no-op."""
    try:
        data = json.loads(raw) if (raw or "").strip() else {}
        return str(((data.get("tool_input") or {}).get("command")) or "")
    except (ValueError, AttributeError):
        return raw or ""


def parse_tasklist(text: str) -> list[str]:
    """Image names from `tasklist /FO CSV /NH`; the "INFO: No tasks" line is dropped."""
    return [row[0] for row in csv.reader(io.StringIO(text or ""))
            if row and row[0].lower().endswith(".exe")]


def commercial_games(images) -> list[str]:
    return sorted({i for i in images if is_commercial_process(i)}, key=str.lower)


def estimate_tokens(text: str, cpt: float = ASCII_CHARS_PER_TOKEN) -> int:
    """A PLANNING estimate (cpt = ASCII chars per token); the server's count is the truth."""
    ascii_n = len(text.encode("ascii", "ignore"))
    return int(ascii_n / cpt + (len(text) - ascii_n)) + 1


def parse_overflow(status: int, body: str):
    """The token count the server gave when refusing an over-window request (truncate:false);
    -1 if it refused without a count; None if this is not an overflow at all."""
    if status != 400 or "exceed" not in (body or "").lower():
        return None
    m = OVERFLOW_RE.search(body or "")
    return int(m.group(1)) if m else -1


def build_request(prompt: str, sections) -> str:
    """Content FIRST, the task LAST: models attend best to the end of a long context, and it is the
    end that survives if anything is ever cut. `sections` = [(header, text), ...]."""
    body = "".join(f"=== {header} ===\n{text}\n\n" for header, text in sections)
    return f"{body}=== TASK ===\n{prompt}"


def split_lines(text: str, budget_tokens: int, cpt: float = ASCII_CHARS_PER_TOKEN) -> list[tuple[int, int, str]]:
    """Consecutive line slices (first, last line number, text), each under the token budget.

    A single line over budget is cut by characters, so nothing is dropped and nothing overlaps."""
    max_chars = max(1, int(budget_tokens * min(cpt, 1.0)))   # 1 char >= 1 token: safe for any mix
    pieces = []
    for n, line in enumerate(text.splitlines(keepends=True), 1):
        if estimate_tokens(line, cpt) <= budget_tokens:
            pieces.append((n, line))
        else:
            pieces.extend((n, line[i:i + max_chars]) for i in range(0, len(line), max_chars))
    chunks, cur, first, last, used = [], [], None, None, 0
    for n, piece in pieces:
        cost = estimate_tokens(piece, cpt)
        if cur and used + cost > budget_tokens:
            chunks.append((first, last, "".join(cur)))
            cur, first, used = [], None, 0
        cur.append(piece)
        first = n if first is None else first
        last, used = n, used + cost
    if cur:
        chunks.append((first, last, "".join(cur)))
    return chunks


def config_candidates(root: pathlib.Path) -> list[pathlib.Path]:
    """The opt-in file; a session in .claude/worktrees/<name>/ also reads the MAIN checkout's."""
    out = [root / CONFIG_REL]
    parts = root.parts
    for i in range(len(parts) - 1):
        if parts[i].lower() == ".claude" and parts[i + 1].lower() == "worktrees":
            out.append(pathlib.Path(*parts[:i]) / CONFIG_REL)
            break
    return out


def main_checkout(root: pathlib.Path) -> pathlib.Path:
    return config_candidates(root)[-1].parent.parent


def _is_our_hook(h: dict) -> bool:
    args = [str(a) for a in (h.get("args") or [])]
    return any(a.replace("\\", "/").endswith("tools/llm/ollama_local.py") for a in args) and "hook" in args


def remove_hook(settings: dict) -> dict:
    """Drop only OUR PreToolUse entry; every other hook and key is left exactly as found."""
    out = dict(settings or {})
    hooks = dict(out.get("hooks") or {})
    groups = []
    for g in hooks.get("PreToolUse") or []:
        kept = [h for h in (g.get("hooks") or []) if not _is_our_hook(h)]
        if kept:
            groups.append({**g, "hooks": kept})
    if groups:
        hooks["PreToolUse"] = groups
    else:
        hooks.pop("PreToolUse", None)
    if hooks:
        out["hooks"] = hooks
    else:
        out.pop("hooks", None)
    return out


def merge_hook(settings: dict, python: str, script: str) -> dict:
    """Idempotent: re-running setup replaces our entry (a moved checkout gets the new path)."""
    out = remove_hook(settings)
    hooks = dict(out.get("hooks") or {})
    hooks["PreToolUse"] = list(hooks.get("PreToolUse") or []) + [{
        "matcher": HOOK_MATCHER,
        "hooks": [{"type": "command", "command": python, "args": [script, "hook"],
                   "timeout": HOOK_TIMEOUT_S}],
    }]
    out["hooks"] = hooks
    return out


def disabled_by_env(env) -> bool:
    return (env.get(DISABLE_ENV) or "").strip().lower() in ("off", "0", "false", "no")


# ── I/O ──────────────────────────────────────────────────────────────────────────────────────────
_OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))   # never via a proxy


def http_json(url: str, payload=None, timeout: float = PROBE_TIMEOUT):
    data = None if payload is None else json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(url, data=data,
                                 headers={"Content-Type": "application/json"} if data else {})
    with _OPENER.open(req, timeout=timeout) as r:
        return json.loads(r.read().decode("utf-8"))


def _toolhelp_images() -> list[str]:
    """Process image names from a Toolhelp32 snapshot -- milliseconds, where `tasklist` took ~1.6 s.

    The hook runs before EVERY Bash call while the model is loaded, so the spawn cost was paid on each
    one (measured 2026-09-25). ctypes, not PowerShell: see the AMSI note in handover section 10."""
    import ctypes
    from ctypes import wintypes

    class PROCESSENTRY32W(ctypes.Structure):
        _fields_ = [("dwSize", wintypes.DWORD), ("cntUsage", wintypes.DWORD),
                    ("th32ProcessID", wintypes.DWORD), ("th32DefaultHeapID", ctypes.c_size_t),
                    ("th32ModuleID", wintypes.DWORD), ("cntThreads", wintypes.DWORD),
                    ("th32ParentProcessID", wintypes.DWORD), ("pcPriClassBase", ctypes.c_long),
                    ("dwFlags", wintypes.DWORD), ("szExeFile", ctypes.c_wchar * 260)]

    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
    k32.CreateToolhelp32Snapshot.argtypes = [wintypes.DWORD, wintypes.DWORD]
    for fn in (k32.Process32FirstW, k32.Process32NextW):
        fn.argtypes = [wintypes.HANDLE, ctypes.POINTER(PROCESSENTRY32W)]
        fn.restype = wintypes.BOOL
    k32.CloseHandle.argtypes = [wintypes.HANDLE]
    snap = k32.CreateToolhelp32Snapshot(0x2, 0)                  # TH32CS_SNAPPROCESS
    if not snap or snap == ctypes.c_void_p(-1).value:            # INVALID_HANDLE_VALUE
        raise OSError(ctypes.get_last_error(), "CreateToolhelp32Snapshot")
    try:
        entry = PROCESSENTRY32W()
        entry.dwSize = ctypes.sizeof(entry)
        names, more = [], k32.Process32FirstW(snap, ctypes.byref(entry))
        while more:
            names.append(entry.szExeFile)
            more = k32.Process32NextW(snap, ctypes.byref(entry))
        return names
    finally:
        k32.CloseHandle(snap)


def _post_with_retry(url: str, payload, timeout: float, post=None, sleep=time.sleep):
    """POST once more after an HTTP 500, never after anything else.

    Measured 2026-09-26: a model load returned 500 because llama-server died on "CUDA error: shared
    object initialization failed" (0xc0000409) -- a laptop dGPU waking from idle -- and the next
    request loaded normally. A 400 is the request's own fault (e.g. an over-window prompt) and a
    second 500 is a real failure; both propagate."""
    post = post or http_json
    try:
        return post(url, payload, timeout=timeout)
    except urllib.error.HTTPError as e:
        if e.code != 500:
            raise
    sleep(LOAD_RETRY_DELAY_S)
    return post(url, payload, timeout=timeout)


def running_images() -> list[str]:
    if os.name == "nt":
        try:
            return _toolhelp_images()
        except (OSError, AttributeError, ValueError):
            pass                                                  # fall back to tasklist
    try:
        out = subprocess.run(["tasklist", "/FO", "CSV", "/NH"], capture_output=True, text=True,
                             errors="replace", timeout=15).stdout
    except (OSError, subprocess.SubprocessError):
        return []
    return parse_tasklist(out)


def gpu_free_mb():
    exe = shutil.which("nvidia-smi")
    if not exe:
        return None
    try:
        out = subprocess.run([exe, "--query-gpu=memory.total,memory.used",
                              "--format=csv,noheader,nounits"],
                             capture_output=True, text=True, timeout=15).stdout.strip()
        total, used = (int(x) for x in out.splitlines()[0].split(","))
    except (OSError, subprocess.SubprocessError, ValueError, IndexError):
        return None
    return total - used


def load_config():
    """(config, where-or-why). Reads no network: a machine without the file stays untouched."""
    if disabled_by_env(os.environ):
        return None, f"{DISABLE_ENV}=off"
    for p in config_candidates(ROOT):
        if p.is_file():
            try:
                cfg = json.loads(p.read_text(encoding="utf-8"))
            except (OSError, ValueError) as e:
                return None, f"unreadable {p.name}: {e}"
            if not isinstance(cfg, dict) or not str(cfg.get("model") or "").strip():
                return None, f'{p.name} has no "model"'
            return cfg, str(p)
    return None, "this machine has not opted in (setup --model TAG)"


class Probe:
    def __init__(self, cfg):
        self.cfg = cfg or {}
        self.url = str(self.cfg.get("url") or DEFAULT_URL).rstrip("/")
        self.num_ctx = int(self.cfg.get("num_ctx") or NUM_CTX)
        self.keep_alive = str(self.cfg.get("keep_alive") or KEEP_ALIVE)
        self.state, self.reason, self.entry, self.loaded = "disabled", "", None, False

    @property
    def name(self) -> str:
        return (self.entry or {}).get("name") or str(self.cfg.get("model") or "")

    def run(self):
        try:
            tags = http_json(self.url + "/api/tags")
        except Exception as e:                      # noqa: BLE001 -- any failure means "absent"
            self.state, self.reason = "absent", f"no Ollama at {self.url} ({type(e).__name__})"
            return self
        self.entry = find_model(tags, self.cfg["model"])
        if not self.entry:
            have = ", ".join(m.get("name", "?") for m in tags.get("models") or []) or "none"
            self.state, self.reason = "no-model", f"{self.cfg['model']} not pulled (have: {have})"
            return self
        self.state, self.loaded = "ready", self.is_loaded()
        return self

    def is_loaded(self) -> bool:
        try:
            ps = http_json(self.url + "/api/ps")
        except Exception:                           # noqa: BLE001
            return False
        return find_model(ps, self.name) is not None

    def unload(self) -> bool:
        """keep_alive 0, then wait until /api/ps no longer lists it (never trust size_vram)."""
        try:
            http_json(self.url + "/api/generate", {"model": self.name, "keep_alive": 0},
                      timeout=UNLOAD_TIMEOUT)
        except Exception:                           # noqa: BLE001
            return False
        for _ in range(20):
            if not self.is_loaded():
                self.loaded = False
                return True
            time.sleep(0.25)
        return False


def available_or_exit(quiet=False) -> Probe:
    cfg, why = load_config()
    if cfg is None:
        if not quiet:
            print(f"local-llm: disabled -- {why}", file=sys.stderr)
        sys.exit(2)
    p = Probe(cfg).run()
    if p.state != "ready":
        if not quiet:
            print(f"local-llm: {p.state} -- {p.reason}", file=sys.stderr)
        sys.exit(2)
    return p


def guard(p: Probe):
    """None when the GPU may be used, else the refusal. Unloads if it finds a game."""
    games = commercial_games(running_images())
    if games:
        if p.loaded:
            p.unload()
        return f"a commercial game is running ({', '.join(games)}); its VRAM is not ours"
    if not p.loaded:
        free = gpu_free_mb()
        need = int((p.entry or {}).get("size") or 0) // (1024 * 1024) + VRAM_MARGIN_MB
        if free is not None and free < need:
            return (f"{free} MB VRAM free, the model needs ~{need} MB -- something else holds "
                    f"the GPU (a game whose exe does not look like a UE shipping build?)")
    return None


# ── Subcommands ──────────────────────────────────────────────────────────────────────────────────
def cmd_status(args) -> int:
    cfg, why = load_config()
    if cfg is None:
        info = {"state": "disabled", "reason": why}
    else:
        p = Probe(cfg).run()
        info = {"state": p.state, "reason": p.reason, "model": p.name, "url": p.url,
                "num_ctx": p.num_ctx, "loaded": p.loaded}
        if p.state == "ready":
            info["commercial_games"] = commercial_games(running_images())
    if args.json:
        print(json.dumps(info))
    else:
        print("  ".join(f"{k}={v}" for k, v in info.items() if v not in ("", None)))
    return 0 if info["state"] == "ready" else 2


def cmd_warm(args) -> int:
    p = available_or_exit()
    refusal = guard(p)
    if refusal:
        print(f"local-llm: refused -- {refusal}", file=sys.stderr)
        return 3
    t0 = time.perf_counter()
    _post_with_retry(p.url + "/api/generate", {"model": p.name, "prompt": "", "keep_alive": p.keep_alive,
                                               "options": {"num_ctx": p.num_ctx}}, GENERATE_TIMEOUT)
    print(f"local-llm: {p.name} loaded (num_ctx={p.num_ctx}, keep_alive={p.keep_alive}) "
          f"in {time.perf_counter() - t0:.1f}s")
    return 0


def cmd_unload(args) -> int:
    cfg, why = load_config()
    if cfg is None:
        print(f"local-llm: disabled -- {why}")
        return 0
    p = Probe(cfg).run()
    if p.state != "ready" or not p.loaded:
        print(f"local-llm: nothing to unload ({p.state}{', not loaded' if p.state == 'ready' else ''})")
        return 0
    ok = p.unload()
    print(f"local-llm: {p.name} {'unloaded' if ok else 'STILL LOADED -- check `ollama ps`'}")
    return 0 if ok else 1


class ContextOverflow(Exception):
    def __init__(self, tokens: int):
        super().__init__(f"request is {tokens} tokens")
        self.tokens = tokens


class Refused(Exception):
    pass


def _chat(p: Probe, user: str, think: bool) -> dict:
    """One request. `truncate: false` turns Ollama's silent over-window cut into ContextOverflow."""
    try:
        return _post_with_retry(p.url + "/api/chat", {
            "model": p.name, "stream": False, "think": bool(think), "keep_alive": p.keep_alive,
            "truncate": False,
            "messages": [{"role": "system", "content": SYSTEM_PROMPT},
                         {"role": "user", "content": user}],
            "options": {"num_ctx": p.num_ctx, "temperature": TEMPERATURE, "num_predict": OUTPUT_RESERVE},
        }, GENERATE_TIMEOUT)
    except urllib.error.HTTPError as e:
        tokens = parse_overflow(e.code, e.read().decode("utf-8", errors="replace"))
        if tokens is None:
            raise
        raise ContextOverflow(tokens) from None


def _stats(r: dict) -> str:
    gen_s = (r.get("eval_duration") or 0) / 1e9
    rate = f"{(r.get('eval_count') or 0) / gen_s:.0f} tok/s" if gen_s else "n/a"
    return (f"[local-llm] load {(r.get('load_duration') or 0) / 1e9:.1f}s, prompt "
            f"{r.get('prompt_eval_count')} tok (untruncated), answer {r.get('eval_count')} tok @ {rate}")


def _answer_slices(p: Probe, prompt: str, name: str, first: int, last: int, text: str, think: bool,
                   cpt: float, depth: int = 0):
    """Yield (first, last, response) for one slice; on overflow re-split it at the MEASURED rate."""
    refusal = guard(p)                               # a game may start mid-batch
    if refusal:
        raise Refused(refusal)
    request = build_request(prompt, [(f"FILE: {name} (lines {first}-{last})", text)])
    try:
        r = _chat(p, request, think)
    except ContextOverflow as e:
        if depth >= MAX_RESPLIT_DEPTH or not text:
            raise
        measured = len(request) / e.tokens if e.tokens > 0 else cpt / 2
        cpt = min(cpt, measured) * RESPLIT_SAFETY
        budget = p.num_ctx - OUTPUT_RESERVE - estimate_tokens(SYSTEM_PROMPT + prompt, cpt) - CHUNK_SLACK_TOKENS
        subs = split_lines(text, max(budget, 256), cpt)
        if len(subs) < 2:                            # never retry the same slice unchanged
            half = max(1, len(text) // 2)
            subs = [(1, 1, text[:half]), (1, 1, text[half:])]
        print(f"[local-llm] {name} lines {first}-{last} was {e.tokens} tokens; re-split into "
              f"{len(subs)} at {cpt:.2f} chars/token", file=sys.stderr)
        for a, b, t in subs:
            yield from _answer_slices(p, prompt, name, first + a - 1, first + b - 1, t, think, cpt, depth + 1)
        return
    p.loaded = True
    yield first, last, r


def cmd_ask(args) -> int:
    prompt = args.prompt or ""
    if args.prompt_file:
        prompt = (prompt + "\n\n" if prompt else "") + pathlib.Path(args.prompt_file).read_text(
            encoding="utf-8", errors="replace")
    if not prompt.strip():
        print("local-llm: --prompt or --prompt-file is required", file=sys.stderr)
        return 1
    files = [(f, pathlib.Path(f).read_text(encoding="utf-8", errors="replace")) for f in args.file or []]
    p = available_or_exit()
    refusal = guard(p)
    if refusal:
        print(f"local-llm: refused -- {refusal}", file=sys.stderr)
        return 3
    budget = p.num_ctx - OUTPUT_RESERVE - estimate_tokens(SYSTEM_PROMPT + prompt) - CHUNK_SLACK_TOKENS

    if not args.chunked:
        whole = build_request(prompt, [(f"FILE: {name}", text) for name, text in files])
        if len(whole) > p.num_ctx * HOPELESS_CHARS_PER_TOKEN:
            print(f"local-llm: {len(whole)} chars cannot fit a {p.num_ctx}-token window. Pre-slice it "
                  f"(grep/sed) or pass --chunked.", file=sys.stderr)
            return 4
        try:
            r = _chat(p, whole, args.think)
        except ContextOverflow as e:
            print(f"local-llm: the server counted {e.tokens} tokens; the window is {p.num_ctx}. Pre-slice "
                  f"it (grep/sed) or pass --chunked.", file=sys.stderr)
            return 4
        print(r["message"]["content"])
        print(_stats(r), file=sys.stderr)
        return 0

    if budget < 1024:
        print("local-llm: the prompt alone nearly fills the window", file=sys.stderr)
        return 4
    n = 0
    try:
        for name, body in files or [("(prompt only)", "")]:
            for first, last, text in split_lines(body, budget) or [(0, 0, "")]:
                for a, b, r in _answer_slices(p, prompt, name, first, last, text, args.think,
                                              ASCII_CHARS_PER_TOKEN):
                    n += 1
                    print(f"### slice {n} -- {name} lines {a}-{b}\n{r['message']['content']}\n", flush=True)
                    print(_stats(r), file=sys.stderr)
    except Refused as e:
        print(f"local-llm: stopped after {n} slice(s) -- {e}", file=sys.stderr)
        return 3
    except ContextOverflow as e:
        print(f"local-llm: a slice stayed over the window after {MAX_RESPLIT_DEPTH} re-splits "
              f"({e.tokens} tokens) -- pre-slice the input", file=sys.stderr)
        return 4
    return 0


def cmd_hook(args) -> int:
    """Never blocks and never fails the tool call: every path exits 0."""
    try:
        raw = sys.stdin.buffer.read().decode("utf-8", errors="replace")
        command = hook_command(raw)
        cfg, _ = load_config()
        if cfg is None:
            return 0
        p = Probe(cfg)
        p.entry = {"name": str(cfg["model"])}
        if not p.is_loaded():                        # the common case: one /api/ps round trip
            return 0
        try:
            p.entry = find_model(http_json(p.url + "/api/ps"), p.name) or p.entry
        except Exception:                            # noqa: BLE001
            pass
        why = command_launches_commercial(command)
        if why:
            why = f"launch in this command: {why}"
        else:
            games = commercial_games(running_images())
            why = f"running: {', '.join(games)}" if games else None
        if why and p.unload():
            print(json.dumps({"systemMessage": f"local-llm: unloaded {p.name} to free VRAM ({why})"}))
    except Exception:                                # noqa: BLE001
        pass
    return 0


def cmd_setup(args) -> int:
    main = main_checkout(ROOT)
    cfg_path, settings_path = main / CONFIG_REL, main / SETTINGS_REL
    settings = {}
    if settings_path.is_file():
        try:
            settings = json.loads(settings_path.read_text(encoding="utf-8"))
        except ValueError as e:
            print(f"local-llm: {settings_path} is not valid JSON ({e}); fix it first", file=sys.stderr)
            return 1

    if args.remove:
        cfg_path.unlink(missing_ok=True)
        if settings_path.is_file():
            settings_path.write_text(json.dumps(remove_hook(settings), indent=2) + "\n", encoding="utf-8")
        print(f"local-llm: opted out -- removed {CONFIG_REL.as_posix()} and the hook")
        return 0

    if not args.model:
        print("local-llm: setup needs --model TAG (or --remove)", file=sys.stderr)
        return 1
    url = (args.url or DEFAULT_URL).rstrip("/")
    try:
        tags = http_json(url + "/api/tags", timeout=SETUP_TIMEOUT)
    except Exception as e:                           # noqa: BLE001
        print(f"local-llm: no Ollama answers at {url} ({type(e).__name__})", file=sys.stderr)
        return 2
    entry = find_model(tags, args.model)
    if not entry:
        have = ", ".join(m.get("name", "?") for m in tags.get("models") or []) or "none"
        print(f"local-llm: {args.model} is not pulled here (have: {have})", file=sys.stderr)
        return 2

    cfg = {"model": entry["name"]}
    if args.url:
        cfg["url"] = url
    if args.num_ctx:
        cfg["num_ctx"] = args.num_ctx
    cfg_path.parent.mkdir(parents=True, exist_ok=True)
    cfg_path.write_text(json.dumps(cfg, indent=2) + "\n", encoding="utf-8")
    python = shutil.which("py") or sys.executable
    script = (main / "tools" / "llm" / "ollama_local.py").as_posix()
    settings_path.write_text(json.dumps(merge_hook(settings, python, script), indent=2) + "\n",
                             encoding="utf-8")
    print(f"local-llm: opted in -- {CONFIG_REL.as_posix()} = {json.dumps(cfg)}; PreToolUse hook "
          f"({HOOK_MATCHER}) written to {SETTINGS_REL.as_posix()}. Both are gitignored.\n"
          f"The hook takes effect in a NEW session (or after opening /hooks once).")
    return 0


# ── Self-test ────────────────────────────────────────────────────────────────────────────────────
def selftest() -> int:
    checks = []

    def ok(name, cond):
        checks.append((name, bool(cond)))

    tags = {"models": [{"name": "gemma4:latest"}, {"name": "gemma-4-12b-it-Q8_0:latest"},
                       {"name": "hf.co/org/repo:Q4_K_M"}]}
    ok("normalize adds :latest", normalize_model("gemma-4-12b-it-Q8_0") == "gemma-4-12b-it-q8_0:latest")
    ok("normalize keeps a tag", normalize_model("qwen:7b") == "qwen:7b")
    ok("normalize: a registry path's colon is the tag", normalize_model("hf.co/org/repo") == "hf.co/org/repo:latest")
    ok("find: bare tag matches :latest", (find_model(tags, "gemma-4-12b-it-Q8_0") or {}).get("name") == "gemma-4-12b-it-Q8_0:latest")
    ok("find: case-insensitive", find_model(tags, "GEMMA-4-12B-IT-q8_0:LATEST") is not None)
    ok("find: gemma4 is not the 12B", (find_model(tags, "gemma4") or {}).get("name") == "gemma4:latest")
    ok("find: absent -> None", find_model(tags, "gemma-4-12b-it-Q4_0") is None)
    ok("find: no models key", find_model({}, "x") is None)

    for image, want in [("Elliot-Win64-Shipping.exe", True), ("FactoryGameSteam-Win64-Shipping.exe", True),
                        ("Game-WinGDK-Shipping.exe", True), ("Some Game-Win64-Shipping.exe", True),
                        ("DumperTest-Win64-Shipping.exe", False), ("DumperTest51-Win64-Shipping.exe", False),
                        ("DumperTest58-Win64-DebugGame.exe", False), ("dumpertest-win64-shipping.exe", False),
                        ("DumperTest.exe", False), ("Elliot.exe", False), ("ollama.exe", False),
                        ("Code.exe", False), ("Elliot-Win64-Shipping.exe.bak", False),
                        ("EOSOverlayRenderer-Win64-Shipping.exe", False)]:
        ok(f"process {image} -> {want}", is_commercial_process(image) is want)

    for cmd, want in [
        ('"C:\\Program Files (x86)\\Steam\\steam.exe" -applaunch 526870', True),
        ("steam -silent -applaunch 3483510", True),
        ("start steam://rungameid/1363080", True),
        ('start "" "D:\\SteamLibrary\\steamapps\\common\\Elliot\\Binaries\\Win64\\Elliot-Win64-Shipping.exe"', True),
        ("taskkill /IM Elliot-Win64-Shipping.exe", False),  # a kill is not a launch (and now reserves the GPU)
        ("taskkill /f /im Elliot-Win64-Shipping.exe && echo done", False),
        ("py tools/verify/launch_dumpertest.py shipping", False),
        ("py tools/verify/inject.py --name DumperTest", False),
        ('"D:\\Out\\DumperTest51\\Binaries\\Win64\\DumperTest51-Win64-Shipping.exe" -windowed', False),
        ("git log --oneline -5", False),
        ("grep -n applaunch docs/handover-2026-08-22.md", False),
        ("", False),
    ]:
        ok(f"command {cmd[:48]!r} -> {want}", (command_launches_commercial(cmd) is not None) is want)

    good = json.dumps({"tool_name": "Bash", "tool_input": {"command": "steam.exe -applaunch 1"}})
    ok("hook input: the command field", hook_command(good) == "steam.exe -applaunch 1")
    ok("hook input: malformed JSON is scanned raw, not ignored",
       command_launches_commercial(hook_command('{"command":"\\"C:\\Steam\\steam.exe\\" -applaunch 3"')) is not None)
    ok("hook input: empty and non-object inputs", hook_command("") == "" and hook_command("[1]") == "[1]")
    ok("command: the EOS overlay helper is not a game launch",
       command_launches_commercial("taskkill /IM EOSOverlayRenderer-Win64-Shipping.exe") is None)

    def fake_post(results):
        calls = []

        def post(url, payload, timeout):
            calls.append(url)
            r = results[len(calls) - 1]
            if isinstance(r, int):
                raise urllib.error.HTTPError(url, r, "fake", None, io.BytesIO(b"{}"))
            return r
        return post, calls

    post, calls = fake_post([500, {"done": True}])
    ok("retry: one transient 500 (CUDA init) is retried once",
       _post_with_retry("u", {}, 1, post=post, sleep=lambda s: None) == {"done": True} and len(calls) == 2)
    for seq, label in (([400], "a 400 is not retried"), ([500, 500], "a second 500 propagates")):
        post, calls = fake_post(seq)
        try:
            _post_with_retry("u", {}, 1, post=post, sleep=lambda s: None)
            raised = None
        except urllib.error.HTTPError as e:
            raised = e.code
        ok(f"retry: {label}", raised == seq[-1] and len(calls) == len(seq))

    listing = ('"System Idle Process","0","Services","0","8 K"\r\n'
               '"Elliot-Win64-Shipping.exe","4242","Console","1","3,210,000 K"\r\n'
               '"DumperTest51-Win64-Shipping.exe","62872","Console","1","900,000 K"\r\n'
               '"py.exe","1","Console","1","9 K"\r\n')
    images = parse_tasklist(listing)
    ok("tasklist: exe rows only", images == ["Elliot-Win64-Shipping.exe", "DumperTest51-Win64-Shipping.exe", "py.exe"])
    ok("tasklist: the no-match INFO line is dropped",
       parse_tasklist("INFO: No tasks are running which match the specified criteria.\r\n") == [])
    ok("games: DumperTest exempt, commercial kept", commercial_games(images) == ["Elliot-Win64-Shipping.exe"])

    ok("estimate: ascii at the planning rate", 1700 <= estimate_tokens("a" * 3500) <= 1800)
    ok("estimate: a measured rate is honoured", 1000 <= estimate_tokens("a" * 1500, 1.5) <= 1001)
    ok("estimate: CJK is one per char", 1000 <= estimate_tokens("中" * 1000) <= 1001)
    text = "".join(f"line {i:05d} 0x7FF6{i:08X}\n" for i in range(1, 5001))
    chunks = split_lines(text, 2000)
    ok("split: more than one chunk", len(chunks) > 1)
    ok("split: every chunk under budget", all(estimate_tokens(c) <= 2000 for _, _, c in chunks))
    ok("split: lossless and in order", "".join(c for _, _, c in chunks) == text)
    ok("split: line numbers contiguous", chunks[0][0] == 1 and chunks[-1][1] == 5000
       and all(a[1] + 1 == b[0] for a, b in zip(chunks, chunks[1:])))
    giant = "x" * 50000
    parts = split_lines(giant, 1000)
    ok("split: an over-budget line is cut, not dropped", "".join(c for _, _, c in parts) == giant
       and all(estimate_tokens(c) <= 1000 for _, _, c in parts))
    ok("split: empty input -> no chunks", split_lines("", 1000) == [])
    tighter = split_lines(text, 2000, 1.5)
    ok("split: a lower measured rate makes more, smaller slices", len(tighter) > len(chunks)
       and "".join(c for _, _, c in tighter) == text)

    measured = ('{"error":"{\\"error\\":{\\"code\\":400,\\"message\\":\\"request (134020 tokens) exceeds the '
                'available context size (32768 tokens), try increasing it\\",\\"type\\":\\"exceed_context_size_error\\"}}"}')
    ok("overflow: the measured 0.34.4 body parses to its count", parse_overflow(400, measured) == 134020)
    ok("overflow: a 400 that says exceed but gives no count -> -1", parse_overflow(400, "context exceeded") == -1)
    ok("overflow: another 400 is not an overflow", parse_overflow(400, '{"error":"model not found"}') is None)
    ok("overflow: a 500 is not an overflow", parse_overflow(500, measured) is None)
    nl = chr(10)
    req = build_request("What is on the last line?", [("FILE: a.log", "x" + nl), ("FILE: b.log", "y" + nl)])
    ok("request: content first, the task LAST", req.index("FILE: a.log") < req.index("FILE: b.log") < req.index("=== TASK ===")
       and req.rstrip().endswith("What is on the last line?"))
    ok("request: no files -> the task alone", build_request("Q", []) == "=== TASK ===" + nl + "Q")

    main = pathlib.Path("D:/Repo")
    wt = main / ".claude" / "worktrees" / "blissful-x"
    ok("config: main checkout reads its own", config_candidates(main) == [main / CONFIG_REL])
    ok("config: a worktree also reads the main checkout's", config_candidates(wt)[-1] == main / CONFIG_REL)
    ok("config: main_checkout of a worktree", main_checkout(wt) == main)

    foreign = {"permissions": {"allow": ["Bash(git *)"]},
               "hooks": {"PreToolUse": [{"matcher": "Bash", "hooks": [{"type": "command", "command": "echo hi"}]}],
                         "Stop": [{"hooks": [{"type": "command", "command": "echo bye"}]}]}}
    merged = merge_hook(foreign, "py", "D:/Repo/tools/llm/ollama_local.py")
    ok("merge: foreign keys kept", merged["permissions"] == foreign["permissions"] and merged["hooks"]["Stop"] == foreign["hooks"]["Stop"])
    ok("merge: foreign PreToolUse kept", merged["hooks"]["PreToolUse"][0] == foreign["hooks"]["PreToolUse"][0])
    ok("merge: ours appended once", sum(_is_our_hook(h) for g in merged["hooks"]["PreToolUse"] for h in g["hooks"]) == 1)
    again = merge_hook(merged, "py", "E:/Moved/tools/llm/ollama_local.py")
    ours = [h for g in again["hooks"]["PreToolUse"] for h in g["hooks"] if _is_our_hook(h)]
    ok("merge: idempotent, and a moved checkout replaces the path", len(ours) == 1 and ours[0]["args"][0].startswith("E:/Moved"))
    ok("merge: exec form, no shell", ours[0]["command"] == "py" and ours[0]["args"][-1] == "hook")
    ok("merge: runs through the missing-script bootstrap", ours[0]["args"][:2] == ["-c", HOOK_BOOTSTRAP]
       and ours[0]["args"][-2] == "E:/Moved/tools/llm/ollama_local.py")
    legacy = {"hooks": {"PreToolUse": [{"matcher": HOOK_MATCHER, "hooks": [{"type": "command", "command": "py",
              "args": ["D:/R/tools/llm/ollama_local.py", "hook"]}]}]}}
    ok("remove: the pre-bootstrap exec form is still recognised as ours", remove_hook(legacy) == {})
    saved_argv = sys.argv
    try:
        sys.argv = ["-c", "Z:/no/such/dir/tools/llm/ollama_local.py", "hook"]
        exec(compile(HOOK_BOOTSTRAP, "<bootstrap>", "exec"), {"__name__": "bootstrap"})
        ran_clean = True
    except BaseException:                            # noqa: BLE001 -- SystemExit(2) is the failure
        ran_clean = False
    finally:
        sys.argv = saved_argv
    ok("bootstrap: a missing script is a silent no-op, never exit 2 (which BLOCKS the tool call)", ran_clean)

    procs = parse_tasklist_procs('"Elliot-Win64-Shipping.exe","4242","Console","1","3 K"\r\n"py.exe","77","C","1","9 K"\r\n')
    ok("tasklist: pid and image", procs == [(4242, "Elliot-Win64-Shipping.exe"), (77, "py.exe")])

    now = 10_000.0
    leases = [{"pid": 1, "started": now - 5}, {"pid": 2, "started": now - 5},
              {"pid": 3, "started": now - LEASE_MAX_AGE_S - 1}, {"pid": 99, "started": now - 1}, "junk"]
    ok("leases: only other LIVE, FRESH processes count",
       [r["pid"] for r in live_leases(leases, now, alive_pids={1, 3, 99}, own_pid=99)] == [1])

    for marker, running, want in [
        (None, False, "none"), (None, True, "none"),
        ({"kind": "launch", "set_at": now - 60}, False, "active"),
        ({"kind": "launch", "set_at": now - RESERVE_GRACE_S["launch"] - 1}, False, "expired"),
        ({"kind": "running", "set_at": now - RESERVE_GRACE_S["running"] - 1}, False, "expired"),
        ({"kind": "running", "set_at": now - 999_999}, True, "active"),
        ({"kind": "???", "set_at": now - 60}, False, "active"),
    ]:
        ok(f"reservation {marker and marker['kind']} game={running} -> {want}",
           reservation_state(marker, now, running) == want)
    ok("remove: restores the foreign settings exactly", remove_hook(again) == foreign)
    ok("remove: an only-ours file loses the hooks key", remove_hook(merge_hook({}, "py", "D:/R/tools/llm/ollama_local.py")) == {})
    ok("remove: a foreign hook that merely mentions the script is kept",
       remove_hook({"hooks": {"PreToolUse": [{"hooks": [{"type": "command", "command": "py",
                    "args": ["D:/R/tools/llm/ollama_local.py", "status"]}]}]}})["hooks"]["PreToolUse"] != [])

    ok("env: UE5CE_LLM=off disables", disabled_by_env({DISABLE_ENV: "off"}) and disabled_by_env({DISABLE_ENV: " OFF "}))
    ok("env: unset or on does not", not disabled_by_env({}) and not disabled_by_env({DISABLE_ENV: "on"}))

    failed = [n for n, good in checks if not good]
    for n in failed:
        print(f"FAIL  {n}")
    print(f"ollama_local selftest: {len(checks) - len(failed)}/{len(checks)} controls passed")
    return 1 if failed else 0


def main(argv) -> int:
    if "--selftest" in argv:
        return selftest()
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("status")
    s.add_argument("--json", action="store_true")
    sub.add_parser("warm")
    sub.add_parser("unload")
    sub.add_parser("hook")
    a = sub.add_parser("ask")
    a.add_argument("--prompt")
    a.add_argument("--prompt-file")
    a.add_argument("--file", action="append", help="read by this script, so the text never enters the caller's context")
    a.add_argument("--chunked", action="store_true", help="ask once per window-sized slice of the files")
    a.add_argument("--think", action="store_true")
    u = sub.add_parser("setup")
    u.add_argument("--model")
    u.add_argument("--url")
    u.add_argument("--num-ctx", type=int)
    u.add_argument("--remove", action="store_true")
    args = ap.parse_args(argv)
    return {"status": cmd_status, "warm": cmd_warm, "unload": cmd_unload, "hook": cmd_hook,
            "ask": cmd_ask, "setup": cmd_setup}[args.cmd](args)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
