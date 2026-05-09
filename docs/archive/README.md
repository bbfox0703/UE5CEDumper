# Archived Documentation

These documents were moved here because they no longer reflect the current
state of the code, but are preserved for historical reference.

For up-to-date information, see the active docs in [`../`](..) — start with
[`../dev-log.md`](../dev-log.md) (running milestone log) and the docs index
in [`../../CLAUDE.md`](../../CLAUDE.md).

## Files

| File | Original purpose | Why archived |
|------|------------------|--------------|
| `UE5CEDumper-UX.md` | UX design spec dated 2026-03-10, written during early Avalonia panel design | The implementation has diverged: Property Search / Game Class / Class Structure routing fixes / Find Refs / OptionalProperty / etc. landed after this spec. The shipped UI is the source of truth; refer to the live AXAML / ViewModels for current behavior. |
| `ufunction-invoker-roadmap.md` | Implementation plan for the UFunction invoker feature | **Phase I (script generation) is fully shipped** — every checkbox in the roadmap is done. Phase II (in-process ProcessEvent dispatch) was superseded by `Stark.cpp` (GameThreadDispatch + MinHook ProcessEvent hook) and the `invoke_function` pipe command. The roadmap is a snapshot of the intent, not a plan for outstanding work. |
