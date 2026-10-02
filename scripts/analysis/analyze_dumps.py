#!/usr/bin/env python3
"""
analyze_dumps.py — derive empirical keyword / class-bonus tables from
JSONL dumps produced by UE5DumpUI's "Export -> Dump All Metadata" feature.

USAGE
    python analyze_dumps.py <dump1.jsonl> [dump2.jsonl ...]
    python analyze_dumps.py *.jsonl --top 100 --keyword Health,Damage

WHAT IT DOES
    1. Loads N dumps (one per game). Each dump = meta line + class lines
       + summary line, as documented in DumpAllService.cs.
    2. Aggregates across games:
         - Property-name frequency (filter to game-only)
         - Class-name token frequency
         - Token co-occurrence: which class tokens host which property keywords?
         - Score distribution given current PropertyScoringTable
    3. Surfaces candidate keywords + class-bonus targets that the
       hand-curated tables in
       `ui/UE5DumpUI/Services/PropertyScoringTable.cs` and
       `ui/UE5DumpUI/Services/ClassLocationScorer.cs` are missing.

NOT IN SCOPE
    Doesn't modify any C# source. Outputs Markdown report + CSV.
    Author copies the actionable rows back into the C# tables by hand
    (with a sanity test added per change).

DESIGN NOTES
    - Each game has different mechanics; statistical signal needs
      ≥3 dumps to be meaningful. Single-dump runs are useful for
      sanity-checking a specific game but don't drive table changes.
    - `is_engine_class` filter mirrors the DLL's IsEnginePackage list.
      Analysis usually restricts to game classes since engine fields
      already have stable English names.
    - Tokenization matches the C# KeywordTokenizer rules so the
      derived keywords plug directly into the scoring table.
"""

from __future__ import annotations
import argparse
import json
import re
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable, Iterator


# =====================================================================
# I/O — read a JSONL dump
# =====================================================================

@dataclass
class Dump:
    """One game's dump, parsed into class records + meta."""
    path: Path
    meta: dict = field(default_factory=dict)
    classes: list[dict] = field(default_factory=list)
    errors: list[dict] = field(default_factory=list)
    summary: dict = field(default_factory=dict)

    @property
    def label(self) -> str:
        """Short human label — module name with extension trimmed."""
        m = self.meta.get("module", "")
        if not m:
            return self.path.stem
        return m.replace("-Win64-Shipping.exe", "").replace(".exe", "")


def load_dump(path: Path) -> Dump:
    d = Dump(path=path)
    with path.open(encoding="utf-8") as f:
        for lineno, line in enumerate(f, 1):
            line = line.strip()
            if not line:
                continue
            try:
                rec = json.loads(line)
            except json.JSONDecodeError as e:
                print(f"  [warn] {path.name}:{lineno} bad JSON: {e}", file=sys.stderr)
                continue
            kind = rec.get("kind")
            # struct, enum, and instance lines are schema or a live index.
            # Folding them into class stats would retune keyword tables
            # against Vector and HitResult.
            if kind == "meta":
                d.meta = rec
            elif kind == "class":
                d.classes.append(rec)
            elif kind == "error":
                d.errors.append(rec)
            elif kind == "summary":
                d.summary = rec
    return d


# =====================================================================
# Tokenizer — mirrors C# KeywordTokenizer
# =====================================================================

_TOKEN_SPLIT_RE = re.compile(r"[_\-]+")

def tokenize(identifier: str) -> list[str]:
    """Mirror of C# KeywordTokenizer.Tokenize: split on underscores,
    hyphens, lower→upper transitions, and run-of-uppers→lower
    transitions. Lowercases output."""
    if not identifier:
        return []
    out: list[str] = []
    for part in _TOKEN_SPLIT_RE.split(identifier):
        if not part:
            continue
        cur = []
        for i, c in enumerate(part):
            # Rule 3: lower→upper split
            if cur and cur[-1].islower() and c.isupper():
                out.append("".join(cur).lower()); cur = [c]
                continue
            # Rule 4: run-of-uppers→lower (split before last upper)
            if len(cur) >= 2 and cur[-1].isupper() and cur[-2].isupper() and c.islower():
                last_upper = cur.pop()
                out.append("".join(cur).lower())
                cur = [last_upper, c]
                continue
            cur.append(c)
        if cur:
            out.append("".join(cur).lower())
    return out


