using System.Runtime.InteropServices;

namespace UE5DumpUI.Helpers;

/// <summary>
/// Why a dispatcher-level unhandled exception was, or was not, treated as a
/// swallowable input-layer fault. Every value except <see cref="InputLayer"/>
/// means "rethrow" — the reason is carried separately only so the log line can
/// say WHICH rule refused, which is what makes a wrong refusal diagnosable.
/// </summary>
internal enum InputFaultVerdict
{
    /// <summary>No allow-listed input-layer frame on the stack. Rethrow.</summary>
    NotInputLayer,

    /// <summary>
    /// An exception type that must never be swallowed no matter where it came
    /// from (memory corruption, a trimmed-away type, a missing native import).
    /// Rethrow.
    /// </summary>
    NeverSwallow,

    /// <summary>
    /// Our own code is on the stack, so the fault is ours even if it started in
    /// the input layer. Rethrow — this is the rule that keeps the guard from
    /// hiding UE5DumpUI bugs.
    /// </summary>
    OurCodeOnStack,

    /// <summary>
    /// The exception carries no stack text at all, so nothing can be proven
    /// about where it came from. Fail closed: rethrow.
    /// </summary>
    NoStackEvidence,

    /// <summary>
    /// Confirmed platform input-layer fault (clipboard / IME). Safe to mark
    /// handled: the keystroke does nothing instead of killing the process.
    /// </summary>
    InputLayer,
}

/// <summary>
/// Decides whether a dispatcher-level unhandled exception is a
/// <b>platform input-layer fault</b> — a failure inside Avalonia's clipboard or
/// IME plumbing that has no consequence beyond "the keystroke did nothing".
///
/// <para><b>Why this exists.</b> A failed clipboard READ inside
/// <c>TextBox.Paste()</c> surfaces through <c>Task.ThrowAsync</c> as an
/// *unobserved* exception on the Avalonia dispatcher and terminates the process
/// (field report <c>[PASTECRASH-2026-08-18]</c>: <c>COMException 0x8007000E —
/// EnumFormatEtc failed</c>, 31 minutes into a connected session, losing a loaded
/// object tree). Nothing of ours is on that stack; the consequence is entirely
/// ours. <c>Ctrl+V</c> into any text box is therefore a potential crash whenever
/// the clipboard is momentarily unreadable — another app holding it, an OLE
/// source that has gone away, a COM-level <c>E_OUTOFMEMORY</c>.</para>
///
/// <para><b>Why it is a separate, pure class.</b> The thing that must be right
/// here is the SCOPE of the swallow, and scope is a predicate — it can be
/// unit-tested with negative controls, while the dispatcher plumbing around it
/// cannot be produced headlessly. A blanket
/// <c>Dispatcher.UnhandledException → e.Handled = true</c> would make every real
/// crash invisible, which is strictly worse than the crash it prevents.</para>
///
/// <para><b>The rules, in order. All four must pass to swallow:</b></para>
/// <list type="number">
/// <item>No exception anywhere in the graph is a never-swallow type
/// (<see cref="IsNeverSwallowable"/>) — memory corruption and
/// trimming/AOT failures must always be loud, this repo especially.</item>
/// <item>There is stack text at all. No evidence ⇒ no swallow.</item>
/// <item><b>No frame belongs to us.</b> Any <c>UE5DumpUI.</c> frame means the
/// fault passed through our code and is ours to fix, so it must still crash.</item>
/// <item>A frame matches the allow-list below — a short, explicit set of Avalonia
/// clipboard / IME types, pinned against the real Avalonia assemblies by
/// <c>InputLayerFaultClassifierTests</c> so an Avalonia upgrade that renames one
/// fails a test instead of silently disabling the guard.</item>
/// </list>
///
/// <para>Rules 2–4 are applied to <b>every exception in the graph that carries a
/// stack</b>, not to their concatenation: an <c>AggregateException</c> must not be
/// able to smuggle an unrelated fault through next to a clipboard one.</para>
///
/// <para><b>Frame matching is by SUBSTRING over the stack text, deliberately.</b>
/// Parsing frames would need the <c>"at "</c> prefix, and .NET localises that word
/// (this project's own machine runs zh-TW); type names are never localised.</para>
/// </summary>
internal static class InputLayerFaultClassifier
{
    /// <summary>
    /// Any frame carrying this prefix means our own code is on the stack.
    /// Built from <see cref="Constants.AppName"/> because it IS the assembly /
    /// root-namespace name — a second spelling here could drift from it.
    /// </summary>
    private static readonly string OwnCodeMarker = Constants.AppName + ".";

