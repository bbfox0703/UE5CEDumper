namespace UE5DumpUI.Helpers;

/// <summary>
/// [FLY-REPORTS-WISH] The Fly card's words for collision, from what the DLL read on the pawn (<c>FlyStatus.Collision</c>:
/// 1 on, 0 off, -1 not read) rather than what the noclip checkbox asked for. A pawn class without
/// SetActorEnableCollision, or a call the game thread never ran, leaves the pawn colliding under a noclip request.
/// </summary>
public static class FlyText
{
    internal const string KeyNoclipNotApplied = "str.TP.FlyNoclipNotApplied";
    internal const string KeyCollisionNotRestored = "str.TP.FlyCollisionNotRestored";
    internal const string KeyCollisionOn = "str.TP.FlyCollisionOn";
    internal const string KeyCollisionOff = "str.TP.FlyCollisionOff";

    /// <summary>The enable line's mode word: the one asked for when the pawn reads that way, else the request and what
    /// happened -- in both directions, as a pawn an earlier noclip left not colliding can stay so when its restore is
    /// refused. -1 (an older DLL) keeps the requested word.</summary>
    public static string ModeWord(bool noclipAsked, int collision)
        => noclipAsked
            ? (collision == 1 ? AobMakerStatus.Say(KeyNoclipNotApplied, "noclip asked, collision still on") : "noclip")
            : (collision == 0 ? AobMakerStatus.Say(KeyCollisionNotRestored, "noclip off, collision still off") : "collision");

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
