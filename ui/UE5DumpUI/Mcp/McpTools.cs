using System.Text.Json.Nodes;
using UE5DumpUI.Helpers;

namespace UE5DumpUI.Mcp;

/// <summary>
/// [PIPE-PER-PROCESS-2026-11-06] Stage D1. The tools the MCP server offers: the DLL's read commands, one tool each.
///
/// <para><b>Only reads, and nothing another client shares.</b> D1 is read-only (maintainer, 2026-11-11). It also
/// leaves out every command whose state another client sees, so an AI working beside the UI never disturbs what the
/// UI is doing (maintainer, 2026-11-11: the two must not interfere): <c>begin_snapshot</c> clears the name cache a UI
/// snapshot is using, the LiveFuncs recording is one per game, and <c>reset_diagnostics</c> and the version / timeout
/// / packed overrides are process-wide. <c>scan_start</c> starts a scan only on a game nobody has scanned. Value-scan
/// sessions belong to this server's connection (Stage C), so they are its own.</para>
///
/// <para><b>Small by default.</b> Limits default to a page an AI can read in one reply, and the schema states each
/// bound; <see cref="McpResultFit"/> cuts whatever still grows past the reply cap. A <c>name_filter</c> on the list
/// tools is applied here, space = AND as in every UI filter box (<see cref="ObjectTreeFilter"/>), because the DLL's
/// own limit would otherwise cut the list before the name is ever looked at.</para>
/// </summary>
internal static class McpTools
{
    // The DLL answers most commands in milliseconds; these are for its heavy walks, which stop at their own deadlines
    // (find_refs 30 s, path search 20 s) and must not be cut by this server first.
    private const int LightSeconds = 30;
    private const int HeavySeconds = 90;
    private const int ListSeconds = 120;

    private const string AddrHint = "Hex address, as the other tools return them.";

    private static readonly (string, JsonObject, bool) PidProp =
        ("pid", McpSchema.Int("Game process id. Needed only when several games run the dumper (read_games lists "
                              + "them); otherwise the connected or only game is used.", 1, int.MaxValue), false);

    private static readonly (string, JsonObject, bool) NameFilterProp =
        ("name_filter", McpSchema.Str("Keep only entries whose name contains every space-separated word "
                                      + "(case-insensitive)."), false);