    /// <summary>
    /// Avalonia TYPES whose frames identify a clipboard / IME fault. Full names as
    /// they appear in a runtime stack trace. Internal Avalonia types are fine —
    /// stack traces print them, and the pin test resolves them by reflection.
    /// </summary>
    internal static readonly string[] TypeMarkers =
    {
        // Win32 clipboard backend — the exact surface in the captured crash.
        "Avalonia.Win32.ClipboardImpl",
        // The MicroCom-generated COM proxy the clipboard reads the data object
        // through; the captured COMException was thrown in its EnumFormatEtc.
        "Avalonia.Win32.Win32Com.Impl.__MicroComIDataObjectProxy",
        // Cross-platform clipboard facade + its extension helpers. Listing the
        // shorter name would also match ClipboardExtensions by substring; both are
        // spelled out so the pin test can resolve each one individually.
        "Avalonia.Input.Platform.Clipboard",
        "Avalonia.Input.Platform.ClipboardExtensions",
        // Win32 IME. The other input surface that can fail on a foreign input
        // state, and this project's own dev machine runs an IME by default.
        "Avalonia.Win32.Input.Imm32InputMethod",
    };

    /// <summary>
    /// Avalonia METHODS (type + method name) that are clipboard entry points. The
    /// three <see cref="Avalonia.Controls.TextBox"/> clipboard commands are the
    /// user-visible triggers: Ctrl+X / Ctrl+C / Ctrl+V.
    /// </summary>
    internal static readonly string[] MethodMarkers =
    {
        "Avalonia.Controls.TextBox.Cut",
        "Avalonia.Controls.TextBox.Copy",
        "Avalonia.Controls.TextBox.Paste",
    };

    /// <summary>Bound on the exception graph walk — a cycle or a pathological
    /// AggregateException must not turn a crash handler into a hang.</summary>
    private const int MaxExceptions = 32;

    /// <summary>Bound on inner-exception nesting depth.</summary>
    private const int MaxDepth = 8;

    /// <summary>
    /// Classify a dispatcher fault. Returns <see cref="InputFaultVerdict.InputLayer"/>
    /// only when every rule in the class summary passes.
    /// </summary>
    /// <param name="ex">The exception the dispatcher is about to rethrow.</param>
    /// <param name="detail">Always set: the rule that decided, for the log line.</param>
    internal static InputFaultVerdict Classify(Exception? ex, out string detail)
    {
        if (ex is null)
        {
            detail = "no exception";
            return InputFaultVerdict.NotInputLayer;
        }

        var pending = new Stack<(Exception Ex, int Depth)>();
        pending.Push((ex, 0));
        int visited = 0;
        int withStack = 0;
        string acceptedDetail = "";

        while (pending.Count > 0)
        {
            if (visited >= MaxExceptions)
            {
                // Truncated walk: an unexamined inner exception could be a
                // never-swallow type, so refuse rather than guess.
                detail = $"exception graph larger than {MaxExceptions} — not classified";
                return InputFaultVerdict.NotInputLayer;
            }

            var (cur, depth) = pending.Pop();
            visited++;

            if (IsNeverSwallowable(cur))
            {
                detail = "never-swallow exception type: " + cur.GetType().FullName;
                return InputFaultVerdict.NeverSwallow;
            }

            // EVERY exception in the graph that carries a stack must independently
            // be input-layer — not "at least one of them", which would let an
            // AggregateException smuggle an unrelated fault through alongside a
            // clipboard one. Exceptions with no stack (a wrapper constructed but
            // never thrown) carry no evidence either way and are skipped.
            var st = cur.StackTrace;
            if (!string.IsNullOrEmpty(st))
            {
                withStack++;
                var one = ClassifyStackText(st, out var oneDetail);
                if (one != InputFaultVerdict.InputLayer)
                {
                    detail = $"{cur.GetType().Name}: {oneDetail}";
                    return one;
                }
                acceptedDetail = oneDetail;
            }

            if (depth >= MaxDepth)
            {
                // Same reasoning as the MaxExceptions bound: what we did not look
                // at could be a never-swallow type, so a truncated chain is a
                // refusal, not an acceptance.
                bool moreBelow = cur is AggregateException deep
                    ? deep.InnerExceptions.Count > 0
                    : cur.InnerException is not null;
                if (moreBelow)
                {
                    detail = $"inner-exception chain deeper than {MaxDepth} — not classified";
                    return InputFaultVerdict.NotInputLayer;
                }
                continue;
            }

            if (cur is AggregateException agg)
            {
                foreach (var inner in agg.InnerExceptions)
                    pending.Push((inner, depth + 1));
            }
            else if (cur.InnerException is { } ie)
            {
                pending.Push((ie, depth + 1));
            }
        }

        if (withStack == 0)
        {
            detail = "no stack evidence";
            return InputFaultVerdict.NoStackEvidence;
        }

        detail = acceptedDetail;
        return InputFaultVerdict.InputLayer;
    }