# =====================================================================
# Filtering — engine vs game classes
# =====================================================================

# Engine classes live under `/Script/<Module>/...`. The DLL emits paths
# with either `.` or `/` as the package/class separator depending on
# code path (Ubel::GetFullName uses `/`; PropertyMatch::classPath uses
# `.`). Substring match against `/Script/` is format-agnostic and
# precise enough — game classes live under `/Game/...` so there's no
# collision risk.
def is_engine_class(cls: dict) -> bool:
    path = cls.get("path", "")
    return "/Script/" in path

def is_game_class(cls: dict) -> bool:
    path = cls.get("path", "")
    return "/Game/" in path or "/Engine/" not in path and "/Script/" not in path


# =====================================================================
# Aggregations
# =====================================================================

@dataclass
class GameAggregates:
    """Per-game roll-up of property-name + class-token + function
    statistics. The function side mirrors the property side: token
    frequency, class-name co-occurrence, optional dedup by addr."""
    label: str
    # OWN props only — fields actually defined on the class (not
    # inherited from supers). This is the cheat-relevant signal: every
    # game class re-lists AActor's bReplicates / bHidden / OnClicked
    # because WalkClass merges supers into one Fields list. We split
    # them back out via super_addr resolution.
    own_prop_name_freq: Counter = field(default_factory=Counter)
    own_prop_token_freq: Counter = field(default_factory=Counter)
    # Number of CLASSES that own each property name (cross-class
    # frequency). e.g. Health=5 means 5 different classes declare
    # their own Health field — a strong cross-game signal.
    own_prop_classes: dict[str, set[str]] = field(default_factory=lambda: defaultdict(set))
    class_token_freq: Counter = field(default_factory=Counter)
    is_bpgc_count: int = 0
    game_class_count: int = 0
    engine_class_count: int = 0
    # Co-occurrence on OWN props only.
    # (class_token, prop_token) -> count
    class_x_prop: Counter = field(default_factory=Counter)
    # === Function-side aggregates (Phase 2) ===
    # Each function entry from the dump represents a function declared
    # on the class (WalkFunctions walks UStruct::Children at the class
    # level — doesn't recurse up the super chain). So unlike props,
    # there's no per-game inheritance dedup needed; the funcs[] list
    # is already "own functions" by virtue of how the DLL gathers them.
    own_func_name_freq: Counter = field(default_factory=Counter)
    own_func_token_freq: Counter = field(default_factory=Counter)
    own_func_classes: dict[str, set[str]] = field(default_factory=lambda: defaultdict(set))
    # (class_token, func_token) co-occurrence — used to surface
    # candidate ClassLocationScorer class rules from real function names.
    class_x_func: Counter = field(default_factory=Counter)


def _resolve_own_props(cls: dict, by_addr: dict[str, dict]) -> list[dict]:
    """Return the subset of `cls['props']` whose offset is at or above
    the super's <c>props_size</c> — those are the fields declared ON
    this class. Walks the super chain to handle the super-of-super case
    when the immediate super has zero own fields (rare but happens for
    pass-through base classes).
    """
    super_addr = cls.get("super_addr", "0x0")
    if not super_addr or super_addr == "0x0":
        return cls.get("props", [])
    super_cls = by_addr.get(super_addr)
    if super_cls is None:
        # Super not in our dump (engine module dropped, etc.). Treat
        # whole prop list as own to avoid losing data; analyst can
        # filter further client-side.
        return cls.get("props", [])
    super_size = super_cls.get("props_size", 0)
    return [p for p in cls.get("props", []) if p.get("offset", 0) >= super_size]


