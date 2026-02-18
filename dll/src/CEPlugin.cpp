// ============================================================
// CEPlugin.cpp — CE Plugin interface with Type 5 Main Menu
//
// When loaded by CE from its plugin folder, this adds a
// "UE5CEDumper: Inject & Connect" item to CE's Plugins menu.
//
// Clicking it calls CE's built-in InjectDLL() to load this same
// DLL into the currently attached game process, then calls
// UE5_AutoStart() (which runs UE5_Init + UE5_StartPipeServer).
//
// CE Plugin SDK v6 reference: docs/private/docs/CE-Plugin-API-Reference.md
// Known traps:             docs/private/docs/CE-Plugin-SDK-Notes.md
// ============================================================

#include <Windows.h>
#include <cstddef>   // offsetof
#include <cstring>

// ── External: DLL module handle saved in dllmain.cpp ─────────────────────
// Used to resolve this DLL's own file path via GetModuleFileNameA().
extern HMODULE g_hDllModule;

// ── CE SDK v6 constants ───────────────────────────────────────────────────
static constexpr unsigned int CESDK_VERSION = 6;
static constexpr int          ptMainMenu    = 5; // Plugin type 5 = CE main form menu

// ── PluginVersion struct (SDK v6 §1.5) ───────────────────────────────────
// Exactly two fields — do NOT add extras.
// TRAP: second param to GetVersion is int VALUE (sizeof struct ≈ 16 on x64),
//       NOT a pointer.  Dereferencing it → AV at 0x10.
struct CePluginVersion {
    unsigned int version;    // Set to CESDK_VERSION (6)
    char*        pluginname; // Must point to static storage, NOT stack variable
};

// ── Partial ExportedFunctions struct ─────────────────────────────────────
// CE's full struct has 100+ fields. We define only what we need, up to
// InjectDLL (offset 88). The size check in InitializePlugin ensures CE's
// struct is at least this large.
//
// Verified field offsets for x64 MSVC (no pragma pack):
//   [0]  sizeofExportedFunctions  int
//   [4]  _pad                     int   (natural alignment padding)
//   [8]  ShowMessage              ptr
//   [16] RegisterFunction         ptr
//   [24] UnregisterFunction       ptr
//   [32] OpenedProcessID          PULONG
//   [40] OpenedProcessHandle      PHANDLE
//   [48] GetMainWindowHandle      ptr
//   [56] AutoAssemble             ptr
//   [64] Assembler                ptr
//   [72] Disassembler             ptr
//   [80] ChangeRegistersAtAddress ptr
//   [88] InjectDLL                ptr
struct ExportedFunctions {
    int   sizeofExportedFunctions;
    int   _pad;                                      // alignment pad

    void (__stdcall *ShowMessage)       (char* message);
    int  (__stdcall *RegisterFunction)  (int pluginid, int type, void* initStruct);
    BOOL (__stdcall *UnregisterFunction)(int pluginid, int functionid);

    ULONG*  OpenedProcessID;     // Dereference to read current process ID
    HANDLE* OpenedProcessHandle; // Dereference to read current process handle

    void* GetMainWindowHandle;
    void* AutoAssemble;
    void* Assembler;
    void* Disassembler;
    void* ChangeRegistersAtAddress;

    BOOL (__stdcall *InjectDLL)(char* dllname, char* functiontocall);
    // (remaining 90+ fields omitted — not used by this plugin)
};

// Compile-time offset guards — build fails if struct layout is wrong
static_assert(offsetof(ExportedFunctions, ShowMessage)        ==  8, "ShowMessage @ 8");
static_assert(offsetof(ExportedFunctions, RegisterFunction)   == 16, "RegisterFunction @ 16");
static_assert(offsetof(ExportedFunctions, UnregisterFunction) == 24, "UnregisterFunction @ 24");
static_assert(offsetof(ExportedFunctions, OpenedProcessID)    == 32, "OpenedProcessID @ 32");
static_assert(offsetof(ExportedFunctions, OpenedProcessHandle)== 40, "OpenedProcessHandle @ 40");
static_assert(offsetof(ExportedFunctions, InjectDLL)          == 88, "InjectDLL @ 88");

// ── Type 5 (ptMainMenu) init struct ──────────────────────────────────────
struct PLUGINTYPE5_INIT {
    char* name;
    void (__stdcall *callbackroutine)(void);
    char* shortcut; // nullptr = no shortcut
};

// ── Plugin state ──────────────────────────────────────────────────────────
static ExportedFunctions g_CE;
static int g_PluginId   = -1;
static int g_MenuItemId = -1;

