using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using Forms = System.Windows.Forms;

namespace NuoMiDesktopPet
{
    internal enum FullscreenSample
    {
        Unknown,
        NotFullscreen,
        FullscreenOnPetMonitor
    }

    /// <summary>
    /// Samples whether the foreground application is genuinely full-screen on
    /// the monitor occupied by the pet. Unknown is intentionally separate from
    /// NotFullscreen so a tray menu, UAC transition or DWM race cannot restore
    /// a pet which was temporarily hidden.
    /// </summary>
    internal static class FullscreenDetector
    {
        private const uint DwmExtendedFrameBounds = 9;
        private const uint DwmCloaked = 14;
        private const uint GetAncestorRoot = 2;
        private const int RunningD3DFullScreen = 3;
        private static readonly uint CurrentProcessId =
            (uint)Process.GetCurrentProcess().Id;

        public static FullscreenSample Sample(
            Rectangle petBounds,
            out IntPtr foregroundWindow)
        {
            foregroundWindow = IntPtr.Zero;
            if (petBounds.Width <= 0 || petBounds.Height <= 0)
            {
                return FullscreenSample.Unknown;
            }

            foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                return FullscreenSample.Unknown;
            }

            IntPtr root = GetAncestor(foregroundWindow, GetAncestorRoot);
            if (root != IntPtr.Zero)
            {
                foregroundWindow = root;
            }

            if (foregroundWindow == GetShellWindow() ||
                foregroundWindow == GetDesktopWindow())
            {
                return FullscreenSample.NotFullscreen;
            }

            if (!IsWindowVisible(foregroundWindow) ||
                IsIconic(foregroundWindow))
            {
                return FullscreenSample.Unknown;
            }

            uint processId;
            GetWindowThreadProcessId(foregroundWindow, out processId);
            if (processId == 0 || processId == CurrentProcessId)
            {
                return FullscreenSample.Unknown;
            }

            string className = GetWindowClassName(foregroundWindow);
            if (IsShellClass(className))
            {
                return FullscreenSample.NotFullscreen;
            }

            int cloaked;
            if (TryGetCloaked(foregroundWindow, out cloaked) &&
                cloaked != 0)
            {
                return FullscreenSample.Unknown;
            }

            Forms.Screen foregroundScreen;
            Forms.Screen petScreen;
            try
            {
                foregroundScreen =
                    Forms.Screen.FromHandle(foregroundWindow);
                petScreen = Forms.Screen.FromRectangle(petBounds);
            }
            catch
            {
                return FullscreenSample.Unknown;
            }

            Rectangle frameBounds;
            Rectangle clientBounds;
            bool hasFrame = TryGetFrameBounds(
                foregroundWindow,
                out frameBounds);
            bool hasClient = TryGetClientScreenBounds(
                foregroundWindow,
                out clientBounds);
            bool isExclusiveD3D = IsRunningD3DFullScreen();

            // Any successfully-read geometry which clearly does not cover
            // the foreground monitor is decisive. A shell-wide D3D state may
            // belong to some other process behind this foreground window.
            if (hasFrame &&
                !CoversMonitor(
                    frameBounds,
                    foregroundScreen.Bounds))
            {
                return FullscreenSample.NotFullscreen;
            }
            if (hasClient &&
                !CoversMonitor(
                    clientBounds,
                    foregroundScreen.Bounds))
            {
                return FullscreenSample.NotFullscreen;
            }

            if (!hasFrame || !hasClient)
            {
                return isExclusiveD3D &&
                    SameScreen(foregroundScreen, petScreen)
                        ? FullscreenSample.FullscreenOnPetMonitor
                        : FullscreenSample.Unknown;
            }

            // Testing the pet monitor as well as the foreground monitor handles
            // both ordinary multi-monitor setups and a full-screen surface
            // intentionally spanning more than one display.
            if (!CoversMonitor(frameBounds, petScreen.Bounds) ||
                !CoversMonitor(clientBounds, petScreen.Bounds))
            {
                return FullscreenSample.NotFullscreen;
            }

            return FullscreenSample.FullscreenOnPetMonitor;
        }

        internal static bool CoversMonitor(
            Rectangle windowBounds,
            Rectangle monitorBounds)
        {
            if (windowBounds.Width <= 0 ||
                windowBounds.Height <= 0 ||
                monitorBounds.Width <= 0 ||
                monitorBounds.Height <= 0)
            {
                return false;
            }

            int tolerance = Math.Max(
                2,
                (int)Math.Round(
                    Math.Min(
                        monitorBounds.Width,
                        monitorBounds.Height) *
                    0.003));

            return
                windowBounds.Left <= monitorBounds.Left + tolerance &&
                windowBounds.Top <= monitorBounds.Top + tolerance &&
                windowBounds.Right >= monitorBounds.Right - tolerance &&
                windowBounds.Bottom >= monitorBounds.Bottom - tolerance &&
                windowBounds.Width >= monitorBounds.Width - tolerance * 2 &&
                windowBounds.Height >= monitorBounds.Height - tolerance * 2;
        }