    public static IReadOnlyList<McpTool> Create(IMcpGame game)
    {
        var tools = new List<McpTool>
        {
            Games(game),
            Status(game),
            ScanStart(game),

            Pipe(game, "read_search_objects", "Search objects",
                "Keyword search over object and class names (space = AND, case-insensitive). Returns addr, name, "
                + "class and outer of each match. instances_only hides classes, functions, structs, enums and "
                + "packages.",
                [("query", McpSchema.Str("Words that must all appear in the object or class name."), true),
                 ("limit", McpSchema.Int("Most matches returned.", 1, 1000, 50), false),
                 ("instances_only", McpSchema.Bool("Return live instances only.", false), false)],
                "search_objects", (a, r) =>
                {
                    r["query"] = a.ReqStr("query");
                    r["limit"] = a.Int("limit", 50, 1, 1000);
                    if (a.Bool("instances_only", false)) r["instances_only"] = true;
                }, HeavySeconds),

            Pipe(game, "read_find_object", "Find object by path",
                "Resolves a full object path (\"/Game/Maps/Map.Map:PersistentLevel.BP_Door_C_3\") or a bare object "
                + "name to its address.",
                [("path", McpSchema.Str("Full path or bare name."), true)],
                "find_object", (a, r) => r["path"] = a.ReqStr("path"), HeavySeconds),

            Pipe(game, "read_find_instances", "Find instances of a class",
                "Objects whose class name matches class_name (substring unless exact_match), or whose own name "
                + "matches name_filter. Includes class templates (Default__...) and archetypes; use "
                + "read_find_live_instance for one live object. Returns a class histogram too.",
                [("class_name", McpSchema.Str("Class name to match, e.g. \"BP_Enemy_C\"."), false),
                 ("name_filter", McpSchema.Str("Substring of the object's own name."), false),
                 ("exact_match", McpSchema.Bool("Match class_name exactly.", false), false),
                 ("limit", McpSchema.Int("Most instances returned.", 1, 2000, 50), false),
                 ("newest_first", McpSchema.Bool("Most recently created objects first (just-spawned actors).",
                     false), false),
                 ("exclude_classes", McpSchema.StrArray("Class names to leave out before the limit applies."),
                     false)],
                "find_instances", (a, r) =>
                {
                    string? cls = a.Str("class_name"), name = a.Str("name_filter");
                    if (string.IsNullOrWhiteSpace(cls) && string.IsNullOrWhiteSpace(name))
                        throw new McpToolError("Pass class_name, name_filter, or both.");
                    if (!string.IsNullOrWhiteSpace(cls)) r["class_name"] = cls;
                    if (!string.IsNullOrWhiteSpace(name)) r["name_filter"] = name;
                    if (a.Bool("exact_match", false)) r["exact_match"] = true;
                    r["limit"] = a.Int("limit", 50, 1, 2000);
                    if (a.Bool("newest_first", false)) r["newest_first"] = true;
                    if (a.StrList("exclude_classes") is { Count: > 0 } ex) r["exclude_classes"] = ToArray(ex);
                }, HeavySeconds),

            Pipe(game, "read_find_live_instance", "Find one live instance",
                "One live object of a class or of a subclass, never a template: the object to inspect. pick is "
                + "\"none\" when no live instance exists.",
                [("class_name", McpSchema.Str("Class name, e.g. \"PlayerController\"."), true)],
                "find_live_instance", (a, r) => r["class_name"] = a.ReqStr("class_name"), HeavySeconds),

            Pipe(game, "read_object", "Describe an object",
                "Name, full path, class and outer of the object at an address.",
                [("addr", McpSchema.Addr("The object's address."), true)],
                "get_object", (a, r) => r["addr"] = a.Addr("addr")),

            Pipe(game, "read_find_by_address", "Which object holds an address",
                "Finds the UObject that contains any address (a field's address, a pointer read from memory) and "
                + "the offset into it. scan_containers also looks inside heap arrays, maps and sets (slower).",
                [("addr", McpSchema.Addr("Any address."), true),
                 ("scan_containers", McpSchema.Bool("Also search inside containers.", false), false)],
                "find_by_address", (a, r) =>
                {
                    r["addr"] = a.Addr("addr");
                    if (a.Bool("scan_containers", false)) r["scan_containers"] = true;
                }, HeavySeconds),

            Filtered(game, "read_list_classes", "List classes",
                "UClasses with their path, super class, property count and a heuristic score (game classes "
                + "first). game_only leaves out engine classes.",
                [("game_only", McpSchema.Bool("Game classes only.", true), false),
                 ("limit", McpSchema.Int("Most classes returned.", 1, 2000, 100), false)],
                "list_classes", "classes", ["class_name", "class_path", "super_name"],
                (a, r) => { if (!a.Bool("game_only", true)) r["game_only"] = false; },
                dllLimitKey: "limit", unfilteredCap: Constants.MaxSearchCap),

            Pipe(game, "read_class", "Class layout",
                "The fields of a UClass or UScriptStruct: name, type, offset, size, and the class or struct a "
                + "field points to. Inherited fields come first. addr is the class's address (class_addr in other "
                + "replies).",
                [("addr", McpSchema.Addr("The UClass or UScriptStruct address."), true), NameFilterProp],
                "walk_class", (a, r) => r["addr"] = a.Addr("addr"),
                shape: (a, reply) => FilterNested(reply, a, "class", "fields", "name")),

            Pipe(game, "read_functions", "Class functions",
                "The UFunctions a class declares itself (not inherited), with flags and parameters (name, type, "
                + "offset, out / return). include_interfaces adds those it gets through an interface.",
                [("addr", McpSchema.Addr("The UClass address."), true),
                 ("include_interfaces", McpSchema.Bool("Add interface functions.", false), false), NameFilterProp],
                "walk_functions", (a, r) =>
                {
                    r["addr"] = a.Addr("addr");
                    if (a.Bool("include_interfaces", false)) r["include_interfaces"] = true;
                },
                shape: (a, reply) => FilterTop(reply, a, "functions", "name")),

            Pipe(game, "read_instance", "Live object fields",
                "The live values of an object's fields: decoded value, raw hex, the name and class an object "
                + "pointer points to, a string's text, array elements up to array_limit. The main tool for "
                + "inspecting an object. An array field's array_inner_addr / array_inner_type / array_elem_size "
                + "and offset feed read_array.",
                [("addr", McpSchema.Addr("The object's address."), true),
                 ("class_addr", McpSchema.Addr("Read the object as this class (default: its own)."), false),
                 ("array_limit", McpSchema.Int("Array elements shown inline per array field.", 0, 256, 16), false),
                 ("preview_limit", McpSchema.Int("Struct members previewed per struct field.", 0, 6, 2), false),
                 NameFilterProp],
                "walk_instance", (a, r) =>
                {
                    r["addr"] = a.Addr("addr");
                    if (a.OptAddr("class_addr") is { } cls) r["class_addr"] = cls;
                    r["array_limit"] = a.Int("array_limit", 16, 0, 256);
                    r["preview_limit"] = a.Int("preview_limit", 2, 0, 6);
                },
                shape: (a, reply) => FilterTop(reply, a, "fields", "name")),

            Pipe(game, "read_array", "Array elements",
                "A page of one TArray field's elements. Take field_offset (the field's offset), inner_addr "
                + "(array_inner_addr), inner_type (array_inner_type) and elem_size (array_elem_size) from "
                + "read_instance's field.",
                [("addr", McpSchema.Addr("The object holding the array."), true),
                 ("field_offset", McpSchema.Int("The TArray field's offset in the object.", 0, int.MaxValue), true),
                 ("inner_addr", McpSchema.Addr("The array's inner property (array_inner_addr)."), true),
                 ("inner_type", McpSchema.Str("The inner property's type, e.g. \"FloatProperty\"."), true),
                 ("elem_size", McpSchema.Int("Bytes per element.", 1, 1 << 20), true),
                 ("offset", McpSchema.Int("First element.", 0, int.MaxValue, 0), false),
                 ("limit", McpSchema.Int("Most elements returned.", 1, 256, 32), false)],
                "read_array_elements", (a, r) =>
                {
                    r["addr"] = a.Addr("addr");
                    r["field_offset"] = a.OptInt("field_offset") ?? throw new McpToolError("'field_offset' is required.");
                    r["inner_addr"] = a.Addr("inner_addr");
                    r["inner_type"] = a.ReqStr("inner_type");
                    r["elem_size"] = a.OptInt("elem_size") ?? throw new McpToolError("'elem_size' is required.");
                    r["offset"] = a.Int("offset", 0, 0, int.MaxValue);
                    r["limit"] = a.Int("limit", 32, 1, 256);
                }),

            Pipe(game, "read_memory", "Read memory",
                "Raw bytes at an address, as hex. Fails cleanly on unreadable memory.",
                [("addr", McpSchema.Addr("Start address."), true),
                 ("size", McpSchema.Int("Bytes to read.", 1, 4096, 256), false)],
                "read_mem", (a, r) =>
                {
                    r["addr"] = a.Addr("addr");
                    r["size"] = a.Int("size", 256, 1, 4096);
                }, needsScan: false),

            Pipe(game, "read_world", "World actors",
                "The actors in the world's persistent level, each with its components. Default world: GWorld.",
                [("addr", McpSchema.Addr("A UWorld address (default: GWorld)."), false),
                 ("limit", McpSchema.Int("Most actors returned.", 1, 500, 50), false)],
                "walk_world", (a, r) =>
                {
                    if (a.OptAddr("addr") is { } w) r["addr"] = w;
                    r["limit"] = a.Int("limit", 50, 1, 500);
                }, HeavySeconds),

            Pipe(game, "read_find_refs", "Who points at an object",
                "Objects that hold a pointer to the given object (object, weak, soft, lazy, interface and delegate "
                + "fields, and arrays, maps and sets of them), with the field and its offset. Stops at 30 s; "
                + "deadline_hit then says the list may be incomplete.",
                [("addr", McpSchema.Addr("The object pointed at."), true),
                 ("max_results", McpSchema.Int("Most references returned.", 1, 256, 32), false)],
                "find_refs_to_uobject", (a, r) =>
                {
                    r["addr"] = a.Addr("addr");
                    r["max_results"] = a.Int("max_results", 32, 1, 256);
                }, HeavySeconds),

            Pipe(game, "read_path_from_root", "Pointer path from GWorld",
                "The shortest chain of pointer fields from GWorld (or the game engine) to a target object or to "
                + "an address inside one: the path a trainer or a Cheat Engine pointer follows. Stops at 20 s.",
                [("target", McpSchema.Addr("The object, or an address inside it."), true),
                 ("object_addr", McpSchema.Addr("The object owning target, when known (skips a search)."), false),
                 ("max_depth", McpSchema.Int("Most pointer hops.", 1, 32, 5), false),
                 ("root_kind", McpSchema.Enum("Where the path starts.", "gworld", "engine"), false),
                 ("deep", McpSchema.Bool("Also follow pointers inside struct arrays (slower).", false), false)],
                "find_path_from_gworld", (a, r) =>
                {
                    r["target"] = a.Addr("target");
                    if (a.OptAddr("object_addr") is { } o) r["object_addr"] = o;
                    r["max_depth"] = a.Int("max_depth", 5, 1, 32);
                    string root = a.Str("root_kind") ?? "gworld";
                    if (root is not ("gworld" or "engine"))
                        throw new McpToolError("'root_kind' must be \"gworld\" or \"engine\".");
                    if (root != "gworld") r["root_kind"] = root;
                    if (a.Bool("deep", false)) r["deep"] = true;
                }, HeavySeconds),

            Pipe(game, "read_related", "Related objects",
                "An object's class and outer, its controller or pawn counterpart, and the sub-objects it owns "
                + "(components, ability system, attribute sets).",
                [("addr", McpSchema.Addr("The object."), true),
                 ("max_results", McpSchema.Int("Most related objects returned.", 1, 256, 64), false)],
                "get_related_objects", (a, r) =>
                {
                    r["addr"] = a.Addr("addr");
                    r["max_results"] = a.Int("max_results", 64, 1, 256);
                }),

            Pipe(game, "read_current_target", "Player's current target",
                "Ranked guesses at the actor the player is targeting, best first.",
                [("max_candidates", McpSchema.Int("Most candidates returned.", 1, 32, 8), false)],
                "get_current_target", (a, r) => r["max_candidates"] = a.Int("max_candidates", 8, 1, 32)),

            Pipe(game, "read_search_properties", "Search properties by name",
                "Classes that have a property whose name matches the query, with its type, offset and the class "
                + "that defines it. deep also searches inside struct members and struct containers.",
                [("query", McpSchema.Str("Property name or part of it, e.g. \"Health\"."), true),
                 ("game_only", McpSchema.Bool("Game classes only.", true), false),
                 ("deep", McpSchema.Bool("Search nested struct members too.", false), false),
                 ("types", McpSchema.StrArray("Property types to keep, e.g. [\"FloatProperty\"]."), false),
                 ("limit", McpSchema.Int("Most matches returned.", 1, 2000, 100), false)],
                "search_properties", (a, r) =>
                {
                    r["query"] = a.ReqStr("query");
                    if (!a.Bool("game_only", true)) r["game_only"] = false;
                    if (a.Bool("deep", false)) r["deep"] = true;
                    if (a.StrList("types") is { Count: > 0 } t) r["types"] = ToArray(t);
                    r["limit"] = a.Int("limit", 100, 1, 2000);
                }, HeavySeconds),

            Pipe(game, "read_property_xrefs", "Functions using a property",
                "Blueprint functions whose bytecode reads or writes a property, with how often and how many "
                + "writes. prop_addr is the property's field_addr (from read_search_properties) or its addr.",
                [("prop_addr", McpSchema.Addr("The property's address."), true),
                 ("game_only", McpSchema.Bool("Game functions only.", true), false),
                 ("max_results", McpSchema.Int("Most functions returned.", 1, 1000, 100), false)],
                "find_property_xrefs", (a, r) =>
                {
                    r["prop_addr"] = a.Addr("prop_addr");
                    if (!a.Bool("game_only", true)) r["game_only"] = false;
                    r["max_results"] = a.Int("max_results", 100, 1, 1000);
                }, HeavySeconds),

            Pipe(game, "read_function_props", "Properties a function touches",
                "The properties one UFunction reads or writes, from its bytecode, or from its machine code for a "
                + "native function.",
                [("func_addr", McpSchema.Addr("The UFunction's address (addr in read_functions)."), true)],
                "walk_function_props", (a, r) => r["func_addr"] = a.Addr("func_addr"), HeavySeconds),

            Pipe(game, "read_function_code", "Native code address",
                "The machine-code entry of a native UFunction (\"\" for a Blueprint function).",
                [("func_addr", McpSchema.Addr("The UFunction's address."), true)],
                "get_function_code_addr", (a, r) => r["func_addr"] = a.Addr("func_addr")),

            Pipe(game, "read_functions_by_class", "Functions taking a class",
                "UFunctions that take or return the given class as a parameter.",
                [("class_addr", McpSchema.Addr("The UClass address."), true),
                 ("game_only", McpSchema.Bool("Game functions only.", true), false),
                 ("max_results", McpSchema.Int("Most functions returned.", 1, 1000, 100), false)],
                "find_functions_by_class", (a, r) =>
                {
                    r["class_addr"] = a.Addr("class_addr");
                    if (!a.Bool("game_only", true)) r["game_only"] = false;
                    r["max_results"] = a.Int("max_results", 100, 1, 1000);
                }, HeavySeconds),

            Filtered(game, "read_list_functions", "List all functions",
                "Every UFunction with its class, flags and parameter count. Heavy on a large game: prefer "
                + "read_functions for one class, and pass a name_filter here.",
                [("game_only", McpSchema.Bool("Game functions only.", true), false),
                 ("limit", McpSchema.Int("Most functions returned.", 1, 2000, 100), false)],
                "list_all_functions", "functions", ["func_name", "class_name"],
                (a, r) => { if (!a.Bool("game_only", true)) r["game_only"] = false; },
                dllLimitKey: "limit", unfilteredCap: 100_000),

            Filtered(game, "read_list_enums", "List enums",
                "Every enum (native and Blueprint) with its entries (n = name, v = value).",
                [("limit", McpSchema.Int("Most enums returned.", 1, 1000, 50), false)],
                "list_enums", "enums", ["name", "full_path"], null,
                dllLimitKey: null, unfilteredCap: 0),

            Pipe(game, "read_ce_pointer", "Cheat Engine pointer",
                "A Cheat Engine pointer to an object (or a field of it): module + offset base and the offset "
                + "chain through GObjects.",
                [("addr", McpSchema.Addr("The object."), true),
                 ("field_offset", McpSchema.Int("A field's offset in the object.", 0, int.MaxValue, 0), false)],
                "get_ce_pointer_info", (a, r) =>
                {
                    r["addr"] = a.Addr("addr");
                    r["field_offset"] = a.Int("field_offset", 0, 0, int.MaxValue);
                }),

            Pipe(game, "read_datatable_rows", "DataTable rows",
                "A page of a DataTable's rows: row name, address and fields.",
                [("addr", McpSchema.Addr("The DataTable object."), true),
                 ("offset", McpSchema.Int("First row.", 0, int.MaxValue, 0), false),
                 ("limit", McpSchema.Int("Most rows returned.", 1, 256, 32), false)],
                "walk_datatable_rows", (a, r) =>
                {
                    r["addr"] = a.Addr("addr");
                    r["offset"] = a.Int("offset", 0, 0, int.MaxValue);
                    r["limit"] = a.Int("limit", 32, 1, 256);
                }),

            ValueScanBegin(game),

            Pipe(game, "read_value_scan_refine", "Value scan: next scan",
                "Narrows a value-scan session (Cheat Engine's Next Scan). Exact / Bigger / Smaller / Between "
                + "compare with value; Changed / Unchanged / Increased / Decreased compare with the previous scan.",
                [("session_id", McpSchema.Long("The session from read_value_scan_begin."), true),
                 ("scan_type", McpSchema.Enum("How to compare.", "Exact", "Bigger", "Smaller", "Between", "Changed",
                     "Unchanged", "Increased", "Decreased", "Contains", "StartsWith", "EndsWith"), true),
                 ("value", McpSchema.Str("The value, as text."), false),
                 ("value2", McpSchema.Str("Upper bound for Between."), false),
                 ("page_size", McpSchema.Int("Candidates in the first page.", 1, 500, 50), false)],
                "refine_value_scan", (a, r) =>
                {
                    r["session_id"] = Session(a);
                    r["scan_type"] = a.ReqStr("scan_type");
                    if (a.Str("value") is { } v) r["value"] = v;
                    if (a.Str("value2") is { } v2) r["value2"] = v2;
                    r["page_size"] = a.Int("page_size", 50, 1, 500);
                }, HeavySeconds, idempotent: false),

            Pipe(game, "read_value_scan_results", "Value scan: results",
                "A page of a session's candidates, optionally filtered by text, sorted, or with classes left out.",
                [("session_id", McpSchema.Long("The session."), true),
                 ("offset", McpSchema.Int("First candidate.", 0, int.MaxValue, 0), false),
                 ("limit", McpSchema.Int("Most candidates returned.", 1, 500, 50), false),
                 ("filter", McpSchema.Str("Text a candidate's names or value must contain."), false),
                 ("sort_key", McpSchema.Enum("Sort column.", "scan", "address", "value", "class", "field",
                     "instance", "type", "offset", "index"), false),
                 ("sort_desc", McpSchema.Bool("Sort descending.", false), false),
                 ("exclude_classes", McpSchema.StrArray("Class names to hide."), false)],
                "query_candidates", (a, r) =>
                {
                    r["session_id"] = Session(a);
                    r["offset"] = a.Int("offset", 0, 0, int.MaxValue);
                    r["limit"] = a.Int("limit", 50, 1, 500);
                    if (a.Str("filter") is { Length: > 0 } f) r["filter"] = f;
                    if (a.Str("sort_key") is { Length: > 0 } s) r["sort_key"] = s;
                    if (a.Bool("sort_desc", false)) r["sort_desc"] = true;
                    if (a.StrList("exclude_classes") is { Count: > 0 } ex) r["exclude_classes"] = ToArray(ex);
                }),

            Pipe(game, "read_value_scan_end", "Value scan: end",
                "Ends a value-scan session and frees it. Sessions also end after 5 minutes idle, and when this "
                + "server's connection closes.",
                [("session_id", McpSchema.Long("The session."), true)],
                "end_value_scan", (a, r) => r["session_id"] = Session(a), needsScan: false),

            Pipe(game, "read_pipe_clients", "Pipe clients",
                "The programs connected to the game's pipe (the DLL serves 6 at once): pid, exe, last command and "
                + "whether it is running.",
                [], "pipe_clients", null, needsScan: false),

            Pipe(game, "read_offsets", "Engine offsets",
                "The engine layout offsets the DLL measured in this game (UObject, FField, FProperty...), and "
                + "whether they were validated.",
                [], "get_offsets", null, needsScan: false),
        };
        return tools;
    }

