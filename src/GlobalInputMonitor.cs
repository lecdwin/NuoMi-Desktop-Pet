using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Drawing = System.Drawing;

namespace NuoMiDesktopPet
{
    internal enum MouseInputKind
    {
        LeftButton,
        RightButton,
        MiddleButton,
        XButton1,
        XButton2
    }

    /// <summary>
    /// Publishes lightweight global keyboard and mouse state changes.
    /// It deliberately exposes virtual-key codes only and never translates,
    /// combines, logs, or stores typed text.
    /// </summary>
    internal sealed class GlobalInputMonitor : IDisposable
    {
        private const int WhKeyboardLowLevel = 13;
        private const int WhMouseLowLevel = 14;

        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;

        private const int WmMouseMove = 0x0200;
        private const int WmLeftButtonDown = 0x0201;
        private const int WmLeftButtonUp = 0x0202;
        private const int WmRightButtonDown = 0x0204;
        private const int WmRightButtonUp = 0x0205;
        private const int WmMiddleButtonDown = 0x0207;
        private const int WmMiddleButtonUp = 0x0208;
        private const int WmMouseWheel = 0x020A;
        private const int WmXButtonDown = 0x020B;
        private const int WmXButtonUp = 0x020C;
        private const int WmMouseHorizontalWheel = 0x020E;

        private const uint LlkhfInjected = 0x00000010;
        private const uint LlmhfInjected = 0x00000001;
        private const int XButton1 = 0x0001;
        private const int XButton2 = 0x0002;

        private readonly object _syncRoot = new object();
        private readonly HookProcedure _keyboardProcedure;
        private readonly HookProcedure _mouseProcedure;
        private readonly bool _ignoreInjectedInput;

        private IntPtr _keyboardHook;
        private IntPtr _mouseHook;
        private bool _disposed;

        public GlobalInputMonitor()
            : this(true)
        {
        }

        public GlobalInputMonitor(bool ignoreInjectedInput)
        {
            _ignoreInjectedInput = ignoreInjectedInput;

            // Keep strong references for the entire hook lifetime.  Native
            // Windows hook registration does not keep managed delegates alive.
            _keyboardProcedure = KeyboardHookCallback;
            _mouseProcedure = MouseHookCallback;
        }

        public event Action<int, bool> KeyChanged;

        public event Action<MouseInputKind, bool> MouseButtonChanged;

        public event Action<int> MouseWheel;

        public event Action<Drawing.Point> MouseMoved;

        public bool IsRunning
        {
            get
            {
                lock (_syncRoot)
                {
                    return _keyboardHook != IntPtr.Zero &&
                        _mouseHook != IntPtr.Zero;
                }
            }
        }

        /// <summary>
        /// Installs both low-level hooks.  Repeated calls are harmless.
        /// The calling thread must have a Windows message loop, as the WPF UI
        /// thread does.
        /// </summary>
        public void Start()
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();

                if (_keyboardHook != IntPtr.Zero &&
                    _mouseHook != IntPtr.Zero)
                {
                    return;
                }

                // A previous failed unhook may leave a partial installation.
                // Clean it up before attempting a fresh, atomic pair.
                if (_keyboardHook != IntPtr.Zero ||
                    _mouseHook != IntPtr.Zero)
                {
                    StopCore();
                }

                IntPtr moduleHandle = GetModuleHandle(null);
                if (moduleHandle == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                IntPtr keyboardHook = SetWindowsHookEx(
                    WhKeyboardLowLevel,
                    _keyboardProcedure,
                    moduleHandle,
                    0U);
                if (keyboardHook == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                _keyboardHook = keyboardHook;

                IntPtr mouseHook = SetWindowsHookEx(
                    WhMouseLowLevel,
                    _mouseProcedure,
                    moduleHandle,
                    0U);
                if (mouseHook == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();

                    // Preserve a failed unhook handle so Stop/Dispose can retry.
                    if (UnhookWindowsHookEx(_keyboardHook))
                    {
                        _keyboardHook = IntPtr.Zero;
                    }

                    throw new Win32Exception(error);
                }

                _mouseHook = mouseHook;
            }
        }

        /// <summary>
        /// Removes both hooks.  Repeated calls are harmless.  Both unhooks are
        /// attempted even if one of them fails.
        /// </summary>
        public void Stop()
        {
            lock (_syncRoot)
            {
                StopCore();
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                StopCore();
                _disposed = true;
            }

            GC.SuppressFinalize(this);
        }

        private void StopCore()
        {
            int firstError = 0;
            bool failed = false;

            if (_mouseHook != IntPtr.Zero)
            {
                if (UnhookWindowsHookEx(_mouseHook))
                {
                    _mouseHook = IntPtr.Zero;
                }
                else
                {
                    failed = true;
                    firstError = Marshal.GetLastWin32Error();
                }
            }

            if (_keyboardHook != IntPtr.Zero)
            {
                if (UnhookWindowsHookEx(_keyboardHook))
                {
                    _keyboardHook = IntPtr.Zero;
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    failed = true;
                    if (firstError == 0)
                    {
                        firstError = error;
                    }
                }
            }

            if (failed)
            {
                throw new Win32Exception(firstError);
            }
        }

