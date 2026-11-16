"""Live check (build 3720+): fly_set refuses a preset out of range, and changes nothing else in that request.

[CPP-HARDENING-2026-11-14] [[nodiscard]] batch 2 found fly_set dropping Dunste::SetPreset's refusal: a preset of 7
was ignored and the reply read as applied. The handler now applies the preset first, and a refused one answers an
error with nothing changed. This rig drives it through the pipe, with no UI and no Fly ON:

    py tools/verify/flyset_preset_live.py [--pid N]

  1. fly_set {preset: 7} answers ok:false, naming the range; fly_get_state's preset is what it was;
  2. fly_set {preset: 7, speed: <another>} answers ok:false and the speed is what it was too (nothing changed);
  3. a valid preset is taken (control), and the original preset and speed are put back.
"""

from __future__ import annotations

import argparse
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).parent))
from pipe_client import PipeClient  # noqa: E402


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--pid", type=int)
    a = ap.parse_args()

    failures: list[str] = []

    def check(ok: bool, what: str, detail: object = "") -> None:
        print(f"  {'ok  ' if ok else 'FAIL'}  {what}" + ("" if ok else f": {detail}"))
        if not ok:
            failures.append(what)

    c = PipeClient(pid=a.pid, timeout=30)
    c.connect()
    try:
        ptrs = c.request("get_pointers")
        print(f"game pid {ptrs.get('pid')}, DLL build {ptrs.get('build_number')}")
        st0 = c.request("fly_get_state")
        preset0, speed0 = st0.get("preset"), st0.get("speed")
        print(f"  start: preset {preset0}, speed {speed0}, active {st0.get('active')}")

        r = c.request("fly_set", preset=7)
        st = c.request("fly_get_state")
        check(not r.get("ok") and "preset must be" in (r.get("error") or "") and st.get("preset") == preset0,
              "fly_set preset 7 answers an error, and the preset is unchanged", (r, st.get("preset")))

        other = 4321.0 if speed0 != 4321.0 else 1234.0
        r = c.request("fly_set", preset=7, speed=other)
        st = c.request("fly_get_state")
        check(not r.get("ok") and st.get("speed") == speed0 and st.get("preset") == preset0,
              f"fly_set preset 7 with speed {other:.0f} answers an error, and neither changed", (r, st))

        valid = (int(preset0 or 0) + 1) % 3
        r = c.request("fly_set", preset=valid)
        check(bool(r.get("ok")) and r.get("preset") == valid, f"control: a valid preset ({valid}) is taken", r)
    finally:
        r = c.request("fly_set", preset=int(preset0 or 0), speed=float(speed0 or 1200.0))
        st = c.request("fly_get_state")
        check(st.get("preset") == preset0 and st.get("speed") == speed0, "the original preset and speed are back", st)
        c.close()
    print(f"{'PASS' if not failures else 'FAIL'} ({len(failures)} failed)")
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
