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
/// <item><b>No frame belongs to us.</b> Any <c>UE5DumpUI.</c> frame — or any frame
/// from code GENERATED into our assembly, see
/// <see cref="GeneratedOwnCodeMarkers"/> — means the fault passed through our code
/// and is ours to fix, so it must still crash.</item>
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
    /// Namespaces of code that is GENERATED INTO our assembly and therefore does
    /// not carry the <see cref="OwnCodeMarker"/> prefix. Rule 3 ("no frame belongs
    /// to us") reads the root namespace, so without these a fault raised inside a
    /// compiled XAML binding would look like nobody's code and could be swallowed
    /// under a clipboard-topped stack.
    ///
    /// <para>Measured, not guessed: the shipped assembly holds <b>47</b> types
    /// outside <c>UE5DumpUI.*</c>, 35 of them under <c>CompiledAvaloniaXaml</c> —
    /// <c>XamlIlTrampolines</c> and <c>XamlDynamicSetters</c> (the compiled-binding
    /// property setters, where a binding type mismatch raises
    /// <see cref="InvalidCastException"/>), plus <c>XamlIlContext</c>,
    /// <c>!XamlLoader</c> and the per-view <c>!AvaloniaResources</c> loaders.</para>
    ///
    /// <para>This list only ever makes the guard REFUSE more, never swallow more —
    /// it is an extension of the "our code is on the stack" rule, checked before the
    /// input-layer allow-list. That is why an ambiguous owner is fine:
    /// <c>CompiledAvaloniaXaml</c> also exists inside Avalonia's own themed
    /// assemblies, and a fault in XAML-compiled code is application-level either
    /// way — never the platform clipboard/IME plumbing this guard is scoped to.</para>
    ///
    /// <para>Deliberately NOT listed, though also generated into this assembly:
    /// <c>&lt;PrivateImplementationDetails&gt;</c>,
    /// <c>&lt;&gt;z__ReadOnlySingleElementList</c> and
    /// <c>CommunityToolkit.Mvvm.ComponentModel.__Internals</c>. Those names are
    /// emitted verbatim into MANY assemblies (the BCL's and Avalonia's included), so
    /// a frame carrying one is not evidence about WHOSE code failed — and our own
    /// generated property setters already appear as <c>UE5DumpUI.…set_X</c>.</para>
    /// </summary>
    internal static readonly string[] GeneratedOwnCodeMarkers =
    {
        "CompiledAvaloniaXaml.",
    };

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
    /// Avalonia METHODS (type + method name) that are clipboard entry points — the
    /// user-visible triggers Ctrl+X / Ctrl+C / Ctrl+V.
    ///
    /// <para><b><c>TextBox.Cut</c> is here as a JUDGED TRADE, not by symmetry with
    /// the other two, and the difference is real.</b> It is the only one of the three
    /// that mutates state around its await, so swallowing its fault does not degrade
    /// quite to "the keystroke did nothing". Read out of the installed Avalonia
    /// 12.1.1 IL rather than assumed (<c>TextBox+&lt;Cut&gt;d__233.MoveNext</c>):</para>
    /// <code>
    /// IL_0047 call TextBox.SnapshotUndoRedo          &lt;- BEFORE the await
    /// IL_0069 call ClipboardExtensions.SetTextAsync
    /// IL_0098 call AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted
    /// IL_00BE call TaskAwaiter.GetResult             &lt;- a clipboard failure throws HERE
    /// IL_00C4 call TextBox.DeleteSelection           &lt;- never runs
    /// </code>
    ///
    /// <para><b>Why that is still worth swallowing.</b> The residue is one undo
    /// snapshot with no edit behind it — taken before the await, of text that then
    /// never changed, so undoing it restores the state it already has and reads as a
    /// no-op Ctrl+Z. Weigh that against the alternative, which is not "clean state":
    /// it is the process terminating and taking a connected session with a loaded
    /// object tree, which is the entire defect this class exists for. The mild,
    /// local, recoverable inconsistency wins.</para>
    ///
    /// <para>The second reason is consistency of behaviour under one environmental
    /// condition. A busy clipboard makes Ctrl+X, Ctrl+C and Ctrl+V fail together and
    /// for the same reason; having two of them shrug and the third close the app
    /// would be indefensible from the user's side, and would make the failure look
    /// like a defect in whichever key they happened to press.</para>
    ///
    /// <para>The IL above stays because it is the reason this is a DECISION: it also
    /// clears the other two outright — <c>Copy</c> mutates nothing at all, and
    /// <c>Paste</c>'s <c>SnapshotUndoRedo</c> is at IL_0106, AFTER its
    /// <c>GetResult</c> at IL_00AD, so a failed read throws before any state changes.
    /// If Cut ever grows a mutation whose undo is NOT a no-op — anything touching the
    /// document, the selection or the clipboard's own contents — re-run this read and
    /// re-take the decision, because the trade above is what would have changed.</para>
    /// </summary>
    internal static readonly string[] MethodMarkers =
    {
        "Avalonia.Controls.TextBox.Cut",
        "Avalonia.Controls.TextBox.Copy",
        "Avalonia.Controls.TextBox.Paste",
    };

    /// <summary>
    /// The same methods as <see cref="MethodMarkers"/>, spelled the way a stack
    /// trace renders them when the async state machine is NOT unwrapped back into
    /// its original method name — <c>TextBox.&lt;Paste&gt;d__235.MoveNext()</c>.
    ///
    /// <para><b>Why this is not paranoia.</b> Unwrapping <c>MoveNext</c> into
    /// <c>Paste</c> costs the formatter a reflection walk over the declaring type
    /// looking for the method whose <c>[AsyncStateMachine]</c> names this class.
    /// That walk is exactly the shape Native AOT trims away, and this app SHIPS as
    /// a Native-AOT trimmed binary — so the plain markers can match in every test
    /// run (CoreCLR, unwrapped) and match nothing at all in the build users
    /// actually run. All three clipboard entry points are <c>async void</c>
    /// (verified: their builders are <c>AsyncVoidMethodBuilder</c>), so every one of
    /// them is exposed to this.</para>
    ///
    /// <para>Two spellings per method because the two renderings differ in how the
    /// nested state-machine type is joined to its declaring type: CoreCLR's
    /// <c>StackTrace</c> does <c>FullName.Replace('+', '.')</c>, but the name is
    /// <c>Avalonia.Controls.TextBox+&lt;Paste&gt;d__235</c> to reflection and to any
    /// formatter that does not do that replacement.</para>
    /// </summary>
    internal static readonly string[] StateMachineMethodMarkers =
        BuildStateMachineMarkers(MethodMarkers);

    /// <summary>Turn <c>Ns.Type.Method</c> into the <c>Ns.Type.&lt;Method&gt;d__</c>
    /// and <c>Ns.Type+&lt;Method&gt;d__</c> renderings. The trailing <c>d__</c> is
    /// kept (without the ordinal, which changes on any Avalonia edit) so the marker
    /// cannot match an ordinary method that merely starts with the same name.</summary>
    private static string[] BuildStateMachineMarkers(string[] methodMarkers)
    {
        var built = new string[methodMarkers.Length * 2];
        for (int i = 0; i < methodMarkers.Length; i++)
        {
            var marker = methodMarkers[i];
            int cut = marker.LastIndexOf('.');
            string type = marker[..cut];
            string method = marker[(cut + 1)..];
            built[i * 2] = $"{type}.<{method}>d__";
            built[i * 2 + 1] = $"{type}+<{method}>d__";
        }
        return built;
    }

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

        // Code generated INTO our assembly counts as ours too, and it does not carry
        // the root-namespace prefix — see GeneratedOwnCodeMarkers. Checked here, with
        // the own-code rule and BEFORE the allow-list, so it can only ever refuse.
        foreach (var marker in GeneratedOwnCodeMarkers)
        {
            if (stackText.Contains(marker, StringComparison.Ordinal))
            {
                detail = "generated code from our own assembly is on the stack (" + marker + ")";
                return InputFaultVerdict.OurCodeOnStack;
            }
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
            if (ContainsWholeMethodName(stackText, marker))
            {
                detail = "input-layer frame: " + marker;
                return InputFaultVerdict.InputLayer;
            }
        }

        // Same methods, as an un-unwrapped async state machine — what the shipped
        // Native-AOT build is liable to print instead of the plain name.
        foreach (var marker in StateMachineMethodMarkers)
        {
            if (stackText.Contains(marker, StringComparison.Ordinal))
            {
                detail = "input-layer frame (state machine): " + marker;
                return InputFaultVerdict.InputLayer;
            }
        }

        detail = "no input-layer frame on the stack";
        return InputFaultVerdict.NotInputLayer;
    }

    /// <summary>
    /// Substring match for a METHOD marker, refusing a hit that runs on into a
    /// longer identifier: <c>TextBox.Paste</c> must match <c>TextBox.Paste()</c> but
    /// NOT a hypothetical <c>TextBox.PasteHistoryItem()</c>.
    ///
    /// <para>Found by a negative control, not by review — the plain
    /// <c>string.Contains</c> this replaces accepted the longer name, which would
    /// have quietly widened the swallow to a method nobody had reasoned about. The
    /// boundary is the only tightening that cannot backfire: requiring a literal
    /// <c>"("</c> instead would DISABLE the marker for any renderer that formats
    /// frames without a parameter list, and a silently disabled guard is the exact
    /// failure this whole class is written against.</para>
    ///
    /// <para>Deliberately not used for <see cref="TypeMarkers"/> (a type name is
    /// legitimately a prefix of its own nested / generic spellings) nor for
    /// <see cref="StateMachineMethodMarkers"/> (which END in <c>d__</c> and are
    /// always followed by the compiler's ordinal digits, so a boundary test there
    /// would reject every real match).</para>
    /// </summary>
    private static bool ContainsWholeMethodName(string stackText, string marker)
    {
        int at = 0;
        while ((at = stackText.IndexOf(marker, at, StringComparison.Ordinal)) >= 0)
        {
            int after = at + marker.Length;
            if (after >= stackText.Length || !IsIdentifierChar(stackText[after]))
                return true;
            at = after;
        }
        return false;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

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
    ///
    /// <para><b>Internal, not private</b>, because the same question is asked at the
    /// other end of the clipboard: <c>WindowsPlatformService.CopyToClipboardAsync</c>
    /// catches a failed WRITE so the Copy buttons degrade instead of killing the
    /// process, and that catch must let this same set through. One list, so the two
    /// halves of the clipboard cannot disagree about what stays loud.</para>
    /// </summary>
    internal static bool IsNeverSwallowable(Exception e) =>
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
