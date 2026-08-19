namespace UE5DumpUI.Services;

/// <summary>
/// Shared class-location heuristic used by both the Interesting Functions
/// scorer and the Interesting Properties scorer.
///
/// Different "lens" depending on what's being scored:
///   - <see cref="FunctionBonus"/> — relevance of a UFunction's owning
///     class (Character / Pawn / ... = good; Anim / Niagara = noise).
///   - <see cref="PropertyBonus"/> — relevance of a UProperty's owning
///     class, with the NEW concept of "unusual location": HP / Damage /
///     Stamina fields that sit in <c>LocalPlayer</c> /
///     <c>GameViewportClient</c> / <c>HUD</c> instead of where Unreal
///     convention would put them. Those hits get a HIGHER bonus AND a
///     boolean flag so the UI can stamp a ⚠ "Unusual Location" badge.
///
/// Substring match (case-insensitive) against the owning class name —
/// same semantics the function-side scorer has had since build 597 (so
/// "AnimCharacter" picks up both the Anim penalty AND the Character
/// bonus, accurately reflecting that it's an animation event on a
/// character).
///
/// TWO PROPERTIES OF THIS TABLE THAT ITS ROW COMMENTS USED TO DENY
/// (audit #5 AC7 / AC8 — the comments were wrong, the code was right):
///
/// 1. THERE IS NO KEYWORD GATE, and there structurally cannot be one:
///    both entry points take the class name and NOTHING else, so no rule
///    can condition its bonus on the function/property name. Four rule
///    blocks used to claim "keyword-gated so the +2 only lands when the
///    name is itself relevant". Untrue — every bonus lands on the class
///    name alone, whatever the member is called. Adding a gate would
///    change the score of every row in every game, so it is a design
///    change to argue for, not a comment to believe.
///
/// 2. EVERY MATCHING ROW STACKS; none is a fallback for the rows above
///    it. "PlayerController" scores PlayerController(+3) + Player(+2) = 5,
///    and "LocalPlayer" scores LocalPlayer(+4) + Player(+2) = 6. That is
///    deliberate and pinned by PropertyScoringTableTests, but it means the
///    class name ALONE can reach KeywordScoringTable.InterestingThreshold
///    (5) with zero keyword hits — e.g. "PlayerCharacter" = Player(+2) +
///    Character(+3). Weigh that before adding another common token.
/// </summary>
public static class ClassLocationScorer
{
    // ------------------------------------------------------------------
    // Function-side bonuses (verbatim from KeywordScoringTable build 607
    // when ClassBonuses lived inside that file -- moved here so Property
    // side can share the bag-of-substrings pattern + tests cover both
    // call sites).
    // ------------------------------------------------------------------

    private static readonly (string Substring, int Bonus)[] FunctionBonuses =
    {
        // Player-controlled / persistent state -- big boost
        ("Character",         3),
        ("Pawn",              3),
        ("PlayerController",  3),
        ("PlayerState",       3),
        // Generic "Player" — catches game-named classes like BP_Player_C,
        // AnimMan_Player_C, MyPlayerStats, etc. Mild bonus (+2) since some
        // false-positives (PlayerCameraManager) are still tolerable.
        // NOT a fallback for the four rows above: it STACKS with them, so
        // "PlayerController" scores 3+2=5 and "PlayerCharacter" 3+2=5 —
        // the interesting threshold, from the class name alone. (AC7)
        ("Player",            2),
        // === Build 686 — Phase 2 function-side analyzer additions.
        // 15-game cross-game function-token co-occurrence:
        //   enemy → damage / attack  : 3-4 games, 39+15 hits — Enemy* classes
        //                              clearly host attack/damage functions
        //   weapon → weapon / save   : 3-5 games — Weapon* classes host
        //                              weapon-related functions (mirror of the
        //                              build 678 Property-side Weapon rule)
        ("Enemy",             2),
        ("Weapon",            2),
        // === 7-game cross-game analysis (2026-07-09, ES2/SEED/TQ2/Avowed/
        // DQ7R/HogwartsLegacy/SB): Health* (4/7) and Damage* (5/7) classes
        // host cheat-relevant functions. Clean tokens (no core UE class
        // contains them). The +2 lands on the CLASS NAME ALONE — there is no
        // keyword gate here or anywhere in this table (see the class doc);
        // the earlier claim of one was never implemented. (AC8) ===
        ("Health",            2),
        ("Damage",            2),
        // Game-level systems
        ("GameMode",          2),
        ("GameInstance",      2),
        ("SaveGame",          2),
        // Visual / audio / animation -- penalty. SURGICAL compound names
        // only: a bare "Anim" substring penalty false-positives on
        // legitimate game-class prefixes like "AnimMan_Player_C" (a
        // game's player character in TowerOfMask). The anim framework
        // itself uses very specific compound class names; we list those
        // explicitly so game classes that happen to begin with "Anim"
        // are not punished.
        ("AnimInstance",     -2),
        ("AnimMontage",      -2),
        ("AnimSequence",     -2),
        ("AnimNotify",       -2),
        ("AnimGraph",        -2),
        ("AnimBlueprint",    -2),
        ("AnimDataController", -2),
        ("NiagaraSystem",    -2),
        ("NiagaraEmitter",   -2),
        ("NiagaraComponent", -2),
        ("SoundCue",         -2),
        ("SoundWave",        -2),
        ("SoundBase",        -2),
        ("AudioComponent",   -2),
        ("ParticleSystem",   -2),
        ("ParticleEmitter",  -2),
        ("UserWidget",       -1),
        ("WidgetComponent",  -1),
    };

