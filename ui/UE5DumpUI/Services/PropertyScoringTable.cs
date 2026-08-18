using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Categories surfaced by the Interesting Properties Finder (B').
/// One property gets exactly one category — the one with the highest
/// keyword hit score, broken by enum order (Stats &gt; Combat &gt;
/// Resources &gt; Movement &gt; Utility &gt; Timing &gt; Other).
///
/// Smaller bucket count than Function-side because property NAMES are
/// usually noun-only (no verb to disambiguate Stats from Combat). When
/// in doubt the table groups by the data-domain rather than the verb.
/// </summary>
public enum PropertyCategory
{
    Other,      // Default — no keyword hits, or score below threshold
    Stats,      // HP / MP / Stamina / XP / Level (numeric character state)
    Combat,     // Damage / Defense / Armor / Crit (numeric combat tuning)
    Resources,  // Gold / Coin / Gem / Ammo / Material (collectibles)
    Movement,   // Speed / Jump / Friction (numeric movement tuning)
    Utility,    // Checkpoint / Save / Quest / Cheat (state flags)
    Timing,     // Cooldown / Duration / RemainingTime / TimeDilation (time knobs)
}

/// <summary>
/// Keyword + class-location heuristic for ranking UProperties by
/// "interesting-to-cheat-engine-user" probability.
///
/// Two layers of signal:
/// 1. Keyword match against the property NAME (KeywordTokenizer
///    whole-token comparison — same machinery the Function side uses).
/// 2. Class-location bonus via <see cref="ClassLocationScorer.PropertyBonus"/>.
///    The Unusual flag from PropertyBonus drives the ⚠ Unusual Location
///    badge in the UI — those are properties sitting in non-canonical
///    containers (LocalPlayer / GameViewportClient / HUD / etc.), which
///    are exactly the hits a CE user would miss looking in
///    Character/Pawn/PlayerState.
///
/// Add a keyword:
///   1. Pick the right category bucket below.
///   2. Append (case doesn't matter — comparison is case-insensitive
///      via KeywordTokenizer).
///   3. Add a test in PropertyScoringTableTests.
/// </summary>
public static class PropertyScoringTable
{
    /// <summary>Score threshold: rows with FinalScore &lt; this are
    /// dropped from default view (visible only when "Show all" is
    /// toggled in the UI). Calibrated against the Function-side
    /// threshold of 5 — Property side runs lower per-hit weight so
    /// 4 lands at roughly the same selectivity in practice.</summary>
    public const int InterestingThreshold = 4;

    // ------------------------------------------------------------------
    // Keyword tables (per category). Whole-token match against the
    // property name via KeywordTokenizer — same lesson from build 609
    // applies: substring matching causes acronym collisions ("Component"
    // contains "MP") so token matching is the safe default.
    // ------------------------------------------------------------------

    public const int StatsKeywordScore = 5;
    public static readonly string[] StatsKeywords =
    {
        // Health / mana / stamina / energy
        "HP", "Hp", "Health", "MP", "Mana", "SP", "Stamina", "Energy",
        // Experience / level
        "XP", "Exp", "Experience", "Level", "Lv", "Lvl",
        // Alive / dead state — IsDead / bIsAlive are very common BP-added
        // boolean cheat targets on character classes. Tokeniser produces
        // ["is","dead"] from "IsDead" so include both forms; "Dead" /
        // "Alive" alone catch property names like "bIsDead" -> ["b","is",
        // "dead"].
        "Dead", "IsDead", "Alive", "IsAlive",
        // General resource bars / regeneration
        "Regen", "Regenerate", "Max",  // "Max" is per-token; only fires on
                                        // "MaxHealth"/"MaxStamina"/etc, not
                                        // engine words like "ChannelMaxIndex"
    };

    public const int CombatKeywordScore = 4;
    public static readonly string[] CombatKeywords =
    {
        // Damage / defense math
        "Damage", "Dmg", "Defense", "Defence", "Def", "Armor", "Armour",
        "Resistance", "Resist",
        // Crit tuning
        "Crit", "Critical", "CritRate", "CritDamage",
        // Attack / multiplier knobs
        "Attack", "Atk", "Multiplier",
        // Weapon-related fields (loadout pointers, weapon stats)
        "Weapon", "Hit", "HitDamage",
        // === Added build 678 — confirmed cross-game via 15-game dump
        // analysis (scripts/analysis/analyze_dumps.py). Each appears
        // in ≥ 7/15 games with substantial per-game hit counts. ===
        "Effect",       // GameplayEffect framework — 14/15 games, 5740 hits
        "Target",       // combat target / aim target — 14/15 games
        "Radius",       // AoE / detection radius — 12/15 games
        "Ability",      // GameplayAbility framework — 7/15 games
        "Modifier",     // DamageModifier / SpeedModifier — 7/15 games
        "Duration",     // buff/effect duration — 9/15 games
    };

    public const int ResourcesKeywordScore = 4;
    public static readonly string[] ResourcesKeywords =
    {
        // Currency — both singular and plural since game property names
        // are commonly plural for scalar currency totals (e.g. "Coins"
        // as the running total). KeywordTokenizer doesn't stem so each
        // form needs an explicit entry.
        "Gold", "Coin", "Coins", "Money", "Currency", "Cash", "Credit", "Credits",
        // Gacha / premium
        "Gem", "Gems", "Diamond", "Diamonds", "Crystal", "Crystals",
        "Token", "Tokens",
        // Crafting / supplies
        "Ammo", "Material", "Materials", "Resource", "Resources", "Supply", "Supplies",
        // Inventory metadata
        "Stack", "Count", "Quantity", "Amount",
        // === Added build 678 — confirmed cross-game (13/15 games via
        // scripts/analysis/analyze_dumps.py, 1351 cross-game hits). ===
        "Item", "Items",
    };

    public const int MovementKeywordScore = 4;
    public static readonly string[] MovementKeywords =
    {
        "Speed", "Velocity", "Walk",
        "Sprint", "Run",  // "Run" safe here at token level — "RunCallback"
                          // is a function name pattern not a property name
        "Jump", "JumpHeight", "JumpZ",
        "Friction", "Acceleration", "Gravity",
        "Dash", "Climb", "Swim",
    };

    public const int UtilityKeywordScore = 3;
    public static readonly string[] UtilityKeywords =
    {
        "Save", "Load", "Checkpoint",
        "Quest", "Mission", "Objective",
        "Cheat", "Debug", "GodMode", "NoClip",
        "Score",
        // Boolean state flags common in BP-added overrides
        "IsImmortal", "CanBeDamaged", "Invincible", "Invulnerable",
    };

    // Timing (L1) — time/timer properties: BP-authored cooldown/countdown/
    // duration floats, respawn/reload/cast timers, and the engine's
    // reflected time-dilation knobs (AWorldSettings::TimeDilation, per-actor
    // CustomTimeDilation). All whole-token: "Time" only fires when 'time' is
    // its own token (CastTime → [cast,time]), never inside "Lifetime" →
    // [lifetime]. Same per-hit weight as Combat/Resources/Movement so a
    // single clean timer token clears InterestingThreshold(4). The category
    // check is appended LAST (lowest tie priority) so an ambiguous name that
    // also hits an earlier bucket keeps that bucket (e.g. BuffDuration stays
    // Combat because "Duration" lives in CombatKeywords and is scored first).
    public const int TimingKeywordScore = 4;
    public static readonly string[] TimingKeywords =
    {
        // Cooldowns / countdowns — both casings ("Cooldown" → [cooldown],
        // "CoolDown" → [cool,down]) since games use both.
        "Cooldown", "CoolDown", "Countdown",
        // Delays / intervals / explicit timer fields.
        "Delay", "Interval", "Timer", "Timers",
        // Elapsed / remaining readouts (RemainingTime/TimeRemaining hit via
        // "Remaining" + "Time"; -Time compounds like RespawnTime/ReloadTime/
        // CastTime/ChargeTime/ExpireTime all carry the "Time" token below).
        "Elapsed", "Remaining",
        // Actor lifetime family ("LifeSpan" → [life,span] catches
        // InitialLifeSpan/LifeSpan; "Lifetime" is a single token).
        "LifeSpan", "Lifespan", "Lifetime", "InitialLifeSpan",
        // Recharge / tick cadence.
        "Recharge", "TickRate", "Cadence",
        // Global slow-mo / freeze-time knobs (the L1 Hemmung levers).
        "TimeDilation", "GlobalTimeDilation",
        // Bare "Time" — whole-token, so safe (CastTime/RespawnTime/
        // TimeSeconds → Timing; Lifetime/Runtime → not).
        "Time",
    };

    // ------------------------------------------------------------------
    // Structural-signal tuning (P2a). Applied on top of keyword +
    // class-location scoring using ONLY data search_properties already
    // carries (PropType + StructType) — no new wire fields. PropertyFlags
    // gating (SaveGame/BlueprintVisible/Transient) is a separate follow-up
    // (P2b) that needs prop_flags added to the search_properties response.
    // ------------------------------------------------------------------

    /// <summary>UScriptStruct name of a GAS gameplay attribute. An
    /// FGameplayAttributeData in an AttributeSet is a near-certain gameplay
    /// stat (HP/Mana/Stamina/Attack/…).</summary>
    public const string GasAttributeStructType = "GameplayAttributeData";
    /// <summary>Boost for a GAS attribute — enough to clear the threshold on
    /// its own, so a GAS stat whose name didn't hit a keyword still surfaces.</summary>
    public const int GasAttributeBonus = 4;
    /// <summary>A value-category keyword (Stats/Resources/Combat/Movement) on a
    /// container / pointer / delegate type is usually a holder or label, not
    /// the live number. Gentle nudge, never enough to bury a multi-signal hit.</summary>
    public const int NonValueTypePenalty = -1;
    /// <summary>A Current/Max stat family within one class (Health + MaxHealth)
    /// is a strong "real gameplay stat" signal — boost every family member.</summary>
    public const int StatPairBonus = 2;

    /// <summary>UScriptStruct names (no F-prefix, matching StructType's
    /// convention — same as <see cref="GasAttributeStructType"/>) that ARE a
    /// unit of time. A property of one of these types is a near-certain timing
    /// field regardless of its name (e.g. an FTimespan "Cooldown").</summary>
    public static readonly HashSet<string> TimeStructTypes =
        new(StringComparer.Ordinal)
        {
            "Timespan", "DateTime", "QualifiedFrameTime", "FrameTime", "Timecode",
        };
    /// <summary>Boost for a time-typed struct — enough to clear the threshold
    /// on its own, so an FTimespan/FDateTime field whose name didn't hit a
    /// keyword still surfaces (mirrors <see cref="GasAttributeBonus"/>).</summary>
    public const int TimeStructBonus = 4;

    // PropertyFlags gating (P2b). Persistent / designer-facing fields are more
    // likely a real cheat target; editor-only fields never are. Conservative
    // on purpose — NO Transient/Net penalty, since current HP is very often
    // transient + replicated (GAS-derived). Needs the DLL's prop_flags field
    // (search_properties/_batch, build 2017+); 0 on older DLLs → no effect.
    public const int SaveGameBonus = 2;          // CPF_SaveGame — persisted player data (Gold/Level/…)
    public const int BlueprintVisibleBonus = 1;  // CPF_BlueprintVisible — designer/BP exposed
    public const int EditorOnlyPenalty = -4;     // CPF_EditorOnly — never a live runtime value

    // UE EPropertyFlags bits we key on (stable across UE4/UE5, from ObjectMacros.h).
    private const ulong CPF_BlueprintVisible = 0x0000000000000004UL;
    private const ulong CPF_SaveGame         = 0x0000000001000000UL;
    private const ulong CPF_EditorOnly       = 0x0000000800000000UL;

    /// <summary>Result of scoring one property.</summary>
    public readonly record struct ScoreResult(
        int FinalScore,
        PropertyCategory Category,
        int KeywordHits,
        int ClassBonus,
        bool IsUnusualLocation,
        int StructuralBonus = 0,
        bool IsGasAttribute = false);

    /// <summary>
    /// Score one property entry. Mirrors <see cref="KeywordScoringTable.Score"/>
    /// in structure so a future audit of either side gets a consistent
    /// breakdown (keywordSum + classBonus = finalScore).
    /// </summary>
    public static ScoreResult Score(PropertySearchMatch match)
    {
        // Tokenise property name. Class name is NOT tokenised into the
        // keyword search domain on this side — that would let a property
        // called "Foo" on class "DamageComponent" pick up the "Damage"
        // keyword via the class side, which is misleading (the *property*
        // isn't about damage; the class is). Function side does fold both
        // because there a function "Get" on "PlayerHealthComponent" still
        // genuinely deals with health.
        var tokens = KeywordTokenizer.TokenizeAsSet(match.PropName);

        int statsHits     = CountTokenHits(tokens, StatsKeywords);
        int combatHits    = CountTokenHits(tokens, CombatKeywords);
        int resourcesHits = CountTokenHits(tokens, ResourcesKeywords);
        int movementHits  = CountTokenHits(tokens, MovementKeywords);
        int utilityHits   = CountTokenHits(tokens, UtilityKeywords);
        int timingHits    = CountTokenHits(tokens, TimingKeywords);

        int statsScore     = statsHits     * StatsKeywordScore;
        int combatScore    = combatHits    * CombatKeywordScore;
        int resourcesScore = resourcesHits * ResourcesKeywordScore;
        int movementScore  = movementHits  * MovementKeywordScore;
        int utilityScore   = utilityHits   * UtilityKeywordScore;
        int timingScore    = timingHits    * TimingKeywordScore;

        var category = PropertyCategory.Other;
        int catScore = 0;
        int totalKeywordHits =
            statsHits + combatHits + resourcesHits + movementHits + utilityHits + timingHits;

        if (statsScore     > catScore) { catScore = statsScore;     category = PropertyCategory.Stats; }
        if (combatScore    > catScore) { catScore = combatScore;    category = PropertyCategory.Combat; }
        if (resourcesScore > catScore) { catScore = resourcesScore; category = PropertyCategory.Resources; }
        if (movementScore  > catScore) { catScore = movementScore;  category = PropertyCategory.Movement; }
        if (utilityScore   > catScore) { catScore = utilityScore;   category = PropertyCategory.Utility; }
        // Timing check LAST → lowest tie priority: a name that also hits an
        // earlier bucket keeps that bucket (BuffDuration stays Combat).
        if (timingScore    > catScore) { catScore = timingScore;    category = PropertyCategory.Timing; }

        // Class-location bonus + Unusual flag. The Unusual classes
        // (LocalPlayer / HUD / GameViewportClient / ...) carry a +4 that
        // pushes interesting properties above the threshold even when
        // their keyword hits are lukewarm — surfacing the "wait, why is
        // there a Health field in GameViewportClient?" findings that
        // are the whole point of B'.
        var (classBonus, isUnusual) = ClassLocationScorer.PropertyBonus(match.ClassName);

        // --- Structural signals (P2a): type-aware + GAS refinement. Uses
        // only PropType + StructType, both already on PropertySearchMatch. ---
        bool isGas = string.Equals(match.StructType, GasAttributeStructType,
                                   StringComparison.Ordinal);
        bool isTimeStruct = !string.IsNullOrEmpty(match.StructType)
                            && TimeStructTypes.Contains(match.StructType);
        int structuralBonus = 0;
        if (isGas)
        {
            structuralBonus += GasAttributeBonus;
            // Default an un-categorised GAS attribute (name didn't hit a
            // keyword bucket) to Stats so it still surfaces + gets a chip.
            if (category == PropertyCategory.Other)
                category = PropertyCategory.Stats;
        }
        else if (isTimeStruct)
        {
            structuralBonus += TimeStructBonus;
            // An FTimespan/FDateTime/FFrameTime whose name missed a keyword is
            // still a timing field purely from its struct type.
            if (category == PropertyCategory.Other)
                category = PropertyCategory.Timing;
        }
        else if (IsValueCategory(category) && IsNonValueType(match.PropType))
        {
            structuralBonus += NonValueTypePenalty;
        }

        // PropertyFlags gating (P2b): persistent / designer-facing fields are
        // more likely a live cheat target; editor-only fields never are.
        // Additive on top of GAS/type signals; a no-op (flags 0) on older DLLs
        // that don't emit prop_flags, so this can't regress existing scores.
        ulong flags = match.PropertyFlags;
        if ((flags & CPF_EditorOnly) != 0)
            structuralBonus += EditorOnlyPenalty;
        if ((flags & CPF_SaveGame) != 0)
            structuralBonus += SaveGameBonus;
        else if ((flags & CPF_BlueprintVisible) != 0)
            structuralBonus += BlueprintVisibleBonus;

        int keywordSum = statsScore + combatScore + resourcesScore +
                         movementScore + utilityScore + timingScore;
        int finalScore = keywordSum + classBonus + structuralBonus;

        return new ScoreResult(
            FinalScore:        finalScore,
            Category:          category,
            KeywordHits:       totalKeywordHits,
            ClassBonus:        classBonus,
            IsUnusualLocation: isUnusual,
            StructuralBonus:   structuralBonus,
            IsGasAttribute:    isGas);
    }

    /// <summary>Subset-match: keyword counts as a hit when ALL its
    /// tokens appear in <paramref name="tokens"/>. Same semantics as
    /// the Function-side scorer (KeywordScoringTable.CountTokenHits).</summary>
    private static int CountTokenHits(HashSet<string> tokens, string[] keywords)
    {
        int hits = 0;
        foreach (var k in keywords)
        {
            var keyTokens = KeywordTokenizer.Tokenize(k);
            if (keyTokens.Length == 0) continue;
            bool allPresent = true;
            foreach (var kt in keyTokens)
            {
                if (!tokens.Contains(kt)) { allPresent = false; break; }
            }
            if (allPresent) hits++;
        }
        return hits;
    }

    // ------------------------------------------------------------------
    // Structural helpers (P2a) — type prior + Current/Max stat pairing
    // ------------------------------------------------------------------

    /// <summary>Categories whose real cheat target is a numeric scalar (or a
    /// GAS attribute), so a non-value type is evidence AGAINST the match.</summary>
    private static bool IsValueCategory(PropertyCategory c) =>
        c is PropertyCategory.Stats or PropertyCategory.Resources
          or PropertyCategory.Combat or PropertyCategory.Movement;

    /// <summary>UE property types that can't themselves be the scalar cheat
    /// value — containers, pointers, interfaces, delegates. Numeric scalars,
    /// bool, enum, name/string and (non-GAS) structs stay neutral.</summary>
    private static bool IsNonValueType(string propType) => propType switch
    {
        "ArrayProperty" or "MapProperty" or "SetProperty"
            or "ObjectProperty" or "ClassProperty" or "WeakObjectProperty"
            or "SoftObjectProperty" or "SoftClassProperty" or "LazyObjectProperty"
            or "InterfaceProperty"
            or "DelegateProperty" or "MulticastInlineDelegateProperty"
            or "MulticastSparseDelegateProperty" => true,
        _ => false,
    };

    /// <summary>Qualifier tokens stripped from a stat name to find its stem.
    /// A name carrying one at either end is a "variant" (MaxHealth,
    /// CurrentMana, BaseDamage) rather than the plain base (Health).</summary>
    private static readonly HashSet<string> StatQualifierTokens =
        new(StringComparer.Ordinal)
        {
            // Both abbreviated and full-word forms — real games mix them
            // (Max/MaxHealth vs Maximum/MaximumHealth, seen in TQ2's
            // GrimAttributeSet* full-word naming).
            "max", "maximum", "min", "minimum", "current", "cur", "base",
            "total", "start", "starting", "initial", "default",
        };

    /// <summary>Strip leading/trailing qualifier tokens (Max/Current/Base/…)
    /// to reveal the shared stat stem. Returns ("", false) when the name is
    /// only qualifiers (e.g. "Max") or empty; <c>isVariant</c> is true when a
    /// qualifier was stripped.</summary>
    internal static (string stem, bool isVariant) NormaliseStatStem(string propName)
    {
        var toks = KeywordTokenizer.Tokenize(propName);
        if (toks.Length == 0) return ("", false);
        int lo = 0, hi = toks.Length;
        bool variant = false;
        while (lo < hi && StatQualifierTokens.Contains(toks[lo]))     { variant = true; lo++; }
        while (hi > lo && StatQualifierTokens.Contains(toks[hi - 1])) { variant = true; hi--; }
        if (lo >= hi) return ("", false);
        return (string.Join('.', toks[lo..hi]), variant);
    }

    /// <summary>
    /// Post-scoring pass: within each defining class, find Current/Max stat
    /// families (≥2 fields sharing a stem where at least one is a
    /// Max/Current/… variant — e.g. Health + MaxHealth, or CurrentHP + MaxHP)
    /// and return a <see cref="StatPairBonus"/> per member. Keyed by
    /// (definingClass, propName, offset) to match the finder's dedup key.
    /// A paired stem is a strong "this is a live gameplay stat" signal that a
    /// per-field score can't see (it needs sibling context).
    /// </summary>
    public static Dictionary<(string cls, string name, int offset), int>
        ComputeStatPairBonuses(IEnumerable<PropertySearchMatch> matches)
    {
        static string DefClass(PropertySearchMatch m) =>
            string.IsNullOrEmpty(m.DefiningClassName) ? m.ClassName : m.DefiningClassName;

        var byClass = new Dictionary<string, List<PropertySearchMatch>>(StringComparer.Ordinal);
        foreach (var m in matches)
        {
            var cls = DefClass(m);
            if (string.IsNullOrEmpty(cls) || string.IsNullOrEmpty(m.PropName)) continue;
            if (!byClass.TryGetValue(cls, out var list)) byClass[cls] = list = new();
            list.Add(m);
        }

        var bonuses = new Dictionary<(string, string, int), int>();
        foreach (var list in byClass.Values)
        {
            // stem -> (any member is a variant, members)
            var families =
                new Dictionary<string, (bool hasVariant, List<PropertySearchMatch> members)>(
                    StringComparer.OrdinalIgnoreCase);
            foreach (var m in list)
            {
                var (stem, isVariant) = NormaliseStatStem(m.PropName);
                if (string.IsNullOrEmpty(stem)) continue;
                if (!families.TryGetValue(stem, out var fam))
                    fam = (false, new List<PropertySearchMatch>());
                fam.members.Add(m);
                fam.hasVariant |= isVariant;
                families[stem] = fam;
            }
            foreach (var fam in families.Values)
            {
                // Need ≥2 fields sharing a stem AND at least one Max/Current/…
                // variant, so two unqualified siblings don't spuriously pair.
                if (fam.members.Count < 2 || !fam.hasVariant) continue;
                foreach (var m in fam.members)
                    bonuses[(DefClass(m), m.PropName, m.PropOffset)] = StatPairBonus;
            }
        }
        return bonuses;
    }

    /// <summary>Display label for a property category.</summary>
    public static string DisplayName(PropertyCategory cat) => cat switch
    {
        PropertyCategory.Stats     => "Stats",
        PropertyCategory.Combat    => "Combat",
        PropertyCategory.Resources => "Resources",
        PropertyCategory.Movement  => "Movement",
        PropertyCategory.Utility   => "Utility",
        PropertyCategory.Timing    => "Timing",
        _                          => "Other",
    };

    /// <summary>Foreground hex colour for the chip in the DataGrid.</summary>
    public static string CategoryColor(PropertyCategory cat) => cat switch
    {
        PropertyCategory.Stats     => "#E07B7B",  // soft red — HP/MP
        PropertyCategory.Combat    => "#E0A050",  // orange — damage/def
        PropertyCategory.Resources => "#DCDCAA",  // gold — money/gem
        PropertyCategory.Movement  => "#7FB6E8",  // sky — speed/jump
        PropertyCategory.Utility   => "#B280D9",  // purple — quest/save
        PropertyCategory.Timing    => "#4EC9B0",  // teal — cooldown/timer
        _                          => "#808080",  // grey — other
    };

    /// <summary>
    /// One canned query per category, used by the Interesting Properties
    /// VM to batch-call <c>search_properties</c> across a wide keyword
    /// surface in round 1 (no new DLL command needed). Each entry is
    /// a representative keyword that the DLL substring-matches against
    /// every property name. The scorer then re-evaluates client-side
    /// using the full keyword tables above — so this list is "what to
    /// fetch", not "what to score on".
    ///
    /// <para><b>This list and the keyword tables above must be kept in step,
    /// and drifted apart for a long time (audit #5 Z3).</b> A property is only
    /// ever scored if it was fetched, so a keyword no seed can reach is a dead
    /// scoring arm: 58 of the 123 scored keywords were unreachable, including
    /// most of Utility and Resources. <c>PropertyScoringTableTests</c> now
    /// asserts the two agree, so this cannot silently drift again — the
    /// "Add a keyword" recipe at the top of this class never mentioned the seed
    /// list, 460 lines below it, which is how the gap opened.</para>
    ///
    /// <para><b>Why this is not simply the union of the keyword tables.</b> The
    /// DLL matches a seed as a bare case-insensitive substring
    /// (<c>lowerPropName.find(s.lowerQuery)</c>) and fills each query's own
    /// 200-row envelope in GObjects walk order, first come first served. So the
    /// scarce resource is the per-query cap, not CPU. A seed whose matches are
    /// mostly unrelated words spends its whole envelope before reaching the
    /// field the user wants — that is why <see cref="DeliberatelyUnseededKeywords"/>
    /// exists and must stay excluded. Volume alone is fine: <c>Item</c> (3,050
    /// matches) and <c>Velocity</c> (665) exceed the cap but are 98%/99%
    /// genuine, so the 200 rows they do return are the right rows.</para>
    ///
    /// <para>Added seeds cost little beyond the first hit: the inner loop tests
    /// <c>results.size() &gt;= cap</c> <i>before</i> the substring search, so a
    /// broad seed goes nearly free once its envelope is full.</para>
    /// </summary>
    public static readonly string[] SeedQueries =
    {
        // Stats
        "HP", "Health", "Mana", "Stamina", "Energy", "Level", "Experience",
        "Max", "Dead", "Alive", "Regen", "Lvl",
        // Combat
        "Damage", "Defense", "Armor", "Crit", "Attack", "Multiplier",
        "Weapon", "Hit",
        "Dmg", "Defence", "Armour", "Resist", "Atk", "Ability", "Effect",
        "Target", "Radius", "Modifier",
        // Resources
        "Gold", "Coin", "Money", "Gem", "Ammo", "Stack", "Count",
        "Currency", "Cash", "Credit", "Diamond", "Crystal", "Token",
        // "Suppl" not "Supply": the plural "Supplies" does not contain the
        // singular, and the DLL match is a bare substring. One seed covers both.
        "Material", "Resource", "Suppl", "Quantity", "Amount", "Item",
        // Movement
        "Speed", "Jump", "Walk", "Sprint", "Friction", "Gravity",
        "Velocity", "Accel", "Dash", "Climb", "Swim",
        // Utility (state flags / quest)
        "Quest", "Cheat", "Immortal", "Damaged", "Invincible",
        "Save", "Checkpoint", "Mission", "Objective", "Debug", "GodMode",
        "NoClip", "Score", "Invulnerable",
        // Timing (L1) — the DLL substring-matches these against property
        // names so round-1 actually fetches timer fields for the scorer.
        "Cooldown", "Duration", "Timer", "Delay", "Interval", "Elapsed",
        "Remaining", "Lifespan", "Recharge", "TimeDilation", "Time",
        "TickRate", "Cadence",
    };

    /// <summary>
    /// Scored keywords that are deliberately NOT seeded, and must not be
    /// "helpfully" added later. See the measurement in the
    /// <see cref="SeedQueries"/> remarks: each of these is a short acronym or
    /// fragment whose substring matches are overwhelmingly unrelated words, so
    /// seeding it would burn a whole 200-row envelope on junk in GObjects walk
    /// order and surface nothing.
    ///
    /// Measured over 578,809 distinct identifiers from three shipped UE games
    /// (DWORIGINS, ES2, CrimsonDesert), as substring-matches / of which the
    /// seed is actually the leading token:
    ///   MP    10,180 /   634 (6%)   — Component, Compression, Template, Sample
    ///   SP     8,847 / 5,514 (62%)  — but Spawn/Space/Speed/Spline, not "SP"
    ///   XP     3,373 /   702 (21%)
    ///   Exp    1,422 / 1,272 (89%)  — but Exposure/Explorer/Export, not "Exp"
    ///   Lv     1,949 /   484 (25%)  — Solve/Valve/Twelve
    ///   Def    1,962 / 1,616 (82%)  — but Default(880) dominates
    ///   Load   1,063 /   421 (40%)  — Upload/Download/Preload
    ///   Run      442 /   284 (64%)  — Runtime(145) outweighs Run(88)
    /// A name like <c>CurrentMP</c> therefore stays unreachable by design. The
    /// honest repair for those is a whole-token match on the DLL side (the
    /// client scorer already tokenises), not a broader substring.
    /// </summary>
    public static readonly string[] DeliberatelyUnseededKeywords =
    {
        "MP", "SP", "XP", "Exp", "Lv", "Def", "Load", "Run",
    };
}
