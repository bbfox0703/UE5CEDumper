// ============================================================
// CEPlugin.cpp — Minimal CE Plugin interface
//
// Allows CE to load UE5Dumper.dll from its plugin folder without
// the "missing CEPlugin_GetVersion" error.
//
// This plugin registers NO CE callbacks.  All UE5 functionality is
// activated by the Lua / CT script calling UE5_Init() and
// UE5_StartPipeServer() explicitly after injecting the DLL into the
// game process via loadLibrary().
//
// CE Plugin SDK v6 reference: docs/private/docs/CE-Plugin-API-Reference.md
// Known traps:             docs/private/docs/CE-Plugin-SDK-Notes.md
// ============================================================

#include <Windows.h>

// CE SDK v6 PluginVersion struct (cepluginsdk.h §12)
// Only two fields — do NOT add extras.
struct CePluginVersion {
    unsigned int version;   // Plugin sets this to CESDK_VERSION (6)
    char*        pluginname; // Pointer to static string — MUST NOT be stack variable
};

// SDK v6 constant
static constexpr unsigned int CESDK_VERSION = 6;

// Plugin name: static storage, valid for the entire DLL lifetime.
// TRAP: using a stack or temporary string here → CE reads freed memory → crash.
static char g_PluginName[] = "UE5CEDumper";

extern "C" {

// ── CEPlugin_GetVersion ────────────────────────────────────────────────────
// CE calls this immediately after LoadLibrary to verify the plugin and get
// its name.  Must return TRUE.
//
// TRAP: second parameter is an INT VALUE (sizeof struct ≈ 16 on x64),
// NOT a pointer.  Treating it as int* and dereferencing → AV at 0x10.
__declspec(dllexport)
BOOL __stdcall CEPlugin_GetVersion(CePluginVersion* pv, int /*sizeofpluginversion*/)
{
    if (!pv) return FALSE;
    pv->version    = CESDK_VERSION; // Report SDK version 6
    pv->pluginname = g_PluginName;  // Static — safe to read after return
    return TRUE;
}

// ── CEPlugin_InitializePlugin ──────────────────────────────────────────────
// CE calls this when the plugin is enabled (after GetVersion succeeds).
// We accept the ExportedFunctions pointer but do not store or use it —
// no CE callbacks are registered here.
// The first parameter is PExportedFunctions; declared as void* to avoid
// depending on the 100+ field ExportedFunctions struct.
__declspec(dllexport)
BOOL __stdcall CEPlugin_InitializePlugin(void* /*ef*/, int /*pluginid*/)
{
    return TRUE;
}

// ── CEPlugin_DisablePlugin ─────────────────────────────────────────────────
// CE calls this when the plugin is disabled or CE is closing.
// Nothing to unregister since we registered nothing.
__declspec(dllexport)
BOOL __stdcall CEPlugin_DisablePlugin(void)
{
    return TRUE;
}

} // extern "C"
