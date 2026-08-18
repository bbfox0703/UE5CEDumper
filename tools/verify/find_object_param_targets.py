"""Y1 target finder: an ObjectProperty that is a REAL PARAMETER (within num_parms),
on a class that has a live non-CDO instance."""
import sys, json
sys.path.insert(0, r"D:\Github\UE5CEDumper\tools\verify")
from pipe_client import PipeClient

OBJ = {"ObjectProperty", "ClassProperty", "SoftObjectProperty", "SoftClassProperty",
       "WeakObjectProperty", "LazyObjectProperty", "InterfaceProperty"}
SEED = ["BP_PlayerCharacter_C", "BP_EnemyCharacter_C", "EnemyCharacter", "FieldEnemyCharacter",
        "DropItemSpawner", "DropItemSettingComponent", "PlayerController",
        "CharacterMovementComponent", "GameplayAbilityRevive", "AttackCollisionData",
        "WBP_SaveLoadItemLife_C", "BP_RoomManager_C", "BP_ViewpointCamera_C"]

with PipeClient() as c:
    c.assert_build(); c.ensure_scanned()
    hits = []
    for name in SEED:
        try:
            fi = c.request("find_instances", class_name=name, max_results=8)
        except Exception:
            continue
        live = [i for i in fi.get("instances", [])
                if not str(i.get("name", "")).startswith("Default__")]
        if not live:
            print(f"{name}: CDO only / none"); continue
        ca, ia = live[0]["class_addr"], live[0]["addr"]
        try:
            r = c.request("walk_functions", addr=ca)
        except Exception as e:
            print(name, "walk failed", e); continue
        funcs = r.get("functions", [])
        n = 0
        for f in funcs:
            ps = f.get("params", [])
            np = f.get("num_parms", 0)
            for p in ps[:np]:                      # ONLY the real parameter block
                if p.get("type") in OBJ and not p.get("ret"):
                    hits.append(dict(cls=name, inst=ia, func=f.get("name"),
                                     param=p.get("name"), type=p.get("type"),
                                     off=p.get("offset"), num_parms=np,
                                     nparams_listed=len(ps)))
                    n += 1
        print(f"{name}: {len(funcs)} funcs, {n} with an OBJECT PARAM  (inst {ia})")
    print("\n=== QUALIFYING ===")
    for h in hits[:30]:
        print(json.dumps(h, ensure_ascii=False))
    print("total", len(hits))
