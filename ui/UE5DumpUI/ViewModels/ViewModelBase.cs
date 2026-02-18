using CommunityToolkit.Mvvm.ComponentModel;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// Base class for all ViewModels.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    protected void ClearError() => ErrorMessage = null;

    protected void SetError(string message) => ErrorMessage = message;

    protected void SetError(Exception ex) => ErrorMessage = ex.Message;
}
