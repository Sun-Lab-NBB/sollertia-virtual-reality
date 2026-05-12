/// <summary>
/// Provides the FullScreenViewManager class for multi-monitor VR display management.
///
/// Manages camera-to-monitor assignments and borderless full-screen game views that run
/// with the Unity editor active, enabling VR studies that use sets of adjacent monitors
/// to display the world. Camera assignments are persisted in per-scene asset files.
/// </summary>
using System.Collections.Generic;
using System.IO;
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
            monitors = Monitor.EnumeratedMonitors();
            LoadCameras();
        }

        /// <summary>Renders a button to refresh monitor positions.</summary>
        public void OnGUIRefreshMonitorPositions()
        {
            if (GUILayout.Button("Refresh Monitor Positions"))
            {
                List<Monitor> refreshedMonitors = Monitor.EnumeratedMonitors();
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

        /// <summary>Renders camera assignment fields for each monitor.</summary>
        public void OnGUICameraObjectFields()
        {
            for (int monitorIndex = 0; monitorIndex < monitors.Count; monitorIndex++)
            {
                Monitor monitor = monitors[monitorIndex];
                EditorGUILayout.LabelField($"Monitor {monitorIndex + 1} at ({monitor.left}, {monitor.top})");
                Camera oldCamera = (Camera)EditorUtility.EntityIdToObject(monitor.cameraEntityId);
                Camera newCamera = (Camera)EditorGUILayout.ObjectField("Camera", oldCamera, typeof(Camera), true);
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
            if (GUILayout.Button("Show Full-Screen Views"))
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
        private string PathName(GameObject gameObject)
        {
            string path = gameObject.name;
            while (gameObject.transform.parent != null)
            {
                gameObject = gameObject.transform.parent.gameObject;
                path = $"{gameObject.name}/{path}";
            }
            return path;
        }
    }
}