def aggregate(dump: Dump, game_only: bool = True) -> GameAggregates:
    """Per-game aggregation, OWN-props-only by default.
    Side effect: skips engine classes when game_only=True so cross-game
    stats focus on game-specific naming patterns."""
    agg = GameAggregates(label=dump.label)

    # Build addr -> class map for super lookup. Needs to include engine
    # classes too — game classes derive FROM engine classes, so the
    # super chain crosses the boundary.
    by_addr = {cls["addr"]: cls for cls in dump.classes if cls.get("addr")}

    for cls in dump.classes:
        if is_engine_class(cls):
            agg.engine_class_count += 1
            if game_only:
                continue
        else:
            agg.game_class_count += 1
        if cls.get("is_bpgc"):
            agg.is_bpgc_count += 1

        class_name = cls.get("name", "")
        class_tokens = set(tokenize(class_name))
        for ct in class_tokens:
            agg.class_token_freq[ct] += 1

        # OWN props only — the meaningful signal.
        for prop in _resolve_own_props(cls, by_addr):
            name = prop.get("name", "")
            if not name:
                continue
            agg.own_prop_name_freq[name] += 1
            agg.own_prop_classes[name].add(class_name)
            tokens = set(tokenize(name))
            for t in tokens:
                agg.own_prop_token_freq[t] += 1
            for ct in class_tokens:
                for pt in tokens:
                    agg.class_x_prop[(ct, pt)] += 1

        # === Function aggregation (Phase 2 — feeds KeywordScoringTable) ===
        # cls['funcs'] holds the class's OWN functions (DLL-side
        # WalkFunctions doesn't recurse supers), so no offset / addr
        # dedup is needed. Same trivial-token / min-games filters
        # at report time keep the signal-to-noise ratio sensible.
        for fn in cls.get("funcs", []):
            fname = fn.get("name", "")
            if not fname:
                continue
            agg.own_func_name_freq[fname] += 1
            agg.own_func_classes[fname].add(class_name)
            ftokens = set(tokenize(fname))
            for t in ftokens:
                agg.own_func_token_freq[t] += 1
            for ct in class_tokens:
                for ft in ftokens:
                    agg.class_x_func[(ct, ft)] += 1
    return agg


# =====================================================================
# Report generators
# =====================================================================

CHEAT_KEYWORD_HINTS = {
    # Hints for picking interesting tokens out of the noise. Each entry
    # is a *category label* the analyst can sort by; the table here is
    # NOT the keyword table — it's a UI sort for the analyst.
    #
    # Synced to mirror PropertyScoringTable.cs as of build 698 — when
    # this drifts the report's "Existing category" column lies about
    # which tokens are already covered, which historically made already-
    # added build-678 keywords (Effect / Target / Radius / etc.) keep
    # surfacing as "uncategorised" in fresh runs. Keep this list in
    # lockstep when you touch StatsKeywords / CombatKeywords / etc.
    # All entries are lowercase because tokenize() lowercases.
    "stats":     ("hp", "health", "mp", "mana", "sp", "stamina", "energy",
                  "xp", "exp", "experience", "level", "lv", "lvl",
                  "dead", "isdead", "alive", "isalive",
                  "regen", "regenerate", "max"),
    "combat":    ("damage", "dmg", "defense", "defence", "def", "armor", "armour",
                  "resistance", "resist",
                  "crit", "critical", "critrate", "critdamage",
                  "attack", "atk", "multiplier",
                  "weapon", "hit", "hitdamage",
                  # Build 678 cross-game additions
                  "effect", "target", "radius", "ability", "modifier", "duration"),
    "resources": ("gold", "coin", "coins", "money", "currency", "cash",
                  "credit", "credits",
                  "gem", "gems", "diamond", "diamonds", "crystal", "crystals",
                  "token", "tokens",
                  "ammo", "material", "materials", "resource", "resources",
                  "supply", "supplies",
                  "stack", "count", "quantity", "amount",
                  # Build 678 cross-game additions
                  "item", "items"),
    "movement":  ("speed", "velocity", "walk", "sprint", "run",
                  "jump", "jumpheight", "jumpz",
                  "friction", "acceleration", "gravity",
                  "dash", "climb", "swim"),
    "utility":   ("save", "load", "checkpoint",
                  "quest", "mission", "objective",
                  "cheat", "debug", "godmode", "noclip", "score",
                  "isimmortal", "canbedamaged", "immortal",
                  "invincible", "invulnerable"),
}

def categorize_token(tok: str) -> str | None:
    for cat, words in CHEAT_KEYWORD_HINTS.items():
        if tok in words:
            return cat
    return None


