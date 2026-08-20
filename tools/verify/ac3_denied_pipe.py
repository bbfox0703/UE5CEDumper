r"""AC3 -- the WARN arm: a bridge pipe that EXISTS but refuses us.

    py tools/verify/ac3_denied_pipe.py [--seconds N]

WHY THIS RIG EXISTS
  `AobMakerBridgeService.ReconnectAsync` splits its failures on purpose (audit #5 AC3). Its own
  doc comment lists five outcomes that a bare `catch` had collapsed into one blank
  "AOBMaker not connected":

      OperationCanceledException  -> Debug "cancelled by caller"
      TimeoutException            -> Debug "no server on \\.\pipe\AOBMakerCEBridge within 2000 ms"
      anything else               -> WARN  "connect to '...' failed ({ExceptionTypeName}): {msg}"
      success                     -> Info  "AOBMaker CE Plugin bridge: available"

  A survey of all 107 UI `init-*.log` files found the Debug no-server line 211 times and the Info
  line 145 times, so those two arms are richly evidenced by ordinary use. **The WARN arm has never
  fired** -- and it is the one that carries the whole point of the fix, because it is what tells a
  user "CE is up, something ELSE is wrong" instead of the useless "start Cheat Engine".

  It cannot be staged by starting or stopping CE, and it cannot be staged by saturating the pipe:
  the try block holds only `ConnectAsync`, and a server at capacity makes ConnectAsync WAIT, which
  ends in TimeoutException -- the Debug arm again, not the Warn.

WHAT THIS DOES
  Creates a named pipe server at the bridge's exact name whose DACL denies everyone, so the UI's
  `ConnectAsync` fails with UnauthorizedAccessException rather than timing out. That is a genuine
  "a server EXISTS and we still could not reach it", which is exactly the branch's stated meaning.

  Nothing is installed and nothing is written to disk: the pipe is a kernel object owned by this
  process and disappears when the process exits.

  CHEAT ENGINE MUST NOT BE RUNNING. If it is, its plugin already owns that name and this rig will
  fail to create the pipe -- which it reports rather than silently degrading into a no-op.
"""
import argparse
import ctypes
import sys
import time
from ctypes import wintypes

PIPE_NAME = r"\\.\pipe\AOBMakerCEBridge"

# Deny GENERIC_ALL to Everyone (WD). A deny ACE first in the DACL wins over any allow.
SDDL = "D:(D;;GA;;;WD)(A;;GA;;;SY)"

PIPE_ACCESS_DUPLEX = 0x00000003
PIPE_TYPE_BYTE = 0x00000000
PIPE_WAIT = 0x00000000
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
adv = ctypes.WinDLL("advapi32", use_last_error=True)


class SECURITY_ATTRIBUTES(ctypes.Structure):
    _fields_ = [("nLength", wintypes.DWORD),
                ("lpSecurityDescriptor", ctypes.c_void_p),
                ("bInheritHandle", wintypes.BOOL)]


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    sys.stdout.flush()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seconds", type=int, default=90,
                    help="how long to hold the denying pipe open (default 90)")
    args = ap.parse_args()

    adv.ConvertStringSecurityDescriptorToSecurityDescriptorW.argtypes = [
        wintypes.LPCWSTR, wintypes.DWORD, ctypes.POINTER(ctypes.c_void_p),
        ctypes.POINTER(wintypes.ULONG)]
    adv.ConvertStringSecurityDescriptorToSecurityDescriptorW.restype = wintypes.BOOL

    psd = ctypes.c_void_p()
    size = wintypes.ULONG(0)
    if not adv.ConvertStringSecurityDescriptorToSecurityDescriptorW(
            SDDL, 1, ctypes.byref(psd), ctypes.byref(size)):
        say("FAIL: could not build the security descriptor (err %d)"
            % ctypes.get_last_error())
        return 1
    say("security descriptor built from SDDL %s (%d bytes)" % (SDDL, size.value))

    sa = SECURITY_ATTRIBUTES()
    sa.nLength = ctypes.sizeof(SECURITY_ATTRIBUTES)
    sa.lpSecurityDescriptor = psd
    sa.bInheritHandle = False

    k32.CreateNamedPipeW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                                     wintypes.DWORD, wintypes.DWORD, wintypes.DWORD,
                                     wintypes.DWORD, ctypes.POINTER(SECURITY_ATTRIBUTES)]
    k32.CreateNamedPipeW.restype = wintypes.HANDLE

    h = k32.CreateNamedPipeW(PIPE_NAME, PIPE_ACCESS_DUPLEX,
                             PIPE_TYPE_BYTE | PIPE_WAIT,
                             4, 4096, 4096, 0, ctypes.byref(sa))
    if h == INVALID_HANDLE_VALUE or not h:
        err = ctypes.get_last_error()
        say("FAIL: CreateNamedPipeW failed (err %d)%s" % (
            err, "  -- ERROR_ACCESS_DENIED usually means Cheat Engine is running and its "
                 "AOBMaker plugin already owns this name; close CE first" if err == 5 else ""))
        return 1

    say("holding a DENY-ALL pipe at %s for %d s" % (PIPE_NAME, args.seconds))
    say("")
    say("Now, in the UI, trigger a bridge probe (the AOBMaker refresh button next to the")
    say("Offline/Connected badge, or switch tabs), then grep the UI's init-0.log for:")
    say("    WARN .* AOBMaker bridge: connect to 'AOBMakerCEBridge' failed (")
    say("Expect the exception TYPE NAME in parentheses -- UnauthorizedAccessException.")
    say("A [DBUG] 'no server ... within 2000 ms' instead means the deny did not take effect")
    say("and the probe merely timed out; that is a FAILED staging, not a result.")
    try:
        time.sleep(args.seconds)
    except KeyboardInterrupt:
        pass
    finally:
        k32.CloseHandle(h)
        k32.LocalFree(psd)
    say("pipe released")
    return 0


if __name__ == "__main__":
    sys.exit(main())
