/// <summary>
/// Provides the Monitor class for detecting and storing system monitor information.
///
/// Enumerates physical monitors across Windows, Linux, and macOS platforms and stores
/// their position, dimensions, and camera assignment for multi-monitor VR displays.
/// </summary>
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Gimbl
{
    /// <summary>
    /// Stores monitor display information and camera assignment.
    /// </summary>
    [Serializable]
    public class Monitor
    {
        /// <summary>The left position of the monitor in pixels.</summary>
        public int left;

        /// <summary>The top position of the monitor in pixels.</summary>
        public int top;

        /// <summary>The width of the monitor in pixels.</summary>
        public int width;

        /// <summary>The height of the monitor in pixels.</summary>
        public int height;

        /// <summary>The pixels per point scaling factor for this monitor.</summary>
        public float pixelsPerPoint;

        /// <summary>The entity ID of the camera assigned to this monitor.</summary>
        public EntityId cameraEntityId;

        /// <summary>Creates a new monitor with the specified position and dimensions.</summary>
        /// <param name="leftPosition">The left position in pixels.</param>
        /// <param name="topPosition">The top position in pixels.</param>
        /// <param name="widthPixels">The width in pixels.</param>
        /// <param name="heightPixels">The height in pixels.</param>
        private Monitor(int leftPosition, int topPosition, int widthPixels, int heightPixels)
        {
            left = leftPosition;
            top = topPosition;
            width = widthPixels;
            height = heightPixels;
            pixelsPerPoint = 1.0f;
            cameraEntityId = EntityId.None;
        }

        /// <summary>Detects and returns a list of all system monitors.</summary>
        /// <returns>The list of detected monitors with their positions and dimensions.</returns>
        public static List<Monitor> EnumeratedMonitors()
        {
            List<Monitor> result = new List<Monitor>();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                EnumDisplayMonitors(
                    IntPtr.Zero,
                    IntPtr.Zero,
                    delegate(IntPtr hMonitor, IntPtr hdc, ref RectApi monitorRect, IntPtr dwData)
                    {
                        result.Add(
                            new Monitor(monitorRect.left, monitorRect.top, monitorRect.Width, monitorRect.Height)
                        );
                        return true;
                    },
                    0
                );
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                using (System.Diagnostics.Process xrandrProcess = new System.Diagnostics.Process())
                {
                    xrandrProcess.StartInfo.UseShellExecute = false;
                    xrandrProcess.StartInfo.RedirectStandardOutput = true;
                    xrandrProcess.StartInfo.FileName = "xrandr";
                    xrandrProcess.Start();
                    string xrandrOutput = xrandrProcess.StandardOutput.ReadToEnd();
                    if (!xrandrProcess.WaitForExit(5000))
                    {
                        xrandrProcess.Kill();
                    }
                    foreach (Match match in Regex.Matches(xrandrOutput, @"(\d+)x(\d+)\+(\d+)\+(\d+)"))
                    {
                        if (
                            match.Groups.Count >= 5
                            && int.TryParse(match.Groups[1].Value, out int matchWidth)
                            && int.TryParse(match.Groups[2].Value, out int matchHeight)
                            && int.TryParse(match.Groups[3].Value, out int matchLeft)
                            && int.TryParse(match.Groups[4].Value, out int matchTop)
                        )
                        {
                            result.Add(new Monitor(matchLeft, matchTop, matchWidth, matchHeight));
                        }
                    }
                }
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                using (System.Diagnostics.Process displayplacerProcess = new System.Diagnostics.Process())
                {
                    displayplacerProcess.StartInfo.UseShellExecute = false;
                    displayplacerProcess.StartInfo.RedirectStandardOutput = true;
                    displayplacerProcess.StartInfo.FileName = "/usr/local/bin/displayplacer";
                    displayplacerProcess.StartInfo.Arguments = "list";
                    displayplacerProcess.Start();
                    string displayplacerOutput = displayplacerProcess.StandardOutput.ReadToEnd();
                    if (!displayplacerProcess.WaitForExit(5000))
                    {
                        displayplacerProcess.Kill();
                    }
                    foreach (
                        Match match in Regex.Matches(
                            displayplacerOutput,
                            @"Resolution: (\d+)x(\d+)(.|\n)*?Origin: [(](\d+),(\d+)[)]"
                        )
                    )
                    {
                        if (
                            match.Groups.Count >= 6
                            && int.TryParse(match.Groups[1].Value, out int matchWidth)
                            && int.TryParse(match.Groups[2].Value, out int matchHeight)
                            && int.TryParse(match.Groups[4].Value, out int matchLeft)
                            && int.TryParse(match.Groups[5].Value, out int matchTop)
                        )
                        {
                            result.Add(new Monitor(matchLeft, matchTop, matchWidth, matchHeight));
                        }
                    }
                }
            }

            foreach (Monitor monitor in result)
            {
                MonitorTester tester = EditorWindow.CreateInstance<MonitorTester>();
                tester.position = new Rect(monitor.left, monitor.top, 20, 20);
                tester.monitor = monitor;
                tester.ShowPopup();
            }

            return result;
        }

        /// <summary>The delegate for Windows monitor enumeration callback.</summary>
        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RectApi pRect, IntPtr dwData);

        /// <summary>Windows API function to enumerate display monitors.</summary>
        [DllImport("user32")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lpRect, MonitorEnumProc callback, int dwData);

        /// <summary>
        /// Windows API rectangle structure for monitor bounds.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct RectApi
        {
            /// <summary>The left edge coordinate in pixels.</summary>
            public int left;

            /// <summary>The top edge coordinate in pixels.</summary>
            public int top;

            /// <summary>The right edge coordinate in pixels.</summary>
            public int right;

            /// <summary>The bottom edge coordinate in pixels.</summary>
            public int bottom;

            /// <summary>The width of the rectangle in pixels.</summary>
            public int Width
            {
                get { return right - left; }
            }

            /// <summary>The height of the rectangle in pixels.</summary>
            public int Height
            {
                get { return bottom - top; }
            }
        }

        /// <summary>
        /// Temporary editor window for detecting pixels per point on each monitor.
        /// </summary>
        private class MonitorTester : EditorWindow
        {
            /// <summary>The monitor to test.</summary>
            internal Monitor monitor;

            /// <summary>Records pixels per point and closes immediately.</summary>
            private void OnGUI()
            {
                monitor.pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
                Close();
            }
        }
    }
}
