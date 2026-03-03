# Pipe Protocol — JSON IPC Specification

Named pipe: `\\.\pipe\UE5DumpBfx`
Format: JSON, newline-delimited (one message per `\n`)
Direction: bidirectional — Request/Response + async push Events

-----

## General Rules

- Every request carries an `"id"` (integer, caller-assigned).
- Every response echoes the same `"id"` and includes `"ok": true|false`.
- On failure: `"ok": false, "error": "message"`.
- On partial success: `"ok": true, "error": "message"` — check for `"error"` even when `ok` is true.
- All addresses are hex strings with no prefix (e.g. `"7FF600A12340"`) unless noted.
- Pagination advances by `"scanned"` (indices iterated), **not** by `objects.length` — null slots are skipped but still counted.

-----

## Commands (UI → DLL)

### Initialization & Info

```jsonc
// Initialize — returns UE version; DLL runs AOB scans internally on startup
{ "id": 1, "cmd": "init" }

// Get global pointer addresses
{ "id": 2, "cmd": "get_pointers" }

// Get total object count
{ "id": 3, "cmd": "get_object_count" }

// Get dynamically-detected DynOff values (diagnostics)
{ "id": 4, "cmd": "get_offsets" }
```

### Object Enumeration

```jsonc
// Paginated object list — advance by "scanned", not objects.length
{ "id": 5, "cmd": "get_object_list", "offset": 0, "limit": 200 }

// Single object detail
{ "id": 6, "cmd": "get_object", "addr": "7FF123456789" }

// Find object by full path
{ "id": 7, "cmd": "find_object", "path": "/Game/BP_Player.BP_Player_C" }

// Reverse address lookup: given any address, find the containing UObject
{ "id": 8, "cmd": "find_by_address", "addr": "7FF123456789" }
```

### Class & Instance Walking

```jsonc
// Walk all FFields of a UClass (static schema, no instance required)
{ "id": 9, "cmd": "walk_class", "addr": "7FF123456789" }

// Walk live field values of a UObject instance
// class_addr is optional (auto-resolved from UObject::ClassPrivate)
// array_limit: max inline array elements (default 64)
// preview_limit: max struct sub-fields in preview (0=none, default 2, max 6)
{ "id": 10, "cmd": "walk_instance", "addr": "7FF123456789" }
{ "id": 10, "cmd": "walk_instance", "addr": "7FF123456789", "class_addr": "7FF...", "preview_limit": 2 }

// Walk GWorld → PersistentLevel → Actors
{ "id": 11, "cmd": "walk_world" }

// Find all instances of a class by name
{ "id": 12, "cmd": "find_instances", "class_name": "BP_Player_C", "limit": 100 }
```

### Array Reading

```jsonc
// Read array elements (paginated) — Phase B+ for scalar/pointer/struct arrays
{
  "id": 13, "cmd": "read_array_elements",
  "addr": "7FF6BB123000",         // UObject instance address
  "field_offset": 256,             // byte offset of the TArray field within the instance
  "inner_addr": "7FF601234560",   // FProperty* of the inner element type
  "inner_type": "FloatProperty",
  "elem_size": 4,
  "offset": 0,                    // pagination start
  "limit": 64                     // max elements to return
}
```

### Memory Access

```jsonc
// Read raw memory (returns hex string)
{ "id": 14, "cmd": "read_mem", "addr": "7FF123456789", "size": 256 }

// Write raw memory
{ "id": 15, "cmd": "write_mem", "addr": "7FF123456789", "bytes": "3F800000" }

// Subscribe to address for periodic push (Live Watch)
{ "id": 16, "cmd": "watch", "addr": "7FF123456789", "size": 4, "interval_ms": 500 }

// Unsubscribe
{ "id": 17, "cmd": "unwatch", "addr": "7FF123456789" }
```

### CE Export

```jsonc
// Get CE-compatible XML pointer chain for an instance
{ "id": 18, "cmd": "get_ce_pointer_info", "addr": "7FF123456789", "class_addr": "7FF..." }
```

-----

## Responses (DLL → UI)

### init

```jsonc
{ "id": 1, "ok": true, "ue_version": 507 }
// ue_version: 507=UE5.7, 505=UE5.5, 427=UE4.27, 422=UE4.22, etc.
```

### get_pointers

