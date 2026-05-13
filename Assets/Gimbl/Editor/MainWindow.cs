/// <summary>
/// Provides the MainWindow class for Gimbl system configuration.
///
/// Renders the main editor window for MQTT settings, session configuration,
/// and setup import/export functionality.
/// </summary>
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gimbl
{
    /// <summary>
    /// Manages the main Gimbl configuration editor window.
    /// </summary>
    public class MainWindow : EditorWindow
    {
        /// <summary>The serialized property for output path.</summary>
        private SerializedProperty _outputPath;

        /// <summary>The serialized property for output file.</summary>
        private SerializedProperty _outputFile;

        /// <summary>The scroll position for the window content.</summary>
        private Vector2 _scrollPosition = Vector2.zero;

        /// <summary>The MQTT client reference for configuration.</summary>
        private MQTTClient _client;

        /// <summary>The session menu settings instance.</summary>
        [SerializeField]
        private SessionMenuSettings _sessionSettings = new SessionMenuSettings();

        /// <summary>Shows the Gimbl main window and related editor windows.</summary>
        /// <remarks>
        /// The Settings window is docked next to <c>UnityEditor.InspectorWindow</c>, which is resolved by
        /// assembly-qualified type name to avoid a hard reference to a private Unity type.
        /// </remarks>
        [MenuItem("Window/Gimbl")]
        public static void ShowWindow()
        {
            System.Type inspectorType = System.Type.GetType("UnityEditor.InspectorWindow,UnityEditor.dll");
            GetWindow<MainWindow>("Settings", true, new System.Type[] { inspectorType });
            ActorWindow.ShowWindow();
            DisplaysWindow.ShowWindow();
        }

        /// <summary>Initializes the scene when the window is enabled.</summary>
        private void OnEnable()
        {
            InitializeScene();
        }

        /// <summary>Renders the MQTT settings and setup import/export GUI.</summary>
        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(
                _scrollPosition,
                GUILayout.Height(position.height),
                GUILayout.Width(position.width)
            );

            if (EditorApplication.isPlaying)
            {
                GUI.enabled = false;
            }
            EditorGUILayout.BeginVertical(LayoutSettings.MainBoxStyle.Style);
            EditorGUILayout.LabelField("MQTT", LayoutSettings.SectionLabel);
            _client.ipAddress = EditorGUILayout.TextField("ip: ", _client.ipAddress, GUILayout.Width(300));

            string portText = EditorGUILayout.TextField("port: ", _client.port.ToString(), GUILayout.Width(300));
            if (int.TryParse(portText, out int parsedPort))
            {
                _client.port = parsedPort;
            }

            if (GUI.changed)
            {
                EditorPrefs.SetString("SollertiaVR_MQTT_IP", _client.ipAddress);
                EditorPrefs.SetInt("SollertiaVR_MQTT_Port", _client.port);
            }
            if (GUILayout.Button("Test Connection"))
            {
                _client.Connect(verbose: true);
                _client.Disconnect();
            }

            _sessionSettings.isFold = EditorGUILayout.Foldout(_sessionSettings.isFold, "External Control");
            if (_sessionSettings.isFold)
            {
                EditorGUILayout.BeginVertical(LayoutSettings.SubBoxStyle.Style);
                bool newExternalStart = EditorGUILayout.Toggle(
                    "External Start Trigger",
                    _sessionSettings.externalStart,
                    LayoutSettings.EditFieldOption
                );
                if (newExternalStart != _sessionSettings.externalStart)
                {
                    _sessionSettings.externalStart = newExternalStart;
                    EditorPrefs.SetBool("Gimbl_externalStart", newExternalStart);
                }
                bool newExternalLog = EditorGUILayout.Toggle(
                    "External Log Naming",
                    _sessionSettings.externalLog,
                    LayoutSettings.EditFieldOption
                );
                if (newExternalLog != _sessionSettings.externalLog)
                {
                    _sessionSettings.externalLog = newExternalLog;
                    EditorPrefs.SetBool("Gimbl_externalLog", newExternalLog);
                }
                EditorGUILayout.EndVertical();
            }

            GUI.enabled = true;
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(LayoutSettings.MainBoxStyle.Style);
            EditorGUILayout.LabelField("Setup", LayoutSettings.SectionLabel);
            if (GUILayout.Button("Export Setup"))
            {
                ExportSetup();
            }
            if (GUILayout.Button("Import Setup"))
            {
                ImportSetup();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndScrollView();
        }

        /// <summary>Ensures required GameObjects and folders exist in the scene.</summary>
        private void InitializeScene()
        {
            if (!AssetDatabase.IsValidFolder("Assets/VRSettings"))
            {
                AssetDatabase.CreateFolder("Assets", "VRSettings");
            }
            if (!AssetDatabase.IsValidFolder("Assets/VRSettings/Controllers"))
            {
                AssetDatabase.CreateFolder("Assets/VRSettings", "Controllers");
            }
            if (!AssetDatabase.IsValidFolder("Assets/VRSettings/Displays"))
            {
                AssetDatabase.CreateFolder("Assets/VRSettings", "Displays");
            }
            if (!AssetDatabase.IsValidFolder("Assets/VRSettings/Actors"))
            {
                AssetDatabase.CreateFolder("Assets/VRSettings", "Actors");
            }

            GameObject sceneObject;
            string[] defaultObjectNames = { "Actors", "Controllers", "MQTT Client" };
            foreach (string objectName in defaultObjectNames)
            {
                if (!GameObject.Find(objectName))
                {
                    Debug.Log($"Creating Object: {objectName}..");
                    sceneObject = new GameObject(objectName);
                    switch (objectName)
                    {
                        case "MQTT Client":
                            sceneObject.AddComponent<MQTTClient>();
                            break;
                        default:
                            break;
                    }
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                }
                else
                {
                    sceneObject = GameObject.Find(objectName);
                }
                switch (objectName)
                {
                    case "MQTT Client":
                        _client = sceneObject.GetComponent<MQTTClient>();
                        sceneObject.hideFlags = HideFlags.HideInHierarchy;
                        break;
                    case "Controllers":
                        sceneObject.hideFlags = HideFlags.None;
                        break;
                    default:
                        break;
                }
            }

            _client.ipAddress = EditorPrefs.GetString("SollertiaVR_MQTT_IP");
            if (string.IsNullOrEmpty(_client.ipAddress))
            {
                _client.ipAddress = "127.0.0.1";
            }
            _client.port = EditorPrefs.GetInt("SollertiaVR_MQTT_Port");
            if (_client.port == 0)
            {
                _client.port = 1883;
            }

            _sessionSettings.externalStart = EditorPrefs.GetBool("Gimbl_externalStart", false);
            _sessionSettings.externalLog = EditorPrefs.GetBool("Gimbl_externalLog", false);
        }

        /// <summary>Exports the current setup to a .gimblsetup package file.</summary>
        private void ExportSetup()
        {
            string[] pathParts = Application.dataPath.Split('/');
            string projectName = pathParts[pathParts.Length - 2];
            string exportFilePath = EditorUtility.SaveFilePanel("Save Setup as..", "", projectName, "gimblsetup");
            if (string.IsNullOrEmpty(exportFilePath))
                return;

            PrefabUtility.SaveAsPrefabAsset(GameObject.Find("Actors"), "Assets/tempActors.prefab");
            PrefabUtility.SaveAsPrefabAsset(GameObject.Find("Controllers"), "Assets/tempControllers.prefab");

            string[] assetBundle = new string[]
            {
                "Assets/tempActors.prefab",
                "Assets/tempControllers.prefab",
                "Assets/VRSettings/Displays/savedFullScreenViews.asset",
            };
            AssetDatabase.ExportPackage(assetBundle, exportFilePath, ExportPackageOptions.IncludeDependencies);

            AssetDatabase.DeleteAsset("Assets/tempActors.prefab");
            AssetDatabase.DeleteAsset("Assets/tempControllers.prefab");
        }

        /// <summary>Imports a setup from a .gimblsetup package file.</summary>
        private void ImportSetup()
        {
            bool confirmImport = EditorUtility.DisplayDialog(
                "Erase current setup?",
                "Importing this setup will remove all current Actors,Controllers and Displays",
                "Continue",
                "Cancel"
            );
            if (!confirmImport)
                return;

            string importFilePath = EditorUtility.OpenFilePanel("Import Setup", Application.dataPath, "gimblsetup");
            if (string.IsNullOrEmpty(importFilePath))
                return;

            DestroyImmediate(GameObject.Find("Actors"));
            DestroyImmediate(GameObject.Find("Controllers"));

            AssetDatabase.ImportPackage(importFilePath, interactive: false);

            Object actorsPrefab = AssetDatabase.LoadAssetAtPath("Assets/tempActors.prefab", typeof(Object));
            GameObject actors = Instantiate(actorsPrefab) as GameObject;
            actors.name = "Actors";

            Object controllersPrefab = AssetDatabase.LoadAssetAtPath("Assets/tempControllers.prefab", typeof(Object));
            GameObject controllers = Instantiate(controllersPrefab) as GameObject;
            controllers.name = "Controllers";

            DisplaysWindow displaysWindow = (DisplaysWindow)GetWindow(typeof(DisplaysWindow));
            displaysWindow.fullScreenManager.LoadCameras();

            foreach (ActorObject actor in actors.GetComponentsInChildren<ActorObject>())
            {
                if (LayerMask.NameToLayer(actor.name) == -1)
                {
                    TagsAndLayers.AddLayer(actor.name);
                }
                GameObject model = actor.GetComponentInChildren<MeshRenderer>().gameObject;
                model.layer = LayerMask.NameToLayer(actor.name);

                DisplayObject actorDisplay = actor.gameObject.GetComponentInChildren<DisplayObject>();
                if (actorDisplay != null)
                {
                    foreach (Camera camera in actorDisplay.GetComponentsInChildren<Camera>())
                    {
                        camera.cullingMask = -1;
                        camera.cullingMask &= ~(1 << LayerMask.NameToLayer(actor.name));
                    }
                }
            }

            AssetDatabase.DeleteAsset("Assets/tempActors.prefab");
            AssetDatabase.DeleteAsset("Assets/tempControllers.prefab");
        }

        /// <summary>
        /// Stores session menu state and external control settings.
        /// </summary>
        [System.Serializable]
        private class SessionMenuSettings
        {
            /// <summary>Determines whether the external control foldout is expanded.</summary>
            public bool isFold = false;

            /// <summary>Determines whether external start trigger is enabled.</summary>
            public bool externalStart = false;

            /// <summary>Determines whether external log naming is enabled.</summary>
            public bool externalLog = false;
        }
    }
}