    // ── Tools that are more than one command ────────────────────────────────────────────────────────────

    private static McpTool Games(IMcpGame game) => new(
        "read_games", "Games running the dumper",
        "The game processes running the UE5Dumper DLL: pid, exe, window title, and which one this server is "
        + "connected to. Pass a pid to the other tools when there are several.",
        McpSchema.Object(), ReadOnly: true,
        (_, _) =>
        {
            var (games, legacy) = game.ListGames();
            var arr = new JsonArray();
            foreach (var g in games)
                arr.Add(new JsonObject
                {
                    ["pid"] = g.Pid,
                    ["exe"] = g.Exe,
                    ["window_title"] = g.WindowTitle,
                    ["connected"] = g.Connected,
                });
            var payload = new JsonObject { ["games"] = arr, ["connected_pid"] = game.ConnectedPid };
            if (legacy) payload["legacy_pipe"] = true;
            string? note = games.Count == 0
                ? "No game runs the UE5Dumper DLL. Inject it, or start a game with a proxy DLL deployed."
                : legacy ? "An old DLL's shared pipe is up too: a game runs a DLL from before per-game pipes and "
                           + "cannot be reached until its proxy is updated (UE5DumpUI, Proxy Deploy, Update All)."
                : null;
            return Task.FromResult(new McpToolResult(payload, Note: note));
        });

