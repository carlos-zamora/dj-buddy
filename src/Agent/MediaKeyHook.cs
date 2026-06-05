using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DJBuddy.Agent;

/// <summary>
/// Installs a system-wide low-level keyboard hook on a background message-loop thread
/// so that media keys (Play/Pause, Stop, Next, Previous) work even while
/// <see cref="Console.ReadLine"/> is blocking the main thread.
/// </summary>
/// <remarks>Only active on Windows. Dispose to unregister the hook.</remarks>
[SupportedOSPlatform("windows")]
internal sealed class MediaKeyHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const uint VK_MEDIA_NEXT_TRACK = 0xB0;
    private const uint VK_MEDIA_PREV_TRACK = 0xB1;
    private const uint VK_MEDIA_STOP       = 0xB2;
    private const uint VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const uint WM_QUIT = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public int ptX, ptY;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool   UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int    GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
    [DllImport("user32.dll")] private static extern bool   TranslateMessage(ref MSG lpMsg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG lpMsg);
    [DllImport("user32.dll")] private static extern bool   PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    private readonly Action _onPlayPause;
    private readonly Action _onStop;
    private readonly Action _onNext;
    private readonly Action _onPrev;
    private readonly LowLevelKeyboardProc _proc; // held to prevent GC
    private IntPtr _hookId;
    private uint _threadId;
    private bool _disposed;

    /// <summary>
    /// Starts the message loop and installs the hook.
    /// Blocks briefly until the hook is registered before returning.
    /// </summary>
    /// <param name="onPlayPause">Invoked on Play/Pause key.</param>
    /// <param name="onStop">Invoked on Stop key.</param>
    /// <param name="onNext">Invoked on Next Track key.</param>
    /// <param name="onPrev">Invoked on Previous Track key.</param>
    public MediaKeyHook(Action onPlayPause, Action onStop, Action onNext, Action onPrev)
    {
        _onPlayPause = onPlayPause;
        _onStop      = onStop;
        _onNext      = onNext;
        _onPrev      = onPrev;
        _proc        = HookCallback;

        var ready = new ManualResetEventSlim(false);
        new Thread(() => RunMessageLoop(ready)) { IsBackground = true, Name = "MediaKeyHook" }.Start();
        ready.Wait();
    }

    private void RunMessageLoop(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        _hookId   = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
        ready.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN && !_disposed)
        {
            var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            try
            {
                switch (kbd.vkCode)
                {
                    case VK_MEDIA_PLAY_PAUSE: _onPlayPause(); break;
                    case VK_MEDIA_STOP:       _onStop();      break;
                    case VK_MEDIA_NEXT_TRACK: _onNext();      break;
                    case VK_MEDIA_PREV_TRACK: _onPrev();      break;
                }
            }
            catch { /* swallow — callback must never throw into the hook */ }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_threadId != 0)
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}
