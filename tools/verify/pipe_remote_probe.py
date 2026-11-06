r"""Who can open the DLL's named pipe: local vs remote clients, default vs explicit DACL.

    py tools/verify/pipe_remote_probe.py matrix     # probe pipes only -- no game, no DLL
    py tools/verify/pipe_remote_probe.py live       # the injected DLL's \\.\pipe\UE5DumpBfx

WHY THIS RIG EXISTS ([PIPE-REJECT-REMOTE], docs/todo.md)
  Fern created its pipe without PIPE_REJECT_REMOTE_CLIENTS, so a client holding this user's (or an
  admin's) credentials on ANOTHER machine could open \\host\pipe\UE5DumpBfx over SMB and drive the
  DLL. Proving the fix needs a remote client, and this PC has no second machine on hand. It does
  not need one: an open of \\localhost\pipe\<name> goes through this machine's own SMB server
  (LanmanServer / srv2), and the pipe sees it exactly as it sees a client from another host -- a
  network logon, the NETWORK SID in its token, and the remote bit PIPE_REJECT_REMOTE_CLIENTS tests.

  ⚠ A REFUSED remote open proves nothing on its own. If the SMB server is stopped, or policy blocks
  loopback, EVERY \\localhost open fails, and "refused" reads like the flag working. Both modes
  therefore first open a CONTROL pipe of their own -- default security, no flag -- over the same
  path, and refuse to report a verdict when the control is not reachable remotely.

WHAT `matrix` MEASURES (no game needed)
  Four probe pipes this process serves, each opened locally (\\.\pipe) and remotely (\\localhost):
    default SD                 -- what Fern had: nullptr security, no flag (the control)
    default SD + flag          -- what Fern ships after [PIPE-REJECT-REMOTE]
    explicit DACL              -- the candidate: deny NETWORK, allow SYSTEM + this user
    explicit DACL + flag
  It also prints this token's user / owner / integrity / elevation and the SDDL that nullptr
  security produces for it, which is what the DACL evaluation in the todo row reasons from, and
  the token the pipe SEES for a local and for a loopback-SMB client (the server end impersonates
  each). A deny-NETWORK ACE fires only on a token that carries NETWORK; when the loopback client's
  does not, the rig says the ACE was not exercised instead of scoring its remote open.
  ⚠ This session's token is the ONLY one it measures. The combinations that differ -- an elevated
  game with a non-elevated client, a game under another account -- need a token this process
  cannot make without a UAC prompt, and the row records them as not measured.

WHAT `live` MEASURES (DumperTest launched and injected; the UI must NOT be connected)
  The control above, then the real pipe: its security descriptor as SDDL, a local read/write open
  (expect success), and a remote read/write open (expect refusal). Each open takes one of the
  DLL's pipe slots for an instant and closes it without sending a byte, exactly as a client that
  connects and leaves; Fern.h's kMaxPipeInstances is why the UI must not hold two of them.

Exit codes: 0 every expectation held; 1 an expectation failed (the verdict line says which);
2 nothing measurable (no SMB loopback, or no pipe to probe).
"""
import argparse
import ctypes
import os
import sys
from ctypes import wintypes

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

LIVE_NAME = "UE5DumpBfx"

PIPE_ACCESS_DUPLEX = 0x00000003
PIPE_TYPE_BYTE = 0x00000000
PIPE_READMODE_BYTE = 0x00000000
PIPE_WAIT = 0x00000000
PIPE_REJECT_REMOTE_CLIENTS = 0x00000008
PIPE_UNLIMITED_INSTANCES = 255
GENERIC_READ = 0x80000000
GENERIC_WRITE = 0x40000000
READ_CONTROL = 0x00020000
OPEN_EXISTING = 3
SE_KERNEL_OBJECT = 6
OWNER_SECURITY_INFORMATION = 0x1
GROUP_SECURITY_INFORMATION = 0x2
DACL_SECURITY_INFORMATION = 0x4
LABEL_SECURITY_INFORMATION = 0x10
SDDL_REVISION_1 = 1
TOKEN_QUERY = 0x0008
TokenUser, TokenOwner, TokenElevation, TokenIntegrityLevel = 1, 4, 20, 25
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value

