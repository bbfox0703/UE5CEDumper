namespace UE5DumpUI.Helpers;

/// <summary>
/// [FLY-REPORTS-WISH] The Fly card's words for collision, from what the DLL read on the pawn (<c>FlyStatus.Collision</c>:
/// 1 on, 0 off, -1 not read) rather than what the noclip checkbox asked for. A pawn class without
/// SetActorEnableCollision, or a call the game thread never ran, leaves the pawn colliding under a noclip request.
/// </summary>
public static class FlyText
{
    internal const string KeyNoclipNotApplied = "str.TP.FlyNoclipNotApplied";
    internal const string KeyCollisionOn = "str.TP.FlyCollisionOn";
    internal const string KeyCollisionOff = "str.TP.FlyCollisionOff";

    /// <summary>The enable line's mode word: "collision" when noclip was not asked for, "noclip" unless the pawn
    /// reads colliding, and then the request and what happened. -1 (an older DLL) keeps the old word.</summary>
    public static string ModeWord(bool noclipAsked, int collision)
        => !noclipAsked ? "collision"
         : collision == 1 ? AobMakerStatus.Say(KeyNoclipNotApplied, "noclip asked, collision still on")
         : "noclip";

    /// <summary>Does the collision read on the pawn agree with the noclip request? An unread collision (-1, or an older
    /// DLL) has nothing to wait for.</summary>
    public static bool CollisionAgrees(bool noclipAsked, int collision)
        => collision < 0 || collision == (noclipAsked ? 0 : 1);

    /// <summary>The readout's clause, or "" when the collision was not read.</summary>
    public static string CollisionClause(int collision) => collision switch
    {
        1 => AobMakerStatus.Say(KeyCollisionOn, ", collision on"),
        0 => AobMakerStatus.Say(KeyCollisionOff, ", collision off"),
        _ => "",
    };
}
