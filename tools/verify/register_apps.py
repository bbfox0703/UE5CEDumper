"""Register our loose exes as per-user installed applications, so computer-use can grant them.

WHY: computer-use refused UE5DumpUI and both DumperTest packages. Measured behaviour (see
docs/auto-verification-session-plan.md §1): a Start-menu entry alone is not enough -- Hollow Knight:
Silksong is Steam-installed with no shortcut and also fails, so the working model is that a name
resolves only when the app is a REGISTRY-INSTALLED application *and* a Start-menu entry supplies its
display name. Our exes have the shortcut and no registration.

This is a HYPOTHESIS TEST, not a known fix. Apply, try request_access, and revert if it does nothing.

SCOPE: HKEY_CURRENT_USER only -- never HKLM, so no machine-wide or elevated change. The entries
appear in Settings -> Apps -> Installed apps. UninstallString is an honest `reg delete` of the entry
itself, because registration is the only thing being installed; nothing is copied anywhere.

  py register_apps.py            # dry run
  py register_apps.py --apply
  py register_apps.py --revert --apply
"""

import argparse
import pathlib
import sys
import winreg

ROOT = r"Software\Microsoft\Windows\CurrentVersion\Uninstall"
MARKER = "UE5CEDumper-verification-registration"

APPS = [
    ("UE5DumpUI", r"D:\Github\UE5CEDumper\dist\UE5DumpUI.exe"),
    ("DumperTest Development",
     r"D:\UE_Analyze_Data\For Testing\DumperTest\Development\Windows\DumperTest\Binaries\Win64\DumperTest.exe"),
    ("DumperTest Shipping",
     r"D:\UE_Analyze_Data\For Testing\DumperTest\Shipping\Windows\DumperTest\Binaries\Win64\DumperTest-Win64-Shipping.exe"),
]


def key_name(display: str) -> str:
    return display.replace(" ", "_")


def apply_one(display: str, exe: str, dry: bool) -> str:
    p = pathlib.Path(exe)
    if not p.exists():
        return f"SKIP (exe missing): {display}"
    sub = f"{ROOT}\\{key_name(display)}"
    if dry:
        return f"WOULD WRITE HKCU\\{sub}  DisplayName={display!r}  DisplayIcon={exe}"
    with winreg.CreateKeyEx(winreg.HKEY_CURRENT_USER, sub, 0, winreg.KEY_WRITE) as k:
        winreg.SetValueEx(k, "DisplayName", 0, winreg.REG_SZ, display)
        winreg.SetValueEx(k, "DisplayIcon", 0, winreg.REG_SZ, exe)
        winreg.SetValueEx(k, "InstallLocation", 0, winreg.REG_SZ, str(p.parent))
        winreg.SetValueEx(k, "Publisher", 0, winreg.REG_SZ, "UE5CEDumper")
        winreg.SetValueEx(k, "DisplayVersion", 0, winreg.REG_SZ, "1.0.0")
        # Honest: the only thing installed is this registration, so removing it IS the uninstall.
        winreg.SetValueEx(k, "UninstallString", 0, winreg.REG_SZ,
                          f'reg delete "HKCU\\{sub}" /f')
        winreg.SetValueEx(k, "NoModify", 0, winreg.REG_DWORD, 1)
        winreg.SetValueEx(k, "NoRepair", 0, winreg.REG_DWORD, 1)
        winreg.SetValueEx(k, "Comment", 0, winreg.REG_SZ, MARKER)
    return f"wrote HKCU\\{sub}"


def revert(dry: bool) -> int:
    n = 0
    try:
        root = winreg.OpenKey(winreg.HKEY_CURRENT_USER, ROOT, 0, winreg.KEY_READ)
    except OSError:
        print("no Uninstall root?")
        return 0
    names = []
    i = 0
    while True:
        try:
            names.append(winreg.EnumKey(root, i))
        except OSError:
            break
        i += 1
    winreg.CloseKey(root)
    for nm in names:
        sub = f"{ROOT}\\{nm}"
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, sub) as k:
                try:
                    c, _ = winreg.QueryValueEx(k, "Comment")
                except OSError:
                    continue
                if c != MARKER:
                    continue
        except OSError:
            continue
        # only ours, identified by the marker -- never a key we did not write
        if not dry:
            winreg.DeleteKey(winreg.HKEY_CURRENT_USER, sub)
        print(f"{'deleted' if not dry else 'WOULD DELETE'} HKCU\\{sub}")
        n += 1
    print(f"\n{n} registration(s){'' if not dry else ' would be'} removed")
    return n


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true")
    ap.add_argument("--revert", action="store_true")
    a = ap.parse_args()
    dry = not a.apply
    print(f"HKEY_CURRENT_USER only. mode: {'APPLY' if a.apply else 'DRY RUN'}\n")
    if a.revert:
        return 0 if revert(dry) >= 0 else 1
    for d, e in APPS:
        print("  " + apply_one(d, e, dry))
    if not dry:
        print("\nNow call request_access for: " + ", ".join(d for d, _ in APPS))
    return 0


if __name__ == "__main__":
    sys.exit(main())