ERR_NAMES = {2: "FILE_NOT_FOUND", 5: "ACCESS_DENIED", 53: "BAD_NETPATH", 67: "BAD_NET_NAME",
             231: "PIPE_BUSY", 1326: "LOGON_FAILURE", 1219: "SESSION_CREDENTIAL_CONFLICT",
             64: "NETNAME_DELETED", 233: "PIPE_NOT_CONNECTED", 1265: "DOWNGRADE_DETECTED"}

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
adv = ctypes.WinDLL("advapi32", use_last_error=True)


class SECURITY_ATTRIBUTES(ctypes.Structure):
    _fields_ = [("nLength", wintypes.DWORD),
                ("lpSecurityDescriptor", ctypes.c_void_p),
                ("bInheritHandle", wintypes.BOOL)]


k32.CreateNamedPipeW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.DWORD,
                                 wintypes.DWORD, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p]
k32.CreateNamedPipeW.restype = wintypes.HANDLE
k32.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p,
                            wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
k32.CreateFileW.restype = wintypes.HANDLE
k32.CloseHandle.argtypes = [wintypes.HANDLE]
k32.GetCurrentProcess.restype = wintypes.HANDLE
k32.LocalFree.argtypes = [ctypes.c_void_p]
adv.ConvertStringSecurityDescriptorToSecurityDescriptorW.argtypes = [
    wintypes.LPCWSTR, wintypes.DWORD, ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(wintypes.ULONG)]
adv.ConvertSecurityDescriptorToStringSecurityDescriptorW.argtypes = [
    ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD, ctypes.POINTER(wintypes.LPWSTR),
    ctypes.POINTER(wintypes.ULONG)]
adv.GetSecurityInfo.argtypes = [wintypes.HANDLE, ctypes.c_int, wintypes.DWORD,
                                ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p,
                                ctypes.POINTER(ctypes.c_void_p)]
adv.GetSecurityInfo.restype = wintypes.DWORD
adv.OpenProcessToken.argtypes = [wintypes.HANDLE, wintypes.DWORD, ctypes.POINTER(wintypes.HANDLE)]
adv.GetTokenInformation.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD,
                                    ctypes.POINTER(wintypes.DWORD)]
adv.ConvertSidToStringSidW.argtypes = [ctypes.c_void_p, ctypes.POINTER(wintypes.LPWSTR)]
adv.GetSidSubAuthorityCount.argtypes = [ctypes.c_void_p]
adv.GetSidSubAuthorityCount.restype = ctypes.POINTER(ctypes.c_ubyte)
adv.GetSidSubAuthority.argtypes = [ctypes.c_void_p, wintypes.DWORD]
adv.GetSidSubAuthority.restype = ctypes.POINTER(wintypes.DWORD)
adv.OpenThreadToken.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.BOOL, ctypes.POINTER(wintypes.HANDLE)]
adv.ImpersonateNamedPipeClient.argtypes = [wintypes.HANDLE]
k32.GetCurrentThread.restype = wintypes.HANDLE
k32.ConnectNamedPipe.argtypes = [wintypes.HANDLE, ctypes.c_void_p]
k32.WriteFile.argtypes = [wintypes.HANDLE, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD),
                          ctypes.c_void_p]
k32.ReadFile.argtypes = [wintypes.HANDLE, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD),
                         ctypes.c_void_p]

TokenGroups = 2
SID_NETWORK = "S-1-5-2"
SID_INTERACTIVE = "S-1-5-4"
SE_GROUP_USE_FOR_DENY_ONLY = 0x10
ERROR_PIPE_CONNECTED = 535


class SID_AND_ATTRIBUTES(ctypes.Structure):
    _fields_ = [("Sid", ctypes.c_void_p), ("Attributes", wintypes.DWORD)]


class TOKEN_GROUPS_HEAD(ctypes.Structure):
    _fields_ = [("GroupCount", wintypes.DWORD), ("Groups", SID_AND_ATTRIBUTES * 1)]


def err_text(code):
    return "%d (%s)" % (code, ERR_NAMES.get(code, "?"))


def sid_str(psid):
    s = wintypes.LPWSTR()
    if not adv.ConvertSidToStringSidW(psid, ctypes.byref(s)):
        return "?"
    out = s.value
    k32.LocalFree(s)
    return out