```jsonc
{
  "id": 2, "ok": true,
  "gobjects":     "7FF600A12340",
  "gnames":       "7FF600B56780",
  "gworld":       "7FF600C89ABC",   // may be "0" if not found
  "object_count": 58432,
  "module_name":  "MyGame-Win64-Shipping.exe",
  "module_base":  "7FF600000000",
  "ue_version":   504,
  "gobjects_method": "aob",         // "aob", "data_scan", "string_ref", "pointer_scan", "not_found"
  "gnames_method":   "string_ref",
  "gworld_method":   "not_found",
  // AOB Usage Tracking (added v1.1)
  "pe_hash":              "5F3A1B2CCDD40000",  // TimeDateStamp(8hex) + SizeOfImage(8hex)
  "gobjects_pattern_id":  "GOBJ_V1",           // winning pattern ID, "" if not AOB
  "gnames_pattern_id":    "",
  "gworld_pattern_id":    "",
  "scan_stats": {
    "gobjects_tried": 40,    // patterns evaluated
    "gobjects_hit":   3,     // patterns with >=1 match
    "gnames_tried":   27,
    "gnames_hit":     0,
    "gworld_tried":   37,
    "gworld_hit":     0
  }
}
```

### get_object_list

```jsonc
{
  "id": 5, "ok": true,
  "total":   58432,
  "scanned": 200,      // ← indices iterated; advance offset by this, NOT by objects.length
  "objects": [
    {
      "addr":  "7FF123456000",
      "name":  "BP_Player_C_0",
      "class": "BlueprintGeneratedClass",
      "outer": "7FF123400000"
    }
  ]
}
```

### walk_class

```jsonc
{
  "id": 9, "ok": true,
  "class": {
    "name":       "BP_Player_C",
    "full_path":  "/Game/BP_Player.BP_Player_C",
    "addr":       "7FF123456000",
    "super_addr": "7FF123450000",
    "super_name": "Character",
    "props_size": 1024,
    "fields": [
      {
        "addr":   "7FF601234000",
        "name":   "Health",
        "type":   "FloatProperty",
        "offset": 720,
        "size":   4
      },
      {
        "addr":   "7FF601234020",
        "name":   "Inventory",
        "type":   "ArrayProperty",
        "offset": 728,
        "size":   16
      }
    ]
  }
}
```

### walk_instance

Field objects include all `walk_class` fields **plus** live typed values and array element data.

```jsonc
{
  "id": 10, "ok": true,
  "addr":        "7FF6AA000000",
  "name":        "BP_Player_C_0",
  "class":       "BP_Player_C",
  "class_addr":  "7FF123456000",
  "outer":       "7FF6BB000000",
  "outer_name":  "ThirdPersonMap",
  "outer_class": "World",
  "fields": [
    // --- Scalar field ---
    {
      "name":   "Health",
      "type":   "FloatProperty",
      "offset": 720,
      "size":   4,
      "hex":    "0000C842",
      "value":  "100.0000000000"
    },
    // --- BoolProperty (bit field) ---
    {
      "name":          "bIsDead",
      "type":          "BoolProperty",
      "offset":        724,
      "size":          1,
      "hex":           "00",
      "value":         "false",
      "bool_mask":     4,
      "bool_bit_idx":  2
    },
    // --- ObjectProperty (pointer) ---
    {
      "name":      "WeaponComponent",
      "type":      "ObjectProperty",
      "offset":    728,
      "size":      8,
      "hex":       "0050AA6F0C020000",
      "value":     "7FF20C6FAA5000",
      "ptr_name":  "BP_Weapon_C_3",
      "ptr_class": "BP_Weapon_C"
    },
    // --- EnumProperty ---
    {
      "name":       "MovementMode",
      "type":       "EnumProperty",
      "offset":     736,
      "size":       4,
      "hex":        "02000000",
      "value":      "2",
      "enum_name":  "EMovementMode::Walking"
    },
    // --- StrProperty (FString) ---
    {
      "name":       "PlayerTag",
      "type":       "StrProperty",
      "offset":     740,
      "size":       16,
      "hex":        "...",
      "str_value":  "Hero_01"
    },
    // --- ArrayProperty: scalar inner type (Phase B inline elements) ---
    {
      "name":             "DamageMultipliers",
      "type":             "ArrayProperty",
      "offset":           756,
      "size":             16,
      "hex":              "000001A0B4C00000 00000005 00000005",
      "count":            5,
      "array_inner_type": "FloatProperty",
      "array_elem_size":  4,
      "array_inner_addr": "7FF601234560",
      "elements": [
        { "i": 0, "v": "1.5000000000", "h": "0000C03F" },
        { "i": 1, "v": "2",            "h": "00000040" },
        { "i": 2, "v": "0.5000000000", "h": "0000003F" }
      ]
      // "elements" only present for scalar arrays with count <= 64
      // For enum inner type, each element also has "en": "EnumName::Value"
    },
    // --- ArrayProperty: NameProperty inner (Phase B) ---
    {
      "name":             "MissionIDs",
      "type":             "ArrayProperty",
      "offset":           772,
      "size":             16,
      "count":            30,
      "array_inner_type": "NameProperty",
      "array_elem_size":  8,
      "array_inner_addr": "7FF601234580",
      "elements": [
        { "i": 0, "v": "S001", "h": "..." },
        { "i": 1, "v": "S002", "h": "..." }
      ]
    },
    // --- ArrayProperty: struct inner type (no inline elements) ---
    {
      "name":                  "LevelCollections",
      "type":                  "ArrayProperty",
      "offset":                788,
      "size":                  16,
      "count":                 3,
      "array_inner_type":      "StructProperty",
      "array_inner_struct_type": "LevelCollection",
      "array_elem_size":       120,
      "array_inner_addr":      "7FF6012345A0",
      "array_inner_struct_addr": "7FF601234600"
      // no "elements" — Phase F scope
    }
  ]
}
```

