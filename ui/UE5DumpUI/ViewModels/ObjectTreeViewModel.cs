using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Object Tree panel.
/// </summary>
public partial class ObjectTreeViewModel : ViewModelBase
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;
    private readonly IPlatformService _platform;

    [ObservableProperty] private ObservableCollection<UObjectNode> _nodes = new();
    [ObservableProperty] private UObjectNode? _selectedNode;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _objectCount;

    /// <summary>Fired when the selected node changes, for cross-VM communication.</summary>
    public event Action<UObjectNode?>? SelectionChanged;

    partial void OnSelectedNodeChanged(UObjectNode? value)
    {
        SelectionChanged?.Invoke(value);
    }

    public ObjectTreeViewModel(IDumpService dump, ILoggingService log, IPlatformService platform)
    {
        _dump = dump;
        _log = log;
        _platform = platform;
    }

    [RelayCommand]
    private async Task CopyClassNameAsync(UObjectNode? node)
    {
        if (node == null || string.IsNullOrEmpty(node.ClassName)) return;
        await _platform.CopyToClipboardAsync(node.ClassName);
    }

    [RelayCommand]
    private async Task CopyObjectNameAsync(UObjectNode? node)
    {
        if (node == null || string.IsNullOrEmpty(node.Name)) return;
        await _platform.CopyToClipboardAsync(node.Name);
    }

    [RelayCommand]
    private async Task CopyAddressAsync(UObjectNode? node)
    {
        if (node == null || string.IsNullOrEmpty(node.Address)) return;
        await _platform.CopyToClipboardAsync(node.Address);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            ClearError();
            IsLoading = true;
            Nodes.Clear();

            int offset = 0;
            int total = 0;

            do
            {
                var result = await _dump.GetObjectListAsync(offset, Constants.DefaultPageSize);
                total = result.Total;
                ObjectCount = total;

                foreach (var obj in result.Objects)
                {
                    Nodes.Add(obj);
                }

                offset += result.Objects.Count;

                // Limit initial load to prevent UI freeze
                if (offset >= 2000) break;

            } while (offset < total);

            _log.Info($"Loaded {Nodes.Count} of {total} objects");
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error("Failed to load objects", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            // Empty search: reload the full list
            await LoadAsync();
            return;
        }

        try
        {
            ClearError();
            IsLoading = true;

            // Server-side case-insensitive partial search across ALL objects
            var result = await _dump.SearchObjectsAsync(SearchText, 2000);
            Nodes.Clear();
            ObjectCount = result.Total;

            foreach (var obj in result.Objects)
            {
                Nodes.Add(obj);
            }

            if (Nodes.Count > 0)
            {
                SelectedNode = Nodes[0];
            }

            _log.Info($"Search '{SearchText}': found {result.Total} results");
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"Search failed for '{SearchText}'", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