def describe_token(tok):
    """What an access check sees in a token: the user, whether NETWORK / INTERACTIVE are among its
    groups (a deny-NETWORK ACE fires only on an ENABLED or deny-only NETWORK group), integrity,
    elevation. The token is the caller's to close."""
    def info(cls):
        need = wintypes.DWORD()
        adv.GetTokenInformation(tok, cls, None, 0, ctypes.byref(need))
        buf = ctypes.create_string_buffer(max(need.value, 8))
        if not adv.GetTokenInformation(tok, cls, buf, need, ctypes.byref(need)):
            raise OSError(ctypes.get_last_error(), "GetTokenInformation(%d)" % cls)
        return buf

    user = sid_str(ctypes.c_void_p.from_buffer(info(TokenUser)).value)
    owner = sid_str(ctypes.c_void_p.from_buffer(info(TokenOwner)).value)
    elevated = bool(wintypes.DWORD.from_buffer(info(TokenElevation)).value)
    il_sid = ctypes.c_void_p.from_buffer(info(TokenIntegrityLevel)).value
    n = adv.GetSidSubAuthorityCount(il_sid)[0]
    il = adv.GetSidSubAuthority(il_sid, n - 1)[0]
    gbuf = info(TokenGroups)
    head = TOKEN_GROUPS_HEAD.from_buffer(gbuf)
    arr = (SID_AND_ATTRIBUTES * head.GroupCount).from_buffer(gbuf, TOKEN_GROUPS_HEAD.Groups.offset)
    groups = {sid_str(g.Sid): g.Attributes for g in arr}

    def group_state(sid):
        if sid not in groups:
            return "absent"
        return "deny-only" if groups[sid] & SE_GROUP_USE_FOR_DENY_ONLY else "present"
    return {"user": user, "owner": owner, "elevated": elevated, "integrity": "0x%04X" % il,
            "network": group_state(SID_NETWORK), "interactive": group_state(SID_INTERACTIVE)}


def token_facts():
    """This process's token: the inputs nullptr security and the candidate DACL are built from."""
    tok = wintypes.HANDLE()
    if not adv.OpenProcessToken(k32.GetCurrentProcess(), TOKEN_QUERY, ctypes.byref(tok)):
        raise OSError(ctypes.get_last_error(), "OpenProcessToken")
    try:
        return describe_token(tok)
    finally:
        k32.CloseHandle(tok)


def fmt_token(t):
    return ("user %(user)s  owner %(owner)s  integrity %(integrity)s  elevated %(elevated)s  "
            "NETWORK %(network)s  INTERACTIVE %(interactive)s" % t)


_ME = None


def say(line):
    """print(), with this account's SID shown as <user>: the output is meant to be pasted into a
    public repo, and the SID names this machine's account."""
    global _ME
    if _ME is None:
        _ME = token_facts()["user"]
    print(line.replace(_ME, "<user>"))


def sddl_of(handle):
    """The handle's owner, group, DACL and label as SDDL. SE_KERNEL_OBJECT, not SE_FILE_OBJECT: on a
    probe pipe with the DEFAULT descriptor the file-object path failed with err 87 for every request,
    the DACL alone included, while the kernel-object path read the same handle (2026-11-06)."""
    what = (OWNER_SECURITY_INFORMATION | GROUP_SECURITY_INFORMATION | DACL_SECURITY_INFORMATION
            | LABEL_SECURITY_INFORMATION)
    psd = ctypes.c_void_p()
    rc = adv.GetSecurityInfo(handle, SE_KERNEL_OBJECT, what, None, None, None, None, ctypes.byref(psd))
    if rc != 0:
        return "<GetSecurityInfo failed: %s>" % err_text(rc)
    s = wintypes.LPWSTR()
    adv.ConvertSecurityDescriptorToStringSecurityDescriptorW(psd, SDDL_REVISION_1, what, ctypes.byref(s), None)
    out = s.value
    k32.LocalFree(s)
    k32.LocalFree(psd)
    return out


class ProbePipe:
    """Listening instances of one probe pipe, served by this process. A client open of a freshly
    created instance succeeds before ConnectNamedPipe is ever called, so no thread is needed; each
    open is given its own instance so one attempt cannot leave the next with PIPE_BUSY."""

    def __init__(self, name, reject_remote, sddl):
        self.name = name
        self.mode = PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT
        if reject_remote:
            self.mode |= PIPE_REJECT_REMOTE_CLIENTS
        self.sa = None
        self._psd = None
        if sddl is not None:
            psd = ctypes.c_void_p()
            if not adv.ConvertStringSecurityDescriptorToSecurityDescriptorW(
                    sddl, SDDL_REVISION_1, ctypes.byref(psd), None):
                raise OSError(ctypes.get_last_error(), "bad SDDL " + sddl)
            self._psd = psd
            self.sa = SECURITY_ATTRIBUTES(ctypes.sizeof(SECURITY_ATTRIBUTES), psd, False)
        self.handles = []

    def listen(self):
        h = k32.CreateNamedPipeW("\\\\.\\pipe\\" + self.name, PIPE_ACCESS_DUPLEX, self.mode,
                                 PIPE_UNLIMITED_INSTANCES, 4096, 4096, 0,
                                 ctypes.byref(self.sa) if self.sa else None)
        if h == INVALID_HANDLE_VALUE:
            raise OSError(ctypes.get_last_error(), "CreateNamedPipeW " + self.name)
        self.handles.append(h)

    def close(self):
        for h in self.handles:
            k32.CloseHandle(h)
        self.handles = []
        if self._psd:
            k32.LocalFree(self._psd)
            self._psd = None


