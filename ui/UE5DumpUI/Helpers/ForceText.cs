namespace UE5DumpUI.Helpers;

/// <summary>
/// [LISTINSTANCES-KEEPS-TEMPLATES] The Force (Solide) status line's note when a hold is armed on nothing because every
/// object of the class is a template -- a class default object, one of its default subobjects, a Blueprint component
/// template -- which a Force never writes: whatever a template holds, every later spawn copies. One wording for the
/// Property Search and Teleport cards.
/// </summary>
internal static class ForceText
{
    internal const string KeyTemplatesOnly = "str.Force.TemplatesOnly";

    /// <summary>Appended to the ARMED line when the DLL passed over <paramref name="templates"/> templates and held
    /// nothing.</summary>
    internal static string TemplatesOnlyNote(string className, int templates) => AobMakerStatus.Say(KeyTemplatesOnly,
        "Only templates of {0} exist right now ({1}: class default objects and their subobjects), and a Force never "
        + "writes a template, since every later spawn copies it. To change one anyway, edit its address from the "
        + "Instance Finder.", className, templates);
}