# Tokens that match the tokenizer but carry no useful signal — common
# English connectives, language particles, single letters that fall out
# of class names like `BP_Player_C` → ["bp","player","c"]. Filtering
# these out keeps the candidate-keyword report focused on signal.
#
# Phase 2 additions (function-side analysis): BP compilation artifacts
# dominate UFunction names. Every BPGC has dozens of
# `ExecuteUbergraph_Foo_K2Node_*`, `Foo__DelegateSignature`,
# `BndEvt__Foo_K2Node_*`, etc. Without this filter the top 60 function
# tokens are 100% UE/BP plumbing and zero cheat signal.
TRIVIAL_TOKENS: frozenset[str] = frozenset({
    "b",            # boolean prefix bX
    "c",            # _C BPGC suffix
    "f", "u", "a",  # UE type prefixes (FStruct/UClass/AActor)
    "is", "has",    # boolean verbs without category signal
    "on",           # delegate prefix Onx
    "in", "to", "of", "for", "with", "by", "the", "a", "an",
    "and", "or", "not", "be", "do", "get", "set",
    "my",           # generic prefix BP_MyFoo_C
    "bp",           # BP_ class prefix
    "tmp", "temp",  # placeholder names
    # --- BP / UE compilation artifacts (function-side) ---
    "ubergraph", "k2node", "evt", "bnd", "bpi",
    "evaluate", "exposed", "inputs", "outputs",
    "signature", "delegate", "bound",
    "execute", "executed", "executes",
    "notify", "notified",
    "finished", "completed",
    "clicked", "changed", "hovered", "unhovered", "released",
    "construct", "construction", "constructed",
    "inp", "func", "out",
    "receive", "received",
    "pre", "post",
    "begin", "end",  # BeginPlay/EndPlay etc — engine boilerplate
    "tick", "ticked",
    "play",  # PlaySound / PlayAnimation / etc — mostly UE
})


def report_top_property_names(aggs: list[GameAggregates], top: int = 100,
                              min_games: int = 1) -> str:
    """Top OWN property names (definition-site frequency), with per-game
    breakdown.

    "Classes" counts the number of distinct classes that declare their
    own copy of this name; "Games" counts how many dumps had at least
    one such declaration. Filter to min_games ≥ 3 to surface cross-game
    conventions and drop single-game noise (e.g. TQ2's m_* C++ field
    family dominates raw counts but says nothing about general patterns).
    """
    merged = Counter()
    per_game: dict[str, dict[str, int]] = defaultdict(dict)
    for agg in aggs:
        for name, classes in agg.own_prop_classes.items():
            cnt = len(classes)
            merged[name] += cnt
            per_game[name][agg.label] = cnt

    lines: list[str] = []
    if min_games > 1:
        title = (f"# Top {top} OWN property names (definition site) "
                 f"— ≥ {min_games} games")
    else:
        title = f"# Top {top} OWN property names (definition site) across {len(aggs)} game(s)"
    lines.append(title)
    lines.append("")
    if min_games > 1:
        lines.append(f"Filtered to names appearing in ≥ {min_games} games — drops "
                     f"single-game spikes (e.g. one game with 500 of the same "
                     f"name) so cross-game conventions stand out.")
    else:
        lines.append("Counts the number of distinct CLASSES that DECLARE their own copy "
                     "of this name (inherited copies excluded). High Total + multiple "
                     "games hit = strong cross-game convention.")
    lines.append("")
    lines.append("| Rank | Name | Classes | Games | Per game (label:N) |")
    lines.append("|---:|---|---:|---:|---|")
    rank = 0
    for name, total in merged.most_common():
        per = per_game[name]
        games_hit = len(per)
        if games_hit < min_games:
            continue
        rank += 1
        if rank > top:
            break
        per_str = "; ".join(f"{lbl}:{n}" for lbl, n in sorted(per.items()))
        lines.append(f"| {rank} | `{name}` | {total} | {games_hit} | {per_str} |")
    return "\n".join(lines)


