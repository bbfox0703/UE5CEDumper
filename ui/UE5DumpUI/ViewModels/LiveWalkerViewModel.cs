using System.Collections.ObjectModel;
using System.Diagnostics;
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

    /// <summary>Max CE DropDownList entries (2^N, default 512). Used during CE XML export.</summary>
    public int DropDownLimit { get; set; } = 512;

    // Search
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private int _searchMatchCount;
    [ObservableProperty] private bool _hasSearchResults;

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

    public LiveWalkerViewModel(IDumpService dump, ILoggingService log, IPlatformService platform)
    {
        _dump = dump;
        _log = log;
        _platform = platform;
    }

    public void SetEngineState(EngineState state)
    {
        _engineState = state;
    }

    [RelayCommand]
    private async Task StartFromWorldAsync()
    {
        try
        {
            ClearError();
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
            ClearError();
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
                var result = await _dump.WalkInstanceAsync(field.StructDataAddr, field.StructClassAddr, arrayLimit: ArrayLimit);
                var displayName = !string.IsNullOrEmpty(field.StructTypeName)
                    ? $"{field.Name} ({field.StructTypeName})"
                    : field.Name;
                Breadcrumbs.Add(new BreadcrumbItem
                {
                    Address = field.StructDataAddr,
                    Label = displayName,
                    ClassAddr = field.StructClassAddr,
                    FieldOffset = field.Offset,
                    FieldName = field.Name,
                    IsPointerDeref = false,
                });
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
    private async Task NavigateToBreadcrumbAsync(BreadcrumbItem? item)
    {
        if (item == null) return;

        try
        {
            ClearError();
            IsLoading = true;

            // Remove all breadcrumbs after this one
            var idx = Breadcrumbs.IndexOf(item);
            if (idx < 0) return;

            while (Breadcrumbs.Count > idx + 1)
                Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1);

            var scrollHint = item.ScrollHintFieldName;

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
            var result = await _dump.WalkInstanceAsync(item.Address, classAddr, arrayLimit: ArrayLimit);
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

        Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1);
        var prev = Breadcrumbs[^1];
        var scrollHint = prev.ScrollHintFieldName;

        try
        {
            ClearError();
            IsLoading = true;

            // If going back to GWorld, re-display actor list
            if (_cachedWorld != null && prev.Address == _cachedWorld.WorldAddr)
            {
                PopulateFromWorld(_cachedWorld);
                if (!string.IsNullOrEmpty(scrollHint))
                    ScrollToFieldRequested?.Invoke(scrollHint);
                return;
            }

            var classAddr = string.IsNullOrEmpty(prev.ClassAddr) ? null : prev.ClassAddr;
            var result = await _dump.WalkInstanceAsync(prev.Address, classAddr, arrayLimit: ArrayLimit);
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
            ClearError();
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

            var result = await _dump.WalkInstanceAsync(parentAddr, arrayLimit: ArrayLimit);
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
            ClearError();
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
            ClearError();
            IsLoading = true;

            // Pre-resolve StructProperty inner fields via DLL
            StatusText = "Resolving struct fields...";
            var resolvedStructs = await CeXmlExportService.ResolveStructFieldsAsync(
                _dump, new List<LiveFieldValue>(Fields), arrayLimit: ArrayLimit);

            // Compute root address in user-selected format
            var rootBc = Breadcrumbs[0];
            var rootAddress = AddressHelper.FormatAddress(
                rootBc.Address, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);

            StatusText = "Generating CE XML...";
            var xml = CeXmlExportService.GenerateHierarchicalXml(
                rootAddress, rootBc.Label, Breadcrumbs, Fields, resolvedStructs,
                collapsePointerNodes: CollapsePointerNodes,
                maxDropDownEntries: DropDownLimit);

            await _platform.CopyToClipboardAsync(xml);
            StatusText = "";
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
    private async Task ExportCeFieldXmlAsync()
    {
        if (SelectedField == null || string.IsNullOrEmpty(CurrentAddress) || Breadcrumbs.Count == 0) return;

        try
        {
            ClearError();
            IsLoading = true;

            var singleFieldList = new List<LiveFieldValue> { SelectedField };

            // Pre-resolve StructProperty inner fields for the selected field
            StatusText = "Resolving struct fields...";
            var resolvedStructs = await CeXmlExportService.ResolveStructFieldsAsync(
                _dump, singleFieldList, arrayLimit: ArrayLimit);

            // Compute root address in user-selected format
            var rootBc = Breadcrumbs[0];
            var rootAddress = AddressHelper.FormatAddress(
                rootBc.Address, _engineState?.ModuleName, _engineState?.ModuleBase, AddrFormat);

            StatusText = "Generating CE Field XML...";
            var xml = CeXmlExportService.GenerateHierarchicalXml(
                rootAddress, rootBc.Label, Breadcrumbs, singleFieldList, resolvedStructs,
                collapsePointerNodes: CollapsePointerNodes,
                maxDropDownEntries: DropDownLimit);

            await _platform.CopyToClipboardAsync(xml);
            StatusText = "";
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
            ClearError();
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
    private async Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(CurrentAddress)) return;

        // Snapshot address before async call — if user navigates while we're awaiting,
        // CurrentAddress will differ and we discard the stale result.
        var addressAtStart = CurrentAddress;
        var breadcrumbCountAtStart = Breadcrumbs.Count;

        try
        {
            ClearError();
            IsLoading = true;

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

            var result = await _dump.WalkInstanceAsync(CurrentAddress, classAddr, arrayLimit: ArrayLimit);
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
            await _platform.CopyToClipboardAsync(field.PtrAddress);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to copy ptr address for {field.Name}", ex);
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
        var result = await _dump.WalkInstanceAsync(addr, arrayLimit: ArrayLimit);

        var displayName = !string.IsNullOrEmpty(result.Name) ? result.Name : label;
        Breadcrumbs.Add(new BreadcrumbItem
        {
            Address = addr,
            Label = displayName,
            FieldOffset = fieldOffset,
            FieldName = fieldName,
            IsPointerDeref = isPointer,
        });

        UpdateDisplay(result);
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

        // Inline structs are not UObjects — they don't have OuterPrivate.
        // The DLL reads garbage when walking a struct address as if it were a UObject.
        // Disable the Parent button and clear Outer info when inside a struct view.
        if (Breadcrumbs.Count > 0 && !Breadcrumbs[^1].IsPointerDeref
            && !string.IsNullOrEmpty(Breadcrumbs[^1].ClassAddr))
        {
            HasParent = false;
            CurrentOuterAddr = "";
            CurrentOuterName = "";
            CurrentOuterClassName = "";
        }

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
}
