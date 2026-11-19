using System.Runtime.InteropServices;
using System.Threading;
using UE5DumpUI.Core;

namespace UE5DumpUI.Services;

/// <summary>
/// Win32 RegisterHotKey-based global hotkey, on a dedicated message-loop thread.
/// AOT-safe: classic blittable DllImports (no LibraryImport / unsafe).
///
/// RegisterHotKey with a NULL hwnd posts WM_HOTKEY to the registering thread's
/// queue, so registration AND the GetMessage loop both run on one owned thread.
/// A combo already claimed elsewhere makes RegisterHotKey fail — that's exactly
/// the "is this combo free?" probe the auto-detect ladder relies on.
/// </summary>
public sealed class WindowsGlobalHotkeyService : IGlobalHotkeyService
{
    public IGlobalHotkeyRegistration? RegisterCursorHotkey(Action onPressed)
        => TryRegister(CursorCandidates, onPressed);

    public IGlobalHotkeyRegistration? RegisterSpecific(uint modifiers, uint vk, string label, Action onPressed)
        => TryRegister(new[] { (modifiers, (int)vk, label) }, onPressed);

    private static IGlobalHotkeyRegistration? TryRegister(
        (uint Mod, int Vk, string Label)[] candidates, Action onPressed)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var reg = new Registration(candidates, onPressed);
            if (reg.Label != null) return reg;
            reg.Dispose();   // no candidate claimable — tear the thread down
            return null;
        }
        catch
        {
            // Never let hotkey setup take down the app — degrade to "no hotkey".
            return null;
        }
    }

    // Candidate ladder for the cursor hotkey (docs/teleport-spec.md): Ctrl+F8..F5
    // first (ARPGs rarely bind Ctrl+F-keys), then Alt+F8..F5 as the fallback rung.
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_QUIT = 0x0012;
    private const int VK_F5 = 0x74, VK_F6 = 0x75, VK_F7 = 0x76, VK_F8 = 0x77;

    private static readonly (uint Mod, int Vk, string Label)[] CursorCandidates =
    {
        (HotkeyModifiers.Control, VK_F8, "Ctrl+F8"),
        (HotkeyModifiers.Control, VK_F7, "Ctrl+F7"),
        (HotkeyModifiers.Control, VK_F6, "Ctrl+F6"),
        (HotkeyModifiers.Control, VK_F5, "Ctrl+F5"),
        (HotkeyModifiers.Alt,     VK_F8, "Alt+F8"),
        (HotkeyModifiers.Alt,     VK_F7, "Alt+F7"),
        (HotkeyModifiers.Alt,     VK_F6, "Alt+F6"),
        (HotkeyModifiers.Alt,     VK_F5, "Alt+F5"),
    };

    private sealed class Registration : IGlobalHotkeyRegistration
    {
        private const int HotkeyId = 0xB1F0;   // arbitrary, unique within our thread
        // Budget (ms) for both the ctor's ready-wait and Dispose's thread-join.
        private const int HotkeyThreadWaitMs = 2000;
        private readonly Thread _thread;
        // IMPORTANT: this event is NOT disposed in the ctor. ManualResetEventSlim
        // forbids Dispose() concurrent with Set() (docs), and the worker calls
        // Set() right as the ctor's Wait() returns — a `using` here races Set()
        // against Dispose() and crashes the process natively. It is disposed in
        // Dispose() only AFTER the worker thread has joined, so Set() is fully finished.
        private readonly ManualResetEventSlim _ready = new(false);
        private uint _threadId;
        private volatile bool _disposed;

        public string? Label { get; private set; }
        string IGlobalHotkeyRegistration.Label => Label ?? "";

        public Registration((uint Mod, int Vk, string Label)[] candidates, Action onPressed)
        {
            _thread = new Thread(() =>
            {
                int chosen = -1;
                try
                {
                    _threadId = GetCurrentThreadId();
                    for (int i = 0; i < candidates.Length; i++)
                    {
                        if (RegisterHotKey(IntPtr.Zero, HotkeyId,
                                candidates[i].Mod | MOD_NOREPEAT, (uint)candidates[i].Vk))
                        {
                            chosen = i;
                            Label = candidates[i].Label;
                            break;
                        }
                    }
                }
                catch { /* registration failed → Label stays null below */ }
                finally { _ready.Set(); }   // always release the ctor's Wait()

                if (chosen < 0) return;

                // Message loop: WM_HOTKEY → fire; WM_QUIT (from Dispose) → exit.
                // Wrapped so NOTHING here can crash the process (uncaught
                // exceptions on a background thread terminate the app).
                try
                {
                    while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
                    {
                        if (msg.message == WM_HOTKEY)
                        {
                            try { onPressed(); } catch { /* keep the loop alive */ }
                        }
                    }
                    UnregisterHotKey(IntPtr.Zero, HotkeyId);
                }
                catch { /* swallow — degrade to no hotkey rather than crash */ }
            })
            {
                IsBackground = true,
                Name = "UE5CEDumper-CursorHotkey",
            };
            _thread.Start();
            _ready.Wait(HotkeyThreadWaitMs);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Break the GetMessage loop (no-op if the worker already exited after
            // a failed registration). Then join so the worker is fully done
            // before we dispose the event it touched.
            if (_threadId != 0)
                PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(HotkeyThreadWaitMs);
            _ready.Dispose();
        }
    }

    // --- Win32 (blittable DllImports — AOT-safe, no AllowUnsafeBlocks) ---

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // NOTE: the real user32 exports are GetMessageW / PostThreadMessageW —
    // there is NO bare "GetMessage"/"PostThreadMessage" symbol, so with
    // ExactSpelling=true the bare names throw EntryPointNotFoundException at the
    // first call (on the worker thread, uncaught → process crash). Pin the W
    // entry points explicitly. (RegisterHotKey / UnregisterHotKey have no A/W
    // variants, so their bare names are correct.)
    [DllImport("user32.dll", EntryPoint = "GetMessageW", ExactSpelling = true)]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();
}
