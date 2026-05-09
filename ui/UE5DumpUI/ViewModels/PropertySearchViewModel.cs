using System.Collections.ObjectModel;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Property Search panel.
/// Search for property names across all UClass objects.
/// </summary>
public partial class PropertySearchViewModel : ViewModelBase
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;

    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private string _typeFilter = "";
    [ObservableProperty] private bool _gameClassesOnly = true;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private ObservableCollection<PropertySearchMatch> _results = new();
    [ObservableProperty] private PropertySearchMatch? _selectedResult;

    /// <summary>
    /// Event raised when user wants to find instances of a class in Instance Finder.
    /// </summary>
    public event Action<string>? NavigateToInstanceFinder;

    /// <summary>
    /// Event raised when user wants to navigate to a class address in Live Walker.
    /// </summary>
    public event Action<string>? NavigateToLiveWalker;

    public PropertySearchViewModel(IDumpService dump, ILoggingService log)
    {
        _dump = dump;
        _log = log;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var trimmedQuery = (SearchQuery ?? "").Trim();
        // Parse comma-separated type filter; whitespace-only entries dropped.
        var types = (TypeFilter ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // DLL requires at least one constraint — name OR type.
        if (string.IsNullOrEmpty(trimmedQuery) && types.Length == 0)
        {
            StatusText = "Enter a property name or type filter";
            return;
        }

        try
        {
            ClearError();
            IsSearching = true;
            StatusText = "Searching...";

            var result = await _dump.SearchPropertiesAsync(
                trimmedQuery,
                types: types.Length > 0 ? types : null,
                gameOnly: GameClassesOnly);

            Results.Clear();
            foreach (var m in result.Results)
            {
                Results.Add(m);
            }

            var typeSuffix = types.Length > 0 ? $" [types: {string.Join(",", types)}]" : "";
            StatusText = $"Found {result.Total} properties in {result.ScannedClasses:N0} classes (scanned {result.ScannedObjects:N0} objects)";
            _log.Info($"SearchProperties: '{trimmedQuery}'{typeSuffix} -> {result.Total} results (classes={result.ScannedClasses}, objects={result.ScannedObjects})");
        }
        catch (Exception ex)
        {
            SetError(ex);
            StatusText = "Search failed";
            _log.Error($"SearchProperties failed for '{SearchQuery}'", ex);
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private void FindInstances(PropertySearchMatch? match)
    {
        if (match == null) return;
        NavigateToInstanceFinder?.Invoke(match.ClassName);
    }

    [RelayCommand]
    private void OpenInWalker(PropertySearchMatch? match)
    {
        if (match == null || string.IsNullOrEmpty(match.ClassAddr)) return;
        NavigateToLiveWalker?.Invoke(match.ClassAddr);
    }

    [RelayCommand]
    private void CopyOffset(PropertySearchMatch? match)
    {
        if (match == null) return;
        Avalonia.Input.Platform.IClipboard? clipboard = null;
        if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            clipboard = desktop.MainWindow?.Clipboard;
        }
        clipboard?.SetTextAsync(match.OffsetHex);
    }
}
