# CLAUDE.md — dll/

Rules that apply to the C++ DLL under this folder; they load when you work here. The rules for the whole repository are in the [root CLAUDE.md](../CLAUDE.md).

## Rules

- **UE offsets**: All UObject/UStruct offsets must be dynamically verified via OffsetFinder, never hardcoded
- **Module naming (Frieren convention)**: every new C++ DLL module (a file with its own namespace) MUST take an unused name from the **Frieren roster** in [docs/naming-convention.md](../docs/naming-convention.md) — never a plain-English name — carry the header comment that doc specifies, and flip that name to 🟢 in the roster. The kept-English exceptions and the finished plain-English migrations are that doc's own tables
