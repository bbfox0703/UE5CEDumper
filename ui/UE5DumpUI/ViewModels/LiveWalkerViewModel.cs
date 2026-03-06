using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Live Data Walker panel.
/// Browse GWorld hierarchy and navigate into any UObject by clicking pointers.
/// </summary>
public partial class LiveWalkerViewModel : ViewModelBase
{
    private readonly IDumpService _dump;
    private readonly ILoggingService _log;
    private readonly IPlatformService _platform;
    private readonly IAobMakerBridge? _aobMaker;

    // Cached GWorld walk result for back-navigation
    private WorldWalkResult? _cachedWorld;

    // Engine state for CE address formatting
    private EngineState? _engineState;

    // Navigation breadcrumb stack
    [ObservableProperty] private ObservableCollection<BreadcrumbItem> _breadcrumbs = new();
    [ObservableProperty] private ObservableCollection<LiveFieldValue> _fields = new();
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _currentObjectName = "";
    [ObservableProperty] private string _currentClassName = "";
    [ObservableProperty] private string _currentAddress = "";
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private LiveFieldValue? _selectedField;
    [ObservableProperty] private string _currentOuterAddr = "";
    [ObservableProperty] private string _currentOuterName = "";
    [ObservableProperty] private string _currentOuterClassName = "";
    [ObservableProperty] private bool _hasParent;
    // UFunction display
    [ObservableProperty] private ObservableCollection<FunctionInfoModel> _functions = new();
    [ObservableProperty] private bool _hasFunctions;
    [ObservableProperty] private FunctionInfoModel? _selectedFunction;
    private string _currentClassAddr = "";
    private bool _isDefinitionView;  // True when displaying a class/struct definition (no live data)
    private DataTableWalkResult? _cachedDataTableRows;  // Cached DataTable row data

    // CE XML output (kept for possible future use but no longer shown in panel)
    [ObservableProperty] private string _ceXmlOutput = "";
    [ObservableProperty] private bool _showCeXml;

    // Address format
    [ObservableProperty] private int _selectedAddressFormatIndex;
    private AddressFormat AddrFormat => (AddressFormat)SelectedAddressFormatIndex;

    /// <summary>Whether CE XML export should collapse pointer/array nodes.</summary>
    public bool CollapsePointerNodes { get; set; }

    /// <summary>Max array element count for inline reading (2^N, default 64).</summary>
    private int _arrayLimit = 64;
    public int ArrayLimit
    {
        get => _arrayLimit;
        set
        {
            if (_arrayLimit == value) return;
            _arrayLimit = value;
            // Auto-refresh current view with new limit
            if (!string.IsNullOrEmpty(CurrentAddress))
                RefreshCommand.Execute(null);
        }
    }

    /// <summary>Max struct sub-fields to show in preview (0 = none, default 2, max 6).</summary>
    private int _previewLimit = 2;
    public int PreviewLimit
    {
        get => _previewLimit;
        set
        {
            if (_previewLimit == value) return;
            _previewLimit = value;
            // Auto-refresh current view with new limit
            if (!string.IsNullOrEmpty(CurrentAddress))
                RefreshCommand.Execute(null);
        }
    }

    /// <summary>Max CE DropDownList entries (2^N, default 512). Used during CE XML export.</summary>
    public int DropDownLimit { get; set; } = 512;

    /// <summary>CSX drilldown depth (0 = flat/dummy, 1+ = real child structures for ObjectProperty).</summary>
    public int CsxDrilldownDepth { get; set; }

    // Search
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private int _searchMatchCount;
    [ObservableProperty] private bool _hasSearchResults;

    // AOBMaker CE Plugin integration
    [ObservableProperty] private bool _isAobMakerAvailable;

    // Auto-refresh
    [ObservableProperty] private bool _isAutoRefreshing;
    [ObservableProperty] private int _autoRefreshIntervalSec = Constants.DefaultAutoRefreshIntervalSec;
    [ObservableProperty] private int _autoRefreshMinSec = Constants.MinAutoRefreshIntervalSec;
    [ObservableProperty] private string _autoRefreshStatusText = "sec";
    private DispatcherTimer? _autoRefreshTimer;
    private DispatcherTimer? _countdownTimer;
    private int _countdownRemaining;
    private bool _isAutoRefreshBenchmarked;
    private bool _isAutoRefreshing_InProgress; // Guard against overlapping refreshes

    /// <summary>
    /// Raised when the View should scroll the DataGrid to a specific field name.
    /// The View subscribes to this and calls ScrollIntoView on the DataGrid.
    /// </summary>
    public event Action<string>? ScrollToFieldRequested;

    /// <summary>
    /// Raised when the View should scroll the DataGrid to the first search match.
    /// </summary>
    public event Action? ScrollToFirstSearchMatch;
    private string _lastScrolledSearchText = "";

    public LiveWalkerViewModel(IDumpService dump, ILoggingService log, IPlatformService platform,
                               IAobMakerBridge? aobMaker = null)
    {
        _dump = dump;
        _log = log;
        _platform = platform;
        _aobMaker = aobMaker;
    }

    public void SetEngineState(EngineState state)
    {
        _engineState = state;
    }

    /// <summary>Clear both error message and status text (e.g., container limit warnings).</summary>
    private void ClearStatus()
    {
        ClearError();
        StatusText = "";
    }