    /// <summary>
    /// Sum bonuses for every substring that appears in the class name.
    /// Multiple hits stack (AnimCharacter = +Character +Anim = +1 net).
    /// </summary>
    public static int FunctionBonus(string className)
    {
        if (string.IsNullOrEmpty(className)) return 0;
        var lower = className.ToLowerInvariant();
        int total = 0;
        foreach (var (sub, bonus) in FunctionBonuses)
        {
            if (lower.Contains(sub.ToLowerInvariant()))
                total += bonus;
        }
        return total;
    }

    // ------------------------------------------------------------------
    // Property-side bonuses (NEW for B' / Interesting Properties).
    //
    // Key conceptual addition: an "Unusual" tier separated from the
    // regular bonus tier. Classes in the Unusual tier ARE valid places
    // to find gameplay state -- just not the FIRST place a cheat-engine
    // user would think to look. Surfacing them with a ⚠ badge is the
    // whole point of B'.
    // ------------------------------------------------------------------

    /// <summary>One row of the property-side class-location table.
    /// <see cref="Bonus"/> is added to the property's keyword score.
    /// <see cref="IsUnusual"/> drives the ⚠ Unusual Location badge.</summary>
    public readonly record struct PropertyClassRule(string Substring, int Bonus, bool IsUnusual);

