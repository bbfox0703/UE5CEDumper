using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using UE5DumpUI.Helpers;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Pins the SCOPE of the dispatcher fault guard added for
/// <c>[PASTECRASH-2026-08-18]</c>.
///
/// <para>The guard's only risk is over-reach: marking a dispatcher exception
/// handled turns a crash into silent corruption, so half of these tests are
/// negative controls — cases that MUST still be allowed to kill the process.</para>
///
/// <para><see cref="CapturedPasteTrace"/> is an ABRIDGED transcription of the field
/// report — the six frames from the COM proxy up to <c>Task.ThrowAsync</c>, which is
/// the part the classifier decides on. The complete 23-frame trace as
/// <c>crash.log</c> recorded it is <see cref="FullCrashLogTrace"/>; the two differ in
/// a way that matters, and
/// <see cref="TheFullPostUnwindTrace_IsRefused_BecauseItReachesMain"/> is where that
/// is pinned.</para>
/// </summary>
public class InputLayerFaultClassifierTests
{
    private const string CapturedPasteTrace =
        "   at Avalonia.Win32.Win32Com.Impl.__MicroComIDataObjectProxy.EnumFormatEtc(Int32 dwDirection)\n" +
        "   at Avalonia.Win32.ClipboardImpl.TryGetDataAsync()\n" +
        "   at Avalonia.Input.Platform.ClipboardExtensions.TryGetValueAsync[T](...)\n" +
        "   at Avalonia.Controls.TextBox.Paste()\n" +
        "   at System.Threading.Tasks.Task.<>c.<ThrowAsync>b__124_0(Object state)";

    /// <summary>
    /// The whole trace, copied out of the real
    /// <c>%LOCALAPPDATA%\UE5CEDumper\crash.log</c> written at 2026-08-18 22:02:50 —
    /// 23 frames plus the unwind separator, not the 5-frame excerpt above.
    ///
    /// <para><b>Its last frame is <c>UE5DumpUI.Program.Main</c></b>, so as a piece of
    /// text it fails rule 3 and can never be swallowed. That is not a contradiction
    /// with the guard working — it is a statement about WHEN each version of the text
    /// exists, and the distinction is the reason this constant is here in full.</para>
    /// </summary>
    private const string FullCrashLogTrace =
        "   at Avalonia.Win32.Win32Com.Impl.__MicroComIDataObjectProxy.EnumFormatEtc(Int32 dwDirection)\n" +
        "   at Avalonia.Win32.OleDataObjectToDataTransferWrapper.ProvideFormats()\n" +
        "   at Avalonia.Input.Platform.PlatformDataTransfer.get_Formats()\n" +
        "   at Avalonia.Win32.ClipboardImpl.TryGetDataAsync()\n" +
        "   at Avalonia.Input.Platform.ClipboardExtensions.TryGetValueAsync[T](IClipboard clipboard, DataFormat`1 format)\n" +
        "   at Avalonia.Controls.TextBox.Paste()\n" +
        "   at System.Threading.Tasks.Task.<>c.<ThrowAsync>b__124_0(Object state)\n" +
        "   at Avalonia.Threading.SendOrPostCallbackDispatcherOperation.InvokeCore()\n" +
        "   at Avalonia.Threading.CulturePreservingExecutionContext.CallbackWrapper(Object obj)\n" +
        "   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)\n" +
        "--- End of stack trace from previous location ---\n" +
        "   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)\n" +
        "   at Avalonia.Threading.DispatcherOperation.Execute()\n" +
        "   at Avalonia.Threading.Dispatcher.ExecuteJobsCore(Boolean fromExplicitBackgroundProcessingCallback)\n" +
        "   at Avalonia.Win32.Win32Platform.WndProc(IntPtr hWnd, UInt32 msg, IntPtr wParam, IntPtr lParam)\n" +
        "   at Avalonia.Win32.Interop.UnmanagedMethods.DispatchMessage(MSG& lpmsg)\n" +
        "   at Avalonia.Win32.Win32DispatcherImpl.RunLoop(CancellationToken cancellationToken)\n" +
        "   at Avalonia.Threading.DispatcherFrame.Run(IControlledDispatcherImpl impl)\n" +
        "   at Avalonia.Threading.Dispatcher.PushFrame(DispatcherFrame frame)\n" +
        "   at Avalonia.Threading.Dispatcher.MainLoop(CancellationToken cancellationToken)\n" +
        "   at Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime.StartCore(String[] args)\n" +
        "   at Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime.Start(String[] args)\n" +
        "   at Avalonia.ClassicDesktopStyleApplicationLifetimeExtensions.StartWithClassicDesktopLifetime(AppBuilder builder, String[] args, Action`1 lifetimeBuilder)\n" +
        "   at UE5DumpUI.Program.Main(String[] args)";

