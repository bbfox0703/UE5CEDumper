r"""[INVOKEINHERIT-2026-08-20] — an INHERITED UFunction cannot be invoked on a derived instance.

    py tools/verify/invoke_inherited_function.py

Exits 1 while the defect stands, so it doubles as the regression test.

THE MECHANISM. `Ubel::WalkFunctions(uclassAddr)` walks that UClass's **own** `UStruct::Children`
chain and never climbs `SuperStruct`. `UE5_FindFunctionByName` is a filter over that list, so it can
only ever resolve a function the class DECLARES. Three callers depend on it:

  * `Fern.cpp` `invoke_function` — and there is **no `func_addr` input** to bypass it, so every
    by-name pipe invoke is affected;
  * `Mimic.cpp` `HandleFindFunction` (`CMD_FIND_FUNCTION`) — the CE Lua by-name lookup;
  * `Frieren.cpp` `UE5_SetDebugCamera`, which resolves `ToggleDebugCamera` off the **live**
    CheatManager's class. A game with a derived CheatManager (`BP_CheatManager_C`) therefore gets
    `ToggleDebugCamera UFunction not found` — a shipped feature failing with a message that reads
    like the engine lacks the function.

NOT affected: `CMD_INVOKE` when the caller already has the `ufuncAddr` (CE scripts with a baked
address), because the mailbox takes the address directly and never re-resolves by name.

THE THREE-WAY DISCRIMINATOR — one variable, and both controls are needed:
  1  inherited function, derived instance    -> expected FAIL   (the defect)
  2  same function, an instance of the class that DECLARES it -> must PASS
     (rules out "the function is broken")
  3  a function the derived class declares ITSELF, on that same derived instance -> must PASS
     (rules out "that instance is broken")
Only if 2 and 3 pass does 1's failure mean what it looks like.
"""
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    # Flush: a backgrounded rig's stdout is a FILE, which Python block-buffers --
    # a long run then shows an EMPTY output file and looks hung.
    sys.stdout.flush()


def main():
    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()

        def live(cls):
            return next((i for i in (c.request("find_instances", class_name=cls,
                                               max_results=60).get("instances") or [])
                         # `find_instances` is a NAME SUBSTRING match without exact_match,
                         # so filter on the reported class.
                         if i.get("class") == cls
                         and not str(i.get("name", "")).startswith("Default__")), None)

        fl = (c.request("list_all_functions", limit=30000,
                        game_only=False).get("functions") or [])
        own_of = {}
        for f in fl:
            own_of.setdefault(f.get("class_name"), []).append(f.get("func_name"))

        base, derived, inherited_fn = "Actor", "StaticMeshActor", "SetActorHiddenInGame"
        b, d = live(base), live(derived)
        if not b or not d:
            say("SKIP: need a live %s AND a live %s on this host" % (base, derived))
            return 0
        own_derived = [n for n in own_of.get(derived, [])]
        if not own_derived:
            say("SKIP: %s declares no function of its own, so control 3 cannot run" % derived)
            return 0
        if inherited_fn not in own_of.get(base, []):
            say("SKIP: %s is not declared on %s on this host" % (inherited_fn, base))
            return 0

        say("%s declares %d functions; %s declares %d of its own (%s)"
            % (base, len(own_of.get(base, [])), derived, len(own_derived), own_derived[:3]))
        say("subjects: %s %s   |   %s %s" % (base, b["addr"], derived, d["addr"]))
        say("")

        def call(inst, fn):
            r = c.request("invoke_function", instance_addr=inst, func_name=fn,
                          parms_size=1, params_hex="00")
            dd = r.get("data", r)
            return bool(dd.get("ok")), (dd.get("error") or dd.get("message") or "")

        ok1, m1 = call(d["addr"], inherited_fn)
        ok2, m2 = call(b["addr"], inherited_fn)
        ok3, m3 = call(d["addr"], own_derived[0])
        say("  1  INHERITED on a DERIVED instance   %-22s -> ok=%-5s %s" % (inherited_fn, ok1, m1[:52]))
        say("  2  control: DECLARED on own class    %-22s -> ok=%-5s %s" % (inherited_fn, ok2, m2[:52]))
        say("  3  control: own function, same inst  %-22s -> ok=%-5s %s" % (own_derived[0], ok3, m3[:52]))

        say("")
        if not (ok2 and ok3):
            say("INCONCLUSIVE: a control failed, so case 1's failure is not attributable to "
                "inheritance. (2=%s, 3=%s)" % (ok2, ok3))
            return 1
        if ok1:
            say("PASS: the inherited function resolved — the defect is fixed.")
            return 0

        # Quantify, since "one function" understates it badly.
        ins = [i for i in (c.request("find_instances", class_name="Actor", max_results=400,
                                     exact_match=False).get("instances") or [])
               if not str(i.get("name", "")).startswith("Default__")]
        zero = [i for i in ins if not own_of.get(i.get("class"))]
        say("DEFECT STANDS [INVOKEINHERIT-2026-08-20]")
        say("  both controls pass, so the only variable is inherited-vs-declared.")
        say("  %s alone declares %d functions that NO derived instance can invoke."
            % (base, len(own_of.get(base, []))))
        say("  of %d live non-CDO objects, %d can invoke NOTHING AT ALL (their own class declares "
            "no function)." % (len(ins), len(zero)))
        for i in zero[:5]:
            say("     %-34s class=%s" % (i.get("name"), i.get("class")))
        return 1


if __name__ == "__main__":
    sys.exit(main())