def open_client(path, access):
    """(ok, error code, handle). The handle is the caller's to close; nothing is ever written."""
    h = k32.CreateFileW(path, access, 0, None, OPEN_EXISTING, 0, None)
    if h == INVALID_HANDLE_VALUE:
        return False, ctypes.get_last_error(), None
    return True, 0, h


def attempt(path, access=GENERIC_READ | GENERIC_WRITE):
    ok, code, h = open_client(path, access)
    if h:
        k32.CloseHandle(h)
    return ok, code


def local_path(name):
    return "\\\\.\\pipe\\" + name


def remote_path(name):
    return "\\\\localhost\\pipe\\" + name


def probe(pipe, path):
    pipe.listen()
    return attempt(path)


def control(tag):
    """The SMB loopback must reach a pipe that does NOT reject remote clients, or every 'refused'
    below is vacuous. Returns (reachable, code)."""
    p = ProbePipe("UE5DumpBfx-probe-%d-%s" % (os.getpid(), tag), reject_remote=False, sddl=None)
    try:
        return probe(p, remote_path(p.name))
    finally:
        p.close()


def client_token(path_of, tag):
    """The token the PIPE sees for a client that opens it by `path_of(name)`: the server end
    impersonates the client and reads its thread token. This is what decides whether a
    deny-NETWORK ACE can fire -- an SMB open is not guaranteed to carry the NETWORK SID."""
    p = ProbePipe("UE5DumpBfx-probe-%d-%s" % (os.getpid(), tag), reject_remote=False, sddl=None)
    h = None
    try:
        p.listen()
        srv = p.handles[-1]
        ok, code, h = open_client(path_of(p.name), GENERIC_READ | GENERIC_WRITE)
        if not ok:
            return None, "open refused, err " + err_text(code)
        n = wintypes.DWORD()
        if not k32.WriteFile(h, b"x", 1, ctypes.byref(n), None):
            return None, "client write failed, err " + err_text(ctypes.get_last_error())
        if not k32.ConnectNamedPipe(srv, None) and ctypes.get_last_error() != ERROR_PIPE_CONNECTED:
            return None, "ConnectNamedPipe failed, err " + err_text(ctypes.get_last_error())
        buf = ctypes.create_string_buffer(1)
        if not k32.ReadFile(srv, buf, 1, ctypes.byref(n), None):
            return None, "server read failed, err " + err_text(ctypes.get_last_error())
        if not adv.ImpersonateNamedPipeClient(srv):
            return None, "ImpersonateNamedPipeClient failed, err " + err_text(ctypes.get_last_error())
        try:
            tok = wintypes.HANDLE()
            if not adv.OpenThreadToken(k32.GetCurrentThread(), TOKEN_QUERY, True, ctypes.byref(tok)):
                return None, "OpenThreadToken failed, err " + err_text(ctypes.get_last_error())
        finally:
            adv.RevertToSelf()
        try:
            return describe_token(tok), ""
        finally:
            k32.CloseHandle(tok)
    finally:
        if h:
            k32.CloseHandle(h)
        p.close()


def show(label, ok, code):
    say("    %-8s %s" % (label, "OPENED" if ok else "refused, err " + err_text(code)))