    private static readonly PropertyClassRule[] PropertyRules =
    {
        // === Expected locations (gameplay state lives here normally) ===
        new("Character",        3, false),
        new("Pawn",             3, false),
        new("PlayerController", 3, false),
        new("PlayerState",      3, false),
        new("AbilitySystem",    3, false),
        new("AttributeSet",     3, false),
        new("Inventory",        3, false),
        new("Equipment",        3, false),
        // Generic "Player" catches game-named classes like BP_Player_C,
        // AnimMan_Player_C, MyPlayerStats, etc. Mild bonus (+2). NOT a
        // fallback for the rows above — it STACKS with them (LocalPlayer
        // scores 4+2=6, pinned by PropertyScoringTableTests). (AC7)
        new("Player",           2, false),
        // Game-level
        new("GameMode",         2, false),
        new("GameInstance",     2, false),
        new("SaveGame",         2, false),
        new("PlayerProfile",    2, false),

        // === Added build 678 — derived from 15-game cross-game dump
        // analysis. Each token confirmed as host for cheat-relevant
        // properties in ≥ 4 of 15 games (see scripts/analysis/
        // analyze_dumps.py "Candidate Unusual Location" section). ===
        new("Weapon",           2, false),  // 8 games host weapon/max stats
        new("Projectile",       2, false),  // 4-5 games host speed/hit/damage
        new("Battle",           2, false),  // 5 games — Battle* containers
                                            // for damage/count/max (common
                                            // in JRPG/strategy battles)
        // === Added build 686 — Phase 2 function-side analyzer also
        // reveals "enemy" class token hosting damage/attack functions
        // in 3-4 games. Mirror to Property side for symmetric scoring
        // (an "Enemy*" class is just as likely to hold damage / health
        // properties as it is to expose damage functions). ===
        new("Enemy",            2, false),

        // === 7-game cross-game analysis (2026-07-09): Health* (4/7) and
        // Damage* (5/7) classes host cheat-relevant properties (HealthComponent,
        // DamageDealer, etc.). Clean tokens — no core UE class contains them.
        // The +2 lands on the CLASS NAME ALONE: there is no keyword gate in
        // this table (see the class doc), and the claim of one was never
        // implemented. (AC8) Rejected same-pass: Item (redundant with
        // the Resources keyword + UI-widget over-match), Jump/Objective/Mission
        // (marginal), Modifier (UInputModifier_* engine over-match). ===
        new("Health",           2, false),
        new("Damage",           2, false),

        // === Timing (L1) — canonical homes of cooldown/duration/dilation
        // fields. GameplayEffect holds effect Duration/Period; GameplayAbility
        // holds cooldown/cost tags; WorldSettings holds TimeDilation. (Ability
        // System/AttributeSet already bonused above; Weapon/Projectile carry
        // reload/charge timers and are bonused too.) Clean tokens — no core UE
        // class outside these families contains them. ===
        new("GameplayEffect",   2, false),
        new("GameplayAbility",  2, false),
        new("WorldSettings",    2, false),

        // === UNUSUAL locations (highest-value hits — devs broke convention) ===
        // LocalPlayer / GameViewportClient / HUD often store gameplay state
        // they "shouldn't" because Unreal's recommended pattern would put it
        // in PlayerState or a component. When a cheat-relevant property
        // lands here it's almost always game-specific and overlooked by
        // people grepping conventional containers.
        new("LocalPlayer",          4, true),
        new("GameViewportClient",   4, true),
        new("HUD",                  4, true),
        // "CheatManager", NOT "UCheatManager": these are UE *reflection* names
        // straight off the FName, which carry no U/A prefix — "PlayerController",
        // "LocalPlayer", "WorldSettings" above are all written that way, and
        // ConsoleViewModel.IsLikelyUCheatManagerExec matches the same substring
        // for the same reason. A "UCheatManager" row therefore matched nothing a
        // game can produce, and on the one input it COULD match it double-scored
        // (+4 for itself and +4 here) — so it is deleted, not renamed. Catches
        // the engine class and game subclasses alike: MyGameCheatManager,
        // BP_CheatManager_C. (audit #5 AC9)
        new("CheatManager",         4, true),

        // No anim/audio/FX penalties on the Property side. Rationale:
        // property NAMES alone already filter most noise (a property
        // called "PlaybackSpeed" on UAudioComponent doesn't match any
        // Stats / Combat / Resources keyword, so it naturally scores 0).
        // The substring "Anim" / "Sound" / "Particle" penalty bit us hard
        // in TowerOfMask: the game's player character class is named
        // "AnimMan_Player_C" — a perfectly cheat-relevant target whose
        // Health field was being filtered out because of the Anim penalty.
        // The Function side keeps refined COMPOUND-name penalties for the
        // function-flavoured noise (which would otherwise dominate);
        // Property side doesn't have that problem and ships penalty-free.
    };

    /// <summary>
    /// Returns (bonus, isUnusual). Sum all matching substring bonuses
    /// (same stacking semantics as <see cref="FunctionBonus"/>);
    /// <c>isUnusual = true</c> if ANY matched rule is flagged Unusual.
    /// Last-one-wins for the bool — but in practice the table is
    /// curated so no class hits more than one Unusual rule.
    /// </summary>
    public static (int Bonus, bool IsUnusual) PropertyBonus(string className)
    {
        if (string.IsNullOrEmpty(className)) return (0, false);
        var lower = className.ToLowerInvariant();
        int total = 0;
        bool unusual = false;
        foreach (var rule in PropertyRules)
        {
            if (lower.Contains(rule.Substring.ToLowerInvariant()))
            {
                total += rule.Bonus;
                if (rule.IsUnusual) unusual = true;
            }
        }
        return (total, unusual);
    }
}
