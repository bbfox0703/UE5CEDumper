# Dev Log

Append-only milestone history, newest first. Each entry references a
build number from `build_number.txt` so commits can be cross-referenced.
**Reading tip:** grep `^## ` for the index, then read the top (newest-first).
Entries for **builds ≤2168** are archived: builds 1800–2168 in
[archive/dev-log-2026-07-pre-build-2200.md](archive/dev-log-2026-07-pre-build-2200.md),
builds 1178–1799 in
[archive/dev-log-2026-06-pre-build-1800.md](archive/dev-log-2026-06-pre-build-1800.md),
builds 939–1177 in
[archive/dev-log-2026-06-pre-build-1180.md](archive/dev-log-2026-06-pre-build-1180.md),
builds 715–937 in
[archive/dev-log-2026-06-pre-build-940.md](archive/dev-log-2026-06-pre-build-940.md),
builds ≤696 in
[archive/dev-log-2026-05-pre-build-700.md](archive/dev-log-2026-05-pre-build-700.md).

> **Looking for current state?** See [roadmap.md](roadmap.md) for the
> capability matrix / per-game configuration / tested games, and
> [todo.md](todo.md) for the prioritized next-work list. This file
> records *what shipped* — the other two record *what works now* and
> *what's next*.

-----

## 2026-08-15 - The freeze dialog took 9999 for a byte and the game got 15 (build 2895)

**Audit #5 segment U4 finding Y9.**

### Why the dialog is the only place this can be caught

Everything downstream narrows in silence *and reports success*.
`ue5_freeze_helper.lua`'s byte writer is `writeByte(addr, math.floor(v) % 256)`;
`Solide::WriteNumeric` is `static_cast<uint8_t>(llround(value))`. Neither has a channel to say
"that did not fit" — the freeze is a background timer and the force-hold is a re-assert worker.
So `9999` on a `ByteProperty` became `15` in the game with nothing on screen, and the user ends up
debugging the game instead of the input.

### The check was asking the wrong question

`ValidateAndConvert` had only ever asked *does this fit a `long`/`ulong`*, which is the wrong
question for seven of its eight integer types. It now checks the **width**: `IntegerRange` is the
inclusive per-type table, and the error names the range *and* the value that would have landed —
*"uint8 holds 0 to 255 — 9999 would be written as 15"*. `WrapToRange` computes that number with the
same modular arithmetic the writers perform, and a test cross-checks it, so the number we quote
cannot drift from the number that lands.

Two more sites of the same defect were inside the same method: **`float` was validated as a
`double`**. The existing `NaN`/`Infinity` guard (B23) passed `1e300` straight through, and CE's
`writeFloat` / Solide's `WriteFloatAt` narrow it to `+inf`; `1e-300` collapses to `0`. Both are now
rejected for `float` and still accepted for `double`, asserted in both directions.

### The pre-fill is inseparable from the check

`SuggestedDefault` returned a flat `"9999"` for every integer type. Adding the range check alone
would have opened every `ByteProperty` dialog holding a value its own OK button rejects. It is now
derived from the same `IntegerRange` table (`min(9999, Max)`), and a test asserts every helper
type's suggestion survives `ValidateAndConvert` — a property that cannot drift, rather than a list
that can.

**One check covers two features**: `PropertySearchPanel.PromptForceValueAsync` reuses this dialog
for Solide's Force value precisely for its per-type validation, so Force gained the same guard.

**Negative controls, three, each isolating one claim.** Deleting the two integer range checks reds
exactly the 11 width tests, and the boundary-acceptance tests stay green — an inclusive bound must
not become an off-by-one that costs the top value of every field. Deleting the float narrowing check
reds exactly the 3 float tests. Reverting `SuggestedDefault` reds exactly the 4 default tests and
**only for `int8`/`uint8`**, which is the predicted shape since 9999 fits every wider type; that
control is also what demonstrates the interaction above. 3674 tests, 0 failed. `dist` is the 54.4 MB
AOT-trimmed binary, launch-verified, no `crash.log`.

**Found while fixing it, recorded not fixed:** `FreezeScriptGenerator.MapToHelperType` maps
`EnumProperty` → `int32` unconditionally, so freezing an `enum class : uint8` writes 4 bytes over 3
neighbours. That is the *third* site of a family this audit has already closed twice (W6 in the CE
XML export, Y2 in the invoke param buffer), and `FreezeScriptParams` carries no size for the
generator to consult — filed as **Y15**, M/low.

⬜ **Not verified in-game** — nobody has typed 9999 into a real `ByteProperty` freeze and watched the
new error appear instead of a 15.

-----

## 2026-08-15 - "Class not found" about the class you just clicked on (build 2888)

**Audit #5 segment U3 finding X2**, plus two twins the finding did not cite.

### The lookup should never have existed

Interesting Funcs and Console build their rows from `list_all_functions`, which supplies
`class_addr` **per row**. The three handlers that need a class address to call `walk_functions`
threw that away, issued `list_classes`, searched the returned page by NAME, and bailed with
`"Class {className} not found"` — about the class whose own row the user had just clicked.

`list_classes` returns at most `limit` rows (5,000) and `Aura::ListClasses` stops walking GObjects
the moment it has them, so on a large title a perfectly real class is simply not in the page. The
three events now carry `classAddr` and the common path issues **no pipe call at all** — it is both
the correct fix and one fewer full-GObjects walk per button click.

### The finding cited one site; there were three

`Console.RequestParameterInvoke` (the FIRE dialog for any exec taking parameters) and
`Console.RequestCopyBakedScript` are byte-for-byte the same body as the cited
`InterestingFunctions.RequestCopyBakedScript`, with the same message and the same hard `return`.
That is the fourth time this audit has found its own defect shape half-covered — §3b's
"grep for its siblings before closing a fix" rule is what caught it.

### The cap is detected at the source now

Per D5/F4's own lesson, the DLL is the only side that knows whether the walk reached the end.
`Aura::ClassListResult` gained `truncated` (set exactly as `SearchResultSet::truncated` is), and
`list_classes` emits it. The C# falls back to inferring it from a full page, so a **pre-2888 DLL
still produces the honest message** instead of silently degrading to "not found".

`ClassListResult.FindClassAddr` is now a pure, unit-testable lookup returning `ClassAddrLookup`.
Its `MissReason` is *"not in the class list — it was CAPPED at 5,000 rows, so the class may still
exist"* on a truncated walk, and plainly *"not found"* on a complete one — the second half matters
as much as the first, or the caveat becomes noise on every genuine miss.

Four further handoff sites (the ones whose wider "blast radius" framing this audit **refuted**) keep
their behaviour but were re-pointed at the same helper: their old text asserted *"Find Instances +
ListClasses both empty"*, which is a false statement about a capped list. **Game Class Filter** now
appends *"⚠ STOPPED at the 5,000-row cap — more classes exist"* — worth it because its
`total_classes` moves in lockstep with the results vector, so on a truncated walk that status line
printed the cap twice and read as a pool size.

**Negative controls, three, each isolating one claim.** Forcing `Truncated = false` in the parser
reds exactly the 2 truncation tests — and the "full walk is not truncated" test correctly stays
green, because it asserts an absence. Passing `""` instead of `row.ClassAddr` reds exactly the 3
address-carrying tests. Flattening `MissReason` to `"not found"` reds exactly the capped-wording
test. 3631 tests, 0 failed. `dist` is the 54.4 MB AOT-trimmed binary, launch-verified, no
`crash.log`.

⬜ **Not verified in-game** — nobody has clicked AA(B) on a class past the cap in a real title and
watched the script generate.

-----

## 2026-08-15 - Struct parameters: one path wrote four bytes of a vector, the other trusted a guessed layout (build 2881)

**Audit #5 segment U4 fixes Y6 and Y7.** Both are struct params, and both come down to the same
question — *what do you do when you cannot know the layout?* They answer it in opposite places, so
the fixes differ.

### Y6 — the CE form cannot edit a struct, so it must stop pretending to

The generated form has one edit box per param, and `StructProperty` has no scalar spelling. It fell
through `GetMailboxWriteStatement`'s size switch to `writeInteger`: **four bytes** of a 12-byte (UE4)
or 24-byte (UE5 LWC) `FVector`, taken from `math.floor(tonumber(text))`, with the remaining bytes left
zero. A garbage vector went into a live call and nothing said so.

The write is now skipped. The params buffer is already zero-filled, so the callee receives a
well-defined zeroed struct rather than a mangled one; the emitted script carries a
`-- <name>: struct (N B) left ZEROED` line; and the box keeps its place with its label changed to
`NOT EDITABLE - sent as zeroes`. Keeping the box is deliberate — `edits[i]` is indexed by param
position, so removing it would silently shift every later param's box.

### Y7 — the engine's size overrules the version guess

`KnownStructLayouts.GetLayout` is keyed on a **detected** UE version, and the dialog built its
sub-field editors from it without ever comparing it against the size the engine reported for that
param. When the two disagree the guess is wrong: a mis-detected version (LWC turns `FVector`'s floats
into doubles, doubling every offset) or a licensee fork with a modified struct — this project has met
both. Expanding on a wrong layout puts every sub-field editor at a wrong offset, so the numbers the
user types land in the wrong bytes of a live call with nothing on screen saying so.

The layout is now refused when it contradicts the engine, and the caller falls through to the
DLL-discovered fields, which came from the actual `UScriptStruct`.

**The rule was extracted to `ResolveTrustedLayout` so it could be tested at all.** It had been an
inline condition inside a View's control-building loop — unreachable from a test, which is the same
structural reason X1's missing field survived two builds. Five tests cover it now, including both
directions of the mismatch and the `engineSize <= 0` case, where nothing contradicts the guess and
keeping it is the right answer.

**Negative control:** reverting both turns 4 tests red — 2 for Y6 (one per struct size) and 2 for Y7
(one per direction of the mismatch). `Generate_StructParam_LabelsTheBoxAsInert` correctly stayed
green: the label and the write-skip are separate changes and only the latter was reverted.
3624 tests, 0 failed.

⬜ Not verified in-game: nobody has passed a struct param through either path against a live UFunction.

-----

## 2026-08-15 - The invoke script's mailbox lookup raised on exactly the case it was written to handle (build 2875)

**Audit #5 segment U4 fix Y8**, and the last bare `getAddress` the repo emitted.

`celua.txt` is unambiguous: `getAddress` *"returns the address of a symbol"*, while `getAddressSafe`
*"returns the address of a symbol, **or nil if not found**"*. The bare form **raises**. The block in
question exists precisely to handle a missing symbol — the DLL not being loaded — so the first call
aborted the whole chunk and took three things with it:

- the **module-prefixed fallback** on the very next line. Both spellings are mandatory, not a
  preference: which one resolves depends on how CE picked the module up (lessons-learned B33).
- the **diagnostic**, so instead of *"make sure UE5Dumper DLL is loaded (version.dll Proxy or CE
  inject)"* the user got CE's raw Lua error.
- the **cleanup timer**, so the memory record stayed **ticked** after a bail-out that applied
  nothing — against CLAUDE.md's rule that a bail-out which applied nothing must untick.

Both siblings already did this right: `BakedScriptGenerator` via `getAddressSafe`, `CeReadinessLua`
via `pcall(getAddress, …)`. Same sibling-divergence shape as W4/W6/X1.

### A pre-existing test broke, and it was the right kind of break

`CeMailboxBailoutTests.NoMailboxWriteEscapesTheIdleWait` uses the mailbox lookup as a *textual
anchor* to find where its window scan starts, and the rename lost it. The test had predicted this in
its own failure message — *"the anchor this scan starts from is gone"*. It now anchors on the
**symbol** instead of the lookup function, so it survives a change of lookup.

Worth distinguishing from the Y1 fix two commits ago, where an actual **assertion** had to change
because it pinned the defect. Here nothing about the test's subject — write ordering — moved; only
its anchor did.

**Negative control:** reverting to `getAddress` turns the two new tests red while
`CeMailboxBailoutTests` stays green, which is the proof the re-anchoring is genuinely
spelling-agnostic rather than retuned to the new spelling. One new test was hardened mid-flight for
the same reason: it had anchored on `getAddressSafe(`, so on revert it failed because `IndexOf`
returned −1 rather than because the untick had gone. 3615 tests, 0 failed.

-----

## 2026-08-15 - Two discovery panels reported a capped page as the whole pool (build 2870)

**Audit #5 segment U3 fix X1** — and it is the D5/F4 truncation fix finally reaching its second site.

The DLL has emitted per-query `truncated` and batch-level `aborted` since F4; the comment at
`Fern.cpp:2822-2825` names *"audit #5 D5/F4"* outright. The C# parsed them on the single-query path
and not on the batch twin, ~80 lines away in the same file, and `PropertySearchQueryEnvelope` had no
field to parse into.

The cost lands on the two panels that use the batch call. **Interesting Properties** sends all 51
`PropertyScoringTable.SeedQueries` at 200 rows each, so ordinary seeds — `Max`, `Count`, `Time`,
`Level`, `Hit` — cap routinely on any real game. The panel then reported *"N unique properties"* with
no caveat, the user filtered that page, did not find their field, and concluded it does not exist.
That is the exact report class the F4 fix was written to end. **Detect Player Stats** makes the same
call at the same limit and had the same blind spot.

Both now say so, mirroring the wording the single-query path has used since F4: *"⚠ N of 51 keywords
STOPPED at the 200-row cap (Max, Count, Time, …) — more matches exist"*, and a shorter form in Detect.

### The test hook is the durable part

The batch parse was inlined in an `async` method that needs a live pipe, so **nothing could reach
it** — which is precisely why a missing field survived two builds. It is now
`internal static ParseSearchPropertiesBatch(JsonObject)` plus a string-taking hook, so the wire
contract is testable without a pipe. Three tests: the per-query flag, the batch flag, and an older
DLL that omits both keys.

That third test matters more than it looks. It passes in **both** directions — before and after the
fix — because it asserts an *absence*: a pre-2818 DLL sends neither key and must not produce a
spurious cap warning on every scan. It is a guard against the fix, not a demonstration of it.

**Negative control:** removing the two parse lines turns the two flag tests red. 3613 tests, 0 failed.

⬜ Not verified in-game: nobody has run a real batch scan on a title where a seed keyword caps and
watched the strip appear.

-----

## 2026-08-15 - One dialog, two different calls: FIRE and Copy AA Script disagreed on what you typed (build 2866)

**Audit #5 segment U4 fixes Y2, Y3, Y4 and Y5.** One commit, because they are one defect wearing four
hats: `ParamBufferBuilder` — the FIRE path — re-implemented, more weakly, the parsing
`BakedScriptGenerator` already did correctly for the exported script.

| | typed | FIRE sent | exported script sent |
|---|---|---|---|
| **Y2** | `3` into a 1-byte enum | **nothing at all** (game got 0) | 3 |
| **Y3** | `true` | **0** | 1 |
| **Y4** | `1,5` | **15.0** | refused |
| **Y5** | `-1` into an `Int8` | **0** | −1 |

Y2 is the sharpest of the four: `EnumProperty` was grouped with `IntProperty` and the write gated on
`available >= 4`, so a 1-byte `enum class : uint8` param — the standard shape — failed the guard and
**was never written at all**. The game received whatever the zero-filled buffer held.

Y4 is the one with a paper trail: `TryParse(text, IFormatProvider, out _)` defaults to
`NumberStyles.Float | AllowThousands` **and accepts `NaN` / `Infinity`**. The baked path had already
discovered that, documented it (as B23) and guarded against it; the FIRE path had not, so `1,5` fired
as 15.0 and `NaN` reached the game as a real float.

### The fix is to share the parsers, not to write a fifth one

`BakedScriptGenerator.ParseBoolLiteral` and `TryParseHexOrDecimal` became `internal`, and the FIRE
path calls them. `EnumProperty` now routes through a new `WriteBySize` that the size-driven fallback
uses too, so those two cannot drift apart either.

Copying the logic across would have been the obvious move and the wrong one. This audit has now found
its *own* fixes applied to only some of the places that needed them three separate times — V2 (one
side of the wire), W4/W6 (some call sites of a helper), X1 (one of two single/batch twins). Sharing
the implementation is the only repair that does not depend on a future editor remembering.

**Negative control:** reverting `ParamBufferBuilder.cs` to its pre-fix state turns **11** tests red,
mapping to all four defects — 2 enum widths, 4 bool spellings (`true`/`TRUE`/`yes`/`on`; `1`/`0`/
`false`/`no` pass either way because the old byte parser handled digits), 4 float cases, 1 signed
byte. 3610 tests, 0 failed.

⬜ Not exercised against a live game: the bytes are unit-verified, but nobody has watched a UFunction
receive a 1-byte enum or a `true` from the FIRE button.

-----

## 2026-08-15 - The CE invoke form passed a null pointer for every address you typed into it (build 2862)

**Audit #5 segment U4 fix Y1 (HIGH).** The generated Cheat Engine invoke form detected a leading
`0x` and then handed the **still-prefixed** string to Lua's `tonumber(s, 16)`. Lua's base form
rejects any character that is not a digit of that base, so the `x` made it `nil` and the `or 0`
fallback wrote a **null pointer** into the params buffer. The DLL memcpys that straight into
`ProcessEvent`, so the UFunction was called with `nullptr` — an access violation for any callee that
dereferences it — while the script still reported `INVOKED OK`, or closed the Lua window silently
when `DEBUG == 0`.

Every `UObject*` / `FName` argument the user actually filled in was affected, because the app formats
every address as `0x` + uppercase hex (`Renge::AddrToStr`). Only the `'0x0'` default parsed
"correctly" — by arriving at the same 0 — which is why a smoke test with unmodified defaults always
passed and the capability never worked.

The irony is that the `else` branch was already right: plain `tonumber(s)` accepts `0x…` hex quite
happily. The special case written to handle hex was the only thing breaking hex.

### The fix

Strip the prefix before the base-16 parse, then fall back to decimal and finally to bare hex:

```lua
local s = edits[N].Text or ''; s = s:gsub('%s+','')
local h = s:match('^0[xX](%x+)$')
if h then return tonumber(h,16) or 0 end
return tonumber(s) or tonumber(s,16) or 0
```

**Decimal is tried before bare hex on purpose.** This branch also serves `NameProperty`, whose value
is an FName index a user may well type in decimal; reading `1234` as hex would silently change its
meaning. A test pins the ordering.

### Verified with three independent detectors

A claim about a runtime we do not control is not settled by reasoning about that runtime:

1. **Cheat Engine's own `lua53-64.dll`** (`_VERSION` = Lua 5.3), driven via ctypes with a stub
   `edits` table, evaluating the emitted expression verbatim. Before: `0x1F2A3B4C5D0` → **0**,
   `0X7FF6CD120000` → **0**, bare hex → **0**, and only a *decimal* address survived. After: every
   form resolves, `1234` stays 1234, junk → 0, and every result is a Lua **integer** (`math.type`),
   which `writeQword` requires.
2. **A standalone Lua 5.4.6 CLI** reproduced all ten inputs identically, which also shows the
   behaviour is stable across 5.3 → 5.4 — worth knowing if CE ever updates its bundled Lua.
3. **CE's bundled Lua source**, `Cheat Engine/lua53/lua53/src/lbaselib.c:48-65`, gives the mechanism:
   `int digit = isdigit(*s) ? *s - '0' : toupper(*s) - 'A' + 10; if (digit >= base) return NULL;` —
   for `'x'` that is `'X' - 'A' + 10 = 33`, and `33 >= 16`.

**A detail worth keeping:** the old code accidentally *worked* for padded input. `  0x40  ` made
`s:sub(1,2)` miss the prefix and fall into the working `else`, so the same field succeeded or
silently returned null depending on stray whitespace.

**Negative control:** reverting the fix turns all four new tests red. One of those tests was wrong on
the first attempt — it asserted the emitted script contains no `tonumber(s,16)` at all, which fails
the *correct* code, since the fix legitimately uses that form as the bare-hex fallback once the prefix
is known absent. Corrected to assert the old prefix-detection idiom (`s:sub(1,2)`) is gone, which is
the defect's precise signature. 3594 tests, 0 failed.

⬜ Not yet exercised inside Cheat Engine against a live game — filed in [todo.md](todo.md).

-----

## 2026-08-14 - A 1-byte enum array was exported as 4-byte CE records, so each one ate the next three elements (build 2857)

**Audit #5 segment U2 fix W6**, and with it the last of the segment's partial-application defects.

`MapInnerTypeToCeField` mapped `EnumProperty` to a hardcoded CE type of `"4 Bytes"`, but element
**addresses** are laid out with the DLL's real `ArrayElemSize` / `SetElemSize` — 1 for the standard
`enum class : uint8`. So a `TArray<ECharacterState>` produced records spaced one byte apart, each
reading four bytes: element 0's record swallowed elements 1–3, and every value in the pasted table
was wrong in a way that still looked like a plausible number.

### The fix is the signature, not the branch

The rule was already known — and already *written down* at the site that got it right:

> *"Enum width follows the sub-field's real byte size (a 1-byte enum must NOT be read as 4 bytes —
> that pulls in the next field's bytes)."*

It was applied at three of five call sites (struct sub-fields, map key, map value) via a caller-side
ternary, and the TArray and TSet element paths simply did not have it. Adding a fourth copy of the
ternary would have left the sixth call site free to forget it again, so instead
`MapInnerTypeToCeField` now **requires** the element size and applies the rule itself. All five sites
get it by construction, and the duplicated ternaries are gone.

Two mappings are deliberately **not** size-driven, and are now documented as such so a later reader
does not "fix" them: `NameProperty` stays 4 bytes because the record shows the FName
`ComparisonIndex` paired with a DropDownList of names rather than the whole 8- or 16-byte FName, and
the pointer flavours stay 8 regardless of stride.

### Negative control

Reverting the width to a hardcoded `"4 Bytes"` fails three tests: the two new byte-wide array/set
tests **and a pre-existing struct-sub-field test**. That last one is the useful part — it confirms
the refactor preserved the behaviour that test was already pinning, rather than merely satisfying
assertions written alongside the change. 3590 tests, 0 failed.

-----

## 2026-08-14 - The .usmap export declared one format and wrote another, and had never produced a readable file (build 2853)

**Audit #5 segment U2 fixes W1 (HIGH) and W7.** W7 came along because W1's deliverable does not work
without it: a name index past the end of the table corrupts the very file the version fix exists to
make openable.

This was never a regression. `git log -S` shows the `Version` constant has exactly one commit in the
file's history — its creation, `7f91295`, 2026-03-01 — and the string `bHasVersion` had never
appeared in any revision. The menu item shipped for 5½ months and could not once have written a
header a consumer would accept.

### The version now describes the bytes

The writer stamped `Version = 3` and emitted the version-0 body. Three independent desyncs followed,
and the first one lands before a single name is read:

- **`int32 bHasVersionInfo` was missing.** Any version ≥ `PackageVersioning` (1) must carry it. A
  reader consumes four bytes there, so it ate the low bytes of the payload size and threw.
- **Enum member counts were `uint8`** where ≥ `LargeEnums` (3) requires `uint16` — and the `uint8`
  also silently truncated any enum past 255 members.
- **ArrayDim was a hardcoded 2-byte `0`** where the format says one byte, so every struct's first
  property slid the stream by one, and a `0` additionally tells a reader to register no schema slots.

It now emits **v4 (ExplicitEnumValues)**, the version both vendored canonical writers produce, with
each member's `int64` value so an enum with gaps is no longer flattened to `0..N-1`. v4 rather than
"the cheapest v2" because the writer already emitted `uint16` name lengths (it can never go below 2)
and because the `uint16` count removes the 255-member truncation. The CEXT extensions block is
deliberately not written — Dumper-7 emits v4 without one, so it is optional.

A fourth fix, quieter but the same class: a struct's two counts are genuinely different numbers — the
first is the sum of every property's ArrayDim (a static array `Foo[4]` occupies four schema slots),
the second is how many property records follow. Both were `Fields.Count`.

### W7 — the name table can no longer be extended behind the file's back

The table's length is written once, up front, but the write pass resolved struct/enum references
through a `GetIndex` that fell through to `GetOrAdd` and **appended**, handing out an index past the
end of the table the file had already declared. `"None"` is now pre-registered, the write pass may
only use a non-appending `IndexOf`, and `NameTable.Seal()` makes any later `GetOrAdd` throw. The
invariant is enforced by the type instead of remembered by the caller.

### The real deliverable is the round-trip reader

`UsmapFile.Parse` in the tests reads the file the way a consumer does, at the widths the canonical
writers define, and asserts **the stream is fully consumed** and every name index is in range.

That is exactly what the old tests could not do. All five skipped a hardcoded 12-byte header and read
each field at the width the *writer* happened to use — so they encoded the bug rather than checking
it, and stayed green for 5½ months. Five were rewritten onto the reader and four added: a full
round-trip over every container shape, static-array slot counting, a 300-member enum, and an
unregistered struct name.

Negative controls were run one per sub-fix, each reverted alone: removing `bHasVersionInfo` fails 9
tests, restoring the `uint8` enum count fails 3, restoring the 2-byte ArrayDim fails 4, and
un-registering `"None"` fails 1. The reader is therefore checking the canonical layout rather than
mirroring the writer. 3587 tests, 0 failed.

⬜ Still unverified against a real consumer: the round-trip proves self-consistency at the vendored
writers' widths, but nobody has opened the output in FModel yet. Filed in [todo.md](todo.md).

-----

## 2026-08-14 - The SDK header re-declared everything it inherited, and gave each packed bool its own byte (build 2842)

**Audit #5 segment U2 fixes W2 (HIGH) and W3.** One commit: both live in the same emitter and the
same layout cursor, and W3's byte accounting is only observable once W2 stops flooding the struct
with inherited members.

### W2 — every derived class had the wrong `offsetof`

`Ubel::WalkClass` deliberately prepends the **entire** SuperStruct chain to its field list, and
nothing between the DLL and the SDK emitter filtered it out. The emitter's own comment said "skip
fields below the superclass boundary", but the code only moved the padding cursor — the loop still
emitted every inherited property. So `struct BP_Player_C : public AActor` re-declared all of AActor's
properties inside a struct that already inherits them. That compiles, which is why nobody noticed;
it just silently lays the struct out wrong from the first member onward.

The boundary is now **sent, not guessed**: `walk_class` gained `super_props_size`, read in
`Ubel::WalkClass` where `SuperClass` is already resolved. Nothing else in the reply implies it, so a
client can only heuristic its way there — the same situation as `map_stride` two builds ago, and the
same answer: *where the input is an engine fact the wire does not carry, send the number.* The old
first-field heuristic survives only as a fallback for an older DLL, and is now documented as one,
because it mis-splits silently when a derived class adds no properties of its own.

### W3 — N packed bools consumed N bytes instead of one

UE packs `uint8 bX:1` flags into a shared byte; they arrive at the same offset with Size 1. The
emitter wrote a whole `bool` for each and advanced the cursor by Size every time, so eight flags took
eight bytes instead of one. Padding could not compensate — it is only emitted when the next field's
offset is *ahead* of the cursor, and by then the cursor had overshot. Every later member, and the
trailing `// Size:` comment, was displaced.

They are now emitted as `uint8_t Name : 1` at their **true bit positions**, with unnamed fillers for
the bits UE left unused, so bit N in the game is bit N in the header. A native `bool` (FieldMask
`0xFF`) keeps its byte, and so does an unresolved mask (`0`) — an unknown must never be allowed to
rewrite the layout.

### Both duplicated loops are gone

The schema and live emitters carried byte-identical member-emit loops, and therefore carried both
defects. They now project into a single `SdkField` record and share one `EmitStructBody`. Fixing this
in two places is how it would have drifted apart again — the same lesson as the three stride mirrors.

### One pre-existing test was changed, deliberately

`GenerateClassHeader_BoolBitfield_EmitsComment` asserted `bool bHidden;` for a field with
`BoolFieldMask = 0x04` — a single-bit mask, i.e. precisely the packed bitfield W3 is about. Its name
and its second assertion show it was written for the *mask comment*; the declaration form was
incidental and pinned the defect. The mask assertion is untouched; the declaration assertion now
expects the bitfield. The arithmetic behind that call: with **one** bool the struct size is identical
either way, but the bit position was wrong (bit 0 instead of bit 2) — and with several bools the size
itself breaks, which the new eight-bool test demonstrates.

Negative controls were run **separately** so each fix is independently guarded: reverting only the
bitfield grouping turns exactly the two W3 tests red; reverting only the inherited-field filter turns
exactly the one W2 test red. Restored and re-confirmed: 3583 tests, 0 failed.

⬜ The unit tests drive the real emitters end-to-end, but the boundary *value* now comes from the DLL
and no headless check has yet read a real `super_props_size` off a live class — filed in
[todo.md](todo.md).

-----

## 2026-08-14 - One '&' in a game string could reject an entire pasted cheat table (build 2836)

**Audit #5 segment U2 fix W4.** `<Description>` text has been XML-escaped since audit #4 B3, because
a single `&` anywhere in a multi-thousand-entry export makes the document malformed and Cheat Engine
rejects **all** of it, with no indication which record was at fault. The `<DropDownList>` body — the
*other* place a game-derived string reaches the XML — was never covered.

Its content is built from live `FName` entries, enum member names and formatted container element
values, then interpolated raw. A stock `TArray<FName> Tags` holding a designer-typed `Bow & Arrow` is
enough. So is a `TMap<int32, FName>`, whose values are routed into a dropdown rather than a
description.

Escaping now happens inside `BuildDropDownContent`, which is the single choke point: all six call
sites — including the cached `_dropDownOwners` link path — build their body there, and both
`<DropDownList>` emit sites interpolate that body.

**The fix has two halves and the second is the one well-formedness cannot catch.** Metacharacters go
through the same `EscapeXmlContent` the Descriptions use. But the body is also **line-delimited**, so
a CR/LF inside a game string forges an extra dropdown row and shifts every following one *without*
making the document malformed. `CollapseLineBreaks` flattens those to spaces.

### Why five existing escaping tests did not catch it

`CeXmlEscapingTests` was written for B3 and every one of its five tests puts the game string in a map
**key** — which lands in `<Description>`. Nothing in the suite reached the dropdown path, so it passed
throughout. The four new tests go through a `TMap<int32,FName>` to reach it, and they live in that
same file on purpose: the file is the record of what "the export must survive arbitrary game text"
means, and it was incomplete.

Verified with a negative control rather than a green run: reverting the fix turns all four red, each
for its own reason — `&` gives *"error parsing EntityName"*, `<` gives *"Name cannot begin with ' '"*
(the parser started reading a tag), and the newline test fails **with no XmlException at all**, which
is precisely why that half needed its own handling. 3579 tests, 0 failed.

Still open in the same emitter and the same family: **W6**, where `CeWidthForSize` is bypassed by the
enum array/set path — the other partial-application defect U2 found.

-----

## 2026-08-14 - The map row that was editing its own key, and a formula the DLL had already fixed for itself (build 2830)

**Audit #5 segment U1 fixes V1 (the audit's only surviving HIGH), V2 and V5.** One commit, because
the three are one subsystem and not independently correct: V1's write address is computed *from*
V2's stride, so shipping V1 alone would have aimed a corrected offset off a wrong base.

### V1 — a TMap element row was inline-editable, and its address was the KEY

A `TPair` stores its key first, so a map element's base address *is* the key. `PopulateMapContainerFields`
built each row with `TypeName = MapValueType` — which makes any scalar-valued map pass
`FieldValueConverter.IsEditableType` — while setting `FieldAddress` to that element base. Every
consumer of `FieldAddress` on such a row acts on the row's declared type: the inline editor writes
there, "+CE" pushes it as a record typed from the value, the Hex button navigates there, the Address
column shows it. So editing a `TMap<FName,int32>` value wrote the user's four bytes over the FName
key — silently corrupting the map in a live game, and every later lookup of that entry missing.

The correction was already known to the file: `MapValueDrillOffset`'s doc comment states it outright,
and it was applied at exactly one call site (`NavigateToFieldAsync`'s `navOffset`) while the edit and
export consumers were not. Rows now carry the value's address.

**A second half the finding did not name:** the row also reported `Size = MapKeySize + MapValueSize`,
and `Size` reaches `TryConvert` as the **write length** — so a `TMap<int32, enum4>` would have written
8 bytes over a 4-byte value even once the address was right. A row now describes the value in all
three respects: type, address, size. `Offset` deliberately stays the element base, because
`MapValueDrillOffset` adds the value offset back when a drill-down builds a breadcrumb.

### V2 — three C# copies of a formula the DLL had already corrected

Build 2554's cluster ① fix (`5ef4c2b`) replaced `ComputeSetElementStride` with an alignment-aware
`Align(Align(elemSize, alignof(T)) + 8, alignof(T))` — **in the DLL only**. The same formula existed
in three C# files (`LiveWalkerViewModel`, `CeXmlExportService`, `CsxExportService`), each carrying a
doc comment claiming it mirrored the DLL. All three stayed on `Align(elemSize,4)+8`, so across five
map call sites the grid's key→value *text* was right (the DLL read it) while every map element
address the UI computed *itself* was 4+ bytes short past index 0: Address column, struct-drill target,
the breadcrumb offset feeding CE chains, and the CE-XML / CSX exports. TSet was unaffected — a bare
`elemSize` is already a multiple of `alignof(T)`, so the DLL's `elemAlign` default of 4 reproduces
the old behaviour exactly.

**The fix is not a fourth copy of the arithmetic.** The stride needs `alignof(Key)`/`alignof(Value)`,
which are engine facts that never cross the wire — a client can only guess. So the DLL now publishes
the stride it *actually used to read the elements* as additive wire fields `map_stride` / `set_stride`
(set at all four `Ubel.cpp` walk sites), and one new `Core/ContainerGeometry.cs` is the only
client-side consumer. All three mirrors are deleted; the old expression survives solely as
`ContainerGeometry.FallbackStride`, documented as correct only for `alignof(T) <= 4` and reached only
when the DLL supplied nothing. UI and DLL can no longer disagree, because there is one number.

### V5 — the multi-select clone dropped the geometry

`FilterContainerToElement` rebuilds a container field property-by-property for the "Copy CE Field(s)"
path and omitted `MapValueOffset`, so exporting *selected* map elements laid out differently from
exporting the same map whole. Fixed by carrying the geometry — which the V2 work made mandatory
anyway, since a dropped `MapStride` would have sent that one path back to the guess.

### Verified with a negative control, not just a green run

8 new tests in `ContainerGeometryTests` (3575 total, 0 failed). Both fixes were then reverted in
`ContainerGeometry.cs` and the suite re-run: **5 tests failed** — the helper, the seam
(`PopulateMapContainerFields`, made `internal` so a test can drive the real populate path) and the
clone — then the fix was restored and green re-confirmed. That ordering matters here: the pre-existing
`FieldValueConverterTests` passed in *both* directions, because the helper was never the broken part.
The bug lived in the caller that fed it an address, which is exactly the seam
[working-lessons.md](working-lessons.md) §1.3 warns about.

⬜ **In-game verification of the UI half is still owed** — see the entry in [todo.md](todo.md). The
DLL half already has `DumperTest` witnesses; what no headless pipe check can see is client-side
arithmetic, so the Address column / inline edit / CE record need a live look on a map whose pair
alignment is 8 and whose pair size is not already a multiple of 8.

-----

## 2026-08-14 - Two replies that reported work they had not done, and the feature that was empty all along (build 2818)

**Audit #5 D5 fixes F4 and F6 — and F8, which the F6 fix immediately exposed.**

### F4 — `search_properties` claimed a full sweep after stopping at the result cap

`Aura::SearchProperties` assigned `result.scannedObjects = GetCount()` **before** the walk, so a
search that stopped at the `maxResults` cap a few percent in still reported the whole pool. The panel
printed that as *"Found 200 properties in 3,412 classes (scanned 1,204,338 objects)"* — and a user
whose field was past the cap read it as proof the field does not exist. `PropertySearchResult` now
carries `truncated` (cap reached) and `aborted` (`Tot::Requested()` fired), `scannedObjects` is what
was actually walked, and the batch twin got the same treatment with **per-query** `truncated` — the
batch loop stops only when *every* query is full, so one seed keyword can be capped while another
swept everything. Measured on DumperTest: capped query `total 3 / classes 8 / scanned 105 /
truncated true`; full sweep `scanned 24445`, which is exactly what `get_object_count` reports.

> The first build had `walked = i` and a full sweep reported **24444** — one short. Caught by the
> same cross-check that made the original lie visible. A number compared only against itself is not
> checked at all.

### F6 — `walk_world` reported the page size as the level's actor count, and failed silently

`actor_total` and `truncated` are now emitted beside `actor_count`, and the two failure branches
(`Actors` unresolved; `ReadTArray` failed) set `data["error"]` instead of returning `actors: []` with
`ok:true` — which the handler already did for the two failures above them.

### F8 (new) — `ULevel` has no reflected `Actors` on this engine, so `walk_world` enumerates nothing

The F6 error string fired on DumperTest's stock ThirdPersonMap on its first run and named the branch:
`actorsOffset < 0`, not a failed read. Walking the live `ULevel` confirms it — **29 fields, 7
`ArrayProperty`, none named `Actors`** (`ModelComponents` @208, `NavDataChunks` @264,
`StreamingTextures` @320, `DestroyedReplicatedStaticActors` @768; the only `*Actor*` names are
`ActorCluster` and `LevelScriptActor`, both `ObjectProperty`). `walk_world` finds the actor array
purely by reflection, so on this engine "Load GWorld" renders a populated level as empty. `Actors` is
a real native member — the fix is a native-offset read, not more reflection. Filed, not fixed.

**Reproduced on a second, unrelated title within the hour.** Solarpunk (commercial, different engine
build), same day's session: `walk_world limit:500` → `{"actor_count":0,"actors":[],"ok":true,
"world_name":"MainLevel"}`. **2 of 2 games tested return nothing**, so this is not a DumperTest
artifact and may not be version-specific at all. Two captures is not a survey — what is established is
that it fails on both engines we have evidence for, not the size of the affected range.

> **This entry originally said "`walk_world` demonstrably works on other titles".** Nothing was checked
> before writing that; it was a conservative-sounding assumption, and the maintainer asking an adjacent
> question is what sent me to look at the one other capture on disk, which refutes it. **An "honest
> limit" written to sound cautious is still an unverified claim — and it draws less scrutiny than a
> bold one precisely because it sounds modest.**

> Worth keeping as method: **the cheapest new finding of the day came from making an existing silent
> failure speak**, not from another scan. When a reply cannot say what it failed to do, the defect
> underneath it is invisible too.

**Wire-only.** `PropertySearchPanel` does not yet bind `truncated`, and `LiveWalkerViewModel` does not
bind `actor_total` — the DLL stopped lying, the panels have not started telling. Separate pass.

-----

## 2026-08-14 - Every game exit paid 5 seconds to drain threads Windows had already killed (build 2813)

**Audit #5 segment D5 fixes F1 and F7 — the first two fixes from the D5 scan, both in `Fern`.**

### F1 — `~Fern()` ran the whole pipe teardown from `DLL_PROCESS_DETACH`

`Fern::Stop` now takes `bool graceful = true`, and `~Fern()` calls `Stop(false)`, which logs the entry
path and returns. Everything it skips — the cancel sweeps, the watch/scan joins, the **5-second**
connection drain, and the accept/monitor joins — is teardown that `Heiter.cpp:288-301` already refuses
to do at DETACH and `Routine.h:51-56` already documents as fatal for every *other* module's worker.
`Fern::Stop`'s explicit `join()` / `wait_for` calls were simply never added to that list, and
`s_pipeServer` being a namespace-scope static (`Frieren.cpp:92`) means the CRT runs them anyway.

Two things went wrong, and the second is worse than the first:

1. **`ExitProcess` has already terminated the connection threads**, so a dead thread can never erase
   itself from `m_conns` and the drain predicate is **unsatisfiable by construction** — the full 5 s
   budget burned on every exit that still had a client registered.
2. The body takes `m_connMutex`, Sein's log mutex and both Radar session mutexes **after their holders
   were killed**. MSDN is explicit that detach code taking a lock a terminated thread held deadlocks
   the process — i.e. a game that never closes.

**Measured on packaged DumperTest, one variable, graceful `WM_CLOSE` → `ExitProcess` → DETACH:**

| | Client at exit | Drain | Process exit |
|---|---|---|---|
| pre-fix, held open | `conns=1` | `TIMEOUT, 1 left (5030 ms, 49 re-asserts)` | 6,046 ms |
| pre-fix, disconnected first | `conns=0` | `satisfied, 0 left (0 ms)` | 1,105 ms |
| **post-fix, held open** | — | *skipped* | **1,185 ms** |

A connection open at exit now costs nothing. **`Stop-Process -Force` cannot see any of this** —
`TerminateProcess` skips DETACH entirely, so a forced kill exits fast and "proves" the bug is gone.

> **This also closes a question that had been answered wrongly four times.** todo.md's
> `Stop conn drain TIMEOUT` entry concluded the connection was *genuinely blocked in a synchronous
> `ReadFile`* (root cause: no `FILE_FLAG_OVERLAPPED`). That explains why `CancelIoEx` found nothing but
> **not** why `CancelSynchronousIo` — the correct API for a live thread blocked in synchronous I/O —
> also reported nothing-pending, 49 times. A terminated thread explains both. What found it was not a
> new diagnostic but asking **who calls `Stop`**: `UE5_Shutdown` logs `"Cleaning up..."` as its first
> statement, the shipped `.CT` only *probes* `UE5_StopPipeServer` before calling `UE5_Shutdown` alone,
> and `grep -rn "Cleaning up"` over the whole Logs tree returns **zero** — so no capture on disk was
> ever the CE-untick repro the entry was written around. Both structural fixes proposed there (close
> the handle from `Stop`; make the pipe overlapped) act on a *live* thread's `ReadFile` and would not
> have helped.

### F7 — an error string that named an execution which never happened

`invoke_function` rendered result `-7` as *"(hook not active, direct call used)"*. `-7` is produced
**only** by `Stark::EnqueueInvoke`'s inactive-hook guard or by `Stark::Shutdown` draining the queue —
neither reaches ProcessEvent by any route; the direct fallback lives on the other side of
`if (Stark::IsHookActive())` and returns 0/-2/-3/-4/-8, never -7. Now reads *"game-thread hook is down
— the invoke was never dispatched; re-enable the script and retry"*, and `-8` gained the mapping it
never had (it used to fall through as a bare number).

**Not verified: the graceful path.** It is unchanged by construction — the fix is an early return in
front of it — but reaching `Stop(graceful=true)` needs a CE Disable, so it is filed in todo.md's
pending register rather than claimed. Note also that `-Target Test` says nothing here: **no test target
compiles `Fern.cpp`.**

-----

## 2026-08-12 - The leftover-proxy refusal nobody could see, and the log we could not read (build 2801/2804)

Finished B13/B41's end-to-end half — *watch a leftover-proxy row actually carry the no-Recycle-Bin
refusal* — and it took two failed runs to get one honest measurement.

**The rig.** Previous attempts used a synthetic UE game on a scratch volume and stalled on "the scan
does not detect it". That turned out to be **a miscount, not a bug** — `Generic scan found 8 UE
game(s)` already included it and the row was on screen the whole time. Replacing it with a real game
copied wholesale (Steam's *Light Maze*, 215 MB) removed the entire question: `Found 9 UE game(s)`,
deploy `version.dll`, delete everything Steam would own, and the sole survivor is exactly the
leftover-after-uninstall shape.

**Defect ① — predicted, and confirmed.** `PlanPrune` computes `OrphanVerdict.NotOnFixedDrive` with a
carefully worded refusal, and the scan's surface filter then threw it away, keeping only
`Deletable`/`FileOnly`. The user was told *"No leftover proxy DLLs found (23 folder(s) examined)"*
while our DLL sat on a volume where deleting it by hand is unrecoverable. The feature's own PASS
criterion was unobservable as shipped.

**Defect ② — found only by running the negative control.** Flipping the bin back ON was supposed to
be a formality; it also returned 0 rows, which is what revealed that the first run had measured
nothing. `CandidatesFromLogs` read our own `view-*.log` with `File.ReadLines`, which opens
`FileShare.Read` and **cannot open the live `view-0.log` our own logger holds**. The per-file `catch`
swallowed the sharing violation, so the current session's whole deploy log contributed zero
candidates — the 22 examined came from an *archived* log. Practical cost: **deploy a proxy, uninstall
the game, press Find leftovers in the same session → nothing found**, until a restart rotates the
log. `SteamShapeScan` masks this for Steam titles, so it bit exactly the non-Steam locations the log
sources exist to cover.

**Fixes.** `OrphanVerdictRules.{IsActionable,ShouldSurface}` — one pure pair beside the enum,
replacing three hand-written copies of two *different* predicates, with `NotOnFixedDrive` now
surfaced but never actionable. `ReadLinesShared` (`FileShare.ReadWrite | FileShare.Delete`) for the
log sweep and for the Steam `.acf` read, where a manifest Steam holds open made
`TryReadAcfInstallDir` report unreadable and silently refuse every Steam candidate. The recycler
question moved **below** `ClassifyLeaf` so a no-bin volume cannot manufacture a refusal for a folder
holding nothing of ours, and it now carries the file list so the row can name the file. Plus honesty
work: a blocked row authorises nothing, the report says *"NOT removable"* rather than *"to be
recycled"*, and the status line counts blocked rows separately.

**Post-fix, on the AOT/trimmed build 2804:** bin OFF → 23 examined, 1 row carrying the refusal, its
checkbox disabled and `Delete checked (0)` greyed; bin ON → same folder becomes
`Recycle version.dll — folders left in place`, tickable, `Delete checked (1)`. One registry DWORD
apart, same process. 22 new tests, each with the negative control that makes it mean something —
including one asserting `File.ReadLines` **throws** on the same handle first, or it would be
asserting nothing. 3567 C# green.

-----

## 2026-08-11 - `executeCodeEx`'s wait cannot be pumped, and one call site asked it to wait forever (build 2792)

Started as a forum question about the mailbox `sleep()` loop — *"why not wrap the sleep in a
`createThread()` or use `createTimer()`, that would prevent CE's GUI from becoming unresponsive?"*
The answer for **that** loop is no change: it already pumps with `processMessagesPaintOnly` (~0.00 ms,
and deliberately re-entrancy-proof because it dispatches no mouse or keyboard), and it has to stay
synchronous because the AA script needs the return value before it can decide whether to untick the
record — going async would re-open the exact "the checkbox lied" defect the build-2743 sweep closed.

But reading CE `7.5-195` to answer it found that **`executeCodeEx` is a different animal**, and one
of our call sites was standing in the worst possible spot.

**The CE facts** (now owned by [ce-plugin-sdk-notes.md](ce-plugin-sdk-notes.md) §13):

- `executeCodeEx` (LuaHandler.pas:11922) is a shim over `executeMethod` (:11417), whose wait is
  `CreateRemoteThread` (:11847) then `WaitForSingleObject(thread, timeout)` (:11861) — **on the
  calling thread, with nothing on that path pumping messages.** From an AA `{$lua}` block the
  calling thread *is* CE's GUI thread, so the timeout is a ceiling on GUI-freeze time and a
  Lua-side pump structurally cannot reach it (it only runs after the call returns). `executeCode`
  (:11943) carries a duplicate wait at :12056.
- **A `nil` timeout means `INFINITE`** (:11504-11505), not "use a default".
- Failure returns **`nil` plus a reason string**, six distinct ones (`'Execution timeout'`,
  `'Failure launching thread'`, `'Wait failure'`, `'Failure reading the result address'`, two arity
  messages) that point at six different problems.
- A **timeout does not reclaim**: `dontfree := true` on the `WAIT_TIMEOUT` branch (:11880) makes the
  `finally` (:11907) skip `VirtualFreeEx` on the stub, the result address and every string
  allocation — permanently, in the *target* process. Deliberate (the remote thread may still be
  running), but it means "just shorten the timeout" is not a free move. The same flag on
  `timeout=0` (:11851) is the source-level mechanism behind celua.txt's leak note.

**What was wrong here.** [`scripts/ue5_dissect.lua`](../scripts/ue5_dissect.lua) passed
`executeCodeEx(1, nil, fn, ...)` — infinite, unpumped, **once per field** of a class walk. A
suspended target or a faulting stub froze CE with no UI-level recovery short of killing the
process. It was also the one `executeCodeEx` call site with no coverage in
`CeExecuteCodeExArityTests`, which is why it survived the audit #4 B1 sweep that fixed the others.

**And a defect we had already fixed once, at a new address.** All three call sites — the dissect
helper, `CeLuaHygiene.AppendCallDllHelper`, and `UE5CEDumper.CT`'s `ue5_callDLL` — checked only
`result ~= nil` and then printed a message they had guessed. The `.CT`'s was
`"executeCodeEx returned nil for %s — process alive?"`, and **four of the six reasons occur with a
perfectly healthy process**. This is the same shape as the mailbox-timeout defect build 2743 fixed
("the message blamed the wrong thing, and the mailbox already held the answer") — the diagnosis was
sitting in a return value nobody read.

**Shipped:** finite `DLL_CALL_TIMEOUT_MS = 5000` in the dissect helper; `local ret, why = …` at all
three sites with CE's reason surfaced; `AppendCallDllHelper` split into two branches because
`pcall`'s second return is the Lua error on a raise and the callee's RAX on a clean run — one
`if not okCall or ret == nil` could not tell them apart. `CeExecuteCodeExArityTests` gained a
dissect test (finite timeout + reason capture) and reason-capture assertions for the other two.
3520 C# tests green.

> **A note on the test that caught itself.** The new `.CT` assertion
> `DoesNotContain("process alive?")` failed on first run — against the *comment* explaining why that
> string was removed. It now checks code lines only. Worth remembering when pinning the absence of a
> string that the fix's own documentation will quote.

-----

## 2026-08-10 - A generous `gc.MaxObjectsInGame` made us enumerate 11.7% of GObjects and call it a full scan (build 2782)

Reported as "Value Search can't find a Native-C value": a live `DsClientLocalPlayer` field at
`+0xAC0` = 9338 was visible in Live Walker's Guess-What but no scan would return it. Native-C was
not at fault, and neither was any scan — **the object was never enumerated**, and neither was any
other runtime object in the process.

`find_by_address` settled it in one call: the live object resolved `index: -1, match_kind:
"backward"` (recovered by walking back to a UObject header) while its CDO resolved `index: 36968,
match_kind: "exact"`. Every instance any tool could find was a `Default__*`. `get_object_count`
returned **37,099** — the same 37,099 it returned 78 minutes and one map earlier. A live
`FUObjectArray` never holds still; that number was not a count of anything current.

Reading the array header confirmed it. `NumElements` (+0x24) was **317,810** and climbing (260,799
at scan time, `NumChunks` 4 → 5); 37,099 is **`ObjLastNonGCIndex`** (+0x04) — the frozen high-water
mark of the startup disregard-for-GC set, which is precisely why everything enumerated was a CDO or
a startup asset.

**Two defects, stacked, either one alone sufficient to cause it.**

1. **The MaxElements sanity cap was a live-object-count intuition applied to a capacity field.**
   `maxElements > 0x800000` (8,388,608) rejected the *correct* preset, because MaxElements is
   `MaxChunks * 64K` and tracks the title's `gc.MaxObjectsInGame` ceiling, not its population:
   161 chunks → **10,551,296**. Every strict preset then failed and both validators fell to their
   relaxed tier. Raised to 33.5M (`kMaxElementsCeiling` / `kStrictMaxCeiling`), still an order of
   magnitude under the 233M that MindsEye's pointer-half produced — which is what the bound is for.
2. **In the relaxed tier, row B and row E are indistinguishable on the only structural check.**
   Both read Objects from **+0x10**, so the pointer + cyclic-class-chain checks cannot separate
   them, and B is listed first. On a UE5-Extended array B's `numOff` (+0x04) lands on
   `ObjLastNonGCIndex` — in range, plausible, and wrong forever. The discriminator is B's own chunk
   counts (`MaxChunks@+0x08` / `NumChunks@+0x0C`): real on a genuine B layout, but on UE5-Extended
   +0x0C is `OpenForDisregardForGC`, a bool reading 0 once startup finishes. `Genau` now runs the
   relaxed table in two passes (chunk-consistent first, then the historical pass verbatim);
   `Aura`'s hand-written B block gained the same guard.

Both fixes are strictly **widening or re-ordering** — nothing that resolves today stops resolving.
Deliberately *not* a reorder of B and E: putting E first would let E's +0x20/+0x24 reads steal a
real Back4Blood layout, trading one silent misread for another.

**The shape worth remembering:** a validator that rejects the right answer does not fail loudly —
it falls through to a wrong answer that looks healthy. `relaxed B, Num=37099` was logged as a
success and copied into [test-games.md](test-games.md) as a normal result. The tell was never in
the validation log; it was that a live counter never moved.

**Verified in-game the same day (build 2786).** `ValidateGObjects: Valid at 0x7FF62529F8C0 (preset
Default, Num=266614, Max=10551296, …)` + `Layout 'Default' detected (strict, preset 1/5)` — the
**strict** tier accepting `Max=10551296` is impossible under the old 8.4M cap, so that line alone
proves the ceiling fix. Live count **266,614 at init → 274,900** later in the session, and the
original repro returns `DsClientLocalPlayer.<raw@0xAC0>` = 9338.

One honest gap, recorded in [todo.md](todo.md): that run resolved the **`ObjObjects`** anchor
(`GOBJ_V13` → `…F8C0`), not the `FUObjectArray` **base** (`GOBJ_ES53_1` → `…F8B0`) the broken run
used — and at the ObjObjects anchor even the old relaxed `A/C` row reads the right `NumElements`.
So defect 1 is proven and defect 2 is verified only by construction. That the same binary, at the
same module base, resolves a different pattern and a different anchor between two runs is itself
worth knowing: which preset row has to be correct is not a fixed property of a title.

-----

## 2026-08-10 - Proxy deploy stopped refusing the proxy it recommends, and stopped hiding why it failed (build 2779)

A newly-installed game (DragonSword Awakening) reported `Deployed: 0 success, 1 failed` with a row
reading **NotDeployed and a blank Error**. Nothing in the panel said why. Two independent defects,
one of which had been silently wrong since it was written.

**1. The import-table refusal was built on a false premise, and it was wrong.**
`DeployAsync` hard-refused any proxy flavour absent from the .exe import table, on the reasoning that
it "would never load". The refusal's own worked example was Octopath Traveler, described in the code
as importing winmm and dxgi *but not version.dll*. **Octopath's import directory names VERSION.dll**
— the premise was a misreading. (Octopath's real quirk is unrelated and already documented elsewhere
in the tree: it instant-exits under the *dxgi* proxy, because dxgi is imported early enough that the
game calls it before the CRT is initialised.)

Measured before changing anything — 21 Steam UE games on the maintainer's machine:

| | |
|---|---|
| games running a **working `version.dll` proxy with NO static import** | **11 of 21** |
| among them | DQ7R, P3R, Stray, Palworld, Manor Lords, Ghostwire Tokyo, both DQ HD-2D remakes, Lushfoil, Arms of God, The Artisan of Glimmith |
| strongest single case | **DQ7R** — the DLL itself reported the proxy load (the "confirmed working" suggestion) |
| games importing dxgi | 21 of 21 — which is why escalating to dxgi on "version not imported" is not a fix |
| our four names in `KnownDLLs` | **none** — the one condition that would make exe-directory proxying impossible |

The mechanism the refusal missed: `version.dll` / `dinput8.dll` arrive via a **run-time
`LoadLibrary`**, and the default search order reaches the .exe directory before System32. An import
proves a proxy WILL load; its absence proves nothing. The refusal therefore rejected `version.dll` —
the broadest-compatible flavour, and the one the Suggested column recommends — on most games,
telling the user to "try the Suggested column" that pointed straight back at it.

Now advisory-only (`ProxyImportAnalyzer.DescribeLoadRisk`). Nothing blocks a deploy. The real lesson
survives: a proxy that genuinely cannot load fails **silently and totally** (zero log, reads exactly
like "nothing happened"), so the one case still worth reporting — a static-import-only flavour
(dxgi/winmm) the game never names — rides along with the successful deploy as a note that names the
alternatives. `Recommend()` was deliberately **not** changed: with 21/21 importing dxgi, escalating
on that signal would trade a working default for Octopath's crash.

**2. A post-operation refresh erased the operation's own result.**
`DeploySelectedAsync` / `UndeploySelectedAsync` / `UpdateAllAsync` each ended with
`RefreshDeployStatusAsync`, which recomputes purely from disk. For a game the deploy just failed on,
disk says "file absent" → `ClassifyAbsentSelected` honestly returns `(NotDeployed, null)`, overwriting
both the failure status and its reason. That is the blank Error column: the reason existed only in
the log. This hid **every** deploy failure mode, not just this one. `RefreshDeployStatusAsync` now
takes a `preserveBinariesDirs` set (`ShouldApplyRefresh`, pure + tested, case-insensitive because it
compares Windows paths); a standalone Refresh still passes null and recomputes everything.

11 new tests (3519 C# green). All 11 were negative-controlled: reverting each fix fails exactly the
tests that cover it, and no others.

**In-game outcome, same day — and it cuts both ways.** On DragonSword Awakening itself the
`version.dll` proxy **does not load**: zero log folder, the silent-total-failure signature. `dxgi.dll`
works, and the game then runs perfectly (UE 5.4, GObjects/GNames/GWorld/Sparse/**&GEngine slot** all
resolved, ProcessEvent hook validated at 9 540 fires/1500 ms, no errors — see
[test-games.md](test-games.md)). So the old refusal's *verdict* on this one game was right, while its
*rule* was still wrong: applied consistently that rule also rejects the 11 games where version.dll is
the only thing that works, and it cannot tell the two groups apart, because the import table does not
distinguish them. Both groups import dxgi+winmm and not version. What separates them is whether
anything in the process ever calls `LoadLibrary("version.dll")` by bare name — invisible to static
analysis. Hence advisory, not refusal: the user tries, and **"no log folder appeared" is the
diagnostic**. That symptom is now documented in both READMEs.

-----

## 2026-08-06 - The CE Lua scripts and the DLL now agree on a contract, and CI keeps them honest (build 2747)

A generated `.CT` and the DLL had **no version relationship at all**. A table saved months ago
against a DLL whose mailbox has since moved writes to the old offsets and gets silence or garbage;
the reverse - a new table against an old DLL - is the same, and neither says anything.

**The design question was which axis to version, and the obvious answer is wrong.** Versioning on
the BUILD number would condemn every saved table on every release: a v1800 script is perfectly valid
against a v1900 DLL if nothing it depends on moved. What has to match is the **contract** - the
`MailboxData` offsets, the `Cmd` values, the per-command op values, the status/result meanings - and
that changes rarely. The repo already had this exact pattern for a different problem:
`Genau::kVersionDetectLogicRev` bumps only when the detection LOGIC changes, not per build.

**Two numbers, because the answer is a RANGE.** The DLL publishes `MAILBOX_CONTRACT` and
`MAILBOX_CONTRACT_MIN`; a script bakes the contract it was generated against and is compatible when
`MIN <= script <= CONTRACT`. That is what lets a months-old table survive hundreds of builds, and it
separates the two failure directions, which need opposite advice:

| condition | meaning | what the script says |
|---|---|---|
| `script < MIN` | the script is too old | regenerate the `.CT` |
| `script > CONTRACT` | **the DLL** is too old | update `UE5Dumper.dll` |

That second row does not exist today at all - a new table against an old DLL is silent corruption.

**Published as its own exported symbol, `g_mailboxContract`, not as a field of the mailbox.** Reading
the layout version out of the struct whose layout is in question is circular, and the check has to
run BEFORE the first write: if the layout moved, writing first scribbles on whatever now occupies
those offsets. It carries a magic (`'UE5C'`) because the symbol resolving to a **stale address** is
not hypothetical - that is exactly what a 2026-08-06 session showed CE doing, holding a mailbox
address the DLL no longer owned while the script wrote into it for ~155 s.

Emitted by one shared `CeLuaHygiene.AppendContractCheck` into all 11 generated toggle/momentary
scripts, and wired into both standalone helpers at `findMailbox` - the single place every path
obtains the mailbox. The bail-out unticks the record **even for momentary scripts**, which the
timeout path does not: their deferred untick timer is created further down the block, so at
contract-check time a bare `return` would reach nothing.

**The hard part is not the check, it is the discipline** - a forgotten bump is WORSE than no
versioning, because every old script then asserts compatibility while writing to offsets that moved,
and the check meant to catch it says "fine". So `tools/check_mailbox_contract.py` hashes the contract
surface (every `MailboxData` field in declaration order, every `Cmd`, every per-command op enum,
`Status`/`InitState`) against a golden value, and separately requires
`CeMailboxLayout.ContractVersion` - the constant actually baked into scripts - to equal the DLL's.
Comments and prose are stripped, so documentation edits do not trip it. Now the seventh CI gate.

Both directions negative-controlled: renumbering `CMD_TIME` without a bump fails with "MAILBOX_CONTRACT
was NOT bumped" and a two-line explanation of which number to move; bumping the C++ constant alone
fails on the C# mirror first, which is the more dangerous case of the two.

3419 C# tests green; DLL rebuilt (`-Target DLL`, not `-Target Test`) and `g_mailboxContract` confirmed
in the export table at ordinal 64; AOT-trimmed publish verified.

**One test defect worth recording, because it is the same shape as the bug being fixed:** the
ordering assertion first matched the substring `"write"`, which hits the word inside the scripts' own
header comments - a cheap proxy standing in for the predicate that actually mattered (a write CALL).
It failed on two generators whose ordering was correct. That is audit #4's 4b root cause, committed
in a test written to catch exactly that class of error.

-----

## 2026-08-06 - Three defects in every emitted mailbox wait, and one of them was a 155-second freeze (build 2743)

Started from one screenshot — `[Movement] mailbox timeout (DLL not responding?)` — and the DLL was
demonstrably fine. Its poller had started at 20:01:00.266 with `poll=1ms`, was still answering at
20:05:54, and logs `Mailbox: received cmd=%d` unconditionally for every command it sees
(received == answered, 4/4 and 2/2). The message sent the user to inspect a healthy DLL.

**Three defects, all present in all seven hand-rolled copies of the wait loop.** That count is the
finding: a rule applied by hand-copying it lands at N-k, and here k was every copy.

**D1 - the checkbox lied.** Every generator cleared `memrec.Active` on the "mailbox not found" path
and **none** cleared it on the timeout path, so a timed-out enable left the CE row TICKED with
nothing applied. Same class as B30/B40, which was fixed in `UE5CEDumper.CT` and
`CeInjectScriptGenerator` and never propagated. The audit then found the same hole on the
"the DLL answered with an error" branches, which the hand pass had missed in every file.

**D2 - the message guessed, and the answer was already in memory.** `status` is
`IDLE=0 / DONE=1 / PROCESSING=0xFF`; the DLL sets PROCESSING the instant its poller picks a command
up and DONE when it finishes. So on timeout the status separates two faults that send the user to
completely different places: **0 = the DLL never saw it** (stale mailbox address - re-inject, or
re-tick so CE re-resolves the symbol) and **0xFF = it took the command and wedged**. The old text
asserted the second and the observed case was the first.

**D3 - the timeout was 15.5x its stated value, and a user-run probe settled it.** The loop counted
`sleep(1)` iterations and bailed at `MailboxPollTimeoutMs = 10000`. A CE Lua probe measured
`sleep(1)` at **15.47 ms**, so the real bound was **~155 s** of frozen Lua Engine before any message.
`sleep(1)` through `sleep(10)` all cost the same ~15.47 ms and `sleep(16)` jumps to ~30 ms - it
quantises to the ~15.6 ms tick. `getTickCount()` was probed in the same run (it exists and returns
ms), which is what made a real deadline safe to emit rather than an invented API.

> **The second machine is the reason this is a fact and not an anecdote.** Re-run on a 9955HX3D
> laptop against the original 9950X3D desktop - very different TDP - every value below the floor
> matched to three decimals (`sleep(1)` 15.470 on both, `sleep(5)` 15.630 on both). Only the 15/16 ms
> readings differed (18.59 vs 17.50, 30.31 vs 29.53) = one tick of quantisation noise from a 15 ms
> timer measuring 15 ms events. **The "performance-dependent per-PC offset" hypothesis is refuted:**
> it is the ~64 Hz kernel tick, so EVERY user had the ~155 s timeout.

**The fix is one emitter, not seven patches.** `CeLuaHygiene.AppendMailboxWait` owns the loop, the
deadline, the status diagnosis and the bail-out. It takes a `MailboxTimeout` mode because the repo
has **two script shapes and they are not interchangeable**:

- **Stateful toggles** (Movement / GodMode / Fly / SeeThrough / Foreground / DebugCamera /
  TimeDilation) - `UntickAndReturn`.
- **Momentary actions** (Teleport's 17 rows) - `FlagAndBreak`. Teleport was **already correct on D1**
  and applying the toggles' fix would have BROKEN it: its record self-unticks from a deferred timer
  that also suppresses the success-close, so an early `return` would skip both. B15's comment in that
  generator had already reasoned this out; the shared emitter respects it instead of overriding it.

**Three more Teleport defects the audit found, none of which were D1/D2/D3:**

- **A silent no-op that closed the window like a clean success, on 16 of the 17 rows.** `Generate`
  sampled `cmd` ONCE with no `else`. The R3 bounded-idle-wait fix had reached `GenerateClearAll` and
  CoordLibrary but never this method - and back-to-back firing is the ORDINARY use here (hotkey-spam
  "TP facing direction", Save then Recall). When it tripped: nothing written, no message, `hadError`
  still false, so the deferred timer closed the window. Now uses the existing
  `CeLuaHygiene.AppendIdleWait` plus the missing `else`.
- **Nine of ten result codes were thrown away.** `Wirbel` defines ten negative codes; only `-7` had a
  message. The rest reached `dbg()` - silent at the shipped `DEBUG == 0` - so "Recall marker 2" on an
  empty slot (`-6`) looked exactly like a successful recall.
- **A stale `code` read on the timeout path.** The timeout `break`s, then `code` was read from a
  command that never completed; a leftover `-7` would pop "marker saved on another map" on top of the
  timeout dialog. Both result arms are now gated on `not hadError`.

**And one in DebugCamera that the hand pass missed:** the failure test was `state == -1`, but
`UE5_SetDebugCamera` re-reads the state after firing `ToggleDebugCamera` and returns whatever it
finds (`Frieren.cpp:1037-1046`) - so a toggle that fired cleanly and did not take returns **0** on an
ENABLE, with no error code. It now tests against the REQUEST (`state ~= req`).

**Tests: one rule asserted once, structurally.** `CeMailboxBailoutTests` walks each generated ENABLE
block and requires every failure message to reach an untick before control leaves its branch, plus
every non-guard `return` to have already unticked. Counting messages does NOT work and the first
version of the test was wrong for exactly that reason - the shared timeout branch has three
alternative messages sharing one untick, so a count says "3 bail-outs, 1 untick". A negative control
confirmed the rewrite: removing one untick fails exactly one test. A second test pins the momentary
shape separately so a future "fix" cannot flatten Teleport into the toggle pattern, and a third
asserts every generator's wait loop is byte-identical apart from its tag. 3385 C# tests green;
AOT-trimmed publish verified (54.2 MB).

**Reported, not fixed** (they need a UX decision, not a mechanical change): `Get camera POV` and
`Get current coords` format their numbers only through `dbg`, so at the shipped `DEBUG == 0` the two
rows whose whole purpose is displaying a number show nothing and then close the window; and
`ClearAll`'s busy/timeout `break` exits the inner wait rather than the `for slot` loop, so one click
can raise up to three dialogs. See [todo.md](todo.md).

-----

## 2026-08-06 - The leftover list stops describing a file it already recycled (build 2736)

Reported from a real session, and `view-0.log` had the whole thing:

```
18:30:20  Orphan scan: 55 candidate folder(s) examined, 3 leftover(s) found
18:30:35  Recycled leftover proxy ...Fantasynth...version.dll
18:30:35  Recycled leftover proxy ...NEKOPALIVE...version.dll
18:30:35  Recycled leftover proxy ...StellarBlade...version.dll
18:30:39  Orphan scan: 55 candidate folder(s) examined, 0 leftover(s) found
```

**The delete worked perfectly; the panel just never said so.** A cleaned row stayed on the list
still promising, in the FUTURE tense, to "Recycle version.dll, then remove up to 3 folder(s) it
leaves empty" - for a file already in the Recycle Bin, because `ActionSummary` switches on `Verdict`
alone and `OnIsRemovedChanged` re-raises it without changing it. The 18:30:39 line is the user
pressing **Find leftovers** a second time to find out what had actually happened.

**Cleaned rows are now dropped, and the equivalence is exact rather than approximate.** `Success`
from `RemoveOrphanProxyAsync` means the proxy DLL is off disk (recycled, or already gone - a partial
FOLDER prune is still a success), and the scan enumerates rows BY that DLL, so a re-scan could not
have re-found the row. Dropping it produces the list a scan would, instantly.

**Why NOT the auto re-scan, which was the other option asked for:** it is strictly worse for the
rows that stay. It would re-find every FAILED row with a **blank status**, and that status - "in use
by a running program: version.dll. Close the game or Cheat Engine and try again", "read-only, left
alone deliberately" - is the only actionable thing a failed delete produces. It also costs the 4-6 s
the log shows, on every pass, and `ScanOrphansAsync` refuses to run while a removal is in flight.

Everything is left **unchecked**, successes and failures alike: a failed row that stayed checked
would re-submit itself on the next click of a button still labelled "Delete checked (1)". With
nothing checked `HasOrphanSelection` is false and the button greys out, which is the "pass is over"
signal. When the list empties, `OrphanScanRan` keeps `ShowNoOrphansFound` true so the green
"No leftover proxy DLLs found." takes over rather than leaving an unexplained blank.

The summary line carries the **file/folder tally** ("Cleaned 3 of 3 leftover(s) - 3 file(s)
recycled, 7 folder(s) removed; 1 still listed with the reason"). The per-row SUCCESS messages are
the one thing dropping rows costs, so the totals have to survive somewhere on screen; per-row detail
for both outcomes, including why a prune stopped early, is in the ProxyDeploy log either way.

9 new tests driven through the REAL scan->delete path (stub service, not a hand-populated
collection, so the per-row `PropertyChanged` wiring the scan installs is exercised too). 5 of the 9
fail with the drop disabled. One intended test was **deleted rather than weakened**: the
"Delete checked (N)" label resolves through the Avalonia resource dictionary, which is not loaded
headlessly, so it returns `""` and the test measured the resource system rather than the behaviour -
`SelectedOrphanCount` / `HasOrphanSelection` are the same fact, testably. 3325 C# tests green;
AOT-trimmed publish verified (54.2 MB).

-----

## 2026-08-06 - Live Walker: the two navigations Back/Forward could not see (build 2734)

A survey of all 19 tabs for "which other panel deserves browser-style Back/Forward" returned an
answer nobody was looking for: **the reference implementation does not cover its own worst case.**
Both holes are one shape - a navigation that **REPLACES** the spine instead of truncating it, which
a `Stack<BreadcrumbItem>` structurally cannot express because a crumb re-**attaches** to whatever is
on screen.

**Hole 1 - stepping out of a bookmark spine wiped the forward history.** `GoBackAsync`'s
pre-bookmark branch did `Breadcrumbs.Clear()` + `Add`, which reaches the invalidation hook as
Reset-then-Add - a fresh navigation - so N Backs' worth of forward entries vanished and the Forward
button greyed on the one press most obviously undoable. It also never called `PushForward`, so the
step-out itself could not be undone. No test touched `_preBookmarkBreadcrumbs`.

**Hole 2 - `NavigateToAddressAsync` was a one-way door.** It NULLED the pre-bookmark slot and
cleared the spine, so Back did nothing at all. That is the sink for the Go box, the Find Refs owner
drill, and every cross-tab "Open in Live Walker" handoff - the paths a user reaches with one click
and no warning.

**One mechanism fixes both.** `ForwardStep` is now `(BreadcrumbItem? Crumb, SpineSnapshot? Spine)`
on a **single** stack - two stacks could not order a spine step against the crumb steps around it.
`ReplaceSpine` performs the swap under the existing replay guard (renamed `_replayingForward` ->
`_replayingHistory`, since Back uses it now too). The `_preBookmark*` triple becomes one
`SpineSnapshot? _replacedSpine`, captured by `CaptureReplacedSpine()` from BOTH the bookmark load
and the address re-root. `_preBookmarkAddress` was written in three places and read in none - dropped.
Forward's spine step is the mirror image: it puts what is on screen back into the slot, so the pair
does not ratchet. Still ONE deep, deliberately - each entry pins a whole crumb list.

**Explicitly NOT captured:** Start from GWorld / GameEngine and the Locate-in-GWorld re-spine. The
user asked for a fresh root; a Back into the discarded one would contradict the button they pressed.

**Two things found while fixing, both real:**
- `OpenReferenceOwnerAsync` set its "held the previous object in 'X'" hint **before** calling
  `NavigateToAddressAsync`, whose first statement is `ClearStatus()`. The hint was written and wiped
  inside one click and had never once reached the screen. Now set after, and it carries the
  `← Back returns to <leaf>` hint - the only affordance for the new return path, since the Back
  button has no enabled-state and the crumb strip shows the NEW spine.
- The hook's own comment claimed "7 crumb-push sites and 6 Clear sites today". Actual: **14 and 7**.
  The counts are gone rather than corrected - the drift *is* the argument for the hook.

**13 new tests, and two of them were too weak until a negative control said so.** Disabling the
`ReplaceSpine` guard failed only 1 of 3 intended tests: `CanGoForward` stays true from the spine
step's own entry even when the wipe eats everything else, so the flag assertion proved nothing.
Rewritten to walk Forward and assert the inner entries come back - 3 fail with the guard off, 8 fail
with the hole-2 capture off. 3316 C# tests green; AOT-trimmed publish verified (54.2 MB).

> ### ✅ VERIFIED in-game 2026-08-06 — SEED BATTLE DESTINY REMASTERED (UE4.27), build 2738
>
> All four presses, with the log to match. The register entry is deleted rather than ticked.
>
> - **1. Find Refs re-root.** On screen: `Opened LifeGameModeBase — held the previous object in
>   'GameState'  ·  ← Back returns to PlayerArray`. The half of that string that predates this
>   build had never once been displayed.
> - **2. Back out of the re-root.** `NAV←Back out of re-rooted spine | BC=GWorld > PersistentLevel
>   > OwningWorld > GameState > **PlayerArray(C,0x238,1E80)**` — and the `(C,…)` is the bonus:
>   the restored leaf is a CONTAINER, so the `RepopulateContainerView` branch of the swap dispatch
>   (the one flagged "unproven and cheap") is verified too, not just the plain re-walk.
> - **3. Forward.** `NAV→Fwd spine (1 crumbs) left=0 | BC=Custom(P,0x0,CB80)` — the spine comes back
>   whole, not appended. It round-tripped **twice** (19:26:31 and 19:26:34), which is the re-arm
>   working: without it the pair would ratchet and the second Back could not have happened.
> - **4. Crumb steps interleaved with it.** After a bookmark load, five Backs down to `BC=GWorld`
>   then five Forwards with `left=` counting `4,3,2,1,0` back to the full spine, ending
>   `NAV→Fwd [0] left=0 … > SaveSlotList(C,0x7D0,5160) > [0](S,0x0,0010)` — forward into a
>   container ELEMENT.
>
> **One press was not made, and it is named rather than glossed:** the final Back at a BOOKMARK
> spine's root (the step-out). The branch it would take was exercised 4× from the Find-Refs capture
> site, both sites call the same `CaptureReplacedSpine()`, and `BookmarkLoad_StepsOutThroughTheSameOneBack`
> covers it headlessly — so what is untested is one line choosing to call a shared helper.



-----

## 2026-08-05 - Logs compress in place; the 21-day purge does not notice (build 2730)

Retention bounds the AGE of the log corpus, not its SIZE. Three weeks of multi-game sessions
measured **111.9 MB**, and the fix is not a shorter window - it is that 102 MB of that is
archived text nobody will open again, sitting uncompressed.

**`compact /C /EXE:LZX`, in place: 102.05 MB -> 7.98 MB (12.8:1) in 2.8 s.** LZX is Windows'
"Compact OS" WOF algorithm, designed for write-once/read-many files, which is exactly what an
archived log is. `compact /c` (LZNT1) manages 4.4:1 against LZX's 25.1:1 on the same 8 MB file
and is not competitive.

**The purge rule needed no change, and that was measured rather than assumed.** `compact`
preserves `LastWriteTime` and `CreationTime` and moves only `LastAccessTime` - **0 of 289 files
had their write time change**. `PruneAgedLogs` keys on `GetLastWriteTime` and globs
`{prefix}-*.log`, so it deletes exactly what it would have. (Same shape as build 2726's snapshot
sweep: last-access is noise on Windows, last-write is the signal - and compression touches only
the noise.)

**gz/zip was measured, not dismissed.** `GZipStream` reaches 6.57 MB at `Optimal` and 6.26 MB at
`SmallestSize` - **1.6 % of the original corpus better than LZX** - and costs both purge globs,
every grep workflow `log-verification-checklist.md` is built on, "Open Log Folder", and owning
decompression for the DLL-written per-game mirror logs forever. An LZX file keeps its name and
extension and decompresses on read: `rg`, `Select-String` and Notepad are unaffected. 1.7 MB is
not worth that.

**Two triggers, different age floors, both honest about it.** The System-tab **button** uses a
1-hour idle floor - the user pressed it, so "compress anything nobody is writing to" is the
instruction. The **startup sweep** uses 7 days and is **opt-in, default OFF**: it is cheap and
reversible, but a launch that rewrites the user's files unasked is not a default to pick for
them.

**Six traps, each of which silently produces a wrong result.** They are why this took
measurement rather than reading:

- **Per-game log folders are game EXE names, and those contain spaces.** The first benchmark run
  built its own command line, printed `...\REMASTERED\: The system cannot find the file
  specified.`, **reported success, and had skipped 23 files.** Shipped code uses
  `ProcessStartInfo.ArgumentList` (which quotes for you) and decides success by re-measuring
  `GetCompressedFileSizeW` per file - `compact` exits 1 on partial failure while still printing
  "compressed".
- **LZX does not set `FileAttributes.Compressed`** (only `/c` does). An 8,441,784-byte file
  sitting at 335,872 on disk still reported `Archive` alone. Detecting "already done" by
  attribute would re-compress everything, forever. `GetCompressedFileSizeW < Length` is the
  signal - and 0 from it means "unreadable", not "compressed", which the policy distinguishes.
- **Appending to an LZX file fully decompresses it** (335,872 -> 8,441,799 measured). Harmless,
  but `-0.log` must never be compressed.
- A file **held open by a logger is safely skipped** (exit 1, nothing touched). The eligibility
  rules are a courtesy; the filesystem is the real guard.
- **Compress files, never the directory** - `compact /c <dir>` would route every future log
  write through the compressor.
- **NTFS only.** The button and checkbox hide on other volumes rather than offering something
  that can only answer "unsupported".

**No I/O-priority throttling, deliberately.** `PriorityClass = Idle` is CPU-only and that is
enough at 2.8 s for the whole backlog. This repo already shipped the opposite reflex once:
multipipe Phase 0 dropped the scan thread's priority, starved scans ~20x, and was reverted
(build 1840).

Policy is pure (`LogCompressionPolicy`, zero `System.IO`) and the process spawn plus P/Invoke sit
behind `Core/ILogCompressionService`. 29 new tests; the load-bearing one compresses a real folder
whose per-game subfolder name contains spaces and asserts `LastWriteTimeUtc` is unchanged. 3300
C# green, AOT-trimmed publish clean. Full numbers + method: `docs/log-compression-eval.md`.

**IN-APP VERIFIED same day**: both triggers run on the maintainer's machine - **180 files,
85.1 MB -> 6.5 MB on disk, 78.6 MB saved, 0 failed**. The folder is now 111.9 MB logical against
17.8 MB on disk.

### Follow-up, build 2732: `-0.log` is a slot name, not a liveness fact

That first real run is what exposed it. Excluding every `*-0.log` outright looked obviously
right and was wrong: a game last played 13 days ago still owns
`SEED BATTLE DESTINY REMASTERED\walk-0.log` at 3.64 MB, and nothing will append to it again
until that game is injected once more. **36 files / 5.03 MB** were being held uncompressed
permanently, and the set grows by one final log per game ever tested.

Two facts decided it, and the first is the interesting one:

- **The sibling sweep already trusts age over the name — for DELETION.**
  `LoggingService.PurgeOrphanedLogs` passes a live-name set only for the UI's own folder; every
  game folder goes through `PruneOrphanedLogs(dir, maxAgeDays)` with none at all, taking the file
  lock as the real guard (*"Locked - a running game's DLL still owns it. Retry next startup."*).
  So the policy was refusing to **compress** files the same subsystem is willing to **delete**.
  The inconsistency was the argument.
- **Compressing them is durable.** `Sein` archives a `-0.log` by RENAME on the next injection,
  and a rename preserves LZX - verified 335,872 bytes on disk before the rename and after. The
  compression rides into the dated archive instead of being undone.

The rule now: a `-0.log` in `Logs\UE5DumpUI\` is skipped on IDENTITY (a Serilog sink holds it all
session - the one place liveness is a fact rather than an inference), and a `-0.log` anywhere
else becomes eligible once idle for `LogCompressLiveFileMinAgeDays` = 7 days, on BOTH triggers
including the manual button. A running game keeps its log's mtime fresh, so age covers it; the
lock is the backstop, and a locked file is safely skipped.

3304 C# green. **In-app verified** the same day.

-----

## 2026-08-05 - Snapshots and bookmarks move out of the app-data root; only one of them expires (build 2726)

`%LOCALAPPDATA%\UE5CEDumper` had become unreadable, and for a structural reason rather than
an untidy one: **every file at that root is app-wide and fixed in number except two families,
and those two grow per game AND per game patch.** A new PE hash means a new
`snapshots.<hash>.db` set and a new `bookmarks.<hash>.json`, forever, so the handful of files
somebody actually has to open by hand - `dll-path.txt`, `ui-options.json`, `experimental.json`,
`teleport-hotkeys.txt`, `window-state.txt` - were buried under the two that never stop
arriving. Both families now live in `Snapshots\` and `Bookmarks\`, siblings of the existing
`Logs\` and `Reports\`.

**Migration runs from the store constructors, not from `App`.** The store that READS a folder
is the one that populates it, so there is no ordering to get wrong: you cannot construct a
`SnapshotStore` or `BookmarkStore` that opens files before its folder has been migrated. A
composition-root call site would have been one reorder away from silently reading an empty new
folder while the user's data sat in the old one.

**A game's files move as a GROUP or not at all.** Half a migration is data loss, not
untidiness: a SQLite `.db` that lands in the new folder without the `-wal` it was
checkpointing has dropped every transaction that WAL still held, and the abandoned `-wal` is
then live bait for the next file to take that name. So each group is attempted `.db`-first
(the file most likely to be locked, so a doomed move touches nothing) and rolled back on the
first failure. A destination that already exists aborts its group rather than overwriting -
the case that produces one is running an older build after migrating, and nothing on the
outside can tell which of the two copies the user wants. **"Remove All Snapshot Data" now
sweeps the legacy root too**, because a set migration had to leave behind is still that
button's problem.

**Snapshots expire at 21 days. Bookmarks never do.** Same folder scheme, deliberately
different retention, and the asymmetry is the point: a snapshot DB is a regenerable multi-GB
capture, which is what makes a disk-reclaiming sweep worth its risk, while a bookmark file is
a few KB of hand-placed navigation nobody can replay their way back to. `BookmarkStore` passes
`maxAgeDays: 0`, which disables the sweep outright - `docs/todo.md`'s old "add a bookmark
startup sweep" item is now marked **rejected**, not pending, so nobody finishes it later.

**"Unused" is RECORDED, not inferred - and last-access time is the trap.** The first cut read
`max(LastWriteTimeUtc, LastAccessTimeUtc)` on the obvious reasoning that write time means
"unmodified" while access time means "unused", so the max of the two can only be safer. The
maintainer's own app-data folder refuted it in one listing: `fsutil behavior query
DisableLastAccess` reports **2 (System Managed, updates ENABLED)**, and every file there read
as accessed *today* against write times weeks old - a `bookmarks.*.json` last written
2026-06-24, "accessed" 2026-08-04. Any antivirus scan, backup pass or search indexer refreshes
it. Honouring that signal would not have made retention safer; it would have made retention
**never fire**, a silent no-op dressed as a feature. So the sweep reads write time only, and
`SetActiveGame` **stamps** it when a game becomes active - nothing outside this process writes
these files, so an explicit stamp is immune to every background reader on the machine.
Connecting to a game therefore resets its window even in a session that never opens the
Snapshot tab. Ageing is per GAME, not per file: the newest timestamp in a set governs the whole
set, so a 200-day-old `.denylist.json` never outlives (or drags down) the `.db` it belongs to.

**Split the way `ProxyOrphanScanner` is split.** `AppDataRetentionPolicy` is pure - zero
`System.IO`, so the rules that decide what gets DELETED are unit-testable without a disk -
and `AppDataFolderMaintenance` does the IO. 46 new tests cover both, including the two that
matter most: a locked `.db` leaves its `-wal` beside it, and a 4000-day-old bookmark file
survives. 3270 C# green.

-----

## 2026-08-05 - The group row shows one pairing; now you can see the other thirty-five (build 2719)

Fourth report of the same shape, and the first fix that is not zero-sum. *"`TickCount=NNN,
FrozenInt=424242` 沒出現"* - and it had matched all along. That session's own `ui-pipe-0.log`
says so: slot 0 (`Changed`) kept offsets `[1288, 1304]`, slot 1 (`Unchanged`) kept 36 including
`1308`. `1304` is `TickCount`; `1308` is `0x51C`, which the same session's `ui-view` log names
outright - `ScrollToFieldByOffset: offset 0x51C -> field 'FrozenInt'`. Two valid assignments,
one row, and build 2701's same-struct preference gave the row to the `Health` pair.

**Every previous fix here changed WHICH witness wins.** That is zero-sum: promote either pairing
and the other reads as missing. Expanding the row did not help either - the detail line said
`= unchanged: 36 candidate offset(s)`, a bare count, because `matched_offsets` shipped raw
integers and only the representative leaf was ever named. An integer cannot tell anyone that
1308 is `FrozenInt`.

**`query_group_slot_leaves`** names them. Given a session, a candidate and a slot it returns
every kept leaf with its field name, type, current value and addresses - on demand, per expanded
row, never inlined (a page is up to 1000 candidates x N slots x `per_slot_cap` leaves). Each leaf
comes back as a full slot match, so Live / Addr / Pivot / Locate work on it with no new plumbing.
The UI affordance is **All fields** in the group row's details.

**`match_count` is finally read.** It has been on the wire since 2690 and parsed nowhere. The
master row now reads `Health.CurrentValue=19 (+1), Health.BaseValue=100 (+35)`; the detail row
reads `... -> 0x504 - 1 of 36 matching field(s)`. Counted through one `MatchingFieldCount`
helper, so Snapshot Group / SPC Group / Class Pivot inherit the annotation from their own offset
lists instead of coming back later as a separate report.

**The witness rule moved to `Radar::PickGroupWitnessAssignment`,** beside the filter it has to
agree with. It lived inside `Fern.cpp`'s JSON encoder, which no test target compiles - which is
*why* it kept drifting from `BuildGroupOrderedView`. `dll_helpers_test` compiles `Radar.cpp` as a
source, so it is now covered: sibling preference, filter-by-name, filter-by-value, distinctness,
empty slot, out-of-range descriptor. One encoder (`GroupLeafToJson`), one decoder
(`ParseGroupSlotLeaf`), one picker - audit #4's root cause 4a closed at the source.

**Four defects found while building it, one of them a fix that was itself wrong.**

`deep` emits one candidate *per container block* and they all intern to one `InstanceRecord`, so
a lookup by `instance_addr` alone would answer an expanded deep row with a different block's
fields. The request now carries an optional `leaf_addr` tie-breaker (an index cannot work - a
refine rebuilds the candidate vector), and where the hint matches nothing AND several candidates
share the address, the server returns `stale_leaf_addr` rather than guessing. The reachable path
is not the race it looks like: a **cancelled** Group Next Scan leaves pre-refine rows on screen
against a session the DLL already mutated.

The new command was missing from `LaneRoutingPipeClient.BulkCommands`, which would have parked it
on the interactive lane behind a refine holding `GroupSessionManager::mu_` - stalling Live Walker.

A deep leaf's `offset` is 0 by construction, so its location rendered as `-> 0x0`. **The first fix
for that was a new bug**: falling back to the absolute `Addr` is right on the live path, where
`addr` is `GroupSlotMatch::leafAddr`, but the Snapshot builders cannot capture an array element's
heap address at all and set `Addr = AddrPlusOffset(objAddr, 0)` - the owning object's base. So a
Snapshot Deep row would have named the **UObject header** as the place the value lives: a
plausible, copyable, wrong address, strictly worse than the obviously-unknown `0x0` it replaced.
The producer now states it (`HasLeafAddress`, set only by the live decoder) instead of the
consumer inferring it from `offset == 0`, and where nothing true can be said the row omits the
arrow entirely. Exactly the audit-#4 4b root cause - *a cheap proxy signal substituted for a
predicate a sibling already computes correctly* - committed while fixing 4a.

`(+N)` also reaches Snapshot Group / SPC Group through their own offset lists, which is
deliberate, but those panels have no live session and so no **All fields** button; they get a
tooltip that explains the annotation without pointing at a control they do not have.

**Then the maintainer made the sharper point, and it changed the design.** *"Either the row
features TickCount or it features FrozenInt — and it features neither."* Correct, and not fixable
by ranking: among slot 1's 36 unchanged fields, nothing distinguishes `FrozenInt` from `I16` or
`FixedArr`. 2 x 36 = 72 valid assignments and the scan cannot know which was meant, so **every
heuristic answer is still zero-sum**. The pairing was never absent from the result set — both
leaves were kept, and the log proves the slot-0 half outright — it was unaskable.

So it became askable. **The group filter is now space = AND** (`Radar::SplitFilterTerms`): it was
the last keyword box in the repo treating its input as one substring, against CLAUDE.md's own
MUST-rule, and that is precisely why "TickCount in one slot, FrozenInt in the other" could not be
expressed. Terms are ANDed over the candidate (field-level OR), and the witness picker claims one
term per slot — so `tickcount frozenint` turns the row into that exact pairing, in either order.

**Verified in-game the same day, and it produced one more rule.** With `tickcount frozenint` the
row became `TickCount=45 (+1), FrozenInt=424242 (+35)` — exactly the requested pairing. The
maintainer then generalised the case they had *not* filtered: *"don't use a 0 as the default
displayed pair — a 0 has little real meaning in a game."* Right, and it was the default row's
worst habit: `PrimaryActorTick.TickInterval=0, InitialLifeSpan=0` while the object's real fields
had matched too. **Non-zero now wins inside every selection rule** (`Radar::IsZeroValueText`) —
a tie-break, never a rule of its own, so a slot whose leaves are all zero still shows one and a
zero the user filtered for still wins. Also widened the field column: `All fields` was truncating
`MinNetUpdateFrequency = unchanged -> 0x174 (FloatProper...`.

And the unasked case became findable: the leaf list is ordered **object's own declared fields
first** (`Radar::OrderGroupSlotLeaves`, stable within tier). Leaves are collected base-class-first,
so `All fields` used to open with `PrimaryActorTick.*` / `InitialLifeSpan` / `CustomTimeDilation` /
`AttachmentReplication.*` and `FrozenInt` sat past row 30 of a 220 px scroll box — the list existed
to make it findable and was hiding it. The tier comes from `definingClassName == className`, not
from the offset: a high offset *correlates* with "declared late" but is not that predicate, and
substituting a proxy for a predicate the data already carries is the mistake this same entry
records two paragraphs up. **"All fields" also toggles** — second press collapses.

-----

## 2026-08-05 - docs tidy: the rot was stale STATUS HEADERS on live docs, not misfiled files

Asked to archive what is out of date and to consider a `docs/evaluations/` subfolder for old
evaluations. A three-way survey said **don't create the subfolder** and I agree: after fixing the
headers, the set of "decision record for something deliberately not built" is **n = 1**
(`text-translation-eval.md`). Everything else that *looked* like a dead evaluation is a shipped
feature wearing a stale header. Moving ~40 files would also have meant regenerating two CI-compared
golden artifacts that embed doc paths (`tools/pe/aob-specificity-baseline.tsv`,
`tools/ghidra/blocks/blocks.json`) and breaking links inside archived files the convention forbids
repairing. **Zero files moved; zero link churn.**

**The most dangerous line was in CLAUDE.md**, and it was about code that does not exist: the index
claimed multipipe **"Phase 0 SHIPPED (scan thread-priority guard, build 1834)"**. `grep -rn
SetThreadPriority dll/src/` returns **0 hits** — Phase 0 was shipped and then *reverted* (build
1840) for starving scans ~20×, which `multipipe-eval.md` itself says and the index never learned.
That row would have sent a session hunting a guard that is not there. The same row also repeated
the head-of-line-blocking root cause **that §10 measured and refuted** (dispatcher idle ~70% of
wall-clock; worst single dispatch out of 24,178 = 14.3 ms; Phase 1 = WON'T DO). Both fixed, and
`multipipe-eval.md`'s own TL;DR now points at §10 instead of contradicting it.

Nine more headers said "not built" about shipped work — `native-c-value-scan-spec.md`
("DESIGN ONLY"), `experimental-snapshot-spc-pivot.md` ("PLAN ONLY (no code yet)"),
`ce-export-drilldown-spec.md` ("PROPOSAL (awaiting approval)"), `aob-block-library-eval.md`
("NOT BUILT" while two of its proposals are CI-gated), the Teleport Coordinate Library index row,
and `todo.md`'s "Time / Timer control … not yet built" (L1 + E shipped and live-verified).

**Every number I touched, I re-derived rather than inherited** — and each is now labelled as
derived so the next drift is a regeneration, not an edit: pipe commands **31 → 99**
(`grep -c 'constexpr const char* CMD' dll/src/Renge.h`), C ABI exports 57 → **59**, Avalonia
12.0.2 → **12.1.0**, DLL sources 28/33 → **31/36**, UI files ~245 → **~269**, tests "496 across 16
files" → ~3200 across 135. `roadmap.md`'s hand-maintained `dev = main @ build 2252` line was 57
commits stale; deleted rather than refreshed, because it rots on the next commit — ask
`git rev-list --count main..dev`.

Also: three `todo.md` bullets carrying their own *"Delete after the batch merges to main"* trigger
were removed (their batches merged long ago), and `docs/archive/README.md` now documents that
relative links **inside** archived files are knowingly broken — converting an invisible defect into
a stated one without violating "nothing was edited, only moved".

-----

## 2026-08-05 - Vendor sync: zydis +1, minhook current, and the RE-UE4SS diff that was not ours

Pre-merge vendor pass. Two of the three answers were "no action", but the third question was the
one worth asking.

**`git describe` lies about the zydis version.** It says `v4.0.0-121-ga95bb71` because the v5 tag is
not in the fetched tag set, so it walks back to the nearest reachable v4 tag. `Zydis.h:89` is the
fact: `ZYDIS_VERSION 0x0005000000000000` = v5.0.0, and `Denken.cpp:155` uses the v5 API
(`op.mem.disp.size == 0`). The memory file that tracks this had a SHA that matched neither. Check
the header, never a label.

**zydis `85d7518` → `a95bb71`** — "Decoder patch for variable-position decoder-tree filters" (#638):
a decoder fix plus a full table regen, +34.9k/−45.7k lines. Same shape as the v4→v5 bump, which was
judged to warrant an in-game check. DLL rebuild clean, 81 + 996 green — and that is evidence rather
than "it compiled", because **five `Test_Denken_*` tests decode real x64 byte sequences through
Zydis**, including `Test_Denken_ExcludesStackAndZeroDisp`, which is exactly the `disp.size == 0`
path the v5 migration touched. ⬜ The in-game Path-2 smoke test is still not done. minhook is 0
behind.

**RE-UE4SS `6c26f038..662df915` — not applicable, and the useful half was the inverse question.**
Upstream fixed "Can't write .usmap file if path is wide" (`fopen_s` → `_wfopen_s`). Our USMAP writer
is C# (`UsmapExportService.cs`), where .NET strings are UTF-16 and the file APIs are W-variants, so
that bug cannot occur. **The question worth asking was whether WE carry the same bug class in the
C++ DLL** — a CJK Windows username would be enough to trigger it. Swept `dll/src/`: every
`ifstream`/`ofstream` in `Flamme.cpp` takes an `fs::path` (`GetCacheFilePath()` returns one), and
MSVC opens `std::filesystem::path` through its wide `native()`. Clean. Second time an RE-UE4SS diff
has come up needing zero code change; the pattern is that their runtime concerns rarely reach a
read-only dumper, but the bug *class* sometimes does and is worth the five-minute sweep.

-----

## 2026-08-05 - The DumperTest sample's on-screen heartbeat is a no-op in Shipping (sample source; docs only)

Reported alongside the group-scan verification: the Development package prints the heartbeat, the
Shipping package does not. **Third assertion about this same function, and the first one that read
the gate.** UE 5.4 `Engine/Source/Runtime/Engine/Private/UnrealEngine.cpp:11397`:

```cpp
void UEngine::AddOnScreenDebugMessage(uint64 Key, ...)
{
#if !(UE_BUILD_SHIPPING || UE_BUILD_TEST)
```

The whole body is compiled out. The message never enters the list, so `bEnableOnScreenDebugMessages`
and `GAreScreenMessagesEnabled` cannot bring it back — and the previous fix, which set all three
flags every draw, was built on a comment claiming the opposite ("not compiled out of Shipping; the
display call site is gated `#if !(UE_BUILD_TEST)`"). That read the DISPLAY gate and missed the ADD
gate. Corrected in the source comment and the sample README, with the consequence spelled out: **a
blank screen in a Shipping package proves nothing about the sample** — use the Development package
for "is the timer running?", or read `TickCount` at `0x518` in Live Walker, which is authoritative
in both.

**BUILT the same day, after the maintainer re-cooked and reported it still missing** (correctly —
the first pass had changed only the comment). `ADumperTestHUD : AHUD` overriding `DrawHUD()` now
draws the three lines, installed at runtime by `ADumperTestActor::EnsureHeartbeatHud()` via
`APlayerController::ClientSetHUD` — **not** the GameMode's `HUDClass` as first sketched, because
that is a binary asset and the sample's whole design avoids those (same reason the actor is spawned
by a subsystem rather than placed in a level).

The entire chain was read in the 5.4 source before a line was written, since this environment
cannot compile UE and a wrong guess costs a cook-and-package cycle: `ClientSetHUD` executes locally
in a standalone game (`Actor.cpp:4923-4935`, `NM_Standalone` → `FunctionCallspace::Local`) →
`ClientSetHUD_Implementation` spawns and assigns `MyHUD` (`PlayerController.cpp:1332`) → the
viewport calls `MyHUD->PostRender()` (`GameViewportClient.cpp:1936`, no `UE_BUILD_*` gate anywhere
in 1700-1940) → `AHUD::PostRender` (`HUD.cpp:149`) → `DrawHUD` (`:638`) → `DrawText` (`:929`), with
`bShowHUD = true` from the ctor (`:75`).

**Installed from `Tick`, never from the 1 Hz timer** — the first draft used the timer and that
recreates the exact ambiguity the split-clock design exists to end: a dead timer would show as a
blank screen, indistinguishable from "the sample never spawned". Caught before the maintainer saw
it; the adversarial review caught six more, all documentation-vs-code splits, including the old
already-refuted paragraph that had survived the README rewrite and still named the deleted
`DrawHeartbeat`.

**A third wrong Shipping assertion fell out of the review, this one pre-existing:**
`UE_LOG(..., Warning, ...)` does *not* survive a Shipping build. `Build.h:328` sets
`NO_LOGGING = !USE_LOGGING_IN_SHIPPING` (0 by default) and `LogMacros.h:146-158` reduces `UE_LOG`
to Fatal-only under it — so `[DumperTest] ADumperTestActor ready at 0x…`, documented as the
without-the-dumper existence check, prints in Development only. Corrected in the source and the
README. All three misreads in this one file share a cause: **inferring a gate from a sibling
instead of opening it.**

⬜ **Needs the maintainer's re-cook to verify** — it cannot be compiled or run here, and nothing
about it is claimed to work until that run.

-----

## 2026-08-04 - The last four audit items, each one a decision rather than a bug (build 2621)

Audit #4 closes at 52/52. These four were held back on purpose: each had a defensible answer in
both directions, so they went to the maintainer rather than being decided by whoever was typing.

**B13/B41 - "Moved to the Recycle Bin" was a promise the guard could not keep.** `FOF_ALLOWUNDO` is
best-effort: on a volume with the recycler disabled (`NukeOnDelete=1`, or policy) it hard-deletes
and returns 0. The only precondition was a `Path.GetPathRoot` **drive-letter** test, which is not a
recycler test at all - and `OrphanVerdict.NotOnFixedDrive` existed in the enum with **no producer**,
because the question was first asked inside the delete call, after the user had already been told
what would happen.

*Decision: refuse, and say why.* `SHQueryRecycleBin` on the volume root now answers it, reached
through `GetVolumePathName` rather than `GetPathRoot` so a mount-point volume is asked about
ITSELF instead of its host disk. The scan asks at plan time, so the verdict finally has a producer
and the confirm dialog never offers a recycle the volume cannot perform.

**B21 - a European decimal comma parsed silently and wrongly.** `AllowThousands` made
`"67162,398"` into **67162398** - a coordinate a thousand times out, accepted with no issue raised,
because the decimal-comma repair only ran for `;`-delimited files.

*Decision: drop the flag.* It also rejects Excel's grouped `"67,162.398"`, and that is the accepted
trade: a rejected row is VISIBLE in the import preview and the user can fix the file, whereas a
silently mis-scaled coordinate teleports them into the void with nothing to look at. Our own
exporter never emits grouped thousands. Seven assertions pin both directions.

**B25 - the most destructive verdict in the detector armed off one uncorroborated field.**
`DetectVersionFromPEResource`'s `major == 4` branch returned **tier 1** off a single
`VS_FIXEDFILEINFO` read, so `bLowConfidence` stayed false and the pre-4.11 total-scan refusal could
fire on it alone. Every other version signal in Genau demands context.

*Decision: require corroboration.* A sub-floor PE reading no longer short-circuits - it falls
through to the memory scan and is kept at **tier 3** unless that scan agrees, and tier 3 sets
`bLowConfidence`, which the refusal gate requires to be false. The residual is stated in the code:
the needle table floors at `"4.18."`, so a genuine 4.0-4.10 title will not be corroborated and pays
a ~4-second sweep. That is the right direction to be wrong in. **The pre-UE4 sentinel path is
untouched** - it is a positive 2-of-4 marker identification and stays tier 1 by design.

**B26 - duplicate CE records sharing one global buffer marker.** Unticking the OLDER record
deAlloc'd the NEWER one's live buffer and unregistered `UE_GameEngine` while that record was still
ticked, leaving its chain at `??`.

*Decision: both halves.* DISABLE now verifies `getAddressSafe(sym) == mem` before freeing - a
mismatch means the newer ENABLE overwrote the marker, so the older record leaves both alone and
says so. And the push is deduped per session: a repeat click falls through to the **clipboard**
rather than being refused, because the legitimate reason to click again is "I deleted the record
and want it back", and pasting satisfies that without creating a second record.

**Audit #4 is complete as work.** What remains is verification: every fix is filed in
[todo.md](todo.md)'s pending-verification register, split into log-derivable and manual-only as the
maintainer asked, and none of it has been run on a real game yet. 81 utf8 + 949 dll + 3212 C# green.

-----

## 2026-08-04 - The rest of audit #4: 26 small defects, six refactors, and a measurement that said no (builds 2603 / 2610 / 2614 / 2617)

The tail of audit #4 - everything that was not a HIGH or a concurrency defect. Four commits, split
by where the damage was.

**2603 - eight DLL + script defects, each of which turns one failure into a worse one.**
`Sein::WriteToFile` fprintf'd a **NULL** `FILE*` whenever a rotation's truncating reopen failed
(full disk, or a viewer holding the file), and the UCRT's invalid-parameter handler *terminates the
injected game*. Both retention sweeps shared one `error_code` between iteration and `fs::remove`, so
the first undeletable entry tripped the loop's own `if (ec) break` - and since enumeration order is
stable, it died at the same entry on every launch and the advertised 21-day retention silently
stopped applying past it. One locked file was enough.

Genau's Extra Scan had **zero** `Tot::` references against Aura's 30, while `Fern::Stop` joined its
thread under a comment asserting these were "bounded AOB scans". `UE5_Shutdown` runs on the CE Lua
caller's thread, so the freeze was CE's whole UI, not just the game's. Laufen/Hemmung captured a
base of 0 during a cutscene or swim and then held the value AT ZERO while the panel read "300%,
active" - and in Hemmung, where base is the RESTORE value, that meant Reset left the game frozen.
The forced hook path skipped the retry cap but still spent its budget, so two clicks of Live Funcs
silenced the automatic retry for the session.

`g_isCEPlugin` was set only in `CEPlugin_InitializePlugin`, which CE calls only on **enable** - so a
registered-but-unticked plugin got DllMain plus a 1 s wait that no human ticking a checkbox can
beat, and the DLL ran a full AOB scan and opened the pipe **inside cheatengine-x86_64.exe**.
`CEPlugin_GetVersion` now claims the identity, plus a process-name guard. The "first-loaded-wins"
proxy mutex was `Global\...` though its comment said per-process: `Global\` needs
`SeCreateGlobalPrivilege`, which a non-elevated game lacks, so `CreateMutexW` returned NULL and the
dedup **silently never worked** - and on the rare elevated game it was worse, because a second
instrumented game anywhere on the machine turned this proxy passive.

`Renge::HexToBytes` could not fail: `strtoul` mapped every non-hex character to `0x00`, so
`"DE AD BE EF"` - spaces and all, the way a person writes a byte pattern - was written into the game
as `{DE,0A,0D,BE,0E}` and answered `ok:true`.

And the proxy generator had no PE-machine check. Measured on this machine: System32 `winmm.dll` has
180 named exports, SysWOW64 has 192 - 12 x86-only names and **174 shared names at different
ordinals**. A regeneration under 32-bit Python therefore emits permanently-null lazy thunks (each a
`jmp rax` with `rax == 0`) and a wholesale-wrong `@ordinal` map, with a build that stays internally
consistent and links clean. That is the upstream cause of B44, whose explanatory comment went into
the **generator** rather than the generated file - `--check` proved a comment written directly into
`Lugner_Winmm.asm` would be deleted by the next regeneration.

**2610 - twelve UI defects, all of the "the report and the reality are different code paths"
family.** The wrong-game banner had exactly one call site, inside the `Pointers.RescanApplied`
lambda, so it fired only after a UE-override apply or an Extra Scan and *never on Connect* - which
is the moment it exists for, since the pipe name is shared and Connect lands on whichever server is
free. The uid was the one coordinate-library field that skipped every ingress guard, so a row
duplicated in Excel and renamed imported with a duplicate uid, and `DeleteCoord`'s
`RemoveAll`-by-uid then wiped **both** rows while naming one. Pose was not cleared on disconnect,
and `PoseMap` feeds the "current map only" filter, so the next game's library rendered "0 of 340".

Two Teleport mailbox waits ended in a bare `then break end` and fell straight into the auto-close,
so the Lua Engine window shut on the one outcome the user needed to read; the assertion that used
to guard this is now a theory over every generator instead of Movement alone. The autorun script
bound `DEBUG` at CE start-up, which made its own printed instruction ("set `UE5_DEBUG = 1` in the
Lua console") impossible to follow.

`DiagnosticsProbe` measured itself: `_txBefore` is a **field initializer**, so it runs after
`BeginAsync`'s opening `get_diagnostics`, while `txAfter` was snapshotted *after* the closing one
that `_sw` had already excluded. The probe's own 93-125 ms round-trip therefore landed in
`transportMs` but not in `wallMs` - `transportMs > wallMs`, `uiMs` clamped to 0, `ipcMs` absorbed
the probe. Those are the lines `docs/multipipe-eval.md` reasons from.

Plus: all four Force actions rendering with nothing selected, five coord-grid sort headers dead
under AOT, "Already gone - nothing left to remove" printed in success green over a run that had just
pruned four directories, a ghost Cancel on whichever card was not scanning, and a second launch that
called `Shutdown(1)` before the logger existed - no window, no dialog, no log line.

**2614 - refactor, and one measurement that overruled the plan.** Three private Lua escapers
collapsed into one; two were byte-identical copies whose own comment said *"keep them mirrors"*, and
the third was silently weaker - backslash, quote and newline only, so a closing long bracket passed
through verbatim into a script AOBMaker wraps in `[==[ ... ]==]`. `aob_specificity.py`'s docstring
still said **"NOT ACTUALLY WIRED INTO CI"** three days after it was wired in as a *blocking* gate,
which would lead a maintainer to `--update-baseline` straight past the one signal the golden file
exists to deliver.

The interesting one is R4. `DumpExplorerViewModel` was the last panel not using the shared
space=AND helpers, and the audit prescribed splitting its concatenated haystack into four fields.
Measured first, as the finding itself instructed - 500K entries, 3 terms, mean of 5 passes:

| shape | time | hits |
|---|---|---|
| concat haystack + `Ordinal` (what it did) | 26.7 ms | 18,829 |
| **four fields + `OrdinalIgnoreCase`** | **55.1 ms** | 18,829 |
| concat haystack + `OrdinalIgnoreCase` (chosen) | 25.8 ms | 18,829 |

Identical hit counts, and the prescribed shape is **2x slower** on the one panel that loads a whole
offline dump. Rejected on the number. The real defect - splitting on `' '` alone, so tab/newline
input from a spreadsheet was missed - is fixed by `SplitTerms`, and the match now goes through
`MatchesAllTerms` so it cannot drift again.

R6 deleted 24 unreachable `en.axaml` keys. The first sweep found 20: a key that is a *prefix* of
another (`str.LiveWalker.Copy` vs `.CopyAddr`) looks used under a substring scan. The new
`tools/check_axaml_strings.py` is token-aware, checks **both** directions, and is a CI gate -
dangling is the crash case (a missing `StaticResource` raises at load time), orphan is the honesty
case.

**2617 - B39, which was missing from the continuation plan entirely.** Four `Flamme.cpp` writers and
`AobUsageService.SaveFileAsync` all staged through the byte-identical `<file>.tmp`, from *different
processes*, each with truncate. So the game's DLL could truncate the staging file while the UI was
mid-write, and whichever renamed last published a half-written document over the real cache. The
in-process semaphore guarding the C# side cannot see the other process. Now `.tmp.<pid>` on both
sides; the final rename stays last-writer-wins, which is the accepted semantics.

**50 of 52 audit items shipped.** 81 utf8 + 949 dll + 3205 C# green.

-----

## 2026-08-04 - "中文一二" is not four ASCII characters (build 2599)

Audit #4 **B28**. `DecodeFStringBuffer` decides whether an FText's buffer is 1-byte (FUtf8String)
or 2-byte (UTF-16) by where the null terminator sits. It tried UTF-8 first and returned on the
first hypothesis that passed — and the UTF-8 gate accepted far more than its docstring claimed.

The docstring justified the gate by reasoning about UTF-16 **high** bytes: they are `0x00` for
ASCII, which the interior-null scan catches. It never mentions **low** bytes, which are `0x00` for
every `U+xx00` codepoint. `中文一二` is `2D 4E 87 65 00 4E 8C 4E 00 00`; `buf[4]` is 一's low byte,
and bytes 0-3 contain no zero. The UTF-8 hypothesis accepted, `Sanitize` produced `-N?e`, the
one-third replacement tolerance passed it (1 of 4), and the correct UTF-16 branch was never
reached. The trigger is not exotic — any even-length CJK string with a `U+xx00` character:
統一, 第一, 唯一, 萬一.

The audit's own caveat ruled out the obvious fix: scoring both candidates by replacement ratio
does nothing for `第1章` → `,{1` or `中A文` → `-NA`, which score **zero** bad characters, and it
can regress the shipped UTF-8 case. So the fix is two structural rules, not a score.

**Rule 1 — strict UTF-8 well-formedness** (new `IsWellFormedUtf8`). The first `n` bytes of a UTF-16
CJK buffer carry a lone continuation byte: `0x87` with no lead. That is not "how much of this looks
like text", it is "this cannot be UTF-8". Kills `中文一二` outright.

**Rule 2 — a well-formed multi-byte sequence decides the width**, before the UTF-16 hypothesis is
even evaluated. A UTF-16 prefix essentially cannot produce a valid multi-byte sequence, and the real
shipped case — Star Trek Voyager's FText, eleven 3-byte CJK characters — is exactly that. This is
what closes the regression the audit warned about; there is a test that hands it a zero heap tail at
the UTF-16 terminator position on purpose and asserts UTF-8 still wins.

**Rule 3 — when the UTF-8 candidate is pure ASCII, prefer UTF-16.** This is the part the finding
identified and it rests on an asymmetry worth stating plainly: the UTF-8 evidence (`buf[n-1] == 0`)
sits **inside** a UTF-16 string's own payload, so UTF-16 text produces it routinely; the UTF-16
evidence (a zero unit at byte `2n-2`) sits **outside** an n-byte UTF-8 string's payload, in
unrelated heap, and can only be satisfied by chance. Evidence that cannot be self-produced beats
evidence that can.

Residual, written into the function header rather than left implicit: an ASCII-only UTF-8 buffer
whose heap tail is `00 00` at exactly `[2n-2]` *and* whose full 2n-byte reading still looks textual
would be misread. Both conditions must hold, and FUtf8String FText exists precisely for non-ASCII.

13 new EXPECTs, including the two cases the second lens found and a `IsWellFormedUtf8` battery
(lone continuation / truncated / overlong / surrogate / 5-byte). Negative control: restoring
"return the first hypothesis that passes" fails exactly the three CJK cases and nothing else.
81 utf8 + 938 dll green.

-----

## 2026-08-04 - The report said collision was off; nobody had turned it off (build 2596)

Audit #4 **B8 + B10 + B14 + R5**.

**B8 — Fly/noclip.** The collision record was written whether or not `SetActorEnableCollision`
ran, in both directions — and the re-emit condition reads that same record, so nothing ever
retried. Turning Fly off while the game thread was paused therefore restored MovementMode, zeroed
velocity, **skipped** the collision restore, and wiped the record. The pawn fell through the world,
and re-enabling Fly without Noclip did not bring it back.

The finding called that an alt-tab edge case. It is the *normal* path: on an idle-when-unfocused
title the click that turns Fly off is in **our** window, which is what backgrounds the game and
stops ProcessEvent — so `IsGameThreadResponsive` is false at exactly the moment the restore is
needed. Three changes, each of which was hiding the next:

- The worker commits `collisionOff/collisionPawn` from what the invoke actually did. The only
  non-committing path is the deferred one, which is precisely the one that must retry. A missing
  setter *does* commit — retrying cannot conjure one, and `InvokeSetCollision` already says so once.
- `active` is cleared, the worker **joined**, and only then is the restore decided. The old order
  let an in-flight tick turn collision back off after the restore (Schlacht's M1 shape).
- The record is **kept**, and a `PendingRestoreLoop` polls for the game thread and restores the
  instant the user clicks back in — Schlacht's shipped precedent, now a second user of it.

It also restores on the pawn the collision was actually disabled on, not a freshly re-resolved one:
after a respawn those differ, and re-enabling collision on the new pawn leaves the original
permanently ghosted.

**B10 — `WalkClassEx` had no memo, and four call sites said it did.** `WalkClass`'s `Fields` is the
flattened inheritance chain, so an Actor subclass carries 100-300 `FieldInfo` × 14 `std::string` —
deep-copied on every call, from every `ParallelGObjectsScan` worker, per struct-array **element**
during snapshot capture and group scan. Added `s_walkClassExCache`; `WalkClassEx` returns
`const ClassInfo&` and 23 call sites bind by reference.

The prerequisite was not optional. `s_walkClassCache[addr] = info` is an assign-over-existing that
destroys the entry's vector — harmless while everyone got a copy, a use-after-free the moment
anyone holds a reference. Both caches now `try_emplace`: first writer wins, the results are equal
anyway (walk and enrichment are pure functions of the same reads), and an existing entry is never
touched. Node-based map, no `erase`/`clear` in `dll/src`, so entries never move. A regex sweep
confirmed no caller mutated the result before the return type changed; the compiler enforces it now.

The payoff is measurable with no new logging — snapshot capture already runs inside a
`DiagnosticsProbe`, so its `PERF Snapshot capture: wall … ms` line records the difference by itself.

**B14 + R5 — the guard that reached 2 of 7 threads.** Build 2389 added a per-tick exception guard
for a live-reproduced `0xC0000409` (a `bad_alloc` escaping a thread entry is `std::terminate`, not a
caught error). It was applied by hand-copying, so it landed at two sites. New **`Routine.h`**
(Frieren roster: ルティーネ, the Shadow Warrior librarian — *"scheduled / periodic subroutine"*) owns
the shape once: `ReassertLoop` = cancel-immunity + sliced sleep + guarded tick + a
`Tot::ShutdownRequested()` break the hand-copied loops never had, plus `RunTickGuarded` and
`SleepSliced`. The four hold workers (Solitar / Laufen / Hemmung / Solide) are now their tick and
nothing else, both `PendingRestoreLoop`s are wrapped, and `Grimoire::WORKER_SLEEP_SLICE_MS` replaces
8 bare `25`s.

The shutdown break matters on its own: `Tot::ShutdownRequested()` is **not** set when a user simply
closes the game, so a `PendingRestoreLoop` could keep walking reflection against a tearing-down
process for up to five minutes.

Deliberately not templated: each module's drift WARN. Those strings are individually worded and the
log-verification checklist greps them by format string, so collapsing them into one template would
have cost more than it saved. 938 C++ + 3161 C# green.

-----

## 2026-08-04 - Two threads, one latch, and a flag that was answering two different questions (build 2592)

Audit #4 **B5 + B4**, the DLL concurrency pair. Both are the same shape: a guard that reads correct
until you ask *which thread*.

**B5 — `UE5_Init` latched only at the end.** The "already initialized?" test at the top read a plain
`static bool` that the body sets on its **last** line, after a multi-second scan. So the guard could
never serialize two callers — it could only tell you about a scan that had already finished. And
there is a designed-in second caller: in **proxy mode** `Heiter` starts the pipe *without* scanning,
so both cached pointers are 0 while the pipe is already live. A UI Scan (`Fern::RunScan` →
`UE5_Init`) and a CE hotkey (`Mimic::EnsureInitialized` → `UE5_Init`) both find the latch clear and
both run a full init. They write `DynOff::*`, Aura's array descriptor, Serie's pool state and
`FindGEngineSlot`'s report wholesale, and the later probes read back what earlier probes latched — so
an interleave latches a *mix*. That is the failure `Grimoire.h` documents as total but silent: every
property type unknown, log still printing `validated=yes`.

`s_initialized` is now `std::atomic<bool>` (the unlocked fast path was itself a data race) behind a
dedicated `s_initMutex` around the body, with the latch **re-tested under the lock** — a caller that
waited returns the first caller's result rather than starting a second scan. `try_to_lock` runs
first purely so the wait is *loggable*: `init already in progress on another thread — tid=… is
waiting`. That line is the only externally observable proof the interleave was ever reachable, and
the verification note in [todo.md](todo.md) is built on it.

**The part the finding did not cover, found while fixing it.** A CE Disable landing mid-scan clears
the latch and tears the server down — and then the scan thread, whose cancellable loops all bailed
early on `Tot::Requested()`, sets the latch to `true` on its way out. The next enable would
short-circuit `UE5_Init` and run the entire session on those partial results. The latch is now
refused when `Tot::ShutdownRequested()` is set at that point. Deliberately **not** fixed by taking
`s_initMutex` in `UE5_Shutdown`: that would make a Disable block for the remainder of the scan and
re-create exactly the wedged-teardown shape [B49](audit-2026-08-04-findings.md) had just removed.

**B4 — one `thread_local`, two questions.** `Tot::t_backgroundWorker` was being asked both *"should
this thread ignore a pipe client's mid-command disconnect?"* and *"is this a repeating worker, so
refuse the off-game-thread invoke fallback?"*. For the six re-assert workers both answers are yes,
which is why one flag sufficed — until the CE mailbox poller needed the first without the second.

It needs the first because Fern's disconnect monitor **latches** `g_perCommand` when a UI client dies
mid-command, and only `Fern::Start` / `AcceptLoop` firstConn clear it: in a CE-only session it never
clears at all. `Aura::FindInstancesByClass` then bails at `n==0`, so every mailbox lookup —
`FIND_INSTANCE`, `LIST_INSTANCES`, `INVOKE_BY_NAME`, plus the class-scan fallback resolvers in
Wirbel/Solitar/Laufen/Hemmung — returns empty *while reporting* `scanned=<full pool>`, which reads
like "the object isn't there" rather than "the scan was cancelled".

It must not have the second, because that policy exists to stop a 10 Hz worker calling ProcessEvent
off the game thread for minutes; the poller carries the user's **one-shot** CE invokes, and blanket
-marking it would start refusing them with `-8` whenever the PE hook is down. So the one-line fix
the symptom suggests would have traded one silent failure for another.

Split into `Tot::t_cancelImmune` + `MarkCancelImmune()`, which is what `Requested()` now reads;
`MarkBackgroundWorker()` sets **both**, so all six existing worker call sites keep their exact
behaviour. Mimic's poller marks itself cancel-immune only.

**Evidence, not trust.** A cold WARN — `cmd=%d runs while a pipe client's per-command cancel is
latched` — fires once per latch, so a whole session costs one line, and the state B4 is about stops
being invisible. 9 EXPECTs across three roles (unmarked / poller / worker); the poller block's *"is
NOT a background worker"* assertion is the negative control for the tempting one-liner. Reverting
`Requested()` to the old flag was confirmed to fail the test before restoring it. 938 C++ green.

-----

## 2026-08-04 - The 8 MB "cap" was a kill switch, and the folder sweep used the one signal its sibling calls unusable (build 2585)

Audit #4 B31 + B37 + B38 — one commit, all in log/report retention.

**B31.** `CreateFileLogger` passed `fileSizeLimitBytes` and stopped there. Serilog defaults
`rollOnFileSizeLimit` to **false** and `rollingInterval` to **Infinite**, so the sink has no roll
point: once a category file reaches 8 MB it **drops every subsequent event for the rest of the
process** — no exception, no `SelfLog`, nothing. Meanwhile `docs/architecture.md:274` and the
CLAUDE.md log rule both state the cap *archives* mid-session, and the DLL half
(`Sein::RotateIfNeeded`) genuinely does.

Two reachable ways to hit it, neither exotic: Teleport's Auto-refresh runs a 500 ms timer that logs a
`Pipe TX`/`Pipe RX` pair per tick, so the pipe category dies mid-session with **no user action at
all**; and `UE5DUMP_PIPE_LOG_FULL=1` uncaps bodies, after which 8 MB falls within a handful of
batched responses.

Fixed with `rollOnFileSizeLimit: true` **and `retainedFileCountLimit: null`**. The second half is not
optional: Serilog defaults that to 31 the moment rolling is enabled, and a generation **count** limit
is exactly the retention policy this project deliberately replaced with an age-based one — leaving it
defaulted would reinstate count eviction by the back door. Retention stays owned by `PruneAgedLogs`,
which still sees the rolled files: they are named `{prefix}-0_001.log`, which matches its
`{prefix}-*.log` glob and does not end in `-0.log`, so the live-file guard leaves only the active
file alone.

**B37.** `CleanupProcessFolders` ranked folders by `DirectoryInfo.LastWriteTimeUtc` — the signal the
age-based sweep 30 lines below **documents as unusable**, because Windows bumps a directory's
timestamp when entries are added or removed but not when an existing child is appended to. A live
game's folder can therefore sink below a batch of stale ones and be deleted out from under it. Both
sweeps now share one `NewestWriteUtc` helper, and the eviction rule moved into
`SelectFoldersToEvict` — pure policy, same shape as `ProxyOrphanScanner.SelectExpiredReports`, so it
is testable without touching disk. The UI's own folder is now exempt from the count cap too, and
excluded *before* the cap so it cannot silently consume a kept slot.

**B38.** `GetAppDataPath()` is `%LOCALAPPDATA%` itself, so the app folder segment has to be added —
every other consumer does. Without it, the written record of a **destructive cleanup** landed in
`%LOCALAPPDATA%\Reports`: outside the System-tab data wipe, and outside "send me your app data
folder". (Reports already written to the old location stay there; they are harmless `.txt` files and
moving user data to tidy up is not worth the risk.)

Also corrected: `walk_payload_audit.py`'s docstring told the reader that setting
`UE5DUMP_PIPE_LOG_FULL=1` gives *"the LAST ~32 MiB … an unbiased one"*. **Both halves were false** —
nothing rotated at all, so the script was measuring the export's opening prefix and reintroducing
precisely the bias the flag exists to remove. It now describes what the code does.

New `LoggingServiceRetentionTests`: five cases pin the eviction policy (including "an actively
written folder outranks stale ones", the B37 failure in one assertion), and one writes past the real
8 MB cap and asserts the event logged **after** it is on disk — the only honest way to test that
property. **Verified by negative control:** removing `rollOnFileSizeLimit` again fails exactly that
test. 3161 green (+6), suite duration unchanged.

-----

## 2026-08-04 - Two exports that published something CE could not use (build 2581)

Audit #4 B2 + B3, together because they are the same shape: code that emits a value without
checking whether the consumer can do anything with it.

**B2 — a mangled symbol name published as if it were an AOB.** `Genau` copies the winning
signature's `pattern` into `gworldAob` (and the same triple for `&GEngine`) so a CE script can
re-find the address with `AOBScanModuleUE` plus a fixed offset. It copied it unconditionally. But
`SIG_EXPORT` entries store an MSVC **mangled name** in that field — Satisfactory resolves GWorld
through `?GWorld@@3VUWorldProxy@@A` — and the UI's "an AOB is available" test is just "the string is
non-empty". So the checkbox lit up, auto-ticked for anyone with the persisted preference, and the
emitted script scanned for the literal characters of a symbol name. Every address in the exported
table resolved to `??`.

New `IsCeReplayableAob(AobResolve)` in `Himmel.h` is now the single place that answers "can CE
replay this?" — true only for the RIP forms. `CallFollow` is excluded alongside the two symbol
forms: its pattern *is* a byte string, but the address comes from following the CALL and scanning
the callee, which no fixed offset into the match can express. All three also carry
`instrOffset/opcodeLen/totalLen = 0`, so the published range would have been the degenerate `[0,0)`
even if the pattern had been scannable.

`Test_Sig_IsCeReplayableAob` pins the classification and then sweeps the four shipped pattern tables
to assert that structural fact directly — and that the tables still *contain* symbol and call-follow
entries, because a gate nothing exercises is a gate that has silently stopped guarding.

**B3 — one `&` voided the whole export.** `<Description>` text is arbitrary game memory: TMap keys,
TSet elements, soft-object paths, DataTable row names. It was interpolated raw, while
`EscapeXmlContent` — which exists in the same file — was called at exactly two `<DropDownListLink>`
sites. A key like `Bow & Arrow` produces an invalid entity reference and Cheat Engine rejects the
**entire document**, so a multi-thousand-entry export imports as nothing with no clue which record
was at fault. `CheatTableBuilder` escaped its output correctly; this file was the outlier, and no
test compared them.

All **eight** Description emissions are escaped now, not the four the audit named — escaping an
already-safe string is a no-op, so "every Description is escaped" is a cheaper invariant to hold
than a list of the risky ones.

The new tests **parse the output with `XDocument`** instead of string-matching for `&amp;`.
Asserting on the entity would pass for output that is still malformed somewhere else; asking a real
parser is the property that actually matters, and it is the check this suite never performed. One of
them round-trips the text back out, because "the document parses" would also be satisfied by output
that dropped the characters rather than encoding them. Verified by negative control: removing the
escaping again fails all five.

**Not verified in-game:** B2 needs a modular build (Satisfactory-class) where GWorld resolves
through a symbol export. 3155 C# green (+5), 929 C++ green (+29).

-----

## 2026-08-04 - "There is a dxgi.dll here" was never evidence that it was ours (build 2577)

Audit #4 B29. `Methode.cpp`'s `IsAlreadyLoadedInTarget` decided "UE5CEDumper is already present in
this process" from a module's **file name** — version/dinput8/dxgi/winmm — plus a test that its path
was not under System32. `OnInjectAndConnect` acted on that with no identity check at all.

Every proxy flavour we ship is named after the Windows DLL it hijacks, so that test is equally true
of ReShade, Ultimate ASI Loader and SpecialK — and a UE game with ReShade installed is a
configuration this repo documents three times over. The user clicks *"UE5CEDumper: Inject &&
Connect"*, the walk matches on name, and the menu answers *"already loaded as 'dxgi.dll' — no
injection needed"*. Nothing is injected, the pipe never exists, and the UI's Connect fails with no
diagnostic pointing anywhere near the cause.

The name is now only a cheap **pre-filter** for the module walk. Ownership is decided by **PE
ProductName == `UE5CEDumper`** (`dll/src/version.rc` sets it on every binary we build), read via
`GetFileVersionInfoW`/`VerQueryValueW` over whichever language block the file actually carries rather
than assuming `040904B0`. That is deliberately the same rule the C# side already uses
(`DumperModuleDetector`), so the two detectors cannot disagree about whether we are loaded.

**The System32 path test is deleted, not kept as a belt.** A genuine Windows DLL fails the
ProductName test anyway, so keeping it would leave two rules that can only drift apart — and it was
the weaker one: the old check accepted *any* same-named DLL outside System32, so a plain copy of
`System32\dxgi.dll` dropped into a game folder (which is what some passthrough wrappers ship) was
claimed as ours.

Second defect, same function, fixed by the same rewrite: the walk used `GetModuleFileNameExA`, which
renders every character the ANSI code page cannot represent as `?`. A path like
`D:\Games\EVERSPACE™ 2\…` was displayed and logged as `EVERSPACE? 2` — unpasteable, in the one
message meant to help. The whole function went wide (`GetModuleFileNameExW`, `wcsrchr`, `_wcsicmp`),
with `Utf8Helpers::EncodeUtf16` only at the log/message boundary. `Heiter.cpp`'s sibling was fixed
the same way earlier.

A same-named module that is *not* ours now logs a line naming it, since that is precisely the case
that used to be misread in silence. A false negative here remains safe by design:
`UE5_StartPipeServer` detects an existing pipe and returns `INIT_SKIPPED`, so the worst case is a
second copy that declines to serve.

**Rule verified on real files**, not just reasoned: all four shipped proxies and `UE5Dumper.dll`
report `ProductName = UE5CEDumper`; all four System32 counterparts report `Microsoft® Windows®
Operating System`. **Not verified:** no ReShade/ASI-Loader install exists on this machine to serve as
the positive negative-control, and this path only runs inside Cheat Engine as a plugin. 3150 green.

-----

## 2026-08-04 - A table script cannot know where it lives, and "documented" is not "verified" (build 2576)

A user opened `dist/UE5CEDumper.CT` from Cheat Engine's recent-files menu with `UE5Dumper.dll`
in the **same folder**, and the script reported it missing — then told them to *"place
UE5Dumper.dll in the same folder as this CT file"*, which is precisely what they had done.

**Cause.** The `.CT` has no slot for "the folder this file lives in", only proxies for it:
`getMainForm().OpenDialog1.FileName` and `SaveDialog1.FileName`. Those are filled by
`File > Open` / `File > Save` and by nothing else, so a double-click or a recent-files pick
leaves both empty and only CE's install folder and `%LOCALAPPDATA%` get searched. Predates all
recent work (blame: `3e3c253`).

**Three rounds of being wrong, each corrected by a probe rather than by re-reading the docs.**
First I told the user CE exposes no registry API — wrong: `getSettings()` is documented at
`celua.txt:3504`. Then I built the fix on `getBinaryValue`, which `celua.txt:3516` advertises as
returning a bytetable. The user asked for evidence, and the measurement settled it:

| call | result |
|---|---|
| `getSettings()` | ✅ `TLuaSettings` |
| `Value["Recent Files"]` (REG_MULTI_SZ) | string of **length 0** |
| `getBinaryValue("Recent Files")` | **`nil`** |
| `getSettings("Plugins64").Value["00000000 A"]` (REG_SZ) | ✅ plugin path |

The control row is what makes it conclusive: the API, subkey selection and `Value[]` all work —
the **type** is unreadable. The File menu is no alternative either; probing
`getMainForm().Menu.Items` showed recent tables are not File-menu children, because `Load Recent`
is a submenu. What does work is `reg.exe` (measured, 1829 bytes), which renders MULTI_SZ
separators as the **literal two characters** `\0`.

**Shipped.** The candidate list is now a slot table ordered by confidence, and **every slot stays
in the report even when it had nothing to offer** — `[NOT SEARCHED]` now reads differently from
`[no DLL]`, which is the distinction whose absence made this hard to diagnose. Table-derived
folders rank above CE's install folder: a DLL there can only have been hand-placed and is likely
a stale build, and loading a mismatched build silently is worse than failing.

The recent-files read is **deferred** — it runs only after every cheap slot misses, because
shelling out flashes a console — and **self-healing**: a hit is written to the breadcrumb so no
later run needs it. Only the slot matched by this table's own filename earns that; "most recently
opened table" could be any game's folder.

New `DumperDllPathStore`: the UI records the folder it launched from (where the DLL ships beside
it) in `%LOCALAPPDATA%\UE5CEDumper\dll-path.txt`. Plain text, not JSON — the consumer is CE Lua,
which has no JSON parser, and it stays trivially AOT-safe. At the app-data **root**, not under
`Logs\`: verified that every `LoggingService` sweep is rooted at the log directory **and** globs
only `*.log`, so it is out of reach on both counts.

A miss is no longer fatal at `[ENABLE]`. The old code returned out of that chunk, leaving
`ue5_inject`/`ue5_shutdown`/`ue5_log` undefined, so the next tick died with *"attempt to call a nil
value"* — a second error that looked unrelated to the first. Enforcement moved into `ue5_inject()`,
where returning false unticks the record, with a file picker as the last resort. The dialog now
names the real cause and gives three ways out. And a failed run finally leaves something **in the
log**: it used to bail before `ue5_logInit()`, and since the log opens with `"w"`, the previous
*successful* run's log survived and read as if nothing had gone wrong.

**✅ VERIFIED in-game:** running `UE5DumpUI.exe` once, then opening the `.CT` from recent files,
resolves the DLL. **Still unverified:** the `reg.exe` fallback (delete `dll-path.txt` to exercise
it) — see [todo.md](todo.md#pending-live-game-verification-verify-only--no-code).

3150 green.

-----

## 2026-08-04 - The teardown was never slow, it was waiting for a client that might never come (build 2569)

Fixing B1 in build 2561 made `UE5_Shutdown` actually run for the first time — and that
exposed a **permanent hang** that had been unreachable behind the broken call. Found by
in-game verification of the very fix that created it.

**The measurement.** Two CE disables took 9.4 s and 13.3 s. Not a constant, so not a timeout. The
third disable — the one where the UI had already disconnected — produced **no `PipeServer: Stopped`
line at all**. `pipe-0.log` just ends. That teardown thread is still parked inside the game process,
holding `m_connMutex`, for the rest of the session.

**The cause.** `Fern::Stop()` called `CloseHandle(m_listenPipe)` under a comment calling it a
*"proven unblock"* for the accept thread's `ConnectNamedPipe`. It is the opposite. The listen
instance is a **synchronous** handle (`AcceptLoop`'s `CreateNamedPipeW` passes no
`FILE_FLAG_OVERLAPPED`), so closing it does not abort the parked call — it **blocks until that call
completes**, i.e. until somebody connects. And it did that while holding `m_connMutex`, which every
connection thread needs to unregister itself. The 9.4 s was how long the user took to click Connect.
With no client left, the wait is unbounded.

`Stop()` now sends a **wake-connect** instead: `CreateFileW` on our own pipe name, immediately
closed. `m_running` is already false, so `AcceptLoop` takes its `!m_running` branch and closes its
**own** handle — which also repairs a leak, because `Stop()` used to null `m_listenPipe` and the
accept thread's `if (m_listenPipe == pipe)` guard then failed, so nobody closed that instance. The
poke is gated on a listener actually being parked, so a second instrumented game (the pipe name is
machine-global) does not see a phantom connect.

**Three more defects found on the same path.** (1) `Start()`'s only guard was `m_running`, which
`Stop()` clears in its *first* statement — so the whole teardown window reads as "stopped", and
starting there move-assigns onto a still-joinable `m_acceptThread`: a standard-mandated
`std::terminate` with no log and no dump. New `m_stopping` flag, RAII-cleared. (2) A duplicate
`StopAllWatches()` sat *before* the cancel block, which is exactly the ordering the surviving call's
own comment warns against. Deleted. (3) The CE teardown called `UE5_StopPipeServer` and then
`UE5_Shutdown` — but `UE5_Shutdown` **is** that `Stop()` plus everything else, and runs it
deliberately *after* `Stark::Shutdown` so a pipe thread blocked in `EnqueueInvoke` gets its −7 and
unwinds. Calling it first inverted that, and because the CE call times out while the remote thread
keeps running, it put two teardowns in the process at once. All three emitters now call
`UE5_Shutdown` alone; the export and the not-loaded probe stay.

**Instrumentation, because this cost a full investigation to reconstruct.** `Stop()` logged exactly
one line, so a 5 s stall elsewhere and a 5 s expiry of the connection-drain wait were
indistinguishable from outside. It now logs entry with the connection count, per-phase elapsed ms,
and — explicitly — whether that wait was `satisfied` or `TIMEOUT` with how many connections remained.

Deliberately NOT done: raising the CE-side timeout (no finite value bounds an unbounded wait), adding
`CancelSynchronousIo` (this run never exercised `CancelIoEx` on the connection handles — it is
sequenced behind the blocking close and never ran — so there is no evidence it is broken; re-measure
first), and a mailbox `shutdownState` field (`Mimic::StopThread` memsets the struct mid-teardown, so
the flag would zero itself). 3127 green (+3).

-----

## 2026-08-04 - CE Disable tore nothing down and said it went fine; making it work first required making the DLL restartable (build 2561)

Audit #4 B1 + B30 + B40, one commit because fixing any one alone was worse than
leaving all three. Detail: [audit-2026-08-04-findings.md](audit-2026-08-04-findings.md).

**What the live test found.** CE's `celua.txt:589` gives the signature as
`executeCodeEx(callmethod, timeout, address, params...)` — `callmethod` 0=stdcall / 1=cdecl,
`timeout` in ms (`nil`/`-1` = forever, **`0` = no wait AND the call memory is never freed**), and
**the address is argument 3**. Every emitter here passed `(0, fn)`, so the address landed in the
timeout slot. Running the teardown for real logged `executeCodeEx returned nil for
UE5_StopPipeServer` / `UE5_Shutdown`: **it returns `nil` without raising.** No remote call ever
happened, and `UE5_Shutdown` had never run in the field.

That inverted the plan. The second defect — `UE5_Shutdown` latches `Tot::RequestShutdown()` and joins
the mailbox poller, whose `StartThread` had exactly one caller, `DllMain` — was not a *risk* of fixing
the arity, it was a *certainty*. So both halves shipped together.

**(a)** One `CeLuaHygiene.AppendCallDllHelper` now emits the call for all three routes, with a finite
5 s timeout, and checks the **result** rather than `pcall`'s status. That second part matters as much
as the arity: the generators did `return (pcall(executeCodeEx, 0, fn))`, whose parentheses truncate to
the pcall status — and since the malformed call returned `nil` instead of raising, both results read
`true`, the failure branch was skipped, and the CE window **auto-closed reporting a clean shutdown**.
The `.CT`'s `:148` comment ("executeCodeEx: retType 0=void, 1=integer") read `callmethod` as a return
type; that comment is how the bug got written, so it went in the same commit.

**(b)** `UE5_AutoStart` now calls `Tot::ResetShutdown()` + `Mimic::StartThread()` at the top. Both are
no-ops on a first start, and `StartThread` was already safely re-callable — it early-returns on
`s_running` and `StopThread` nulls the handle; nothing had ever called it outside `DllMain`.
`ResetShutdown` had to move here rather than lean on `Fern::Start`, which runs *after* `UE5_Init`: a
re-enable would otherwise rescan with `g_shutdown` still latched and every `StartWorker*` gate — which
reads that same flag — would refuse to spawn.

**(B30)** "Already loaded" was one branch doing one thing; it is now two, told apart by `initState`.
**SERVING** (a proxy or another instance owns the pipe) is not ours: say so and untick, so the disable
block can never run `UE5_Shutdown` against a pipe this record never started. **PARKED** (`UE5_Shutdown`
left it at IDLE) is ours: revive in place via `UE5_AutoStart` rather than re-injecting an
already-mapped DLL. `memrec` only exists in the record's own chunk, so `ue5_inject()` now returns a
bool and `[ENABLE]` acts on it. **(B40)** `ue5_callDLL`'s bare `getAddress` — which *throws* — is
`pcall`-wrapped, and the log handle closes on every path.

**One deliberate invariant was narrowed rather than quietly dropped.** A test forbade `executeCodeEx`
anywhere in `[ENABLE]`, justified by *"start-up is exactly when games block CreateRemoteThread"*. That
reason covers the start-up path only, and reviving a parked DLL genuinely needs a remote call — the
mailbox poller has been joined, so no memory-write channel is left to ask through. The rule is now two
tests: nothing calls into the target from `injectDLL` onward, and any remaining use must be the shared
emitter's exact text. Same change in the autorun twin.

New `CeExecuteCodeExArityTests` pins the three-argument form and the finite-non-zero timeout across
both generators **and reads the shipped `.CT` from disk** — the first automated coverage that file has
ever had, which audit #4 had flagged as the worst-covered surface in the project. Verified by negative
control: reverting the `.CT` to the two-argument form fails the test. 3124 green (+6).

-----

## 2026-08-04 - The Coordinate Library never persisted, and the thing that hid it was an optional parameter (build 2560)

Audit #4's first two fixes, shipped together because fixing one alone would have made the other
dangerous. Full audit: [audit-2026-08-04-findings.md](audit-2026-08-04-findings.md) (51 items).

**B27.** `App.axaml.cs` constructed a `CoordinateLibraryStore` and then called
`new MainWindowViewModel(...)` with **11 positional arguments** against a 12-parameter constructor
whose 12th is `CoordinateLibraryStore? coordLibrary = null`. The store bound to its default, was
forwarded as null into `TeleportViewModel`, and every persistence path — load, save,
`Delete`, `SavePreImportBackup` — early-returned on its null guard. So the Coordinate Library
worked perfectly in-session and lost everything on restart, with no exception, no log line and no
compiler warning. It had been that way since the feature shipped (builds 2257–2267), which is also
why `todo.md` still listed it as *"needs in-game verification"*: nobody had run it long enough to
notice, and no test could.

**Why no test could.** Every existing test that builds `MainWindowViewModel` passes *named*
arguments for the services it cares about — `MainWindowInjectHelperTests.BuildVm` passes 7. That is
correct for a unit test and structurally blind to this defect: the test supplies what it needs, so
it can never notice what `App` forgot. A new test that built the VM itself would have had the same
blind spot — it would assert that *the test* passes the store.

So the wiring moved out of `App` into `AppComposition.BuildMainWindowViewModel`, **whose parameters
are all required**. `App` calls it, `CompositionRootWiringTests` calls it, and the compiler now
enforces what optional parameters cannot. Verified the guard rather than assuming it: dropping the
argument again was re-tried on disk and the build failed with `CS7036 … required parameter
'coordLibrary'` instead of silently disabling the feature. Three tests: the positive (the store
arrives), a negative control (omit it and `HasCoordStore` is false — without this the positive could
pass for the wrong reason), and a structural one asserting `AppComposition`'s parameters stay
required and its arity matches the VM's.

**B6, and why it could not ship later.** "Clear all" had no confirmation and no pre-clear backup —
and unlike Delete/Duplicate it has no `HasSelectedCoord` gate, so with nothing selected it is the
only live button of the three and it sits next to Delete. That was **harmless only because nothing
persisted**. Wiring B27 first would have converted it into unrecoverable data loss on one misclick,
so it is in the same commit. The rolling `.bak` cannot cover this: the next Save overwrites it, and
`OnCoordZToleranceChanged` saves on every spinner nudge, so a cleared library survived roughly two
clicks of a NumericUpDown. New `SavePreClearBackup` writes a `.preclear.bak` — distinct from both
the rolling `.bak` and `.preimport.bak`, so a clear cannot eat an import's rollback copy or vice
versa — plus a confirmation dialog and a status line naming the backup file.

The tooltip said *"There is no undo."* It now says what is actually true, which is the point: audit
#4's cross-cutting 4a root cause is **the report and the reality being computed by different code
paths**, and leaving that string alone would have been a fresh instance of it.

3117 tests green (+7).

-----

## 2026-08-03 - Log retention had a hole exactly the size of a renamed category (build 2553)

Found while reading a real session's logs to verify the entry below. `Logs\UE5DumpUI\` still
held `walk-0.log`…`walk-3.log` and `ui-view-1.log` dated **2026-03-04** — five months past a
21-day retention window that the docs describe as age-based and absolute.

The cause is structural, not a missed edge case. `PruneAgedLogs` globs `{prefix}-*.log` for each
prefix in the CURRENT category list, so **the day a category is renamed or retired, its files
stop matching any glob and become immortal**. `walk` had been a UI category once; the `ui-`
mirror prefix only ever belongs in a game folder, never in the UI's own. Both then also slipped
past that method's `-0.log` live-file guard, which reads a retired category's live file as
untouchable forever. `CleanupOldDailyLogs` covers a different (daily-format) legacy shape and
`CleanupOldLogFolders` only removes folders wholesale, so nothing else caught them either.

New `PurgeOrphanedLogs` runs at UI startup as a third, widest pass and matches on **age alone**.
That is the point rather than a shortcut: knowing nothing about categories is exactly what lets
it also sweep the DLL-written per-game folders, whose category list the UI does not track and
should not have to. A file being actively written has an mtime of NOW, so it can never be older
than the window — a running game's live `-0.log` files protect themselves, *including categories
the UI has never heard of*. An explicit live-name set is kept as a belt for the handles the UI
process itself is about to hold, covering the one case age cannot: a category that stays silent
past the window while the session keeps running.

**Second bug, found on the way in:** `CleanupOldLogFolders` was called from the constructor at a
point where `_initLogger` was still `null` — its `"Deleted old log folder"` line dereferenced
null and threw straight into the adjacent best-effort `catch`. The folders were being deleted
correctly, but the record of it had never once been written. Both retention passes now run after
the loggers exist, and the purge reports its count.

4 tests: a retired-category sweep (the exact `walk-0.log` / `ui-view-1.log` shape), age being
the only rule, the named-live-file guard, and non-`.log` files left alone. UI-only — no DLL
change, so the existing DLL logs are cleaned by the UI's startup pass rather than by `Sein`.

-----

## 2026-08-03 - Live Walker grows a Forward button, and Back finally puts the view back (build 2550)

The Live Walker had a `Back` and no `Next`, and the reason was structural rather than an
oversight: `Breadcrumbs` is a **path**, not a history. `GoBackAsync` and a breadcrumb jump
both *truncate* it and the removed crumbs were dropped on the floor, so there was nothing to
go forward to.

What made this cheap is that a `BreadcrumbItem` already carries everything needed to
re-render its level — that is precisely how Back re-renders `prev`. So Forward is "push the
crumb back and render it", and the four render cases (live container / path-synthetic
container / GWorld actor-list root / ordinary re-walk) are the same four Back has always had.

**Invalidation rides `Breadcrumbs.CollectionChanged`**, next to the `IsRootGWorld` hook that
already lived there for the same reason. There are 7 crumb-push sites and 6 `Clear()` sites
today; a hand-placed `ClearForwardStack()` at each would have silently missed the *next*
navigation path someone adds. `Add`/`Reset` = a fresh navigation, so the forward history is
dead; `Remove` = Back / a breadcrumb jump, which *pushes* and must not clear. A
`_replayingForward` flag exempts Forward's own re-push, so multi-level forward works.

**The view state is the other half, and it was already written.** Bookmarks have captured
"what was on screen" since build ~1100 — the multi-select snapshot plus a top-visible-row
scroll anchor pulled from the View through the synchronous `CaptureViewAnchor` /
`ViewAnchorRef` carrier, restored by `RestoreBookmarkView`, which matches rows by
name+offset with a name-only fallback and silently skips rows that are gone. That is exactly
the semantics Back/Forward needed, so `BreadcrumbItem` gained `ViewSelectedFields` +
`ViewTopRow` (name+offset records, not `LiveFieldValue` refs — the forward stack holds
several levels and must not pin their field lists) and both events are reused verbatim. **Zero
new View code.**

Restore is a three-rung ladder so nothing regresses: a captured view state wins; otherwise the
legacy `ScrollHintFieldName` (all that Locate-in-GWorld and bookmark-re-resolution spines ever
have); otherwise nothing — no selection, grid at the top. That last rung is also the automatic
outcome when a Forward re-walk finds the object gone: rung 1 matches no rows and degrades into
it by itself. A *class-name* mismatch is louder, reusing the bookmark load path's check —
"object changed — selection not restored" — because restoring a stale selection onto a
different object would dress up wrong data as a successful return.

Back also got the upgrade for free (it previously restored only the single clicked row, matched
by name alone, and only scrolled that row into view rather than restoring the position).

**Shortcuts, browser-mapped.** `Alt+Left` / `Alt+Right`, mouse buttons 4/5, and the dedicated
`BrowserBack` / `BrowserForward` keys — all three routes, because gaming-mouse drivers are
often configured to emit the keystrokes instead of a true XButton, and handling one route means
"the side buttons do nothing" for some users. Verified through Avalonia 12.1: the Win32 backend
maps `WM_XBUTTONDOWN` → `RawPointerEventType.XButton1Down` → `MouseDown` → an ordinary
`PointerPressed` carrying `PointerUpdateKind.XButton1Pressed`, and `WM_SYSKEYDOWN` becomes a
normal `KeyDown` with `SC_KEYMENU` swallowed and `WM_MENUCHAR` muted, so `Alt+Arrow` neither
opens the system menu nor beeps.

Handlers sit at the **window root, tunnelling**: the field DataGrid claims Left/Right for cell
navigation and would eat the arrows, and when focus is on the tab header the Live Walker panel
is not even on the event route. Gated on foreground window **and** `TabItem.Tag == "LiveWalker"`
(by Tag, never by index — `MainTabIndex` documents how those drifted). Both gates live in the
pure `Helpers/LiveWalkerNavShortcuts`, because a physical 4th mouse button cannot be simulated
headlessly but the decision it feeds can. Avalonia 12 exposes no `IsRepeat`, so a held
`Alt+Left` would fire ~30x/second; the plumbing swallows repeats while `IsLoading`.

Forward rolls its optimistic push back if the walk throws — Back can leave a truncated spine
because that spine is still *consistent*, but Forward would strand a dead level on it.

`BreadcrumbItem` is flattened into the separate `PersistedCrumb` DTO for bookmark persistence,
so the two new fields **do not touch the on-disk schema** — no JSON context change, no migration.

24 new tests (forward-stack mechanics, invalidation through the collection hook, view-state
capture incl. the empty-selection case, class-mismatch reporting, walk-failure rollback,
shortcut policy incl. both gates). 3105 total, all green. UI-only: no DLL, pipe, or persistence
change.

**LIVE-VERIFIED on The Adventures of Elliot (UE 5.4), 2026-08-03 20:10–20:11.** From
`Logs/UE5DumpUI/view-0.log` — note the UI's own per-process folder, NOT the `ui-*.log` mirror in
the game's folder, which stops after the connect handshake and shows none of this:

```
20:10:46 NAV←Back removed=OwningWorld
20:10:47 NAV→Fwd OwningWorld left=0
20:10:52 NAV→ AISystem                  <- fresh drill while a crumb sat on the forward stack
20:10:54 NAV←Back removed=AISystem
20:10:58 NAV→ GameState                 <- and another
20:11:01 NAV→Struct PrimaryActorTick
20:11:05 NAV←Back removed=PrimaryActorTick
20:11:06 NAV←Back removed=GameState
20:11:07 NAV→Fwd GameState left=1
20:11:07 NAV→Fwd PrimaryActorTick left=0
```

Three things that log proves, none of which a unit test reaches:

- **The `_replayingForward` guard holds.** `left=1` on the first Forward is the whole ballgame —
  without the guard its own `Breadcrumbs.Add` would have raised `Add`, the hook would have wiped
  the stack, `left` would read 0, and the second Forward could not have happened.
- **Invalidation fires on the real drill-down path.** The two Forwards recovered GameState and
  PrimaryActorTick, *not* the AISystem crumb that a Back had put on the stack at 20:10:54 — the
  fresh drill at 20:10:58 correctly killed it. The `left` counter agrees: 2 entries, not 3.
- **A StructProperty crumb survives the round trip.** PrimaryActorTick carries the `S` flag, and
  the DLL's `pipe-0.log` shows the replay walk went out as
  `walk_instance addr=0xE1BEAC8 class_addr=0x7FF4DE1E45C0 id=210` — the crumb's `ClassAddr` came
  back intact. That same log line also confirms Forward genuinely re-READS from the game rather
  than re-showing cached data.

**The input route was the mouse side buttons** (maintainer-confirmed; the log itself cannot tell
a button click from `Alt+←` from mouse button 4). So the XButton path is proven end to end —
`WM_XBUTTONDOWN` → `PointerUpdateKind.XButton1Pressed` → the tunnelling window-root handler → the
command — which was the route with the most moving parts under it.

Still unproven: `Alt+←/→` and the dedicated `BrowserBack`/`BrowserForward` keys; the view-state
restore (entirely View-side, logs nothing); and the class-mismatch degrade, the tab/foreground
gates, and the walk-failure rollback, none of which were hit (no mismatch, no failure occurred).

-----

## 2026-08-01 - `ES2-0517` upgraded in place: 12m43s once, and a scan goes from >10 min to 30 s (build 2545)

The follow-through from the entry below, and the maintainer corrected the *method* too: the answer
is to **upgrade the existing project**, not to re-import it. A re-import costs a full re-analysis —
hours for a 169 MB binary with 28.6 M instructions. The language-version upgrade is a **migration of
the existing database**: every instruction, function and PDB symbol survives it.

`GROUND-TRUTH.md` has prescribed the fix all along — run the project once **without `-readOnly`** so
the upgrade persists — and it had never been done.

| | |
|---|---|
| one-time cost | **12 m 43 s** |
| a scan BEFORE | **>10 min, did not finish** — the window went into the upgrade, which `-readOnly` discarded |
| the same scan AFTER | **30 s**, zero `Updating language version` lines |
| `.rep` size | 12 GB → 12 GB |

It pays for itself on the **second** run. Only one project needed it: that log line appears for
`ES2-0517` and nothing else across every run today.

**Behaviour-preserving, checked not assumed.** `scan_*.txt` / `scan_*.tsv` / `consensus_*.txt` /
`blocks_*.tsv` all byte-identical to the pre-upgrade baselines; symbol digest identical
(5,298,149 symbols); 507,555 functions and 28,635,821 instructions unchanged. `meta.Created With
Ghidra Version` stays `11.3.2` — a migration does not rewrite what created the program, and per the
correction below that field is not worth preserving anyway.

**One self-inflicted hazard worth recording.** Taking a 12 GB safety copy first was right; then
timing a "before" run *against that copy* and killing it at a 10-minute timeout left **~6 GB of
orphaned transient DB files** in the backup plus a stale `.lock`. That is the documented
"`-readOnly` does not mean no writes" behaviour in miniature — and a reminder that the benchmark
you run to measure a thing can damage the thing.

-----

## 2026-08-01 - Two corrections to the entry below: the backup claim, and pinning a Ghidra version (build 2545)

The entry below is left as written (this file is append-only). Both of its closing claims were
wrong, and the maintainer caught both.

### "The corpus is single-copy" — WITHDRAWN

`X:\Ghidra_Projs_Backup` is on **the other machine**. I checked `X:` from the laptop, found no such
drive, and wrote "the corpus is single-copy … `X:\Ghidra_Projs_Backup` never produced a file" into
three documents. **A drive-letter check only reports the machine you ran it on.** This is the same
failure the measurement-discipline notes already warn about — a number (or here, an absence)
recorded without its conditions — and it slipped through because an absent drive *feels* like a
fact about the world rather than about one host. Backup state must be asserted from the machine
that holds it, naming that machine.

### "Keep an 11.3.2 install for ES2-0517" — BACKWARDS

`reimport_verify.py` graded `meta.Created With Ghidra Version: orig='11.3.2' rebuild='12.1.2'` as a
`MISMATCH`, which made the corpus's one 11.3.2 project look irreproducible and produced the advice
"keep that `.rep`, or keep an 11.3.2 install". That does not scale: on 12.2 or 13.0 **every**
project would read as irreproducible against a pinned original.

The rule is the inverse — *always rebuild on the installed Ghidra; if a new release breaks
something, stay on the working version until it is fixed.* An artifact's birth version is not a
property worth preserving. The field is now an informational note, never a failure, and
**`ES2-0517` grades `REBUILT-MODULO-ANALYSIS` like every other analysed row: 17 of 17 rows verified,
zero mismatches.** The follow-through is to re-import it from
`D:\UE_Analyze_data\Game archive\ES2\5.5-0517 (…)`, which also permanently removes the
language-version upgrade it re-runs on every open.

-----

## 2026-08-01 - A deleted `.rep` can be rebuilt: demonstrated, graded on a symbol-table digest (build 2545)

`docs/todo.md` carried a standing rule — no `.rep` may be deleted until a re-import is demonstrated
end to end, because *"the inputs exist"* is not *"a re-import reproduces the `.rep`"*.
`tools/ghidra/reimport_verify.py` now does it.

### The bar, and why counts are not it

A rebuild is graded on three things that fail independently: Ghidra's recorded executable
MD5/SHA256, a **SHA-256 over every `(address, name, type, scope, source)` in the symbol table**, the
block map with a per-block MD5, and byte-identical sweep output under the row's own `GS_TRUE`.
Matching symbol *counts* would pass a rebuild that put the right number of symbols at the wrong
addresses — precisely what a same-named-but-different PDB produces, and the failure this corpus is
most exposed to.

| rebuild | original | result | wall |
|---|---|---|---|
| `UE4.10-Game`, `-noanalysis` | raw import, no PDB | **REBUILT-IDENTICAL** | 95 s |
| `AudioMixerCore` (0.1 MB), `--analyze` | PDB + disassembled | **REBUILT-EQUIVALENT** | 98 s |
| `FactoryGame-CoreUObject` (4.2 MB, 684,805 instr.), `--analyze` | PDB + disassembled | **REBUILT-IDENTICAL** | 422 s |

### Determinism was measured, not assumed — which is what made the one anomaly readable

`AudioMixerCore`'s rebuild matched its original on the symbol digest and on every function /
instruction / defined-data count, and differed by **+1 data type and +7 in `# of Symbols`**. Two
readings were available: Ghidra's analysis is non-deterministic, or the original carries history.
Running the rebuild **twice** settled it — the two rebuilds were **field-for-field identical, 0
differing fields** — so it is the original that is unusual, not the method. It did not recur on the
40x larger CoreUObject, where every field including `# of Symbols` (159,554) matched exactly.

### What the corpus actually contains, and the discriminator that is not `.rep` size

`dump_identity.java` over all 57 rows / 74 programs. **The discriminator is instructions, not
functions** — applying a PDB creates functions and defined data without disassembling anything, so
`UE4.10-GameDev` shows 195,451 functions, 840,853 defined data and `Analyzed=false` with **zero
instructions**. A `.rep`'s size says the same thing and is likewise not an analysis signal.

| PDB loaded | disassembled | programs |
|---|---|---|
| yes | yes | **42** |
| no | yes | 18 |
| no | no | 13 (includes the 4 broken stubs) |

A raw import's symbols are **not PDB symbols**: `UE4.10-Game` has 184,438 `DEFAULT` (auto-named off
the `.pdata` function table) plus 1,233 `IMPORTED` (the PE export/import table). GROUND-TRUTH's note
that `-noanalysis` skips PDB application is correct; a PDB-loaded project looks different
(`UE4.20-Everspace`: 445,962 `IMPORTED`).

### Two findings that change the deletion calculus

**`ES2-0517` is the only project created by Ghidra 11.3.2** — the other 72 programs are 12.1.2. That
is the cause of its language-version upgrade on every open, and it is the one project whose original
toolchain a rebuild cannot reproduce.

**18 patterns have exactly one program where they resolve correctly** (Satisfactory 7 across four
DLLs, UE4.22-Satisfactory 4, Solarpunk 4, Everspace 2). That reads as a reason to keep those `.rep`s
and is not: now that `pe_sweep.py` reads binaries, the risk attaches to the **binary**. All **11
sole-source programs have an archived binary** — 0 missing.

### Still not licensed

Deleting anything. The corpus is **single-copy** — `X:` no longer exists and
`X:\Ghidra_Projs_Backup` never produced a file. A demonstration that rebuilds work is not a backup.

-----

## 2026-08-01 - The AOB sweep no longer needs Ghidra: 210/210 files byte-identical from the PEs (build 2545)

`scan_patterns.java` calls **four** Ghidra APIs and **zero** analysis APIs — `getImageBase`,
`getMemory`, `getBlocks`, and per block `isExecute` / `isInitialized` / `getBytes` / `getStart`. So
for the sweep a 181 GB Ghidra corpus is a container holding *image base + block map + bytes*, and
all three come out of the game binary. `py tools/ghidra/pe_sweep.py` now replays the whole
signature database from the binaries: **138 s** vs **773 s** for `sweep.sh`, no JVM, no Ghidra
install, and none of the GB-scale transient writes that `-readOnly` does not prevent.

### The bar was byte-identical output, because a sweep's wrong answers look right

A sweep answers "does the first hitting pattern by priority resolve correctly on this build". Wrong
answers to that stay plausible. Grading a replacement on a summary comparison — *the same patterns
are still green* — would pass a scanner that had quietly lost half its hits on one program, so
`compare_sweeps.py` compares **bytes, and the file set in both directions**.

* **210/210 identical**, 0 differing, 0 only-in-B, against a fresh `sweep.sh` reference.
* `aggregate_sweep.py`'s `REPORT.md` matches too, modulo one line naming the broken imports the PE
  route cannot produce. Matrix **162 ✅ / 59 ⚠️ / 2 ❌** — unchanged.
* The only A-only files are 12 belonging to **4 stub re-imports** (image base `0000:0000`, ~1 KB of
  DOS header mapped as code) that exist in the `.rep` and not in any PE.

### The section rule that 69 of 70 programs agreed on and one refuted

Ghidra's initialized block for a section is **`max(SizeOfRawData, VirtualSize)`**, zero-padded.
Both simpler rules fit real evidence and both are wrong:

* `min(raw, virtual)` — what `replay_patterns.py` uses — is short by 24-450 bytes on nearly every
  section, since raw is virtual rounded up to the file alignment.
* **raw alone reproduced Ghidra's `exec bytes` on 28 of 29 measured programs**, which is exactly
  the kind of agreement that reads as conclusive. The exception is **DQ7R**, a packed build whose
  *executable* `.debug` section has `vsz` 1024 bytes larger than `rsz`.

`dump_blocks.java` settles it rather than argument: it emits Ghidra's own map plus an **MD5 per
initialized block**, and `check_pe_memory.py` diffs the two. Matching starts and sizes would prove
only that the layout agrees; the digest is what proves the bytes do. **70/70 EXACT.** The 74 maps
are committed (`tools/ghidra/memory-maps/`, 62 KB) so the check runs without Ghidra and
`pe_memory.py` has a regression oracle instead of a remembered rule.

Ghidra synthesizes a `tdb` block at `0xff00000000` on PDB-bearing imports. That is exempted by a
**computed** criterion — non-executable and outside one signed-32-bit displacement of every
executable block, so no `getBlock`/`getLong` a scan performs can reach it — and every exemption is
printed, never dropped silently.

### A latent non-determinism in the Java, found by trying to reproduce it

`scan_patterns.java` built its consensus table in a `HashMap` and then stably sorted by vote count,
so equal-count rows came out in **bucket order** — a function of `Long.hashCode`'s spread and the
table's resize history. Worse, the listing truncates at 6 rows once votes fall to `n=1`, so map
order changed **which rows appeared**, not merely their order. Deterministic, but reproducible only
by emulating `java.util.HashMap`, and free to shift under a JDK upgrade. Now a `LinkedHashMap`.
Scope verified rather than asserted: over 222 files, **0 `scan_*` changed and 64 `consensus_*` did**.

### Tests

`pe_scan_selftest.py` — stdlib only, ~1 s, no corpus. Prefilter vs a brute-force matcher over 400
random patterns with **planted** matches (992 hits; unseeded, the same test found 12 and would have
passed with the prefilter deleted), the block model against a synthetic PE, the 40 000-hit detail
cap that **no corpus program reaches**, and Java's `HALF_UP` `%f` rounding. Every check is paired
with a negative control that must fail.

### What did NOT change

Authoring a new AOB still needs Ghidra — decompiler, xrefs, symbols. Only the replay moved. And
**no `.rep` may be deleted yet**: the acceptance test proves the sweep does not need them, not that
one can be rebuilt, which has still never been demonstrated.

-----

## 2026-07-30 - Leftover proxy cleanup: Report + Execute, and the first directory deletion in the app (build 2525)

Uninstalling a game leaves its folder behind when our proxy DLL is in it — Steam will not delete a
file it does not own. Measured on this machine: **9 leftover shells**, the oldest carrying a build-447
proxy. New card in the Proxy Deploy tab finds them and removes them.

### Two authorisations, not one — and the second is enforced by the kernel

The first design bound "may we delete the file" to "is the folder clean", so a folder shared with a
third party refused the whole row and left OUR litter on disk forever. The repo owner called that out:
removing our own DLL is a **must**, the folder is what is negotiable. Separated:

* **Our DLL always goes**, to the **Recycle Bin** (`SHFileOperationW` + `FOF_ALLOWUNDO`), never
  unlinked, so a wrong call is recoverable. Refused on non-fixed drives, because `FOF_ALLOWUNDO` on a
  volume with no recycler **silently hard-deletes** and the promise would be a lie.
* **Folders** are removed leaf→root with the **NON-recursive** `Directory.Delete`, one level at a
  time. That is a kernel-enforced emptiness check no pre-computed plan can fake: the walk stops by
  itself at the first level that still holds anything, so "prune only while empty, stop at the level
  with other files" needs no predicate at all. It also closes the scan→confirm→act race for free.

That change also fixed a real defect in the first design: its "each ancestor holds only the path
child" clause **refused 2 of the 9 real orphans** (`Deep Rock Galactic\FSD` also holds `Saved\`,
`Romancing SaGa 2\Game` also holds `GlobalConfig\`), leaving their DLLs forever.

### Report and Execute

Two buttons. **Report…** writes `%LOCALAPPDATA%\UE5CEDumper\Reports\leftover-proxies-<stamp>.txt` and
ShellExecutes it — the full plan, readable outside a modal, keepable. Built from the same rows Execute
acts on, so the two cannot describe different plans. It states its own limits in its own text
(snapshot, re-checked at execution), and three of those sentences are pinned by a test because without
them the button is a list with no context.

**"Smaller, never larger" is a property of the code, not a hope.** Execute re-plans from disk AND
**intersects** the fresh plan with what the row authorised. Re-planning alone was not enough: a folder
that merely stopped being shared between the scan and the click came back prunable, and we would have
removed directories the confirmation said would be left in place.

### Identity: two signals, OR

`ProductName == "UE5CEDumper"` plus a corroborating identity field, OR **6 of 13 founding export
names**. Measured: our proxies 13/13; System32 `version.dll` and `dxgi.dll` **0/13**, correctly
refused. The export leg earns its place for a job no other signal can do — it is the only one
evaluable through an **already-open handle**, which is what lets the pre-delete re-check hold
`FileShare.Read | FileShare.Delete` across verify→recycle and deny a writer restoring a real
`version.dll` (Steam "Verify integrity" is the realistic race, not an attacker).

### Discovery: three sources, and my first instinct was wrong

Measured coverage: the bounded Steam-library shape scan finds **9/9**; the DLL load log finds **2/9**.
I had called the log the primary source — it is not; its value is the **non-Steam** paths the Steam
scan structurally cannot see. Union of: Steam shape scan (authoritative), our deploy log, the DLL load
banner. Steam's `content_log.txt` was evaluated and **rejected as a discovery source** — it records
uninstall AppIDs but not paths (67% correlatable), its window is ~6 weeks, and it did not contain the
actual case at all. `appmanifest` absence dominates it informationally with no time window.

Liveness is two independent vetoes, either sufficient: no `appmanifest` naming the install dir (checked
**per library** — measured, the same installdir existed in two libraries at once, one live one a shell),
and no executable under the game root. Plus a leaf `.exe` pre-pass that also protects a proxy the user
deployed on purpose into a live game.

### Performance: 30 s → 0.7 s, and the cause was not where anyone guessed

The first version took ~30 s. Not the scan (measured: 129 games, 2520 directory ops, **0.74 s**) and
not the logs (**0.6 MB / 64 files**). It was `ClassifyLeaf` classifying EVERY file in every candidate
folder — a live game's `Binaries\Win64` holds dozens of DLLs, each getting a version-resource read and
possibly a full PE export parse. Early-exit on the first non-ours file (the quantifier is universal, so
the verdict cannot change) plus the leaf `.exe` pre-pass: **725 ms**.

### Adversarially reviewed, then fixed

A 9-agent review found **zero data-loss paths** — the `SHFILEOPSTRUCTW` x64 layout was independently
re-derived correct — but three places where consent did not match what ran, all now closed: the
delete-path re-check dropped the live-game veto (empty set), the executed plan was a fresh plan rather
than the confirmed one, and the confirmation printed "no game content anywhere" from a check that was
**never implemented** (dead `LiveContentDirNames`; the only content signal is the executable walk).
Also fixed: `TryReadAcfInstallDir` failed OPEN on a present-but-malformed `installdir` line — which
tears precisely while Steam is writing it, i.e. while the game is live.

UI defects from the same review: an unbounded list inside a `DockPanel.Top` child pushed the game
DataGrid off-screen at the measured 9 rows (now `ScrollViewer MaxHeight`), failures rendered in success
green, a cleaned row's checkbox stayed enabled, a Cancel button bound to the SCAN showed during a
delete, and a vanished file reported "nothing was removed".

### Verified, not assumed

3059 tests. The Recycle Bin was proven by a real call **plus** counting
`Shell.Application.Namespace(0xA)` before/after (34 → 35, item found by name) — `rc == 0` and
`!File.Exists` is also what a hard delete looks like. AOT trimmed publish clean, no IL2xxx/IL3xxx.
The measured filesystem traps behind all of this are in
[lessons-learned.md](lessons-learned.md#windows-filesystem-traps-measured-while-building-leftover-proxy-cleanup-build-2525);
the user-facing recipe is in [tips.md](tips.md).

Also fixed on the way, a real bug this feature depended on: the DLL's load banner wrote its host path
with `GetModuleFileNameA`, so non-codepage characters were destroyed — a real install logged as
`EVERSPACE? 2`, and `?` is not a legal path character, making that log line unusable. Now UTF-8 encodes
the wide path already in hand (`dll/src/Heiter.cpp`).

-----

## 2026-07-30 - Pre-UE4 (UE3) refused by design; the too-old gate turns out to have never fired (build 2508)

Prompted by a UE3 title — Gal\*Gun: Double Peace (`GG2Game.exe`, UE3 x64, 51 MB). The question was
whether the existing `MIN_SUPPORTED_UE_VERSION = 411` gate already stops it. It does not, and the
reason generalises further than expected.

### The gate could not fire, and three of its four conjuncts failed

Measured against the real binary: `DetectVersionFromPEResource` returns 0 (its `ProductVersion` is a
**game** version, `1.0.10897.0` — `major` is neither 4 nor 5, and the StringFileInfo strings hold no
`++UEx+Release-` tag). Re-running `DetectVersionDetailed`'s Tier 1/2/3 loops over the reconstructed
mapped image: **zero hits on all three tiers** — the needle table floors at `"4.18."`, so no entry
can match a UE3 version. `CompanyName` is `Epic Games, Inc.`, and `kPublishers` holds only
`SQUARE ENIX`, so the publisher bias does not apply either. Control lands on `Genau.cpp`'s
detection-failed fallback: **`UEVersion = 504`, `bLowConfidence = true`, `bVersionDetected = false`**.

So the gate's four-way AND fails on `504 < 411` **first** — the decisive one. Correcting the
confidence flags would change nothing; the fallback *number* is above the floor.

### A stale comment, and a stronger claim than it was making

The gate's rationale comment named **Fantasynth and NEKOPALIVE** as the titles that burned ~4 s to
reach "no winner" before it existed. Both are wrong for that role, and the HintCache proves it:
`Nekopara.exe` is recorded `ueVersion=411, versionDetected=true, gObjects.method="aob"` and
`Fantasynth-Win64-Shipping.exe` `ueVersion=413, ..., "aob"` — both at or **above** the floor, both
with GObjects **resolved**. `test-games.md` already lists NEKOPALIVE as fully working. They are the
two titles that *define* the 4.11 support floor, not examples of gated failures. Comment corrected.

The defensible form of the claim is broader: **across all 30 games in the local HintCache the
minimum detected version is 411, so this gate has never fired on a real title.** Its only trigger is
a PE `ProductVersion` of literally 4.0–4.10, which no installed game reports. It gates "an honestly
self-labelled UE 4.10", not "an engine too old" — the 4.10 evidence is the two reference builds in
the AOB corpus.

### `Grimoire::PRE_UE4_SENTINEL_VERSION = 300`

`CountPreUE4Markers` runs in `DetectVersionDetailed`'s **terminal branch** and, on ≥2 of 4 markers,
returns 300 at tier 1 — which trips the existing gate. Two decisions carried the design:

* **A sentinel number, not a new bool.** A flag computed inside detection would be false on launch 2,
  because a HintCache hit skips detection entirely; the number round-trips for free. It also reuses
  all 12 non-presentation wiring sites verbatim — no new `EnginePointers` field, `Frieren` global,
  `Fern` JSON key, `EngineState` property or `DumpService` parse line.
* **Terminal-branch placement is the false-positive defence.** Reaching that line already proves the
  PE resource, Tier 1 (ASCII + UTF-16LE), Tier 2 and Tier 3 all found nothing, so "no UE4/UE5
  evidence" is a property of the control flow rather than a separate test that could be written
  wrong. Absence alone is never sufficient — that is exactly the state of SUPPORTED stripped-tag
  titles (Elliot detects as 0 by the identical route), so gating on it would refuse working games.

Markers, measured: `UnrealEngine3`, `SeqAct_` (UE3 Kismet's native-registration table — the object
model, not a strippable version string), `PhysXLoader64`, and an Epic `LegalCopyright` whose newest
year is ≤2013. **65 supported binaries scored 0/4** (30 reference builds UE 4.10–5.8 + 35 installed
UE games); Gal\*Gun scores 4/4. Rejected: bare `UE3` (3–85 hits in every one of the 30 supported
builds) and nonstandard section names (27 of 30 carry one). Full table + the Manor Lords near miss
(`Epic Games` copyright with **no** year, which is why an explicit year is required) in
[technical-notes.md](technical-notes.md).

### Two latent holes closed on the way

* **The publisher rule had TWO sites, not one.** `Genau.cpp`'s cache-reuse branch re-applies
  `publisher != nullptr → bLowConfidence` **live on every launch after the first**. Guarding only
  the fresh-detection site would have gated a thumbprinted pre-UE4 title correctly once and then
  silently un-gated it forever. Both now carry `&& UEVersion >= MIN_SUPPORTED_UE_VERSION`. No
  behavioural change for any supported game: three independent floors keep every real version ≥411.
* **`ShowVersionTooOldWarning` was never notified.** It is a plain computed property and
  `_isVersionTooOld` has no `[NotifyPropertyChangedFor]`, so it was missing from
  `NotifyComputedProperties` — meaning the existing red banner only rendered if its binding happened
  to be evaluated for the first time *after* `Update()` had run. On any refresh of an already-attached
  panel it stayed hidden. Both banners are now raised, with a regression test.

### Honest messaging, and Extra Scan actually disabled

A second string (`str.Pointers.EnginePreUE4`) rather than reusing the 4.10 one, because that text
ends "set a UE version override" — advice that cannot work for UE3 at any version, and the override
list has no value below 4.18 anyway. The new text says what the engine is, that the skip was
deliberate, and that Extra Scan is disabled — and it is: `CanExtraScan` now excludes a refused
engine, and the DLL refuses `CMD_RESCAN` / `CMD_APPLY_RESCAN` too, so the pipe stays honest even if a
client ignores the hidden button. That second refusal matters independently: apply re-enters
`Aura::Init` and `ValidateAndFixOffsets(g_cachedUEVersion)` **outside** `FindAll`, so the gate's
early return does not by itself fence a refused version out of the version-dependent code.

The override deliberately stays **enabled**, and the message points at it: it is the only escape if
the marker check ever false-positives on a real UE4/UE5 game, and the gate's `!bUserOverride`
conjunct exists for exactly that.

`kVersionDetectLogicRev` 2 → 3. Mandatory, not cosmetic: a UE3 game already cached as `ueVersion=504`
would otherwise be restored from cache forever and never re-detected — the fix would silently not
apply to the machine that reported the problem.

### LIVE-VERIFIED, both sides (build 2516)

**Gal\*Gun: Double Peace** (`GG2Game`, PE hash `57FC3FE20352D000`), `scan-0.log` reads exactly the
intended sequence: `PE VERSIONINFO Product=1.0 File=1.0 — unrecognised` → memory tiers miss →
`PreUE4: Epic copyright newest year 2012 (PRE-UE4)`, `marker 'SeqAct_ (UE3 Kismet)' hit at
0x244DAF9`, `marker 'UnrealEngine3' hit at 0x2729BD0`, `marker 'PhysXLoader64 (PhysX 2.8)' hit at
0x2AA61D0` → `PRE-UE4 engine POSITIVELY identified (4/4 markers, 2 needed) -> sentinel 300` →
`UE Version = 300 (tier=1, detected=yes, lowConfidence=no)` → `SKIPPING the scan`. **No AOB pass
ran at all.** The marker sweep cost **~31 ms** (45.959 → 45.990); the 1.9 s before it is the
pre-existing Tier-2/3 needle scan, which the gate still cannot avoid because it sits after
`DetectVersionDetailed`.

**The Adventures of Elliot** — the mandatory regression target, being the one supported title
measured on the same version==0 path. `pre-UE4 markers 0/4, below the 2 needed` → falls through →
`UE Version = 427 (tier=0, detected=no, lowConfidence=yes, publisher=SQUARE_ENIX)` → all four
globals `(aob)`, name sanity **10/10**, 352,853 objects. The narrowed publisher rule correctly does
NOT fire here (427 ≥ 411), so its low-confidence badge is untouched.

### One thing only a live run could show

The banner rendered correctly, but the three pointer cards below it read **"🔴 All AOB patterns
failed"** and **"⚠ AOB failed — found via not found"** — after a scan that never happened. Those
lines key on `PatternsHit == 0`, and on a refused engine 0 hits means 0 patterns **tried**, so they
were not merely noisy but false, and they contradicted the banner directly above them. Every
per-pointer failure line is now silent when `IsVersionTooOld`: `ShowG{Objects,Names,World}Warning`,
`G{Objects,Names,World}AobAllFailed`, `IsGEngineNotFound` (its method is only `"not_found"` because
`FindGEngineSlot` never ran) and `IsSparseDelegatesUnsupported`. That last one is factually true for
a real UE 4.10, but printing "sparse delegates did not exist yet" beside "this engine is
unsupported" implies the rest of the panel works — so both refusal flavours now show exactly one
explanation. Paired tests pin both directions: silence when refused, and a genuine 0-hit sweep on a
supported engine still reports itself.

### UE3 support itself: evaluated, NOT built

The blocker is not the array shape — `Aura::ArrayLayout` can already express UE3's flat
`TArray<UObject*>` as `{0x00, 0x0C, 0x08, -1, -1}`, an *easier* shape than 4.10's inline chunk table.
It is that `OFF_UOBJECT_CLASS`/`OFF_UOBJECT_NAME` are `constexpr` and baked into the scan validators
themselves, so `ValidateCyclicClassChain` rejects even a **correct** UE3 GObjects address (measured
on this binary via its own exported `UObject::GetOutermost`: `Outer` @+0x40, `Class` @+0x50). ~113
use sites plus a bootstrap probe, and an AOB layer that can never have a symbolised oracle (UE3 was
never public source; the only public UE3, UDK, is Win32-only). 22–42 dev-days for an order of 10–30
**x64** UE3 titles, against a dumper that is x64-only by construction. Not pursued.

-----

## 2026-07-29 - UE 4.10 + stock 5.4.4 join; 58 -> 70 programs; the GNames bisection closes at 5.4 (build 2505)

Same-day follow-on to build 2503. Two things landed: the full sweep that 2503 could not run, and
the corpus's oldest binary.

### UE 4.10.4, and why its ❌ is the point

`UE410_Game_Shipping` + `UE410_Game_Development`, both full-PDB, both Epic-stock (CL 2872498,
`++depot+UE4-Releases+4.10`, `IsLicenseeVersion=0`). **Nothing was compiled** — 4.10 needs VS2015,
which is not installed. The launcher engine already ships monolithic `UE4Game-Win64-Shipping.exe`
(38.7 MB) and `UE4Game.exe` (83 MB) with PDBs in `Engine/Binaries/Win64`.

That generalises, and it is now step `00` of the derivation recipe: **check for the engine's own
prebuilt game targets before packaging or compiling anything.** Surveyed across the installed
engines — 4.23 / 4.27 / **5.4** / 5.7 / 5.8 ship all three configs, 4.10 / 4.15 ship Shipping +
Development. The caveat is that these are content-free engine defaults: fine for engine globals,
useless for the gameplay-feature matrix, which still needs a packaged Character-based project.

**GObjects is unresolvable on both rows, and it is meant to be — the only ❌ in the matrix.** It
converts "below 4.11 is UNSUPPORTED" from an assertion into a measurement with two independent
causes:

1. *It cannot be found.* At 4.10 the array is a **function-local static behind a magic-static
   guard** in `GetUObjectArray()`; consumers reach it by `call` and never materialise the address
   inline, while all 52 `GOBJ_*` patterns are `lea reg,[rip+GUObjectArray]`-shaped. 4.11 promoted
   it to a plain global — which is exactly why 4.11 Nekopara resolves one row below. Measured: 74
   GObjects candidates on Shipping / 105 on Development, with the true VA and its `+0x10` alias in
   **neither list at any rank**.
2. *It could not be read anyway.* Per `4.10.4-release` source, `TUObjectArray` is
   `TStaticIndirectArrayThreadSafeRead` and **`FUObjectItem` does not exist** — elements are bare
   `UObjectBase*`. No `ArrayLayout` preset models that.

So mining a `GetUObjectArray`-shaped pattern would buy nothing. GNames/GWorld/GEngine resolve
normally, so the rows still earn their scan as the oldest coverage for those three.

Truth for GObjects came from disassembly (no public symbol exists): the guarded init does
`lea rbx,[rip+X]`, passes rbx as `this` to `??0FUObjectArray@@QEAA@XZ` and returns rbx —
corroborated by `GetObjectArrayForDebugVisualizers`, which is literally `GetUObjectArray();
add rax,0x10` and therefore **measures** `ObjObjects@+0x10` at this version instead of inheriting
it. `pdb_globals.py` now prints that route as a hint when GObjects has no symbol, so the dead end
is not rediscovered; both of its validation rows (4.23-Flying, 5.8-StackOBot) still reproduce
byte-for-byte.

### Stock UE 5.4.4 — the last UE5 version without a symbolised oracle, and it closed the bisection

`ThirdPerson54_{Shipping,Development,DebugGame}`, Epic stock (CL 35576357, `++UE5+Release-5.4`,
`IsLicenseeVersion=0`). Packaged rather than taken from the engine's prebuilt `UnrealGame` target,
deliberately: the prebuilt one is free but content-free, so it cannot serve the gameplay-feature
matrix. These binaries serve both jobs. All five targets double-derived on all three configs
(`pdb_globals.py` + 151-pattern replay, exact agreement).

**The non-Shipping GNames bisection is CLOSED — the edge is 5.3 → 5.4:**

| version | non-Shipping GNames | lands on | wasted |
|---|---|---|---|
| 5.3 Dev + DebugGame | **15/15 patterns correct** | `GNAM_ES53_1` | **0** |
| **5.4.4 Dev + DebugGame** | **1/6** | `GNAM_V1` | **2,240** |
| 5.7.4 DebugGame | 1/8 | `GNAM_V1` | 2,199 |

Budgeted at two installs (5.4 *and* 5.6); cost one, because 5.4 collapsed outright instead of
landing mid-interval. 5.5/5.6 lose their bisection argument and are now coverage-only. If a fix
pattern is ever mined, **5.3-vs-5.4 is the pair to mine it against**.

Two more results it was actually ordered first for:

* **Every UE5 version now has a symbolised oracle.** The new Shipping row also **corroborates
  UE5.4-Elliot**, whose truth is disassembly-derived with no PDB — GObjects 8/15 vs 9/15, GNames
  13/16 vs 13/17, GWorld 15/16 vs 13/14. First independent check that row has ever had.
* **MindsEye has a stock control at last.** The engine is **5.4.4, MindsEye's exact patch
  version**, so `mindseye-fork-notes.md`'s "the fork changed X" claims become measurable deltas
  instead of inference.

### The backup measurement that overturned the manifest plan

`D:\UE_Analyze_Data\Game Binary backup` (30 games / 11 GB) hashed against the manifest's
import-time `binary_md5`: **24 rows byte-identical to the corpus build, Palworld included.** With
`Game archive` and `Varies Version builds` covering the archive and self-built rows, the manifest
splits **33 rows with 2 copies / 3 with 3 / and just 2 with none.**

That reverses the earlier recommendation to add `--merge` to `build_corpus_manifest.py` before
regenerating. `steam_buildid` is a **last-resort** route, and 36 of 38 rows never need it — losing
one only matters if the bytes are also gone. The two that qualify are the `UE5.5-Everspace2{,b}`
same-appid pair, and both are already preserved in `corpus-provenance.tsv` as hand-resolved
`STEAMDB-MANIFEST` entries. Regenerating is safe; `--merge` is a nice-to-have because the nulling
is *silent*, not because data is at risk.

### Manifest regenerated — and `duplicate_copies` turned out to lie on drifted rows

`build_corpus_manifest.py` re-run: **38 → 57 entries**, `preflight.py` **DRIFT → `GO (exit 0)`**,
`pdb gaps 19 → 4`, `wrong build 1 → 0`, `unknown 16 → 0`. Cost was exactly the predicted one row —
Palworld's `steam_buildid` / size / sha256 nulled, all three already in `corpus-provenance.tsv`.

The before/after diff caught something the generator's own output cannot show, since it prints only
`N tags -> path`: for Palworld it wrote **"the `.rep` is the last copy"** and `duplicate_copies: []`,
when two byte-identical copies of the corpus build exist.

**The first diagnosis of that was wrong, and the correction is the interesting part.** It was not
"compares today's bytes instead of `binary_md5`" — the md5 test has always used Ghidra's
import-time hash. The cause was a **size prefilter** sizing candidates against whatever sits at
`binary_last_seen` *today*; on a drifted row that is the replacement build, so the surviving copy
(old size) was skipped **before its md5 was computed**. A fast path whose guard assumed "the file
on disk is the corpus build" — false precisely where the field matters. Fixed with
`size_prefilter=(state == 'MATCH')`; Palworld goes **0 → 2** copies. Unlike a nulled field, which
reads as *unknown*, `[]` plus that note was a positive false claim.

### PE link timestamps — there is an authoritative flag, and plausibility lies

Asked whether a build's own time could stand in for file mtime (which dates the copy, not the
build). First answer used a plausible-date heuristic and a cross-config-spread discriminator.
**Both were wrong; a `2039-01-13` value that looked merely odd is what prompted the recheck.**

The real test is a flag: with `/Brepro` the linker overwrites `TimeDateStamp` with a **content
hash** and emits debug-directory entry **type 16 (`IMAGE_DEBUG_TYPE_REPRO`)**. Measured:

* **Plausibility is worthless.** ~1 in 5 hashes lands in a 2000-2030 window. **Hogwarts Legacy
  reads `2025-11-12` and Elliot reads `2026-07-15` — both hashes**, and the heuristic had scored
  Hogwarts as a real link time.
* **It is per-CONFIG, not per-version.** Epic's UBT sets `/Brepro` on **Shipping only** from ~5.3;
  Development and DebugGame keep real link times at every version tested (4.10 → 5.7). UE 5.8
  non-Shipping zeroes the field entirely — a third state.
* That also kills the cross-config-spread discriminator: 5.4's 393-million-second spread came from
  comparing a hash against a *real* time, not two hashes. Right conclusion, wrong reasoning.
* **Studios choose for themselves** — Hogwarts is `/Brepro` at 4.27 while DQ7R, same version, is
  not. The UE version predicts nothing for a shipped game.

`pdb_match.py` now reports the flag on every check. **The standing rule, which lives here:** a PE
`TimeDateStamp` is a DATE only when `IMAGE_DEBUG_TYPE_REPRO` (type 16) is absent — a corroborating
signal then, never the primary answer. A `/Brepro` hash is still deterministic per link, so it works
as a weak IDENTITY; it is just never a clock. For "is this the same build?" skip timestamps entirely:
`binary_md5` answers it exactly, and for a PDB the CodeView **GUID+Age** is a true per-link identity.

### `tools/pe/pdb_match.py` — "can I trust this PDB for this binary?"

A matching filename proves nothing: a PDB from a different build of the same game loads without
complaint and yields plausible-looking wrong addresses. The tool compares the PE's CodeView
**GUID + Age** against the PDB's info stream (fresh GUID per link, so a rebuild cannot fake it),
then checks the publics stream is not a stripped shell. `--scan` walks a backup tree.

Self-tested both ways before use — a known-good pair passes, and the 5.4 Shipping exe against the
5.4 Development PDB is correctly rejected. (The first run failed on a known-good pair and caught a
real bug: `IMAGE_DEBUG_DIRECTORY.AddressOfRawData` @20 was being used as a file offset instead of
`PointerToRawData` @24, which silently reads as "no CodeView record".)

Applied to `Game Binary backup`: **9/9 pairs valid**, and 6 of the 7 that are corpus oracles
reproduce their recorded `GS_TRUE` byte-for-byte. The 7th (Solarpunk) differs only cosmetically —
`GObjects=A|B` is a set, not an ordered pair, and its GNames legitimately has no `pdb_globals`
route. Recorded in tools/README.md: **the strongest PDB check is reproducing a row you already
have**, not the pairing test.

### The full sweep — 65 programs / 52 oracles, UE 4.10-5.8 (70 / 55 after 5.4)

54 rows, run end to end for the first time since the 5.3 / 4.15 / 4.27.2 additions. The 5.8.1
Development row that build 2503 left blocked on a Ghidra lock is included.

* **The non-Shipping GNames collapse is bisected to 5.4/5.5/5.6.** The 5.3 Development and
  DebugGame rows land on `GNAM_ES53_1` with **zero** wasted validations — cleaner than 4.23/4.27 —
  while 5.7.4 / 5.8.0 / 5.8.1 / Titan fall through to `GNAM_V1` after 2,199 / 2,369 / 2,372 /
  2,424. 4.10 and 4.15 extend the healthy band downward, so it is a sharp UE5-era edge, not drift.
* Two documentation caveats discharged: `UE423_Flying-Win64-DebugGame` is a live sweep row rather
  than an orphan `.rep`, and `ISDefenseEditor_UE410` is no longer the sole evidence for the
  pre-4.11 floor — both noted in the corpus-preservation drop list.

-----

## 2026-07-29 - Ground truth without Ghidra; corpus 51 -> 58 programs; three documented beliefs corrected (build 2503)

A corpus/tooling pass, not a feature one. The through-line: **almost everything this project uses
Ghidra for does not need it**, and once that was measured the corpus grew, three long-standing
claims turned out to be wrong, and the sweep came back green.

### `tools/pe/pdb_globals.py` — the five globals straight out of a PDB

Standalone MSF 7.00 reader (stdlib only, same house style as `pe_imports_exports.py`). Walks the
publics stream, maps `(segment, offset)` through the PDB's own section headers, and prints a
paste-ready `GS_TRUE=` line. GNames has no usable symbol at any version, so it is recovered by
disassembling `FNameDebugVisualizer::GetBlocks` (`lea rax,[&Blocks]; ret`) and subtracting `0x10` —
printing the bytes it read, so the `-0x10` stays checkable.

**Validated before being trusted**: it reproduces the `UE4.23-Flying` and `UE5.8-StackOBot` rows
byte-for-byte, including the `GetBlocks @0x14062c010` / `48 8d 05 f9 83 82 02 c3` detail already
recorded in `sweep.sh`. Re-run those two after touching it.

The 5.8 GObjects `base|base+0x10` alias — the one manual judgement call — is now **auto-detected**
from `??1FFieldClass@@UEAA@XZ` (`U` = virtual dtor = 5.8+), the one-second test GROUND-TRUTH.md
already documented. Verified across five eras. Also made the module importable (`if __name__ ==
"__main__"`), which is what enabled the offline function-body check below.

This replaces step 2 of the "Deriving truth for a new game" recipe — a ~10-minute headless run per
binary — with about two seconds.

### AUTO ANALYZE IS NOT NEEDED FOR THE SWEEP, and it is ~88% of a `.rep`

`scan_patterns.java` touches only `getMemory()` / `getBytes()` / `getImageBase()` — no
`FunctionManager`, no `Listing`, no `SymbolTable` — which is why `sweep.sh` already passes
`-noanalysis -readOnly`. Measured on one 49 MB binary:

| | `.rep` size |
|---|---|
| `-noanalysis` import | **169 MB, 46 s** |
| fully analysed | **1,369 MB** |

**8.1x — analysis is 88% of the artifact the sweep never reads**, and `scan_patterns.java` returns
the *identical five verdicts* from the raw import. Of the 15 Ghidra scripts, 5 need no analysis; the
other 10 are all *read-the-code* pattern-mining tools — analyse into a throwaway project and delete
it. A 5.8 DebugGame project had previously been deleted because auto-analyze would not finish, when
the import never needed it. Written up in [corpus-preservation.md](corpus-preservation.md).

### Corpus 51 -> 58 programs / 36 -> 43 oracles, and the sweep is green

Imported with `-noanalysis` and each verified post-import: 4.23 DebugGame, 4.27.2 x3 configs
(Development / DebugGame / Shipping), 5.7.4 DebugGame, 5.8.0 DebugGame, 5.8.0 Titan DebugGame,
plus rows for 5.8.1 Shipping + Development. Every value double-derived (`pdb_globals.py` + a
151-pattern byte replay) with zero contradicting candidates.

**`Every target present in every oracle resolves to the correct address.`** The `GOBJ_DI427_1`
demotion (105 -> 256, below) is verified neutral: no lander moved, the band audit came back empty.
Two rows did not run — `UE5.8.1-StackOBot{,Dev}` were LOCKED by an open Ghidra session, which
`preflight.py` flagged in advance as exactly those two.

This also **closes the "verify the corpus after moving it to the HDD" item** — a full sweep is the
only real integrity check for a silently corrupted `.rep`, and every pre-existing oracle still
resolves on the same landing pattern. Scope it honestly though: `aggregate_sweep.py` overwrites
`REPORT.md`, so this was a comparison against the rows read earlier in the session, not a literal
file diff against the pre-move report.

### `GOBJ_DI427_1` demoted 105 -> 256: it is a build-config fingerprint, not a UE 4.27 one

Proven by a controlled A/B the corpus could not do before — one project, three configs, one engine:

|  | `DI427_1` | `DI427_2` | `DI427_3` |
|---|---|---|---|
| 4.27.2 Development | 832 | 1415 | 246 |
| 4.27.2 DebugGame | 832 | 1415 | 246 |
| **4.27.2 Shipping** | **0** | **0** | **0** |

`_1`/`_2` anchor on the `E8 <check-fail>; nop; int3` that `check()` emits; `_1` additionally needs
the 32-byte `TStatId` `FUObjectItem` (`STATS` is 0 in Shipping). `_1` fires on **1 of 51** programs
and is never selected, so it should not hold a batch-1 slot every shipped game pays to scan. `_2`
(5 programs) and `_3` (4) do reach real Shipping builds — Satisfactory ships with checks on — and
keep their bands.

### THREE CORRECTIONS, all to things this repo asserted confidently

1. **`UCheatManager::God`/`Fly`/`Ghost` are NOT body-stripped in Shipping.** Read the bytes at
   `?God@UCheatManager@@UEAAXXZ` out of a stock 4.27.2 *Shipping* EXE: a full body, not a `ret`.
   `CheatManager.cpp` @ 4.27 has exactly one `#if !(UE_BUILD_SHIPPING || UE_BUILD_TEST)` block and
   it wraps `TickCollisionDebug`, not the cheat commands. **The real gate is that no
   `UCheatManager` INSTANCE exists** — `AddCheats` spawns one only when `AGameModeBase::AllowCheats`
   (`NM_Standalone || GIsEditor`) permits, so the invoke lands on the CDO and `GetPawn()` yields
   nothing. Same symptom, different cause; and the fix is not "give up" but "you need an instance",
   or do what `Solitar` already does and set `bCanBeDamaged` by reflection — which is literally what
   `God()` itself does.
2. **GNames on a non-Shipping UE5 build.** Called "a 5.8 thing" (5.7.4 disproved it), then "all 37
   patterns miss, n=0" (a byte-replay display showing only the top 4 candidates; the true VA had one
   voter, ranked off the bottom), then "unreachable". The sweep settled it: it **resolves**, on
   `GNAM_V1` (pri 870, 4 literal bytes) after **2,199 / 2,369 / 2,424** rejected candidates — the
   three most expensive fall-throughs in the corpus, next worst 475. Config-gated, not a version
   regression; boundary is between 4.27 and 5.7.4, and **5.0-5.6 non-Shipping is untested**.
3. **Rule 5 paid out, twice.** `SPARSE_MEL55_1` and `SPARSE_X1` are now in REPORT.md's Load-bearing
   table: `MEL55_1` is the *only* pattern reaching sparse on 5.7.4 DebugGame. All three of
   `X1`/`X2`/`MEL55_1` were added as pure redundancy against binaries that already resolved. Had any
   been pruned as dead weight, a whole build configuration would silently have lost sparse support.

### `tools/ghidra/corpus-provenance.tsv` — nothing in the corpus is unrecoverable any more

`build_corpus_manifest.py` **nulls** `steam_buildid`/`size`/`sha256` on a drifted row (correctly — it
must never assert the wrong build), which destroys the pointer to the build a `.rep` was made from
the first time a game patches. Palworld drifted that same day. This snapshot preserves it, with four
recovery routes: 22 `STEAMDB-BUILDID`, 6 `STEAMLOG-MANIFEST` (Steam's `console_log.txt` records every
depot fetch with its exact manifest; an archived file's mtime falls inside its download), 4
`STEAM-BACKUP-MANIFEST` (`sku.sis` in a Steam backup — exact, and survives delisting), 4 `REBUILD`,
2 `STEAMDB-MANIFEST`. **`NONE-HASH-ONLY`: 0.**

Two traps recorded: **use `file_modified`, never `file_created`** (a copy resets ctime but preserves
mtime, so install->copy->uninstall keeps the original Steam write time — reading ctime made the dates
look destroyed by the corpus move when they were not); and `console_log.txt` **rotates**, so its ten
surviving records were transcribed into the doc.

### Also

* **Palworld patched mid-session** and it answered "does a pattern survive a game update?" better
  than the ES2 cross-build pair could: every global moved (+0x3300, sparse +0x3180) and **not one
  pattern broke** — all six voter sets came back character-identical.
* Himmel.h header regenerated (158 entries, 151 AOB, 31 source tags — it was four short), corpus
  paragraph refreshed, and a **UE 5.8 layout note** added: 5.8 moved `ObjObjects` +0x10 -> +0x00, so
  the version-fixed `adjustment` patterns encode pre-5.8 arithmetic. `New patterns:` headers lost the
  meaningless `New`.
* The 4.23 sparse-delegate comment said "only 4.23 itself is unverified, and no 4.23 binary is in the
  corpus" — stale since 2026-07-28. Also recorded **why there is no `SPARSE_EXP`**: the symbol exists
  in modular builds but its mangled name embeds the whole template argument list and differs on all
  three engine versions measured, so an exact-name `GetProcAddress` cannot work.
* README UE badge `4.18-5.8` -> **`4.11-5.8`**; `winmm.dll` documented as the spare proxy slot for
  when `dxgi`/`version` is taken; CLAUDE.md "3 proxy DLLs" -> 4.
* [aob-block-library-eval.md](aob-block-library-eval.md) — evaluated, not built. The copyright
  question is sidesteppable because every finding this session came from the **self-built** tier.

### Same day, later: 5.3 + 4.15 config groups — sweep.sh 47 -> 52 rows

Five more rows, all `-noanalysis` imports, each corroborated by running `scan_patterns.java`
against its derived truth immediately after import.

**UE 5.3 ThirdPerson x3 configs — 5.3's FIRST symbolised oracle.** Until now the only 5.3 binary
was Avowed: no PDB, truth for 1 of 5 targets, so GObjects/GNames/GWorld/GEngine had *zero* ground
truth at 5.3. It was picked to bisect the non-Shipping GNames collapse and **it did**: the two
non-Shipping rows land GNames on `GNAM_ES53_1` **UNIQUE-OK** — no fall-through at all — and sparse
on `SPARSE_ES2_1`. So the collapse starts **after 5.3**, and the open interval shrinks from
5.0-5.6 to **5.4 / 5.5 / 5.6**. Next bisect is 5.5.

**UE 4.15.3 Development + DebugGame** — the oldest config group in the corpus, anchoring the far
end. Both resolve all four applicable targets (SparseDelegates absent by design pre-4.23);
GNames lands on `GNAM_SAT422_1`, GWorld on `GWLD_FD_1`.

**`pdb_globals.py` gained a pre-4.23 GNames route**, which it previously could not do at all —
`FNameDebugVisualizer` does not exist before 4.23, so it now falls back to `FName::GetNames` and
takes the RIP load at **+4 with NO `-0x10`** (that adjustment is an FNamePool/Blocks artifact and
applying it here lands 16 bytes low). Validated the same way as everything else: it reproduces the
UE4.15-Flying Shipping row's recorded `GNames=142c92508` exactly before being used on the new two.

**`UE5.8.1-StackOBot` (Shipping) swept** once Ghidra released its lock — all five targets, matching
the offline derivation. Only `UE5.8.1-StackOBotDev` is still outstanding.

**A C++ project was NOT needed for any of this, and the failure that prompted the question is worth
recording.** A C++ project on the 5.3 launcher engine dies in UBT with *"must be compiled with
Visual Studio 2022 17.4 (MSVC 14.34.x) or later … The current compiler version was detected as:
14.29.30159"*. The message blames the VS version and a forced `VisualStudio2019` setting; both are
wrong here. From UBT's own log it is **toolset ranking**: UE 5.3 predates 14.44/14.50/14.51 so it
ranks all of them `FamilyRank=4` ("unknown"), while the one family it recognises — 14.29, supplied
by **VS2026's v142 component**, not by any VS2019 install — ranks 3, wins, and then fails the
`>= 14.34` gate. VS2022's 14.44 was perfectly usable and lost for being too *new* to be in the
table. Nothing needed fixing: the launcher ships `UnrealGame{,-Win64-DebugGame,-Win64-Shipping}.exe`
**with PDBs**, so a Blueprint-only project packages all three configs with nothing compiled.

-----

## 2026-07-27 - GWLD_FD_1: the GWorld fall-through list is now empty (build 2478)

Landed the `UWorld::FinishDestroy` GWorld pattern mined at the end of the 4.11/4.13 support pass.
It was held back deliberately — `GROUND-TRUTH.md`'s own rule requires a full 46-program sweep
before any pattern change, and the priority placement was undecided.

```
48 8B 05 ?? ?? ?? ?? 48 3B C? 48 0F 44 C? 48 89 05 ?? ?? ?? ?? E8   pri 265, io 0
```

22 bytes, 12 fully-literal. The shape is a **read of a global followed by a conditional write-back
of the same global**, which is self-evidencing — that, not the length, is why it is clean. Source
PDB-confirmed on three independent oracles (HeliumRain 4.20, DropIn 4.24, DropIn 4.27).

### Measured over the full sweep: 46 programs / 32 oracles

**21 hits, 16 UNIQUE-OK, zero decoys anywhere**, never more than 1 hit on any binary. It appears
in neither the hotspot table nor the dead-weight table, and the band audit stays clean.
It became the lander on four binaries — three improvements, one lateral, **no regressions**:

| binary | before | after |
|---|---|---|
| UE4.11 Nekopara | `GWLD_G42_1` (880), 5 wasted | `GWLD_FD_1`, **0 wasted** |
| UE4.13 Fantasynth | `GWLD_G42_1` (880), 6 wasted | `GWLD_FD_1`, **0 wasted** |
| UE4.26 Satisfactory Engine | `GWLD_SF_2` (300), 2 wasted | `GWLD_FD_1`, **0 wasted** |
| UE5.2 Satisfactory Engine | `GWLD_SF_2` (300), 0 wasted | `GWLD_FD_1`, 0 wasted |

Those were the *only* three GWorld entries in the report's fall-through list, so **that list is now
GObjects-only**. GWorld redundancy rose by one on 13 oracles and fell nowhere. `GWLD_SF_2` is no
longer the lander anywhere but still reaches truth on both Satisfactory DLLs, so it stays as
redundancy (never prune on "no proof", only on counter-proof).

### Why the sweep understates what this fixes

The *baseline* sweep already showed 4.11 and 4.13 resolving GWorld correctly. That is the harness
model, not the runtime: `scan_patterns.java` has the truth and walks past a decoy, whereas the live
`ValidateGWorldBasic` is deliberately loose and accepts the first one it is handed. In-game both
titles were landing on a **wrong** GWorld — Nekopara via `GWLD_SAT52_1` → `1423C9940` (a
`TSharedPtr {Object, ReferenceController}` singleton whose `+0` reads like a UWorld pointer),
Fantasynth via `GWLD_SF_2` → `14288E648` — and were rescued only by instance-scan recovery.

At 265 the new pattern is scanned **ahead of both** (`SF_2` 300, `SAT52_1` 365), so the true GWorld
is validated and returned before either decoy is ever presented. This is the shape the maintainer
asked for when declining to tighten the validator: *add a pattern, do not touch something 30+
oracles depend on to fix one 2016 title.*

### Placement: 265 over a Tier-1 slot

`GWORLD_PATTERNS` went 49 → 50 byte patterns, i.e. 6 full batches + 2 either way — **no batch
boundary moves**. At 265 it lands in batch 3, leaving batches 1–2 byte-identical, so nothing that
resolves off the modern Tier-1 block could be perturbed at all. Tier 1 (~102) is defensible on the
raw numbers (16 UNIQUE-OK / 0 decoys beats `GWLD_TQ_1`'s 10) but the existing Tier-1 block has not
been re-measured corpus-wide, so what a promotion would displace is unknown. Recorded in
`Himmel.h`, not decided by taste.

Also corrected the pattern-count summary block in `Himmel.h`, which had drifted 4 short
(`SPARSE_AV53_1`/`X1`/`X2` were never added to it): now **150 AOB + 1 CallFollow + 6 symbol
exports = 157 entries**, with a note to regenerate it from `extract_patterns.py` rather than
hand-edit.

-----

## 2026-07-27 - Five new oracles close the 4.21 and 5.0 holes; GWLD_TQ_1 promoted 210 -> 101

Five games added — Helium Rain (4.20.3, PDB), Freud Gate (4.21, no PDB), Breeders of the Nephelym
(4.27, PDB), Maelstrom (4.27.2, PDB), Light Maze (5.0.3, no PDB). Derived in parallel, five agents
on five projects (Ghidra's lock is per-project, so that is safe).

**23 of 23 targets resolve correctly, zero version disagreements, and nothing justified mining a
new pattern.** No repeat of the Elliot "PE says 4.27, actually 5.4" trap — every version was
confirmed independently from the `++UE4+Release-X.Y` build tag and refined where the label was
coarse (4.20.3, 4.27.2, and Light Maze's `CL-20979098` = the 5.0.3 release changelist).

### `GWLD_TQ_1`: 210 -> 101

Measured before moving. It wins on **6 of 16** oracles — no other GWorld pattern wins more than 2 —
and has **zero decoys anywhere**: 10 UNIQUE-OK, 6 NO-TRUTH on probes, 23 MISS. It was sitting
behind 13 AOBs.

The saving is **a whole `.text` pass, not a few validations**. Patterns scan in **batches of 8**,
so order *within* a batch only changes validation order — one AVX2 sweep either way — but crossing
a batch boundary costs an entire extra sweep. At 210 it sat in batch 2, so every game it wins paid
for batch 1 first. What it displaces out of batch 1 is `GWLD_ES2_3`, which wins on nothing, so the
swap is free. Placed at 101 rather than 95 because the 40–90 band means "symbol-derived", and
first-vs-second inside a batch is worth nothing.

Then the five new games arrived and `GWLD_TQ_1` won **all five** — 4.20, 4.21, 4.27 ×2, 5.0. The
promotion is now backed by 11 wins across five engine generations.

Bundling the reorder with the corpus additions was safe, and worth stating why: **scanning is
per-program independent**, so adding rows cannot change another program's result. Any GWorld
change on an *existing* oracle is attributable to the reorder alone; the new games are new
information regardless.

### What the batch settled

- **DropIn's 32-byte `FUObjectItem` is a config artifact, not a 4.27 trait** — proven by two
  independent symbolised 4.27 binaries carrying the stock 24-byte item.
- **`SPARSE_PAL51_1` fires and is CORRECT on Maelstrom (4.27)** — its first correct fire outside
  Palworld, and on a non-5.1 binary. It stays "provenance ≠ version coverage", but it is no longer
  a pattern that has only ever worked on the game it was mined from.
- **`SPARSE_X1`/`X2` are UNIQUE-OK on Maelstrom** — second corroboration outside 5.1.
- **`GENG_X4` is clean on four of the five** and takes 1 decoy on Breeders that is never selected.
  DQ7R stays the only place it is convergent-and-wrong.
- A reusable recipe for pre-4.23 GNames without symbols (`mov ecx,0x408` → the nearby rip store),
  which is a live lead for the three 4.18 rows that leave GNames unset on purpose.

### The thinnest thing in the table now

**Pre-4.23 GNames rests on exactly two patterns, and they are the same shape** — `GNAM_CT3` and
`GNAM_G42_1`, both the `FName::GetNames` lazy-init prologue, both OK-BEHIND, both batch 3,
confirmed identical on Helium Rain *and* Freud Gate. That is the sparse-`n=1` situation again on a
different target. If a third pre-4.23 sample ever arrives, mining a structurally different anchor
is the highest-value thing to do with it.

### UE 4.23 — closed as a deliberate non-goal

It shipped 2019-09 and 4.24 landed that December, so essentially every surviving title has been
bumped to 4.27, and building a sample needs an old Visual Studio the maintainer will not install.
It is also the version where the feature matters least — sparse delegates were barely adopted that
early, so an unverified 4.23 is close to unobservable. The mitigation was never going to be a
sample anyway: **`Aura` probes the live key shape instead of gating on a version number**, which is
what makes 4.23 *and any licensee fork* safe without a binary to test against.

### Version coverage

4.18, 4.20, **4.21**, 4.22, 4.24, 4.25, 4.26, 4.27, **5.0**, 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7 —
contiguous from 4.24 up, with 4.19 the only remaining UE4 gap (sandwiched between covered
neighbours) and 4.23 deliberately skipped. 5.8 is next, and the practical route is packaging a
Blueprint template for **Shipping** from an Epic Launcher engine install — installing the engine
alone yields Editor binaries, which are the wrong shape entirely.

-----

## 2026-07-27 - Grimhook: the first symbolised UE 5.1; sparse n=1 cluster closed

Grimhook ships a **full public PDB** on a `-Win64-Shipping.exe` (2.7 M symbols, 232 K functions).
Until now the corpus's only 5.1 was Palworld, which has no symbols — so every 5.1 claim rested on
consensus. All five globals read straight off the PDB, and the version was confirmed
*structurally* rather than from the label: the PDB's `EUnrealEngineObjectUE5Version` terminates at
`ADD_SOFTOBJECTPATH_LIST = 1008`, which is exactly 5.1 (5.0 stops at 1004, 5.2 adds 1009). Stock
layout — 24-byte `FUObjectItem`, `UObject*` at `+0x00`, chunked.

### What it settled about Palworld

Different binary, so the addresses cannot match — what transfers is which *sets* of patterns
converge. They match almost exactly, which corroborates Palworld's derived values:

- GEngine: the identical 4-set `[X1,X2,X3,X4]` converges on truth here and on `149657F38` there.
- GNames: the identical **12**-pattern set converges on truth here and on `14944DB80` there.
- GObjects: the base gets one 6-set on both and `+0x10` a different 5-set on both — so Palworld's
  base/`ObjObjects` split was the right way round.
- Sparse: `SPARSE_ES2_1` is now *proven* correct on real 5.1, and it is what hit Palworld.

And three patterns that had never been checked against 5.1 symbols are now proven: `GOBJ_V13`
(136 hits, 136 ok), `GNAM_V8` (the priority-100 winner), `GWLD_V7` (its second oracle after
Meltopia).

**One falsification.** `SPARSE_PAL51_1` takes **0 hits** on Grimhook. It is not a generic UE 5.1
shape — it is Palworld-specific inlining. A MISS is not counter-proof so it stays, but its
`PAL51` tag must not be read as "covers 5.1".

### The n=1 cluster — closed except Avowed

Six binaries reached SparseDelegates through `SPARSE_ES2_1` and **nothing else**; a patch moving
that one site would have taken sparse support with it. `SPARSE_X1` / `X2`, mined here, anchor on
`Remove`/`RemoveAll`/`Clear` — different *functions* from `ES2_1`'s `NotifyUObjectDeleted`, so
this is real redundancy, not a re-anchor on the same instruction stream.

| binary | patterns reaching truth |
|---|---|
| Everspace 2 5.5 / 5.5b | 1 → **2** |
| Satisfactory 5.2 / 5.6 CoreUObject, CrashReportClient 5.6, Grimhook 5.1 | 1 → **3** |
| Avowed 5.3 | 1 → 1 — **now the only n=1 left** |

Both are decoy-free across 39 programs including 8 monolithic EXEs up to 414 MB of `.text`.
No binary that currently fails starts working; this is insurance, on the same footing as
`PAL51_1` / `MEL55_1` / `AV53_1`.

### The adversarial pass earned its keep — twice

**X1 was refuted as submitted and shipped shorter.** Its mined form ended with one more
`48 8D 0D` (the `GUObjectArray` ref). Measured, those 3 bytes are inert on 36 of 38 programs — and
they *cost* both Everspace 2 5.5 builds, because 5.5 emits `lea rdx,…; call` with no second `lea`.
Since ES2 5.5 is one of the exact `n=1` binaries the pattern exists to fix, **the longer form
failed at its own purpose.** Longer is not safer; it is only safer where the extra bytes are
load-bearing. This is the mirror image of the `GWLD_G42_4` finding, where wildcarding *more* was
the mistake.

**The `instrOffset` trap was demonstrated, not just asserted.** X2 needs `instrOffset = 11`. A
deliberate wrong-value control at 26 resolves to `SparseDelegateObjectListener` — a plausible
adjacent global 8 bytes below truth — and goes DECOY-ONLY on all 15 binaries *while the hit count
stays healthy*. That is exactly the silent failure rule 7 warns about, now with a worked example.

### Build quirks worth remembering

- `.rodata` is marked **executable** here (2 KB) — every other corpus binary has it non-exec, so
  "exec bytes" for Grimhook is `.text` + that.
- A `.msvcjmc` section is present: MSVC `/JMC` instrumentation on ~512 functions, which adds a
  `call __CheckForDebuggerJustMyCode` prologue. It disturbed nothing here, but it is the kind of
  thing that would shift a prologue-anchored AOB in a game that enabled it globally.
- `GNameBlocksDebug` **is** symbolised (`0x14632A4D0`) and is a **trap**: it is a separate pointer
  variable, not `NamePoolData+0x10`, and it is all-zero in the file. Recorded in GROUND-TRUTH.md
  so nobody takes the shortcut on a future PDB game.

### Version coverage after this

4.18, 4.20, 4.22, 4.24, 4.25, 4.26, 4.27, **5.1**, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7. The named holes
are **4.23** (still the only unverified sparse-delegate version — mitigated because `Aura` probes
the live key shape rather than gating on a version number) and **5.0** (bracketed by 4.27 and a
now-symbolised 5.1, so low risk). 4.19 / 4.21 sit between covered neighbours.

-----

## 2026-07-27 - UE_GameEngine binds the &GEngine slot instead of a frozen pointer (build 2453)

The Teleport tab's Global Pointers card exported two CE symbols with asymmetric backing:
`UE_GWorld` bound directly to the stable `&GWorld` slot (auto-follows), but `UE_GameEngine` was an
`allocateMemory(8)` buffer holding a `UEngine*` **snapshot**. That asymmetry existed for one
reason — `&GEngine` could not be resolved — and that stopped being true today.

- **DLL:** new `QUERY_OP_GENGINE_SLOT = 2` returns the SLOT (`g_cachedGEngine`) plus its deref,
  same shape as `QUERY_OP_GWORLD`. Op 1 still returns the live instance.
- **Script:** `UE_GameEngine` now asks for the slot first and registers the symbol straight to it.
  Only when no GEngine AOB validated does it fall back to op 1 + the buffer.

**The choice is made at ENABLE time, not at generation time.** A CE record gets saved into a `.CT`
and re-enabled in later sessions, where the AOB may resolve even though it did not when the record
was created. Baking the decision in when the script is generated would make the artifact silently
wrong later — and a downgrade to a frozen pointer is precisely the failure a user would not notice.

Two details worth keeping:

- **A marker symbol, not a heuristic, decides what to free.** The snapshot path also registers
  `UE_GameEngine_buf`; `[DISABLE]` frees only through that. Deciding from `UE_GameEngine` itself
  would call `deAlloc` on a game address on the slot path. The GWorld script emits no `deAlloc`
  or `allocateMemory` **at all** — a test asserts the string is absent, so a reader can see the
  record cannot free a game address without tracing logic.
- **The busy check had to become a bounded wait.** `SetDone`/`SetError` publish `status = DONE`
  **before** clearing `cmd` (deliberately). A script issuing two round-trips back to back can
  exit its status poll and still observe the previous `cmd` for an instant, so a single sample
  would report "mailbox busy" and silently abandon the fallback. The prior scripts never hit this
  because they only ever queried once. Now `MailboxIdleWaitMs = 100` ms, bounded.

-----

## 2026-07-27 - Avowed 5.3 sparse closed; 3 games added as oracles; GENG_X4 demoted in prose

### Avowed (UE 5.3) sparse delegates — found, and the fork did NOT change the structure

`FSparseDelegateStorage::SparseDelegates = 0x14B5BD9A8`. This had been an open "zero hits" line
in GROUND-TRUTH.md since the Avowed case study.

The route the docs suggested is dead here: **`SparseDelegateReport` does not exist in the binary
at all** (the `!UE_BUILD_SHIPPING` console command is compiled out), verified against every
initialized block in both ASCII and UTF-16. Found structurally instead — scan `.text` for TSet
element-stride arithmetic adjacent to a rip-relative `.data` reference, bucket by global, and
take the one with a pure 0x60-stride profile. Corroborated by `SparseDelegateMapCritical` sitting
exactly `0x28` (`sizeof(CRITICAL_SECTION)`) below it: the two statics of `SparseDelegate.cpp`,
adjacent, as expected.

**The user's question was whether Obsidian changed the sparse structure, given they changed the
object array. Answer: no, on every observable axis** — outer stride `0x60`, `TSet::HashSize` at
`+0x48`, element `HashNextId` at `+0x58`, inner `TMap` at element`+8`, inner stride `0x20` with
the value at `+8`, `PointerHash` = `ptr>>4` into the Murmur finalizer. The fork's known
deviations (packed 20-byte `FUObjectItem`, static `FUObjectArray`) stop at the object array.
Practical consequence: `ValidateSparseDelegates`' hardcoded `kOuterStride = 0x60` was already
correct for Avowed.

Three candidates were mined; **one was added**. An adversarial pass measured over 42 programs
refuted the other two:

- the twin-ref form baked in `[rsp+0x20]`, which the mining report called shadow space. It is
  not — `mov [rsp+0x20],rdi` spills the key into a frame **local**, and `DI427_1/2` encode the
  same out-param idiom with that disp8 wildcarded. One added spill in a future build takes its
  only hit to zero.
- the `mov rdx` variant is strictly dominated: a nibbled form covers its sites *and* `AV53_1`'s.
- both would push `SPARSE_PATTERNS` from 8 to 9 = **two batches** (`kBatchSize = 8`), costing a
  second AVX2 pass over ~430 MB of `.text` across the titles that find nothing in batch 1, for a
  pattern that can only ever hit Avowed.

Honest caveat recorded in the header: `AV53_1`'s head alone (14 literal bytes) measures
identically, so its tail is inert on this corpus — the selectivity is exact register allocation,
not length.

### Three games promoted from "not in the corpus" to oracles

DQ7R (4.27), The Adventures of Elliot (**5.4 — the corpus had none**) and DQ XI S (4.18, a second
pre-4.23 sample). Live-run first, then corroborated by disassembly. 14 of 15 globals confirmed.

DQ XI S's GNames is **deliberately omitted**: 4.18 predates `FNamePool`, every GNames pattern is
FNamePool-shaped, and the consensus is noise. Per the standing rule, leave it out rather than
guess a value that would mislabel every hit as a decoy.

### The one contradiction — and the rule it produced

**DQ7R's GEngine is `145FF4B28`, not the `145D76D78` the runtime log pointed at.** I had reported
that address to the user as strongly supported: 41 hits converging on one address, against a
7-hit runner-up. It was wrong. `145D76D78` is a game-side manager singleton, and `GENG_X4` alone
accounts for 50 of its 55 hits; the "runner-up" was `GENG_X1`+`X3`+`X2` — the semantically
specific patterns — agreeing on the truth. `145FF4B28` is proven three ways
(`UWorld::GetGameViewport`, `UWorld::GetRealTimeSeconds`, and a `GetWorld` fallback that loads
GEngine and GWorld in the same function) and sits `-0x3948` from GWorld, in family with DropIn
4.27's `-0x4648`.

So GROUND-TRUTH.md rule 4 gained a limit: **convergence only holds WITHIN one pattern. Across
patterns, rank by DISTINCT PATTERNS AGREEING, never by raw hit count.** `consensus_*.txt` already
does this; a hand tally of a runtime log does not.

`GENG_X4` keeps its priority (it is still what reaches FF7 Remake, and `ValidateGEngineSlot`
rejects its decoys so it costs validations, not correctness) but its note no longer claims
"correct on 7 oracles" — it now says what it is: the broadest and noisiest pattern in the table,
whose decoys are game singletons.

-----

## 2026-07-27 - Proxy Deploy CTD: bound rows were mutated from thread-pool threads (build 2445)

**Symptom:** Proxy Deploy tab → *Scan Steam* → *Update all* → the whole app disappears. No managed
exception, no error dialog, nothing after the last log line. Windows recorded
`0xc0000005` (access violation) in **`libSkiaSharp.DLL`**, i.e. inside the renderer.

The deploy itself had *succeeded* — `Updated: 26, up-to-date: 0, failed: 0` is the last line in
`view-0.log`, ~2 s before the crash. So the work was fine; the painting of the result was not.

### Root cause

`DetectedGame` is an `ObservableObject` whose `Status` / `InstalledVersion` / `ErrorMessage` /
`SuggestedProxy` are bound to the Proxy Deploy `DataGrid`. Four `ProxyDeployService` methods
(`RefreshDeployStatusAsync`, `DeployAsync`, `UndeployAsync`, `ApplyProxySuggestionsAsync`) wrote
those properties **from inside their `Task.Run` bodies**, so `PropertyChanged` fired on
thread-pool threads for 29 bound rows at once. Avalonia then mutated the visual tree while the
render thread was composing it. That is not an exception path — it is an AV in Skia, which takes
the process down with no managed stack to look at.

The codebase had already met this bug and mistaken it for a cosmetic one. From
`ProxyDeployPanel.axaml`:

> *"Marking the whole grid IsReadOnly=True caused row visuals to lag behind item PropertyChanged
> events **from the background-thread Refresh**, requiring a second click to repaint."*

The late repaint and the crash are the same race. Setting `IsReadOnly` per column instead of on
the grid fixed the visible symptom and left the thread violation in place.

### Fix — compute off-thread, apply on the caller's thread

All four methods now do their file I/O inside `Task.Run`, collect the results into a
`GameStatusUpdate` record, and apply them **after** the `await`, which resumes on the caller's
context. All 11 call sites are UI-thread `[RelayCommand]`s in `ProxyDeployViewModel` with no
`ConfigureAwait(false)`, so that context is the UI thread.

`GameStatusUpdate` carries `SetInstalledVersion` / `SetErrorMessage` flags because some paths
deliberately leave a field alone (the "already up to date" deploy sets only `Status`) and `null`
is itself a meaningful value for the other two — a blanket three-field apply would have changed
behaviour.

The contract is now written down on `IProxyDeployService` rather than left implicit, since
nothing enforced it the first time.

### Notes

- `ProxyDeployService` deliberately does **not** take a dependency on `Avalonia.Threading`. In
  this codebase `Dispatcher.UIThread` appears only in ViewModels and Views; services stay
  UI-framework-free, so the marshalling is done by *where the code runs*, not by a dispatcher call.
- Swept the rest of `Services/`: `PipeClient` and `SnapshotStore` are the only other files using
  `Task.Run`, and neither references any `ObservableObject` model. `DriveDescriptor.IsSelected` is
  read but never written off-thread. `ProxyDeployService` was the only offender.
- A UI crash whose faulting module is `libSkiaSharp` and whose trigger is a batch operation over
  a bound collection should be treated as a threading bug until proven otherwise — SOS cannot
  read the dump (Native AOT publish has no CoreCLR), so the dump is a dead end and the code is not.

-----

## 2026-07-27 - &GEngine was never resolvable: the validator ran before the offsets it needs (build 2441)

**Symptom:** the System tab reported `&GEngine — AOB not found` on *every* game. Reported against
DQ7R (4.27), The Adventures of Elliot (5.4), DQ XI S (4.18), Titan Quest II (5.6) and
Everspace 2 (5.5) — the last two being cases where the offline sweep says the patterns resolve
correctly, which is what made it obviously a code bug rather than a coverage gap.

### The patterns were right the whole time

The scan log records every candidate. On Everspace 2, **14 of 15 candidates across four
independent patterns resolved to `0x7FF68ACC37B0`** — and the PDB's `&GEngine` is image VA
`0x149DA37B0`, which at that process's load base is exactly `0x7FF68ACC37B0`. Same picture
everywhere else: DQ7R 41 hits on one address, Elliot 27, TQ2 28, DQ XI S 12. Textbook
convergence. Then every one of them was rejected.

### Root cause — an ordering contract that was documented but not honoured

`Genau.cpp`'s `FindGEngineSlot` carried a comment stating it *"MUST be called after
GObjects/GNames/offsets are up"*, because `ValidateGEngineSlot` derefs the candidate and asks the
reflected class for a `GameViewport` property. `FindPropertyOffsetByName` needs
`DynOff::USTRUCT_CHILDPROPS` / `FFIELD_NAME` / `FFIELD_NEXT` / `FPROPERTY_OFFSET` **and**
`Serie::GetString` — i.e. the dynamic offsets *and* a live FNamePool.

The call site did not satisfy it. From the Everspace 2 log:

| time | event |
|---|---|
| `12:10:01.340` | GEngine AOB scan + validation (inside `Genau::FindAll`, `Frieren.cpp:122`) |
| `12:10:02.505` | `FNamePool: Initialized` |
| `12:10:02.506` | `ValidateAndFixOffsets: Starting…` (`Frieren.cpp:319`) |
| `12:10:02.508` | `ChildProperties found at struct+0x50`, `FField::Name at +0x20` |

The validator ran **1.2 s before** the values it reads were discovered, so it walked the property
chain with compile-time default offsets and a dead name pool. Every candidate failed, on every
game, always — the feature had never worked since it was added at build 2399.

### Fix — resolve &GEngine in a second pass

- `FindGEngineSlot` now **enforces** its own precondition instead of documenting it: if
  `DynOff::bOffsetsValidated` is false it returns 0 with method `deferred` **without scanning**.
  That also stops burning the 0.2–0.7 s AVX2 pass on a scan whose result cannot be accepted.
- New `Genau::ResolveGEngineDeferred(EnginePointers&)` re-runs the scan once the offsets exist,
  republishing the pattern-id / scan-addr / AOB triple so a GameEngine-rooted CE export can still
  be AOB-wrapped (a deferred win that only set the address would have left that empty).
- `UE5_Init` calls it directly after `ValidateAndFixOffsets` and re-caches the seven
  `g_cachedGEngine*` globals the pipe serves to the System tab.
- The `apply_rescan` pipe path got the same second pass: a recovery rescan that revives
  GObjects/GNames is exactly the case where the offsets GEngine was waiting on have just arrived.

### Follow-up (same day) — the two rows that had no AOBMaker buttons

**LIVE-VERIFIED on Everspace 2**: `&GEngine (engine slot) = 0x7FF6430237B0` via `GENG_X3`.

With the address finally resolving, the Pointer panel's last two rows were still Copy-only.
`FSparseDelegateStorage` and `&GEngine` gained a **HEX** button (same contract as the three
above them), and `&GEngine` gained a **SYM** button matching GWorld's.

`SYM` registers the **slot**, not the UEngine object — which is the entire point. The slot
address is restart-stable, so a GameEngine-rooted CE record auto-follows engine recreation
instead of freezing a stale `UEngine*`. Symbol name `gengine_addr`, mirroring `gworld_addr`.
`CanRegisterGEngineSymbol` requires the AOB triple, not just an address, for the same reason
GWorld's does: without the pattern the generated AA script cannot re-scan on enable.

### Notes for next time

- The 0.2–0.7 s per game the wasted scan cost was invisible because it was folded into the
  scan-progress bar.
- Nothing was wrong in `Himmel.h`. Every GEngine pattern is fine and the target hits on 5/5 live
  games spanning UE 4.18 / 4.27 / 5.4 / 5.5 / 5.6 — **adding more GEngine AOBs would have fixed
  nothing.** A "not found" in the UI is only evidence about the *pipeline*, not the patterns,
  until the scan log's candidate list has been read.
- `RecoverGWorldViaEngine` has the same reflection dependency but is already invoked from a later
  path (`Frieren.cpp:447`, after offsets), so it was never affected.

-----

## 2026-07-26 - Stack-displacement rule codified and applied; GWLD_SF_4 coverage 4 -> 15 binaries (build 2437)

The user stated a design rule I had been applying too bluntly: **`lea rdx,[rsp+????????]` is fine
in a pattern; `lea rdx,[rsp+00000318]` is not.** The instruction form is acceptable — the literal
frame offset is not. I had read it as "avoid stack instructions" and dropped a leading `lea` from
`SPARSE_MEL55_1` for no reason.

### Why the rule is right — and what it is really about

A frame displacement encodes the **callee's frame layout**: local count, register spills, inlining
decisions, alignment. None of that is a property of Unreal Engine; it is a property of one
compilation, and it moves when a patch adds a single local.

A **struct** displacement is the exact opposite and must be kept: `cmp [rcx+0x2C0],rax` (a UWorld
member) or `cmp eax,[rdi+0x34]` (TSet Max) pin UE's real data layout, which is version-stable and
is precisely the evidence that makes a pattern trustworthy. So the rule is not "avoid stack
instructions" — it is **wildcard FRAME displacements, keep STRUCT displacements**.

Auditing the whole database for literal rsp-relative displacements found 18, splitting cleanly:
ten are small shadow-space constants (`sub rsp,0x28`, `mov [rsp+0x20],rbx`, all ≤ 0x40) which are
the idiomatic x64 prologue and stable across compilers; eight are genuinely frame-specific
(0x50–0x70), in four patterns.

**An honest note on evidence:** breadth statistics do *not* separate the two groups (frame-offset
patterns average 6.5 binaries hit / 3.5 correct vs 7.5 / 3.3 overall), and the one same-game
cross-build pair in the corpus cannot test them (all four score zero hits on both ES2 builds). The
rule stands on the mechanism, not on a correlation this corpus can show. What the corpus *can* do
is test each change directly, which is what was done.

### Measured, one pattern at a time

| pattern | literal bytes | wildcarding the frame offset | action |
|---|---|---|---|
| `GWLD_GH_3` | 22 | 5 → 7 binaries, **UNIQUE-OK and decoy-free on every one** | applied |
| `GWLD_SF_4` | 9 | 2 → 6 binaries, UNIQUE-OK on five, one late decoy on 4.27 | applied |
| `GOBJ_G42_4` | 24 | neutral — still 1/1 on Everspace 4.20 where it is the lander | applied (free build-tolerance) |
| `GWLD_G42_4` | **7** | gains 4.24 but breaks three versions to OK-BEHIND and **38 hits / 37 decoys on UE 4.27** | **rejected** |

Final coverage after the full re-sweep: `GWLD_SF_4` **4 → 15 binaries** hit and **3 → 9** correct;
`GWLD_GH_3` 9 → 12 and 4 → 6. `GWLD_G42_4` and `GOBJ_G42_4` unchanged.

That last row is the qualifier worth keeping: **wildcard the frame displacement only if the
pattern has enough other literal context.** On a seven-literal-byte pattern the frame offset *is*
the selectivity — which is itself a reason to distrust that pattern, but removing it makes things
worse, not better. Both the rule and this exception are now recorded in the band-discipline block
in `Himmel.h`.

### A bug the verification caught

Restoring the leading `lea rdx,[rsp+d32]` to `SPARSE_MEL55_1` shifted its RIP-relative
instruction from byte 0 to byte 8, but `instrOffset` was left at 0 — so it resolved off the wrong
instruction and silently dropped to **0 correct** while still reporting 3 hits. The sweep caught
it; the regression matrix did not move only because that pattern sits last at priority 160.
This is the characteristic failure mode of an `instrOffset` mistake: hits look healthy, the
resolved address is garbage. Fixed to `instrOffset = 8` and re-verified (1 correct, and the three
binaries it hits are unchanged).

Full 35-program re-sweep after every change: every target on all 20 oracles still correct.
Build + tests green.

-----

## 2026-07-26 - Sparse-delegate coverage audit: a second 5.5/5.6 anchor; FF7 Rebirth answered but not fixed (build 2432)

Prompted by "should FF7 Rebirth get insurance AOBs, and does it even have FSparseDelegateStorage?"
Auditing that produced a corpus-wide finding worth more than the original question.

### SparseDelegates is the systematically weakest target

Counting anchors per binary showed **eight** binaries resolving sparse through exactly ONE
pattern (`SPARSE_ES2_1`), spanning UE 5.2 / 5.5 / 5.6. Many other `n=0` rows are *correct* —
pre-4.23 engines have no sparse delegates (FF7 Remake 4.18, Everspace 4.20, Satisfactory 4.22,
Octopath), and the modular DLLs where it does not live. The genuine gaps were the `n=1` band plus
**Avowed 5.3 and FF7 Rebirth**.

`n=1` matters more here than for other targets: `ValidateSparseDelegates` can only range-check
two ints, so unlike the GObjects/GNames/GWorld/GEngine validators it cannot reject a wrong hit or
rescue a miss.

### `SPARSE_MEL55_1` — mined on Meltopia, covers three of the eight

Meltopia's PDB names the whole family (`Add` / `AddUnique` / `Clear` / `Remove` / `RemoveAll` /
`Get*MulticastDelegate` / `SetMulticastDelegate` / `NotifyUObjectDeleted`), which made the shapes
easy to compare. The one that generalises is a **twin reference**:

```
lea    rcx,[SparseDelegates]     <- passed as `this` to TSet::FindOrAddId
call   <FindOrAddId>
movsxd rax,[rsp+d32]             <- out-param element index (displacement WILDCARDED)
lea    rdi,[rax+rax*2]; shl rdi,5   <- element stride 0x60
add    rdi,[SparseDelegates]     <- the SAME global again
```

Two references to one static with the stride math between them — the same property that makes
`SPARSE_ES2_1` reliable. Meltopia 3/3 decoy-free; also hits **Manor Lords** and **TQ2**, both
previously `n=1`, converging on a single address on each. Zero hits on Everspace 2 5.5,
Satisfactory 5.2/5.6, Solarpunk, DropIn, Avowed and FF7 Rebirth — codegen-specific rather than
version-specific, i.e. genuinely additive. Priority **160, last**, so it cannot perturb anything.

Sparse `n=1` binaries: **8 → 5** (the remaining five are the two Everspace 2 builds and
Satisfactory's 5.2/5.6 CoreUObject).

### Two rejected candidates, both worth recording

- The **TSet hash-bucket probe** (`dec ecx; mov eax,rNd; and rcx,rax; mov eax,[rdx+rcx*4];
  cmp eax,-1; jz; mov rdx,[Sparse]`) reads like an ideal anchor and is the opposite: it is the
  *generic* TSet lookup every TSet in the engine uses. It resolved to **39–43 different globals
  per binary** and was DECOY-ONLY on Solarpunk and Satisfactory 5.2.
- The **register-nibbled** form of the accepted pattern took **0 hits**. Over-wildcarding does
  not generalise a pattern; it just stops it matching. That is now the third independent
  confirmation of the exact-register rule for this target.

The leading `lea rdx,[rsp+d32]` was also dropped on purpose — a frame-layout detail is not a
semantic anchor, and the pattern is equally unique without it.

### FF7 Rebirth: question answered, patterns deliberately NOT added

**Yes, it has `FSparseDelegateStorage`.** Proven from `.rdata`, which carries
`SparseDelegateFunction`, `MulticastSparseDelegateProperty` and even the `SparseDelegateReport`
console command with its help text. So the storage exists and every one of our sparse patterns
simply misses this fork's codegen — including both new ones.

Its other four targets are in reasonable shape (GNames `1490D3C00` n=5, GObjects `14871EB38`
n=5 with `GOBJ_RE1` independently finding the `-0x10` base, GWorld `148F30420` n=3, GEngine
`148F4B580` n=2), and the tool is recorded as working in-game on it.

No pattern was added, for two reasons worth stating rather than hiding:

1. **Cost/benefit.** Locating the global needs a dedicated RE pass on a 377K-function
   symbol-less binary — the `SparseDelegateReport` console command is the obvious lead (find the
   string xref, follow the `FAutoConsoleCommand` handler) and is recorded here for whoever picks
   it up. Sparse is lazily resolved and non-critical: only the sparse-delegate drill-down
   degrades, nothing in the boot path.
2. **It probably would not transfer.** The hope was insurance for FF7 part 3. But the history
   argues against it: FF7 Remake (4.18) and FF7 Rebirth (4.26 fork) share *no* signatures —
   `GOBJ_RE2`/`GOBJ_V12` work on Remake, `GOBJ_RE1` on Rebirth, and `GENG_X4` is DECOY-ONLY on
   Remake while merely divergent on Rebirth. A pattern mined from Rebirth would only help part 3
   if it reuses the same fork *and* toolchain, which those two titles did not manage between
   themselves. Better to re-mine when the binary exists.

-----

## 2026-07-26 - Two AOBs mined from Palworld (UE 5.1): a second sparse anchor + the broadest GEngine pattern yet (build 2426)

Palworld ships no PDB, so this is a worked example of mining from a symbol-less binary. Ground
truth first, patterns second — the reverse order silently produces patterns for a wrong address.

### Establishing Palworld's truth without symbols

The consensus table gave GNames `14944DB80` and GWorld `14965BBE0` at **12 agreeing patterns**
each — not in doubt. GObjects showed a `1494ED280` / `1494ED290` pair (6 patterns each, exactly
`base` and `base+0x10` = ObjObjects). SparseDelegates had **one** pattern and nothing to
corroborate it, so it was confirmed structurally instead: disassembling the `SPARSE_ES2_1` site
gives `FSparseDelegateStorage::NotifyUObjectDeleted` —

```
LEA  RCX,[0x148FB66B0]   ; passed as `this`
CALL <TMap::Remove>
MOV  EAX,[0x148FB66B8]   ; +0x8   \ the two int32s ValidateSparseDelegates range-checks
CMP  EAX,[0x148FB66E4]   ; +0x34  /
...
LEA  RCX,[0x1494ED280]   ; then RemoveUObjectDeleteListener  => confirms GObjects too
```

### What was actually weak, and what was not

| target | Palworld anchors | action |
|---|---|---|
| GNames / GWorld | 12 patterns each | nothing needed |
| GObjects | 12 patterns | nothing needed (the ~40 wasted validations are cost, not risk) |
| GEngine | 3 patterns, but X1/X3 overlap by construction ⇒ **2 independent shapes** | added one |
| **SparseDelegates** | **1** — and its validator is the weakest we have, so it cannot rescue a miss | added one |

### `SPARSE_PAL51_1` — a second sparse anchor

Anchors the element-address block rather than `NotifyUObjectDeleted`:
`lea r,[rax+rax*2]; shl r,5` (stride 0x60) → **`add r,[SparseDelegates]`** → `lea r,[r+8];
cmovz; test; jz near` → `mov eax,[r+8]; cmp eax,[r+0x34]` (the TSet Num-vs-Max compare).
`SPARSE_DI427_2` models the same semantics but with a *short* jz and a different instruction
order, which is why it takes 0 hits here. 29 literal bytes; fires on exactly three binaries,
decoy-free on all: Palworld 2/2, **UE 4.26 Satisfactory 2/2 UNIQUE-OK** (an unplanned bonus — it
is not 5.1-only), and DQ I&II HD-2D (2 hits converging on one address). Zero hits on the other
32 programs. Placed at priority **150, deliberately last**, so it cannot perturb any existing
selection — it is the backup for when `SPARSE_ES2_1`'s site changes, not a replacement.

The register-agnostic nibbled variant was measured and **rejected**: it produced a decoy on
Palworld itself, reproducing the trap already recorded for `SPARSE_DI427_2`. Exact-register forms
remain the safe ones for this target — that is now two independent confirmations.

### `GENG_X4` — mined on 5.1, useful nearly everywhere

`mov rax,[GEngine]; test rax,rax; jz; mov rcx,[rax+disp32]; test rcx,rcx; jz` — null-check the
engine, load one of its object members at a **32-bit** displacement, null-check that. The
`?? ?? 00 00` is load-bearing: it pins the member load to a disp32, which UEngine's layout forces
and which keeps the pattern off the far commoner 8-bit-displacement `mov rcx,[rax+0x30]` idiom.

Correct on **twelve** oracles spanning UE 4.20 → 5.7 — UNIQUE-OK on 4.22 / 4.24 / 4.26 / 4.27 /
5.2 / 5.5 Meltopia / 5.6, and correct-site-first on 4.20 / 4.25 / both 5.5 Everspace builds / 5.7.
On Avowed all 53 hits converge on one address.

**Recorded honestly, because it is not clean everywhere:** on FF7 Remake it is DECOY-ONLY (106
hits, 3 distinct targets) and FF7 Rebirth is similarly divergent (90 hits, 6 targets) — the
SquareEnix forks reuse this shape for something else. It costs nothing today because `GENG_X3`
(pri 105) wins on FF7 Remake first, and `ValidateGEngineSlot` derefs the slot and demands a
reflected `GameViewport` property. Placed at 115, behind the three cleaner X-family patterns.

A rejected candidate is worth recording too: `mov rcx,[G]; test; jz; call [vtable]` looked
plausible and produced **76–93 different targets per binary**. Divergent hits mean a generic
idiom; convergent hits mean a real global. That single test separated the two candidates.

### Verification

Full 35-program re-sweep after both additions: **the regression matrix is byte-for-byte
identical** — every target on all 20 oracles still resolves correctly and no landing pattern
moved. Palworld's SparseDelegates is now `n=2`. Build + tests green.

-----

## 2026-07-26 - Corpus to 35 programs / 20 oracles: UE 4.24 + 5.1 + a same-game cross-build pair; sparse delegates settled (build 2420)

Five more Ghidra projects, all produced with the current Ghidra: `DropIn_UE424` (UE 4.24.3, PDB),
`ES2_UE55` (UE 5.5, 2025-06-17 build, PDB), `Meltopia_V2` (UE 5.5, PDB **now applied**),
`Palworld` (UE 5.1) and `FF7Re` (FF7 Rebirth). Corpus: **35 programs, 20 with ground truth,
twelve engine versions.** No new pattern was needed — **every target on every one of the 20
oracles still resolves to the correct address**, and the four pre-existing fall-throughs are
unchanged.

### UE 4.24 settles the sparse-delegate question

`DropIn_UE424` carries a `FSparseDelegateStorage::SparseDelegates` symbol whose mangled name
demangles to

```
TMap<UObjectBase const*, TMap<FName, TSharedPtr<TMulticastScriptDelegate<FWeakObjectPtr>>>>
```

— a **raw pointer key**, identical to 4.25 / 4.26 / 4.27 / 5.x. Sparse delegates arrived in 4.23,
so **only 4.23 itself is now unverified** and no 4.23 binary exists in the corpus. `Aura` still
probes the live key shape rather than gating on a version number, which is what keeps 4.23 and
any licensee fork safe without a binary; the note in `Himmel.h` that once claimed
"4.23-4.26 remain unverified" is now down to one version. All five 4.24 targets resolve with no
new patterns (`GOBJ_ES53_1` / `GNAM_V8` / `GWLD_TQ_1` / `SPARSE_DI427_1` / `GENG_X1`).

### The same-game cross-build pair — patterns survive a game update

`ES2-0517` (2025-05-17) and `ES2_UE55` (2025-06-17) are the same game, same engine, two manifests
apart. Every global moved:

| | 0517 | UE55 | delta |
|---|---|---|---|
| GObjects | `149AA7EE0` | `149AA5F60` | -0x1f80 |
| GNames | `149C009C0` | `149BFE940` | -0x2080 |
| GWorld | `149B37D18` | `149B35DD8` | -0x1f40 |
| SparseDelegates | `149AA7E90` | `149AA5F10` | -0x1f80 |
| GEngine | `149DA5810` | `149DA37B0` | -0x2060 |

so this is a real re-find, not a trivially identical binary. Both builds land on the **same
pattern with the same cost for all five targets**. That is the first direct evidence in the
corpus that a signature survives a shipped patch rather than merely a version bump — every other
pair differs by engine version too.

### Meltopia: PDB applied via MSDIA, and it vindicates the consensus method

The first import silently failed to apply Meltopia's 347 MB PDB; the retry succeeded by selecting
the **MSDIA** loader — **PDB-Universal fails on this file**. Worth remembering as a first
resort when a game ships a PDB and the probe still reports zero UE globals.

The payoff is a clean, blind validation. While Meltopia had no symbols, the sweep's consensus
table predicted GEngine `149F002F8`, GWorld `149F03D10`, GObjects `149D87430`, GNames
`149CA3C80`. The PDB then gave `149f002f8`, `149f03d10`, `149d87420` (+0x10 = `149d87430`) and
`149ca3c80` — **all four exact**. The ≥3-independent-patterns-agree heuristic has now been
confirmed against symbols twice (Everspace, Meltopia).

### A caution about pruning, learned the same day

`GWLD_V7` ("Palworld long context") sat at **0 correct across the whole corpus** and appeared in
the dead-weight table — and then went **UNIQUE-OK the moment Meltopia gained symbols**. A pattern
with *no proof* is not the same as a pattern with *counter-proof*.

So the four GWorld patterns removed in build 2409 were re-tested against all three new oracles
rather than assumed. `GWLD_V2` / `V4` / `V5` / `V6` are still `DECOY-ONLY` on every one — now
**0 correct across 12 oracle groups** while firing 11–395 times each. That is counter-proof, and
it is precisely why those went and V7 stayed. Both facts are recorded in the corpus note in
`Himmel.h` so the next pruning pass starts from the right test.

### Palworld and FF7 Rebirth close two attribution loops

Both are symbol-less noise probes, but each is the binary its namesake patterns were contributed
for, and neither had ever been in the corpus:

- **`GOBJ_RE1`** ("FF7 Rebirth add+cmp+jge") had **zero hits anywhere** across 31 programs. On
  FF7 Rebirth it hits exactly once — it was never broken, just never tested on its own game.
- **`GWLD_V7`**, **`GOBJ_V13`** and **`GOBJ_V9`** ("Palworld …") all fire on Palworld, the UE 5.1
  title they were named after and the corpus's only 5.1 sample.

Fourteen patterns still hit nothing anywhere (`GOBJ_SAT425_1`, `GOBJ_RE3`, `GOBJ_V11`,
`GOBJ_SF_1`, `GOBJ_PS4`, `GOBJ_PS5`, `GOBJ_CT3`, `GNAM_SAT52_1`, `GNAM_V6`, `GWLD_GH_2`,
`GWLD_V1`, `GWLD_SF_3`, `GWLD_G427_3`, `GWLD_G427_4`). On the evidence above they are being left
alone: zero cost at their priorities, and the corpus keeps demonstrating that "never seen to
fire" often means "the right binary is not here yet".

-----

## 2026-07-26 - Pattern tables sorted + compile-time-enforced; 4 never-correct GWorld patterns removed (build 2414)

### The tables had drifted out of priority order, and the file was lying about it

The user asked why `GNAM_V1`/`V3`/`V4` "still have not been re-prioritised". They **had** been —
demoted to 870/880/890 in build 2405 — but the array had not been re-sorted, so all three still
sat under a `// 500–590: Tier 3 — short patterns` header. `GNAM_V5` (850) sat inside the Tier-1
block, `GNAM_V2` (860) inside Tier 2, `GOBJ_PS7` (970) under `// 600–690`, and `GWLD_G42_1`
(880) inside the 325–365 run. `ScanForTarget` sorts by priority so **behaviour was always
correct** — but anyone reading the file got a different order from the one that actually runs.
That is a worse failure than a plain bug: it silently invalidates review.

All five tables are now written in priority order, the band headers match their contents, and
the invariant is **enforced by the compiler** rather than by discipline:

```cpp
ASSERT_TABLE_ORDER(GOBJECTS_PATTERNS);   // static_assert: sorted AND no duplicate priorities
```

Verified the guard actually fires by deliberately mis-numbering an entry:
`error C2338: static assertion failed: 'GNAMES_PATTERNS must be listed in priority order'`.
Duplicate priorities are rejected too — two patterns on one number have an order that depends on
the sort's stability, which makes a regression sweep unreproducible.

### GWLD_V2 / V4 / V5 / V6 removed — never once correct in 31 programs

The user's read that `AOB_GWORLD_V5` / `V6` "look a bit short, priority should be low" was right,
and the data went further than that. Across 31 programs (9 groups with GWorld ground truth):

| pattern | literal bytes | matches | reaches truth on |
|---|---|---|---|
| `GWLD_V4` `48 8B 3D ?? ?? ?? ?? 48 85 FF` | 6 | 5,809 | **0 of 9** |
| `GWLD_V6` `48 89 1D ?? ?? ?? ?? E8` (write) | 4 | 2,403 | **0 of 9** |
| `GWLD_V2` `48 89 05 ?? ?? ?? ?? 48 85 C0 74` (write) | 7 | 1,301 | **0 of 9** |
| `GWLD_V5` `48 39 05 ?? ?? ?? ?? 74` | 4 | 929 | **0 of 9** |
| `GWLD_V3` `48 8B 1D ?? ?? ?? ?? 48 85 DB` — **kept** | 6 | 22,581 | 6 of 9 |

Every shape is already covered by a longer sibling that does work: the `mov rdi,[GWorld]` read by
`SP57_3`/`G427_2`/`SF_4`, the rax-write by `SAT426_2`/`ES53_1`/`SAT425_3`, the rbx-write by
`SF_3`. Removing them loses no mechanism, only the degenerate context-free form.

**The deciding argument is specific to GWorld: a wrong GWorld is worse than no GWorld.**
`ValidateGWorldBasic` is deliberately loose, and when it is fooled the damage is silent — exactly
what happened on Solarpunk, where `GWLD_SF_2` matched a decoy `.data` global, passed validation
and produced a wrong world. With nothing resolving, Genau instead falls back to instance-scan
recovery, which found the *right* world on that same title. A pattern that has never once been
correct is therefore pure downside here, however low its priority.

### Why the GNames short patterns were NOT removed

Same question, different answer, because the evidence differs. Over the same corpus
(10 GNames oracle groups): `GNAM_V2` 8 correct / 2 decoy-only, `V5` 8/2, `V3` 7/3, `V4` 6/4,
`V1` 6/4. They are **redundant, not wrong** — where each is correct there are 3–14 other correct
patterns, so deleting them changes no result today, but "correct yet redundant" is worth keeping
as insurance for an engine build the corpus does not cover, whereas "never correct" is not. The
second half of the argument is the validator: `ValidateGNames` reads the pool structure and is
strong, while `ValidateGWorldBasic` is loose and has been fooled in the field. At 850–890 they
are only reached when everything above failed; on all 10 oracles GNames resolves by 715 at the
latest, so they are never even scanned.

Re-ran the full 31-program sweep after both changes: the regression matrix is **byte-for-byte
identical**, and all eight symbol-less titles still pick GWorld at priority 100–390, far above
the removed slots.

### The file header was stale, and a second dead constant fell out of checking it

The top-of-file block still said *"128+ AOB pattern database"* and *"signatures for GObjects,
GNames, GWorld"* — it never mentioned **SparseDelegates or GEngine at all**, despite both being
first-class `AobTarget` values. Its source list also overclaimed: `RE1-RE5` when only RE1–RE3
exist, `UD1-UD3`, `CT1-CT5`, and `D7_1` which was deleted back in 2404.

Rewritten with a per-target breakdown (counts machine-verified against
`extract_patterns.py`, not hand-copied), the priority-order + `static_assert` rule, the
"verify against the corpus before trusting it" step with the actual command, and a description
of what the 31-program / 17-oracle corpus contains and why half of it deliberately has no
ground truth.

Auditing "is every declared constant actually in a table?" then turned up
**`AOB_GOBJECTS_CT2`** — dead in exactly the way `AOB_GNAMES_UD1` was, and worse on inspection:
`push rbx; sub rsp,0x20; mov rbx,rcx; test rdx,rdx; jz; mov` is a bare MSVC prologue matching
thousands of functions, and it contains **no RIP-relative operand at all**, so there was nothing
for `TryResolveMatch` to resolve — wiring it up could never have produced an address. Removed.

Since this class of rot has now bitten twice, `extract_patterns.py` reports it: any `AOB_*`
constant declared but referenced by no `PATTERNS[]` array is listed as `DEAD`, with a whitelist
for the one deliberate exception (`AOB_NAMEDECRYPT_ME1`, which `Genau::ResolveNameKeyTable`
consumes directly because it de-obfuscates FName payloads rather than resolving a pointer).
Verified by planting a fake constant and watching it get flagged.

-----

## 2026-07-26 - 31-program AOB sweep: GEngine symbol export + FF7R coverage, two dead patterns removed, one unmatchable pattern fixed (build 2408)

The corpus grew from 8 binaries to **31 programs across 18 Ghidra projects — 17 of them with PDB
truth**, spanning UE 4.18 / 4.20 / 4.22 / 4.25 / 4.26 / 4.27 / 5.2 / 5.3 / 5.5 / 5.6 / 5.7. The
sweep is now a script (`tools/ghidra/sweep.sh` + `aggregate_sweep.py`) rather than a hand-run
command per project, because the next round has to be repeatable.

**Headline: every target on every one of the 17 oracles resolves to the correct address.** No
pattern added in this or the previous two builds changed what any engine version lands on.

### What the bigger corpus exposed

| finding | detail |
|---|---|
| **GEngine was never given a symbol export** | `?GEngine@@3PEAVUEngine@@EA` is exported by the Engine module in every modular build we have binaries for — verified in the export table of Satisfactory's `FactoryGame-Engine-Win64-Shipping.dll` on **both** UE 4.26 (ordinal 13690) and UE 5.2 (19170), sitting directly beside `?GWorld@@3VUWorldProxy@@A`. GObjects and GWorld had `SIG_EXPORT` entries; GEngine simply never got one, so modular titles paid for a full AOB sweep to find something `GetProcAddress` returns in O(1). Added at priority 0. |
| **`AOB_GNAMES_SAT422_1` could never match anything** | It omitted the `48 85 C0` (`test rax,rax`) between the load and the jump — and MSVC cannot emit `mov`+`jnz` with no flag-setting instruction between them, so the string was unmatchable *by construction*. Zero hits across all 31 programs, including the very Satisfactory UE 4.22 build it is named after. Re-derived from that build's PDB (`FName::GetNames` @ `0x140BCEBF0`, load at +4) and moved 730 → 715, so UE 4.22 now lands on its purpose-built anchor instead of falling through to `GNAM_CT4`, a `ret; mov [rip],rbx` **write** pattern that only got there after rejecting a decoy. |
| **`AOB_GNAMES_UD1` was dead code** | Declared since the DB was written, never referenced by `GNAMES_PATTERNS[]` — it has never been scanned for in any build. The suspicion about it was well founded: `cmp dword [rbp-0x18], 0` pins an exact frame-pointer-relative stack slot, a property of one compilation of one function in one game. Deleted rather than wired up. |
| **`GNAM_CT2` is byte-for-byte redundant with `GNAM_UD2`** | CT2 is UD2 minus its final `05`. Measured over all 31 programs the two produced **identical** hit counts on every single one (0/0, 10/10, 11/11, 15/15, 36/36, 932/932 on FF7R…). The `C6` CT2 stops on is `mov byte ptr`, and the only encoding that ever follows here is the `C6 05` UD2 pins. CT2 removed; UD2 takes priority 300. |

### FF7 Remake: the one binary where GEngine found nothing

Of 31 programs, FF7R was the only one where **every** GEngine pattern missed. Its
`GetWorldFromContextObject` wrapper spills the result (`mov rbx,rax`) *before* the null check, so
`GENG_X1`'s trailing `48 85 C0` no longer follows the call — a length change no nibble can
bridge. New **`GENG_X3`** is X1's head only (`sub rsp,0x2X; mov rdx,rcx; mov rcx,[GEngine];
call`, REX nibble-masked, tail dropped). Dropping the tail was measured, not assumed: X3 is
UNIQUE-OK with **zero decoys** on both calibration oracles and finds strictly *more* correct
sites than X1 (DropIn 3 vs 2, Solarpunk 2 vs 1). It also closes UE 5.5, where X1 misses.

Disassembling X3's single FF7R hit confirmed the address and handed over two more constants for
free — the caller runs the returned UWorld's `InternalIndex` through
`cmp [0x1453BD48C]` / `mov rax,[0x1453BD480]` / `lea rcx,[rax+idx*24]`, i.e. textbook
`GUObjectArray.IndexToObject`. So FF7R is now a **partial oracle**: `GEngine = 0x145879EE8`,
`GObjects = 0x1453BD470`, both corroborated by independent patterns. GNames/GWorld are
deliberately left unset — a guessed truth is worse than none, because it mislabels every hit as
a decoy (the mistake that once got two good GEngine patterns demoted).

GEngine coverage is now complete over the corpus: `GENG_X1` lands on 8 engine versions,
`GENG_X3` on the 2 it misses.

### Band discipline extended to GObjects and GWorld

Build 2405 fixed the GNames table; the same audit had never been applied to the other two.
`GWLD_V3` alone takes **22,017 matches** — 95.7 per MB of `.text` on a monolithic game EXE, 2,658
on FF7 Remake by itself — out of six literal bytes. `GOBJ_V1` takes 10,152 (53/MB).

Be precise about what moving them buys, because the two tables differ:

- **GObjects** (V1/V2/V3/V5/V6/V7/CT3 + PS6/PS7, 390–660 → 890–970): a **real** ordering change.
  They previously outranked `GOBJ_G427_2` (700), `G427_4` (720), `CT1` (800) and the Octopath
  `OT_1`/`OT_2` pair (820/840) — all 9–13 literal bytes against these six or seven.
- **GWorld** (V2/V3/V4/V5/V6, 500–580 → 900–980): **consistency only**. They already sat behind
  every other GWorld pattern (highest was 435), so the validator never reached them on any
  oracle. The point is that the band now *means* something. The one genuine ordering change here
  is `GWLD_G42_1` (7 literal bytes) 340 → 880.

**Counter-example kept in the header comment,** because literal-byte count is necessary but not
sufficient: `GOBJ_ES53_1` has 16 literal bytes yet takes 21–475 matches on every monolithic
title — its shape is the generic MSVC function-scope-static + `atexit` registration thunk, so it
matches once per static with a destructor. It stays at priority 100 anyway: it is the landing
pattern for six module-instances, and patterns are scanned in **batches of 8** with an early
return on the first validated match, so winning from batch 1 avoids every later `.text` pass.
Rejecting a few hundred candidates by validation is far cheaper than an extra AVX2 sweep of a
130 MB `.text`. Do not demote a noisy pattern that is also a winner.

### Harness defects fixed along the way

Three of these silently corrupted results rather than failing loudly, which is the dangerous kind:

- `scan_patterns.java` wrote a fixed `scan_patterns.txt`, so a `-process` run over a **modular**
  project overwrote itself and only the last DLL survived. Outputs are now keyed by
  `tag + program + image base` — all three are needed: `FactoryGame-FactoryGame-Win64-Shipping.dll`
  exists in both the 4.26 and 5.2 projects, and Satisfactory v1.2.3.1 holds a good *and* a broken
  import of Core/CoreUObject/Engine under identical names. The broken duplicate had overwritten
  the real 5.6 Engine results.
- Programs with zero executable bytes (failed imports, image base `0000:0000`) are now skipped.
- Hit counts were reported as `hits.size()`, which is capped at 40,000 — hot patterns were
  under-counted. Now counted uncapped, with only the *detail* list capped.
- `extract_patterns.py` parsed the `#define SIG_RIP(...)` macro **definition** as a signature,
  producing a phantom 154th row with `pattern = "<UNRESOLVED:pat>"`.
- The regression model itself was wrong: `>>> SELECTED` names the first pattern that *hits*, but
  `ScanForTarget` validates every match and moves on when they all fail. A `DECOY-ONLY` top
  pattern is a **fall-through (cost)**, not a wrong answer **(correctness)**. `aggregate_sweep.py`
  now replays the real walk. Reading the old line as "what we resolve to" overstated risk.

### Corpus notes for next time

- `Satisfactory_UE521.rep` is **mis-imported**: only the *game* DLL is 5.2, and its
  Core/CoreUObject/Engine are duplicates of the 4.26 DLLs (plus four broken empty programs). The
  real 5.2 engine DLLs + PDBs were imported into a separate `SF521_pdb` project so the original
  stays untouched. UE 5.2 is now a full oracle.
- `Meltopia` ships a 347 MB PDB that its import never applied — it works as a monolithic UE 5.5
  noise probe, and re-importing with the PDB would make it a second symbolised 5.5 oracle.
- `ES2-0517` needs a one-time Ghidra language-version upgrade that `-readOnly` cannot save.
- `Satfi426` is superseded by `Satisfactory_UE426` and can be deleted.

All of this — the truth table, the per-project quirks, and the derivation procedure — is in
[tools/ghidra/GROUND-TRUTH.md](../tools/ghidra/GROUND-TRUTH.md).

-----

## 2026-07-26 - GNames band discipline: short patterns demoted, hand-derived UE4 ones promoted; UE 4.25 folded in (build 2405)

### The GNames table had drifted in *both* directions

The user's read of it was right, and the sweep data was blunt about it. A pattern's band is
supposed to track how **specific** it is — count its literal (non-wildcard) bytes — but:

| pattern | old pri | bytes | literal | measured |
|---|---|---|---|---|
| `GNAM_V5` | **110** (Tier 1) | 19 | 7 | 16,686 hits on 4.27; OK-BEHIND on every engine it touches |
| `GNAM_V2` | 400 | 14 | 6 | 16,692 hits on 4.27 |
| `GNAM_V1`/`V3`/`V4` | 500/520/540 | 8 | **4** | DECOY-ONLY on 4.20/5.5/5.7; 539-2060 hits elsewhere |
| `GNAM_CT3` | **800** | 27 | **20** | UNIQUE-OK on 4.20, MISS on every FNamePool binary |
| `GNAM_G42_1` | 840 | 18 | 9 | UNIQUE-OK on 4.20, MISS elsewhere |

The four-literal-byte patterns were running *before* the twenty-literal-byte ones. The
pre-FNamePool UE4 entries had been hand-derived later and deliberately lengthened to cut
collisions — but nobody moved them out of the last-resort band afterwards.

Re-sorted from measurement, not vibes: `V5→850`, `V2→860`, `V1→870`, `V3→880`, `V4→890`;
`CT3→700`, `G42_1→710`, `CT4→720`, `SAT422_1→730`. Promoting the UE4 set is provably free —
they target `TStaticIndirectArrayThreadSafeRead`/`TNameEntryArray`, a different structure, and
MISS on all four FNamePool binaries. A band-discipline note now sits in `Himmel.h` so the rule
survives: **fewer than ~8 literal bytes means 800+, regardless of what it anchors on.**

**Four of five engine versions improved, none regressed:**

| | before | after |
|---|---|---|
| UE 4.20 | `GNAM_V5` DECOY-ONLY (after ~710 wasted validations) | **`GNAM_CT3` CORRECT (all hits)** |
| UE 5.5 | `GNAM_V5` OK-BEHIND, 15 hits | **`GNAM_ES53_1` CORRECT (all hits)** |
| UE 5.6 | `GNAM_V5` AT RISK, 5 decoys first | **`GNAM_ES53_1` CORRECT (all hits)** |
| UE 5.7 | `GNAM_V5` OK-BEHIND, 86 hits | **`GNAM_SAT425_3` CORRECT (all hits)** |
| UE 4.27 | `GNAM_DI427_2` CORRECT | unchanged |

### UE 4.25 added — and it closes the sparse-delegate gap

`ES2-UE425.rep` (Everspace 2 from a Steam depot, **UE 4.25.2**, full PDB) is the FField/FProperty
transition band. Ground truth: `GUObjectArray` `0x1444B0510`, `NamePoolData` `0x144497D00`
(via `FNameDebugVisualizer::GetBlocks` @ `0x140EF8410`), `GWorld` `0x1445F1160`,
`SparseDelegates` `0x1440070C0`, `GEngine` `0x1445EDAD8`.

**It needs no new patterns** — GEngine `GENG_X1`, GNames `GNAM_V8`, GWorld `GWLD_TQ_1` and
Sparse `SPARSE_DI427_1` are all CORRECT-on-all-hits; GObjects reaches truth via
`GOBJ_SAT425_2`. More usefully it *extends* two families: `SPARSE_DI427_1/_2` and
`GNAM_DI427_1/_2`, both mined on 4.27, are correct here too.

And it settles a documented unknown: the 4.25 PDB gives
`TMap<UObjectBase const*, …>` for `FSparseDelegateStorage::SparseDelegates` — **a raw pointer
key, identical to 4.27 and 5.x**. The "UE 4.23-4.26 uses FObjectKey" claim is now falsified on
two independent UE4 builds; only 4.23/4.24 remain unverified.

`GENG_X1` is now correct-first on **4.20, 4.25, 4.27, 5.6 and 5.7** — five engine versions from
one signature. `GROUND-TRUTH.md` updated with the 4.25 row and its `GS_TRUE` line.

-----

## 2026-07-25 - Removed the 27k-decoy GNames pattern; sparse validator now checks content (build 2404)

Closed the two weaknesses the six-engine harness surfaced last build.

### `GNAM_D7_1` removed

It was `"48 8D 0D ?? ?? ?? ?? E8"` — `lea rcx,[rip+X]; call`, **three literal bytes**, i.e. a
match on essentially every this-call in the image. Measured hit counts: **27,001** on UE 4.20,
**104,897** on UE 4.27, **40,000** on UE 5.5. Every one of those was resolved and validated
(several SEH-guarded reads each) *before* the scan could reach the patterns that actually work
on those titles — on UE 4.20 the winners are `GNAM_CT3` (pri 800) and `GNAM_G42_1` (pri 840),
both well after D7_1 at 560.

It was never the sole correct pattern on any of the eight binaries in the sweep, and its own
comment already said "same as V2 but shorter context; already covered by V2/V5". Dumper-7 can
afford the bare string because it follows the CALL and checks the callee for
`InitializeSRWLock` + a `"ByteProperty"` reference — a second stage we do not implement, so for
us it was pure cost. Re-adding it would need `AobResolve::CallFollow` plus that callee check,
not the byte string.

**Removing it improved DropIn**: GNames now selects `GNAM_DI427_2` → CORRECT (all hits),
where it previously fell through `GNAM_V7` and D7_1's 104,897 validations first.

### Validation is now bounded

Independently of that pattern, `ScanForTarget`'s per-match validation loop gained a
`kMaxValidatePerPattern = 4096` cap with a `LOG_WARN`. If the correct site is not in a
pattern's first 4096 matches, that pattern was not selective enough to trust anyway — and the
warning makes the next over-generic signature visible instead of silently expensive.

### `ValidateSparseDelegates` now checks content, not just shape

The old validator only range-checked two int32s, so it accepted any `.data` blob that looked
vaguely like a TMap — which is why offline sweeps kept finding sparse patterns whose decoys
resolve to unrelated 0x60-stride TSets, and why an `OK-BEHIND` sparse pattern would have been
genuinely dangerous. When the map is **non-empty** it now also requires that one of the first
32 slots holds a key that is a userspace pointer **whose own first qword is a vtable inside the
module image** — which is exactly what `TMap<UObjectBase const*, …>` guarantees and what a map
keyed by FName/int/FString cannot fake. Empty maps are still accepted on shape alone, on
purpose: `FindAll` can legitimately run before anything binds a sparse delegate.

### And the "SPARSE_SP57_1 risk" was a reporting bug, not a real one

Last build flagged `SPARSE_SP57_1` on Solarpunk as "2 decoys scan first". It does not — its
correct site is the *first* match (`0x1413D5EE5`, well below the decoys at `0x143DB6E21`). The
harness printed the warning whenever any decoy existed, ignoring scan order. Fixed to compare
the two indices, so the verdict now reads `CORRECT first (2 decoy(s) scan later, never
reached)`. The strengthened validator above is still worth having — it protects the genuinely
`AT RISK` orderings that a future game may produce.

### Re-verified

Full six-engine sweep re-run after the removal: no target regressed on any binary, `GENG_X1`
still correct-first on 4.20/4.27/5.6/5.7, `SPARSE_ES2_1` still correct on 4.27/5.5/5.6.
`tools/ghidra/GROUND-TRUTH.md` added — the per-project `GS_TRUE` strings, the verdict glossary,
and the procedure for folding in the next PDB game, so the next sweep is copy-paste.

-----

## 2026-07-25 - Six-engine regression harness; a measurement error corrected (build 2402)

Two more symbolised projects arrived — **Everspace re-analysed WITH its PDB** (`ES1-420.rep`,
UE 4.20) and **Satisfactory v1.2.3.1** (UE 5.6.1, modular, CoreUObject+Core+Engine+FactoryGame
all imported). Both were folded into the sweep, which now covers **six engine versions with
real symbols on five of them**: 4.20, 4.27, 5.5, 5.6, 5.7 (+ Avowed 5.3, symbol-less).

### The correction

Last build demoted `GENG_X1` and `GENG_DI427_1` on the strength of "5 decoys on Everspace 4.20".
**That was a measurement artifact.** Everspace had no symbols then, so the sweep had been given
a *placeholder* truth value (`GEngine=5`); every hit necessarily compared unequal and got
labelled a decoy. With the real PDB both are **UNIQUE-OK on 4.20** — `GENG_X1` 1/1,
`GENG_DI427_1` 5/5. Priorities restored, and `GENG_X1` is now the lead GEngine pattern: it is
correct-first on **4.20, 4.27, 5.6 and 5.7**, the broadest single signature in the file.

Systemic fix so it cannot recur: `scan_patterns.java` now emits **`NO-TRUTH`** instead of
`DECOY-ONLY` when a target has no plausible truth value, and refuses to render decoy counts at
all in that case. It also skips `CallFollow`/`Symbol*` resolutions, whose model it cannot
reproduce (`GNAM_V7` is CallFollow and had been scoring phantom decoys the same way).

### The regression harness (answers "does adding AOBs break anything?")

`scan_patterns.java` gained a **`>>> SELECTED`** line: walking priority order, which is the
FIRST pattern that hits, and does it reach truth? That mirrors `Genau::ScanForTarget`, which
validates each match and takes the first that passes — so a newly-added lower-numbered pattern
can only do harm if it hits, survives validation, AND is wrong.

Result across all eight binaries/modules: **every time a newly-added pattern is selected it is
CORRECT on all hits** (`GENG_X1` ×4, `GENG_ES55_1`, `GWLD_DI427_1`, `SPARSE_DI427_1`). No
existing target changed hands on any binary — Solarpunk still selects `GWLD_SP57_1` /
`SPARSE_SP57_1`, ES2 still selects `GWLD_ES2_1` / `SPARSE_ES2_1`.

The harness also surfaced two **pre-existing** (not new) weaknesses worth recording:
* `GOBJ_ES53_1` (pri 100) and `GNAM_V5`/`GNAM_V7` are selected first on several binaries and
  reach truth only after the validator rejects their decoys — by design, since
  `ValidateGObjects`/`ValidateGNames*` are strong. The one to watch is `SPARSE_SP57_1` on
  Solarpunk (2 decoys scan first) because `ValidateSparseDelegates` is deliberately weak.
* UE 4.20 GNames is covered only in the last-resort band (`GNAM_CT3` pri 800, `GNAM_G42_1`
  pri 840 — both UNIQUE-OK, anchored on `FName::GetNames`) while `GNAM_D7_1` fires **27,001**
  decoys at pri 560 first. Correct, but slow.

### Satisfactory 5.6.1 — no new patterns needed

All five targets already resolve: GObjects `GOBJ_ES53_1`, GNames `GNAM_V5`, GWorld `GWLD_SF_1`,
Sparse `SPARSE_ES2_1`, GEngine `GENG_X1`. Layout note: **the name pool moved from CoreUObject
to Core by 5.6** — `NamePoolData` `0x18082E8C0`, recovered from `FNameDebugVisualizer::GetBlocks`
(`lea rax,[pool+0x10]; ret`), the same 2-instruction oracle that worked on DropIn.

`SPARSE_ES2_1` is now verified correct on **UE 4.27, 5.5, 5.6 and 5.7** — four engine versions
from one signature.

### Everspace 4.20 also validated the consensus technique

Before its PDB existed, running the full database and keeping addresses that ≥3 independent
patterns agreed on gave GWorld `0x1432E1AC0`, GObjects `0x142E797F0`, GNames `0x1431DEAD8`.
The symbols confirmed **all three exactly** (`Names` is reached via `FName::GetNames`, which
lazily `new`s a 0x408-byte `TNameEntryArray` — 4.20 predates FNamePool). Consensus is a sound
fallback for any symbol-less binary.

-----

## 2026-07-25 - Three more Ghidra projects swept; GEngine gains UE5.5 (build 2401)

Followed the DropIn work by running the same audit over three donated Ghidra projects.
Net result: **one new signature, two demotions, and a clear "this one can't help" verdict.**

**Everspace 2 `ES2-0517` — UE 5.5, and the second symbolised oracle.** The project name's
`0517` is a **date, not a version**. There is no `++UE5+Release-` string in the image, so the
version was pinned **structurally**: `FFieldVariant`=0x08 (≥5.1.1), `UEnum::Names` still
`TArray<TTuple<FName,int64>>` (<5.6), `FUObjectItem` 24B **with `RefCount`@+0x14**, classic
`FChunkedFixedUObjectArray` order (<5.8), and — decisively — the PDB's
`EUnrealEngineObjectUE5Version` enum ends at `ASSETREGISTRY_PACKAGEBUILDDEPENDENCIES`, whereas
vendored UE 5.8 adds `METADATA_SERIALIZATION_OFFSET` / `VERSE_CELLS` after it. `dump_types.java`
gained enum support for exactly this: **the last member of that enum is the most reliable UE
version marker available when the build strings are stripped and there is no PE on disk.**

Audit found GObjects/GNames/GWorld/Sparse well covered but **GEngine hitting on only 1 of 4
patterns** — `GENG_X1`/`X2` both MISS on 5.5 because 5.5 emits `FEngineLoop::Tick`'s null check
as a NEAR `0F 84` where 4.27/5.7 use a short `74`, a length change no nibble can bridge.
Added **`GENG_ES55_1`** (`UEngine::GetEngineSubsystem<T>` prologue): UNIQUE-OK on **both** 5.5
(7 sites) and 5.7 (6 sites), zero hits on 4.27/5.3/4.20.

The obvious 5.5 `FEngineLoop::Tick` pattern was **rejected**: 6 hits on Avowed resolving to six
*different* globals. Recorded as a rule — **divergent hits mean a generic shape; accepted
patterns' extra hits all converge on one address.**

**Everspace 1 `ES1` — UE 4.20, no PDB.** Usable two ways. As a **negative control** it demoted
two GEngine patterns that had looked fine on a 3-binary sweep (`GENG_X1` 1 decoy, `GENG_DI427_1`
5 decoys here), so `GENGINE_PATTERNS` was reordered to put the three all-clean patterns
(`X2`, `ES55_1`, `SP57_1`) first. And via **pattern consensus** — an address independently
agreed on by N distinct signatures — it yielded truth without symbols: GWorld `0x1432E1AC0`
(12 patterns), GObjects `0x142E797F0` (8), GNames `0x1431DEAD8` (3). No GEngine/Sparse
consensus, as expected: 4.20 predates sparse delegates (4.23+).

**Satisfactory `Satfi426` — UE 4.26 modular: cannot help as supplied.** The .rep holds only 3
game DLLs; `FactoryGame-CoreUObject`/`-Engine`, which DEFINE the globals, were never imported.
The game module does carry the IAT slots (`__imp_?GUObjectArray` `0x180722950`,
`__imp_?GWorld` `0x180727CB8`, `__imp_?GEngine` `0x180727CB0`) — the "via `_imp_`" shape
`GOBJ_SF_1` already models, with `RipDeref` doing the second hop at runtime — but **all 490
referencing sites are game code** (`UFG*`/`AFG*`/`FFG*`), so nothing mined there would
generalise, and no existing pattern scores a correct hit on it. Re-importing those two DLLs
would make the project productive. (`find_syms3.java` stopped filtering `__imp_*` so the IAT
is visible at all.)

**Re-verified: all 12 DI427 signatures are 0-hit / 0-decoy on both new binaries**, so the
gauntlet now stands at five: DropIn 4.27, ES2 5.5, Solarpunk 5.7, Avowed 5.3, Everspace 4.20.

-----

## 2026-07-25 - DropIn UE 4.27 PDB: 12 new AOBs, a new GEngine target, and three corrected premises (build 2399)

**DropIn - VR Battle Royale** (Steam, `DropIn.exe`) is **UE 4.27.2** (`++UE4+Release-4.27-CL-18319896`,
2021-11-30) and ships its full **286 MB PDB** — the project's first symbolised UE 4.27 oracle.
It is a **Development** build (`.msvcjmc` + Live++ `.lpp_*` + `.uedbg` + engine source paths in
`.rdata`), non-editor. Ghidra project `D:\Tools\GHIDRA_Projs\DropIn.rep`.

**Method — the three-binary gauntlet.** Every candidate had to be `UNIQUE-OK` on DropIn (every
hit resolves to the true VA, zero decoys) **and** 0-hit-or-correct on Solarpunk (UE 5.7) and
Avowed (UE 5.3). This is stricter than the SP57 rule and it earned its keep twice: a 14-byte
GObjects form that is decoy-free on DropIn produces 1 decoy on Solarpunk and **9** on Avowed,
and making a sparse pattern register-agnostic with nibbles made it *worse* (picked up two
unrelated 0x60-stride `TSet`s that scan **before** the real sites — fatal, because
`ValidateSparseDelegates` only range-checks two ints).

**What the audit of the existing database found.** Replaying all 140 signatures over the whole
129 MB `.text`: GWorld 12 working, GNames 12, SparseDelegates 1 — and **GObjects 0 of 52**.
Root cause, measured across all 400 xrefs to `ObjObjects.Objects`: the chunk-load destination
register is rdi(156)/rsi(92)/r14(63)/rbx(40)/… and **never rcx**, because rcx is the *index*
register at every one of those sites. `GOBJ_V1` hardcodes `48 8B 0C C8` (dest = rcx), so the
entire V-series is structurally unable to fire. Compounding it, this build's `FUObjectItem` is
**32 bytes** (`StatID` compiled in), so the within-chunk math is `shl r,5`, not the 24-byte
`lea r,[r+r*2]; shl 4` the patterns assume.

**Added — 12 signatures (source tag `DI427`)**: `GOBJ_DI427_1/2/3`, `GNAM_DI427_1/2`,
`GWLD_DI427_1/2`, `SPARSE_DI427_1/2`, and 4 for the new GEngine target.
`GWLD_DI427_2` is the first `mov qword[rip], imm32` (C7-form) **store** pattern in the file —
that opcode shape was absent from all 52 GWorld entries, so this class of site was invisible in
every game, not just this one (note `totalLen = 11`, not 7).

**New — `AobTarget::GEngine`** (`Himmel.h`, `GENGINE_PATTERNS[]`). Resolves **`&GEngine`, the
static slot**, not the object. `GENG_X1` (`UWorld::GetGameViewport`) and `GENG_X2`
(`FEngineLoop::Tick`) are **cross-version** — verified on UE 4.27 *and* UE 5.7, and X1 also
matches Avowed (5.3). Two payoffs: `FindLiveGameEngine` stops walking the entire object pool
resolving a property offset per class (one deref instead), and — the user-visible one — a
GameEngine-rooted CE record can be AOB-wrapped like a GWorld-rooted one instead of baking in an
`allocateMemory` snapshot of a `UEngine*` that goes stale on restart. Scanned after
GObjects/GNames in `FindAll` because the validator asks the reflected class for `GameViewport`.
Surfaced over the pipe (`gengine`, `gengine_method`, `gengine_aob*`) and as a System-tab row.

**Three premises corrected by ground truth:**

1. **Sparse delegates on UE4 (feature unlocked).** `Aura.cpp`'s `UEVersion < 500` gate rested on
   "UE 4.23-4.27 keys the outer map by `FObjectKey {FWeakObjectPtr, int32}` (16B)". The PDB says
   the key is a raw `UObjectBase const*` — same as UE5 — and `FObjectKey` is **8** bytes
   (`{int32 ObjectIndex; int32 ObjectSerialNumber}`); `FWeakObjectPtr` *is* those two ints, so
   the old note double-counted. All six walker constants already matched 4.27 exactly. The gate
   is now a **runtime key-shape probe** (first occupied outer key must look like a userspace
   pointer), so 4.23-4.26 — for which we still have no symbols — fail safe rather than being
   guessed at. `SPARSE_ES2_1` was verified to resolve correctly on 4.27 all along.
2. **`Grimoire::FPROPERTY_ELEMSIZE` was 0x38 = `ArrayDim`, not `ElementSize` (0x3C).** Latent:
   only used when dynamic offset validation fails. The `Genau` heuristic was likewise
   `Offset_Internal - 0x14`; the correct delta is **0x10** in *both* known layouts
   (4.25-4.27/5.0-5.1 → 0x4C-0x3C, 5.1.1+ → 0x44-0x34).
3. **`DetectVersion` Tier 1 could essentially never match.** Its needles carry a trailing dot
   (`"4.27."`) but real tags are `++UE4+Release-4.27` with nothing after the minor. Tier 1 now
   drops the dot (the `++UEx+Release-` prefix is already all the context it needs) **and** runs a
   second UTF-16LE pass — DropIn keeps the tag *only* as a wide literal (4 copies, zero ASCII).
   `DetectVersionFromPEResource` also gained a StringFileInfo `ProductVersion`/`FileVersion`
   fallback: DropIn's is literally `++UE4+Release-4.27-CL-18319896`, an O(1) lookup we were
   ignoring. `kVersionDetectLogicRev` 1 → 2 so cached versions recompute once.
   *(Correction to an earlier read: DropIn **does** have a valid `VS_FIXEDFILEINFO` (4.27.2) —
   .NET's `FileVersionInfo` returns empty strings for it because they sit under a non-default
   translation, which is why it first looked absent.)*

**ProcessEvent detection hardened** (the primary pattern path was already correct — build 648 —
and this PDB is its 4th independent 4.27 confirmation at **+0x220**, the first with a symbol
proving the slot *is* `UObject::ProcessEvent`):
* `kBodySize` `0xF00` → `0x2000`. Measured: pattern 2 sits at byte **3537** of a 5182-byte
  `UObject::ProcessEvent` — 303 bytes (7.9%) inside the old window.
* `FindAnyValidVTable` → `CollectCandidateVTables` (up to 12 distinct). `AActor::ProcessEvent`
  is a thin override containing the FUNC_Native test but **not** the high-flag test, so the scan
  returns −1 for any Actor vtable and used to fall through to the version table.
* Version fallback: `>= 427 → 0x220`. `0x218` is slot 67 = `UObject::OverridePerObjectConfigSection`,
  one slot before PE — exactly the build-647 failure. 4.25/4.26 deliberately left at `0x218`:
  unmeasured, and RE-UE4SS's vendored `VTableLayout_4_2*_Template.ini` cannot settle it (computed
  absolute slots give 4.27 = 70/`0x230`, i.e. those templates are editor-inclusive and sit 2 slots
  above every non-editor measurement).

**Tooling landed in `tools/ghidra/`** — `scan_patterns.java` (supersedes `verify_aob.java`: TSV
input, nibble wildcards, and a decoy-**ordering** verdict), `extract_patterns.py` (whole
`Himmel.h` → TSV), `gen_cands.py` (xrefs → mechanically enumerated candidates), plus
`dump_xrefs2` / `dump_types` / `dump_vtables` / `pe_probe` / `dump_dataat` / `dump_func` /
`find_syms3` / `scan_strings` / `probe`. `tools/README.md` documents the full PDB→AOB loop.

**Layout facts now backed by real 4.27 symbols** (see technical-notes): `FProperty` ordering
confirms the hard-won `feedback-fproperty-layout` note exactly (`ArrayDim@0x38`,
`ElementSize@0x3C`, `PropertyFlags@0x40` — +4, not +8); `Offset_Internal@0x4C`,
`FStructProperty::Struct@0x78`, `FField Next@0x20/Name@0x28`, `Outer@0x20` are byte-identical to
what the tool already reports for SBDR; `UEnum::Names` is still the classic
`TArray<TTuple<FName,int64>>` (the `FNameData` change really is 5.6+); legacy `UProperty` does
not exist at 4.27; the `FNamePool` model (`FRWLock@0`, `CurrentBlock@8`, `Blocks[8192]@0x10`,
`0x20000`-byte blocks, stride 2) that `ValidateGNamesStructural` assumes is exact.

**Not done (deliberate):** re-rooting a *GWorld*-rooted CE export through GEngine when GWorld's
AOB fails. `GEngine→GameViewport→World` does re-enter the GWorld subtree, but it needs path-prefix
re-derivation — bigger than this change. Also unaddressed: the stride sweep still tries only
{16, 24, 20}, so a 32-byte `FUObjectItem` like DropIn's is not in the candidate list (adding 32
naively is unsafe — it aliases on 16-byte-item games, where it would validate and halve the
object count).

-----

## 2026-07-25 - AOB priority scheme widened 0–100 → sparse 0–1000 bands (build 2393)

`Himmel.h`'s AOB priorities were cramped in 0–100 with collisions (e.g. three GObjects patterns all at
10), leaving no room to insert a new pattern between two existing ones without renumbering. Re-spread
all 140 pattern entries across **sparse 0–1000 bands** (exports 0–30 · Tier-1 long/specific 100–290 ·
Tier-2 medium 300–490 · Tier-3 short 500–590 · patternsleuth 600–690 · UE4/legacy 700–790 · last-resort
800–990), stepping by 10 within a band so there's always room to slot a new pattern in. Done by a
verified script (`scratchpad/repriority.py`): it parses each `AobSignature[]`, re-bands each entry from
its old priority, and **asserts the new ordering is identical to the old** (`sort by new priority` ==
`sort by (old priority, textual position)`) with no duplicates — so **scan behaviour is unchanged**;
the only difference is that previously-tied priorities are now deterministic (a strict improvement — a
tie was resolved by an unstable `std::sort`). Absolute values are meaningless; only per-target order
matters. SP57 GWorld patterns are now pri 100–160 (were 10–13), SP57 Sparse 100–120 (were 15–16).
Header comment documents the bands + "pick an unused value in the matching band" rule.

-----

## 2026-07-25 - PDB-mined UE5.7 GWorld + SparseDelegate AOBs; the GWorld-decoy root cause (build 2383)

Solarpunk (rokaplay, `SolarpunkSteam-Win64-Shipping.exe`) is **UE 5.7 and ships a full 1.6 GB PDB** —
our first UE5.7 symbol oracle. Its reported "GWorld AOB fails" turned out to be a precise, now
fully cross-validated failure, and the PDB let us fix it with **verified-unique** signatures instead
of guesses.

**Root cause (two independent methods agree).** UE5.7's MSVC codegen shifted, so **all** priority-10-20
UE5 GWorld patterns (ES2_1-6, SF_1, GH_1/2, TQ_1/2, V1) get **0 hits**. The scan reaches the generic
`GWLD_SF_2` (pri 21), which matched a single **decoy** `.data` global `0x1478C25A8` (0xA8B0 below the
real GWorld) that the deliberately-loose `ValidateGWorldBasic` (readable + `LooksLikeDataPtr`)
accepted. `UE5_Init`'s secondary guard then caught it — scan-0.log / init-0.log:
`GWLD_SF_2: Unique match -> 0x7FF655F125A8` → `GWorld=0x7FF655F125A8 does not deref to a UWorld —
recovering...` → `recovered via instance_scan_recovery -> 0x7FF655F1CE58`. That recovered address
equals **exactly** the PDB symbol (`GWorld` RVA `0x78CCE58` + imagebase `0x7FF64E650000`). So GWorld
*worked via the fallback net*, but the AOB path failed and cached no fast hint (`gWorld.patternId`
saved empty → a relaunch re-scans clean, **no cache clear needed**).

**Fix (`dll/src/Himmel.h`, source tag `SP57`).** Four GWorld patterns at pri 10-13 (before the decoy
SF_2) + two SparseDelegate patterns at pri 15-16 (`SPARSE_ES2_1` got **0/21** on this build). **Every
candidate was scanned against the real `.text` and its every hit resolved the DLL's way before
inclusion** — kept only `correct>=1, decoys=0` (or decoys strictly higher-addressed so the real site
validates first):

| ID | pri | anchor | hits/correct/decoy |
|---|---|---|---|
| `GWLD_SP57_1` | 10 | UGameEngine::Tick `cmp [rcx+2C0]` (tolerates inserted `mov rcx,[rbx+rax]`) | 1/1/0 |
| `GWLD_SP57_2` | 11 | FMallocLeakReporter::WriteReports (`mov rsi,rcx` variant of GH_1) | 1/1/0 |
| `GWLD_SP57_3` | 12 | UEngine::GetWorldFromContextObject fallback | 1/1/0 |
| `GWLD_SP57_4` | 13 | UActorComponent::On*PhysicsState `mov [rax+298]` (0x298 version-specific → last) | 2/2/0 |
| `SPARSE_SP57_1` | 15 | TSet::Find/FindOrAdd/EmplaceByHash element index (mov rdx) | 5/3/2 (decoys higher-addr) |
| `SPARSE_SP57_2` | 16 | TSet::Remove element index (mov r8) | 1/1/0 |

(The FinishDestroy twin-ref candidate was **dropped** — verification found 1 decoy.)

**Reusable tooling** (`tools/ghidra/`): `dump_global_xref_aob.java` resolves UE globals by PDB name and
dumps per-xref raw window + disp-masked AOB + read/write kind + function; `verify_aob.java` scans
`.text` and resolves every hit exactly like `Genau::ScanForTarget`, reporting hits/decoys/correct so a
candidate is proven before it ships. Two traps baked into the headers: a ~GB PDB OOMs headless →
`export _JAVA_OPTIONS=-Xmx16G`; and never touch **variable** symbols (`getAddress()` lazy-loads the
whole datatype list → OOM) — filter `SymbolType.LOCAL_VAR/PARAMETER`.

**Corroborations (doc-only claims now backed by real symbols):** SparseDelegates outer key is a raw
`UObjectBase*` (symbol `EmplaceByHash<TKeyInitializer<UObjectBase_const_*_&&>>` — confirms the
"UE5.0+ raw-pointer key vs UE4.23-4.27 FObjectKey" note); UWorld member `+0x2C0` compared in
UGameEngine::Tick (a 3rd version behind ES2_3/SF_1's hardcoded 2C0); GObjects tool-convention =
symbol base **+0x10** (ObjObjects); GNames only 2 xrefs, both in `GetNamePool` (function-static).
The AOB parser is confirmed **full-byte `??` only** (no nibble wildcards) and the scanner is AVX2
single-anchor (`Macht::ScanRegion`), so all SP57 patterns use full-byte wildcards.

**LIVE-VERIFIED (build 2384).** Redeployed + re-tested on Solarpunk: `GWLD_SP57_1: Unique match ->
0x7FF6D4D1CE58` — the real GWorld (imagebase `0x7FF6CD450000` + RVA `0x78CCE58`), method **`aob`**, no
"does not deref" warning, no recovery. GWorld scan **143 ms (1 batch)** vs the old **1.24 s (2
batches)**. `SPARSE_SP57_1 -> 0x7FF6D4B1A4B0` — Sparse **found** (was `not_found`). `FindAll: Complete`
all four `(aob)`.

**Then — nibble wildcards + validator hardening (build 2387).**

*Nibble match.* `Macht::ParsePattern` now accepts nibble tokens: `4?` fixes the high nibble (matches
0x40-0x4F, i.e. any REX prefix), `?5` fixes the low nibble. Representation changed from a `{0,1}` mask
to a per-byte AND-mask (`0x00` wildcard / `0x0F`,`0xF0` nibble / `0xFF` literal) with pre-masked
`bytes`; every verify site is now `(mem & mask) == bytes`. **Perf is unchanged**: the AVX2 hot loop
still broadcasts a single full-literal anchor (nibble bytes are never chosen as the anchor); nibbles
only touch the sparse per-candidate verify. `ParsePattern` moved to `Macht.h` as `inline` (pure — no
Win32) so `dll_helpers_test` exercises the real parser (`Test_Macht_ParsePattern_Nibble`, 875/0).
First use: `GWLD_SP57_1`'s `?? 8B` → `4? 8B` (the inserted `mov rcx,[rbx+rax]` REX byte is 0x4A).

*Validator hardening (the decoy's root fix).* `ValidateGWorldBasic` was too loose (readable +
`LooksLikeDataPtr`) — that is what accepted the decoy in the first place. It now adds an
offset-independent C++-object guard: a real UWorld's `[[world]]` (vtable → first virtual method) is a
code pointer in a module image; a `.data` global that merely holds a data-shaped pointer fails it, so
the scan rejects the decoy and continues to the real GWorld. Because a rejected decoy can leave GWorld
= 0, the `UE5_Init` recovery gate (Frieren.cpp:388) no longer requires `ptrs.GWorld != 0` — so a
no-valid-AOB title (Avowed) still recovers. No regression for the 30+ tested games (a real GWorld
always passes the vtable check).

**Also found + FIXED — the M5 see-through-close crash (build 2389).** Exercising the
previously-untested M5 scenario (leave See-through ON, close the GAME) crashed the injected DLL:
event log Id 1000, faulting module VERSION.dll, `0xc0000409` (__fastfail). A WER minidump
(`tools/pe/minidump_triage.py`) showed the faulting thread stack was **pure `version.dll` + the ntdll
fail-fast chain — no game-engine frames**, i.e. the fault was in OUR worker thread, in our code. Root
cause: the See-through worker's per-tick invokes (`InvokeSetHidden`/`InvokeRetVec`) size a
`std::vector<uint8_t> buf(fi.parmsSize)` directly from a UFunction's ParmsSize, with **no upper
bound**. During the game's own shutdown a freed/reused UFunction reads a garbage-huge ParmsSize → the
vector throws `std::bad_alloc` → it escapes `WorkerLoop` (no handler) → `std::terminate` →
`__fastfail(0xC0000409)`. Fix, two levels: (1) `FindFuncByName` now rejects an out-of-range ParmsSize
(`> 0x4000`) so the per-tick invoke is a clean no-op instead of allocating; (2) `WorkerLoop` wraps
`Tick()` in `try/catch` so no exception class can ever `std::terminate` the host. **Dunste (Fly) had
the identical invoke-with-parmsSize twin** (`InvokeSetCollision`) → same two fixes applied. The four
field-writer workers (Solitar/Laufen/Hemmung/Solide) write via SEH-guarded reads/writes with no
game-sized allocation, so they have no equivalent trigger. (`UE5_Shutdown` is still never called on a
game-close — `DllMain(DETACH)` is a deliberate no-op — but that's now moot: the crash was the worker
faulting during the game's shutdown window, not the missing clean teardown.)
**LIVE-VERIFIED (build 2389, 2026-07-25, a DEBUG build — stricter):** re-ran the exact M5 repro on
Solarpunk — `SeeThrough: worker started` + a `Time:` re-assert worker both live, then closed the game.
**No crash, no dump, no event-log error** (vs. the 18:38 run that produced all three); the `try/catch`
"tick threw" line never fired, so the ParmsSize cap headed off the `bad_alloc` before it could throw.
GWorld still `aob` (GWLD_SP57_1, now a cached hint).

-----

## 2026-07-23 - MEASURED then SHIPPED: the lean walk payload (builds 2339 / 2351)

Build 2335 ended with "the next lever is BYTES, not messages" and an admitted blank: nobody had
measured how much of a `walk_instance` payload the CE export actually consumes. Two steps closed it.

**Measured first (build 2339).** `scripts/analysis/walk_payload_audit.py` byte-accounts every JSON
key of the `walk_instance` / `walk_instance_batch` responses in a UI pipe log against a key-by-key
map of what `CeXmlExportService` / `CsxExportService` really read (each verdict cites its consuming
line). On a real Copy CE XML on SEED - 6,778 responses, 14,263 instances, 27,002 complete field
objects:

| scope | share | used | csx-only | **unused** |
|---|---:|---:|---:|---:|
| `field` | 52.7% | 60.9% | 18.6% | **16.7%** |
| `elem` (inline array elements) | 20.3% | 43.9% | - | **44.6%** |
| `instance` (per-instance header) | 20.4% | 0% | - | **99.0%** |

The exporter reads `result.Fields` and nothing else, so the whole per-instance header is dead
weight; and the biggest single droppable key is `elements[].h` (element raw hex, ~9% of the entire
payload). Because CE XML output is **structural** - description + offset + CE type + drill-down -
every decoded VALUE the walk carries is dead for it. Full table in
[multipipe-eval.md](multipipe-eval.md) section 10.6.

**Then shipped (build 2351).** `lean: true` on `walk_instance` / `walk_instance_batch` omits exactly
those keys (drop list in [pipe-protocol.md](pipe-protocol.md)). Three properties make it cheap to
trust: it is **subtractive only** (a lean object is the full object minus keys, so no client needs a
new parsing branch and an older DLL that ignores the flag stays correct); the **default stays
full-fat** because `CsxExportService` calls the same `ResolveDrilldownAsync` and genuinely reads
`hex` / `bool_mask` / `bool_byte_offset`, so only the three CE XML callers opt in; and
`WalkInstanceLeanTests` runs the same export over full and lean payloads demanding **byte-identical
XML** - mutation-checked, so it fails when a key the exporter really reads is dropped.

**In-game verified (build 2353, SEED).** Same object, before (DLL 2338) vs after (DLL 2353):
**payload 1,982,875 -> 1,168,944 bytes across the same 134 batch responses, -41.0%** - section
10.6's prediction held. The XML is unchanged: 149,621 lines and 14,326 leaves both sides, with 15
differing lines that are all per-session values (the root heap address, and 14 DropDownList entries
whose FName ComparisonIndex moved while the name half stayed identical). DLL serialise time fell
20% (146.7 -> 116-119 ms) consistently across both runs.

**The wall-clock is still not claimed.** `ipc` did not move (207 -> 213-216 ms) despite the bytes
nearly halving - at ~15 KB per response over 134 calls, IPC is dominated by fixed per-call cost, so
this export is simply too small to attribute. Build 2335's lesson applies to its own successor:
repeat on the ~20k-call export before quoting a speed-up. Also of note from the audit tooling:
`UE5DUMP_PIPE_LOG_FULL=1` uncaps `PipeClient`'s 1024-char body log so the audit can sample whole
payloads instead of prefixes.

-----

## 2026-07-23 - RESULT: struct-tree batching is 1.71x, and the IPC cost model was wrong (build 2335)

`top:` names `walk_instance_batch` - the acceptance criterion - and the same Copy CE XML on SEED
went **5,893.3 -> 3,437.1 ms (1.71x)**, dispatches **22,522 -> 1,355 (16.6x fewer)**.

| | before | after |
|---|---:|---:|
| wall | 5,893.3 ms | **3,437.1 ms** |
| dll | 1,751.7 | 1,505.5 |
| **ipc** | **3,531.7** | **1,278.4** |
| ui | 609.9 | 653.2 |

**The projection was 2.4-3.5x; reality is 1.71x - and the gap is informative.** Section 10.4 flagged
the projection as an upper bound because it assumed batching adds nothing. The data now says
precisely what it adds: **IPC is not purely a per-round-trip cost.** At the old 0.157 ms/call, 1,355
calls should have cost ~212 ms; they cost 1,278 ms. Of the original 3,532 ms, **~2,253 ms was fixed
per-round-trip overhead (removed)** and **~1,066 ms is payload-proportional** - the same bytes still
cross the pipe however many messages carry them. `ui` rose 610 -> 653 ms for the same reason: bigger
JSON documents cost more to parse.

Two secondary findings, recorded in [multipipe-eval.md](multipipe-eval.md) section 10.5:
- **Average batch size is ~16.6**, far below the 200 chunk cap - the limit is struct fan-out, not
  the cap, so raising the chunk would achieve nothing.
- **Worst single dispatch rose 14.5 -> 85.2 ms** (a batch does ~16 walks). Harmless at that scale,
  but it is the exact metric Phase 1 cared about, and batching moves it the wrong way - one more
  reason those two ideas do not belong together.

**Next lever is BYTES, not messages.** The residual 3,437 ms is dll 1,506 (real work) + ipc 1,278
(mostly payload) + ui 653 (parse); trimming fields the export never reads would attack the last two
together. Left unquantified on purpose - nobody has measured what share of a `walk_instance` payload
the CE export actually consumes, and this cycle already showed what happens when a projection
outruns its measurement.

-----

## 2026-07-23 — The batching was aimed at the wrong loop; fixed at the struct tree (build 2335; dev, UI-only)

Build 2329 shipped `walk_instance_batch` and batched the CE export's object-pointer
drilldown. The next live run showed **no change at all** — 22,522 dispatches, still
`walk_instance 22,521x`, and no fallback warnings, so the batch command was simply never
called:

```
PERF Copy CE XML: wall 5,893.3 ms · busy 1,751.7 ms (29.7%) · 22522 dispatches
   · split dll 1,751.7 / ipc 3,531.7 / ui 609.9 ms · top: walk_instance 1,751.7ms/22521x
```

**The calls come from the STRUCT tree, not the pointer drilldown.**
`ResolveStructFieldsIntoAsync` → `ResolveStructRecursiveAsync` issues one
`walk_instance` per `StructProperty` and recurses into nested structs — and a UE class is
full of them (FVector, FTransform, custom structs, each nesting further). The
object-pointer loop that got batched is a minor contributor by comparison.

**Why the fix isn't "batch that recursion too".** `ResolveStructRecursiveAsync` produces a
**depth-first flattened list**, and that traversal order — with its accumulated
`Parent.Child` name prefixes and summed offsets — *is* the emitted CE XML's field order.
Restructuring it breadth-first would reorder every exported struct.

So: a separate **breadth-first prefetch** (`PrefetchStructTreeAsync`) walks the tree one
batched call per level, bounded by the same `MaxStructDepth`, and the **unchanged**
depth-first emit reads from that cache. Output order is preserved by construction, because
the emit traversal is literally the same code.

Details worth keeping:
- **One shared predicate** (`IsRecursableStruct`) decides both what the prefetch fetches and
  what the emit recurses into, so the two can't drift. A mismatch is harmless either way — a
  superset wastes a walk, a subset falls back to a live call — but matching is what makes it pay.
- **The cache is a pure optimisation.** Any miss (older DLL, failed batch, an unanticipated
  shape) walks live exactly as before. `PrefetchStructTreeAsync` swallows batch failures and
  returns what it has.
- **Dedup doubles as the cycle guard**: a self-referential struct is fetched once, then the
  depth bound stops the descent.
- Cache key includes the class address — the same data address walked as a different class is
  a different walk.

**Verification:** 2911 tests green (+4), the important one comparing batched vs
batch-disabled output field-for-field (names, types, offsets, order) over a deliberately
asymmetric tree. AOT publish clean. **Not verified: the speed-up** — that needs another live
Copy CE XML, and this time the check is simple: `top:` should show `walk_instance_batch`,
not `walk_instance`.

**Process note:** build 2329's claim rested on the round-trip count alone; a single grep of
the next PERF line for `walk_instance_batch` would have caught the miss immediately. That
check is now the stated acceptance criterion rather than the projection.

-----

## 2026-07-23 — `walk_instance_batch`: act on the measurement (build 2329; dev, DLL + UI)

Implements what §10.4 concluded. A Copy CE XML issued **20,357** single `walk_instance` calls whose
cost split as dll 30% / **ipc 59-73%** / ui ~0% — per call, 0.16-0.21 ms of round-trip overhead
carrying 0.08 ms of actual work. Collapsing the calls is the lever.

**Built to the `walk_class_batch` precedent, all three safety layers:**
- **Layer 1 — structural.** The DLL handler is a trivial `for` loop over `Ubel::WalkInstance`, the
  same function the single command calls. Equivalence is true by construction, not by promise.
- **Layer 2 — shared serialiser.** New `EncodeInstanceWalkToJson` on the DLL side (the single
  command's inline emit was extracted into it) and `DumpService.DeserializeInstanceWalk` on the UI
  side. One emitter, one parser, so the two paths cannot disagree about a field — including the
  **optional** keys (`is_definition` / `stale` / `props_size`), which is where an independently
  written batch encoder diverges first.
- **Layer 3 — equivalence test.** `WalkInstanceBatchEquivalenceTests` runs the same fixture through
  both paths and compares field-for-field, and covers chunk splitting, ordering, and both
  degradation paths.

**The export walks breadth-first per level now.** `ResolvePointerInstancesRecursiveAsync` collects
every pointer target at one depth, walks them in one batched call, then recurses. That restructuring
is what makes batching possible at all — targets at one depth are independent. The `visited` /
`resolved` guards deliberately stay outside the batch so cycle protection and dedup behave exactly
as before.

**Two failure modes, both degrading rather than losing data:**
- A batch that throws — including an **older DLL that doesn't know the command** — replays that
  chunk as single calls. Each can then fail independently, exactly as before batching existed.
- **A short or long reply also falls back.** Consuming N-1 rows positionally would silently attach
  one instance's fields to a *different* address — in a CE export that is a wrong pointer chain that
  looks perfectly valid. There is a test for precisely this.

**Verification:** all 4 proxies + DLL build clean; 2907 tests green (+7). **Not verified: the actual
speed-up** — the projection is 2.4-3.5×, but it is an upper bound (it assumes batching adds nothing,
while a larger payload costs something on both sides). The next live Copy CE XML will print its own
`split dll / ipc / ui` line and settle it.

-----

## 2026-07-23 — MEASURED: IPC is 59-73% of a heavy export; batch `walk_instance` (build 2327; docs)

The decomposition built earlier today, read on real data. Three Copy CE XML runs on **SEED BATTLE
DESTINY REMASTERED (UE 4.27)**:

| run | wall | dll | ipc | ui | calls | per call (dll / ipc / ui) |
|---|---:|---:|---:|---:|---:|---|
| A | 5,548.3 ms | 1,689.5 (30.5%) | **3,290.0 (59.3%)** | 568.8 (10.3%) | 20,357 | 0.083 / 0.162 / 0.028 ms |
| B | 555.1 ms | 157.7 (28.4%) | **406.3 (73.2%)** | 0.0 (0%) | 1,901 | 0.083 / 0.214 / 0.000 ms |
| C | 614.6 ms | 165.9 (27.0%) | **411.8 (67.0%)** | 36.9 (6.0%) | 2,108 | 0.079 / 0.195 / 0.018 ms |

**IPC is the cost — 59-73% of wall-clock, roughly 2× the actual DLL work — and it is exactly the
part batching removes.** The per-call figures barely move across a 10× spread in operation size,
which is what a fixed per-round-trip overhead looks like. UI-side per-result work is negligible
(0.000-0.028 ms/call), so the export tree building is *not* where the time goes — worth knowing,
because that was the other plausible suspect.

Projected at the established ~200/call chunk: **2.4-3.5×** (A: 5,548 → ~2,275 ms, 20,357 round-trips
→ 102).

**Treat that as an upper bound.** It assumes batching removes IPC proportionally and adds nothing,
whereas real batching serialises a larger payload and parses a bigger document — some of which
reappears in `dll` and `ui`. `ui` in run B hit the zero floor (transport ≥ wall), so it is
"negligible, at or below the measurement floor", not precisely quantified. And `dll` at ~0.08 ms/call
is untouched by batching: run A cannot go below its 1,689 ms of actual walking.

**This settles multipipe Phase 1 harder than the first measurement did.** Phase 1 targets the `dll`
share (27-30%); the cost is `ipc` (59-73%). It would have been aimed at the smaller half of the
wrong problem — and it had already been built and reverted once on that premise.

Recommendation recorded in [multipipe-eval.md](multipipe-eval.md) §10.4 and [todo.md](todo.md):
**batch `walk_instance`**, following the `walk_class_batch` / `search_properties_batch` precedent
*including* their three-layer equivalence safety net, because a silently dropped field in a CE
export is invisible until someone needs it months later.

-----

## 2026-07-23 — Decompose the per-call overhead: dll / ipc / ui (build 2327; dev, UI-only)

Build 2324 proved the dispatcher is not the bottleneck and pointed at `walk_instance`'s **20,357
round-trips per export** instead — 0.088 ms of DLL work carrying 0.208 ms of overhead. But it could
not say how much of that 0.208 ms **batching would actually recover**: pipe latency vanishes when
200 calls become one, UI-side per-result work does not. Promising a speed-up on that basis would
have been guessing.

**One new measurement closes the gap.** `PipeTransportStats` accumulates time spent inside
`PipeClient.SendAsync` (write → response). Combined with the two figures already collected, every
part of an operation is now accounted for:

| part | derivation | does batching remove it? |
|---|---|---|
| `dll` | Sense dispatcher busy | no — it is the actual work |
| `ipc` | transport − dll | **yes** — the round-trip itself |
| `ui`  | wall − transport | no — deserialise + per-result caller work |

The PERF line gained both the totals and the per-call breakdown, the latter at microsecond
resolution because the whole decision turns on sub-millisecond figures that `N1` would round away:

```
PERF Copy CE XML: wall 5,362.7 ms · dispatcher busy 1,651.3 ms (30.8%) · 20357 dispatches
   · split dll 1,651.3 / ipc 2,348.7 / ui 1,362.7 ms
   · (per call: dll 0.081 / ipc 0.115 / ui 0.067 ms)
```

Details worth keeping:
- **Transport is timed in a `finally`**, so a cancelled or faulted request still counts. Dropping
  those would flatter the IPC figure exactly when the pipe is misbehaving.
- **The probe subtracts its own two round-trips** from the call count, the same reasoning that
  already excludes its `get_diagnostics` from the busy total.
- **Monotonic snapshots, differenced** — never a reset — so two overlapping probes cannot clobber
  each other's baseline.
- **Every derived figure is floored at zero.** Concurrency or clock skew can make transport look
  smaller than the DLL time it contains, and a negative "ipc" in a log is worse than a wrong one.
- **Stated caveat:** transport is summed per call, so with both lanes sending concurrently the sum
  can exceed wall-clock. The heavy exports this measures are sequential, but the number is not
  exclusive time and must not be read as such.

Cost is one `Stopwatch.GetTimestamp` pair and two interlocked adds per pipe call.

**Verification:** 2900 tests green (+4), including the split arithmetic shaped on the real Copy CE
XML numbers, the omit-when-unsampled path, and the negative-guard. **Not verified: real split
figures** — that needs another live export, and is the point of the change.

-----

## 2026-07-23 — MEASURED: the dispatcher is not the bottleneck; Phase 1 is WON'T-DO (build 2324; docs)

The question this whole diagnostics chain was built to answer, answered. `multipipe-eval.md`'s core
claim — that DLL-side serial-dispatch head-of-line blocking is what makes the UI lag — was reasoned
in 2026-06 and never measured. It is now, on two games spanning both engine generations: **Elliot
(UE 5.4)** and **SEED BATTLE DESTINY REMASTERED (UE 4.27)**, 24,178 dispatches across five real
Copy CE XML / Copy CE Field runs.

**Dispatcher busy: 29.8% aggregate**, and remarkably stable — 22-31% across operations spanning
2.6 ms to 5.4 s. **Worst single dispatch out of 24,178: 14.3 ms.**

**Verdict: do not build Phase 1.** Three independent readings agree. The dispatcher is idle ~70% of
wall-clock, so non-blocking dispatch can only recover a slice of the busy 30% — and only if
something were queued behind it, which in a single-user export there is not. There is no
head-of-line spike to remove: nothing holds the read loop for more than a frame. And Phase 1 is
expensive — it was shipped and reverted once already (build 1840) and a correct version needs
overlapped/async pipe I/O.

**What the data says the real lever is: call count.** `walk_instance` is 100% of the dispatcher cost
in every single row, and one Copy CE XML issued **20,357** of them. Per round-trip: **0.088 ms
inside the DLL, 0.208 ms everywhere else** — pipe latency, JSON envelope, UI-side deserialise. **2.4x
the actual work is overhead.** Batching at the established ~200/call chunk (the pattern
`search_properties_batch` and `walk_class_batch` already use) would collapse 24,178 round-trips to
~121.

**Stated limit on that estimate:** this data cannot decompose the 0.208 ms into pipe latency (which
batching removes) and UI-side per-result work (which it does not). The trend is suggestive —
per-call overhead falls from 0.427 ms at 386 calls to 0.182 ms at 20,357 — but the split must be
measured before promising a number. Recorded as a candidate with that caveat rather than as a plan.

Written up in [multipipe-eval.md](multipipe-eval.md) §10 with the full table; Phase 1's status in
that document changes from "phased recommendation" to **WON'T-DO**, revisit only if a workload
appears whose *single* dispatches block for hundreds of ms.

**Also confirmed this run:** the winmm proxy on a second game — SEED is **UE 4.27**, so the proxy is
now verified across both engine generations, 180/180 exports forwarded on each.

-----

## 2026-07-23 — winmm proxy LIVE-VERIFIED; the PERF records immediately found two of their own bugs (build 2324; dev)

**winmm proxy works.** First live run, The Adventures of Elliot (UE 5.4):

```
DllMain ProxyStart: proxy DLL mode — starting pipe server only (no scan)
DllMain ProxyStart: pipe server started
[PROXY] winmm proxy: lazily forwarded 180/180 exports to real System32 winmm.dll
UE5_Init: Complete (UE504, GObjects=0x149BFF150, GNames=0x149B1B600, Objects=326364)
```

**180/180 forwarded**, and at T+1.2 s — i.e. lazily, on a game thread after DllMain returned, exactly
as designed rather than under the loader lock. Name sanity 10/10, full offset detection, GWorld
found. The proxy family is now version / dinput8 / dxgi / winmm, all four working.

**And the automatic PERF records earned their keep on their first outing** by exposing two defects in
themselves — which a hand-run measurement session would very likely have shrugged past:

```
PERF Copy CE Field: wall 57.7 ms · dispatcher busy 93 ms (161.2%) · top: walk_instance 0ms/128x max 15ms
```

**161% busy, and the breakdown contradicting the total.** Two independent causes:

1. **`GetTickCount64` was the wrong clock.** Its ~15.6 ms granularity floors every sub-tick dispatch
   to zero, so 128 `walk_instance` calls summed to "0 ms" while one that happened to straddle a tick
   read 15 ms. That is an artefact of tick alignment, not a measurement — and sub-millisecond
   commands are precisely the population this exists to measure. `Sense` now times with
   **QueryPerformanceCounter and accumulates microseconds**, reporting fractional ms on the wire
   (`total_ms` / `max_ms` / `last_ms` became doubles, and the C# model with them).
2. **The probe was measuring itself.** `busy` came from the global `total_busy_ms` delta, which
   includes the probe's own opening `get_diagnostics` (~93 ms) — while the per-command ranking
   already excluded it. Hence 93 ms of "busy" against a 57.7 ms operation whose top row showed 0 ms.
   Busy is now **summed from the per-command deltas**, so the percentage and the breakdown agree by
   construction rather than by coincidence.

Both are pinned by regression tests carrying the real numbers from the log. Note that a genuine
>100% remains possible and meaningful — the two-connection lane split can have two dispatchers busy
at once — so the figure is deliberately not capped.

**Verification:** all 4 proxies + main DLL build clean; 2896 tests green (+3). **Still to do: re-read
the PERF lines with the fixed clock** — the pre-fix numbers above understate sub-ms commands and
overstate short operations, so the multipipe Phase 1 decision should wait for fresh ones.

-----

## 2026-07-23 — Automatic PERF records around every heavy operation (build 2320; dev, UI-only)

The user's idea, and better than the manual measurement session it replaces: a deliberate test run
only ever covers the scenario somebody thought to try, and only if they remembered to reset the
counters first. Recording every real **Copy CE XML / Copy CE Field / Value Scan (First & Next) /
Snapshot capture** instead means the evidence for the multipipe Phase 1 decision accumulates from
actual use — including the combinations nobody would think to test.

New `Services/DiagnosticsProbe.cs` brackets each operation with two `get_diagnostics` snapshots and
writes one `PERF` line to the `view` log:

```
PERF Value Scan (First): wall 2,340.0 ms · dispatcher busy 1,980 ms (84.6%) · 7 dispatches
   · top: value_scan_begin 1900ms/1x max 1900ms, get_object_list 80ms/6x max 32ms
```

Design decisions that make the line trustworthy:
- **Deltas, not absolutes.** Absolute totals answer "what has this session done"; the question is
  what *this* operation cost. Wall-clock is measured locally rather than from the DLL's uptime, so a
  `reset_diagnostics` landing mid-operation cannot produce a negative duration — every figure is
  floored at zero and there is a test that fires a mid-operation reset.
- **The probe excludes its own calls.** The opening snapshot is itself a dispatch that lands in the
  closing one; without the filter every measurement would list the measurement.
- **`await using`**, so the closing sample happens even when the operation throws or is cancelled.
- **Never affects the operation.** No connection, an older DLL that doesn't know the command, a
  mid-operation disconnect — all swallowed, and `BeginAsync` returns a working no-op probe rather
  than null so call sites need no null handling. A diagnostic that breaks what it measures is worse
  than no diagnostic.
- **`MaxMs` is reported, not differenced** — it is a running high-water mark, so a delta would be
  meaningless.

Cost is two pipe round-trips (~0-125 ms each) around operations that run for seconds, so it is on
unconditionally rather than behind a flag.

**Verification:** 2893 tests green (+11), covering the delta arithmetic, the self-call exclusion, the
mid-operation-reset floor, and the zero-length-operation divisor guard. **Not verified: the lines
against a real heavy operation** — that is the point of the feature, and the next thing to look at.

-----

## 2026-07-23 — winmm.dll proxy: the spare slot (build 2317; dev, DLL + UI)

The 4th proxy. **Built on the slot-contention trigger, not the coverage one** — the n=24 census
(build 2313) stands: winmm and dxgi both cover 100% of installed UE games and winmm reaches exactly
zero that dxgi misses. What justifies it is the other half of that finding: **a proxy only works if
its filename is free.** `dxgi.dll` is the name ReShade and many mod loaders take, `version.dll` is
likewise a common ASI/mod-loader name (e.g. Ultimate ASI Loader), and with both gone the only
remaining choice was dinput8 at 2/24. winmm is the spare universally-viable slot, so users now get a real dxgi/winmm
choice.

**Generated, never hand-written.** New `scripts/gen_proxy_forwarders.py` reads the export table of
the real System32 DLL and emits all three artefacts in the shapes the dxgi proxy already uses —
`Lugner_Winmm.cpp` (the `mProcs[]` table + lazy System32 resolver), `Lugner_Winmm.asm` (180 MASM
lazy jmp-thunks), `ProxyWinmm.def` (`name = fN @ordinal` + our C ABI). At 180 exports hand-editing
was never an option; `--check` verifies the checked-in files are current. The generator carries the
two hard-won constraints in its header: jmp-thunks rather than C forwarders (a bare `jmp` forwards
ANY signature, which matters because the export table holds undocumented internals), and LAZY
resolution rather than eager DllMain (eager resolution crashed Octopath Traveler through the dxgi
proxy by running LoadLibrary under the loader lock).

**Verified against the real DLL rather than by inspection:** 180/180 forwarding exports present,
**every ordinal matching System32 winmm exactly**, zero missing, plus the 60-symbol UE5 ABI
including `g_invokeMailbox`. One ordinal-only export (@2, an internal) is skipped and reported by
the generator — a game importing winmm by ordinal would miss it; none does. And the proxy does
**not import winmm itself**, which is only possible because build 2301 moved Mimic's
`timeBeginPeriod` off a static import.

**Two more hardcoded proxy lists found and removed** while wiring this up — the same desync class
that had left the double-inject guards blind to dinput8/dxgi. `DumperModuleDetector.ProxyNames` and
`WindowsPlatformService`'s module filter both carried literal `{version, dinput8, dxgi}`; both now
derive from `ProxyType` through one `IsInterestingModuleName` helper. New `ProxyTypeCoverageTests`
walks every enum value through `GetDllName` / `GetDisplayName` / `FromDllName` / the module filter,
so a fifth flavour cannot be half-added again.

**Also in this build:** the Diagnostics card's process line now reads *"Game process (not the DLL)"*.
The figures are the whole game's — we are injected into it and there is no supported way to
attribute a working set to one module — and an unlabelled "7,453 MiB" next to our own diagnostics
read as ours.

**Verification:** all 4 proxies + main DLL build clean; 2882 tests green (+7). **Not verified: the
winmm proxy loading a real game** — that needs a deploy-and-launch and is the obvious next step.

-----

## 2026-07-23 — Diagnostics card: auto-refresh toggle + resizable columns (build 2315; dev, UI-only)

Two things the first live run made obvious.

**Auto-refresh (5 s), off by default.** The interval is deliberately unhurried, and the reason is
specific to this card: **every poll is itself a dispatch**, so a fast timer would inflate the very
numbers being reported — `get_diagnostics` already appears in its own table (8.6% of busy time on
the user's first run, from a single call). 5 s stays in the noise while making CPU% meaningful, since
that needs two samples to difference.

Three guards, all for the same reason — the measurement must not perturb what it measures:
- **Pauses on tab-leave, resumes on tab-enter** (`OnLeavingTab` / `OnEnteringTab`, wired the same way
  Live Funcs auto-stops its recording). A forgotten toggle would otherwise keep adding pipe traffic
  while the user works elsewhere. The checkbox stays ticked.
- **Never stacks requests** — a tick is skipped while one is in flight. The first snapshot measured
  125 ms; queuing polls behind each other would turn a timer into a burst.
- Toggling on fires one refresh immediately rather than making the user wait a full interval.

**Resizable columns.** The numeric columns had been sized to their content and were clipping their
own headers ("Cou", "% bu") with no way to widen them — `CanUserResizeColumns` was never set. Now
explicit, with `MinWidth` so a dragged column can't collapse. **Sorting is explicitly OFF**: Avalonia's
DataGrid sort is reflection-based (an AOT hazard — see `ui-avalonia12-pinvoke-gotchas`), the rows
already arrive ranked by total time, and switching it off reclaims the header space the sort glyph
was reserving, which is part of why the headers fit now.

One self-inflicted compile break worth noting: adding `vm.Pointers?.OnLeavingTab()` made the
compiler treat `vm.Pointers` as nullable from that point on, breaking a pre-existing non-null
dereference three lines later. `Pointers` is a non-nullable property; the `?.` was wrong, not the
old code.

-----

## 2026-07-23 — Diagnostics fix: a UINT64_MAX sentinel on the wire blanked the whole card (build 2311; dev, DLL + UI)

First live run of the new Diagnostics card failed outright with *"An element of type 'Number'
cannot be converted to a 'System.Int64'"*.

**Root cause.** `Stark::MsSinceLastHookFire()` returns `UINT64_MAX` for "the PE hook never fired —
liveness unknown", and build 2308 put that straight on the pipe. 18446744073709551615 does not fit
an `Int64`, so `GetValue<long>()` threw and took the entire panel with it. **"Never fired" is the
NORMAL state on a fresh connection** — the hook installs lazily on the first invoke — so this was
the default path, not an edge case.

**Why it took longer than it should have:** `System.Text.Json` emits the *identical* message for
out-of-range and for fractional values, and names neither. That sends you hunting for a decimal
point. The raw payload settled it in one grep — the UI's own pipe log had
`"ms_since_last_fire":18446744073709551615` sitting there the whole time. Recorded in
[lessons-learned.md](lessons-learned.md): grab the payload before theorising.

**Two fixes, because either alone would be insufficient.**
- **Wire boundary (DLL).** The sentinel is now mapped to `-1` before serialising. An in-process
  `UINT64_MAX` convention is fine; the wire is a narrower type system and sentinels must land in the
  range the other side can parse.
- **Reader (UI).** New `Services/JsonNum.cs` — saturating, non-throwing `L/I/D/B` reads, now used
  throughout the diagnostics parse. **Telemetry must degrade, never throw:** one odd field is worth
  a wrong number in one cell, not a blank panel the user opened to debug something else. `JsonNum.D`
  also collapses non-finite values, since a `NaN` reaching a format string prints "NaN%" at the user.
  A pre-fix DLL still works — `UINT64_MAX` saturates to `long.MaxValue`, which `HasFired` reads as
  "unknown" rather than as a plausible age.

**UI:** the card now prints *"never fired (hook installs on first invoke)"* instead of a nonsense
age. **Tests:** +17, including the verbatim failing payload from the log and both causes of that
ambiguous STJ message pinned separately. 2875 green.

-----

## 2026-07-23 — Diagnostics (`Sense`): measure what the pipe traffic actually costs (build 2308; dev, DLL + UI)

Tier 1 + Tier 2 of the performance-counter evaluation. Exists for one reason:
[multipipe-eval.md](multipipe-eval.md) names DLL-side **serial-dispatch head-of-line blocking** as
the root cause of UI lag and game-thread CPU starvation as the CE-mailbox risk — and **nothing
measured either**, so "should Phase 1 (non-blocking dispatch) be built?" was a blind decision. Now it
isn't.

**New `Sense` module** (Frieren roster: Second-Exam proctor, "scythe" — the roster's own suggested
use for that name was *harvest-collection*). Records per-command dispatch cost — count / total / max
/ last — plus Win32 process facts and game-thread health. New pipe commands `get_diagnostics` /
`reset_diagnostics`, both pipe-only.

**Where the timing is taken matters.** Fern already brackets `DispatchCommand` with an `inFlight`
flag, documented as the CPU-bound stretch that never touches the pipe. That span *is* the window
during which the connection's dispatcher is unavailable to anything else — so it is exactly the
head-of-line blocking in question, and the measurement needed no new chokepoint.

**The headline number is `busy_percent`** — what fraction of wall-clock a dispatcher was occupied.
High, with a lagging UI, is the case *for* Phase 1; low says the lag is elsewhere and Phase 1 would
not help. The per-command table ranks by **total** rather than max, because the question is which
command *owns* the dispatcher, not which one spiked once — `max_ms` is reported alongside because
that is the spike a user actually feels.

Three deliberate choices:
- **Dedicated mutex.** Borrowing a lock a long scan also holds would make the diagnostics contend
  with the very thing they exist to measure. Cost when idle is a map lookup and a few adds per
  command — noise next to commands that are microseconds at best.
- **CPU% is `-1`, not `0`, until a second sample exists** to difference against. The UI renders that
  as an em dash: "0%" would read as *idle*, a different and wrong claim. Normalised by core count so
  100 means one whole machine, matching what a user sees in Task Manager.
- **Tier 2 is on demand only.** Thread count walks a system-wide `TH32CS_SNAPTHREAD` snapshot (no
  cheaper documented API), so it never runs unless a client asks.

**UI:** System tab → *Diagnostics — DLL dispatch cost*, placed directly above the existing Pipe
Activity card — that one shows *what* crossed the pipe, this shows what it *cost*. Refresh + Reset
counters; the reset re-reads immediately so the card shows a live empty baseline rather than looking
broken. Counters also reset when the last client disconnects, so one session's numbers never
pollute the next.

**Not built, deliberately:** per-worker tick counters for Solide / Hemmung / Laufen / Solitar /
Schlacht. That means touching five modules for a number that does not bear on the dispatch question,
and the dispatch question is what blocks a decision. Recorded in [todo.md](todo.md) as the natural
follow-on.

**Verification:** 2858 tests green (+12). DLL + all 3 proxies build clean. App launched to confirm
the new card and its `DataGrid` bind without error. **Not verified: the numbers themselves against a
live game** — that needs an attached session, and is the point of the feature rather than of the
code.

-----

## 2026-07-23 — Modular UE builds: fold the engine modules into the proxy-import hint (build 2308; dev, UI-only)

Fixes the LOW-severity defect the n=24 proxy census turned up. `ReadProxyImports` was handed
`game.ExePath` only. In a **modular** build the exe is a thin bootstrap — Satisfactory's is 264 KB,
with the engine split across ~182 sibling `*-Win64-Shipping.dll` modules — so the analyzer saw no
dxgi/dinput8 and the Suggested-proxy column claimed `version · default · no dxgi/dinput8` for a game
where a dxgi proxy loads perfectly well (`D3D12RHI` imports it).

A proxy activates if **any** module in the process imports that name — the loader searches the exe's
directory whichever one asks. So when the exe imports none of the three (`ImportsNone`, the
bootstrap-stub signature), the sibling modules are now folded in with `Merge`, a pure OR. The
file-walking half stays in `ProxyDeployService`; `ProxyImportAnalyzer` remains OS-free and
synthetic-PE-testable by design.

**Measured, not assumed:** Satisfactory goes from *nothing* to `version + dxgi` in **30 ms** for all
182 modules (header-only parsing), and a monolithic game is untouched at 0 ms because the fallback
never triggers. The 512 cap is a runaway guard, not a budget — 182 is walked in full, since the
all-three short-circuit cannot fire on a build that imports no dinput8.

Severity was LOW throughout: imports are advisory context the analyzer never lets override the
version default, so the harm was a misleading hint string, not a wrong deployment. +5 tests.

-----

## 2026-07-23 — Stop statically importing winmm: resolve the 1 ms timer from System32 (build 2301; dev, DLL)

Clears the hard prerequisite the winmm-proxy evaluation identified. Correct on its own, and shipped
separately from the proxy so the two can be judged independently.

**The trap.** `Mimic.cpp` raises the timer resolution for the CE-mailbox poll thread
(`timeBeginPeriod(1)` / `timeEndPeriod`), and it lives in `UE5_COMMON_SOURCES` — the object library
linked into the main DLL *and every proxy*, with `Winmm` in both link lists. The day a proxy target
**is** `winmm.dll`, our own static import of `winmm.dll!timeBeginPeriod` resolves against the module
of that name in the process — **ourselves** — landing in our forwarding stub. Before the stub has
resolved the real export it returns 0, and **0 is `TIMERR_NOERROR`**: no crash, no error, the call
just silently does nothing while `Sleep(1)` degrades to the 15.6 ms tick and mailbox latency gets
~15× worse. Delay-loading would not have helped (a delay-load `LoadLibrary("winmm.dll")` from the
game folder finds us again), and no test would have caught it — `dll_helpers_test` linked `Winmm`
into the test exe, so its latency assert passed regardless of proxy behaviour.

**The fix.** `Mimic.cpp` resolves both functions from the **System32** copy by explicit path
(`GetSystemDirectoryW` → `LoadLibraryW` → `GetProcAddress`); Windows keys loaded modules by full
path, so this yields the genuine OS winmm even with a same-named proxy of ours mapped. `Winmm` is now
absent from `UE5Dumper`'s link list, `PROXY_LINK_LIBS`, **and** `dll_helpers_test`. Unresolvable →
returns a non-zero rc so the existing "log and proceed" path runs and the paired `timeEndPeriod` is
skipped; the worst case was always a graceful degradation to system Sleep granularity, never a
correctness break. The helper is deliberately proxy-agnostic (no `UE5_PROXY_*` test), which is what
lets `Mimic.cpp` stay in the shared object library — an `#ifdef` would have violated that invariant
and forced the file out of the compile-once set.

**Verification — objective, not by inspection.** Parsing the built PE import tables shows
`winmm.dll` is gone from `UE5Dumper.dll`, all three proxies, **and** the test exe. The poll-latency
micro-benchmark was reworked to resolve the same way rather than through a linked import, so it now
covers the real mechanism; it measures **1.95 ms/sleep** (194.9 ms for 100 × `Sleep(1)`) — a silent
no-op would have landed near 15.6 ms/sleep. `dll_helpers_test` 845 pass / 0 fail (+4), UI 2846 / 0.

-----

## 2026-07-23 — Undeploy removes every proxy flavour of ours, not just the selected one (build 2299; dev, UI-only)

**Reported bug.** With `dxgi.dll` deployed and the radio switched to `version.dll`, *Undeploy* did
nothing — `UndeployAsync` only ever looked at `proxyType.GetDllName()`. The user was left unable to
remove the proxy at all, while the grid cheerfully reported `DeployedOtherType` at them (the
*detection* side has handled all flavours since build 2134 via `deployedProxyNames`; only the removal
was type-scoped).

**Fix: undeploy is type-agnostic.** The radio governs what to *deploy*; undeploy is a clean-up, so it
now sweeps every flavour we ship. `UndeployAsync` lost its `ProxyType` parameter entirely rather than
keeping a misleading one. It still only deletes files that are **ours** (`IsOurProxyDll` →
`FileVersionInfo.ProductName`); a foreign `version.dll`/`dxgi.dll` (mod loader, another tool) is left
alone and named in the message.

Three decisions worth keeping:
- **Per-file try/catch.** One locked DLL must not abandon the rest — removing what we can is the
  point, and the locked one is reported by name.
- **Refusing a foreign DLL is only a FAILURE when we removed nothing of ours.** Otherwise it's a note
  on an otherwise successful clean-up (`NotDeployed` + "Left another program's version.dll").
  A locked file outranks both, since it's the actionable one.
- **The policy is pure and separately testable.** `PlanUndeploy` (which files) and
  `ResolveUndeployOutcome` (status/message/success) are static and side-effect free, because
  ownership is decided by a PE version resource — fabricating one in a unit test would test the
  fixture, not the policy. `AllProxyDllNames()` is now shared with the refresh path.

**Verification:** 2846 tests green (+12 pure-policy cases covering the exact reported combination,
the all-three sweep, the foreign-DLL spare, and the locked/foreign precedence). The real
file-touching path was additionally exercised once against the **actual built proxies** in
`dist\proxy\` (real `ProductName`, so `IsOurProxyDll` ran for real): both of ours deleted, a foreign
`version.dll` kept and named. That integration check was not kept as a test — it would depend on
build outputs being present.

-----

## 2026-07-23 — CE autorun helper: every table gets `ue5_inject()`, permanently (build 2297; dev, UI-only)

The fourth and last delivery route, and the only one needing **neither** the standalone `.CT`
**nor** the AOBMaker plugin. **Tools → Install CE autorun Helper** writes `ue5_autorun.lua` into
`<CheatEngine>\autorun\`, which CE executes at start-up — so `ue5_inject()` / `ue5_shutdown()` then
exist in **every** table, plus a **UE5CEDumper: Inject DLL** entry in CE's main menu. Takes effect on
the next CE start.

**Finding Cheat Engine without new plumbing.** The install directory comes from a *running* CE
process via the existing `GameProcessInfo.Path` (`ListGameProcessesAsync(showAll: true)` — CE isn't a
UE game, so the UE-only filter would hide it), falling back to the save dialog when CE isn't running.
Deliberately not the registry: that would need a new platform-abstraction surface, and a running CE
is both the common case and the authoritative answer for *which* install of several is in play.

**The early-startup API risk is designed out, not tested away.** Autorun runs before any process is
attached, so the file **only defines things at load time** — every process-dependent call sits inside
a function the user invokes later. A unit test enforces it by parsing top-level statements and
rejecting `injectDLL` / `getOpenedProcessID` / `readInteger` / `executeCodeEx` / `showMessage` there.
The one genuinely uncertain call, `getMainForm().Menu`, is `pcall`-wrapped: if the form isn't ready
the menu is simply absent and `ue5_inject()` still works from the Lua console — a cosmetic extra must
never break someone's CE start-up. The menu API shape (`createMenuItem` / `parent.add` / `.Caption` /
`.OnClick`) is copied from the verified precedent in `vendor/UE4 Dumper.CT` rather than invented, per
the CE-API rule. A `ue5_menuAdded` global makes a manual re-run idempotent.

**Shared readiness emitter.** With two generators plus the `.CT` all needing the same
"wait until the DLL is actually up" loop, it now lives in one place —
`Services/CeReadinessLua.cs` — so the offsets, timeouts, and the two properties that matter
(pure memory read, never `executeCodeEx`; symbol resolved *inside* the loop) cannot drift.
`CeInjectScriptGenerator` was refactored onto it; the three failure messages are shared too, so both
routes give the same diagnosis for the same state.

**Route ranking updated to four** across the Proxy Deploy panel line and the Deploy / Inject /
bootstrap / autorun tooltips, and a new **"Getting `UE5Dumper.dll` into the game — which of the four
routes?"** recipe leads [tips.md](tips.md).

**Verification:** 2834 tests green (+12). The generated Lua was parsed with a real Lua parser (whole
file for the autorun helper, per-`{$lua}`-block for the record) — the shape assertions alone would
not catch a syntax error. **LIVE-VERIFIED 2026-07-23** — Cheat Engine picks the file up at start-up
and the route works end to end, which also settles the early-startup API question the evaluation
flagged: `getMainForm().Menu` is reachable from `autorun\`.

-----

## 2026-07-23 — Push the "Inject DLL" record into the CE table you already have open (build 2295; dev, UI-only)

Kills the two-stage table load. Cheat Engine holds **one table at a time**, so using the standalone
`scripts/UE5CEDumper.CT` meant: open ours → inject → open the game's own table → the injection entry
is gone. New **Tools → "Add \"Inject DLL\" Record to Current CE Table"** generates the same bootstrap
as an `[ENABLE]`/`[DISABLE]` memory record and pushes it into whatever table CE currently has open,
via the AOBMaker plugin's existing `CreateAAScript` (grouped under `UE5CEDumper (DLL)` so it doesn't
litter the user's root). **The standalone `.CT` is unchanged and still shipped** — it stays the
developer / no-AOBMaker path.

**Zero new plumbing.** `CreateAAScript` already wrote into the open address list (Teleport and
LiveWalker invoke have used it for builds); the bootstrap simply had no generator behind it. New
`Services/CeInjectScriptGenerator.cs` + one Tools command; no DLL, pipe, or CE-plugin change.

Carried over from the build-2291 `.CT` work, so both routes behave identically: **polls the DLL's
mailbox `initState` instead of sleeping a fixed budget** (pure memory read via `g_invokeMailbox`,
never `executeCodeEx` — games block `CreateRemoteThread` during start-up), resolves the symbol
**inside** the poll loop (CE's symbol handler may not see the fresh module on the first try), and
treats a timeout as a real error rather than printing "probably fine". `[DISABLE]` may use
`executeCodeEx` because by then the game is running normally.

Improvements over the `.CT` version:
- **The DLL path is baked in.** The UI already knows where `dist\UE5Dumper.dll` is (same resolution
  as `ProxyDeployViewModel.InjectIntoRunningGameAsync`), so there is no run-time directory search,
  and a missing DLL is reported by the UI *before* generating rather than failing inside CE.
- **`[DISABLE]` is a quiet no-op when nothing was ever loaded.** `[ENABLE]`'s early bail-outs set
  `memrec.Active = false`, which makes CE run `[DISABLE]` against a DLL that never loaded; it now
  probes for `UE5_StopPipeServer` first and stays silent instead of reporting a false failure.
- Falls back to CE record XML on the clipboard when AOBMaker isn't reachable, distinguishing "pipe
  broke mid-send" from "CE was never running".

**Route guidance (build 2296).** Three delivery routes now exist and they are not equally good, so
the ordering is stated where each choice is made: an always-visible line in the Proxy Deploy panel
(`str.ProxyDeploy.RouteOrder`) ranks **① deploy a proxy DLL** (loads with the game, survives
restarts, no CE at all) → **② inject into a running game, or push the bootstrap record into your open
CE table** → **③ the standalone `dist\UE5CEDumper.CT`** (developer fallback, and the only route
needing no AOBMaker plugin); the Deploy / Inject / Tools-bootstrap tooltips each name their own rank
so the ordering stays consistent. In-panel rather than tooltip-only on purpose — that panel is where
the decision happens, and a tooltip is only found by someone who already knew to look.

**Verification:** 2822 tests green (+17). **LIVE-VERIFIED 2026-07-23** — the pushed
*UE5CEDumper: Inject DLL + Start Pipe Server* record was ticked in a real CE table and injected +
came up correctly. `CeMailboxLayout` gained `OffInitState` + the five
`InitState` values so the offsets stay single-sourced. The emitted Lua was additionally checked by
running both `{$lua}` blocks through a real Lua parser — the shape assertions alone would not have
caught a syntax error. For the route-guidance strings the tests prove nothing (no string-resource
coverage test exists), so those were checked by confirming every key resolves, then launching the
app: `ProxyDeployPanel` is instantiated directly by `MainWindow.axaml`, not lazily, so a clean start
with an error-free init log means the new `StaticResource` resolved. The wrapped layout itself was
not visually inspected.

Two test-only traps worth remembering: `Assert.DoesNotContain("\0", s)` **always fails** — the string
overload is culture-sensitive and under ICU a NUL has zero collation weight, so it "matches" at
position 0 of any string (use the `char` overload, which is ordinal); and asserting
`DoesNotContain("executeCodeEx(")` misses `pcall(executeCodeEx, ...)`, so the check now strips Lua
comment lines and asserts on the bare identifier against code only.

-----

## 2026-07-23 — CE `.CT` inject: poll for readiness instead of sleeping 15 s; double-inject guard learns dinput8/dxgi (build 2291; dev)

Two small fixes to the Cheat-Engine injection path, both from the 2026-07-23 evaluation batch
in [todo.md](todo.md). **LIVE-VERIFIED 2026-07-23** — the `.CT` route was run against a real game
(CE Lua is not unit-testable, so this was the only way to confirm it). The `Methode.cpp` half of the
double-inject guard is only reachable via CE's *Inject && Connect* plugin menu item and has not been
exercised.

**1. The 15 s blind wait is now a 250 ms poll.** `scripts/UE5CEDumper.CT` `ue5_inject()` used to
`sleep(1000)` fifteen times and then print "complete (or failed — check DLL log)" **without ever
checking anything**: a normal run (its own comment budgets "1 s thread delay + ~2-8 s AOB scan")
wasted 5-10 s, and a *failed* run still reported success.

The readiness signal is a new `Mimic::InitState` published into the mailbox
(`IDLE`/`RUNNING`/`READY`/`FAILED`/`SKIPPED`), written by `UE5_AutoStart` (`Frieren.cpp`) and both
`AutoStartThreadProc` flavours (`Heiter.cpp`, proxy + CE-inject). CE Lua reads it with
`getAddress("g_invokeMailbox")` + `readInteger` — **a pure memory read**, deliberately not
`executeCodeEx`, because the script's own step-1 comment says `CreateRemoteThread` is avoided here
(games block it). Timeout raised to 25 s but only reached when genuinely wedged; a timeout is now an
**error** (`showMessage` + `return`), as is `FAILED`. `SKIPPED` (another instance owns the pipe, or
we are the CE plugin host) proceeds — a pipe server *is* up.

Three details worth keeping:
- **`initState` reuses the former `reserved` alignment slot** at `MailboxData+0x0C` (same type, same
  offset) ⇒ struct layout unchanged, so no proxy `.def` needed a new `DATA` entry and the UI's
  mailbox offsets are untouched.
- **The symbol is resolved inside the poll loop, not once up-front.** CE's symbol handler may not
  have picked up the just-injected module yet; a single failed `getAddress` would have silently
  dropped back to the blind wait and lost the entire benefit. A 5 s grace period, then the old
  fixed wait as fallback for pre-`initState` DLL builds.
- **`READY`/`FAILED` are published only after `UE5_StartPipeServer` returns**, so a poller that
  observes `READY` can connect immediately. `UE5_Shutdown` resets to `IDLE` (load-bearing for the
  path where `Mimic::StopThread` early-returns and its whole-struct `memset` never runs).

**2. The double-inject guard only knew the *old* proxy pair.** `Methode.cpp`
`IsAlreadyLoadedInTarget` and the `.CT`'s `ue5_isAlreadyLoaded` both tested `version.dll` /
`winmm.dll` — **neither checked `dinput8.dll` or `dxgi.dll`, the two proxies we actually ship**
(`winmm` was aspirational; no such proxy exists). A user running the dxgi or dinput8 proxy got no
guard at all and could double-map. Both sites now drive off a named list (`kProxyDllNames` /
`UE5_PROXY_DLL_NAMES`) carrying all three real flavours, with cross-references so a future 4th
flavour can't desync them.

**Verification:** DLL + all 3 proxies build clean; `dll_helpers_test` 841 pass / 0 fail; UI suite
2805 pass / 0 fail. The `.CT`'s embedded Lua was checked by parsing the table as XML and running
every `{$lua}` block through a real Lua parser — which also caught that the new `<` / `>=` operators
needed XML-escaping (`&lt;` / `&gt;=`), since this table stores Lua as escaped text, not CDATA
(precedent: the pre-existing `2&gt;nul` shell redirect at `UE5CEDumper.CT:110`).

-----

## 2026-07-23 — Teleport Coordinate Library: unlimited labelled positions, CSV + CE-Lua round trip (builds 2257-2267; dev, UI-only)

**P1-P5 all shipped, 2777 tests green, ZERO DLL/pipe change.** An unlimited, labelled +
grouped, filterable list of positions persisted per game, with pick→confirm→teleport, CSV
export/import, and a CE-Lua picker in both needs-DLL and no-DLL flavours. The 3 DLL marker
slots are untouched and stay what they were (DLL-side, hotkey-driven); this is a separate
curated UI-side list. Teleport reuses the existing explicit-coordinate path — `teleport_recall_marker`
with x/y/z (`DumpService.cs:3107`) and mailbox `CMD_TELEPORT` op 13 — so nothing DLL-side moved.

Design contract: [teleport-coord-library-spec.md](teleport-coord-library-spec.md).

**P1 (2257)** — `CoordEntry` + `CoordinateLibraryStore` + a collapsed-by-default Expander card
below "Teleport to Coordinates". Three deliberate deviations from the `BookmarkStore` pattern
it otherwise clones: keyed by **exe module name, not PE hash** (bookmarks hold offsets and
*should* die on a game patch; a hand-curated 4 000-entry list must not); the JSON context omits
`WhenWritingDefault` so a legitimately-saved `0.0` coordinate is written rather than reloading as
0 *by accident*; and it keeps a rolling `.bak` that `Load` falls back to on a corrupt main file.
Entries carry an opaque **`uid`, never `id`** — AOBMaker's `CtIdRenumberService` classifies a
script as an ID-check script on `RxIdField` alone and would silently renumber `id = N` literals in
any `.CT` the user later renumbers.

**Precision (D4)** resolves two reviewers' opposite recommendations: round to **3 dp at capture**,
then format shortest-round-trippable. The stored double is then the nearest double to a 3-decimal
literal, so the text is a clean `67162.398` **and** the round trip is bit-exact and idempotent —
neither the lossy `0.0###`/`0.000` helpers nor a bare `"R"` on an unrounded double achieves both.
It also denoises rotator values like `1.4210855e-14` to a clean 0.

**P2 (2260)** — CSV, scheduled *before* the Lua export because it is what users actually curate
4 000 rows in, and its import-report machinery is reused by P4. The repo had **no** CSV reader or
writer, so every rule is stated: RFC 4180 quoting (one unquoted comma in a label shifts every later
column); split positionally with **no `RemoveEmptyEntries`** (the obvious template, `BugItGoParser`,
uses it and would collapse an empty Group from 10 fields to 9, shifting Map→Group and X→Map);
**UTF-8 WITH BOM**, a documented exception to the house BOM-less rule because a BOM-less CJK export
opens as mojibake in Excel on a zh-TW box; delimiter sniffing with comma-decimals when it is `;`;
and formula-injection armouring (`=Boss Arena` displays `#NAME?` and Excel saves the *displayed*
text, destroying the label with no error).

**Import is two-stage and that is the point.** Excel silently coerces `1-2` to a date and `0012` to
`12` and writes back the displayed text, producing perfectly valid CSV no validator can reject. So
stage 1 parses with per-line diagnostics and a **cell-level diff** against the current library;
stage 2 commits after an explicit Apply, writing a `.preimport.bak` first. Merge identity is
uid-first then `(Label, Group, Map)` case-insensitively, **never** the coordinates — a spreadsheet
rewrites coordinate text, so coordinate matching would fail on every row and turn a 4 000-row merge
into 8 000. Export writes the **model, never the view**.

**P3 (2263)** — `-- @UE5CD:COORDS v1` … `-- @UE5CD:END`, named-field records, plus AOBMaker's
`---- GENERATED CODE (do not edit below) ----` separator verbatim. Shape adapted from
`@AOBMAKER:AA_TOGGLE v1` but deliberately in **our** namespace: AOBMaker's end marker is the
feature-less shared `-- @AOBMAKER:END` matched by unanchored `IndexOf`, so a block of ours pasted
into an AA Toggle script would make AOBMaker slice from its own start marker to our END, parse zero
entries, and **silently untick every record in the tree**. Escaping moved into one
`CeLuaHygiene.EscapeLuaString` rather than adding a fifth private copy. The picker is built only
from CE controls verified in the wild (`CrimsonDesert.CT` CheatEntry 357) — **`createListBox` and
`createComboBox` appear nowhere in this repo and were not used**. Confirm-before-teleport is two
buttons (no CE yes/no API is verified); interactive, so the untick lives on `OnClose`, never the
momentary auto-close.

**P4 (2265)** — brace-balanced re-import, tolerating reordered fields, missing commas, entries split
over lines, inserted comments and unknown keys from a newer build. Four AOBMaker parser defects
deliberately **not** inherited (each confirmed by running its shipped assembly): version baked into
the marker literal so a v2 block reads as "no block"; unanchored marker matching that hits inside
string literals; **100 % silent failure**; and single-quoted values silently ignored. The `.CT`
clipboard form decodes all **five** entities `CheatTableBuilder.EscapeXml` emits — `ExtractAssemblerScript`
reverses only three — but only when the paste really is XML.

**P5 (2267)** — the no-DLL flavour, added by threading a `Flavour` through the same generator. Both
flavours emit the **byte-identical** data block (asserted) so the round trip cannot drift. It is
honest about what it gives up: no map guard (the map name is only readable through the DLL), the
existing weak-raw-write caveat, staleness on a game patch, and a runtime `UE5T_ready` guard.

**Three bugs caught by the tests as they were written**, each a silent-corruption class:
CSV `Write` did not round, so an entry built by any path other than a pose capture broke
`Write → Parse → Write`; the Lua parser treated a key *present with a non-numeric value*
(`x=oops`) as absent and imported it as 0 with no diagnostic; and the first draft of the picker
hand-wrote mailbox offsets and got all but one wrong (they now come from `CeMailboxLayout`, with a
test).

**Experimental-gated (build 2269, user call).** The card carries
`IsVisible="{Binding ExperimentalEnabled}"` like the other five Teleport cards. The design draft
had argued it should *not* be gated ("a coordinate bookmark list is not combat-affecting") — too
narrow, since it writes the pawn position live and emits CE scripts that do the same. Gating also
fixes the quick-jump menu for free: the code-behind already skips a card that is not
`IsEffectivelyVisible`. Two lifecycle consequences fell out, both fixed: an un-applied import
preview is cancelled when the gate goes off (it would otherwise sit behind a hidden card where the
user can neither see nor cancel it), and — **a pre-existing bug the gating work surfaced** — it is
also dropped when the active game changes, since the diff was computed against the *previous*
game's library and applying it would have written those rows into the new game's file.

**NOT yet verified in-game.** Nothing here has executed a line of the emitted Lua, and the teleport
itself needs a live game. The CSV path has not met a real spreadsheet.

-----

## 2026-07-22 — CE-Lua escaping: closing long brackets of any level could break the AOBMaker push (build 2256; dev, UI-only)

**Three latent bugs in the script emitters, all one root cause.** AOBMaker's CE plugin wraps
the **entire** submitted script in a Lua long bracket at a **hardcoded** level —
`mr.Script = [==[ … ]==]` (`AOBMaker/plugins/CEPlugin/src/pipe_server.cpp:857`) — and does
**not** escape the script body (only `description`/`group` go through `EscapeLuaString`). Its
`InjectTableFile` sibling is safe because it calls `PickLongBracketLevel` to pick a
non-colliding level; `HandleCreateAAScript` does not. So the byte sequence `]==]` must not
appear **anywhere** in an emitted script, in **any** Lua context — including inside a quoted
string, where it is harmless to Lua itself.

1. **`BakedScriptGenerator.EscapeLuaComment`** neutralised `]]` only. `]=]`, `]==]`, `]===]`…
   passed straight through. Now scans for `]` + `=`* + `]` and pads after the leading `]`,
   breaking a closing bracket of any level.
2. **Pre-existing, found while fixing #1:** a trailing `]` in the escaped text fused with
   `MarkUnparsed`'s own `]]` into `]]]`, closing the comment **one character early** and
   leaving `] 0` as dangling syntax — `x]` produced `--[[unparsed:x]]] 0`. The old
   `abc]]def` test never caught it because that input ends in a letter. Now padded.
3. **Found during the audit — `BakedScriptGenerator.EscapeLua`** had the same hole and is
   equally user-reachable: the string-param path (`:467`) emits the invoke param dialog's
   free text as a Lua literal. Padding would corrupt the value, so the leading `]` becomes
   the decimal escape `\093` — same runtime string, different source bytes. 3 digits because
   `\ddd` greedily takes three (a following digit would fuse into `\931` > 255); by
   construction the next char is always `=` or `]`, so that is belt-and-braces.
   `FreezeScriptGenerator.EscapeLua` got the same case to keep its documented
   *"mirrors BakedScriptGenerator's escape rules"* claim true.

Reachability: `MarkUnparsed` (`InvokeParamDialog` → unparseable numeric/pointer/bool param)
and the string-param literal are the **only two** places arbitrary user-typed text enters a
generated script. Both are now covered. Deliberately **not** changed —
`InvokeScriptGenerator.EscapeLua` and `StandaloneTrainerScriptGenerator` (which interpolates
into Lua literals with *no* escaping at all): engine/const-derived inputs today, documented in
`todo.md` to revisit if they ever take free text.

**Verified 2026-07-22: nothing under `ui/` or `scripts/` emitted `]==]`**, so all three were
latent, never active. 8 new tests (adversarial long-bracket inputs at both entry points, plus
a regression guard that ordinary values escape unchanged); 2562 green.

Surfaced by the Teleport Coordinate Library evaluation — see
[teleport-coord-library-spec.md](teleport-coord-library-spec.md) §4, which turns the same
finding into that feature's character-blocking rule.

-----

## 2026-07-22 — Snapshot DB: concurrent first-open could silently DROP a just-captured snapshot (build 2252; dev, UI-only)

**Data-loss fix, found by chasing a CI-only test flake.** `SnapshotStore.OpenAsync` ran
`EnsureSchemaAsync` on **every** open with no mutual exclusion. That method reads
`PRAGMA user_version` and, when it reads low, `DROP`s `snapshots`/`objects`/`fields` before
re-`CREATE`ing them. Two connections opening the same **brand-new** DB both read `0`, so the
slower one could DROP the tables — and the committed rows — the faster one had just written.
No exception is raised on that path: the capture simply disappears.

Reachable in the shipping app, not just tests: `SnapshotViewModel.SetEngineState` ends with a
fire-and-forget `_ = RefreshAsync()` (its own open, on a thread-pool thread) while the user can
press Capture immediately, whose `CreateSnapshotAsync` + `BeginCaptureSessionAsync` open the same
file. Local machines always won the race; a loaded CI runner interleaved.

Three distinct races lived in that window:

1. `PRAGMA journal_mode=WAL` was **first** in the pragma batch — ahead of `busy_timeout=5000` —
   so the pragma that needs a brief exclusive lock ran under the default **0 ms** timeout and
   returned `SQLITE_BUSY` on the spot. `busy_timeout` now goes first.
2. The `user_version` read → DROP → CREATE sequence above (the data-loss one).
3. `AddColumnIfMissingAsync` is read-then-`ALTER`; a tie throws `duplicate column name: is_usable`.

**Fix:** schema init now runs **at most once per (DB path, process)** behind a per-path
`SemaphoreSlim`, with a double-checked `s_schemaReady` memo; only the *first* open of a file pays
the gate, so the pipelined capture's producer/consumer connections still open concurrently. The
memo is invalidated in `DeleteAllSnapshotDatabasesAsync` — a whole-**file** wipe (as opposed to a
row purge) would otherwise leave the memo describing a file that no longer exists, and the next
open would skip `CREATE TABLE` and die on `no such table: snapshots`.

Regression cover: `SnapshotStoreTests.ConcurrentFirstOpens_OnFreshDb_DoNotRace` (12 iterations,
fresh dir each — reproduced the row loss on the first run before the fix) and a re-open assertion
appended to `DeleteAllSnapshotDatabases_RemovesEveryGameFileFromDisk` (verified to fail when the
memo invalidation is removed). The CI-only symptom was
`SnapshotViewModelTests.Capture_StreamsAllChunks_PersistsWithCorrectCounts` failing at
`Assert.True(dump.LastGameOnly)` → `null`; that assert and its two neighbours now carry `Diag()`
too, since the prior `Diag()` sat 8 lines later and printed nothing. Tests 2545 → 2546.

-----

## 2026-07-19 — MindsEye: GNames solved — obfuscated FNameEntry payloads decoded from the fork's own key table (build 2238; dev, DLL needs re-inject)

**LIVE-VERIFIED on MindsEye game version 7.3.1 ONLY** (PE hash `0863E3B90C993000`; the exe carries no
game-version resource, so pin the build by that hash). **Name sanity 10/10** — GNames had never been
found for this title. Every RVA below is build-specific; re-derivation playbook in
[mindseye-fork-notes.md](mindseye-fork-notes.md). Experimental-gated end to end; a title without the
fork's fingerprint runs byte-identical code.

**The format.** MindsEye keeps the STOCK UE5 `FNameEntryHeader` but inserts a field and obfuscates
the payload:

```
+0x00  u16 header   stock: bIsWide:1 | LowercaseProbeHash:5 | Len:10   (len = header >> 6)
+0x02  u16 tag      NON-STOCK — selects this entry's XOR key
+0x04  chars[len]   single-byte XOR (stock puts chars at +0x02), 2-byte aligned
```

`FNamePool` = RVA `0x0BA306C0` (`FRWLock`=0, `CurrentBlock`, `CurrentByteCursor`, `Blocks[]` at
`+0x10`). Block 0 decodes under `0x09` to the canonical EName list — `None`, `ByteProperty`,
`IntProperty`, … — every length matching `header >> 6`.

**The key is per TAG, not per block** — an early hypothesis that cost a build to disprove. Observed:
tag `0x0001`→`0x09`, `0x0002`/`0x0082`→`0x5B`, `0x0003`→`0x1D`, `0x0016`→`0xA3`, `0x0036`→`0xE3`,
`0x0061`→`0xC9`.

**Where the key comes from.** The fork's de-obfuscator (RVA `0x0178B440` ANSI, `0x0178B540` wide)
does `len = header>>6`, `memcpy(dst, entry+4, len)`, then `KeyDerive(ctx, u16 @ entry+2)`
(RVA `0x0178CF50`) and `xor byte ptr [rax], dl` with an SSE `xorps` fast path. `KeyDerive` is an
open-hash probe under `RtlAcquireSRWLockShared`:

```
ctx +0x10 entries   +0x18 count   +0x44 sentinel (== count => empty)
ctx +0x48 inline buckets   +0x50 bucket array (0 => inline)   +0x58 capacity (pow2)
bucket = tag & (capacity-1)
entry = 24 bytes: +0x00 u16 tag | +0x08 u64 (LOW BYTE = key) | +0x10 i32 next (-1 = end)
```

**We read that table directly and NEVER call the routine.** Calling it was designed, adversarially
reviewed and dropped: `KeyDerive` takes the SRW lock *before* probing, so a fault inside it would
unwind out of game frames with the lock still held and permanently wedge every later
`FName::ToString` — no crash, no log, and `Tot` is a cooperative poll with no poll point inside game
code. Its ctx getter also reads `gs:[0x58]` with a lazy per-thread init branch, so calling it from a
thread the game never used is its own hazard. Reading the table has neither failure mode.

**Locating ctx** — all static, no control transfer: `Sig::AOB_NAMEDECRYPT_ME1` (Himmel, new `ME`
source tag) matches the de-obfuscator's semantically anchored prologue (unique in the 145 MB
`.text`; the 16-byte MSVC prologue alone hits 139x), then `match + AOB_NAMEDECRYPT_ME1_CTX_CALL_OFF`
(`0x2F`) `E8 rel32` -> the getter, and the getter's first `48 8D 05` rip-relative LEA -> ctx
(RVA `0x0BA47700`). Live: `decrypt fn=0x7FF60492B440 getter=0x7FF604931050 keyTable=0x7FF60EBE7700`
— all three exactly the RVAs confirmed in CE.

**Changes.**
- `Flamme::IsExperimentalEnabled()` — reads the SAME `%LOCALAPPDATA%\UE5CEDumper\experimental.json`
  the UI's `ExperimentalGate` writes, so the DLL honours the toggle on every entry path (UI pipe
  scan, CE Lua `UE5_Init`, proxy auto-start) with no protocol change. Missing/malformed => OFF.
- `Genau::TryObfuscatedPool` — appended LAST inside the `ValidateGNames` block-offset loop, after
  both stock layouts are rejected for that candidate. Acceptance is the SAME standard as stock:
  entry 0 must decode to exactly `"None"` — found in ONE compare, not 256, since a single-byte XOR
  onto `"None"` exists only if the three inter-byte deltas already match — then >=6 chained
  identifier entries must corroborate. The AOB scan runs only after all of that passes, so an
  ordinary title never scans it. No key table => the pool is REFUSED (decoding block 0 alone is not
  name resolution).
- `Serie::InitObfuscated` — adopts the geometry Genau proved instead of running the stock detectors
  (they all hunt a literal `"None"` in the payload, impossible before decryption), so no stock
  detection path is modified, only bypassed on this one branch. `s_payloadGap` is 0 for every stock
  title, so `strStart` resolves to the same address as before.

**Two bugs found on the way, both mine:**
1. *Heuristic key recovery was wrong.* The first design brute-forced a key per block behind an
   identifier-charset filter. It rejected 135 of 465 blocks: real pools are full of asset paths
   (`/Game/Storm/Animations/...` fails a `[A-Za-z0-9_]` gate) and wide entries aborted the walk.
   Superseded entirely by the table read; every heuristic deleted.
2. *A memory-ordering race produced wrong keys.* The tag->key cache wrote value then flag as two
   plain stores; nothing stops the compiler publishing the flag first, so another thread saw
   "resolved" with a still-zero key and XORed with 0 — `Object` rendered as `Fkclj}`. The same tag
   resolved to `0x09` on one thread and `0x00` on another **in the same millisecond**. Fixed by
   collapsing value+flag into ONE `std::atomic<uint16_t>` (bit 8 resolved, bit 9 miss, bits 0-7 key)
   — a single word cannot tear — plus a decode retry, since the table is LIVE (the game adds names
   as it runs) and a lock-free read can catch it mid-update.

**What is NOT recoverable, and why that is not a defect.** MindsEye ran a symbol-rename pass over
its own non-engine symbols at build time: property and class names are generated 16-character
all-lowercase identifiers (`wcxugjojsqaqvers`, `eurngjogndgrjhls`, ...). Proven, not inferred —
those strings appear verbatim in the exe's `.rdata`, and the binary holds **21,635** distinct 16-char
all-lowercase tokens. Length comes from the header and is key-independent, so a wrong key could
never produce them. Engine symbols (`LocalPlayers`, `NetTimeSyncComponent`, `AnalyticsComponent`, ...)
are untouched and read normally. The original names exist nowhere — not in memory, not in the
binary — so no tool can recover them.

Live Walker now walks `GWorld -> PersistentLevel -> StormWP -> EVMindsEyeGameInstance ->
LocalPlayers -> LocalPlayer -> BP_PlayerController_C` with correct values, classes and outer chains.

> The `GWorld does not deref to a UWorld — recovering...` line in an earlier session log is a
> scan-time timing artifact, not a defect: `GWorld` is a `UWorld**` static slot and `*GWorld` was
> still null while the game was loading. Later runs log no warning and `Start from GWorld` works.

-----

## 2026-07-19 — MindsEye: GObjects solved via preset-bound item layout; GNames located + name obfuscation reverse-engineered (build 2220; dev, DLL needs re-inject)

**LIVE-VERIFIED on MindsEye (Build A Rocket Boy, UE 5.4.4 licensee fork) — game version 7.3.1 ONLY**
(PE hash `0863E3B90C993000`). The first game in the matrix where `GNames=MISSING`, and the first where
the tool **reported `GObjects=OK` on garbage**. Every RVA below is build-specific; see
[mindseye-fork-notes.md](mindseye-fork-notes.md) to re-derive them after a game update.

**What was actually wrong (two independent bugs).** The AOB *did* find the real `GUObjectArray`
(RVA `0x0BB139B0`) and `ValidateGObjects` **rejected** it — the existing `"MindsEye"` preset is written
relative to `FChunkedFixedUObjectArray`, but the AOB resolves the `FUObjectArray` base, `0x10` earlier,
so `num@+0x14` read `NumChunks=9` and failed the `num < 0x1000` gate. The relaxed Tier 2 fallback then
accepted an unrelated heap blob (an ICU-like locale object containing the ASCII text `"International"`)
because its `numOff` landed on the **high half of an adjacent module pointer** — `Num=32758` is literally
`0x7FF6`. Result: `Count=509`, `named=0`, every lookup empty, init reporting `GObjects=OK`.

**Ground truth by offline disassembly** (capstone + the `.pdata` RUNTIME_FUNCTION table — no Ghidra;
`.text` is 145 MB so headless analysis was not worth the hours). `.rdata` still carries the `__FILE__`
anchors (`J:\work\e18f6e32b612e2cd\Engine\Source\Runtime\CoreUObject\Private\UObject\UObjectArray.cpp`),
so xref → containing function → rip-relative globals recovers everything:
`FUObjectArray::AllocateObjectPool` (RVA `0x019B17B0`) pins the five chunked fields, and the
index→object accessors (e.g. RVA `0x0191AA10`: `shr rcx,0x10 / movzx edx,bx / shl rdx,5 /
add rdx,[r9+rcx*8] / cmp qword [rdx+0x10],0`) pin the item layout.

| | value |
|---|---|
| `GUObjectArray` | RVA `0x0BB139B0` (`ObjObjects` at `+0x10`) |
| chunked fields | MaxElements `+0x10`, NumChunks `+0x14`, MaxChunks `+0x20`, NumElements `+0x24`, Objects `+0x28` |
| `FUObjectItem` | **32 bytes**, `UObject*` at **`+0x10`** (stock: 24 / `+0x00`) |
| elements per chunk | 65536 (stock) |

Matches the vendored Dumper-7 `MindsEye` layout exactly (`vendor/Dumper-7/.../ObjectArray.cpp:60`).

**Changes (all additive; the shared detection paths are byte-for-byte unchanged for other titles).**
- `Genau.cpp` — appended `{ 0x28, 0x10, 0x24, 0x20, 0x14, "MindsEye-Extended" }` as the **last** strict
  preset (the `Default → UE5-Extended` relationship already in that table).
- `Aura.cpp` — same row appended **last** in Tier 2 `s_ue4ExtendedPresets`. Cannot steal an existing
  title: `ValidateChunkedLayout` needs `maxChunks ∈ [6, 0x5FF]`, and on a UE5-Extended array this row
  reads `MaxElements` (~2.1M) as `maxChunks`.
- `Aura.cpp` — new **preset-bound `LayoutPreset::itemHint {stride, objOff}`**, consumed by
  `DetectItemSize` *before* the shared sweep and only when the winning preset carries one.
  **Deliberately not another entry in `candidates[]`:** MindsEye's 32/`+0x10` item aliases perfectly
  with stride 16 (every odd 16-byte slot is a real object pointer → `good=100 / bad=100`), so putting
  it in the shared sweep would let it outscore the true stride on genuine stride-16 titles
  (Titan Quest II, Octopath Traveler). Still evidence-gated (`hGood >= 8 && hBad*4 <= hGood`, which the
  50%-aliased result cannot reach); on rejection it logs and falls through to the unchanged sweep.
- `Genau.cpp` — relaxed Tier 2 rows gained a mirrored `maxOff` feeding an **upper-bound-only** check
  (`kRelaxedMaxCeiling = 0x4000000`, 8× the strict cap; skipped entirely if the read fails).
  Deliberately weaker than Tier 1 (no `max < num`, looser ceiling) so a title that raises
  `gc.MaxObjectsInGame`, or whose max field is elsewhere, still reaches the accept path as before.
  Kills the blob by 3.5× (its `max` reads `0x0DE62600` = 233M).
- `Genau.cpp` — GNames diagnostics: `ValidateGNames` now logs the **candidate pool base** (previously
  never logged, so a `chunk0` could not be attributed) plus **raw header bytes** instead of `name` —
  `name` is memset before the log and only ever filled after a length match, so the empty name in every
  historical log was a **logging artifact, not evidence about the game**. Added a 96-byte dump of the
  block itself on its own budget (the 7×2 per-offset probes used to exhaust the shared 10-line throttle).

Rejected after adversarial regression review (3 hunters × 5 rules): a pointer-high-bits reject rule
(would kill The Artisan of Glimmith at 24K objects — the qword covering `numOff` on a real array is
`Max | Num<<32`, which is itself in userspace range), an Aura quality floor, and a DEGRADED-report
trigger keyed on relaxed-tier acceptance (fires on correct resolutions, e.g. Avowed).

**Result:** `Layout 'MindsEye-Extended' detected (strict)` → `FUObjectItem size=32, object-ptr
offset=+0x10 (preset item hint) — 200 total, 0 bad` → `Count=530638, ItemSize=32`. Was
`Count=509, ItemSize=16, bad=100`.

**GNames — located, still unresolved (names ARE obfuscated).** The new block dump identified the pool
immediately: `FNamePool` = RVA `0x0BA306C0` (`FRWLock=0`, `CurrentBlock=507`,
`CurrentByteCursor=0x197D4`, then 64 KB-aligned `Blocks[]` at `+0x10`). The entry format keeps the
**stock UE5 header** but inserts a field and encrypts the payload:

```
+0x00  u16 header   stock: bIsWide:1 | LowercaseProbeHash:5 | Len:10   (len = header >> 6)
+0x02  u16 tag      NON-STOCK — the lookup key for this block's XOR key
+0x04  chars[len]   XOR-obfuscated (stock puts chars at +0x02), 2-byte aligned
```

Block 0 decodes under XOR `0x09` to the canonical hardcoded EName list — `None`, `ByteProperty`,
`IntProperty`, `BoolProperty`, `FloatProperty`, `ObjectProperty` — every length matching `header >> 6`.
The key is **per block**: block 0/1/2/3/6 = `0x09` / `0xE3` / `0x81` / `0xE7` / `0x33`, decoding
`None` / `GameplayTargetDataFilterHandle` / `RigUnit_DebugTransform` / `GetBoneTrackByName` /
`NetConnPacke…`. Encrypted bytes are identical across sessions at different addresses, so the key is
deterministic, not address-derived.

Decrypt routine found: **RVA `0x0178B440`** (ANSI; `0x0178B540` is the wide twin, `add r8,r8`). It does
`len = header>>6`, `memcpy(dst, entry+4, len)`, then `KeyDerive(ctx, u16 @ entry+2)` (RVA `0x0178CF50`)
and a byte-wise `xor byte ptr [rax], dl` with an SSE `xorps` fast path. `KeyDerive` is **not** closed
form — it takes a lock and does a hash-map probe (`bucket = tag & (capacity-1)`) into a runtime table
at ctx RVA `0x0BA47700`. So the key cannot be computed offline; it must be read from the live table or
obtained by calling the game's own routine. Follow-up (Plan A): AOB the decrypt function and route
`Serie` through it for this title.

**Also corrected:** the pak/IoStore container AES (CUE4Parse `MindsEyeAes.cs`) is real but irrelevant —
that is asset-load-time, not process memory. The binary itself is unpacked, has no Denuvo/EAC/BattlEye,
and stock `GWLD_ES2_6` / `SPARSE_ES2_1` still match uniquely.

-----

## 2026-07-15 — Time Dilation: dual-row World + Player levers, held simultaneously (builds 2207 + 2215; MERGED main PRs #442/#443, tag `v2215`)

**UI-ONLY — zero C++/pipe/mailbox change** (both commits touch only `ui/` + `docs/tips.md` + the build
number). The DLL already supported this: `Hemmung` keeps a per-target slot (`s_dils[DIL_COUNT]`, one
shared re-assert worker started while *any* lever is active), `set/reset_time_dilation` take a target,
and `get_time_state` has always returned **both** the Global and Pawn knobs in one reply. The single
slider + "Player only" checkbox shipped at build 2149 was the *only* thing making the two levers
mutually exclusive — so this is the UI catching up to the DLL, not new capability.

**Why it matters:** UE multiplies the world's effective dilation into `AActor::CustomTimeDilation`, so
the pawn's real rate is always **world × pawn**. Holding both at once is what produces classic
bullet-time — **World 0.5× + Player 2× = the player at normal speed inside a half-speed world** — and
it was unreachable from the card before.

- **Dual-row card** ([TeleportPanel.axaml](../ui/UE5DumpUI/Views/TeleportPanel.axaml), `3a91ba0`,
  build 2207) — "Whole world (`AWorldSettings.TimeDilation`)" and "Player pawn
  (`AActor.CustomTimeDilation`)", each with its own slider, preset row, **Apply**, **Reset**, badge and
  live readout, plus one shared **↻ Refresh both**. Reset is strictly per-lane: it resets its own
  target and snaps only its own slider to 1×; the DLL restores that lever's captured base and stops the
  worker only when no lever remains active.
- **Lane-parameterised VM core, not duplicated code**
  ([TeleportViewModel.cs](../ui/UE5DumpUI/ViewModels/TeleportViewModel.cs)) — `private enum TimeLane
  { World, Pawn }` + `LaneKey()` (`"global"`/`"pawn"` pipe target) + `LaneName()`, with
  `ApplyTimeAsync`/`ResetTimeAsync`/`ApplyTimePresetAsync(lane, …)` cores behind six thin
  `[RelayCommand]` wrappers. `ApplyTimeDilationReadout` fills BOTH lanes from one `TimeState`;
  `RefreshHeldTimeStateAsync` (connect-time read-back) syncs each slider only when *that* lever reports
  Active, so an inactive lever keeps its disk-persisted preference. **Add a lever by extending the
  enum, not by copying methods.**
- **"Combined player speed" readout + ceiling raise** (`a1fbe64`, build 2215) — the Player row shows a
  live `Combined player speed: 0.895×  (world 0.298 * pawn 3)` line so *Whole world* also slowing the
  player is never a surprise; the tooltip documents the **Player = 1 ÷ World** compensation. It is a
  pure C# getter over the **two slider values** (reactive via `[NotifyPropertyChangedFor]` on both
  lanes) — an intent/preview readout, *not* live state; the state readouts remain
  `World/PawnTimeCurrentText` fed from `get_time_state`. The **pawn slider ceiling went 3× → 10×
  (1000%)** for `CustomTimeDilation` super-speed; **World deliberately stays 0–3×**. Both are far
  inside the DLL clamp `Grimoire::TIME_DILATION_MIN/MAX = [0, 100]`. Card gained the dim subtitle
  "slow-mo / bullet time / freeze / fast-forward", echoed into the exported CE Lua `[ENABLE]` comment.
- **CE export bakes each record at its own value**
  ([TimeDilationScriptGenerator.cs](../ui/UE5DumpUI/Services/TimeDilationScriptGenerator.cs)) — new
  `BuildBatchRows(worldValue, pawnValue)` (the single-`double` form kept as a convenience overload
  delegating to it). The two records **"Time: World"** and **"Time: Player"** can be ticked at the same
  time — they serialise cleanly through the single-slot `CMD_TIME` mailbox, so enabling one never
  clobbers the other.
- **Persistence keys renamed** ([UiOptionsSettings.cs](../ui/UE5DumpUI/Models/UiOptionsSettings.cs)) —
  `TeleportUiOptions.TimeDilation` + `TimeTargetIsPawn` → `WorldTimeDilation` + `PawnTimeDilation`
  (both default 1.0). ⚠ **No migration**: unknown JSON members are ignored, so a user crossing build
  2207 silently loses their saved value/target once and falls back to 1.0/1.0.
- **Strings** ([en.axaml](../ui/UE5DumpUI/Resources/Strings/en.axaml)) — dropped `TP.TdPawnToggle` +
  `Tip.TP.TdPawnToggle`, `TP.TdRefresh` → `TP.TdRefreshBoth`, added `TdWorldHeader`/`TdPawnHeader`/
  `TdSubtitle`/`Tip.TP.TdEffective`.
- **Docs** ([tips.md](tips.md) "Slowing, freezing, or speeding up game time", `a4554f1` + `dec4303`) —
  the recipe now covers the two rows, the simultaneous CE records and the 1 ÷ World compensation.
- **Tests** — dual-lane command independence, `PawnEffectiveRateText` = world × pawn and its
  reactivity, per-lane presets, the two-arg CE batch rows, renamed-option round-trip. Compiled bindings
  (`x:DataType=vm:TeleportViewModel`) validated every new binding at build time.

**Known rough edges (unchanged by these commits):** the preset buttons still clamp to `[0, 3]` for
**both** lanes (`Math.Clamp(v, 0.0, 3.0)`) — harmless today since the largest preset is 2×, but a
future pawn preset above 3× would be silently clamped. **Not yet verified in-game:** only the *pawn*
lever has ever been live-exercised (The Adventures of Elliot, UE 4.27, build 2151); the world lever and
the bullet-time combination are unit-tested only.

-----
