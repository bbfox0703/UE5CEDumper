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

// 反向位址查詢：給定任意位址，找到對應的 UObject instance
{ "id": 16, "cmd": "find_by_address", "addr": "0x7FF123456789" }
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

// find_by_address 回應（精確匹配）
{
  "id": 16, "ok": true, "found": true, "match_type": "exact",
  "addr": "0x7FF123456789", "index": 12345, "name": "BP_Player_C_0",
  "class": "BP_Player_C", "outer": "0x...", "offset_from_base": 0,
  "query_addr": "0x7FF123456789"
}

// find_by_address 回應（包含匹配：位址在某 UObject 內部）
{
  "id": 16, "ok": true, "found": true, "match_type": "contains",
  "addr": "0x7FF123456000", "index": 12340, "name": "BP_Player_C_0",
  "class": "BP_Player_C", "outer": "0x...", "offset_from_base": 1929,
  "query_addr": "0x7FF123456789"
}

// find_by_address 回應（未找到）
{ "id": 16, "ok": true, "found": false }

// walk_instance 回應 — ArrayProperty with inline elements (Phase B)
{
  "id": 8, "ok": true,
  "addr": "0x...", "name": "TestActor", "class": "BP_Player_C",
  "class_addr": "0x...", "outer": "0x...", "outer_name": "Level", "outer_class": "World",
  "fields": [
    {
      "name": "DamageMultipliers", "type": "ArrayProperty",
      "offset": 256, "size": 16,
      "hex": "000001A0B4C00000 00000005 00000005",
      "count": 5,
      "array_inner_type": "FloatProperty",
      "array_elem_size": 4,
      "array_inner_addr": "0x7FF601234560",
      "elements": [
        { "i": 0, "v": "1.5000000000", "h": "0000C03F" },
        { "i": 1, "v": "2", "h": "00000040" },
        { "i": 2, "v": "0.5000000000", "h": "0000003F" }
      ]
    }
  ]
}
// Note: "elements" is only present for scalar arrays with count <= 64.
// For enum arrays, each element also has "en" (resolved enum name).
// For struct/object arrays, "elements" is omitted.

// read_array_elements 請求（Phase B: paginated array element reading）
{ "cmd": "read_array_elements",
  "addr": "0x7FF6BB123000", "field_offset": 256,
  "inner_addr": "0x7FF601234560", "inner_type": "FloatProperty",
  "elem_size": 4, "offset": 0, "limit": 64 }

// read_array_elements 回應
{
  "id": 20, "ok": true,
  "total": 128, "read": 64,
  "inner_type": "FloatProperty", "elem_size": 4,
  "elements": [
    { "i": 0, "v": "100.5", "h": "0000C842" },
    { "i": 1, "v": "200", "h": "00004843" }
  ]
}

// Watch 主動推送 Event（無 id）
{ "event": "watch", "addr": "0x7FF...", "bytes": "0000803F", "timestamp": 1234567890 }
```