def report_top_prop_tokens(aggs: list[GameAggregates], top: int = 60,
                           min_games: int = 1) -> str:
    """Property TOKEN frequency on OWN properties only. Trivial tokens
    (b/c/on/is/...) are filtered out so candidate keywords stay
    visible. Cross-references existing categorisation so analyst sees
    which tokens land outside the table.

    When min_games > 1, drops tokens that appear in only a few dumps —
    those are likely game-specific terminology (e.g. one game's
    "uberGraph" or "m_pIconTexture") rather than cross-game patterns
    worth seeding into the scoring table.
    """
    merged = Counter()
    per_game: dict[str, set[str]] = defaultdict(set)
    for agg in aggs:
        for tok, n in agg.own_prop_token_freq.items():
            if tok in TRIVIAL_TOKENS:
                continue
            if len(tok) < 2:
                continue
            merged[tok] += n
            per_game[tok].add(agg.label)

    lines: list[str] = []
    if min_games > 1:
        lines.append(f"# Top {top} OWN property TOKENS (≥ {min_games} games)")
    else:
        lines.append(f"# Top {top} OWN property TOKENS (candidate keywords)")
    lines.append("")
    lines.append("Tokens of game-defined property names. Trivial tokens "
                 "(b/c/on/is/etc.) filtered. Cross-referenced against "
                 "existing PropertyScoringTable buckets.")
    lines.append("")
    lines.append("| Rank | Token | Hits | Games | Existing category |")
    lines.append("|---:|---|---:|---:|---|")
    rank = 0
    for tok, hits in merged.most_common():
        games = len(per_game[tok])
        if games < min_games:
            continue
        rank += 1
        if rank > top:
            break
        cat = categorize_token(tok) or ""
        lines.append(f"| {rank} | `{tok}` | {hits} | {games} | {cat} |")
    return "\n".join(lines)


def report_top_func_names(aggs: list[GameAggregates], top: int = 100,
                          min_games: int = 1) -> str:
    """Top function names (definition-site frequency, OWN funcs per
    DLL-side WalkFunctions semantics — see GameAggregates docstring).
    Mirrors report_top_property_names for the function side. Feeds
    KeywordScoringTable.cs candidate adds."""
    merged = Counter()
    per_game: dict[str, dict[str, int]] = defaultdict(dict)
    for agg in aggs:
        for name, classes in agg.own_func_classes.items():
            cnt = len(classes)
            merged[name] += cnt
            per_game[name][agg.label] = cnt

    lines: list[str] = []
    if min_games > 1:
        lines.append(f"# Top {top} OWN function names — ≥ {min_games} games")
    else:
        lines.append(f"# Top {top} OWN function names across {len(aggs)} game(s)")
    lines.append("")
    lines.append("Counts the number of distinct CLASSES that DECLARE this "
                 "function name. Cross-game frequency surfaces function "
                 "naming conventions that should feed KeywordScoringTable.")
    lines.append("")
    lines.append("| Rank | Name | Classes | Games | Per game (label:N) |")
    lines.append("|---:|---|---:|---:|---|")
    rank = 0
    for name, total in merged.most_common():
        per = per_game[name]
        games_hit = len(per)
        if games_hit < min_games:
            continue
        rank += 1
        if rank > top:
            break
        per_str = "; ".join(f"{lbl}:{n}" for lbl, n in sorted(per.items()))
        lines.append(f"| {rank} | `{name}` | {total} | {games_hit} | {per_str} |")
    return "\n".join(lines)


def report_top_func_tokens(aggs: list[GameAggregates], top: int = 60,
                           min_games: int = 1) -> str:
    """Top function TOKENS — candidate keywords for KeywordScoringTable.
    Same trivial-token / length-2 filtering as property side."""
    merged = Counter()
    per_game: dict[str, set[str]] = defaultdict(set)
    for agg in aggs:
        for tok, n in agg.own_func_token_freq.items():
            if tok in TRIVIAL_TOKENS:
                continue
            if len(tok) < 2:
                continue
            merged[tok] += n
            per_game[tok].add(agg.label)

    lines: list[str] = []
    if min_games > 1:
        lines.append(f"# Top {top} OWN function TOKENS — ≥ {min_games} games")
    else:
        lines.append(f"# Top {top} OWN function TOKENS (candidate keywords)")
    lines.append("")
    lines.append("Tokens of game-defined function names. Trivial tokens "
                 "(b/c/on/is/etc.) filtered. Candidates for "
                 "KeywordScoringTable.cs Stats / Inventory / Movement / "
                 "Combat / Utility / ExplicitMovementCheats buckets.")
    lines.append("")
    lines.append("| Rank | Token | Hits | Games | Existing category |")
    lines.append("|---:|---|---:|---:|---|")
    rank = 0
    for tok, hits in merged.most_common():
        games = len(per_game[tok])
        if games < min_games:
            continue
        rank += 1
        if rank > top:
            break
        cat = categorize_token(tok) or ""
        lines.append(f"| {rank} | `{tok}` | {hits} | {games} | {cat} |")
    return "\n".join(lines)


