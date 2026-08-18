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

    private void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Avalonia's own guidance for this event: the handler must not be able to
        // raise a secondary exception. Everything below is inside ShouldHandle's
        // try/catch, so this assignment is the only work done here.
        e.Handled = ShouldHandle(e.Exception);
    }

    /// <summary>
    /// The decision, split from the event so it is reachable from a test —
    /// <c>DispatcherUnhandledExceptionEventArgs</c> cannot be constructed outside
    /// Avalonia, and a headless dispatcher cannot be driven without adding a
    /// package this project deliberately does not take.
    /// </summary>
    /// <returns><c>true</c> to swallow the fault, <c>false</c> to let it terminate
    /// the process as before.</returns>
    internal bool ShouldHandle(Exception? ex)
    {
        try
        {
            var verdict = InputLayerFaultClassifier.Classify(ex, out var detail);
            var typeName = ex?.GetType().FullName ?? "(null)";

            if (verdict == InputFaultVerdict.InputLayer)
            {
                int n = Interlocked.Increment(ref _swallowedCount);
                _log.Warn(Constants.LogCatView,
                    $"Input-layer fault swallowed (#{n}) — the keystroke did nothing, the app is " +
                    $"still running. {typeName}: {ex?.Message} [{detail}]");
                return true;
            }

            // Not swallowed: this is on its way to crash.log. Log it here too so the
            // UI's own log carries the last thing the dispatcher saw — crash.log is
            // overwritten by the next crash, these files are not.
            _log.Error(Constants.LogCatInit,
                $"Unhandled dispatcher exception, NOT swallowed ({verdict}: {detail}). " +
                $"{typeName}: {ex?.Message}");
            return false;
        }
        catch
        {
            // A guard that throws would replace one crash with a worse one. Fail
            // closed: let the original exception continue on its way.
            return false;
        }
    }
}
