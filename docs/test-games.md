# Recommended Test Games

> Moved from CLAUDE.md. A curated list of games used to validate UE5CEDumper across UE versions.

-----

| Game | UE Version | Notes |
|------|-----------|-------|
| EverSpace 2 | UE5.5 (PE: 505) | GNames via pointer-scan fallback. Stride 24, 1.16M objects. GWorld ✅ (build 1.0.0.27) |
| Titan Quest II | UE5.7 (PE: 507) | CasePreservingName + DynOff. Stride 16. 486,782 objects. GWorld ✅ via fallback ([GWorld]=0, UWorld found in GObjects) |
| OctoPath Traveler | UE4.22 (inferred) | 406060 objects. GNames via GNAM_CT3 ✅. GObjects via GOBJ_RE2 ✅ (Flat FFixedUObjectArray, validated by "Flat" preset). GWorld via GWLD_TQ_1 ✅. Codename "Kingship". Ghidra: GObjects RVA `0x29E5C20`, GNames RVA `0x29DCF08` (TNameEntryArray stride 0x4000). GOBJ_OT_1/OT_2 also added but untested (lower priority than RE2) |
| Final Fantasy VII Rebirth (FF7Re) | UE4.26 fork | Hash-prefixed FNameEntry (hdrOff=4, stride=4) + stride 24 — fully working. GWorld ✅ |
| Final Fantasy VII Remake Intergrade (FF7R) | UE4.18 fork | Flat FFixedUObjectArray ✅. UProperty fallback ✅. 315304 objects. GWorld ✅. Version: flat+UProperty → 418. Base GameInstance (9 fields, no BP subclass) |
| DQ I&II HD-2D Remake | UE5.05 (detected) | Stride 24, 128678 objects. GWorld ✅. SE HD-2D fork uses FFieldVariant=0x10 (UE5.0 layout) despite reporting UE505 — fixed by Step 6.5 inference (Name=0x28 from Next=0x20). BP_SantiagoGameInstance_C (64 fields) |
| DQ III HD-2D Remake | UE5.05 (detected) | 126022 objects. GWorld ✅. Same SE HD-2D fork layout as DQ I&II — FFieldVariant=0x10 inference fix applied |
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

**Working (17/20):** TQ2, EverSpace 2, Hogwarts Legacy, IDOLM@STER, Romancing SaGa 2, Tower of Mask, Ghostwire: Tokyo, Cat Island Petrichor Demo, Way of the Hunter 2 Demo, COMBAT PILOT Demo, OctoPath Traveler, FF7R, FF7Re, DQ I&II, DQ III, Lushfoil Photography Sim, Manor Lords

**Failing (GWorld not found or untested):** DQ XI S, Star Wars Jedi, Satisfactory

## Naming Convention

- **FF7Re** = Final Fantasy VII Rebirth (UE4.26 Square Enix fork)
- **FF7R** = Final Fantasy VII Remake Intergrade (UE4.18 Square Enix fork)
