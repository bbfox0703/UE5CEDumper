using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Hex View panel.
/// </summary>
public partial class HexViewViewModel : ViewModelBase
{
    private readonly IDumpService _dump;
    private readonly IPipeClient _pipeClient;
    private readonly ILoggingService _log;

    private EngineState? _engineState;

    // Track the resolved address used for the active watch
    private string _watchedResolvedAddr = "";

    // Update counter for watch status
    private int _watchUpdateCount;

    [ObservableProperty] private string _address = "";
    [ObservableProperty] private int _size = Constants.DefaultHexViewSize;
    [ObservableProperty] private ObservableCollection<HexViewRow> _hexRows = new();
    [ObservableProperty] private bool _isWatching;
    [ObservableProperty] private int _watchInterval = Constants.DefaultWatchIntervalMs;
    [ObservableProperty] private string _watchStatusText = "";
    [ObservableProperty] private string _watchButtonText = "Watch";

    public HexViewViewModel(IDumpService dump, IPipeClient pipeClient, ILoggingService log)
    {
        _dump = dump;
        _pipeClient = pipeClient;
        _log = log;

        _pipeClient.EventReceived += OnEventReceived;
    }

    public void SetEngineState(EngineState state)
    {
        _engineState = state;
    }

    /// <summary>
    /// Normalize the user-provided address (supports CE formats like "module.exe"+offset).
    /// </summary>
    private string ResolveAddress(string raw)
    {
        return AddressHelper.NormalizeAddress(raw, _engineState?.ModuleBase);
    }

    [RelayCommand]
    private async Task ReadAsync()
    {
        if (string.IsNullOrWhiteSpace(Address)) return;

        try
        {
            ClearError();
            var resolved = ResolveAddress(Address);
            var data = await _dump.ReadMemAsync(resolved, Size);
            UpdateHexRows(data);
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log.Error($"Failed to read memory at {Address}", ex);
        }
    }

    [RelayCommand]
    private async Task ToggleWatchAsync()
    {
        try
        {
            ClearError();

            if (IsWatching)
            {
                await StopWatchAsync();
            }
            else
            {
                await StartWatchAsync();
            }
        }
        catch (Exception ex)
        {
            SetError(ex);
            IsWatching = false;
            UpdateWatchUI();
        }
    }

    [RelayCommand]
    private async Task StopWatchAsync()
    {
        if (!IsWatching) return;

        try
        {
            ClearError();
            await _dump.UnwatchAsync(_watchedResolvedAddr);
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to stop watch on {_watchedResolvedAddr}", ex);
        }
        finally
        {
            IsWatching = false;
            _watchedResolvedAddr = "";
            UpdateWatchUI();
        }
    }

    private async Task StartWatchAsync()
    {
        if (string.IsNullOrWhiteSpace(Address)) return;

        var resolved = ResolveAddress(Address);
        await _dump.WatchAsync(resolved, Size, WatchInterval);
        _watchedResolvedAddr = resolved;
        _watchUpdateCount = 0;
        IsWatching = true;
        UpdateWatchUI();
    }

    private void UpdateWatchUI()
    {
        if (IsWatching)
        {
            WatchButtonText = "Stop";
            WatchStatusText = _watchUpdateCount > 0
                ? $"Watching (every {WatchInterval}ms) — {_watchUpdateCount} updates"
                : $"Watching (every {WatchInterval}ms)";
        }
        else
        {
            WatchButtonText = "Watch";
            WatchStatusText = _watchUpdateCount > 0
                ? $"Stopped ({_watchUpdateCount} updates received)"
                : "";
        }
    }

    public void SetAddress(string addr)
    {
        Address = addr;
    }

    private void OnEventReceived(JsonObject evt)
    {
        var eventType = evt["event"]?.GetValue<string>();
        if (eventType != "watch") return;

        var addr = evt["addr"]?.GetValue<string>() ?? "";
        if (!string.Equals(addr, _watchedResolvedAddr, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(addr, Address, StringComparison.OrdinalIgnoreCase))
            return;

        var hexStr = evt["bytes"]?.GetValue<string>() ?? "";
        try
        {
            var data = Convert.FromHexString(hexStr);
            // Must dispatch to UI thread
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                UpdateHexRows(data);
                _watchUpdateCount++;
                UpdateWatchUI();
            });
        }
        catch { /* ignore malformed data */ }
    }

    private void UpdateHexRows(byte[] data)
    {
        HexRows.Clear();

        for (int i = 0; i < data.Length; i += 16)
        {
            int count = Math.Min(16, data.Length - i);
            var row = FormatRow(i, data.AsSpan(i, count));
            HexRows.Add(row);
        }
    }

    public static HexViewRow FormatRow(int offset, ReadOnlySpan<byte> bytes)
    {
        var hexParts = new char[48]; // 16 bytes * 3 chars each
        var asciiParts = new char[16];

        Array.Fill(hexParts, ' ');
        Array.Fill(asciiParts, ' ');

        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            hexParts[i * 3] = GetHexChar(b >> 4);
            hexParts[i * 3 + 1] = GetHexChar(b & 0xF);
            if (i < bytes.Length - 1) hexParts[i * 3 + 2] = ' ';

            asciiParts[i] = (b >= 0x20 && b < 0x7F) ? (char)b : '.';
        }

        return new HexViewRow
        {
            Offset = offset.ToString("X8"),
            HexPart = new string(hexParts).TrimEnd(),
            AsciiPart = new string(asciiParts, 0, bytes.Length),
        };
    }

    private static char GetHexChar(int nibble) =>
        (char)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);
}
