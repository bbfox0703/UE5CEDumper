# Avalonia UI App Specification

> Moved from CLAUDE.md. Contains UI tech stack, component skeletons, and layout definitions.

-----

## Tech Stack

- **.NET 10** + **Avalonia 11.3.12** or higher compatible version. Can use VS 2026 IDE (.sln file)
- **ReactiveUI + ReactiveUI.Fody** (ViewModel property auto-notification)
- **Theme**: `FluentTheme` Dark mode
- **Publish**: `PublishSingleFile` Native AoT, single exe
- **Testing**: Minimum versions: `xunit.v3 3.2.2`, `Application Insights 3.0`, `Microsoft.Testing.Platform 2.1.0`
- **Other**: Minimum versions: `SkiaSharp 3.119.2`, `MicroCom.Runtime 0.11.3`, `SeriLog 4.3.1`, `Tmds.DBus.Protocol 0.90.3`

See "Rules" section in [CLAUDE.md](../CLAUDE.md) for language, i18n, logging, platform abstraction, and other constraints.

-----

## AOT Compatibility

| Component | Status | Notes |
|-----------|--------|-------|
| CommunityToolkit.Mvvm | ?? | Source generators, no reflection |
| Serilog (Console/File) | ?? | Basic sinks compatible |
| Avalonia Compiled Bindings | ?? | `AvaloniaUseCompiledBindingsByDefault=true` |
| LibraryImport (P/Invoke) | ?? | Designed for AOT |
| ViewLocator | ?? | Changed to explicit type mapping, no reflection |

-----

## PipeClient.cs — Connection Management

```csharp
// Services/PipeClient.cs
public class PipeClient : IDisposable
{
    private const string PipeName = "UE5DumpBfx";
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource _cts = new();

    // Connection state (ReactiveUI)
    public IObservable<bool> IsConnected { get; }

    // Triggered when Event push received (watch, etc.)
    public event Action<JsonObject>? EventReceived;

    public async Task ConnectAsync()
    {
        _pipe = new NamedPipeClientStream(".", PipeName,
            PipeDirection.InOut, PipeOptions.Asynchronous);
        await _pipe.ConnectAsync(5000); // 5 second timeout
        _reader = new StreamReader(_pipe, Encoding.UTF8);
        _writer = new StreamWriter(_pipe, Encoding.UTF8) { AutoFlush = true };

        // Background read for push events
        _ = Task.Run(ReadLoopAsync);
    }

    // Send request, await response (matched by id)
    public async Task<JsonObject> SendAsync(JsonObject request,
        CancellationToken ct = default)
    {
        var json = request.ToJsonString();
        await _writer!.WriteLineAsync(json);
        // Response matched by ReadLoop via TaskCompletionSource
        ...
    }

    private async Task ReadLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            var line = await _reader!.ReadLineAsync();
            if (line is null) break;
            var obj = JsonNode.Parse(line) as JsonObject;
            if (obj?["event"] != null)
                EventReceived?.Invoke(obj);  // Push event
            else
                MatchResponse(obj!);          // Match request id
        }
    }
}
```

-----

## DumpService.cs — Business Logic Wrapper

```csharp
// Services/DumpService.cs
public class DumpService
{
    private readonly PipeClient _pipe;

    public async Task<EngineState> InitAsync()
    {
        var res = await _pipe.SendAsync(new JsonObject { ["cmd"] = "init" });
        return new EngineState {
            UEVersion   = res["ue_version"]!.GetValue<int>(),
            GObjectsAddr = res["gobjects"]!.GetValue<string>()!,
            GNamesAddr   = res["gnames"]!.GetValue<string>()!,
        };
    }

    public async Task<List<UObjectNode>> GetObjectListAsync(int offset, int limit)
    {
        var res = await _pipe.SendAsync(new JsonObject {
            ["cmd"] = "get_object_list",
            ["offset"] = offset,
            ["limit"] = limit
        });
        // Parse objects array -> List<UObjectNode>
        ...
    }

    public async Task<ClassInfo> WalkClassAsync(string addr)
    {
        var res = await _pipe.SendAsync(new JsonObject {
            ["cmd"] = "walk_class",
            ["addr"] = addr
        });
        // Parse class + fields -> ClassInfo
        ...
    }

    public async Task<byte[]> ReadMemAsync(string addr, int size)
    {
        var res = await _pipe.SendAsync(new JsonObject {
            ["cmd"] = "read_mem",
            ["addr"] = addr,
            ["size"] = size
        });
        return Convert.FromHexString(res["bytes"]!.GetValue<string>()!);
    }
}
```

