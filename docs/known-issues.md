# Known issues

Issues and limitations that last across releases. Each release's notes link here instead of repeating them.
How-to notes for particular games and setups (data outside `UObject`s, games that pause in the background, the EA
app) are in the README's [Notes](../README.md#notes).

## Limits of the engine

- Before UE 5.8, the engine itself passes null to a native function's by-value `TWeakObjectPtr` parameter, whatever
  the dumper sends. UE 5.8 fixed it.
- A function call can pass a lazy pointer, or an asset pointer on UE 4.11–4.17, only as null.

## Open issues

- Below 25 FPS, Live Funcs can tag a function that runs every frame as **Timer** and list it under **Periodic only**.
- Snapshots (experimental): on a heavily loaded PC, a snapshot may stop early inside a very large container, so two
  snapshots of an unchanged game can differ.

To report a new problem, see [Reporting a problem](../README.md#reporting-a-problem).
