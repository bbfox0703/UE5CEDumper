using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using UE5DumpUI.Views;
using Xunit;

namespace UE5DumpUI.HeadlessTests;

/// <summary>
/// The Live Funcs panel laid out as Avalonia 12.1.3 lays it out, in the maintainer's 1389x868 window, for the tests that
/// measure what sits above its table. The view model is the real one, behind a dump service that answers with a canned
/// page (two keyed functions over a 10 s window) so a choice can be made the way the user makes it.
/// </summary>
internal static class LiveFuncsLayout
{
    public sealed class Gate(bool enabled) : IExperimentalGate
    {
        public bool IsEnabled { get; set; } = enabled;
        public int SnapshotQuotaMb { get; set; }
        public bool IsLocked => false;
        public void Lock() { }
        public event EventHandler? Changed { add { } remove { } }
    }

    /// <summary>The dump service: the canned page for every fetch, a Start that arms whatever trace it is asked for (as
    /// a DLL that takes every choice answers), and a Stop that reports <see cref="StopStack"/> as what the stacks cost.
    /// Anything else answers its type's default, or a completed task of it.</summary>
    public class Canned : DispatchProxy
    {
        public static PeProfileResult Page => new()
        {
            DistinctFuncs = 2, TotalCalls = 1_050, WindowMs = 10_000,
            Entries = new()
            {
                new PeProfileEntry { ClassName = "A", FuncName = "F", FuncAddr = "0x1", FnameKey = new NameKey(1, 0, 9, 0),
                                     Count = 1_000, NumParms = 1, ParmsSize = 16, FunctionFlags = 0x400 },
                new PeProfileEntry { ClassName = "A", FuncName = "G", FuncAddr = "0x2", FnameKey = new NameKey(2, 0, 9, 0),
                                     Count = 50, NumParms = 1, ParmsSize = 16, FunctionFlags = 0x400 },
            },
        };

