using System;
using Avalonia.Threading;

namespace UE5DumpUI.Helpers;

/// <summary>
/// [EXPORT-STATUS-LATE-PROGRESS] The status-line progress sink of a long export. Each report is queued to the UI
/// thread ONCE, and once <see cref="Complete"/> has set the final status, a report still in the queue is dropped.
///
/// <para>The hazard: <c>new Progress&lt;string&gt;(msg =&gt; Dispatcher.UIThread.Post(() =&gt; StatusText = msg))</c>
/// queues every report twice (<c>Progress&lt;T&gt;</c> posts to the captured context, and the handler posts again),
/// while a final <c>StatusText = …</c> after <c>await File.Write…Async</c> is a plain assignment. Nothing orders
/// the two, so a report queued before the service returned can land after the final status and replace it: a USMAP
/// export would end on the service's last "Generated USMAP …" report, and its own final status, which carries the
/// [P1-ENUMNAMES] enum-names warning, would never show.</para>
///
/// <para>The caller's rule: create it before the <c>try</c>. Once the service holds it, set every status through
/// it: <see cref="Report"/> for an intermediate one, and <see cref="Complete"/> for the final one on EVERY exit,
/// the catch blocks included (a queued report replaces "Export failed" just as well). A bare
/// <c>StatusText =</c> after that point can be overtaken. <c>UsmapExportServiceTests</c> pins the four Export
/// actions (Symbols, SDK Header, USMAP, Dump All) and the Dump Explorer parse to this rule; a service that reports a
/// record rather than a string takes <see cref="For{T}"/>. [R7-D-02]</para>
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

    /// <summary>[R7-D-02] A sink for a service that reports a record rather than a string. Each report is formatted
    /// and handed to <see cref="Report"/> at once, so it is still queued once and still dropped after
    /// <see cref="Complete"/>.</summary>
    internal IProgress<T> For<T>(Func<T, string> format)
        => new Mapped<T>(this, format ?? throw new ArgumentNullException(nameof(format)));

    private sealed class Mapped<T> : IProgress<T>
    {
        private readonly StatusProgress _owner;
        private readonly Func<T, string> _format;
        internal Mapped(StatusProgress owner, Func<T, string> format) { _owner = owner; _format = format; }
        public void Report(T value) => _owner.Report(_format(value));
    }
}
