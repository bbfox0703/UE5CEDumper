using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UE5DumpUI.Core;
using UE5DumpUI.ViewModels;
using UE5DumpUI.Views;
using Xunit;
using static UE5DumpUI.HeadlessTests.LiveFuncsLayout;

namespace UE5DumpUI.HeadlessTests;

/// <summary>
/// [LW-CRUMB-UNDERSCORE] A Button whose Content is a bound string draws it through an access-key presenter, which takes
/// the first <c>_</c> as the mnemonic marker and drops it: Live Walker's crumb read "DumperTestActor0" for
/// <c>DumperTestActor_0</c> and "ArrChurn" for <c>Arr_Churn</c> (seen live on build 3659). Game names are full of
/// underscores, so a crumb must show its label as plain text. Negative control: bind the crumb Button's Content to the
/// label again and this fails.
/// </summary>
public class CrumbUnderscoreTests
{
    [Fact]
    public Task A_crumb_shows_a_name_with_an_underscore_whole() => Headless.Run(() =>
    {
        LoadAppResources();
        var dump = DispatchProxy.Create<IDumpService, Canned>();
        var log = DispatchProxy.Create<ILoggingService, Canned>();
        var platform = DispatchProxy.Create<IPlatformService, Memory>();
        var vm = new LiveWalkerViewModel(dump, log, platform);
        vm.Breadcrumbs.Add(new BreadcrumbItem { Label = "DumperTestActor_0" });
        vm.Breadcrumbs.Add(new BreadcrumbItem { Label = "Arr_Churn [4 x IntProperty]" });
        var panel = new LiveWalkerPanel { DataContext = vm };
        var window = new Window { Content = panel, Width = 1389, Height = 868 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var crumbs = panel.GetVisualDescendants().OfType<Button>().Where(b => b.DataContext is BreadcrumbItem).ToList();
        Assert.Equal(2, crumbs.Count);
        foreach (var crumb in crumbs)
        {
            var label = ((BreadcrumbItem)crumb.DataContext!).Label;
            var shown = crumb.GetVisualDescendants().OfType<TextBlock>().ToList();
            Assert.DoesNotContain(shown, t => t is AccessText);
            Assert.Contains(shown, t => t.Text == label);
        }
        window.Close();
    });
}