### walk_world

```jsonc
{
  "id": 11, "ok": true,
  "world_addr": "7FF6CC000000",
  "world_name": "ThirdPersonMap",
  "level_addr": "7FF6DD000000",
  "actors": [
    { "addr": "7FF6AA000000", "name": "BP_Player_C_0",  "class": "BP_Player_C"  },
    { "addr": "7FF6AB000000", "name": "BP_Enemy_C_0",   "class": "BP_Enemy_C"   }
  ]
}

// Partial success (GWorld null, UWorld found via GObjects fallback):
{ "id": 11, "ok": true, "world_addr": "...", "actors": [...], "error": "GWorld=0, found via GObjects fallback" }

// GWorld failure (CDO or no UWorld instance):
{ "id": 11, "ok": true, "actors": [], "error": "PersistentLevel is null (CDO or uninitialized)" }
```

### find_instances

```jsonc
{
  "id": 12, "ok": true,
  "class_name":    "BP_Player_C",
  "total_scanned": 58432,
  "instances": [
    {
      "addr":  "7FF6AA000000",
      "name":  "BP_Player_C_0",
      "class": "BP_Player_C",
      "outer": "7FF6BB000000"
    }
  ]
}
```

### find_by_address

```jsonc
// Exact match (query addr == UObject base)
{
  "id": 8, "ok": true, "found": true, "match_type": "exact",
  "addr":            "7FF123456000",
  "index":           12345,
  "name":            "BP_Player_C_0",
  "class":           "BP_Player_C",
  "outer":           "7FF6BB000000",
  "offset_from_base": 0,
  "query_addr":      "7FF123456000"
}

// Contains match (query addr is inside a UObject)
{
  "id": 8, "ok": true, "found": true, "match_type": "contains",
  "addr":            "7FF123456000",
  "index":           12345,
  "name":            "BP_Player_C_0",
  "class":           "BP_Player_C",
  "outer":           "7FF6BB000000",
  "offset_from_base": 1929,
  "query_addr":      "7FF123456789"
}

// Not found
{ "id": 8, "ok": true, "found": false }
```

### get_offsets

```jsonc
{
  "id": 4, "ok": true,
  "offsets": {
    "ustruct_super":          64,
    "ustruct_children":       72,
    "ustruct_childprops":     80,
    "ustruct_propssize":      88,
    "ffield_class":           8,
    "ffield_next":            32,
    "ffield_name":            40,
    "fproperty_elemsize":     56,
    "fproperty_flags":        64,
    "fproperty_offset":       76,
    "uobject_outer":          32,
    "case_preserving_name":   false,
    "use_fproperty":          true,
    "offsets_validated":      true
  }
}
```

### read_array_elements

```jsonc
{
  "id": 13, "ok": true,
  "total":      128,
  "read":       64,
  "inner_type": "FloatProperty",
  "elem_size":  4,
  "elements": [
    { "i": 0, "v": "100.5000000000", "h": "0000C842" },
    { "i": 1, "v": "200",            "h": "00004843" }
  ]
}
```

### get_ce_pointer_info

```jsonc
{
  "id": 18, "ok": true,
  "xml": "<CheatTable><CheatEntries>...</CheatEntries></CheatTable>"
}
```

### read_mem / write_mem

```jsonc
// read_mem response
{ "id": 14, "ok": true, "bytes": "48 8B 05 AB CD EF 12 ..." }

// write_mem response
{ "id": 15, "ok": true }
```

### Error response (any command)

```jsonc
{ "id": 5, "ok": false, "error": "Object not found at address 7FF123456789" }
```

-----

## Push Events (DLL → UI, no id)

```jsonc
// Live watch periodic push (triggered by "watch" command)
{
  "event":     "watch",
  "addr":      "7FF123456789",
  "bytes":     "0000803F",
  "timestamp": 1234567890
}
```

-----

## Pagination Pattern

```
UI loop:
  offset = 0
  while allNodes.Count < target:
      send: { "cmd": "get_object_list", "offset": offset, "limit": 200 }
      recv: { "scanned": N, "objects": [...] }
      append objects to tree
      offset += scanned          ← MUST use "scanned", not objects.length
      if scanned == 0: break     ← end of array
```

**Why:** The DLL silently skips null/unnamed slots. `scanned` reports how many indices were actually iterated, ensuring the next request starts from the correct position even when many consecutive slots are empty (common in UE4).