    private static McpTool Status(IMcpGame game) => new(
        "read_status", "Game status",
        "The connected game's engine globals (GObjects, GNames, GWorld, GEngine) and how each was found, the UE "
        + "version, object count, module and DLL build, and whether the object scan is done. Call it first.",
        McpSchema.Object(PidProp), ReadOnly: true,
        async (a, ct) =>
        {
            int? pid = a.OptInt("pid");
            var reply = await game.SendAsync(pid, new JsonObject { ["cmd"] = "get_pointers" },
                TimeSpan.FromSeconds(LightSeconds), ct);
            if (!IsOk(reply)) return DllError(reply, game.TakeNotice());
            Strip(reply);
            bool scanned = McpGameLink.ScannedFrom(reply);
            if (scanned) game.MarkScanned();
            reply["scanned"] = scanned;
            string? note = null;
            if (!scanned)
            {
                var scan = await game.SendAsync(pid, new JsonObject { ["cmd"] = "scan_status" },
                    TimeSpan.FromSeconds(LightSeconds), ct);
                Strip(scan);
                reply["scan"] = scan;
                note = Truthy(scan, "running")
                    ? "The object scan is running: call read_status again until scanned is true."
                    : "The game is not scanned yet, so object tools answer with nothing. Once the game world is "
                      + "loaded, call scan_start.";
            }
            int ours = typeof(McpTools).Assembly.GetName().Version?.Revision ?? 0;
            long dll = McpJson.TryGetLong(reply["build_number"], out long n) ? n : 0;
            reply["mcp_build"] = ours;
            if (ours > 0 && dll > 0 && ours != dll)
                note = (note is null ? "" : note + " ")
                       + $"The game's DLL is build {dll} and this server build {ours}; a command newer than the DLL "
                       + "answers \"Unknown command\".";
            return Combine(new McpToolResult(reply, Note: note), game.TakeNotice());
        });

