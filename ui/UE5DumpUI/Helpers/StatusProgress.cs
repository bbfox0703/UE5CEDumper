using System;
using Avalonia.Threading;

namespace UE5DumpUI.Helpers;

/// <summary>
/// [EXPORT-STATUS-LATE-PROGRESS] The status-line progress sink of a long export. Each report is queued to the UI
/// thread ONCE, and once <see cref="Complete"/> has set the final status, a report still in the queue is dropped.
///
/// <para>What it replaces: <c>new Progress&lt;string&gt;(msg =&gt; Dispatcher.UIThread.Post(() =&gt; StatusText = msg))</c>
/// queued every report twice (<c>Progress&lt;T&gt;</c> posts to the captured context, and the handler posted again),
/// while the final <c>StatusText = …</c> was a plain assignment after <c>await File.Write…Async</c>. Nothing ordered
/// the two, so a report queued before the service returned could land after the final status and replace it.
/// Observed 2026-09-23 (L41 step 1, red run): the USMAP export ended on the service's last report, "Generated USMAP
/// (1412624 bytes, 7692 structs, 1569 enums)", and its own final status, which since [P1-ENUMNAMES] also carries
/// the enum-names warning, never showed.</para>
///
/// <para>The caller's rule: create it before the <c>try</c>. Once the service holds it, set every status through
/// it: <see cref="Report"/> for an intermediate one, and <see cref="Complete"/> for the final one on EVERY exit,
/// the catch blocks included (a queued report replaces "Export failed" just as well). A bare
/// <c>StatusText =</c> after that point can be overtaken. <c>UsmapExportServiceTests</c> pins the three Export
/// actions (Symbols, SDK Header, USMAP) to this rule.</para>
/// </summary>
internal sealed class StatusProgress : IProgress<string>
{
    private readonly Action<string> _setStatus;
    private readonly Action<Action> _post;
    private volatile bool _completed;

    /// <summary>Reports go to the Avalonia UI thread.</summary>
    internal StatusProgress(Action<string> setStatus)
        : this(setStatus, static a => Dispatcher.UIThread.Post(a)) { }

    /// <summary>Test seam: <paramref name="post"/> stands in for the UI thread's queue.</summary>
    internal StatusProgress(Action<string> setStatus, Action<Action> post)
    {
        _setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
        _post = post ?? throw new ArgumentNullException(nameof(post));
    }

    /// <summary>Queue <paramref name="value"/> for the status line. It is applied only if it runs before
    /// <see cref="Complete"/>.</summary>
    public void Report(string value)
        // Checked when the report RUNS, not when it is made: the late report is one made before completion.
        => _post(() => { if (!_completed) _setStatus(value); });

    /// <summary>Set the final status. From here on, every report still queued, or made later, is dropped.</summary>
    internal void Complete(string finalStatus)
    {
        _completed = true;   // before the assignment, so no queued report can follow it
        _setStatus(finalStatus);
    }
}
