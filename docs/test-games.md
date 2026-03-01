# Recommended Test Games

> Moved from CLAUDE.md. A curated list of games used to validate UE5CEDumper across UE versions.

-----

| Game | UE Version | Notes |
|------|-----------|-------|
| EverSpace 2 | UE5.5 (PE: 505) | GNames via pointer-scan fallback. Stride 24, 1.16M objects. GWorld ✅ (build 1.0.0.27) |
| Titan Quest II | UE5.7 (PE: 507) | CasePreservingName + DynOff. Stride 16. 486,782 objects. GWorld ✅ via fallback ([GWorld]=0, UWorld found in GObjects) |
| OctoPath Traveler | UE4 (pre-4.25) | UE4 TNameEntryArray + Layout B + stride 16. Object Tree working. GWorld fails |
| Final Fantasy VII Rebirth (FF7Re) | UE4.26 fork | Hash-prefixed FNameEntry (hdrOff=4, stride=4) + stride 24 — fully working. GWorld fails |
| Final Fantasy VII Remake Intergrade (FF7R) | UE4.18 fork | Flat FFixedUObjectArray ✅ (build 1.0.0.27). UProperty fallback ✅. 165792 objects. GWorld fails. Version: flat+UProperty → 418 |
| DQ I&II HD-2D Remake | UE5.05 (detected) | Stride 24, 128678 objects — full pipeline working. GWorld fails. UE version may be incorrect (HD-2D lineage). CE pointer lookup works |
| DQ III HD-2D Remake | UE5.05 (detected) | Working (build 1.0.0.27). 126022 objects. GWorld fails. UE version may be incorrect |
| DQ XI S: Echoes of an Elusive Age | UE4.22 | Working (build 1.0.0.27). 70137 objects. GWorld fails |
| Tower of Mask | UE4.27 | Standard UE4 indie game — full pipeline confirmed working. Stride 24. GWorld ✅ (build 1.0.0.27) |
| Hogwarts Legacy | UE4.27 (PE: 427) | GNames via pointer-scan fallback. Stride 24, 379K objects. GWorld ✅ (build 1.0.0.27) |
| IDOLM@STER STARLIT SEASON | UE4.24 | Working. GWorld ✅ (build 1.0.0.27). CDO skip fix effective |
| Romancing SaGa 2 | UE4.27 | Working (build 1.0.0.27). GWorld ✅ |
| Star Wars Jedi: Fallen Order | UE4.21 | Working (build 1.0.0.25). 318022 objects. GWorld untested |
| Ghostwire: Tokyo | UE505 detected (possibly UE4) | Working (build 1.0.0.27). 254493 objects. GWorld ✅. UE version likely incorrect. RE-UE4SS has only AOB signatures, no version override |
| Lushfoil Photography Sim | UE5.6 (PE: 506) | NEW (build 1.0.0.40). All working. 58630 objects |
| Manor Lords | UE5.5 | NEW (build 1.0.0.40). All working |
| Satisfactory | UE5.3 (PE: 503) | NEW (build 1.0.0.40). Working. GWorld fails. 35776 objects |
| Cat Island Petrichor Demo | UE5.6 | Full working. GWorld ✅ |
| Way of the Hunter 2 Demo | UE5.7 | Full working. GWorld ✅ |
| COMBAT PILOT: CARRIER QUALIFICATION Demo | UE5.5 | Full working. GWorld ✅ |

-----

## GWorld Status Summary

**Working (10/20):** TQ2, EverSpace 2, Hogwarts Legacy, IDOLM@STER, Romancing SaGa 2, Tower of Mask, Ghostwire: Tokyo, Cat Island Petrichor Demo, Way of the Hunter 2 Demo, COMBAT PILOT Demo

**Failing:** FF7R, FF7Re, DQ XI S, DQ I&II, DQ III, OctoPath, Star Wars Jedi, Satisfactory — mostly Square Enix UE4 forks + some UE5.

## Naming Convention

- **FF7Re** = Final Fantasy VII Rebirth (UE4.26 Square Enix fork)
- **FF7R** = Final Fantasy VII Remake Intergrade (UE4.18 Square Enix fork)