    private static McpTool ScanStart(IMcpGame game) => new(
        "scan_start", "Start the object scan",
        "Starts the DLL's scan for the engine globals and the object pool, on a game whose scan has not run (a "
        + "game loaded through a proxy DLL waits for one). Load into the game world first. Returns at once; poll "
        + "read_status until scanned is true. Does nothing on a game already scanned, so it never disturbs the UI.",
        McpSchema.Object(PidProp), ReadOnly: false,
        async (a, ct) =>
        {
            int? pid = a.OptInt("pid");
            var ptrs = await game.SendAsync(pid, new JsonObject { ["cmd"] = "get_pointers" },
                TimeSpan.FromSeconds(LightSeconds), ct);
            if (!IsOk(ptrs)) return DllError(ptrs, game.TakeNotice());
            if (McpGameLink.ScannedFrom(ptrs))
            {
                game.MarkScanned();
                return Combine(new McpToolResult(new JsonObject
                {
                    ["already_scanned"] = true,
                    ["object_count"] = ptrs["object_count"]?.DeepClone(),
                }), game.TakeNotice());
            }
            var reply = await game.SendAsync(pid, new JsonObject { ["cmd"] = "trigger_scan" },
                TimeSpan.FromSeconds(LightSeconds), ct);
            if (!IsOk(reply)) return DllError(reply, game.TakeNotice());
            Strip(reply);
            return Combine(new McpToolResult(reply,
                Note: "The scan runs in the background: poll read_status until scanned is true."), game.TakeNotice());
        });

