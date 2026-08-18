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
/// <para>The captured trace below is the real one from the field report, verbatim
/// apart from the argument list the report itself elided on the
/// <c>TryGetValueAsync</c> frame.</para>
/// </summary>
public class InputLayerFaultClassifierTests
{
    private const string CapturedPasteTrace =
        "   at Avalonia.Win32.Win32Com.Impl.__MicroComIDataObjectProxy.EnumFormatEtc(Int32 dwDirection)\n" +
        "   at Avalonia.Win32.ClipboardImpl.TryGetDataAsync()\n" +
        "   at Avalonia.Input.Platform.ClipboardExtensions.TryGetValueAsync[T](...)\n" +
        "   at Avalonia.Controls.TextBox.Paste()\n" +
        "   at System.Threading.Tasks.Task.<>c.<ThrowAsync>b__124_0(Object state)";

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
    [InlineData("   at Avalonia.Controls.TextBox.Cut()")]
    public void EachAllowListedSurface_IsInputLayer(string frame)
    {
        Assert.Equal(InputFaultVerdict.InputLayer,
            InputLayerFaultClassifier.ClassifyStackText(frame, out _));
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

            var method = type.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (method is null)
                Assert.Fail($"Avalonia type '{typeName}' no longer has method '{methodName}' — " +
                            "the guard's marker is dead.");
        }
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
