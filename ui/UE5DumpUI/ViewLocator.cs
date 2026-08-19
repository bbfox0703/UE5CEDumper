using Avalonia.Controls;
using Avalonia.Controls.Templates;
using UE5DumpUI.ViewModels;
using UE5DumpUI.Views;

namespace UE5DumpUI;

/// <summary>
/// Explicit ViewLocator — no reflection, AOT compatible.
///
/// <para><b>Match is deliberately wider than Build, and this is not an oversight</b>
/// (audit #5 AF27). <c>Match</c> accepts all <b>21</b> concrete <see cref="ViewModelBase"/>
/// subclasses; <c>Build</c> has arms for <b>6</b>. The other 15 fall to the <c>_ =&gt;</c> arm
/// and render "View not found: X". Narrowing <c>Match</c> to the 6 would NOT be an improvement:
/// Avalonia would then apply its default template and render <c>ToString()</c>, i.e. the type
/// name with no indication that a view is missing. A visible diagnostic beats a silent wrong
/// render, so the fallback arm is the feature.</para>
///
/// <para><b>The locator is dormant today.</b> <c>App.axaml</c> registers it as an app-wide
/// <c>DataTemplate</c>, but every panel is instantiated directly in <c>MainWindow.axaml</c>
/// rather than by assigning a ViewModel to <c>ContentControl.Content</c>, so nothing currently
/// routes through it. The first code that DOES route a ViewModel through content templating
/// must add its arm below — otherwise that panel silently becomes a TextBlock.</para>
///
/// <para>Counts drift: re-derive with
/// <c>grep -rl ": ViewModelBase" ui/UE5DumpUI/ViewModels/*.cs | wc -l</c> against the arm count
/// here, rather than trusting the numbers above.</para>
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? param)
    {
        return param switch
        {
            ObjectTreeViewModel => new ObjectTreePanel(),
            ClassStructViewModel => new ClassStructPanel(),
            PointerPanelViewModel => new PointerPanel(),
            ProxyDeployViewModel => new ProxyDeployPanel(),
            LiveWalkerViewModel => new LiveWalkerPanel(),
            InstanceFinderViewModel => new InstanceFinderPanel(),
            _ => new TextBlock { Text = "View not found: " + param?.GetType().Name }
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