    private static McpTool ValueScanBegin(IMcpGame game)
    {
        string[] dataTypes = ["Int8", "Int16", "Int32", "Int64", "UInt8", "UInt16", "UInt32", "UInt64", "Float",
                              "Double", "Bool", "FString", "FName", "FText", "FVector", "FRotator"];
        return Pipe(game, "read_value_scan_begin", "Value scan: first scan",
            "Cheat Engine-style First Scan over every object's UPROPERTY fields. Opens a session on this server's "
            + "connection (read_value_scan_refine narrows it, read_value_scan_results pages it, "
            + "read_value_scan_end frees it) and returns its first page and a class histogram. Strings compare "
            + "case-insensitively. Vectors take \"x,y,z\".",
            [("data_type", McpSchema.Enum("The value's type.", dataTypes), true),
             ("scan_type", McpSchema.Enum("How to compare.", "Exact", "Bigger", "Smaller", "Between", "Contains",
                 "StartsWith", "EndsWith"), false),
             ("value", McpSchema.Str("The value, as text, e.g. \"100\", \"3.5\", \"true\", \"Sword\"."), true),
             ("value2", McpSchema.Str("Upper bound for Between."), false),
             ("game_only", McpSchema.Bool("Game classes only.", true), false),
             ("deep", McpSchema.Bool("Also search deeply nested containers (slower).", false), false),
             ("native_c", McpSchema.Bool("Also search memory no UPROPERTY covers (native members; noisy).",
                 false), false),
             ("newest_first", McpSchema.Bool("Keep the newest objects when the result cap is hit.", false), false),
             ("case_sensitive", McpSchema.Bool("Strings compare case-sensitively.", false), false),
             ("deadline_ms", McpSchema.Int("Scan time budget.", 1000, 300_000, 15_000), false),
             ("page_size", McpSchema.Int("Candidates in the first page.", 1, 500, 50), false)],
            "begin_value_scan", (a, r) =>
            {
                string dt = a.ReqStr("data_type");
                if (Array.IndexOf(dataTypes, dt) < 0)
                    throw new McpToolError("'data_type' must be one of: " + string.Join(", ", dataTypes) + ".");
                r["data_type"] = dt;
                r["scan_type"] = a.Str("scan_type") ?? "Exact";
                r["value"] = a.ReqStr("value");
                if (a.Str("value2") is { } v2) r["value2"] = v2;
                if (!a.Bool("game_only", true)) r["game_only"] = false;
                if (a.Bool("deep", false)) r["deep"] = true;
                if (a.Bool("native_c", false)) r["native_c"] = true;
                if (a.Bool("newest_first", false)) r["newest_first"] = true;
                if (a.Bool("case_sensitive", false)) r["case_sensitive"] = true;
                int deadline = a.Int("deadline_ms", 15_000, 1000, 300_000);
                if (deadline != 15_000) r["deadline_ms"] = deadline;
                r["page_size"] = a.Int("page_size", 50, 1, 500);
            },
            // The scan stops at its own deadline; the reply must be allowed to come after it.
            timeoutFor: a => a.Int("deadline_ms", 15_000, 1000, 300_000) / 1000 + 60, idempotent: false);
    }

