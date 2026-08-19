using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Categories surfaced by the Interesting Functions Finder. Picked
/// from a Cheat-Engine-user perspective: which UE function would I
/// hook / call to alter common gameplay state?
///
/// One function gets exactly one category -- the one with the highest
/// total keyword hit score (ties broken by table order: Stats &gt;
/// Inventory &gt; Movement &gt; Combat &gt; Utility &gt; GameplayAction &gt;
/// Other). Keeps the UI chip filter unambiguous; multi-tag UI can come
/// later if needed.
/// </summary>
public enum FunctionCategory
{
    Other,      // Default -- no keyword hits, or score below threshold
    Stats,      // HP/MP/SP/XP/Level/Damage/Heal/Score
    Inventory,  // Gold/Money/Item/Loot/Equip
    Movement,   // Teleport/Walk/Speed/NoClip/Fly
    Combat,     // Attack/Fire/Cast/Ability
    Utility,    // Save/Load/Spawn/Timer/Cheat/Console
    GameplayAction, // OPT-IN (default off): Dash/Dodge/Interact/Open/Buy/Sell/Shop/Vendor/Trade
}

/// <summary>
/// Keyword + class + flag heuristic that ranks UFunctions by
/// "interesting-to-cheat-engine-user" probability. All scoring is
/// hardcoded here (no settings.json hooks yet) so a single static
/// review tells you exactly what shows up in the finder UI.
///
/// Add a keyword:
///   1. Pick the right category bucket below.
///   2. Append to its array (case doesn't matter -- comparison is
///      case-insensitive).
///   3. Add a unit test in KeywordScoringTableTests for the new
///      keyword (ensures coverage doesn't regress when bucket
///      arrays get reshuffled).
///
/// Categories use substring matching across the FUNCTION name and
/// the CLASS name (e.g. "AddMoney" hits Inventory via 'Money'; a
/// "Damage" function on PlayerCharacter hits Stats via 'Damage' AND
/// gets the Character class bonus).
/// </summary>
public static class KeywordScoringTable
{
    /// <summary>Score threshold: rows with FinalScore &lt; this are
    /// dropped from default view (visible only when "Show all" is
    /// toggled in the UI).</summary>
    public const int InterestingThreshold = 5;

    // ------------------------------------------------------------------
    // Keyword tables (per category). Substring matched against
    // funcName + className, case-insensitive.
    // ------------------------------------------------------------------

    // Tokenisation history (build 597-607 -> build 608+):
    //
    // Original substring matching false-positived everywhere short
    // acronyms appeared inside common engine words:
    //   "Component" contained "mp" -> Stats false hit
    //   "Spawn" contained "sp"     -> Stats false hit
    //   "GetTPSStream" contained "tp" -> Movement false hit
    // Resolution at the time was to drop HP/MP/SP/XP/TP/Drop/Time/Clock/
    // Create/Destroy entirely.
    //
    // After switching to KeywordTokenizer (whole-token match against
    // tokens like "Component" / "Spawn" / "Stream"), short acronyms are
    // safe again -- "MP" only matches when it appears as its own
    // token (GetMP / PlayerMP / SetMaxMP), never as a buried letter
    // pair. The dropped keywords are restored below; comments record
    // which ones came back and why.

    /// <summary>Per-hit score for each Stats keyword.</summary>
    public const int StatsKeywordScore = 5;
    public static readonly string[] StatsKeywords =
    {
        // Health / mana / stamina / energy. Short acronyms (HP/MP/SP)
        // restored post-tokenisation -- safe because tokens are matched
        // whole, not as substrings of "Component"/"Spawn"/etc.
        //
        // "Hp" used to sit beside "HP" here. It is a leftover from the
        // pre-tokenisation era: KeywordTokenizer lowercases every token it
        // emits, so BOTH spellings tokenise to exactly ["hp"] and
        // CountTokenHits scored the SAME keyword twice -- +5 on every
        // HP-named row over every other stat keyword, in the very column
        // the panel sorts on, with the tooltip rendering "keywords(2 hits)"
        // for one keyword. That contradicts CountTokenHits' own documented
        // contract ("Each keyword counts once total"). Case variants are
        // never needed here; SPELLING variants (Defense/Defence,
        // Coin/Coins) still are. (audit #5 Z13)
        "HP", "Health", "MP", "Mana", "SP", "Stamina", "Energy",
        // Experience / level / score. XP restored.
        "XP", "Exp", "Experience", "Level", "Score",
        // Combat-stat verbs (still primarily affect a stat field).
        "Damage", "Heal", "Hurt", "Kill", "Revive", "Death",
    };