    [RelayCommand]
    private async Task StartFromWorldAsync()
    {
        try
        {
            ClearStatus();
            IsLoading = true;
            StopAutoRefreshTimer();

            var world = await _dump.WalkWorldAsync(500, arrayLimit: ArrayLimit);
            _cachedWorld = world;

            Breadcrumbs.Clear();
            Breadcrumbs.Add(new BreadcrumbItem
            {
                Address = world.WorldAddr,
                Label = "GWorld",
                IsPointerDeref = true,
                FieldOffset = 0,
                FieldName = "GWorld",
            });

            PopulateFromWorld(world);

            // Show DLL-side error if world walk was partial (e.g. PersistentLevel not found)
            if (!string.IsNullOrEmpty(world.Error))
            {
                SetError(new InvalidOperationException(world.Error));
            }
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error("Failed to load GWorld", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void PopulateFromWorld(WorldWalkResult world)
    {
        CurrentObjectName = world.WorldName;
        CurrentClassName = "UWorld";
        CurrentAddress = world.WorldAddr;
        HasData = true;
        ShowCeXml = false;
        HasParent = false;
        _isDefinitionView = false;
        CurrentOuterAddr = "";
        CurrentOuterName = "";
        CurrentOuterClassName = "";

        Fields.Clear();

        // Compute base address for FieldAddress display
        ulong worldBase = 0;
        try
        {
            if (!string.IsNullOrEmpty(world.WorldAddr))
                worldBase = Convert.ToUInt64(world.WorldAddr.Replace("0x", "").Replace("0X", ""), 16);
        }
        catch { /* ignore parse failures */ }

        // PersistentLevel as first navigable entry (offset from DLL walk_world response)
        if (!string.IsNullOrEmpty(world.LevelAddr) && world.LevelAddr != "0x0")
        {
            var pLevel = new LiveFieldValue
            {
                Name = world.LevelName ?? "PersistentLevel",
                TypeName = "ObjectProperty",
                Offset = world.LevelOffset,
                Size = 8,
                PtrAddress = world.LevelAddr,
                PtrName = world.LevelName ?? "PersistentLevel",
                PtrClassName = "ULevel",
            };
            if (worldBase != 0)
                pLevel.FieldAddress = $"0x{worldBase + (ulong)world.LevelOffset:X}";
            Fields.Add(pLevel);
        }

        // Each actor as a navigable entry
        foreach (var actor in world.Actors)
        {
            Fields.Add(new LiveFieldValue
            {
                Name = actor.Name,
                TypeName = "ObjectProperty",
                Offset = 0,
                Size = 8,
                PtrAddress = actor.Address,
                PtrName = actor.Name,
                PtrClassName = actor.ClassName,
            });

            // Components as indented sub-entries
            foreach (var comp in actor.Components)
            {
                Fields.Add(new LiveFieldValue
                {
                    Name = $"  {actor.Name}.{comp.Name}",
                    TypeName = "ObjectProperty",
                    Offset = 0,
                    Size = 8,
                    PtrAddress = comp.Address,
                    PtrName = comp.Name,
                    PtrClassName = comp.ClassName,
                });
            }
        }
    }

    [RelayCommand]
    private async Task NavigateToFieldAsync(LiveFieldValue? field)
    {
        if (field == null || !field.IsNavigable) return;

        try
        {
            ClearStatus();
            IsLoading = true;

            // Save the clicked field name on the current breadcrumb for scroll restoration on Back
            if (Breadcrumbs.Count > 0)
                Breadcrumbs[^1].ScrollHintFieldName = field.Name;

            if (!string.IsNullOrEmpty(field.PtrAddress) && field.PtrAddress != "0x0")
            {
                // ObjectProperty navigation (pointer dereference)
                await NavigateToAsync(field.PtrAddress, field.Name, field.Offset, field.Name, isPointer: true);
            }
            else if (!string.IsNullOrEmpty(field.StructDataAddr) && field.StructDataAddr != "0x0")
            {
                // StructProperty navigation: walk struct data using its class
                var result = await _dump.WalkInstanceAsync(field.StructDataAddr, field.StructClassAddr, arrayLimit: ArrayLimit, previewLimit: PreviewLimit);
                var displayName = !string.IsNullOrEmpty(field.StructTypeName)
                    ? $"{field.Name} ({field.StructTypeName})"
                    : field.Name;

                // DataTable row navigation: the uint8* is a pointer that needs dereference,
                // not an inline struct. Set IsPointerDeref=true for correct CE XML pointer chain.
                var isDataTableRow = Breadcrumbs.Count > 0 && Breadcrumbs[^1].IsDataTableView;

                Breadcrumbs.Add(new BreadcrumbItem
                {
                    Address = field.StructDataAddr,
                    Label = displayName,
                    ClassAddr = field.StructClassAddr,
                    FieldOffset = field.Offset,
                    FieldName = field.Name,
                    IsPointerDeref = isDataTableRow,
                });
                _log.Info($"NAV→Struct {field.Name} addr={field.StructDataAddr} off=0x{field.Offset:X} dtRow={isDataTableRow} | BC={FormatBreadcrumbTrace()}");
                UpdateDisplay(result);
            }
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"Failed to navigate to {field.Name}", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task NavigateToContainerAsync(LiveFieldValue? field)
    {
        if (field == null || !field.IsContainerNavigable) return;

        try
        {
            ClearStatus();
            IsLoading = true;

            // Save scroll hint on current breadcrumb
            if (Breadcrumbs.Count > 0)
                Breadcrumbs[^1].ScrollHintFieldName = field.Name;

            if (field.DataTableRowCount > 0 && _cachedDataTableRows != null)
            {
                NavigateToDataTableContainer(field, _cachedDataTableRows);
            }
            else if (field.ArrayCount > 0 && !string.IsNullOrEmpty(field.ArrayInnerType))
            {
                await NavigateToArrayContainerAsync(field);
            }
            else if (field.MapCount > 0 && !string.IsNullOrEmpty(field.MapKeyType))
            {
                NavigateToMapContainer(field);
            }
            else if (field.SetCount > 0 && !string.IsNullOrEmpty(field.SetElemType))
            {
                NavigateToSetContainer(field);
            }
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"Failed to navigate to container {field.Name}", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task NavigateToArrayContainerAsync(LiveFieldValue field)
    {
        var typeLabel = !string.IsNullOrEmpty(field.ArrayStructType)
            ? field.ArrayStructType : field.ArrayInnerType;
        var label = $"{field.Name} [{field.ArrayCount} x {typeLabel}]";

        Breadcrumbs.Add(new BreadcrumbItem
        {
            Address = CurrentAddress,
            Label = label,
            FieldOffset = field.Offset,
            FieldName = field.Name,
            IsPointerDeref = false,
            IsContainerView = true,
            ContainerField = field,
        });
        _log.Info($"NAV→Container {field.Name} addr={CurrentAddress} off=0x{field.Offset:X} | BC={FormatBreadcrumbTrace()}");

        List<ArrayElementValue> elements;
        if (field.ArrayElements != null && field.ArrayElements.Count >= field.ArrayCount)
        {
            // All elements already inline (complete set)
            elements = field.ArrayElements;
        }
        else if (field.ArrayElements is { Count: > 0 } && IsPointerOrStructArrayType(field.ArrayInnerType))
        {
            // Pointer/struct arrays: use inline elements (Phase D/E/F resolved names).
            // read_array_elements is scalar-only and cannot resolve pointer names.
            elements = field.ArrayElements;
        }
        else if (!string.IsNullOrEmpty(field.ArrayInnerAddr) && !string.IsNullOrEmpty(CurrentAddress))
        {
            // Scalar arrays: fetch full element list from DLL (Phase B)
            var result = await _dump.ReadArrayElementsAsync(
                CurrentAddress, field.Offset, field.ArrayInnerAddr,
                field.ArrayInnerType, field.ArrayElemSize, 0, field.ArrayCount);
            elements = result.Elements;
        }
        else
        {
            elements = field.ArrayElements ?? new();
        }

        PopulateArrayContainerFields(elements, field);
    }

    private void NavigateToMapContainer(LiveFieldValue field)
    {
        var keyLabel = !string.IsNullOrEmpty(field.MapKeyType) ? field.MapKeyType : "?";
        var valLabel = !string.IsNullOrEmpty(field.MapValueType) ? field.MapValueType : "?";
        var label = $"{field.Name} {{Map: {field.MapCount}, {keyLabel} \u2192 {valLabel}}}";

        Breadcrumbs.Add(new BreadcrumbItem
        {
            Address = CurrentAddress,
            Label = label,
            FieldOffset = field.Offset,
            FieldName = field.Name,
            IsPointerDeref = false,
            IsContainerView = true,
            ContainerField = field,
        });
        _log.Info($"NAV→MapContainer {field.Name} addr={CurrentAddress} off=0x{field.Offset:X} | BC={FormatBreadcrumbTrace()}");

        PopulateMapContainerFields(field.MapElements ?? new(), field);
    }

    private void NavigateToSetContainer(LiveFieldValue field)
    {
        var elemLabel = !string.IsNullOrEmpty(field.SetElemType) ? field.SetElemType : "?";
        var label = $"{field.Name} {{Set: {field.SetCount}, {elemLabel}}}";

        Breadcrumbs.Add(new BreadcrumbItem
        {
            Address = CurrentAddress,
            Label = label,
            FieldOffset = field.Offset,
            FieldName = field.Name,
            IsPointerDeref = false,
            IsContainerView = true,
            ContainerField = field,
        });
        _log.Info($"NAV→SetContainer {field.Name} addr={CurrentAddress} off=0x{field.Offset:X} | BC={FormatBreadcrumbTrace()}");

        PopulateSetContainerFields(field.SetElements ?? new(), field);
    }

    private void NavigateToDataTableContainer(LiveFieldValue field, DataTableWalkResult dtResult)
    {
        var label = $"RowMap [{dtResult.RowCount} x {dtResult.RowStructName}]";

        Breadcrumbs.Add(new BreadcrumbItem
        {
            Address = CurrentAddress,
            Label = label,
            FieldOffset = dtResult.RowMapOffset,
            FieldName = field.Name,
            IsPointerDeref = false,
            IsContainerView = true,
            IsDataTableView = true,
            ContainerField = field,
            DataTableData = dtResult,
        });
        _log.Info($"NAV\u2192DataTable {field.Name} addr={CurrentAddress} rows={dtResult.RowCount} struct={dtResult.RowStructName} | BC={FormatBreadcrumbTrace()}");

        PopulateDataTableRowFields(dtResult);
    }

    private void PopulateDataTableRowFields(DataTableWalkResult dtResult)
    {
        CurrentObjectName = "RowMap";
        CurrentClassName = $"DataTable<{dtResult.RowStructName}>";
        HasData = true;
        ShowCeXml = false;
        HasParent = false;
        CurrentOuterAddr = "";
        CurrentOuterName = "";
        CurrentOuterClassName = "";

        Fields.Clear();
        foreach (var row in dtResult.Rows)
        {
            // Build preview from first 2 scalar fields
            var preview = "";
            var previewParts = new List<string>();
            foreach (var fv in row.Fields)
            {
                if (previewParts.Count >= 2) break;
                if (!string.IsNullOrEmpty(fv.TypedValue) && fv.TypedValue != "0" && fv.TypedValue != "0.0"
                    && fv.TypeName != "ObjectProperty" && fv.TypeName != "ClassProperty")
                {
                    previewParts.Add($"{fv.Name}={fv.TypedValue}");
                }
                else if (!string.IsNullOrEmpty(fv.StrValue))
                {
                    var s = fv.StrValue.Length > 30 ? fv.StrValue[..30] + "..." : fv.StrValue;
                    previewParts.Add($"{fv.Name}=\"{s}\"");
                }
                else if (!string.IsNullOrEmpty(fv.PtrName))
                {
                    previewParts.Add($"{fv.Name}={fv.PtrName}");
                }
            }
            if (previewParts.Count > 0)
                preview = " | " + string.Join(", ", previewParts);

            // Actual byte offset of the uint8* pointer within TSparseArray data
            int rowPtrOffset = row.SparseIndex * dtResult.Stride + dtResult.FNameSize;
            var f = new LiveFieldValue
            {
                Name = $"[{row.SparseIndex}] {row.RowName}",
                TypeName = "StructProperty",
                Offset = rowPtrOffset,
                Size = 0,
                TypedValue = $"{{{dtResult.RowStructName}}}{preview}",
                // Enable struct navigation to drill into the row data
                StructDataAddr = row.DataAddr,
                StructClassAddr = dtResult.RowStructAddr,
                StructTypeName = dtResult.RowStructName,
            };
            if (!string.IsNullOrEmpty(row.DataAddr))
                f.FieldAddress = row.DataAddr;
            Fields.Add(f);
        }
    }

    private void PopulateArrayContainerFields(List<ArrayElementValue> elements, LiveFieldValue sourceField)
    {
        var typeLabel = !string.IsNullOrEmpty(sourceField.ArrayStructType)
            ? sourceField.ArrayStructType : sourceField.ArrayInnerType;
        CurrentObjectName = sourceField.Name;
        CurrentClassName = $"Array<{typeLabel}>";
        HasData = true;
        ShowCeXml = false;
        // Disable Parent button for container views (not a UObject)
        HasParent = false;
        CurrentOuterAddr = "";
        CurrentOuterName = "";
        CurrentOuterClassName = "";

        // Parse TArray::Data base address for computing element addresses
        ulong dataBase = 0;
        if (!string.IsNullOrEmpty(sourceField.ArrayDataAddr))
            ulong.TryParse(sourceField.ArrayDataAddr.Replace("0x", "").Replace("0X", ""),
                System.Globalization.NumberStyles.HexNumber, null, out dataBase);

        // Check if this is a struct array with navigation metadata
        bool isStructArray = sourceField.ArrayInnerType == "StructProperty"
            && !string.IsNullOrEmpty(sourceField.ArrayStructClassAddr);

        Fields.Clear();
        foreach (var elem in elements)
        {
            // Compute element address for struct navigation
            var elemAddr = (isStructArray && dataBase != 0 && sourceField.ArrayElemSize > 0)
                ? $"0x{dataBase + (ulong)(elem.Index * sourceField.ArrayElemSize):X}" : "";

            var f = new LiveFieldValue
            {
                Name = $"[{elem.Index}]",
                TypeName = sourceField.ArrayInnerType,
                Offset = elem.Index * sourceField.ArrayElemSize,
                Size = sourceField.ArrayElemSize,
                HexValue = elem.Hex,
                TypedValue = !string.IsNullOrEmpty(elem.PtrName)
                    ? (!string.IsNullOrEmpty(elem.PtrClassName)
                        ? $"{elem.PtrName} ({elem.PtrClassName})"
                        : elem.PtrName)
                    : (!string.IsNullOrEmpty(elem.EnumName) ? elem.EnumName : elem.Value),
                PtrAddress = elem.PtrAddress,
                PtrName = elem.PtrName,
                PtrClassName = elem.PtrClassName,
                EnumName = elem.EnumName,
                // Struct navigation for StructProperty elements
                StructDataAddr = elemAddr,
                StructClassAddr = isStructArray ? sourceField.ArrayStructClassAddr : "",
                StructTypeName = isStructArray ? sourceField.ArrayStructType : "",
            };
            if (dataBase != 0 && sourceField.ArrayElemSize > 0)
                f.FieldAddress = $"0x{dataBase + (ulong)(elem.Index * sourceField.ArrayElemSize):X}";
            Fields.Add(f);
        }
    }

    private void PopulateMapContainerFields(List<ContainerElementValue> elements, LiveFieldValue sourceField)
    {
        var keyLabel = !string.IsNullOrEmpty(sourceField.MapKeyType) ? sourceField.MapKeyType : "?";
        var valLabel = !string.IsNullOrEmpty(sourceField.MapValueType) ? sourceField.MapValueType : "?";
        CurrentObjectName = sourceField.Name;
        CurrentClassName = $"Map<{keyLabel}, {valLabel}>";
        HasData = true;
        ShowCeXml = false;
        HasParent = false;
        CurrentOuterAddr = "";
        CurrentOuterName = "";
        CurrentOuterClassName = "";

        // Parse TSparseArray::Data base address for computing element addresses
        ulong dataBase = 0;
        if (!string.IsNullOrEmpty(sourceField.MapDataAddr))
            ulong.TryParse(sourceField.MapDataAddr.Replace("0x", "").Replace("0X", ""),
                System.Globalization.NumberStyles.HexNumber, null, out dataBase);
        int pairSize = sourceField.MapKeySize + sourceField.MapValueSize;
        int stride = ComputeSetElementStride(pairSize);

        // Check if value type is StructProperty with navigation metadata
        bool isStructValue = sourceField.MapValueType == "StructProperty"
            && !string.IsNullOrEmpty(sourceField.MapValueStructAddr);

        Fields.Clear();
        if (elements.Count == 0)
        {
            // Show metadata summary when element data couldn't be read
            StatusText = $"Map has {sourceField.MapCount} entries but element data could not be read (key={keyLabel} sz={sourceField.MapKeySize}, val={valLabel} sz={sourceField.MapValueSize})";
        }
        foreach (var elem in elements)
        {
            var keyDisplay = !string.IsNullOrEmpty(elem.KeyPtrName) ? elem.KeyPtrName : elem.Key;
            var valDisplay = !string.IsNullOrEmpty(elem.ValuePtrName) ? elem.ValuePtrName : elem.Value;

            // Compute value struct address: entry start + keySize
            var valStructAddr = (isStructValue && dataBase != 0 && stride > 0)
                ? $"0x{dataBase + (ulong)(elem.Index * stride) + (ulong)sourceField.MapKeySize:X}" : "";

            var f = new LiveFieldValue
            {
                Name = $"[{elem.Index}] {keyDisplay}",
                TypeName = sourceField.MapValueType,
                Offset = elem.Index * stride,
                Size = sourceField.MapKeySize + sourceField.MapValueSize,
                HexValue = !string.IsNullOrEmpty(elem.ValueHex) ? $"{elem.KeyHex} | {elem.ValueHex}" : elem.KeyHex,
                TypedValue = $"{keyDisplay} \u2192 {valDisplay}",
                // Enable → navigation for ObjectProperty values
                PtrAddress = elem.ValuePtrAddress,
                PtrName = elem.ValuePtrName,
                PtrClassName = elem.ValuePtrClassName,
                // Struct navigation for StructProperty values
                StructDataAddr = valStructAddr,
                StructClassAddr = isStructValue ? sourceField.MapValueStructAddr : "",
                StructTypeName = isStructValue ? sourceField.MapValueStructType : "",
            };
            if (dataBase != 0 && stride > 0)
                f.FieldAddress = $"0x{dataBase + (ulong)(elem.Index * stride):X}";
            Fields.Add(f);
        }
    }

    /// <summary>
    /// Re-populate the container view from a (potentially refreshed) container field.
    /// Dispatches to the appropriate populate helper based on container type.
    /// </summary>
    private void RepopulateContainerView(LiveFieldValue containerField, BreadcrumbItem? bc = null)
    {
        // DataTable rows: use cached DataTableWalkResult from breadcrumb
        if (bc is { IsDataTableView: true, DataTableData: not null })
        {
            PopulateDataTableRowFields(bc.DataTableData);
        }
        else if (containerField.ArrayCount > 0 && !string.IsNullOrEmpty(containerField.ArrayInnerType))
        {
            PopulateArrayContainerFields(containerField.ArrayElements ?? new(), containerField);
        }
        else if (containerField.MapCount > 0 && !string.IsNullOrEmpty(containerField.MapKeyType))
        {
            PopulateMapContainerFields(containerField.MapElements ?? new(), containerField);
        }
        else if (containerField.SetCount > 0 && !string.IsNullOrEmpty(containerField.SetElemType))
        {
            PopulateSetContainerFields(containerField.SetElements ?? new(), containerField);
        }
    }

    private void PopulateSetContainerFields(List<ContainerElementValue> elements, LiveFieldValue sourceField)
    {
        var elemLabel = !string.IsNullOrEmpty(sourceField.SetElemType) ? sourceField.SetElemType : "?";
        CurrentObjectName = sourceField.Name;
        CurrentClassName = $"Set<{elemLabel}>";
        HasData = true;
        ShowCeXml = false;
        HasParent = false;
        CurrentOuterAddr = "";
        CurrentOuterName = "";
        CurrentOuterClassName = "";

        // Parse TSparseArray::Data base address for computing element addresses
        ulong dataBase = 0;
        if (!string.IsNullOrEmpty(sourceField.SetDataAddr))
            ulong.TryParse(sourceField.SetDataAddr.Replace("0x", "").Replace("0X", ""),
                System.Globalization.NumberStyles.HexNumber, null, out dataBase);
        int stride = ComputeSetElementStride(sourceField.SetElemSize);

        // Check if element type is StructProperty with navigation metadata
        bool isStructElem = sourceField.SetElemType == "StructProperty"
            && !string.IsNullOrEmpty(sourceField.SetElemStructAddr);

        Fields.Clear();
        foreach (var elem in elements)
        {
            var display = !string.IsNullOrEmpty(elem.KeyPtrName) ? elem.KeyPtrName : elem.Key;

            // Compute struct element address
            var structAddr = (isStructElem && dataBase != 0 && stride > 0)
                ? $"0x{dataBase + (ulong)(elem.Index * stride):X}" : "";

            var f = new LiveFieldValue
            {
                Name = $"[{elem.Index}]",
                TypeName = sourceField.SetElemType,
                Offset = elem.Index * stride,
                Size = sourceField.SetElemSize,
                HexValue = elem.KeyHex,
                TypedValue = display,
                // Enable → navigation for ObjectProperty elements
                PtrAddress = elem.KeyPtrAddress,
                PtrName = elem.KeyPtrName,
                PtrClassName = elem.KeyPtrClassName,
                // Struct navigation for StructProperty elements
                StructDataAddr = structAddr,
                StructClassAddr = isStructElem ? sourceField.SetElemStructAddr : "",
                StructTypeName = isStructElem ? sourceField.SetElemStructType : "",
            };
            if (dataBase != 0 && stride > 0)
                f.FieldAddress = $"0x{dataBase + (ulong)(elem.Index * stride):X}";
            Fields.Add(f);
        }
    }

    /// <summary>
    /// Create a container field copy with only the element matching the selected synthetic field.
    /// Extracts sparse index from the "[N]" or "[N] description" name pattern.
    /// Used by CE XML export to emit only the selected element within the container.
    /// </summary>
    private static LiveFieldValue FilterContainerToElement(LiveFieldValue containerField, LiveFieldValue selectedField)
    {
        var sparseIndex = ParseSparseIndex(selectedField.Name);
        if (!sparseIndex.HasValue) return containerField;

        if (containerField.DataTableRowCount > 0 && containerField.DataTableRowData != null)
        {
            return new LiveFieldValue
            {
                Name = containerField.Name,
                TypeName = containerField.TypeName,
                Offset = containerField.Offset,
                Size = containerField.Size,
                DataTableRowCount = containerField.DataTableRowCount,
                DataTableStructName = containerField.DataTableStructName,
                DataTableFNameSize = containerField.DataTableFNameSize,
                DataTableStride = containerField.DataTableStride,
                DataTableRowStructAddr = containerField.DataTableRowStructAddr,
                DataTableRowData = containerField.DataTableRowData
                    .Where(r => r.SparseIndex == sparseIndex.Value).ToList(),
            };
        }

        if (containerField.MapCount > 0 && containerField.MapElements != null)
        {
            return new LiveFieldValue
            {
                Name = containerField.Name,
                TypeName = containerField.TypeName,
                Offset = containerField.Offset,
                Size = containerField.Size,
                MapCount = containerField.MapCount,
                MapKeyType = containerField.MapKeyType,
                MapValueType = containerField.MapValueType,
                MapKeySize = containerField.MapKeySize,
                MapValueSize = containerField.MapValueSize,
                MapDataAddr = containerField.MapDataAddr,
                MapKeyStructAddr = containerField.MapKeyStructAddr,
                MapKeyStructType = containerField.MapKeyStructType,
                MapValueStructAddr = containerField.MapValueStructAddr,
                MapValueStructType = containerField.MapValueStructType,
                MapElements = containerField.MapElements.Where(e => e.Index == sparseIndex.Value).ToList(),
            };
        }

        if (containerField.SetCount > 0 && containerField.SetElements != null)
        {
            return new LiveFieldValue
            {
                Name = containerField.Name,
                TypeName = containerField.TypeName,
                Offset = containerField.Offset,
                Size = containerField.Size,
                SetCount = containerField.SetCount,
                SetElemType = containerField.SetElemType,
                SetElemSize = containerField.SetElemSize,
                SetDataAddr = containerField.SetDataAddr,
                SetElemStructAddr = containerField.SetElemStructAddr,
                SetElemStructType = containerField.SetElemStructType,
                SetElements = containerField.SetElements.Where(e => e.Index == sparseIndex.Value).ToList(),
            };
        }

        if (containerField.ArrayCount > 0 && containerField.ArrayElements != null)
        {
            return new LiveFieldValue
            {
                Name = containerField.Name,
                TypeName = containerField.TypeName,
                Offset = containerField.Offset,
                Size = containerField.Size,
                ArrayCount = containerField.ArrayCount,
                ArrayInnerType = containerField.ArrayInnerType,
                ArrayStructType = containerField.ArrayStructType,
                ArrayElemSize = containerField.ArrayElemSize,
                ArrayInnerAddr = containerField.ArrayInnerAddr,
                ArrayDataAddr = containerField.ArrayDataAddr,
                ArrayStructClassAddr = containerField.ArrayStructClassAddr,
                ArrayElements = containerField.ArrayElements.Where(e => e.Index == sparseIndex.Value).ToList(),
                ArrayEnumAddr = containerField.ArrayEnumAddr,
                ArrayEnumEntries = containerField.ArrayEnumEntries,
            };
        }

        return containerField; // fallback: emit whole container
    }

    /// <summary>Parse sparse index from "[N]" or "[N] name" patterns.</summary>
    private static int? ParseSparseIndex(string name)
    {
        if (string.IsNullOrEmpty(name) || name[0] != '[') return null;
        var endBracket = name.IndexOf(']');
        if (endBracket <= 1) return null;
        if (int.TryParse(name.Substring(1, endBracket - 1), out var index))
            return index;
        return null;
    }

    /// <summary>
    /// Check if an array inner type requires Phase D/E/F resolution (pointer names, struct fields).
    /// read_array_elements (Phase B) only handles scalars; pointer/struct arrays must use
    /// the inline elements from walk_instance which have full resolution.
    /// </summary>
    private static bool IsPointerOrStructArrayType(string innerType)
        => innerType is "ObjectProperty" or "ClassProperty"
            or "WeakObjectProperty" or "SoftObjectProperty" or "LazyObjectProperty"
            or "StructProperty";

    /// <summary>
    /// Compute TSparseArray element stride: AlignUp(elemSize, 4) + 8.
    /// Mirrors Mem::ComputeSetElementStride in the DLL and CeXmlExportService.
    /// </summary>
    private static int ComputeSetElementStride(int elemSize)
    {
        int hashStart = (elemSize + 3) & ~3;
        return hashStart + 8;
    }

    /// <summary>
    /// Detect fields whose container element count exceeds the loaded element count.
    /// Returns a warning string listing the truncated fields, or null if none.
    /// </summary>
    private static string? BuildContainerLimitWarning(IEnumerable<LiveFieldValue> fields, int arrayLimit)
    {
        var truncated = new List<string>();
        foreach (var f in fields)
        {
            if (f.ArrayCount > arrayLimit)
            {
                int loaded = f.ArrayElements?.Count ?? 0;
                truncated.Add($"{f.Name} (Array: {f.ArrayCount} total, {loaded} loaded)");
            }
            if (f.MapCount > arrayLimit)
            {
                int loaded = f.MapElements?.Count ?? 0;
                truncated.Add($"{f.Name} (Map: {f.MapCount} total, {loaded} loaded)");
            }
            if (f.SetCount > arrayLimit)
            {
                int loaded = f.SetElements?.Count ?? 0;
                truncated.Add($"{f.Name} (Set: {f.SetCount} total, {loaded} loaded)");
            }
        }
        if (truncated.Count == 0) return null;
        return $"⚠ Container element limit ({arrayLimit}): {string.Join(", ", truncated)}";
    }

    [RelayCommand]
    private async Task NavigateToBreadcrumbAsync(BreadcrumbItem? item)
    {
        if (item == null) return;

        try
        {
            ClearStatus();
            IsLoading = true;

            // Remove all breadcrumbs after this one
            var idx = Breadcrumbs.IndexOf(item);
            if (idx < 0) return;

            var removedCount = Breadcrumbs.Count - idx - 1;
            while (Breadcrumbs.Count > idx + 1)
                Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1);

            _log.Info($"NAV⇒BC[{idx}] {item.FieldName ?? item.Label} removed={removedCount} | BC={FormatBreadcrumbTrace()}");
            var scrollHint = item.ScrollHintFieldName;

            // If navigating back to a container view, re-populate from saved field
            if (item.IsContainerView && item.ContainerField != null)
            {
                RepopulateContainerView(item.ContainerField, item);
                if (!string.IsNullOrEmpty(scrollHint))
                    ScrollToFieldRequested?.Invoke(scrollHint);
                return;
            }

            // If navigating back to GWorld, re-display actor list
            if (_cachedWorld != null && item.Address == _cachedWorld.WorldAddr)
            {
                PopulateFromWorld(_cachedWorld);
                if (!string.IsNullOrEmpty(scrollHint))
                    ScrollToFieldRequested?.Invoke(scrollHint);
                return;
            }

            // Re-walk this object (pass ClassAddr for StructProperty navigation)
            var classAddr = string.IsNullOrEmpty(item.ClassAddr) ? null : item.ClassAddr;
            var result = await _dump.WalkInstanceAsync(item.Address, classAddr, arrayLimit: ArrayLimit, previewLimit: PreviewLimit);
            UpdateDisplay(result);

            if (!string.IsNullOrEmpty(scrollHint))
                ScrollToFieldRequested?.Invoke(scrollHint);
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

    [RelayCommand]
    private async Task GoBackAsync()
    {
        if (Breadcrumbs.Count < 2) return;

        var removed = Breadcrumbs[^1];
        Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1);
        var prev = Breadcrumbs[^1];
        var scrollHint = prev.ScrollHintFieldName;
        _log.Info($"NAV←Back removed={removed.FieldName ?? removed.Label} | BC={FormatBreadcrumbTrace()}");

        try
        {
            ClearStatus();
            IsLoading = true;

            // If going back to a container view, re-populate from saved field
            if (prev.IsContainerView && prev.ContainerField != null)
            {
                RepopulateContainerView(prev.ContainerField, prev);
                if (!string.IsNullOrEmpty(scrollHint))
                    ScrollToFieldRequested?.Invoke(scrollHint);
                return;
            }

            // If going back to GWorld, re-display actor list
            if (_cachedWorld != null && prev.Address == _cachedWorld.WorldAddr)
            {
                PopulateFromWorld(_cachedWorld);
                if (!string.IsNullOrEmpty(scrollHint))
                    ScrollToFieldRequested?.Invoke(scrollHint);
                return;
            }

            var classAddr = string.IsNullOrEmpty(prev.ClassAddr) ? null : prev.ClassAddr;
            var result = await _dump.WalkInstanceAsync(prev.Address, classAddr, arrayLimit: ArrayLimit, previewLimit: PreviewLimit);
            UpdateDisplay(result);

            if (!string.IsNullOrEmpty(scrollHint))
                ScrollToFieldRequested?.Invoke(scrollHint);
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

    [RelayCommand]
    private async Task GoToParentAsync()
    {
        if (string.IsNullOrEmpty(CurrentOuterAddr) || CurrentOuterAddr == "0x0") return;

        try
        {
            ClearStatus();
            IsLoading = true;

            // Navigate to the parent (OuterPrivate) object
            var parentAddr = CurrentOuterAddr;

            // Add current object as a breadcrumb before navigating up
            // so user can go back down via breadcrumbs
            Breadcrumbs.Add(new BreadcrumbItem
            {
                Address = parentAddr,
                Label = !string.IsNullOrEmpty(CurrentOuterName) ? CurrentOuterName : "Parent",
                IsPointerDeref = true,
                FieldOffset = 0,
                FieldName = "Outer",
            });

            var result = await _dump.WalkInstanceAsync(parentAddr, arrayLimit: ArrayLimit, previewLimit: PreviewLimit);
            UpdateDisplay(result);
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"Failed to navigate to parent {CurrentOuterAddr}", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task NavigateToAddressAsync(string? addr)
    {
        if (string.IsNullOrEmpty(addr)) return;

        try
        {
            ClearStatus();
            IsLoading = true;
            StopAutoRefreshTimer();
            Breadcrumbs.Clear();

            // Normalize address: supports CE formats like "module.exe"+offset,
            // quoted module names ("module.exe"+offset), and plain hex
            var normalizedAddr = AddressHelper.NormalizeAddress(addr, _engineState?.ModuleBase);

            await NavigateToAsync(normalizedAddr, "Custom", 0, "Custom", isPointer: true);
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"Failed to navigate to {addr}", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExportCeXmlAsync()
    {
        if (string.IsNullOrEmpty(CurrentAddress) || Breadcrumbs.Count == 0) return;

        try
        {
            ClearStatus();
            IsLoading = true;

            // Container view: strip container breadcrumb, use original ContainerField.
            // Container breadcrumbs share the parent's Address, which causes CleanBreadcrumbs
            // to falsely detect a cycle and remove them. Using parent breadcrumbs + ContainerField
            // lets EmitFields dispatch to EmitMapProperty/EmitArrayProperty/EmitSetProperty correctly.
            var lastBc = Breadcrumbs[^1];
            var isContainerView = lastBc.IsContainerView && lastBc.ContainerField != null;
            var breadcrumbsForXml = isContainerView
                ? (IReadOnlyList<BreadcrumbItem>)Breadcrumbs.Take(Breadcrumbs.Count - 1).ToList()
                : Breadcrumbs;
            var fieldsForXml = isContainerView
                ? new List<LiveFieldValue> { lastBc.ContainerField! }
                : new List<LiveFieldValue>(Fields);

            _log.Info($"CEXML export: containerView={isContainerView} bcCount={breadcrumbsForXml.Count} | BC={FormatBreadcrumbTrace()}");

            // Pre-check CleanBreadcrumbs to log any cycle removals
            var cleaned = CeXmlExportService.CleanBreadcrumbs(breadcrumbsForXml);
            if (cleaned.Count != breadcrumbsForXml.Count)
            {
                _log.Info($"CEXML CleanBC: {breadcrumbsForXml.Count}→{cleaned.Count} removed={breadcrumbsForXml.Count - cleaned.Count}");
                for (int i = 0; i < cleaned.Count; i++)
                {
                    var bc = cleaned[i];
                    var flags = bc.IsContainerView ? "C" : bc.IsPointerDeref ? "P" : "S";
                    _log.Info($"  [{i}] {bc.FieldName ?? bc.Label} ({flags}) off=0x{bc.FieldOffset:X} addr={bc.Address}");
                }
            }

            // Pre-resolve StructProperty inner fields via DLL
            StatusText = "Resolving struct fields...";
            var resolvedStructs = await CeXmlExportService.ResolveStructFieldsAsync(
                _dump, fieldsForXml, arrayLimit: ArrayLimit);

            // Compute root address in user-selected format
            var rootBc = breadcrumbsForXml[0];
            var rootAddress = AddressHelper.FormatAddress(
                rootBc.Address, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);

            StatusText = "Generating CE XML...";
            var xml = CeXmlExportService.GenerateHierarchicalXml(
                rootAddress, rootBc.Label, breadcrumbsForXml, fieldsForXml, resolvedStructs,
                collapsePointerNodes: CollapsePointerNodes,
                maxDropDownEntries: DropDownLimit);

            await _platform.CopyToClipboardAsync(xml);
            var limitWarn = BuildContainerLimitWarning(fieldsForXml, ArrayLimit);
            StatusText = limitWarn ?? "";
            _log.Info($"CE XML copied to clipboard for {CurrentClassName} ({resolvedStructs.Count} structs resolved)");
        }
        catch (Exception ex)
        {
            StatusText = "";
            SetError(ex);
            _log.Error("Failed to export CE XML", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExportCsxAsync()
    {
        if (string.IsNullOrEmpty(CurrentAddress) || !HasData) return;

        try
        {
            ClearStatus();

            // Build struct name: "ClassName_ObjectName" or "ClassName"
            var structName = !string.IsNullOrEmpty(CurrentObjectName)
                ? $"{CurrentClassName}_{CurrentObjectName}".Replace(" ", "_")
                : CurrentClassName.Replace(" ", "_");
            // Sanitize for file name and XML attribute
            structName = structName.Replace("<", "").Replace(">", "").Replace("\"", "");
            // Sanitize for file system: remove invalid chars
            var safeFileName = string.Join("_",
                structName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

            // Show save-file dialog; user picks folder + file name
            var filePath = await _platform.ShowSaveFileDialogAsync(
                safeFileName, "CE Structure Dissect (*.CSX)", ".CSX");
            if (string.IsNullOrEmpty(filePath)) return; // user cancelled

            IsLoading = true;
            StatusText = CsxDrilldownDepth > 0 ? "Resolving struct + pointer fields..." : "Resolving struct fields...";
            var csx = await CsxExportService.GenerateCsxAsync(
                _dump, structName, Fields, arrayLimit: ArrayLimit, drilldownDepth: CsxDrilldownDepth);

            // Write to file (overwrite if exists — user already confirmed via dialog)
            await File.WriteAllTextAsync(filePath, csx);

            StatusText = "";
            _log.Info($"CSX exported to {filePath} for {CurrentClassName}");
        }
        catch (UnauthorizedAccessException)
        {
            StatusText = "";
            SetError("Cannot write to the selected location — access denied.");
            _log.Error("CSX export failed: access denied");
        }
        catch (Exception ex)
        {
            StatusText = "";
            SetError(ex);
            _log.Error("Failed to export CSX", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExportCeFieldXmlAsync()
    {
        if (SelectedField == null || string.IsNullOrEmpty(CurrentAddress) || Breadcrumbs.Count == 0) return;

        try
        {
            ClearStatus();
            IsLoading = true;

            // Container view: strip container breadcrumb, use filtered ContainerField
            // containing only the selected element. Same rationale as ExportCeXmlAsync.
            var lastBc = Breadcrumbs[^1];
            var isContainerView = lastBc.IsContainerView && lastBc.ContainerField != null;

            IReadOnlyList<BreadcrumbItem> breadcrumbsForXml;
            List<LiveFieldValue> singleFieldList;

            if (isContainerView)
            {
                breadcrumbsForXml = Breadcrumbs.Take(Breadcrumbs.Count - 1).ToList();
                singleFieldList = new List<LiveFieldValue>
                    { FilterContainerToElement(lastBc.ContainerField!, SelectedField) };
            }
            else
            {
                breadcrumbsForXml = Breadcrumbs;
                singleFieldList = new List<LiveFieldValue> { SelectedField };
            }

            _log.Info($"CEFieldXML export: field={SelectedField.Name} containerView={isContainerView} bcCount={breadcrumbsForXml.Count} | BC={FormatBreadcrumbTrace()}");

            // Pre-check CleanBreadcrumbs to log any cycle removals
            var cleaned = CeXmlExportService.CleanBreadcrumbs(breadcrumbsForXml);
            if (cleaned.Count != breadcrumbsForXml.Count)
            {
                _log.Info($"CEFieldXML CleanBC: {breadcrumbsForXml.Count}→{cleaned.Count} removed={breadcrumbsForXml.Count - cleaned.Count}");
                for (int i = 0; i < cleaned.Count; i++)
                {
                    var bc = cleaned[i];
                    var flags = bc.IsContainerView ? "C" : bc.IsPointerDeref ? "P" : "S";
                    _log.Info($"  [{i}] {bc.FieldName ?? bc.Label} ({flags}) off=0x{bc.FieldOffset:X} addr={bc.Address}");
                }
            }

            // Pre-resolve StructProperty inner fields for the selected field
            StatusText = "Resolving struct fields...";
            var resolvedStructs = await CeXmlExportService.ResolveStructFieldsAsync(
                _dump, singleFieldList, arrayLimit: ArrayLimit);

            // Compute root address in user-selected format
            var rootBc = breadcrumbsForXml[0];
            var rootAddress = AddressHelper.FormatAddress(
                rootBc.Address, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);

            StatusText = "Generating CE Field XML...";
            var xml = CeXmlExportService.GenerateHierarchicalXml(
                rootAddress, rootBc.Label, breadcrumbsForXml, singleFieldList, resolvedStructs,
                collapsePointerNodes: CollapsePointerNodes,
                maxDropDownEntries: DropDownLimit);

            await _platform.CopyToClipboardAsync(xml);
            var limitWarn = BuildContainerLimitWarning(singleFieldList, ArrayLimit);
            StatusText = limitWarn ?? "";
            _log.Info($"CE Field XML copied for {SelectedField.Name} ({SelectedField.TypeName})");
        }
        catch (Exception ex)
        {
            StatusText = "";
            SetError(ex);
            _log.Error("Failed to export CE Field XML", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Compute CE-compatible "Module.exe"+RVA string from an absolute address.
    /// </summary>
    private string ComputeModuleRva(string hexAddr)
    {
        var addr = Convert.ToUInt64(hexAddr.Replace("0x", "").Replace("0X", ""), 16);
        var moduleBase = Convert.ToUInt64(_engineState!.ModuleBase.Replace("0x", "").Replace("0X", ""), 16);
        var rva = addr - moduleBase;
        return $"\"{_engineState.ModuleName}\"+{rva:X}";
    }

    [RelayCommand]
    private async Task GenerateCeAAScriptAsync()
    {
        if (string.IsNullOrEmpty(CurrentAddress)) return;

        try
        {
            ClearStatus();
            var symbolName = !string.IsNullOrEmpty(CurrentClassName)
                ? CurrentClassName.Replace(" ", "_").Replace("-", "_")
                : "UE5_Symbol";

            var formattedAddr = AddressHelper.FormatAddress(
                CurrentAddress, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);

            var xml = CeXmlExportService.GenerateRegisterSymbolXml(symbolName, formattedAddr);

            await _platform.CopyToClipboardAsync(xml);
            _log.Info($"CE AA script copied to clipboard for {CurrentClassName}");
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error("Failed to generate CE AA script", ex);
        }
    }

    [RelayCommand]
    private async Task ExportSdkHeaderAsync()
    {
        if (string.IsNullOrEmpty(CurrentAddress) || !HasData) return;

        try
        {
            ClearStatus();

            var structName = !string.IsNullOrEmpty(CurrentObjectName)
                ? $"{CurrentClassName}_{CurrentObjectName}".Replace(" ", "_")
                : CurrentClassName.Replace(" ", "_");
            structName = structName.Replace("<", "").Replace(">", "").Replace("\"", "");
            var safeFileName = string.Join("_",
                structName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

            var filePath = await _platform.ShowSaveFileDialogAsync(
                safeFileName, "C++ Header (*.h)", ".h");
            if (string.IsNullOrEmpty(filePath)) return;

            IsLoading = true;
            StatusText = "Generating SDK header...";

            // Get the superclass name from the first breadcrumb's class info if available
            var superName = "";
            if (Breadcrumbs.Count > 0)
            {
                var bc = Breadcrumbs[^1];
                if (!string.IsNullOrEmpty(bc.ClassAddr))
                {
                    try
                    {
                        var classInfo = await _dump.WalkClassAsync(bc.ClassAddr);
                        superName = classInfo.SuperName;
                    }
                    catch
                    {
                        // Non-critical — just emit without super
                    }
                }
            }

            // Estimate properties size from the last field end or use a safe heuristic
            var propsSize = 0;
            if (Fields.Count > 0)
            {
                var lastField = Fields.OrderByDescending(f => f.Offset + f.Size).First();
                propsSize = lastField.Offset + lastField.Size;
            }

            var header = SdkExportService.GenerateClassHeader(
                CurrentClassName, superName, propsSize, Fields.ToList());

            await File.WriteAllTextAsync(filePath, header);

            StatusText = "";
            _log.Info($"SDK header exported to {filePath} for {CurrentClassName}");
        }
        catch (UnauthorizedAccessException)
        {
            StatusText = "";
            SetError("Cannot write to the selected location — access denied.");
            _log.Error("SDK header export failed: access denied");
        }
        catch (Exception ex)
        {
            StatusText = "";
            SetError(ex);
            _log.Error("Failed to export SDK header", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(CurrentAddress)) return;

        // Snapshot address before async call — if user navigates while we're awaiting,
        // CurrentAddress will differ and we discard the stale result.
        var addressAtStart = CurrentAddress;
        var breadcrumbCountAtStart = Breadcrumbs.Count;

        try
        {
            ClearStatus();
            IsLoading = true;

            // If refreshing a container view, re-walk the parent instance and re-extract container data
            if (Breadcrumbs.Count > 0 && Breadcrumbs[^1].IsContainerView && Breadcrumbs[^1].ContainerField != null)
            {
                var containerBc = Breadcrumbs[^1];

                // DataTable container: re-fetch rows directly
                if (containerBc.IsDataTableView)
                {
                    var dtResult = await _dump.WalkDataTableRowsAsync(containerBc.Address);
                    if (CurrentAddress != addressAtStart || Breadcrumbs.Count != breadcrumbCountAtStart) return;
                    containerBc.DataTableData = dtResult;
                    PopulateDataTableRowFields(dtResult);
                    return;
                }

                var containerField = containerBc.ContainerField!;

                // Re-walk the parent instance to get fresh container data
                string? parentClassAddr = null;
                if (Breadcrumbs.Count >= 2)
                {
                    var parentBc = Breadcrumbs[^2];
                    if (!string.IsNullOrEmpty(parentBc.ClassAddr))
                        parentClassAddr = parentBc.ClassAddr;
                }

                var parentResult = await _dump.WalkInstanceAsync(containerBc.Address, parentClassAddr, arrayLimit: ArrayLimit, previewLimit: PreviewLimit);
                if (CurrentAddress != addressAtStart || Breadcrumbs.Count != breadcrumbCountAtStart) return;

                // Find the container field by name and offset in the refreshed result
                var updatedField = parentResult.Fields
                    .FirstOrDefault(f => f.Name == containerField.Name && f.Offset == containerField.Offset);

                if (updatedField != null)
                    RepopulateContainerView(updatedField);
                return;
            }

            // If refreshing GWorld view (first breadcrumb only), re-fetch the world.
            // Must check Breadcrumbs.Count == 1 because a sub-World (e.g. S01L04) can share
            // the same address as GWorld — without this guard, auto-refresh at deeper levels
            // would incorrectly show the GWorld actor list instead of instance fields.
            if (_cachedWorld != null && CurrentAddress == _cachedWorld.WorldAddr
                && Breadcrumbs.Count == 1)
            {
                var world = await _dump.WalkWorldAsync(500, arrayLimit: ArrayLimit);
                if (CurrentAddress != addressAtStart || Breadcrumbs.Count != breadcrumbCountAtStart) return;
                _cachedWorld = world;
                PopulateFromWorld(world);
                return;
            }

            // Pass ClassAddr from current breadcrumb (needed for StructProperty context;
            // without it the DLL interprets struct memory as UObject → garbage → empty grid)
            string? classAddr = null;
            if (Breadcrumbs.Count > 0)
            {
                var current = Breadcrumbs[^1];
                if (!string.IsNullOrEmpty(current.ClassAddr))
                    classAddr = current.ClassAddr;
            }

            var result = await _dump.WalkInstanceAsync(CurrentAddress, classAddr, arrayLimit: ArrayLimit, previewLimit: PreviewLimit);
            if (CurrentAddress != addressAtStart || Breadcrumbs.Count != breadcrumbCountAtStart) return;
            UpdateDisplay(result);
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

    [RelayCommand]
    private async Task CopyFieldAddressAsync(LiveFieldValue? field)
    {
        if (field == null || string.IsNullOrEmpty(CurrentAddress)) return;

        try
        {
            var instanceAddr = Convert.ToUInt64(CurrentAddress.Replace("0x", "").Replace("0X", ""), 16);
            var absAddr = instanceAddr + (ulong)field.Offset;
            var hexAddr = $"0x{absAddr:X}";

            var formatted = AddressHelper.FormatAddress(
                hexAddr, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);
            await _platform.CopyToClipboardAsync(formatted);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to copy address for {field.Name}", ex);
        }
    }

    [RelayCommand]
    private async Task CopyFieldNameAsync(LiveFieldValue? field)
    {
        if (field == null || string.IsNullOrEmpty(field.Name)) return;

        try
        {
            await _platform.CopyToClipboardAsync(field.Name);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to copy name for {field.Name}", ex);
        }
    }

    [RelayCommand]
    private async Task CopyPtrAddressAsync(LiveFieldValue? field)
    {
        if (field == null || string.IsNullOrEmpty(field.PtrAddress)) return;

        try
        {
            var formatted = AddressHelper.FormatAddress(
                field.PtrAddress, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);
            await _platform.CopyToClipboardAsync(formatted);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to copy ptr address for {field.Name}", ex);
        }
    }

    // --- AOBMaker CE Plugin: hex view navigation ---

    /// <summary>Check AOBMaker availability (called after data load).</summary>
    public async Task CheckAobMakerAsync()
    {
        if (_aobMaker == null) return;
        try
        {
            IsAobMakerAvailable = await _aobMaker.CheckAvailabilityAsync();
        }
        catch { IsAobMakerAvailable = false; }
    }

    /// <summary>Strip leading "0x" prefix for AOBMaker hex navigation.</summary>
    private static string StripHexPrefix(string addr)
        => addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? addr[2..] : addr;

    [RelayCommand]
    private async Task HexFieldAddressAsync(LiveFieldValue? field)
    {
        if (_aobMaker == null || field == null || string.IsNullOrEmpty(field.FieldAddress)) return;
        try
        {
            await _aobMaker.NavigateHexViewAsync(StripHexPrefix(field.FieldAddress));
        }
        catch (Exception ex)
        {
            _log.Error($"AOBMaker HEX field failed for {field.Name}", ex);
        }
    }

    [RelayCommand]
    private async Task HexPtrAddressAsync(LiveFieldValue? field)
    {
        if (_aobMaker == null || field == null || string.IsNullOrEmpty(field.PtrAddress)) return;
        try
        {
            await _aobMaker.NavigateHexViewAsync(StripHexPrefix(field.PtrAddress));
        }
        catch (Exception ex)
        {
            _log.Error($"AOBMaker HEX ptr failed for {field.Name}", ex);
        }
    }

    [RelayCommand]
    private async Task HexObjectAddressAsync()
    {
        if (_aobMaker == null || string.IsNullOrEmpty(CurrentAddress)) return;
        try
        {
            await _aobMaker.NavigateHexViewAsync(StripHexPrefix(CurrentAddress));
        }
        catch (Exception ex)
        {
            _log.Error("AOBMaker HEX object address failed", ex);
        }
    }

    [RelayCommand]
    private async Task CopyCurrentAddressAsync()
    {
        if (string.IsNullOrEmpty(CurrentAddress)) return;

        try
        {
            var formatted = AddressHelper.FormatAddress(
                CurrentAddress, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);
            await _platform.CopyToClipboardAsync(formatted);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to copy current address", ex);
        }
    }

    [RelayCommand]
    private async Task CopyCurrentNameAsync()
    {
        if (string.IsNullOrEmpty(CurrentObjectName)) return;

        try
        {
            await _platform.CopyToClipboardAsync(CurrentObjectName);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to copy current name", ex);
        }
    }

    [RelayCommand]
    private async Task CopyOuterAddressAsync()
    {
        if (string.IsNullOrEmpty(CurrentOuterAddr) || CurrentOuterAddr == "0x0") return;

        try
        {
            var formatted = AddressHelper.FormatAddress(
                CurrentOuterAddr, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);
            await _platform.CopyToClipboardAsync(formatted);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to copy outer address", ex);
        }
    }

    // ========================================
    // Auto-refresh
    // ========================================

    /// <summary>
    /// Reacts to IsAutoRefreshing changes (driven by ToggleButton.IsChecked binding).
    /// Starts or stops the auto-refresh timer accordingly.
    /// </summary>
    partial void OnIsAutoRefreshingChanged(bool value)
    {
        if (value)
            StartAutoRefreshTimer();
        else
            StopAutoRefreshTimer();
    }

    partial void OnAutoRefreshIntervalSecChanged(int value)
    {
        // Enforce minimum interval (dynamic minimum from benchmark)
        if (value < AutoRefreshMinSec)
        {
            AutoRefreshIntervalSec = AutoRefreshMinSec;
            return;
        }

        // Update timer interval if already running
        if (_autoRefreshTimer != null && _autoRefreshTimer.IsEnabled)
        {
            _autoRefreshTimer.Interval = TimeSpan.FromSeconds(value);
            _countdownRemaining = value; // Reset countdown to new interval
        }
    }

    private void StartAutoRefreshTimer()
    {
        // Stop existing timer, but don't reset IsAutoRefreshing
        if (_autoRefreshTimer != null)
        {
            _autoRefreshTimer.Stop();
            _autoRefreshTimer.Tick -= OnAutoRefreshTick;
            _autoRefreshTimer = null;
        }

        // Reset benchmark state — first tick will measure refresh duration
        _isAutoRefreshBenchmarked = false;

        var interval = Math.Max(AutoRefreshIntervalSec, AutoRefreshMinSec);
        _autoRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(interval)
        };
        _autoRefreshTimer.Tick += OnAutoRefreshTick;
        _autoRefreshTimer.Start();

        // Start 1-second countdown timer for status display
        StopCountdownTimer();
        _countdownRemaining = interval;
        AutoRefreshStatusText = $"sec · {_countdownRemaining}s";
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += OnCountdownTick;
        _countdownTimer.Start();
    }

    private void StopCountdownTimer()
    {
        if (_countdownTimer != null)
        {
            _countdownTimer.Stop();
            _countdownTimer.Tick -= OnCountdownTick;
            _countdownTimer = null;
        }
    }

    private void OnCountdownTick(object? sender, EventArgs e)
    {
        if (_isAutoRefreshing_InProgress)
        {
            AutoRefreshStatusText = "sec · refreshing...";
            return;
        }

        _countdownRemaining--;
        if (_countdownRemaining < 0) _countdownRemaining = 0;
        AutoRefreshStatusText = $"sec · {_countdownRemaining}s";
    }

    public void StopAutoRefreshTimer()
    {
        if (_autoRefreshTimer != null)
        {
            _autoRefreshTimer.Stop();
            _autoRefreshTimer.Tick -= OnAutoRefreshTick;
            _autoRefreshTimer = null;
        }

        StopCountdownTimer();
        IsAutoRefreshing = false;

        // Reset dynamic minimum and benchmark state on stop (tab switch, navigation, etc.)
        AutoRefreshMinSec = Constants.MinAutoRefreshIntervalSec;
        _isAutoRefreshBenchmarked = false;
        AutoRefreshStatusText = "sec";
    }

    private async void OnAutoRefreshTick(object? sender, EventArgs e)
    {
        // Anti-flooding: skip if a refresh is already in progress or no data to refresh.
        // Uses a dedicated flag (_isAutoRefreshing_InProgress) to prevent re-entrant calls
        // from the DispatcherTimer firing while a previous refresh is still awaiting.
        if (_isAutoRefreshing_InProgress || !HasData || string.IsNullOrEmpty(CurrentAddress)) return;

        _isAutoRefreshing_InProgress = true;
        try
        {
            var sw = Stopwatch.StartNew();
            await RefreshAsync();
            sw.Stop();

            var durationSec = (int)Math.Ceiling(sw.Elapsed.TotalSeconds);

            // Benchmark: on first successful auto-refresh, check if the interval is too short.
            // If refresh took longer than the user's interval, auto-clamp the minimum.
            if (!_isAutoRefreshBenchmarked)
            {
                _isAutoRefreshBenchmarked = true;

                if (durationSec >= AutoRefreshIntervalSec)
                {
                    var newMin = durationSec + Constants.AutoRefreshBenchmarkBufferSec;
                    AutoRefreshMinSec = newMin;
                    AutoRefreshIntervalSec = newMin;

                    // Restart timer with the new interval
                    if (_autoRefreshTimer != null)
                    {
                        _autoRefreshTimer.Interval = TimeSpan.FromSeconds(newMin);
                    }

                    _log.Info($"Auto-refresh: benchmark {durationSec}s, clamped interval to {newMin}s");
                }
            }

            // Reset countdown after refresh completes
            _countdownRemaining = Math.Max(AutoRefreshIntervalSec, AutoRefreshMinSec);
        }
        catch
        {
            // Silently ignore auto-refresh errors to avoid flooding the UI with error dialogs
        }
        finally
        {
            _isAutoRefreshing_InProgress = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplySearch(value);
    }

    private void ApplySearch(string query)
    {
        // Require at least 2 characters — single char matches too broadly
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            foreach (var f in Fields) f.IsSearchMatch = false;
            SearchMatchCount = 0;
            HasSearchResults = false;
            _lastScrolledSearchText = "";
        }
        else
        {
            int count = 0;
            foreach (var f in Fields)
            {
                bool match =
                    f.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    f.TypeName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    f.DisplayValue.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    f.PtrClassName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(f.StructTypeName) && f.StructTypeName.Contains(query, StringComparison.OrdinalIgnoreCase));

                f.IsSearchMatch = match;
                if (match) count++;
            }

            SearchMatchCount = count;
            HasSearchResults = count > 0;

            // Scroll to first match when search text changes and has results
            if (count > 0 && query != _lastScrolledSearchText)
            {
                _lastScrolledSearchText = query;
                ScrollToFirstSearchMatch?.Invoke();
            }
        }

        // Force DataGrid to re-evaluate row styles by resetting the collection
        var items = new ObservableCollection<LiveFieldValue>(Fields);
        Fields = items;
    }

    private async Task NavigateToAsync(string addr, string label, int fieldOffset, string fieldName, bool isPointer)
    {
        var result = await _dump.WalkInstanceAsync(addr, arrayLimit: ArrayLimit, previewLimit: PreviewLimit);

        var displayName = !string.IsNullOrEmpty(result.Name) ? result.Name : label;
        Breadcrumbs.Add(new BreadcrumbItem
        {
            Address = addr,
            Label = displayName,
            FieldOffset = fieldOffset,
            FieldName = fieldName,
            IsPointerDeref = isPointer,
        });

        _log.Info($"NAV→ {fieldName} addr={addr} off=0x{fieldOffset:X} ptr={isPointer} | BC={FormatBreadcrumbTrace()}");
        UpdateDisplay(result);
    }

    /// <summary>Format breadcrumb trail for debug logging.</summary>
    private string FormatBreadcrumbTrace()
    {
        if (Breadcrumbs.Count == 0) return "(empty)";
        var parts = new List<string>(Breadcrumbs.Count);
        foreach (var bc in Breadcrumbs)
        {
            var flags = bc.IsContainerView ? "C" : bc.IsPointerDeref ? "P" : "S";
            parts.Add($"{bc.FieldName ?? bc.Label}({flags},0x{bc.FieldOffset:X},{bc.Address?[^4..]})");
        }
        return string.Join(" > ", parts);
    }

    [RelayCommand]
    private async Task GenerateInvokeScriptAsync(FunctionInfoModel? func)
    {
        if (func == null || string.IsNullOrEmpty(CurrentClassName)) return;

        try
        {
            ClearStatus();
            var script = InvokeScriptGenerator.Generate(CurrentClassName, func.Name, func);
            var description = $"Invoke: {CurrentClassName}::{func.Name}";

            // Try AOBMaker CE Plugin first, fallback to clipboard
            if (_aobMaker != null)
            {
                var sent = await _aobMaker.CreateAAScriptAsync(description, script, autoActivate: false);
                if (sent)
                {
                    _log.Info($"Invoke script sent to CE: {description}");
                    StatusText = $"Invoke script created in CE: {func.Name}";
                    return;
                }
            }

            await _platform.CopyToClipboardAsync(script);
            StatusText = $"Invoke script copied to clipboard: {func.Name}";
            _log.Info($"Invoke script copied to clipboard: {description}");
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"Failed to generate invoke script for {func.Name}", ex);
        }
    }

    [RelayCommand]
    private async Task InvokeViaPipeAsync(FunctionInfoModel? func)
    {
        if (func == null || string.IsNullOrEmpty(CurrentAddress)) return;

        try
        {
            ClearStatus();

            if (Avalonia.Application.Current?.ApplicationLifetime is not
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                || desktop.MainWindow is not { } owner)
                return;

            var inputParams = func.Params.Where(p => !p.IsReturn).ToList();

            // Dialog owns the entire invoke lifecycle:
            // - Shows input fields (or "no params" message)
            // - FIRE button calls InvokeFunctionAsync internally
            // - Decoded results shown inline (return values, out params)
            // - Returns "ok" on Close, null on Cancel
            var dialog = new Views.InvokeParamDialog(
                CurrentClassName, func.Name, inputParams, func.Params, func.ParmsSize,
                CurrentAddress, _dump, _engineState?.UEVersion ?? 0);

            var dialogResult = await dialog.ShowDialog<string?>(owner);

            StatusText = dialogResult == "ok"
                ? $"Invoke dialog closed: {CurrentClassName}::{func.Name}"
                : $"Invoke cancelled: {func.Name}";

            _log.Info($"Pipe invoke dialog {(dialogResult == "ok" ? "completed" : "cancelled")}: " +
                      $"{CurrentClassName}::{func.Name} inst={CurrentAddress}");
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"Failed to invoke {func?.Name} via pipe", ex);
        }
    }

    private void UpdateDisplay(InstanceWalkResult result)
    {
        CurrentObjectName = result.Name;
        CurrentClassName = result.ClassName;
        CurrentAddress = result.Address;
        HasData = true;
        ShowCeXml = false;

        // Update parent (Outer) info
        CurrentOuterAddr = result.OuterAddr;
        CurrentOuterName = result.OuterName;
        CurrentOuterClassName = result.OuterClassName;
        HasParent = !string.IsNullOrEmpty(result.OuterAddr) && result.OuterAddr != "0x0";

        // Inline structs are not UObjects — they don't have OuterPrivate or FName at
        // the UObject::Name offset. The DLL reads garbage when walking a struct address
        // as if it were a UObject, producing corrupted name strings (亂碼).
        // Override CurrentObjectName with the breadcrumb label (set from field metadata
        // during navigation) and disable the Parent button / clear Outer info.
        if (Breadcrumbs.Count > 0 && !Breadcrumbs[^1].IsPointerDeref
            && !string.IsNullOrEmpty(Breadcrumbs[^1].ClassAddr))
        {
            CurrentObjectName = Breadcrumbs[^1].Label;
            HasParent = false;
            CurrentOuterAddr = "";
            CurrentOuterName = "";
            CurrentOuterClassName = "";
        }

        // Track whether this is a definition view (schema-only, no live values)
        _isDefinitionView = result.IsDefinition;

        // Compute absolute field addresses
        ulong baseAddr = 0;
        try
        {
            if (!string.IsNullOrEmpty(result.Address))
                baseAddr = Convert.ToUInt64(result.Address.Replace("0x", "").Replace("0X", ""), 16);
        }
        catch { /* ignore parse failures */ }

        // Update fields. When refreshing the same object (same field count and layout),
        // replace items in-place to preserve DataGrid scroll position.
        // When navigating to a different object, do a full clear+rebuild.
        var newFields = result.Fields;
        foreach (var f in newFields)
        {
            if (baseAddr != 0)
                f.FieldAddress = $"0x{baseAddr + (ulong)f.Offset:X}";
        }

        if (Fields.Count == newFields.Count && Fields.Count > 0
            && Fields[0].Name == newFields[0].Name)
        {
            // Same layout — replace in-place (preserves scroll position)
            for (int i = 0; i < newFields.Count; i++)
                Fields[i] = newFields[i];
        }
        else
        {
            // Different layout — full rebuild
            Fields.Clear();
            foreach (var f in newFields)
                Fields.Add(f);
        }

        // Store class address and load functions asynchronously
        _currentClassAddr = result.ClassAddr;
        _ = LoadFunctionsAsync(result.ClassAddr);

        // DataTable detection: if this is a DataTable, fetch rows and inject synthetic RowMap field
        _cachedDataTableRows = null;
        if (result.ClassName == "DataTable" && !string.IsNullOrEmpty(result.Address))
            _ = TryLoadDataTableRowsAsync(result.Address);
    }

    /// <summary>
    /// Detect DataTable and inject a synthetic RowMap field for container navigation.
    /// Called fire-and-forget from UpdateDisplay to avoid blocking the UI.
    /// </summary>
    private async Task TryLoadDataTableRowsAsync(string dataTableAddr)
    {
        try
        {
            var dtResult = await _dump.WalkDataTableRowsAsync(dataTableAddr);
            _cachedDataTableRows = dtResult;

            // Inject a synthetic "RowMap" field at the end of the field list
            var syntheticField = new LiveFieldValue
            {
                Name = "RowMap",
                TypeName = "DataTableRows",
                Offset = dtResult.RowMapOffset,
                Size = 0,
                TypedValue = $"{{DataTable: {dtResult.RowCount} rows, {dtResult.RowStructName}}}",
                DataTableRowCount = dtResult.RowCount,
                DataTableStructName = dtResult.RowStructName,
                DataTableFNameSize = dtResult.FNameSize,
                DataTableStride = dtResult.Stride,
                DataTableRowStructAddr = dtResult.RowStructAddr,
                DataTableRowData = dtResult.Rows,
            };

            // Add on UI thread
            await Dispatcher.UIThread.InvokeAsync(() => Fields.Add(syntheticField));
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to load DataTable rows for {dataTableAddr}", ex);
        }
    }

    private async Task LoadFunctionsAsync(string classAddr)
    {
        if (string.IsNullOrEmpty(classAddr) || classAddr == "0x0")
        {
            Functions.Clear();
            HasFunctions = false;
            return;
        }

        try
        {
            var funcs = await _dump.WalkFunctionsAsync(classAddr);
            Functions.Clear();
            foreach (var f in funcs)
                Functions.Add(f);
            HasFunctions = funcs.Count > 0;
        }
        catch
        {
            Functions.Clear();
            HasFunctions = false;
        }
    }
}

/// <summary>
/// A breadcrumb navigation item, recording navigation history for CE XML export.
/// </summary>
public sealed class BreadcrumbItem
{
    public string Address { get; init; } = "";
    public string Label { get; init; } = "";
    public string ClassAddr { get; init; } = "";

    /// <summary>Offset of the field that was clicked to reach this level (hex).</summary>
    public int FieldOffset { get; init; }

    /// <summary>Field name (e.g., "m_pAttributeSetHealth").</summary>
    public string FieldName { get; init; } = "";

    /// <summary>True if navigation was through a pointer dereference (ObjectProperty), false for inline struct.</summary>
    public bool IsPointerDeref { get; init; }

    /// <summary>Field name the user was looking at before drilling in. Used to restore scroll position on Back.</summary>
    public string? ScrollHintFieldName { get; set; }

    /// <summary>True if this breadcrumb represents a container element view (Array/Map/Set/DataTable).</summary>
    public bool IsContainerView { get; init; }

    /// <summary>The source container field (for refreshing container views).</summary>
    public LiveFieldValue? ContainerField { get; init; }

    /// <summary>True if this breadcrumb represents a DataTable row container view.</summary>
    public bool IsDataTableView { get; init; }

    /// <summary>Cached DataTable walk result (for refreshing DataTable row views).</summary>
    public DataTableWalkResult? DataTableData { get; set; }
}
