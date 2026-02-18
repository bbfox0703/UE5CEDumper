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

    public ObjectTreeViewModel(IDumpService dump, ILoggingService log)
    {
        _dump = dump;
        _log = log;
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
        if (string.IsNullOrWhiteSpace(SearchText)) return;

        try
        {
            ClearError();
            IsLoading = true;

            var result = await _dump.FindObjectAsync(SearchText);
            if (!string.IsNullOrEmpty(result.Address) && result.Address != "0x0")
            {
                // Find and select in tree, or add as search result
                var node = new UObjectNode
                {
                    Address = result.Address,
                    Name = result.Name,
                };
                SelectedNode = node;
            }
            else
            {
                // Client-side filter
                var filtered = Nodes.Where(n =>
                    n.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();

                if (filtered.Count > 0)
                {
                    SelectedNode = filtered[0];
                }
            }
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
