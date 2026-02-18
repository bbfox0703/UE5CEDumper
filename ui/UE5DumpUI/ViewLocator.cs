using Avalonia.Controls;
using Avalonia.Controls.Templates;
using UE5DumpUI.ViewModels;
using UE5DumpUI.Views;

namespace UE5DumpUI;

/// <summary>
/// Explicit ViewLocator — no reflection, AOT compatible.
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
            HexViewViewModel => new HexViewPanel(),
            _ => new TextBlock { Text = "View not found: " + param?.GetType().Name }
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