// Static strings — must NOT be stack variables (CE reads them after return)
static char g_PluginName[]   = "UE5CEDumper";
static char g_MenuName[]     = "UE5CEDumper: Inject && Connect";
static char g_AutoStartFn[]  = "UE5_AutoStart";

// ── Type 5 callback: runs on CE's main thread when the user clicks the menu item
static void __stdcall OnInjectAndConnect()
{
    // 1. Check that CE has a process attached
    if (!g_CE.OpenedProcessID || *g_CE.OpenedProcessID == 0) {
        g_CE.ShowMessage(const_cast<char*>(
            "UE5CEDumper: Please attach CE to a UE5 game process first."));
        return;
    }

    // 2. Get the full path of this DLL (running in CE's process)
    char dllPath[MAX_PATH] = {};
    if (!GetModuleFileNameA(g_hDllModule, dllPath, MAX_PATH)) {
        g_CE.ShowMessage(const_cast<char*>(
            "UE5CEDumper: Failed to resolve DLL path."));
        return;
    }

    // 3. Inject this DLL into the game process and call UE5_AutoStart.
    //    CE's InjectDLL handles: CreateRemoteThread → LoadLibrary(path)
    //    → GetProcAddress("UE5_AutoStart") → call it in the game process.
    //    UE5_AutoStart runs UE5_Init (AOB scan) + UE5_StartPipeServer.
    if (!g_CE.InjectDLL(dllPath, g_AutoStartFn)) {
        g_CE.ShowMessage(const_cast<char*>(
            "UE5CEDumper: Injection failed.\n"
            "Ensure the target is a 64-bit UE5 process and CE has\n"
            "sufficient privileges. Check Logs\\UE5Dumper-*.log for details."));
        return;
    }

    g_CE.ShowMessage(const_cast<char*>(
        "UE5CEDumper: DLL injected — GObjects/GNames scan started.\n"
        "Connect UE5DumpUI.exe to pipe: \\\\.\\pipe\\UE5DumpBfx\n"
        "(check the log file if the scan fails on first run)"));
}

// ── Required CE Plugin exports ────────────────────────────────────────────
extern "C" {

// ── CEPlugin_GetVersion ───────────────────────────────────────────────────
// CE calls this to verify the plugin and retrieve its name.
// Returns TRUE. Second param is an int VALUE (sizeof struct), NOT a pointer.
__declspec(dllexport)
BOOL __stdcall CEPlugin_GetVersion(CePluginVersion* pv, int /*sizeofpluginversion*/)
{
    if (!pv) return FALSE;
    pv->version    = CESDK_VERSION;
    pv->pluginname = g_PluginName;
    return TRUE;
}

// ── CEPlugin_InitializePlugin ─────────────────────────────────────────────
// CE calls this when the plugin is enabled. We copy the ExportedFunctions
// struct (partial copy — only our truncated fields) and register a Type 5
// Main Menu item.
__declspec(dllexport)
BOOL __stdcall CEPlugin_InitializePlugin(ExportedFunctions* ef, int pluginid)
{
    if (!ef) return FALSE;

    // Copy only our truncated struct portion from CE's full struct.
    // CE's struct is always larger than ours — safe because we verified offsets.
    g_CE      = *ef;
    g_PluginId = pluginid;

    // Sanity check: CE's declared struct size must be >= our partial struct.
    if (g_CE.sizeofExportedFunctions < static_cast<int>(sizeof(ExportedFunctions)))
        return FALSE;

    // Register Type 5 (ptMainMenu) — adds item to CE's main Plugins menu.
    PLUGINTYPE5_INIT init = {};
    init.name            = g_MenuName;    // "UE5CEDumper: Inject && Connect"
    init.callbackroutine = OnInjectAndConnect;
    init.shortcut        = nullptr;

    g_MenuItemId = g_CE.RegisterFunction(pluginid, ptMainMenu, &init);
    return (g_MenuItemId != -1) ? TRUE : FALSE;
}

// ── CEPlugin_DisablePlugin ────────────────────────────────────────────────
// CE calls this when the plugin is disabled or CE is closing.
// Unregister the menu item to free CE's internal state.
__declspec(dllexport)
BOOL __stdcall CEPlugin_DisablePlugin(void)
{
    if (g_MenuItemId != -1 && g_PluginId != -1) {
        g_CE.UnregisterFunction(g_PluginId, g_MenuItemId);
        g_MenuItemId = -1;
    }
    return TRUE;
}

} // extern "C"