    /// <summary>A COMException carrying a chosen stack, without having to reproduce
    /// the original throw. <c>ExceptionDispatchInfo.SetRemoteStackTrace</c> is a
    /// first-class BCL API for exactly this, and works only on a never-thrown
    /// exception — so every fixture here is freshly constructed.</summary>
    private static COMException ClipboardComException(string trace)
    {
        var ex = new COMException("EnumFormatEtc failed", unchecked((int)0x8007000E));
        ExceptionDispatchInfo.SetRemoteStackTrace(ex, trace);
        return ex;
    }

    // ---------------------------------------------------------------- accepts

    [Fact]
    public void RealCapturedPasteCrash_IsInputLayer()
    {
        var verdict = InputLayerFaultClassifier.Classify(
            ClipboardComException(CapturedPasteTrace), out var detail);

        Assert.Equal(InputFaultVerdict.InputLayer, verdict);
        Assert.Contains("Avalonia.Win32", detail, StringComparison.Ordinal);
    }

    [Theory]
    // Each allow-listed surface on its own, so no single marker is load-bearing
    // for the others.
    [InlineData("   at Avalonia.Win32.ClipboardImpl.TryGetDataAsync()")]
    [InlineData("   at Avalonia.Input.Platform.ClipboardExtensions.TryGetValueAsync[T](...)")]
    [InlineData("   at Avalonia.Input.Platform.Clipboard.GetDataAsync(String format)")]
    [InlineData("   at Avalonia.Win32.Win32Com.Impl.__MicroComIDataObjectProxy.GetData(FORMATETC& f)")]
    [InlineData("   at Avalonia.Win32.Input.Imm32InputMethod.SetComposition(String text)")]
    [InlineData("   at Avalonia.Controls.TextBox.Paste()")]
    [InlineData("   at Avalonia.Controls.TextBox.Copy()")]
    public void EachAllowListedSurface_IsInputLayer(string frame)
    {
        Assert.Equal(InputFaultVerdict.InputLayer,
            InputLayerFaultClassifier.ClassifyStackText(frame, out _));
    }