        private static bool TryGetFrameBounds(
            IntPtr windowHandle,
            out Rectangle bounds)
        {
            NativeRect nativeBounds = new NativeRect();
            try
            {
                if (DwmGetWindowAttribute(
                        windowHandle,
                        DwmExtendedFrameBounds,
                        out nativeBounds,
                        Marshal.SizeOf(typeof(NativeRect))) == 0 &&
                    IsValid(nativeBounds))
                {
                    bounds = ToRectangle(nativeBounds);
                    return true;
                }
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }

            if (GetWindowRect(windowHandle, out nativeBounds) &&
                IsValid(nativeBounds))
            {
                bounds = ToRectangle(nativeBounds);
                return true;
            }

            bounds = Rectangle.Empty;
            return false;
        }

        private static bool TryGetClientScreenBounds(
            IntPtr windowHandle,
            out Rectangle bounds)
        {
            NativeRect clientRect;
            if (!GetClientRect(windowHandle, out clientRect) ||
                !IsValid(clientRect))
            {
                bounds = Rectangle.Empty;
                return false;
            }

            NativePoint topLeft = new NativePoint();
            topLeft.X = clientRect.Left;
            topLeft.Y = clientRect.Top;
            NativePoint bottomRight = new NativePoint();
            bottomRight.X = clientRect.Right;
            bottomRight.Y = clientRect.Bottom;
            if (!ClientToScreen(windowHandle, ref topLeft) ||
                !ClientToScreen(windowHandle, ref bottomRight))
            {
                bounds = Rectangle.Empty;
                return false;
            }

            bounds = Rectangle.FromLTRB(
                topLeft.X,
                topLeft.Y,
                bottomRight.X,
                bottomRight.Y);
            return bounds.Width > 0 && bounds.Height > 0;
        }

        private static bool TryGetCloaked(
            IntPtr windowHandle,
            out int cloaked)
        {
            cloaked = 0;
            try
            {
                return DwmGetWindowAttribute(
                    windowHandle,
                    DwmCloaked,
                    out cloaked,
                    Marshal.SizeOf(typeof(int))) == 0;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        private static bool IsRunningD3DFullScreen()
        {
            int state;
            try
            {
                return SHQueryUserNotificationState(out state) == 0 &&
                       state == RunningD3DFullScreen;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        private static bool SameScreen(
            Forms.Screen first,
            Forms.Screen second)
        {
            return first != null &&
                   second != null &&
                   String.Equals(
                       first.DeviceName,
                       second.DeviceName,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsShellClass(string className)
        {
            return
                String.Equals(
                    className,
                    "Progman",
                    StringComparison.OrdinalIgnoreCase) ||
                String.Equals(
                    className,
                    "WorkerW",
                    StringComparison.OrdinalIgnoreCase) ||
                String.Equals(
                    className,
                    "Shell_TrayWnd",
                    StringComparison.OrdinalIgnoreCase) ||
                String.Equals(
                    className,
                    "Shell_SecondaryTrayWnd",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static string GetWindowClassName(IntPtr windowHandle)
        {
            StringBuilder name = new StringBuilder(128);
            return GetClassName(windowHandle, name, name.Capacity) > 0
                ? name.ToString()
                : String.Empty;
        }

        private static bool IsValid(NativeRect bounds)
        {
            return bounds.Right > bounds.Left &&
                   bounds.Bottom > bounds.Top;
        }

        private static Rectangle ToRectangle(NativeRect bounds)
        {
            return Rectangle.FromLTRB(
                bounds.Left,
                bounds.Top,
                bounds.Right,
                bounds.Bottom);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(
            IntPtr windowHandle,
            uint flags);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(
            IntPtr windowHandle,
            out uint processId);

        [DllImport(
            "user32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern int GetClassName(
            IntPtr windowHandle,
            StringBuilder className,
            int maximumCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(
            IntPtr windowHandle,
            out NativeRect rectangle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetClientRect(
            IntPtr windowHandle,
            out NativeRect rectangle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ClientToScreen(
            IntPtr windowHandle,
            ref NativePoint point);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(
            IntPtr windowHandle,
            uint attribute,
            out NativeRect attributeValue,
            int attributeSize);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(
            IntPtr windowHandle,
            uint attribute,
            out int attributeValue,
            int attributeSize);

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(
            out int queryState);
    }
}