-----

## MainWindow Layout (AXAML Skeleton)

```xml
<!-- Views/MainWindow.axaml -->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="UE5DumpUI.Views.MainWindow"
        Title="UE5 Dump UI" Background="#1E1E1E"
        Width="1400" Height="900">

  <DockPanel>
    <!-- Top toolbar -->
    <StackPanel DockPanel.Dock="Top" Orientation="Horizontal"
                Background="#2D2D2D" Height="40">
      <Button Content="Connect" Command="{Binding ConnectCommand}" />
      <TextBlock Text="{Binding StatusText}" Foreground="#4EC9B0"
                 VerticalAlignment="Center" Margin="12,0" />
    </StackPanel>

    <!-- Main: Left Tree + Right Panel -->
    <Grid>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="350" />
        <ColumnDefinition Width="4" />   <!-- Splitter -->
        <ColumnDefinition Width="*" />
      </Grid.ColumnDefinitions>

      <!-- Left: Object Tree -->
      <views:ObjectTreePanel Grid.Column="0" DataContext="{Binding ObjectTree}" />

      <GridSplitter Grid.Column="1" Background="#3C3C3C" />

      <!-- Right: Tab panels (8 tabs) -->
      <TabControl Grid.Column="2" Background="#1E1E1E">
        <TabItem Header="Class Structure">
          <views:ClassStructPanel DataContext="{Binding ClassStruct}" />
        </TabItem>
        <TabItem Header="Live Walker">
          <views:LiveWalkerPanel DataContext="{Binding LiveWalker}" />
        </TabItem>
        <TabItem Header="Instance Finder">
          <views:InstanceFinderPanel DataContext="{Binding InstanceFinder}" />
        </TabItem>
        <TabItem Header="Property Search">
          <views:PropertySearchPanel DataContext="{Binding PropertySearch}" />
        </TabItem>
        <TabItem Header="Game Classes">
          <views:GameClassFilterPanel DataContext="{Binding GameClassFilter}" />
        </TabItem>
        <TabItem Header="Global Pointers">
          <views:PointerPanel DataContext="{Binding Pointers}" />
        </TabItem>
        <TabItem Header="Proxy Deploy">
          <views:ProxyDeployPanel DataContext="{Binding ProxyDeploy}" />
        </TabItem>
      </TabControl>
    </Grid>
  </DockPanel>
</Window>
```

-----

## ObjectTreeViewModel.cs Skeleton

```csharp
// ViewModels/ObjectTreeViewModel.cs
public class ObjectTreeViewModel : ReactiveObject
{
    private readonly DumpService _dump;

    [Reactive] public ObservableCollection<UObjectNode> Nodes { get; set; } = new();
    [Reactive] public UObjectNode? SelectedNode { get; set; }
    [Reactive] public string SearchText { get; set; } = "";
    [Reactive] public bool IsLoading { get; set; }

    public ReactiveCommand<Unit, Unit> LoadCommand { get; }
    public ReactiveCommand<string, Unit> SearchCommand { get; }

    // Notify ClassStructViewModel when object is selected
    public IObservable<UObjectNode?> SelectionChanged =>
        this.WhenAnyValue(x => x.SelectedNode);
}
```

-----

## ProxyDeployPanel Key Points

- **Not pipe-dependent** — works independently of game connection
- Detects Steam-installed UE games via `libraryfolders.vdf` parsing
- One-click deploy/undeploy/update of proxy `version.dll`
- Identifies our proxy DLL via `FileVersionInfo.ProductName == "UE5CEDumper"`
- If another program's proxy DLL is detected, status shows `OtherProxy` and blocks actions
- DataGrid with: checkbox select, game name, binaries path, status (color-coded), version, error
- Bottom action bar: Deploy Selected, Undeploy Selected, Update All, Force Overwrite checkbox