        private IntPtr KeyboardHookCallback(
            int code,
            IntPtr messagePointer,
            IntPtr dataPointer)
        {
            try
            {
                if (code >= 0)
                {
                    KeyboardLowLevelData data =
                        (KeyboardLowLevelData)Marshal.PtrToStructure(
                            dataPointer,
                            typeof(KeyboardLowLevelData));

                    if (!_ignoreInjectedInput ||
                        (data.Flags & LlkhfInjected) == 0U)
                    {
                        int message = messagePointer.ToInt32();
                        if (message == WmKeyDown ||
                            message == WmSysKeyDown)
                        {
                            RaiseKeyChanged(unchecked((int)data.VirtualKey), true);
                        }
                        else if (message == WmKeyUp ||
                            message == WmSysKeyUp)
                        {
                            RaiseKeyChanged(unchecked((int)data.VirtualKey), false);
                        }
                    }
                }
            }
            catch
            {
                // Hook callbacks must never interrupt another application's
                // input path, including when a subscriber misbehaves.
            }

            return CallNextHookEx(
                _keyboardHook,
                code,
                messagePointer,
                dataPointer);
        }

        private IntPtr MouseHookCallback(
            int code,
            IntPtr messagePointer,
            IntPtr dataPointer)
        {
            try
            {
                if (code >= 0)
                {
                    MouseLowLevelData data =
                        (MouseLowLevelData)Marshal.PtrToStructure(
                            dataPointer,
                            typeof(MouseLowLevelData));

                    if (!_ignoreInjectedInput ||
                        (data.Flags & LlmhfInjected) == 0U)
                    {
                        PublishMouseMessage(messagePointer.ToInt32(), data);
                    }
                }
            }
            catch
            {
                // Never allow managed code to break the global input chain.
            }

            return CallNextHookEx(
                _mouseHook,
                code,
                messagePointer,
                dataPointer);
        }

        private void PublishMouseMessage(int message, MouseLowLevelData data)
        {
            switch (message)
            {
                case WmMouseMove:
                    RaiseMouseMoved(new Drawing.Point(data.Point.X, data.Point.Y));
                    break;

                case WmLeftButtonDown:
                    RaiseMouseButtonChanged(MouseInputKind.LeftButton, true);
                    break;

                case WmLeftButtonUp:
                    RaiseMouseButtonChanged(MouseInputKind.LeftButton, false);
                    break;

                case WmRightButtonDown:
                    RaiseMouseButtonChanged(MouseInputKind.RightButton, true);
                    break;

                case WmRightButtonUp:
                    RaiseMouseButtonChanged(MouseInputKind.RightButton, false);
                    break;

                case WmMiddleButtonDown:
                    RaiseMouseButtonChanged(MouseInputKind.MiddleButton, true);
                    break;

                case WmMiddleButtonUp:
                    RaiseMouseButtonChanged(MouseInputKind.MiddleButton, false);
                    break;

                case WmXButtonDown:
                    PublishXButton(data.MouseData, true);
                    break;

                case WmXButtonUp:
                    PublishXButton(data.MouseData, false);
                    break;

                case WmMouseWheel:
                case WmMouseHorizontalWheel:
                    RaiseMouseWheel(unchecked((short)(data.MouseData >> 16)));
                    break;
            }
        }

        private void PublishXButton(uint mouseData, bool isDown)
        {
            int button = unchecked((ushort)(mouseData >> 16));
            if (button == XButton1)
            {
                RaiseMouseButtonChanged(MouseInputKind.XButton1, isDown);
            }
            else if (button == XButton2)
            {
                RaiseMouseButtonChanged(MouseInputKind.XButton2, isDown);
            }
        }

        private void RaiseKeyChanged(int virtualKey, bool isDown)
        {
            Action<int, bool> handler = KeyChanged;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(virtualKey, isDown);
            }
            catch
            {
                // Subscribers are isolated from the native hook callback.
            }
        }

        private void RaiseMouseButtonChanged(
            MouseInputKind button,
            bool isDown)
        {
            Action<MouseInputKind, bool> handler = MouseButtonChanged;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(button, isDown);
            }
            catch
            {
                // Subscribers are isolated from the native hook callback.
            }
        }

        private void RaiseMouseWheel(int delta)
        {
            Action<int> handler = MouseWheel;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(delta);
            }
            catch
            {
                // Subscribers are isolated from the native hook callback.
            }
        }

        private void RaiseMouseMoved(Drawing.Point point)
        {
            Action<Drawing.Point> handler = MouseMoved;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(point);
            }
            catch
            {
                // Subscribers are isolated from the native hook callback.
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(
                    typeof(GlobalInputMonitor).FullName);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr HookProcedure(
            int code,
            IntPtr messagePointer,
            IntPtr dataPointer);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardLowLevelData
        {
            public uint VirtualKey;
            public uint ScanCode;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseLowLevelData
        {
            public NativePoint Point;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int hookIdentifier,
            HookProcedure callback,
            IntPtr moduleHandle,
            uint threadIdentifier);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(
            IntPtr hook,
            int code,
            IntPtr messagePointer,
            IntPtr dataPointer);

        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Auto,
            SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string moduleName);
    }
}