def cmd_matrix():
    facts = token_facts()
    say("this process: " + fmt_token(facts))
    candidate = "D:P(D;;GA;;;NU)(A;;GA;;;SY)(A;;GA;;;%s)" % facts["user"]

    reachable, code = control("ctl")
    if not reachable:
        say("VERDICT: NOT MEASURABLE -- the SMB loopback did not reach a no-flag, default-security probe "
              "pipe (err %s). Is LanmanServer running? Every remote 'refused' would be vacuous." % err_text(code))
        return 2
    say("control: a no-flag, default-security pipe is reachable over \\\\localhost\\pipe\\ -- remote opens "
          "below are real")

    # Which token each kind of client presents to the pipe. A deny-NETWORK ACE can only be judged
    # on a remote client whose token carries NETWORK; loopback may hand over the caller's own token.
    remote_tok = None
    for label, path_of, tag in (("local client", local_path, "tokl"), ("remote client", remote_path, "tokr")):
        t, why = client_token(path_of, tag)
        say("  %-14s as the pipe sees it: %s" % (label, fmt_token(t) if t else why))
        if path_of is remote_path:
            remote_tok = t
    deny_network_exercised = bool(remote_tok) and remote_tok["network"] != "absent"
    if not deny_network_exercised:
        say("  ⚠ the remote client's token carries no NETWORK SID, so the candidate's deny-NETWORK ACE is NOT "
              "exercised here: a remote open under it says nothing about a client from another machine")

    cases = [("default SD, no flag (Fern before)", False, None, True, True),
             ("default SD + PIPE_REJECT_REMOTE_CLIENTS", True, None, True, False),
             ("explicit DACL " + candidate, False, candidate, True,
              None if not deny_network_exercised else False),
             ("explicit DACL + PIPE_REJECT_REMOTE_CLIENTS", True, candidate, True, False)]
    failed = []
    for i, (label, flag, sddl, want_local, want_remote) in enumerate(cases):
        p = ProbePipe("UE5DumpBfx-probe-%d-%d" % (os.getpid(), i), flag, sddl)
        try:
            p.listen()
            ok, h_code, h = open_client(local_path(p.name), READ_CONTROL)
            sd = sddl_of(h) if ok else "<READ_CONTROL open refused: %s>" % err_text(h_code)
            if h:
                k32.CloseHandle(h)
            lok, lcode = probe(p, local_path(p.name))
            rok, rcode = probe(p, remote_path(p.name))
        finally:
            p.close()
        say("  %s" % label)
        say("    SDDL     %s" % sd)
        show("local", lok, lcode)
        show("remote", rok, rcode)
        if want_remote is None:
            say("    (remote: no expectation -- the deny-NETWORK ACE is not exercised by this client)")
        if lok != want_local:
            failed.append(label + ": local " + ("opened" if lok else "refused"))
        if want_remote is not None and rok != want_remote:
            failed.append(label + ": remote " + ("opened" if rok else "refused"))
    if failed:
        say("VERDICT: FAIL -- " + "; ".join(failed))
        return 1
    say("VERDICT: PASS -- every local open succeeded; PIPE_REJECT_REMOTE_CLIENTS refused every remote open"
          + ("" if deny_network_exercised else "; deny-NETWORK NOT exercised (see the warning above)"))
    return 0


def cmd_live():
    reachable, code = control("ctl")
    if not reachable:
        say("VERDICT: NOT MEASURABLE -- the SMB loopback did not reach this rig's own no-flag probe pipe "
              "(err %s); a refused remote open of the DLL's pipe would prove nothing." % err_text(code))
        return 2
    say("control: a no-flag probe pipe is reachable over \\\\localhost\\pipe\\")

    ok, code, h = open_client(local_path(LIVE_NAME), READ_CONTROL)
    if not ok:
        say("VERDICT: NOT MEASURABLE -- \\\\.\\pipe\\%s would not open (err %s). Is the DLL injected, and is "
              "the UI disconnected?" % (LIVE_NAME, err_text(code)))
        return 2
    say("live pipe SDDL: %s" % sddl_of(h))
    k32.CloseHandle(h)

    lok, lcode = attempt(local_path(LIVE_NAME))
    rok, rcode = attempt(remote_path(LIVE_NAME))
    say("  \\\\.\\pipe\\%s" % LIVE_NAME)
    show("local", lok, lcode)
    show("remote", rok, rcode)
    if not lok:
        say("VERDICT: FAIL -- a local read/write client was refused")
        return 1
    if rok:
        say("VERDICT: FAIL -- a REMOTE client opened the DLL's pipe (no PIPE_REJECT_REMOTE_CLIENTS?)")
        return 1
    say("VERDICT: PASS -- local client opened, remote client refused")
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("mode", choices=["matrix", "live"])
    a = ap.parse_args()
    return cmd_matrix() if a.mode == "matrix" else cmd_live()


if __name__ == "__main__":
    sys.exit(main())