    // ── Builders ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A tool that is one DLL command: arguments go through <paramref name="fill"/> (which validates them
    /// before any pipe I/O), the reply through <paramref name="shape"/>.</summary>
    private static McpTool Pipe(IMcpGame game, string name, string title, string description,
        (string, JsonObject, bool)[] props, string cmd, Action<McpArgs, JsonObject>? fill,
        int timeoutSeconds = LightSeconds, bool needsScan = true, Func<McpArgs, JsonObject, JsonNode>? shape = null,
        Func<McpArgs, int>? timeoutFor = null, bool idempotent = true)
    {
        var all = new List<(string, JsonObject, bool)>(props) { PidProp };
        return new McpTool(name, title, description, McpSchema.Object([.. all]), ReadOnly: true,
            async (a, ct) =>
            {
                int? pid = a.OptInt("pid");
                var request = new JsonObject { ["cmd"] = cmd };
                fill?.Invoke(a, request);
                if (needsScan) await RequireScanAsync(game, pid, ct);
                int seconds = timeoutFor?.Invoke(a) ?? timeoutSeconds;
                var reply = await game.SendAsync(pid, request, TimeSpan.FromSeconds(seconds), ct);
                if (!IsOk(reply)) return DllError(reply, game.TakeNotice());
                Strip(reply);
                JsonNode payload = shape is null ? reply : shape(a, reply);
                return Combine(new McpToolResult(payload), game.TakeNotice());
            }, idempotent);
    }

