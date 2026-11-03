namespace UE5DumpUI.Helpers;

/// <summary>
/// [FINDINSTANCE-SUBSTRING-USERFACING] What the Console says about, and whether it pins, the object a class name
/// resolved to (<c>invoke_function</c>'s <c>instance_pick</c>).
/// </summary>
internal static class InvokePickText
{
    internal const string KeyObject = "str.Invoke.Pick.Object";
    internal const string KeyClassDefault = "str.Invoke.Pick.ClassDefault";
    internal const string KeyDebugCameraOwned = "str.Invoke.Pick.DebugCameraOwned";

    /// <summary>Whether the Console may keep running the class's commands on this object. A class-default pick
    /// re-resolves instead, so a CheatManager spawned later is found, and so does a Debug-Camera-owned one, so a
    /// Debug Camera destroyed later leaves no pin on it. An empty pick is an older DLL that never said: pinned, as
    /// before.</summary>
    internal static bool IsPinnable(string pick)
        => pick is "" or Constants.InvokePickLive or Constants.InvokePickAddress;

    /// <summary>What to append to the run's status line: " " and the sentence for the pick, or "" when the DLL named
    /// no object.</summary>
    internal static string Note(string pick, string name, string className)
    {
        if (pick.Length == 0 || name.Length == 0) return "";
        return " " + pick switch
        {
            // A partial-name pick that is a default object has no live object of its class either.
            Constants.InvokePickClassDefault or Constants.InvokePickPartialNameDefault => ClassDefaultText(name, className),
            Constants.InvokePickDebugCameraOwned => DebugCameraOwnedText(name, className),
            _ => ObjectText(name, className),
        };
    }

    internal const string KeyTemplateClassDefault = "str.Invoke.Template.ClassDefault";
    internal const string KeyTemplateSubobject = "str.Invoke.Template.Subobject";

    /// <summary>[INVOKE-NONSTATIC-ON-CDO] Whether to pin, given the template fact too: a template (whatever the
    /// function was) re-resolves, so a live object found later is used instead.</summary>
    internal static bool IsPinnable(string pick, string? template)
        => IsPinnable(pick) && string.IsNullOrEmpty(template);

    /// <summary>[INVOKE-NONSTATIC-ON-CDO] The Console's note. From a DLL that sends the verdict, a class-default or
    /// template warning appears only when the function is not static there; an older DLL keeps the pick-only note.</summary>
    internal static string Note(UE5DumpUI.Models.InvokeFunctionResult r)
    {
        if (r.NonStaticOnTemplate is null) return Note(r.InstancePick, r.InstanceName, r.InstanceClassName);
        if (r.InstanceName.Length == 0) return "";
        if (r.NonStaticOnTemplate == true)
        {
            if (r.InstanceTemplate == Constants.InvokeTemplateSubobject)
                return " " + TemplateSubobjectText(r.InstanceName, r.InstanceClassName, r.TemplateOwnerName);
            // The Console resolves by class name, so its class-default pick means no live object of the class exists.
            return " " + (r.InstancePick is Constants.InvokePickClassDefault or Constants.InvokePickPartialNameDefault
                ? ClassDefaultText(r.InstanceName, r.InstanceClassName)
                : TemplateClassDefaultText(r.InstanceName, r.InstanceClassName));
        }
        return " " + (r.InstancePick == Constants.InvokePickDebugCameraOwned
            ? DebugCameraOwnedText(r.InstanceName, r.InstanceClassName)
            : ObjectText(r.InstanceName, r.InstanceClassName));
    }

    /// <summary>[INVOKE-NONSTATIC-ON-CDO] The param dialog's warning line, or "" when the DLL raised none (or is older).
    /// The dialog targets an address the user chose, so neither sentence claims no live object exists.</summary>
    internal static string TemplateWarning(UE5DumpUI.Models.InvokeFunctionResult r)
    {
        if (r.NonStaticOnTemplate != true) return "";
        return r.InstanceTemplate == Constants.InvokeTemplateSubobject
            ? TemplateSubobjectText(r.InstanceName, r.InstanceClassName, r.TemplateOwnerName)
            : TemplateClassDefaultText(r.InstanceName, r.InstanceClassName);
    }

    internal static string TemplateClassDefaultText(string name, string className)
        => AobMakerStatus.Say(KeyTemplateClassDefault,
            "⚠ {0} is the class default object of {1} (a template), not a live object: a function that is not static " +
            "may do nothing there, or fail; one that does not use its object may still work", name, className);

    internal static string TemplateSubobjectText(string name, string className, string owner)
        => AobMakerStatus.Say(KeyTemplateSubobject,
            "⚠ {0} ({1}) is a template, part of {2}, not a live object: a function that is not static may do nothing " +
            "there, or fail; one that does not use its object may still work", name, className, owner);

    internal const string KeyTargetByClass = "str.Invoke.Dialog.TargetByClass";
    /// <summary>[MAINWINDOW-INVOKE-LIMIT1-CDO] The param dialog's line, before any FIRE, when it was opened on a class and
    /// no object: where each FIRE will land.</summary>
    internal static string TargetByClassText(string className)
        => AobMakerStatus.Say(KeyTargetByClass,
            "Runs on a live {0} (or a subclass), found again at each FIRE; with none live, on its class default object.",
            className);

    internal static string ObjectText(string name, string className)
        => AobMakerStatus.Say(KeyObject, "on {0} ({1})", name, className);

    // Neither sentence says the call ran: the note is on the failure line too, where nothing may have been dispatched.
    internal static string ClassDefaultText(string name, string className)
        => AobMakerStatus.Say(KeyClassDefault,
            "⚠ the object is {0}, the class default object: no live {1} exists, so an engine cheat command has no " +
            "effect there (a command that does not use its owner may still work)", name, className);

    internal static string DebugCameraOwnedText(string name, string className)
        => AobMakerStatus.Say(KeyDebugCameraOwned,
            "⚠ the object is {0} ({1}), which belongs to the Debug Camera: no other live one exists", name, className);
}
