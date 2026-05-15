/// <summary>
/// Provides the FullScreenViewManager class for multi-monitor VR display management.
///
/// Manages camera-to-monitor assignments and borderless full-screen game views that run
/// with the Unity editor active, enabling VR studies that use sets of adjacent monitors
/// to display the world. Camera assignments are persisted in per-scene asset files.
/// </summary>
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Gimbl
{
    /// <summary>
    /// Manages camera-to-monitor assignments and full-screen view creation.
    /// </summary>
    /// <remarks>
    /// The constructor unconditionally calls <see cref="LoadCameras"/>, which guarantees
    /// <see cref="_savedFullScreenViews"/> is non-null (loaded or newly created) before any other method
    /// runs. <see cref="SaveCameras"/> relies on this invariant; do not call it on an instance that has
    /// bypassed the constructor.
    /// </remarks>
    public class FullScreenViewManager
    {
        /// <summary>The tooltip shown on every camera dropdown entry.</summary>
        private const string CameraOptionTooltip =
            "Scene Camera that renders to this monitor when full-screen views are launched. "
            + "None leaves the monitor unused.";

        /// <summary>The list of detected monitors in the system.</summary>
        public List<Monitor> monitors;

        /// <summary>The saved camera assignments for the current scene.</summary>
        private FullScreenViewsSaved _savedFullScreenViews;

        /// <summary>Initializes the manager by detecting monitors and loading camera assignments.</summary>
        public FullScreenViewManager()
        {
            monitors = Monitor.EnumerateMonitors();
            LoadCameras();
        }

        /// <summary>Renders a button to refresh monitor positions.</summary>
        public void OnGUIRefreshMonitorPositions()
        {
            if (
                GUILayout.Button(
                    new GUIContent(
                        "Refresh Monitor Positions",
                        "Re-detect monitors from the OS, preserving existing camera assignments."
                    )
                )
            )
            {
                List<Monitor> refreshedMonitors = Monitor.EnumerateMonitors();
                for (int i = 0; i < refreshedMonitors.Count; i++)
                {
                    if (i < monitors.Count)
                    {
                        refreshedMonitors[i].cameraEntityId = monitors[i].cameraEntityId;
                    }
                }
                monitors = refreshedMonitors;
            }
        }

        /// <summary>Renders a per-monitor row pairing the monitor coordinates with a camera dropdown.</summary>
        /// <remarks>
        /// Enumerates every <see cref="Camera"/> in the active scene each frame so the dropdown reflects
        /// the current scene state without manual refresh. Selections that would alias another monitor's
        /// camera are silently ignored to preserve the existing one-camera-per-monitor invariant.
        /// </remarks>
        public void OnGUICameraObjectFields()
        {
            Camera[] sceneCameras = EnumerateAssignableCameras();
            GUIContent[] cameraOptions = BuildCameraOptions(sceneCameras);

            for (int monitorIndex = 0; monitorIndex < monitors.Count; monitorIndex++)
            {
                RenderMonitorRow(monitorIndex, sceneCameras, cameraOptions);
            }
        }

        /// <summary>Renders a button to show full-screen views.</summary>
        public void OnGUIShowFullScreenViews()
        {
            if (
                GUILayout.Button(
                    new GUIContent(
                        "Show Full-Screen Views",
                        "Open a borderless full-screen window per assigned monitor showing the chosen camera. "
                            + "Stop Play Mode or close the windows to return to the editor. Only has effect "
                            + "when the VR scene is playing."
                    )
                )
            )
            {
                ShowFullScreenViews(closeOldViews: true);
            }
        }

        /// <summary>Creates full-screen views for all monitors with assigned cameras.</summary>
        /// <param name="closeOldViews">
        /// Determines whether to close existing full-screen views before creating new ones.
        /// </param>
        public void ShowFullScreenViews(bool closeOldViews)
        {
            if (closeOldViews)
            {
                List<FullScreenView> existingViews = new List<FullScreenView>(FullScreenView.Views);
                foreach (FullScreenView view in existingViews)
                {
                    view.Close();
                }
            }

            foreach (Monitor monitor in monitors)
            {
                Camera camera = GetCameraFor(monitor);
                if (camera == null)
                {
                    continue;
                }

                FullScreenView window = EditorWindow.CreateInstance<FullScreenView>();
                window.position = ComputeWindowRect(monitor);
                window.cameraEntityId = camera.GetEntityId();
                window.ShowPopup();
            }
        }

        /// <summary>Saves camera assignments to the scene's asset file.</summary>
        public void SaveCameras()
        {
            _savedFullScreenViews.cameraNames.Clear();
            for (int monitorIndex = 0; monitorIndex < monitors.Count; monitorIndex++)
            {
                Camera camera = GetCameraFor(monitors[monitorIndex]);
                string path = camera != null ? PathName(camera.gameObject) : string.Empty;
                _savedFullScreenViews.cameraNames.Add(path);
            }
            AssetDatabase.Refresh();
            EditorUtility.SetDirty(_savedFullScreenViews);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Loads camera assignments from the scene's asset file.</summary>
        public void LoadCameras()
        {
            string savedViewsPath = Path.Combine(
                "Assets",
                "VRSettings",
                "Displays",
                $"{EditorSceneManager.GetActiveScene().name}-savedFullScreenViews.asset"
            );
            _savedFullScreenViews = (FullScreenViewsSaved)
                AssetDatabase.LoadAssetAtPath(savedViewsPath, typeof(FullScreenViewsSaved));

            if (_savedFullScreenViews != null)
            {
                for (int savedIndex = 0; savedIndex < _savedFullScreenViews.cameraNames.Count; savedIndex++)
                {
                    if (savedIndex < monitors.Count)
                    {
                        string cameraPath = _savedFullScreenViews.cameraNames[savedIndex];
                        GameObject cameraObject = GameObject.Find(cameraPath);
                        if (cameraObject != null)
                        {
                            Camera camera = cameraObject.GetComponent<Camera>();
                            if (camera != null)
                            {
                                monitors[savedIndex].cameraEntityId = camera.GetEntityId();
                            }
                        }
                    }
                }
            }
            else
            {
                _savedFullScreenViews = ScriptableObject.CreateInstance<FullScreenViewsSaved>();
                AssetDatabase.CreateAsset(_savedFullScreenViews, savedViewsPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        /// <summary>Returns every Camera in the active scene that is not the Unity-default Main Camera.</summary>
        /// <remarks>
        /// Filters by tag and name so a stale Main Camera left over from a fresh scene template does not
        /// appear in the dropdown when <see cref="MainWindow"/>'s removal pass has not yet run.
        /// </remarks>
        private static Camera[] EnumerateAssignableCameras()
        {
            return UnityEngine
                .Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                .Where(camera =>
                    !camera.CompareTag("MainCamera")
                    && !string.Equals(camera.gameObject.name, "Main Camera", System.StringComparison.Ordinal)
                )
                .ToArray();
        }

        /// <summary>Builds the dropdown <see cref="GUIContent"/> array for the supplied scene cameras.</summary>
        /// <param name="cameras">The cameras to include after the leading "None" option.</param>
        /// <returns>An array sized <c>cameras.Length + 1</c> with "None" at index 0.</returns>
        private static GUIContent[] BuildCameraOptions(Camera[] cameras)
        {
            GUIContent[] options = new GUIContent[cameras.Length + 1];
            options[0] = new GUIContent("None", CameraOptionTooltip);
            for (int i = 0; i < cameras.Length; i++)
            {
                options[i + 1] = new GUIContent(cameras[i].name, CameraOptionTooltip);
            }
            return options;
        }

        /// <summary>Renders the dropdown row for a single monitor and applies the user's selection.</summary>
        /// <param name="monitorIndex">The index of the monitor being rendered.</param>
        /// <param name="sceneCameras">The cameras presented in the dropdown.</param>
        /// <param name="cameraOptions">The pre-built <see cref="GUIContent"/> dropdown entries.</param>
        private void RenderMonitorRow(int monitorIndex, Camera[] sceneCameras, GUIContent[] cameraOptions)
        {
            Monitor monitor = monitors[monitorIndex];
            Camera oldCamera = GetCameraFor(monitor);

            int currentIndex = 0;
            for (int i = 0; i < sceneCameras.Length; i++)
            {
                if (sceneCameras[i] == oldCamera)
                {
                    currentIndex = i + 1;
                    break;
                }
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                new GUIContent(
                    $"Monitor {monitorIndex + 1} ({monitor.left}, {monitor.top})",
                    "OS-reported monitor index and its top-left origin in pixel coordinates."
                ),
                GUILayout.Width(170)
            );
            int newIndex = EditorGUILayout.Popup(currentIndex, cameraOptions);
            EditorGUILayout.EndHorizontal();

            Camera newCamera = newIndex == 0 ? null : sceneCameras[newIndex - 1];

            if (newCamera != null)
            {
                EntityId entityId = newCamera.GetEntityId();
                bool alreadyUsed = false;
                foreach (Monitor otherMonitor in monitors)
                {
                    if (otherMonitor.cameraEntityId == entityId)
                    {
                        alreadyUsed = true;
                        break;
                    }
                }
                if (!alreadyUsed)
                {
                    monitor.cameraEntityId = entityId;
                }
            }
            else
            {
                monitor.cameraEntityId = EntityId.None;
            }
            if (newCamera != oldCamera)
            {
                SaveCameras();
            }
        }

        /// <summary>Resolves the camera currently bound to the supplied monitor, or null when unbound.</summary>
        /// <param name="monitor">The monitor whose camera assignment is being read.</param>
        /// <returns>The resolved camera, or null when the assignment is empty or the camera was destroyed.</returns>
        private static Camera GetCameraFor(Monitor monitor)
        {
            return (Camera)EditorUtility.EntityIdToObject(monitor.cameraEntityId);
        }

        /// <summary>Computes the borderless-window rect for the supplied monitor, adjusted for DPI scale.</summary>
        /// <param name="monitor">The monitor whose window position and size are being computed.</param>
        /// <returns>The window rect in editor-space pixels.</returns>
        private Rect ComputeWindowRect(Monitor monitor)
        {
            float pixelsPerPointX = monitor.left < 0 ? monitor.pixelsPerPoint : monitors[0].pixelsPerPoint;
            float pixelsPerPointY = monitor.top < 0 ? monitor.pixelsPerPoint : monitors[0].pixelsPerPoint;
            int windowX = (int)(monitor.left / pixelsPerPointX);
            int windowY = (int)(monitor.top / pixelsPerPointY);
            int windowWidth = (int)(monitor.width / monitor.pixelsPerPoint);
            int windowHeight = (int)(monitor.height / monitor.pixelsPerPoint);
            return new Rect(windowX, windowY, windowWidth, windowHeight);
        }

        /// <summary>Returns the full hierarchy path name for a GameObject.</summary>
        /// <param name="gameObject">The GameObject to get the path for.</param>
        /// <returns>The full hierarchy path from root to the GameObject.</returns>
        private static string PathName(GameObject gameObject)
        {
            GameObject walker = gameObject;
            string path = walker.name;
            while (walker.transform.parent != null)
            {
                walker = walker.transform.parent.gameObject;
                path = $"{walker.name}/{path}";
            }
            return path;
        }
    }
}
#endif