def report_unusual_func_locations(aggs: list[GameAggregates],
                                  min_count: int = 3,
                                  min_games: int = 3) -> str:
    """Class tokens hosting cheat-relevant function tokens. Surfaces
    candidate ClassLocationScorer additions from the function side
    (mirror of Property-side Unusual Locations report). Function names
    are verb+noun, so the property-side cheat_tokens set works as a
    first filter."""
    KNOWN_BONUSES = {
        # ClassLocationScorer.FunctionBonus expected (+) entries.
        "character", "pawn", "playercontroller", "playerstate",
        "player", "gamemode", "gameinstance", "savegame",
        # Property-side rules too — same classes show up on both sides.
        "abilitysystem", "attributeset", "inventory", "equipment",
        "playerprofile", "localplayer", "gameviewportclient", "hud",
        "ucheatmanager", "cheatmanager",
        "weapon", "projectile", "battle",  # build 678 adds
    }
    cheat_tokens = set().union(*CHEAT_KEYWORD_HINTS.values())

    merged: Counter = Counter()
    games_for_pair: dict[tuple[str, str], set[str]] = defaultdict(set)
    for agg in aggs:
        for (ct, ft), n in agg.class_x_func.items():
            if ft not in cheat_tokens:
                continue
            if ct in KNOWN_BONUSES:
                continue
            if ct in TRIVIAL_TOKENS:
                continue
            if len(ct) < 3:
                continue
            merged[(ct, ft)] += n
            games_for_pair[(ct, ft)].add(agg.label)

    lines: list[str] = []
    lines.append(f"# Candidate function-side class bonuses (hosts cheat-relevant function tokens, not yet in ClassLocationScorer)")
    lines.append("")
    lines.append(f"Filter: ≥ {min_count} occurrences across all dumps; "
                 f"appears in ≥ {min_games} game(s).")
    lines.append("")
    lines.append("| Class token | Func token | Hits | Games |")
    lines.append("|---|---|---:|---:|")
    for (ct, ft), n in merged.most_common(400):
        if n < min_count:
            continue
        games = len(games_for_pair[(ct, ft)])
        if games < min_games:
            continue
        lines.append(f"| `{ct}` | `{ft}` | {n} | {games} |")
    return "\n".join(lines)


def report_unusual_locations(aggs: list[GameAggregates], min_count: int = 3,
                             min_games: int = 1) -> str:
    """Class tokens hosting Stats/Combat/Resources tokens — flag the
    ones we DON'T have in the ClassLocationScorer table as candidates
    for the Unusual list (cheat-relevant property in a non-canonical
    container)."""
    KNOWN_BONUSES = {
        # From ClassLocationScorer.PropertyRules — Expected (+) entries.
        "character", "pawn", "playercontroller", "playerstate",
        "abilitysystem", "attributeset", "inventory", "equipment", "player",
        "gamemode", "gameinstance", "savegame", "playerprofile",
        # Unusual (+ flagged) entries.
        "localplayer", "gameviewportclient", "hud", "ucheatmanager", "cheatmanager",
    }
    cheat_tokens = set().union(*CHEAT_KEYWORD_HINTS.values())

    merged: Counter = Counter()
    games_for_pair: dict[tuple[str, str], set[str]] = defaultdict(set)
    for agg in aggs:
        for (ct, pt), n in agg.class_x_prop.items():
            if pt not in cheat_tokens:
                continue
            if ct in KNOWN_BONUSES:
                continue
            if ct in TRIVIAL_TOKENS:
                continue
            if len(ct) < 3:
                # Drop very short tokens (c/bp/ar/etc.) — too ambiguous
                # to seed a class-bonus rule.
                continue
            merged[(ct, pt)] += n
            games_for_pair[(ct, pt)].add(agg.label)

    lines: list[str] = []
    lines.append(f"# Candidate 'Unusual Location' class tokens (host cheat-relevant properties, not yet in ClassLocationScorer)")
    lines.append("")
    lines.append(f"Filter: ≥ {min_count} occurrences across all dumps; "
                 f"appears in ≥ {min_games} game(s). Games column promotes "
                 f"cross-game patterns over single-game spikes.")
    lines.append("")
    lines.append("| Class token | Property token | Hits | Games |")
    lines.append("|---|---|---:|---:|")
    for (ct, pt), n in merged.most_common(400):
        if n < min_count:
            continue
        games = len(games_for_pair[(ct, pt)])
        if games < min_games:
            continue
        lines.append(f"| `{ct}` | `{pt}` | {n} | {games} |")
    return "\n".join(lines)


