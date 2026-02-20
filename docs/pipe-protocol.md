# Pipe Protocol — JSON IPC Specification

> Moved from CLAUDE.md. Defines the Named Pipe JSON protocol between UE5Dumper.dll and UE5DumpUI.

Pipe 名稱：`\\.\pipe\UE5DumpBfx`
格式：JSON，newline-delimited（每筆訊息一行 `\n`）
方向：雙向，Request/Response + 主動推送 Event

-----

## Request（UI → DLL）

```jsonc
// 初始化，回傳 UE 版本與 global pointer 位址
{ "id": 1, "cmd": "init" }

// 取得 global pointer 位址
{ "id": 2, "cmd": "get_pointers" }

// 取得 object 總數
{ "id": 3, "cmd": "get_object_count" }

// 分頁取得 object 列表
{ "id": 4, "cmd": "get_object_list", "offset": 0, "limit": 200 }

// 取得單一 object 詳情
{ "id": 5, "cmd": "get_object", "addr": "0x7FF123456789" }

// 搜尋 object
{ "id": 6, "cmd": "find_object", "path": "/Game/BP_Player.BP_Player_C" }

// 遍歷 class 欄位
{ "id": 7, "cmd": "walk_class", "addr": "0x7FF123456789" }

// 讀取記憶體（回傳 hex string）
{ "id": 8, "cmd": "read_mem", "addr": "0x7FF123456789", "size": 256 }

// 寫入記憶體
{ "id": 9, "cmd": "write_mem", "addr": "0x7FF123456789", "bytes": "3F800000" }

// 訂閱位址定期推送（Live Watch）
{ "id": 10, "cmd": "watch", "addr": "0x7FF123456789", "size": 4, "interval_ms": 500 }

// 取消訂閱
{ "id": 11, "cmd": "unwatch", "addr": "0x7FF123456789" }

// 取得動態偵測的 offset 值（診斷用）
{ "id": 12, "cmd": "get_offsets" }

// 遍歷 GWorld → PersistentLevel → Actors
{ "id": 13, "cmd": "walk_world" }

// 搜尋特定 class 的所有 instance
{ "id": 14, "cmd": "find_instances", "class_name": "BP_Player_C", "limit": 100 }

// 取得 CE pointer info（XML 格式，用於 Cheat Engine 匯入）
{ "id": 15, "cmd": "get_ce_pointer_info", "addr": "0x7FF123456789", "class_addr": "0x7FF..." }
```

-----

## Response（DLL → UI）

```jsonc
// init 回應
{ "id": 1, "ok": true, "ue_version": 504 }

// get_pointers 回應
{
  "id": 2, "ok": true,
  "gobjects": "0x7FF600A12340",
  "gnames":   "0x7FF600B56780",
  "object_count": 58432
}

// get_object_list 回應
{
  "id": 4, "ok": true, "total": 58432,
  "objects": [
    { "addr": "0x...", "name": "BP_Player_C", "class": "BlueprintGeneratedClass",
      "outer": "0x..." },
    ...
  ]
}

// walk_class 回應
{
  "id": 7, "ok": true,
  "class": {
    "name": "BP_Player_C",
    "full_path": "/Game/BP_Player.BP_Player_C",
    "super_addr": "0x...",
    "super_name": "Character",
    "props_size": 1024,
    "fields": [
      { "addr": "0x...", "name": "Health",    "type": "FloatProperty",
        "offset": 720, "size": 4 },
      { "addr": "0x...", "name": "MaxHealth", "type": "FloatProperty",
        "offset": 724, "size": 4 },
      { "addr": "0x...", "name": "Inventory", "type": "ArrayProperty",
        "offset": 728, "size": 16 }
    ]
  }
}

// 錯誤回應
{ "id": 5, "ok": false, "error": "Object not found" }

// get_offsets 回應
{
  "id": 12, "ok": true,
  "offsets": {
    "ustruct_super": 64, "ustruct_children": 72, "ustruct_childprops": 80,
    "ustruct_propssize": 88, "ffield_class": 8, "ffield_next": 32,
    "ffield_name": 40, "fproperty_elemsize": 56, "fproperty_flags": 64,
    "fproperty_offset": 84, "ffieldclass_name": 0,
    "case_preserving_name": true, "offsets_validated": true
  }
}

// Watch 主動推送 Event（無 id）
{ "event": "watch", "addr": "0x7FF...", "bytes": "0000803F", "timestamp": 1234567890 }
```