    public const int InventoryKeywordScore = 5;
    public static readonly string[] InventoryKeywords =
    {
        "Gold", "Money", "Coin", "Currency", "Cash", "Credit",
        "Item", "Inventory", "Pickup", "Loot", "Drop", "Equip",
        "Wallet", "Stack",
        // 'Drop' restored -- token-based match means DropItem ->
        // ["Drop","Item"] hits Inventory cleanly without false-positiving
        // "DropDownList" (which tokenises to ["Drop","Down","List"] --
        // OK, that one DOES match. UI penalty offsets it though.)
    };

    public const int MovementKeywordScore = 5;
    public static readonly string[] MovementKeywords =
    {
        // TP restored -- only matches "TP" as a standalone token (rare
        // in cheat-relevant func names but legit when present).
        "Teleport", "Warp", "TP",
        "Move", "Location",  // multi-token "SetLocation"/"SetActorLocation"
                              // collapsed to the meaningful single token
        "Speed", "Velocity", "Walk", "Sprint", "Jump",
        // 'Run' still dropped -- even tokenised, "Run" appears in
        // RunCallback/RunGameMode plumbing too often to be cheat-relevant.
    };

    /// <summary>
    /// Explicit movement-cheat verbs. Higher per-hit weight (8) than
    /// regular Movement (5) so a NoClip function on DebugCheatManager
    /// stays in Movement instead of being pulled into Utility by the
    /// "Cheat" + "Debug" class-name keywords. Categorised as Movement.
    /// </summary>
    public const int ExplicitCheatScore = 8;
    public static readonly string[] ExplicitMovementCheats =
    {
        "NoClip", "Fly", "God", "Ghost", "Invincible", "Invisible",
    };

    public const int CombatKeywordScore = 4;
    public static readonly string[] CombatKeywords =
    {
        "Attack", "Fire", "Shoot", "Cast",
        "Ability", "Skill", "Buff", "Debuff",
        "Reload", "Aim", "Block", "Parry",
    };

    public const int UtilityKeywordScore = 3;
    public static readonly string[] UtilityKeywords =
    {
        "Save", "Load", "Checkpoint",
        "Spawn", "Summon",
        // Create/Destroy still dropped -- even tokenised they fire on
        // engine plumbing (CreateWidget / DestroyComponent / etc) far
        // more often than cheat-relevant code. Use Spawn/Summon for
        // gameplay-actor creation instead.
        "Timer", "Time", "Clock", "Countdown",
        // Time/Clock restored -- "Time" only fires on actual timing
        // tokens now (TimeRemaining, ClockTick) not on "Lifetime" etc.
        // L1 timer methods: cooldown/dilation getters+setters that the
        // "Timer"/"Time" tokens above miss (GetCooldownRemaining,
        // SetGlobalTimeDilation, ResetRecharge, DelayedCall). Whole-token,
        // so "Dilation" only fires standalone; all additive to Utility.
        "Cooldown", "Dilation", "Delay", "Interval", "Elapsed", "Recharge",
        "Cheat", "Debug", "Console", "Toggle",
    };

    /// <summary>Per-hit score for each Gameplay-Action keyword. Same
    /// weight as Stats/Inventory/Movement (5) so a single clean action
    /// verb clears the InterestingThreshold on its own.</summary>
    public const int GameplayActionKeywordScore = 5;

    /// <summary>
    /// OPT-IN "gameplay action" verbs — scored ONLY when the caller
    /// passes <c>includeGameplayActions: true</c> (wired to the
    /// Interesting Functions panel's "Gameplay Actions" checkbox, which
    /// defaults OFF). These are the character-control / world-interaction
    /// / shop verbs the cheat-oriented buckets above deliberately omit:
    /// Dash/Dodge/Interact/Open/Buy/Sell/Shop/Vendor/Trade all score 0
    /// without this pack, so a plain <c>OpenShop()</c> or <c>Dash()</c>
    /// never clears the threshold and stays hidden.
    ///
    /// Only NEW tokens live here — verbs already in Movement
    /// (Jump/Move/Walk/Sprint) or Combat (Attack/Fire) are intentionally
    /// NOT duplicated, because <see cref="Score"/> sums EVERY bucket into
    /// the final score and a duplicate would double-count.
    ///
    /// Whole-token matching (<see cref="CountTokenHits"/>) means every
    /// entry is a single token: <c>OpenShop</c> tokenises to
    /// ["open","shop"] and hits both "Open" and "Shop" here (score 10).
    /// Noisier common words (Use/Open/Store) are included anyway — the
    /// pack being opt-in means enabling it deliberately trades precision
    /// for recall. Tie note: verbs that also hit a built-in bucket keep
    /// that bucket's label (e.g. <c>SellItem</c> ties Sell↔Item at 5 →
    /// stays Inventory since GameplayAction is lowest tie priority) but
    /// still gain the extra points, so they surface.
    /// </summary>
    public static readonly string[] GameplayActionKeywords =
    {
        // Traversal / movement actions absent from the Movement bucket.
        "Dash", "Dodge", "Roll", "Slide", "Climb", "Vault", "Glide", "Grapple", "Crouch",
        // World interaction.
        "Interact", "Use", "Grab", "Activate", "Talk", "Open",
        // Shop / economy / trade (the "open shop UI + sell in the open world" goal).
        "Buy", "Sell", "Shop", "Vendor", "Merchant", "Trade", "Purchase", "Store",
    };