def report_meta(aggs: list[GameAggregates], dumps: list[Dump]) -> str:
    lines: list[str] = []
    lines.append("# Dump summary")
    lines.append("")
    lines.append("| Game | UE | Module | Object count | Game classes | BPGCs | Engine classes |")
    lines.append("|---|---:|---|---:|---:|---:|---:|")
    for agg, dump in zip(aggs, dumps):
        ue = dump.meta.get("ue_version", "?")
        mod = dump.meta.get("module", "")
        obj = dump.meta.get("object_count", "?")
        lines.append(f"| {agg.label} | {ue} | {mod} | {obj} | "
                     f"{agg.game_class_count} | {agg.is_bpgc_count} | "
                     f"{agg.engine_class_count} |")
    return "\n".join(lines)


# =====================================================================
# Entry
# =====================================================================

def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("dumps", nargs="+", type=Path,
                   help="JSONL dump files (one per game)")
    p.add_argument("--top", type=int, default=100,
                   help="Top N rows in frequency tables (default 100)")
    p.add_argument("--min-games", type=int, default=3,
                   help="Minimum number of games a name/token/pair must appear in to "
                        "be included in the cross-game sections (default 3). Single-"
                        "game spikes (e.g. one game with 500x m_pIconTexture) are "
                        "filtered out. Set to 1 to see everything.")
    p.add_argument("--include-engine", action="store_true",
                   help="Include engine /Script/* classes in aggregates")
    p.add_argument("--output", type=Path, default=Path("analysis-report.md"),
                   help="Markdown report path (default: analysis-report.md)")
    args = p.parse_args(argv)

    dumps: list[Dump] = []
    for path in args.dumps:
        if not path.exists():
            print(f"[error] missing dump: {path}", file=sys.stderr)
            return 2
        print(f"[load] {path} ...", file=sys.stderr)
        dumps.append(load_dump(path))

    aggs = [aggregate(d, game_only=not args.include_engine) for d in dumps]

    sections = [report_meta(aggs, dumps)]
    if len(aggs) >= 2:
        # Lead with cross-game sections — single-game spikes get filtered
        # so the actionable signal surfaces immediately.
        sections.append("# --- PROPERTY SIDE ---")
        sections.append(report_top_property_names(aggs, top=args.top, min_games=args.min_games))
        sections.append(report_top_prop_tokens(aggs, top=min(args.top, 60), min_games=args.min_games))
        sections.append(report_unusual_locations(aggs, min_games=args.min_games))
        # Function side — mirrors property side. Feeds KeywordScoringTable.
        sections.append("# --- FUNCTION SIDE ---")
        sections.append(report_top_func_names(aggs, top=args.top, min_games=args.min_games))
        sections.append(report_top_func_tokens(aggs, top=min(args.top, 60), min_games=args.min_games))
        sections.append(report_unusual_func_locations(aggs, min_games=args.min_games))
        # Raw "all data" sections for completeness (no min-games filter).
        sections.append("# --- RAW (no min-games filter, single-game spikes included) ---")
        sections.append(report_top_property_names(aggs, top=args.top, min_games=1))
        sections.append(report_top_prop_tokens(aggs, top=min(args.top, 60), min_games=1))
        sections.append(report_top_func_names(aggs, top=args.top, min_games=1))
        sections.append(report_top_func_tokens(aggs, top=min(args.top, 60), min_games=1))
    else:
        sections.append(report_top_property_names(aggs, top=args.top))
        sections.append(report_top_prop_tokens(aggs, top=min(args.top, 60)))
        sections.append(report_unusual_locations(aggs))
        sections.append(report_top_func_names(aggs, top=args.top))
        sections.append(report_top_func_tokens(aggs, top=min(args.top, 60)))
    report = "\n\n".join(sections)
    args.output.write_text(report, encoding="utf-8")
    print(f"[done] wrote {args.output} ({len(report):,} chars)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
