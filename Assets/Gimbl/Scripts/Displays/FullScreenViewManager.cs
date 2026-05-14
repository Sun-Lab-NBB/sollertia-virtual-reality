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
    public class FullScreenViewManager
    {
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
            // Filters out the Unity-default Main Camera so a stale instance does not appear in the
            // dropdown if MainWindow.RemoveDefaultMainCamera has not run for the current scene yet.
            Camera[] sceneCameras = UnityEngine
                .Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                .Where(camera =>
                    !camera.CompareTag("MainCamera")
                    && !string.Equals(camera.gameObject.name, "Main Camera", System.StringComparison.Ordinal)
                )
                .ToArray();
            const string cameraTooltip =
                "Scene Camera that renders to this monitor when full-screen views are launched. "
                + "None leaves the monitor unused.";
            GUIContent[] cameraOptions = new GUIContent[sceneCameras.Length + 1];
            cameraOptions[0] = new GUIContent("None", cameraTooltip);
            for (int i = 0; i < sceneCameras.Length; i++)
            {
                cameraOptions[i + 1] = new GUIContent(sceneCameras[i].name, cameraTooltip);
            }

            for (int monitorIndex = 0; monitorIndex < monitors.Count; monitorIndex++)
            {
                Monitor monitor = monitors[monitorIndex];
                Camera oldCamera = (Camera)EditorUtility.EntityIdToObject(monitor.cameraEntityId);

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
            List<FullScreenView> existingViews = new List<FullScreenView>(FullScreenView.Views);
            foreach (FullScreenView view in existingViews)
            {
                if (closeOldViews)
                {
                    view.Close();
                }
            }

            foreach (Monitor monitor in monitors)
            {
                Camera camera = (Camera)EditorUtility.EntityIdToObject(monitor.cameraEntityId);
                if (camera != null)
                {
                    FullScreenView window = EditorWindow.CreateInstance<FullScreenView>();

                    float pixelsPerPointX = (monitor.left < 0) ? monitor.pixelsPerPoint : monitors[0].pixelsPerPoint;
                    int windowX = (int)(monitor.left / pixelsPerPointX);
                    float pixelsPerPointY = (monitor.top < 0) ? monitor.pixelsPerPoint : monitors[0].pixelsPerPoint;
                    int windowY = (int)(monitor.top / pixelsPerPointY);

                    int windowWidth = (int)(monitor.width / monitor.pixelsPerPoint);
                    int windowHeight = (int)(monitor.height / monitor.pixelsPerPoint);

                    window.position = new Rect(windowX, windowY, windowWidth, windowHeight);
                    window.cameraEntityId = camera.GetEntityId();

                    window.ShowPopup();
                }
            }
        }

        /// <summary>Saves camera assignments to the scene's asset file.</summary>
        public void SaveCameras()
        {
            _savedFullScreenViews.cameraNames.Clear();
            for (int monitorIndex = 0; monitorIndex < monitors.Count; monitorIndex++)
            {
                Camera camera = (Camera)EditorUtility.EntityIdToObject(monitors[monitorIndex].cameraEntityId);
                string path = (camera != null) ? PathName(camera.gameObject) : "";
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