    /// <summary>A list command filtered here by name. With a filter the DLL is asked for everything (up to
    /// <paramref name="unfilteredCap"/>), since its own limit would cut the list before any name is looked at; the
    /// matches are then cut to the caller's limit, and <c>matched</c> / <c>truncated</c> say how many there were.
    /// <paramref name="dllLimitKey"/> null: the command has no limit and always returns everything.</summary>
    private static McpTool Filtered(IMcpGame game, string name, string title, string description,
        (string, JsonObject, bool)[] props, string cmd, string listKey, string[] nameKeys,
        Action<McpArgs, JsonObject>? fill, string? dllLimitKey, int unfilteredCap)
    {
        var all = new List<(string, JsonObject, bool)>(props) { NameFilterProp };
        int maxLimit = props.Select(p => p.Item1 == "limit" ? (int?)p.Item2["maximum"]!.GetValue<int>() : null)
                            .FirstOrDefault(m => m is not null) ?? 1000;
        int defLimit = props.Select(p => p.Item1 == "limit" ? (int?)p.Item2["default"]!.GetValue<int>() : null)
                            .FirstOrDefault(m => m is not null) ?? 50;
        return Pipe(game, name, title, description, [.. all], cmd, (a, r) =>
            {
                fill?.Invoke(a, r);
                if (dllLimitKey is null) return;
                bool filtering = !string.IsNullOrWhiteSpace(a.Str("name_filter"));
                r[dllLimitKey] = filtering ? unfilteredCap : a.Int("limit", defLimit, 1, maxLimit);
            },
            ListSeconds,
            shape: (a, reply) =>
            {
                int limit = a.Int("limit", defLimit, 1, maxLimit);
                var terms = ObjectTreeFilter.SplitTerms(a.Str("name_filter"));
                if (reply[listKey] is not JsonArray list) return reply;
                if (terms.Length == 0 && list.Count <= limit) return reply;
                var keep = new JsonArray();
                int matched = 0;
                foreach (var item in list.ToList())
                {
                    if (item is not JsonObject obj) continue;
                    if (terms.Length > 0)
                    {
                        var fields = nameKeys.Select(k => McpJson.GetString(obj, k)).ToArray();
                        if (!ObjectTreeFilter.MatchesAllTerms(terms, fields)) continue;
                    }
                    matched++;
                    if (keep.Count < limit) { list.Remove(obj); keep.Add(obj); }
                }
                reply[listKey] = keep;
                reply["matched"] = matched;
                if (matched > keep.Count) reply["truncated"] = true;
                return reply;
            });
    }

    /// <summary>Before an object command: the game's scan must be done, or every answer is an empty list that reads
    /// like "nothing there". Asks once per connection; a scanned game stays scanned.</summary>
    private static async Task RequireScanAsync(IMcpGame game, int? pid, CancellationToken ct)
    {
        if (game.Scanned && (pid is null || pid == game.ConnectedPid)) return;
        var ptrs = await game.SendAsync(pid, new JsonObject { ["cmd"] = "get_pointers" },
            TimeSpan.FromSeconds(LightSeconds), ct);
        if (IsOk(ptrs) && McpGameLink.ScannedFrom(ptrs))
        {
            game.MarkScanned();
            return;
        }
        var scan = await game.SendAsync(pid, new JsonObject { ["cmd"] = "scan_status" },
            TimeSpan.FromSeconds(LightSeconds), ct);
        throw new McpToolError(Truthy(scan, "running")
            ? "The game's object scan is still running: call read_status until scanned is true, then call again."
            : "The game is not scanned yet, so this would answer with nothing. Once the game world is loaded, call "
              + "scan_start, then read_status until scanned is true.");
    }

    // ── Reply helpers ───────────────────────────────────────────────────────────────────────────────────

    private static bool IsOk(JsonObject reply) => Truthy(reply, "ok");

    private static bool Truthy(JsonObject obj, string key)
        => obj[key] is JsonValue v && v.TryGetValue<bool>(out bool b) && b;

    private static McpToolResult DllError(JsonObject reply, string? notice)
        => Combine(McpToolResult.Fail("The DLL refused: " + (McpJson.GetString(reply, "error") ?? "no reason given")
                                      + "."), notice);

    /// <summary>Drops the envelope (id, ok) and the timings the UI's pipe client adds to every reply.</summary>
    internal static void Strip(JsonObject reply)
    {
        foreach (var key in new[] { "id", "ok", "_t_read_ms", "_t_rxlog_ms", "_t_parse_ms" })
            reply.Remove(key);
    }

    private static McpToolResult Combine(McpToolResult result, string? notice)
        => notice is null ? result
         : result with { Note = result.Note is null ? notice : result.Note + " " + notice };

    private static JsonNode FilterTop(JsonObject reply, McpArgs a, string listKey, string nameKey)
    {
        var terms = ObjectTreeFilter.SplitTerms(a.Str("name_filter"));
        if (terms.Length == 0 || reply[listKey] is not JsonArray list) return reply;
        reply[listKey] = Keep(list, terms, nameKey);
        return reply;
    }

    private static JsonNode FilterNested(JsonObject reply, McpArgs a, string objKey, string listKey, string nameKey)
    {
        if (reply[objKey] is JsonObject inner) FilterTop(inner, a, listKey, nameKey);
        return reply;
    }

    private static JsonArray Keep(JsonArray list, string[] terms, string nameKey)
    {
        var keep = new JsonArray();
        foreach (var item in list.ToList())
        {
            if (item is JsonObject obj && ObjectTreeFilter.MatchesAllTerms(terms, McpJson.GetString(obj, nameKey)))
            {
                list.Remove(obj);
                keep.Add(obj);
            }
        }
        return keep;
    }

    private static long Session(McpArgs a)
        => a.OptLong("session_id") ?? throw new McpToolError("'session_id' is required.");

    private static JsonArray ToArray(IEnumerable<string> items)
    {
        var arr = new JsonArray();
        foreach (var s in items) arr.Add(s);
        return arr;
    }
}