    [Fact]
    public void TheFullPostUnwindTrace_IsRefused_BecauseItReachesMain()
    {
        // The complete crash.log text ends at UE5DumpUI.Program.Main, so rule 3 fires
        // and it is refused. The five-frame excerpt every other test uses is accepted.
        // BOTH are right, because they are the same fault at two different moments:
        //
        //   decision time — the guard runs from Dispatcher.UIThread.UnhandledException,
        //                   INSIDE the dispatcher's own catch. The stack it reads stops
        //                   at ExecutionContext.RunInternal; the message loop, the
        //                   lifetime and Main are still on the machine stack but are
        //                   not part of the exception's captured trace yet.
        //
        //   report time   — nothing handled it, so the exception unwinds all the way
        //                   out of StartWithClassicDesktopLifetime and is caught in
        //                   Program.Main, and .NET appends every frame it passed
        //                   through on the way. THAT is the text in crash.log.
        //
        // So the trace grows an "our code" tail precisely BECAUSE the guard declined
        // (or was not there). This test exists so nobody reads crash.log, sees a
        // UE5DumpUI frame, and concludes the classifier must be broken — and so that
        // if the two ever converge, the fail-closed direction is the pinned one.
        var verdict = InputLayerFaultClassifier.Classify(
            ClipboardComException(FullCrashLogTrace), out var detail);

        Assert.Equal(InputFaultVerdict.OurCodeOnStack, verdict);
        Assert.Contains("UE5DumpUI.", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFullTraceMinusTheMainTail_IsWhatTheGuardActuallySees()
    {
        // Negative control for the test above: the SAME text truncated where the
        // dispatcher's catch truncates it is accepted. Without this, the assertion
        // above would pass just as well if the classifier had stopped recognising
        // clipboard frames entirely.
        int cut = FullCrashLogTrace.IndexOf(
            "--- End of stack trace from previous location ---", StringComparison.Ordinal);
        Assert.True(cut > 0);
        var atDecisionTime = FullCrashLogTrace[..cut];

        Assert.DoesNotContain("UE5DumpUI.", atDecisionTime, StringComparison.Ordinal);
        Assert.Equal(InputFaultVerdict.InputLayer,
            InputLayerFaultClassifier.Classify(
                ClipboardComException(atDecisionTime), out _));
    }

    [Fact]
    public void ClipboardFaultWrappedInAnUnthrownWrapper_IsInputLayer()
    {
        // The wrapper has no stack of its own (never thrown), so the inner
        // exception's evidence is what decides.
        var wrapped = new InvalidOperationException(
            "paste failed", ClipboardComException(CapturedPasteTrace));

        Assert.Equal(InputFaultVerdict.InputLayer,
            InputLayerFaultClassifier.Classify(wrapped, out _));
    }

    // ------------------------------------------------------ negative controls

    [Fact]
    public void NullReferenceFromOurOwnCode_IsNeverSwallowed()
    {
        // THE mandated negative control, thrown for real rather than fabricated:
        // a ViewModel-shaped NRE must reach the crash handler untouched.
        Exception? captured = null;
        try
        {
            ThrowLikeAViewModel();
        }
        catch (NullReferenceException ex)
        {
            captured = ex;
        }

        Assert.NotNull(captured);

        var verdict = InputLayerFaultClassifier.Classify(captured, out var detail);

        Assert.Equal(InputFaultVerdict.OurCodeOnStack, verdict);
        Assert.Contains("UE5DumpUI.", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ClipboardFrameWithOneOfOurFrames_IsRejected()
    {
        // The dangerous case: a genuine clipboard entry point that then ran OUR
        // code, which is where our own bug would hide. Our frame wins.
        const string mixed =
            "   at UE5DumpUI.ViewModels.ObjectTreeViewModel.OnFilterTextChanged(String value)\n" +
            "   at Avalonia.Win32.ClipboardImpl.TryGetDataAsync()\n" +
            "   at Avalonia.Controls.TextBox.Paste()";

        Assert.Equal(InputFaultVerdict.OurCodeOnStack,
            InputLayerFaultClassifier.ClassifyStackText(mixed, out _));
    }

    [Theory]
    // "The process is no longer trustworthy" + "trimming/AOT damage": these must
    // crash even standing on a perfect clipboard stack.
    [InlineData(typeof(OutOfMemoryException))]
    [InlineData(typeof(AccessViolationException))]
    [InlineData(typeof(BadImageFormatException))]
    [InlineData(typeof(TypeLoadException))]
    [InlineData(typeof(DllNotFoundException))]        // : TypeLoadException
    [InlineData(typeof(MissingMethodException))]      // : MissingMemberException
    [InlineData(typeof(InvalidProgramException))]
    [InlineData(typeof(SEHException))]
    public void NeverSwallowTypes_AreRejectedEvenOnAClipboardStack(Type exceptionType)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType)!;
        ExceptionDispatchInfo.SetRemoteStackTrace(ex, CapturedPasteTrace);

        Assert.Equal(InputFaultVerdict.NeverSwallow,
            InputLayerFaultClassifier.Classify(ex, out _));
    }

    [Fact]
    public void TypeInitializationException_OnAClipboardStack_IsRejected()
    {
        // Constructed separately: it has no parameterless constructor. This is the
        // shape an AOT/trim failure takes, and it is the one this repo can least
        // afford to hide.
        var ex = new TypeInitializationException("Some.Trimmed.Type", null);
        ExceptionDispatchInfo.SetRemoteStackTrace(ex, CapturedPasteTrace);

        Assert.Equal(InputFaultVerdict.NeverSwallow,
            InputLayerFaultClassifier.Classify(ex, out _));
    }

    [Fact]
    public void NeverThrownExceptionWithNoStack_IsRejected()
    {
        // Fail closed: nothing can be proven about where it came from.
        Assert.Equal(InputFaultVerdict.NoStackEvidence,
            InputLayerFaultClassifier.Classify(new COMException("boom"), out _));
    }

    [Fact]
    public void Null_IsRejected()
    {
        Assert.Equal(InputFaultVerdict.NotInputLayer,
            InputLayerFaultClassifier.Classify(null, out _));
    }

    [Theory]
    // Ordinary faults from elsewhere in the stack — none of them input-layer.
    [InlineData("   at System.IO.Pipes.NamedPipeClientStream.ConnectAsync(Int32 timeout)")]
    [InlineData("   at Avalonia.Rendering.Composition.Server.ServerCompositor.Render()")]
    [InlineData("   at Avalonia.Controls.DataGrid.OnApplyTemplate()")]
    [InlineData("   at Microsoft.Data.Sqlite.SqliteCommand.ExecuteReader()")]
    public void UnrelatedStacks_AreRejected(string frame)
    {
        Assert.Equal(InputFaultVerdict.NotInputLayer,
            InputLayerFaultClassifier.ClassifyStackText(frame, out _));
    }

    [Fact]
    public void AggregateCannotSmuggleAnUnrelatedFaultAlongsideAClipboardOne()
    {
        var clipboard = ClipboardComException(CapturedPasteTrace);

        var unrelated = new InvalidOperationException("pipe died");
        ExceptionDispatchInfo.SetRemoteStackTrace(unrelated,
            "   at System.IO.Pipes.NamedPipeClientStream.ConnectAsync(Int32 timeout)");

        var agg = new AggregateException(clipboard, unrelated);
        ExceptionDispatchInfo.SetRemoteStackTrace(agg, "   at Avalonia.Controls.TextBox.Paste()");

        Assert.Equal(InputFaultVerdict.NotInputLayer,
            InputLayerFaultClassifier.Classify(agg, out _));
    }

    [Fact]
    public void AChainTooDeepToWalk_IsRejectedRatherThanAccepted()
    {
        // Every link is a perfect clipboard fault, so the only thing that can make
        // this refuse is the depth bound itself — what the walk did not look at
        // could have been a never-swallow type.
        Exception chain = ClipboardComException(CapturedPasteTrace);
        for (int i = 0; i < 12; i++)
        {
            var outer = new InvalidOperationException("layer " + i, chain);
            ExceptionDispatchInfo.SetRemoteStackTrace(outer, CapturedPasteTrace);
            chain = outer;
        }

        Assert.Equal(InputFaultVerdict.NotInputLayer,
            InputLayerFaultClassifier.Classify(chain, out var detail));
        Assert.Contains("deeper than", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AChainShallowEnoughToWalk_IsStillAccepted()
    {
        // Negative control for the bound above: three layers of the same shape must
        // NOT trip it, or the bound would be silently disabling the whole guard.
        Exception chain = ClipboardComException(CapturedPasteTrace);
        for (int i = 0; i < 3; i++)
        {
            var outer = new InvalidOperationException("layer " + i, chain);
            ExceptionDispatchInfo.SetRemoteStackTrace(outer, CapturedPasteTrace);
            chain = outer;
        }

        Assert.Equal(InputFaultVerdict.InputLayer,
            InputLayerFaultClassifier.Classify(chain, out _));
    }

    [Fact]
    public void TextBoxCut_IsNotSwallowed_BecauseItMutatesStateAroundItsAwait()
    {
        // Cut LOOKS like Copy and Paste and is deliberately treated differently.
        // Read out of the installed Avalonia 12.1.1 IL, TextBox+<Cut>d__233.MoveNext:
        //
        //   IL_0047 call TextBox.SnapshotUndoRedo         <- BEFORE the await
        //   IL_0069 call ClipboardExtensions.SetTextAsync
        //   IL_0098 call AsyncVoidMethodBuilder.AwaitUnsafeOnCompleted
        //   IL_00BE call TaskAwaiter.GetResult            <- a failed write throws HERE
        //   IL_00C4 call TextBox.DeleteSelection          <- never runs
        //
        // Swallowing that does not leave "the keystroke did nothing": it leaves an
        // undo snapshot with no edit behind it. The guard's licence is that the
        // consequence is nil, so Cut is outside it and still crashes.
        //
        // The same read is what CLEARS the other two, which is why they stayed:
        // Copy mutates nothing at all, and Paste's SnapshotUndoRedo is at IL_0106 —
        // after its GetResult at IL_00AD — so a failed read throws before any state
        // changes. Re-run that check before adding Cut back.
        Assert.Equal(InputFaultVerdict.NotInputLayer,
            InputLayerFaultClassifier.ClassifyStackText(
                "   at Avalonia.Controls.TextBox.Cut()", out _));

        Assert.DoesNotContain("TextBox.Cut", InputLayerFaultClassifier.MethodMarkers);
    }

    // ------------------------------------------------- AOT-rendered async frames

    [Theory]
    // Under Native AOT the formatter cannot necessarily unwrap MoveNext back into
    // the method it came from, so the frame arrives as the state-machine type. All
    // three clipboard entry points are async void, so all of them are exposed.
    // Both separators: CoreCLR's StackTrace does FullName.Replace('+','.'), other
    // renderings keep the '+'.
    [InlineData("   at Avalonia.Controls.TextBox.<Paste>d__235.MoveNext()")]
    [InlineData("   at Avalonia.Controls.TextBox+<Paste>d__235.MoveNext()")]
    [InlineData("   at Avalonia.Controls.TextBox.<Copy>d__234.MoveNext()")]
    [InlineData("   at Avalonia.Controls.TextBox+<Copy>d__234.MoveNext()")]
    public void StateMachineRenderedClipboardFrames_AreStillInputLayer(string frame)
    {
        Assert.Equal(InputFaultVerdict.InputLayer,
            InputLayerFaultClassifier.ClassifyStackText(frame, out var detail));
        Assert.Contains("state machine", detail, StringComparison.Ordinal);
    }

    [Theory]
    // Negative controls for the rule above. Cut stays excluded in BOTH renderings
    // (a state-machine marker generated from a list that no longer holds Cut), and
    // the marker must not degrade into "any frame mentioning Paste".
    [InlineData("   at Avalonia.Controls.TextBox.<Cut>d__233.MoveNext()")]
    [InlineData("   at Avalonia.Controls.TextBox+<Cut>d__233.MoveNext()")]
    [InlineData("   at Some.Other.Type.<Paste>d__1.MoveNext()")]
    public void StateMachineMarkers_DoNotOverMatch(string frame)
    {
        Assert.Equal(InputFaultVerdict.NotInputLayer,
            InputLayerFaultClassifier.ClassifyStackText(frame, out _));
    }

    [Theory]
    // A method marker must be a whole method NAME, not any prefix of one. This
    // found a live over-match: plain string.Contains accepted
    // "TextBox.PasteHistoryItem()" as a Paste frame, so a method nobody had reasoned
    // about would have been inside the swallow. Avalonia has no such method today —
    // which is the point, the guard must not silently acquire one.
    [InlineData("   at Avalonia.Controls.TextBox.PasteHistoryItem()")]
    [InlineData("   at Avalonia.Controls.TextBox.CopyToBuffer(Int32 n)")]
    [InlineData("   at Avalonia.Controls.TextBox.Paste2()")]
    [InlineData("   at Avalonia.Controls.TextBox.Copy_Internal()")]
    public void AMethodMarkerMustBeAWholeMethodName(string frame)
    {
        Assert.Equal(InputFaultVerdict.NotInputLayer,
            InputLayerFaultClassifier.ClassifyStackText(frame, out _));
    }

    [Theory]
    // Negative control for the boundary rule: it must not have narrowed the marker
    // out of matching the real frames. Trailing "(" is the normal rendering; the
    // bare name is what a formatter that omits the parameter list would print, and
    // the rule has to keep accepting it — that is why the boundary is "not an
    // identifier character" rather than a literal "(".
    [InlineData("   at Avalonia.Controls.TextBox.Paste()")]
    [InlineData("   at Avalonia.Controls.TextBox.Copy()")]
    [InlineData("   at Avalonia.Controls.TextBox.Paste")]
    public void TheBoundaryRuleStillAcceptsRealFrames(string frame)
    {
        Assert.Equal(InputFaultVerdict.InputLayer,
            InputLayerFaultClassifier.ClassifyStackText(frame, out _));
    }

    [Fact]
    public void EveryMethodMarker_HasBothStateMachineRenderings()
    {
        // Structural: the derived list must stay derived. A hand-added method marker
        // with no state-machine twin would work in every test run (CoreCLR unwraps
        // the frame) and be dead in the shipped AOT build.
        foreach (var marker in InputLayerFaultClassifier.MethodMarkers)
        {
            int cut = marker.LastIndexOf('.');
            string type = marker[..cut];
            string method = marker[(cut + 1)..];

            Assert.Contains($"{type}.<{method}>d__", InputLayerFaultClassifier.StateMachineMethodMarkers);
            Assert.Contains($"{type}+<{method}>d__", InputLayerFaultClassifier.StateMachineMethodMarkers);
        }

        Assert.Equal(InputLayerFaultClassifier.MethodMarkers.Length * 2,
            InputLayerFaultClassifier.StateMachineMethodMarkers.Length);
    }

    // ------------------------------------------- generated code IS our code too

    [Fact]
    public void GeneratedXamlFrameCountsAsOurCode_EvenUnderAClipboardStack()
    {
        // The hole this closes: rule 3 tests for the "UE5DumpUI." root namespace, but
        // 47 types in the shipped assembly live outside it — 35 of them under
        // CompiledAvaloniaXaml (the compiled-binding setters and trampolines, where a
        // binding type mismatch raises InvalidCastException). Such a fault, arriving
        // under a clipboard-topped stack, used to look like nobody's code.
        const string generated =
            "   at CompiledAvaloniaXaml.XamlIlTrampolines.Set_UE5DumpUI_ViewModels_X_Y(Object o, Object v)\n" +
            "   at Avalonia.Win32.ClipboardImpl.TryGetDataAsync()\n" +
            "   at Avalonia.Controls.TextBox.Paste()";

        Assert.Equal(InputFaultVerdict.OurCodeOnStack,
            InputLayerFaultClassifier.ClassifyStackText(generated, out var detail));
        Assert.Contains("CompiledAvaloniaXaml.", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedMarkersOnlyEverRefuse_TheyNeverWidenTheSwallow()
    {
        // The direction check. This rule is an extension of "our code is on the
        // stack", so its only possible effect is MORE refusals. A frame that carries
        // a generated marker and NOTHING else is still refused — it does not become
        // an accepted input-layer frame by itself.
        Assert.Equal(InputFaultVerdict.OurCodeOnStack,
            InputLayerFaultClassifier.ClassifyStackText(
                "   at CompiledAvaloniaXaml.XamlDynamicSetters.Set_1(Object o, Object v)", out _));
    }

    [Fact]
    public void HandWrittenAvaloniaXamlFrames_AreNotMistakenForGeneratedCode()
    {
        // Negative control: the marker is the GENERATED root namespace, not the word
        // "Xaml". Avalonia's own runtime XAML types must not trip it, or an ordinary
        // markup fault would start reading as ours.
        Assert.Equal(InputFaultVerdict.NotInputLayer,
            InputLayerFaultClassifier.ClassifyStackText(
                "   at Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(Object obj)", out _));
    }

    [Fact]
    public void EveryGeneratedMarker_MatchesRealTypesInTheShippedAssembly()
    {
        // Pins the premise, not the guess: these names must actually exist in OUR
        // assembly and must NOT be reachable via the UE5DumpUI. prefix, or the rule
        // above is guarding a hole that was never there.
        var ourTypes = typeof(UE5DumpUI.App).Assembly.GetTypes()
            .Select(t => t.FullName ?? t.Name)
            .ToArray();

        foreach (var marker in InputLayerFaultClassifier.GeneratedOwnCodeMarkers)
        {
            var hits = ourTypes.Where(n => n.StartsWith(marker, StringComparison.Ordinal)).ToArray();
            Assert.True(hits.Length > 0,
                $"No type in the UE5DumpUI assembly starts with '{marker}'. The marker is dead — " +
                "either the compiler stopped emitting that namespace or it was mistyped, and the " +
                "'generated code is our code' rule now guards nothing.");
            Assert.All(hits, n => Assert.False(n.StartsWith("UE5DumpUI.", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void SubstringOfAMarkerDoesNotMatch()
    {
        // Guards against the allow-list being loosened into a keyword search: a
        // frame that merely mentions a clipboard-ish word is not a match.
        Assert.Equal(InputFaultVerdict.NotInputLayer,
            InputLayerFaultClassifier.ClassifyStackText(
                "   at UE5Dump.Something.ClipboardHelper.Read()", out _));
    }

    // --------------------------------------------------- Avalonia surface pin

    [Fact]
    public void EveryTypeMarker_ResolvesInTheInstalledAvalonia()
    {
        // Without this, an Avalonia upgrade that renames ClipboardImpl would
        // silently disable the guard: the classifier would simply stop matching
        // and every paste fault would go back to killing the process, with no
        // build error and no test failure anywhere.
        var assemblies = AvaloniaAssemblies();

        foreach (var marker in InputLayerFaultClassifier.TypeMarkers)
        {
            var found = assemblies.Any(a => SafeGetTypes(a)
                .Any(t => string.Equals(t.FullName, marker, StringComparison.Ordinal)));

            if (!found)
            {
                var candidates = assemblies.SelectMany(SafeGetTypes)
                    .Select(t => t.FullName ?? "")
                    .Where(n => n.Contains(LastSegment(marker), StringComparison.OrdinalIgnoreCase))
                    .Take(10);
                Assert.Fail(
                    $"Avalonia no longer has type '{marker}'. The input-layer fault guard " +
                    $"matches on this name, so it is now dead. Candidates: {string.Join(", ", candidates)}");
            }
        }
    }

    [Fact]
    public void EveryMethodMarker_ResolvesInTheInstalledAvalonia()
    {
        var assemblies = AvaloniaAssemblies();

        foreach (var marker in InputLayerFaultClassifier.MethodMarkers)
        {
            int cut = marker.LastIndexOf('.');
            string typeName = marker[..cut];
            string methodName = marker[(cut + 1)..];

            var type = assemblies.SelectMany(SafeGetTypes)
                .FirstOrDefault(t => string.Equals(t.FullName, typeName, StringComparison.Ordinal));
            if (type is null)
                Assert.Fail($"Avalonia no longer has type '{typeName}' (marker '{marker}').");

            // GetMethod(name, flags) throws AmbiguousMatchException the day Avalonia
            // adds a Paste(string) overload — which would abort this test with a
            // reflection error instead of the message it was written to give. The
            // question here is only "does a method by this name still exist", and
            // overloads answer it just as well as a single match does.
            var overloads = type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static)
                .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
                .ToArray();
            if (overloads.Length == 0)
                Assert.Fail($"Avalonia type '{typeName}' no longer has method '{methodName}' — " +
                            "the guard's marker is dead.");
        }
    }

    [Fact]
    public void EveryStateMachineMarker_MatchesARealAvaloniaStateMachineType()
    {
        // The AOT half of the pin above. The plain markers can only be verified as
        // METHOD names; these have to be verified as the compiler-generated TYPES a
        // stack trace would print, because that is the whole point of them.
        var assemblies = AvaloniaAssemblies();
        var allTypes = assemblies.SelectMany(SafeGetTypes)
            .Select(t => t.FullName ?? "")
            .ToArray();

        foreach (var marker in InputLayerFaultClassifier.StateMachineMethodMarkers)
        {
            // Reflection always spells a nested type with '+'; the '.' variant exists
            // for the formatters that rewrite it, so normalise before looking it up.
            var asReflected = NormaliseNestedSeparator(marker);
            bool found = allTypes.Any(n => n.StartsWith(asReflected, StringComparison.Ordinal));

            Assert.True(found,
                $"No Avalonia type matches the state-machine marker '{marker}' (looked for " +
                $"'{asReflected}…'). Either the method stopped being async — in which case the " +
                "marker is dead weight — or it was renamed, in which case the plain marker is " +
                "dead too and the guard is disabled in the shipped AOT build.");
        }
    }

    /// <summary>Turn <c>Ns.Type.&lt;M&gt;d__</c> into <c>Ns.Type+&lt;M&gt;d__</c>, the
    /// spelling reflection uses for a nested type.</summary>
    private static string NormaliseNestedSeparator(string marker)
    {
        int open = marker.IndexOf('<');
        if (open <= 0) return marker;
        return marker[..(open - 1)] + "+" + marker[open..];
    }

    private static Assembly[] AvaloniaAssemblies() =>
    [
        typeof(Avalonia.Input.Platform.IClipboard).Assembly,   // Avalonia.Base
        typeof(Avalonia.Controls.TextBox).Assembly,            // Avalonia.Controls
        typeof(Avalonia.Win32PlatformOptions).Assembly,  // Avalonia.Win32
    ];

    private static Type[] SafeGetTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.OfType<Type>().ToArray(); }
    }

    private static string LastSegment(string fullName)
    {
        int cut = fullName.LastIndexOf('.');
        return cut < 0 ? fullName : fullName[(cut + 1)..];
    }

    private static void ThrowLikeAViewModel()
    {
        string? nothing = null;
        _ = nothing!.Length;
    }
}
