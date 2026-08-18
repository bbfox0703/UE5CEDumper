using Avalonia.Threading;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;

namespace UE5DumpUI.Services;

/// <summary>
/// Keeps a failure in Avalonia's own input plumbing from killing the process.
///
/// <para><b>The defect this exists for</b> (<c>[PASTECRASH-2026-08-18]</c>): a
/// clipboard read that fails inside <c>TextBox.Paste()</c> surfaces through
/// <c>Task.ThrowAsync</c> as an unobserved exception on the dispatcher. Nobody
/// handles it, the dispatcher rethrows, and the app dies — losing a connected
/// session with a loaded object tree because a paste did not work. Observed as
/// <c>COMException 0x8007000E: EnumFormatEtc failed</c> 31 minutes into a live
/// session.</para>
///
/// <para><b>The scope is the whole risk.</b> Marking dispatcher exceptions handled
/// is how a crash becomes an invisible corruption, so this class contains no
/// policy of its own: it asks <see cref="InputLayerFaultClassifier"/>, and that
/// predicate refuses anything with our own code on the stack, anything without
/// stack evidence, and a fixed set of never-swallow exception types. Everything
/// it does not positively recognise is rethrown and still reaches
/// <c>crash.log</c>.</para>
///
/// <para>Both outcomes are logged. A swallowed fault that leaves no trace would
/// turn "paste silently did nothing" into an unreportable ghost, which is the
/// second-worst outcome after the crash itself.</para>
/// </summary>
internal sealed class DispatcherFaultGuard
{
    private readonly ILoggingService _log;
    private int _swallowedCount;

    internal DispatcherFaultGuard(ILoggingService log) => _log = log;

    /// <summary>How many faults have been swallowed this session. Exposed for tests
    /// and for a future diagnostics readout; never used to change behaviour.</summary>
    internal int SwallowedCount => Volatile.Read(ref _swallowedCount);

    /// <summary>Subscribe to a dispatcher's unhandled-exception event.</summary>
    internal void Attach(Dispatcher dispatcher)
    {
        dispatcher.UnhandledException += OnUnhandledException;
    }

    /// <summary>
    /// Unsubscribe. Called at shutdown BEFORE the logging service is disposed: past
    /// that point the guard can no longer report what it did, and a swallow nobody
    /// can see is worse than the crash it prevents. Detaching makes teardown
    /// behaviour a stated decision instead of a side effect of object lifetimes.
    /// </summary>
    internal void Detach(Dispatcher dispatcher)
    {
        dispatcher.UnhandledException -= OnUnhandledException;
    }

    private void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (NextHandledFlag(e.Handled, e.Exception))
            e.Handled = true;
    }

    /// <summary>
    /// The set-only-true rule, as a function so it is reachable from a test —
    /// <c>DispatcherUnhandledExceptionEventArgs</c> cannot be constructed outside
    /// Avalonia.
    ///
    /// <para><b>Why it is not <c>e.Handled = ShouldHandle(...)</c>.</b> That form
    /// writes the result unconditionally, so it can write <c>false</c> over a
    /// <c>true</c> another subscriber already set — the event is multicast and the
    /// invocation order is nobody's contract, so a second handler added later (or by
    /// Avalonia itself) could have its decision silently revoked by ours. This
    /// guard's job is to add a reason to swallow, never to remove one.</para>
    ///
    /// <para>An already-handled fault also short-circuits before
    /// <see cref="ShouldHandle"/>, so <see cref="SwallowedCount"/> counts only
    /// swallows THIS guard is responsible for.</para>
    /// </summary>
    internal bool NextHandledFlag(bool alreadyHandled, Exception? ex) =>
        alreadyHandled || ShouldHandle(ex);

    /// <summary>
    /// The decision, split from the event so it is reachable from a test —
    /// <c>DispatcherUnhandledExceptionEventArgs</c> cannot be constructed outside
    /// Avalonia, and a headless dispatcher cannot be driven without adding a
    /// package this project deliberately does not take.
    /// </summary>
    /// <returns><c>true</c> to swallow the fault, <c>false</c> to let it terminate
    /// the process as before.</returns>
    ///
    /// <remarks>
    /// <b>The verdict does not depend on the logger.</b> The classify step and the
    /// log step used to share one try/catch, so a logger that threw — which is the
    /// NORMAL state after <c>ShutdownRequested</c> disposes it — turned a swallow
    /// into a crash. Whether a clipboard fault kills the app must not be a function
    /// of how far through teardown the process happens to be, so the decision is
    /// made first and logging is best-effort afterwards.
    /// </remarks>
    internal bool ShouldHandle(Exception? ex)
    {
        var verdict = ClassifySafely(ex, out var detail);
        bool swallow = verdict == InputFaultVerdict.InputLayer;

        // Only now, with the decision fixed and unable to change below, does the
        // counter move: it previously incremented BEFORE logging, so a throwing
        // logger left a count of faults that were then rethrown anyway.
        int n = swallow ? Interlocked.Increment(ref _swallowedCount) : 0;

        TryLog(swallow, verdict, detail, ex, n);
        return swallow;
    }

    /// <summary>Classify, treating a fault in the classifier itself as "cannot
    /// prove it is safe" — the same fail-closed answer as no evidence.</summary>
    private static InputFaultVerdict ClassifySafely(Exception? ex, out string detail)
    {
        try
        {
            return InputLayerFaultClassifier.Classify(ex, out detail);
        }
        catch (Exception classifierFault)
        {
            detail = "the classifier itself threw " + classifierFault.GetType().Name;
            return InputFaultVerdict.NotInputLayer;
        }
    }

    /// <summary>
    /// Best-effort logging of a decision already made. Everything here can throw —
    /// the logger may be disposed, and even <c>ex.Message</c> is user code on a
    /// custom exception type — and none of it may change the outcome.
    ///
    /// <para>Both outcomes go to the SAME category (<see cref="Constants.LogCatView"/>).
    /// They were split across <c>view</c> and <c>init</c>, which meant reading the
    /// guard's story required two files and made the documented verification step
    /// ("grep view-0.log") silently unable to see half of it.</para>
    /// </summary>
    private void TryLog(bool swallow, InputFaultVerdict verdict, string detail, Exception? ex, int n)
    {
        try
        {
            var typeName = ex?.GetType().FullName ?? "(null)";

            if (swallow)
            {
                _log.Warn(Constants.LogCatView,
                    $"Input-layer fault swallowed (#{n}) — the keystroke did nothing, the app is " +
                    $"still running. {typeName}: {ex?.Message} [{detail}]");
                return;
            }

            // Not swallowed: this is on its way to crash.log. Log it here too so the
            // UI's own log carries the last thing the dispatcher saw — crash.log is
            // overwritten by the next crash, these files are not.
            _log.Error(Constants.LogCatView,
                $"Unhandled dispatcher exception, NOT swallowed ({verdict}: {detail}). " +
                $"{typeName}: {ex?.Message}");
        }
        catch
        {
            // A guard that throws would replace one crash with a worse one, and this
            // is the one place that can: Avalonia's guidance for this event is that
            // the handler must not raise a secondary exception.
        }
    }
}