    // ------------------------------------------------------------------
    // Class-name boosts moved to ClassLocationScorer.FunctionBonus so
    // both Function side and Property side (B' / Interesting Properties)
    // share the same bag-of-substrings pattern. Property side has its
    // OWN table because the relevant noise / signal classes differ —
    // e.g. LocalPlayer / GameViewportClient / HUD are "unusual but
    // valuable" for properties but irrelevant for functions.
    // ------------------------------------------------------------------

    // ------------------------------------------------------------------
    // FunctionFlag boosts. Read from AllFunctionEntry projections so
    // the helper stays decoupled from raw flag bits.
    // ------------------------------------------------------------------

    private const int BlueprintCallableBonus = 2;  // user-facing exposed surface
    private const int BlueprintEventBonus    = 1;  // BP-overridable event
    private const int SafeGetterBonus        = 1;  // pure/const + 0-1 params -> easy 1-click test
    private const int LargeParmsSizePenalty  = -1; // ParmsSize > 64 -> annoying to call
    private const int LargeParmsSizeThreshold= 64;

    /// <summary>Result of scoring one entry.</summary>
    public readonly record struct ScoreResult(
        int FinalScore,
        FunctionCategory Category,
        int KeywordHits,
        int ClassBonus,
        int FlagBonus);

    /// <summary>
    /// Score one function entry. Returns a tuple of (finalScore,
    /// category, breakdown) so the UI can show a tooltip explaining
    /// why a row is interesting.
    /// </summary>
    /// <param name="includeGameplayActions">Opt-in: when true, also score
    /// the <see cref="GameplayActionKeywords"/> pack (Dash/Interact/Open/
    /// Buy/Sell/Shop/…). Defaults false so the built-in categorisation is
    /// byte-identical to before this pack existed — the panel's "Gameplay
    /// Actions" checkbox flips it on.</param>
    public static ScoreResult Score(AllFunctionEntry entry, bool includeGameplayActions = false)
    {
        // Tokenise the function + class names once. The tokeniser already
        // emits lowercased tokens so keyword comparison is case-blind by
        // construction. Union of both name's tokens is the search domain
        // for keyword hits -- that way a match in either name counts
        // (e.g. function "AddCash" on class "Inventory" hits Inventory
        // twice via "Cash" and "Inventory").
        var tokens = KeywordTokenizer.TokenizeAsSet(entry.FuncName);
        foreach (var t in KeywordTokenizer.Tokenize(entry.ClassName))
            tokens.Add(t);

        // Keyword pass: tally hits per category, pick the winner.
        int statsHits     = CountTokenHits(tokens, StatsKeywords);
        int inventoryHits = CountTokenHits(tokens, InventoryKeywords);
        int movementHits  = CountTokenHits(tokens, MovementKeywords);
        int cheatHits     = CountTokenHits(tokens, ExplicitMovementCheats);
        int combatHits    = CountTokenHits(tokens, CombatKeywords);
        int utilityHits   = CountTokenHits(tokens, UtilityKeywords);
        // Opt-in pack: only tallied when the caller enables it, so the
        // default path scores exactly as it did before this pack existed.
        int gameplayHits  = includeGameplayActions
                          ? CountTokenHits(tokens, GameplayActionKeywords) : 0;

        int statsScore     = statsHits     * StatsKeywordScore;
        int inventoryScore = inventoryHits * InventoryKeywordScore;
        // Movement folds in explicit-cheat hits at the higher per-hit weight.
        int movementScore  = movementHits  * MovementKeywordScore
                           + cheatHits     * ExplicitCheatScore;
        int combatScore    = combatHits    * CombatKeywordScore;
        int utilityScore   = utilityHits   * UtilityKeywordScore;
        int gameplayScore  = gameplayHits  * GameplayActionKeywordScore;

        // Pick highest-scoring category; ties broken by enum order
        // (Stats > Inventory > Movement > Combat > Utility > GameplayAction).
        var category = FunctionCategory.Other;
        int catScore = 0;
        int totalKeywordHits =
            statsHits + inventoryHits + movementHits + cheatHits + combatHits
            + utilityHits + gameplayHits;

        if (statsScore > catScore)     { catScore = statsScore;     category = FunctionCategory.Stats; }
        if (inventoryScore > catScore) { catScore = inventoryScore; category = FunctionCategory.Inventory; }
        if (movementScore > catScore)  { catScore = movementScore;  category = FunctionCategory.Movement; }
        if (combatScore > catScore)    { catScore = combatScore;    category = FunctionCategory.Combat; }
        if (utilityScore > catScore)   { catScore = utilityScore;   category = FunctionCategory.Utility; }
        // GameplayAction last -> lowest tie priority: a verb that also
        // hits a built-in bucket (e.g. SellItem: Sell↔Item) keeps that
        // bucket's label but still gains the extra points.
        if (gameplayScore > catScore)  { catScore = gameplayScore;  category = FunctionCategory.GameplayAction; }

        // Class bonus delegated to shared ClassLocationScorer so the
        // identical stacking semantics ("AnimCharacter" = Anim + Character)
        // are also available to the Property-side scorer.
        int classBonus = ClassLocationScorer.FunctionBonus(entry.ClassName);

        // Flag bonus
        int flagBonus = 0;
        if (entry.IsBlueprintCallable) flagBonus += BlueprintCallableBonus;
        if (entry.IsBlueprintEvent)    flagBonus += BlueprintEventBonus;
        if ((entry.IsBlueprintPure || entry.IsConst) && entry.NumParms <= 1)
            flagBonus += SafeGetterBonus;
        if (entry.ParmsSize > LargeParmsSizeThreshold)
            flagBonus += LargeParmsSizePenalty;

        // Sum across keyword + class + flag layers. All-keyword-hits-
        // sum (vs single-category picked score) means a function that
        // matches BOTH Stats AND Inventory gets credit for both even
        // though only one category label shows.
        int keywordSum = statsScore + inventoryScore + movementScore +
                         combatScore + utilityScore + gameplayScore;
        int finalScore = keywordSum + classBonus + flagBonus;

        return new ScoreResult(
            FinalScore:    finalScore,
            Category:      category,
            KeywordHits:   totalKeywordHits,
            ClassBonus:    classBonus,
            FlagBonus:     flagBonus);
    }