    /// <summary>
    /// The stack half of the decision for ONE exception, split out so it can be
    /// tested against the exact trace captured in the field without needing to
    /// reproduce the throw.
    /// </summary>
    internal static InputFaultVerdict ClassifyStackText(string? stackText, out string detail)
    {
        if (string.IsNullOrWhiteSpace(stackText))
        {
            detail = "no stack evidence";
            return InputFaultVerdict.NoStackEvidence;
        }

        if (stackText.Contains(OwnCodeMarker, StringComparison.Ordinal))
        {
            detail = "our own code is on the stack (" + OwnCodeMarker + ")";
            return InputFaultVerdict.OurCodeOnStack;
        }

        foreach (var marker in TypeMarkers)
        {
            if (stackText.Contains(marker, StringComparison.Ordinal))
            {
                detail = "input-layer frame: " + marker;
                return InputFaultVerdict.InputLayer;
            }
        }

        foreach (var marker in MethodMarkers)
        {
            if (stackText.Contains(marker, StringComparison.Ordinal))
            {
                detail = "input-layer frame: " + marker;
                return InputFaultVerdict.InputLayer;
            }
        }

        detail = "no input-layer frame on the stack";
        return InputFaultVerdict.NotInputLayer;
    }

    /// <summary>
    /// Exception types that must ALWAYS reach the crash handler, whatever the
    /// stack says. Two families:
    /// <list type="bullet">
    /// <item><b>The process is no longer trustworthy</b> — OOM, stack overflow,
    /// an access violation, a raw SEH exception escaping native code. Continuing
    /// after one of these produces a second, unrelated failure later that nobody
    /// can trace back.</item>
    /// <item><b>Trimming / Native-AOT damage</b> — a type that failed to load, a
    /// missing member, a bad image, a failed static constructor. Every AOT bug in
    /// this repo's history was found by it failing loudly; swallowing one inside
    /// Avalonia's clipboard code would hide exactly the class of defect the AOT
    /// build exists to surface.</item>
    /// </list>
    /// Note <see cref="COMException"/> is deliberately NOT here even when its
    /// HRESULT is <c>E_OUTOFMEMORY</c> (0x8007000E, the captured case): that is a
    /// COM peer reporting ITS memory state, not this process running out.
    /// </summary>
    private static bool IsNeverSwallowable(Exception e) =>
        e is OutOfMemoryException            // + InsufficientMemoryException
          or StackOverflowException
          or AccessViolationException
          or SEHException                    // raw native fault; NOT COMException's base path
          or BadImageFormatException
          or TypeInitializationException
          or TypeLoadException               // + DllNotFoundException, EntryPointNotFoundException
          or MissingMemberException          // + MissingMethodException, MissingFieldException
          or InvalidProgramException;
}
