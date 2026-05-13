/// <summary>
/// Provides the DisplaysWindow class for VR display management in the editor.
///
/// Renders the editor window for creating, editing, and managing VR displays,
/// and handles camera-to-monitor mapping for full-screen views.
/// </summary>
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gimbl
{
    /// <summary>
    /// Manages the editor window for display configuration and camera mapping.
    /// </summary>
    public class DisplaysWindow : EditorWindow
    {
        /// <summary>The scroll position for the window content.</summary>
        private Vector2 _scrollPosition = Vector2.zero;

        /// <summary>Determines whether a scene change is pending after exiting play mode.</summary>
        private bool _exitPlayModeSceneChangeComing = false;

        /// <summary>The available display model names from Resources.</summary>
        private string[] _displayModels;

        /// <summary>The index of the selected model in the dropdown.</summary>
        private int _selectedModel = 0;

        /// <summary>The selected display type for creation.</summary>
        private DisplayType _displayType = DisplayType.Monitor;

        /// <summary>The serialized object for property editing.</summary>
        private SerializedObject _serializedObject;

        /// <summary>The menu settings for display management.</summary>
        [SerializeField]
        private DisplayMenu _displaySettings = new DisplayMenu() { typeName = "Display" };

        /// <summary>The full-screen view manager for camera mapping.</summary>
        public FullScreenViewManager fullScreenManager;

        /// <summary>The current editor window instance.</summary>
        private static EditorWindow _currentWindow;

        /// <summary>The delegate type for display creation functions.</summary>
        /// <typeparam name="T">The Unity Object type to create.</typeparam>
        /// <param name="settings">The menu settings for the creation.</param>
        private delegate void CreateFunc<T>(MenuSettings<T> settings)
            where T : UnityEngine.Object;

        /// <summary>Shows the DisplaysWindow editor window.</summary>
        public static void ShowWindow()
        {
            if (_currentWindow == null)
            {
                _currentWindow = GetWindow<DisplaysWindow>("Displays", true, typeof(MainWindow));
            }
        }

        /// <summary>Initializes display models and scene change handlers when enabled.</summary>
        private void OnEnable()
        {
            TagsAndLayers.AddTag("VRDisplay");
            UnityEngine.Object[] data = Resources.LoadAll<GameObject>("Displays");
            _displayModels = data.Select(model => model.name).ToArray();
            fullScreenManager = new FullScreenViewManager();

            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>Removes scene change handlers when disabled.</summary>
        private void OnDisable()
        {
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        /// <summary>Reloads camera assignments when the active scene changes.</summary>
        /// <param name="oldScene">The previous active scene.</param>
        /// <param name="newScene">The new active scene.</param>
        private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
        {
            if (_exitPlayModeSceneChangeComing)
            {
                _exitPlayModeSceneChangeComing = false;
            }
            else
            {
                fullScreenManager.LoadCameras();
            }
        }

        /// <summary>Handles play mode transitions for full-screen view management.</summary>
        /// <param name="state">The play mode state change.</param>
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                fullScreenManager.ShowFullScreenViews(closeOldViews: false);
            }

            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                _exitPlayModeSceneChangeComing = true;
            }
        }

        /// <summary>Renders the display management and camera mapping GUI.</summary>
        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(
                _scrollPosition,
                GUILayout.Height(position.height),
                GUILayout.Width(position.width)
            );

            EditorGUILayout.BeginVertical(LayoutSettings.MainBoxStyle.Style);
            EditorGUILayout.LabelField("Displays", LayoutSettings.SectionLabel);

            EditorGUILayout.BeginHorizontal();
            SelectMenu(_displaySettings);
            if (GUILayout.Button("Delete", LayoutSettings.ButtonOption))
            {
                DeleteDisplay();
            }
            EditorGUILayout.EndHorizontal();

            if (_displaySettings.SelectedObject != null)
            {
                EditorGUILayout.BeginHorizontal();
                if (_displaySettings.SelectedObject.currentBrightness > 0)
                {
                    if (GUILayout.Button("Blank Display"))
                    {
                        _displaySettings.SelectedObject.currentBrightness = 0;
                    }
                }
                else
                {
                    if (GUILayout.Button("Show Display"))
                    {
                        _displaySettings.SelectedObject.currentBrightness = _displaySettings
                            .SelectedObject
                            .settings
                            .brightness;
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            _displaySettings.show[0] = EditorGUILayout.Foldout(_displaySettings.show[0], "Edit");
            if (_displaySettings.show[0])
            {
                if (_displaySettings.SelectedObject != null)
                {
                    EditorGUILayout.BeginVertical(LayoutSettings.SubBoxStyle.Style);
                    _serializedObject = new SerializedObject(_displaySettings.SelectedObject.settings);
                    float prevHeight = _displaySettings.SelectedObject.settings.heightInVR;
                    float prevBrightness = _displaySettings.SelectedObject.settings.brightness;
                    EditorGUILayout.PropertyField(
                        _serializedObject.FindProperty("isActive"),
                        includeChildren: true,
                        LayoutSettings.EditFieldOption
                    );
                    EditorGUILayout.PropertyField(
                        _serializedObject.FindProperty("brightness"),
                        includeChildren: true,
                        LayoutSettings.EditFieldOption
                    );
                    EditorGUILayout.PropertyField(
                        _serializedObject.FindProperty("heightInVR"),
                        includeChildren: true,
                        LayoutSettings.EditFieldOption
                    );
                    _serializedObject.ApplyModifiedProperties();
                    if (prevHeight != _displaySettings.SelectedObject.settings.heightInVR)
                    {
                        _displaySettings.SelectedObject.transform.localPosition = new Vector3(
                            0,
                            _displaySettings.SelectedObject.settings.heightInVR,
                            0
                        );
                    }
                    if (prevBrightness != _displaySettings.SelectedObject.settings.brightness)
                    {
                        _displaySettings.SelectedObject.currentBrightness = _displaySettings
                            .SelectedObject
                            .settings
                            .brightness;
                    }
                    EditorGUILayout.EndVertical();
                }
            }

            if (EditorApplication.isPlaying)
            {
                GUI.enabled = false;
            }
            _displaySettings.show[1] = EditorGUILayout.Foldout(_displaySettings.show[1], "Create");
            if (_displaySettings.show[1])
            {
                EditorGUILayout.BeginVertical(LayoutSettings.SubBoxStyle.Style);
                EditorGUILayout.LabelField("Create Display", EditorStyles.boldLabel);
                _displaySettings.name = EditorGUILayout.TextField(
                    "Display Name: ",
                    _displaySettings.name,
                    LayoutSettings.EditFieldOption
                );
                _selectedModel = EditorGUILayout.Popup(
                    "Model: ",
                    _selectedModel,
                    _displayModels,
                    LayoutSettings.EditFieldOption
                );
                _displayType = (DisplayType)
                    EditorGUILayout.EnumPopup("Type: ", _displayType, LayoutSettings.EditFieldOption);
                CreateButton(_displaySettings, new CreateFunc<DisplayObject>(CreateDisplay));
                EditorGUILayout.EndVertical();
            }
            GUI.enabled = true;
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(LayoutSettings.MainBoxStyle.Style);
            EditorGUILayout.LabelField("Camera Mapping", LayoutSettings.SectionLabel);

            fullScreenManager.OnGUIRefreshMonitorPositions();
            fullScreenManager.OnGUICameraObjectFields();
            if (EditorApplication.isPlaying)
            {
                GUI.enabled = false;
            }
            fullScreenManager.OnGUIShowFullScreenViews();
            GUI.enabled = true;
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndScrollView();
        }

        /// <summary>Renders the object selection field.</summary>
        /// <typeparam name="T">The type of Unity Object to select.</typeparam>
        /// <param name="settings">The menu settings containing the selection state.</param>
        private void SelectMenu<T>(MenuSettings<T> settings)
            where T : UnityEngine.Object
        {
            T existingObject = FindAnyObjectByType<T>();
            if (settings.SelectedObject == null && existingObject != null)
            {
                settings.SelectedObject = existingObject;
            }
            settings.SelectedObject = (T)
                EditorGUILayout.ObjectField(settings.SelectedObject, typeof(T), allowSceneObjects: true);
        }

        /// <summary>Renders the create button with validation for duplicate and empty names.</summary>
        /// <typeparam name="T">The type of Unity Object to create.</typeparam>
        /// <param name="settings">The menu settings containing the new object name.</param>
        /// <param name="createFunction">The function to call when creating the object.</param>
        private void CreateButton<T>(MenuSettings<T> settings, CreateFunc<T> createFunction)
            where T : UnityEngine.Object
        {
            EditorGUILayout.BeginHorizontal();
            T[] existingObjects = FindObjectsByType<T>(FindObjectsSortMode.None);
            string[] existingNames = existingObjects.Select(existingObject => existingObject.name).ToArray();
            string validationMessage = "";
            if (ArrayUtility.Contains(existingNames, settings.name))
            {
                validationMessage = "Duplicate name";
                GUI.enabled = false;
            }
            if (string.IsNullOrEmpty(settings.name))
            {
                validationMessage = "Empty Name";
                GUI.enabled = false;
            }
            EditorGUILayout.LabelField(validationMessage, GUILayout.Width(197));
            if (GUILayout.Button("Create", LayoutSettings.ButtonOption))
            {
                createFunction(settings);
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>Deletes the currently selected display after confirmation.</summary>
        private void DeleteDisplay()
        {
            GameObject displayObject = _displaySettings.SelectedObject.gameObject;
            bool confirmDelete = EditorUtility.DisplayDialog(
                $"Remove Display {displayObject.name}?",
                $"Are you sure you want to delete Display {displayObject.name}?",
                "Delete",
                "Cancel"
            );
            if (confirmDelete)
            {
                Undo.DestroyObjectImmediate(displayObject);
            }
        }

        /// <summary>Creates a new display with the specified settings.</summary>
        /// <typeparam name="T">The type of component for the menu settings.</typeparam>
        /// <param name="settings">The menu settings containing the display name.</param>
        private void CreateDisplay<T>(MenuSettings<T> settings)
            where T : DisplayObject
        {
            UnityEngine.Object modelPrefab = Resources.Load($"Displays/{_displayModels[_selectedModel]}");
            GameObject displayObject = Instantiate(modelPrefab) as GameObject;
            displayObject.name = settings.name;
            DisplayObject display = displayObject.AddComponent<DisplayObject>();
            displayObject.tag = "VRDisplay";

            DisplaySettings displaySettings = CreateInstance<DisplaySettings>();
            string assetPath = Path.Combine("Assets", "VRSettings", "Displays", $"{displayObject.name}.asset");
            AssetDatabase.CreateAsset(displaySettings, assetPath);
            display.settings = displaySettings;

            switch (_displayType)
            {
                case DisplayType.Monitor:
                    MeshRenderer[] meshRenderers = displayObject.GetComponentsInChildren<MeshRenderer>();
                    foreach (MeshRenderer mesh in meshRenderers)
                    {
                        mesh.GetComponent<MeshCollider>().enabled = false;
                        GameObject cameraObject = new GameObject($"Camera: {mesh.name}");
                        cameraObject.transform.SetParent(mesh.transform.parent);
                        cameraObject.transform.localPosition = new Vector3(0, 0, 0);
                        Camera cameraComponent = cameraObject.AddComponent<Camera>();
                        cameraComponent.nearClipPlane = 0.3f;
                        cameraComponent.targetDisplay = 8;
                        cameraComponent.clearFlags = CameraClearFlags.Skybox;
                        cameraComponent.backgroundColor = Color.black;
                        PerspectiveProjection projection = cameraObject.AddComponent<PerspectiveProjection>();
                        projection.projectionScreen = mesh.gameObject;
                        projection.setNearClipPlane = false;
                        mesh.enabled = false;
                    }
                    break;
                default:
                    break;
            }
            Undo.RegisterCreatedObjectUndo(displayObject, "Create Display");
            settings.SelectedObject = display as T;
            settings.name = "";
        }

        /// <summary>
        /// Stores serializable menu settings for DisplayObject selection.
        /// </summary>
        [System.Serializable]
        public class DisplayMenu : MenuSettings<DisplayObject> { }
    }
}
