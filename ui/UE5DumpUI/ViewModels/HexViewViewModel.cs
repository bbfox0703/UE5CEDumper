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

    [ObservableProperty] private string _address = "";
    [ObservableProperty] private int _size = Constants.DefaultHexViewSize;
    [ObservableProperty] private ObservableCollection<HexViewRow> _hexRows = new();
    [ObservableProperty] private bool _isWatching;
    [ObservableProperty] private int _watchInterval = Constants.DefaultWatchIntervalMs;

    public HexViewViewModel(IDumpService dump, IPipeClient pipeClient, ILoggingService log)
    {
        _dump = dump;
        _pipeClient = pipeClient;
        _log = log;

        _pipeClient.EventReceived += OnEventReceived;
    }

    [RelayCommand]
    private async Task ReadAsync()
    {
        if (string.IsNullOrWhiteSpace(Address)) return;

        try
        {
            ClearError();
            var data = await _dump.ReadMemAsync(Address, Size);
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
                await _dump.UnwatchAsync(Address);
                IsWatching = false;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(Address)) return;
                await _dump.WatchAsync(Address, Size, WatchInterval);
                IsWatching = true;
            }
        }
        catch (Exception ex)
        {
            SetError(ex);
            IsWatching = false;
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
        if (!string.Equals(addr, Address, StringComparison.OrdinalIgnoreCase)) return;

        var hexStr = evt["bytes"]?.GetValue<string>() ?? "";
        try
        {
            var data = Convert.FromHexString(hexStr);
            // Must dispatch to UI thread
            Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateHexRows(data));
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