        /// <summary>What the next Stop says the stacks cost; null reports a Stop with no trace.</summary>
        public static StackInfo? StopStack;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var m = method!;
            if (m.Name == "PeProfileGetAsync") return Task.FromResult(Page);
            if (m.Name == "PeProfileStartAsync")
            {
                var trace = args?.OfType<TraceStartOptions>().FirstOrDefault();
                var stacks = trace?.Snapshots?.Stacks;
                return Task.FromResult(new PeProfileStartResult
                {
                    HookActive = true,
                    Trace = trace == null ? null : new TraceInfo
                    {
                        Allocated = true, Tracing = true, Gen = 1,
                        Names = new StartNames
                        {
                            Ticks = trace.TickedNames.Count, Chosen = trace.Snapshots?.Funcs.Count ?? 0,
                            Stacks = stacks?.Funcs.Count,
                        },
                        Stack = stacks == null ? null : new StackInfo { Rings = stacks.Funcs.Count, Depth = stacks.Depth },
                    },
                });
            }
            if (m.Name == "PeProfileStopWithTraceAsync")
                return Task.FromResult<TraceInfo?>(StopStack == null ? null : new TraceInfo
                {
                    Allocated = true, Quiesced = true, Gen = 1, Written = 40, QpcFreq = 10_000_000, Stack = StopStack,
                });
            var t = m.ReturnType;
            if (t == typeof(Task)) return Task.CompletedTask;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var arg = t.GetGenericArguments()[0];
                object? value = arg.IsValueType ? Activator.CreateInstance(arg) : null;
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(arg).Invoke(null, new[] { value });
            }
            return t.IsValueType && t != typeof(void) ? Activator.CreateInstance(t) : null;
        }
    }

    /// <summary>Free memory as the platform reports it; everything else is the interface's default or nothing.</summary>
    public class Memory : DispatchProxy
    {
        public static long AvailableBytes = long.MaxValue;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IPlatformService.GetAvailablePhysicalMemoryBytes)) return AvailableBytes;
            var t = method.ReturnType;
            if (t == typeof(Task)) return Task.CompletedTask;
            return t.IsValueType && t != typeof(void) ? Activator.CreateInstance(t) : null;
        }
    }

    private static bool _loaded;

    /// <summary>The panel's strings and the DataGrid's theme, which the app's own App.axaml adds: without the theme the
    /// grid has no template, so no column headers and no top edge to measure. Every test body runs on the one dispatcher
    /// thread, so the flag needs no lock.</summary>
    public static void LoadAppResources()
    {
        if (_loaded) return;
        var strings = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://UE5DumpUI/Resources/Strings/en.axaml"));
        Application.Current!.Resources.MergedDictionaries.Add(strings);
        Application.Current.Styles.Add((IStyle)AvaloniaXamlLoader.Load(
            new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml")));
        _loaded = true;
    }

    public static string Res(string key)
    {
        Assert.True(Application.Current!.TryFindResource(key, out var value) && value is string,
                    $"{key} is not in en.axaml");
        return (string)value!;
    }

    /// <summary>The panel shown, nothing fetched. <paramref name="availableBytes"/> is the free memory the platform
    /// reports (unknown by default, so no memory warning).</summary>
    public static (LiveFuncsPanel panel, LiveFuncsViewModel vm) Laid(bool experimental = true,
                                                                     long availableBytes = long.MaxValue)
    {
        LoadAppResources();
        Memory.AvailableBytes = availableBytes;
        Canned.StopStack = null;
        var dump = DispatchProxy.Create<IDumpService, Canned>();
        var log = DispatchProxy.Create<ILoggingService, Canned>();
        var platform = DispatchProxy.Create<IPlatformService, Memory>();
        var vm = new LiveFuncsViewModel(dump, log, platform, experimentalGate: new Gate(experimental));
        var panel = new LiveFuncsPanel { DataContext = vm };
        var window = new Window { Content = panel, Width = 1389, Height = 868 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (panel, vm);
    }

    /// <summary>The panel with the canned page fetched, Trace on, and A::F chosen for a stack. With
    /// <paramref name="usPerCapture"/>, a traced recording is run first whose Stop measured that cost a capture, so the
    /// stack estimate weighs it (25 captures a second for A::F under Standard).</summary>
    public static (LiveFuncsPanel panel, LiveFuncsViewModel vm) LaidWithAStackChosen(double? usPerCapture = null,
                                                                                     long availableBytes = long.MaxValue)
    {
        var (panel, vm) = Laid(availableBytes: availableBytes);
        Run(vm.RefreshCommand.ExecuteAsync(null));
        vm.TraceEnabled = true;
        Run(vm.ToggleStackCommand.ExecuteAsync(vm.Results.Single(r => r.FuncName == "F")));
        Assert.True(vm.HasStackChoices, "A::F was not chosen for a stack");
        if (usPerCapture is double us)
        {
            Canned.StopStack = new StackInfo
            {
                Rings = 1, Depth = 16, Captures = 100, SpentTicks = (ulong)(us * 100 * 10), MaxTicks = (ulong)(us * 10),
            };
            Run(vm.StartCommand.ExecuteAsync(null));
            Assert.True(vm.IsRecording, "the traced Start did not start");
            Run(vm.StopCommand.ExecuteAsync(null));
            Assert.False(vm.IsRecording);
        }
        Dispatcher.UIThread.RunJobs();
        return (panel, vm);
    }

    /// <summary>A command's task, run to its end: the canned service answers at once, so nothing waits on the
    /// dispatcher the body runs on.</summary>
    private static void Run(Task task)
    {
        Dispatcher.UIThread.RunJobs();
        Assert.True(task.IsCompleted, "a command waited on something the canned service never answers");
        task.GetAwaiter().GetResult();
    }

    /// <summary>A visual's box in the panel's coordinates.</summary>
    public static Rect Box(Visual v, Visual panel) => new(v.TranslatePoint(default, panel)!.Value, v.Bounds.Size);

    public static double GridTop(Visual panel)
        => Box(panel.GetVisualDescendants().OfType<DataGrid>().Single(), panel).Top;

    /// <summary>The TextBlocks showing <paramref name="text"/>, shown or not.</summary>
    public static List<TextBlock> TextBlocks(Visual panel, string text)
        => panel.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text == text).ToList();

    /// <summary>The one TextBlock showing <paramref name="text"/> that is on screen.</summary>
    public static TextBlock Shown(Visual panel, string text)
    {
        var shown = TextBlocks(panel, text).Where(t => t.IsEffectivelyVisible).ToList();
        Assert.True(shown.Count == 1, $"{shown.Count} TextBlocks on screen show \"{text}\"");
        return shown[0];
    }

    /// <summary>The lines a TextBlock wraps its text to, as laid out.</summary>
    public static int Lines(TextBlock block) => block.TextLayout.TextLines.Count;

    /// <summary>The headless platform draws every glyph as wide as the font size (a hundred 'x' at 12 px measure
    /// 1,200 px), where the app's own font, Inter Regular, averages about 5.6 px a character of English text at 12 px
    /// (its advance widths, read from the font Avalonia.Fonts.Inter embeds, 2026-10-08): a line the app shows on one
    /// line can take two here, and push what follows it in a WrapPanel onto the next. So "on one line in the app" is held
    /// as parts that need, unwrapped and side by side, less than 1.5 times the room here: under 0.7 of it in Inter.</summary>
    public static void AssertOneLineInTheAppsFont(double room, params Control[] parts)
    {
        double needed = parts.Sum(Unwrapped);
        Assert.True(needed < 1.5 * room,
                    $"{string.Join(" + ", parts.Select(p => $"{p.GetType().Name} {Unwrapped(p):0.#}"))} = {needed:0.#} px here, "
                    + $"{needed / room:0.##} of the {room:0.#} px room: more than one line in the app's font");
    }

    /// <summary>A part's width on one line: a TextBlock measured again without its room, anything else as laid out (a
    /// button does not wrap).</summary>
    private static double Unwrapped(Control part)
    {
        if (part is not TextBlock text) return part.DesiredSize.Width;
        var unwrapped = new TextBlock { Text = text.Text, FontSize = text.FontSize, FontFamily = text.FontFamily, Margin = text.Margin };
        unwrapped.Measure(Size.Infinity);
        return unwrapped.DesiredSize.Width;
    }
}