    /// <summary>
    /// Count distinct keywords whose tokens all appear in
    /// <paramref name="tokens"/>. Replaces the v1 substring-matching
    /// <c>CountHits</c>.
    ///
    /// Multi-token keywords are common (e.g. "NoClip" tokenises to
    /// ["no","clip"], "SetActorLocation" to ["set","actor","location"])
    /// so the match is a subset check: ALL keyword tokens must be
    /// present in the function/class token set, in any order. That
    /// means "EnableNoClip" -> ["enable","no","clip"] still matches
    /// keyword "NoClip" because {"no","clip"} ⊆ {"enable","no","clip"}.
    ///
    /// Each keyword counts once total even if multiple tokens would
    /// match (e.g. a function "AddHealthHealth" only credits "Health"
    /// once -- avoids over-rewarding repetitive names).
    /// </summary>
    private static int CountTokenHits(HashSet<string> tokens, string[] keywords)
    {
        int hits = 0;
        foreach (var k in keywords)
        {
            // Re-tokenise the keyword (cheap; keyword count is ~40
            // total across all buckets). Result is already lowercased
            // by the tokeniser so direct HashSet lookup is fine.
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

    /// <summary>Display label for a category (shown in the chip filter
    /// dropdown + as a column value).</summary>
    public static string DisplayName(FunctionCategory cat) => cat switch
    {
        FunctionCategory.Stats     => "Stats",
        FunctionCategory.Inventory => "Inventory",
        FunctionCategory.Movement  => "Movement",
        FunctionCategory.Combat    => "Combat",
        FunctionCategory.Utility   => "Utility",
        FunctionCategory.GameplayAction => "Gameplay",
        _                          => "Other",
    };

    /// <summary>Foreground hex colour for the category chip in the
    /// DataGrid -- matches the existing UI palette (DCDCAA / 4EC9B0
    /// / 569CD6 / etc.).</summary>
    public static string CategoryColor(FunctionCategory cat) => cat switch
    {
        FunctionCategory.Stats     => "#E07B7B", // soft red -- combat/HP
        FunctionCategory.Inventory => "#DCDCAA", // gold -- money/loot
        FunctionCategory.Movement  => "#7FB6E8", // sky -- teleport/move
        FunctionCategory.Combat    => "#E0A050", // orange -- attack/skill
        FunctionCategory.Utility   => "#B280D9", // purple -- save/spawn
        FunctionCategory.GameplayAction => "#5FBF7F", // green -- action/interact/shop
        _                          => "#808080", // grey -- other
    };
}
