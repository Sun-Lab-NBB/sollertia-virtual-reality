/// <summary>
/// Provides the MainWindow class for the consolidated Task Parameters editor window.
///
/// Renders the single editor window that hosts every per-scene configuration surface for Gimbl:
/// Task, Actor, Display, Camera Mapping, and MQTT. Replaces the previous Settings / Actors /
/// Displays three-window layout with one aggregated window.
/// </summary>
using System.Linq;
using SL.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gimbl
{
    /// <summary>
    /// Manages the consolidated Gimbl Task Parameters editor window.
    /// </summary>
    public class MainWindow : EditorWindow
    {
        /// <summary>The scroll position for the window content.</summary>
        private Vector2 _scrollPosition = Vector2.zero;

        /// <summary>The MQTT client reference for configuration.</summary>
        private MQTTClient _client;

        /// <summary>Determines whether a scene change is pending after exiting play mode.</summary>
        private bool _exitPlayModeSceneChangeComing = false;

        /// <summary>The full-screen view manager for camera mapping.</summary>
        public FullScreenViewManager fullScreenManager;

        /// <summary>Shows the Task Parameters editor window.</summary>
        /// <remarks>
        /// The Window menu entry uses the full "Task Parameters" name while the docked tab uses the
        /// shorter "Parameters" label to avoid redundancy. The window is docked next to
        /// <c>UnityEditor.InspectorWindow</c>, resolved by assembly-qualified type name to avoid a hard
        /// reference to a private Unity type.
        /// </remarks>
        [MenuItem("Window/Task Parameters")]
        public static void ShowWindow()
        {
            OpenOrFocusWindow(focus: true);
        }

        /// <summary>Registers auto-open hooks that keep the Parameters window available across sessions.</summary>
        /// <remarks>
        /// Subscribes to scene-open and Play-Mode-enter events so closing the window does not strand the
        /// user without access to the per-scene configuration surface. Also defers a one-shot open via
        /// <see cref="EditorApplication.delayCall"/> so the window appears immediately after a script
        /// reload or editor start, once the editor finishes initializing.
        /// </remarks>
        [InitializeOnLoadMethod]
        private static void RegisterAutoOpen()
        {
            EditorSceneManager.sceneOpened += (Scene scene, OpenSceneMode mode) => EnsureWindowOpen();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    EnsureWindowOpen();
                }
            };
            EditorApplication.delayCall += EnsureWindowOpen;
        }

        /// <summary>Opens the Parameters window without stealing focus when no instance is currently open.</summary>
        private static void EnsureWindowOpen()
        {
            if (HasOpenInstances<MainWindow>())
            {
                return;
            }
            OpenOrFocusWindow(focus: false);
        }

        /// <summary>Creates or surfaces the Parameters window and pins its title to the short label.</summary>
        /// <param name="focus">Determines whether the window should take input focus on open.</param>
        private static void OpenOrFocusWindow(bool focus)
        {
            System.Type inspectorType = System.Type.GetType("UnityEditor.InspectorWindow,UnityEditor.dll");
            MainWindow window = GetWindow<MainWindow>("Parameters", focus, new System.Type[] { inspectorType });
            window.titleContent = new GUIContent("Parameters");
        }

        /// <summary>Initializes the scene, full-screen view manager, and scene change handlers.</summary>
        private void OnEnable()
        {
            TagsAndLayers.AddTag("VRDisplay");
            fullScreenManager = new FullScreenViewManager();
            InitializeScene();

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

        /// <summary>Renders every configuration section in order.</summary>
        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(
                _scrollPosition,
                GUILayout.Height(position.height),
                GUILayout.Width(position.width)
            );

            DrawActorSection();
            DrawMQTTSection();
            DrawDisplaySection();
            DrawCameraMappingSection();
            DrawTaskSection();

            EditorGUILayout.EndScrollView();
        }

        /// <summary>Renders the Task section that exposes per-scene Task settings.</summary>
        /// <remarks>
        /// Locates the Task component via <see cref="UnityEngine.Object.FindAnyObjectByType{T}()"/> each frame
        /// because Task references move with scene changes. Disables the controls in Play Mode since the live
        /// guidance toggles are driven by MQTT during runtime. Hides <c>Require Lick</c> and <c>Require Wait</c>
        /// when the active scene lacks a corresponding <see cref="GuidanceZone"/> or <see cref="OccupancyZone"/>
        /// to keep the section focused on the toggles actually consumed by the current task.
        /// </remarks>
        private void DrawTaskSection()
        {
            EditorGUILayout.BeginVertical(LayoutSettings.MainBoxStyle.Style);
            EditorGUILayout.LabelField("Task", LayoutSettings.SectionLabel);

            Task task = FindAnyObjectByType<Task>();
            if (task == null)
            {
                EditorGUILayout.HelpBox("No Task component found in the current scene.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            if (task.actor == null)
            {
                ActorObject resolvedActor = FindAnyObjectByType<ActorObject>();
                if (resolvedActor != null)
                {
                    task.actor = resolvedActor;
                    EditorUtility.SetDirty(task);
                }
            }

            if (EditorApplication.isPlaying)
            {
                GUI.enabled = false;
            }

            bool hasLickZone = FindAnyObjectByType<GuidanceZone>() != null;
            bool hasOccupancyZone = FindAnyObjectByType<OccupancyZone>() != null;

            EditorGUI.BeginChangeCheck();
            bool newRequireLick = task.requireLick;
            if (hasLickZone)
            {
                newRequireLick = EditorGUILayout.Toggle(
                    new GUIContent(
                        "Require Lick: ",
                        "When on, the animal must lick inside the stimulus zone to trigger the reward. "
                            + "When off, reaching the guidance zone automatically triggers the reward. "
                            + "Addressable via MQTT at runtime."
                    ),
                    task.requireLick,
                    LayoutSettings.EditFieldOption
                );
            }
            bool newRequireWait = task.requireWait;
            if (hasOccupancyZone)
            {
                newRequireWait = EditorGUILayout.Toggle(
                    new GUIContent(
                        "Require Wait: ",
                        "When on, the animal must remain in the occupancy zone to disarm the stimulus trigger. "
                            + "When off, the VR emits a warning to the experiment controller via MQTT, "
                            + "enabling it to interfere by activating brakes. Addressable via MQTT at runtime."
                    ),
                    task.requireWait,
                    LayoutSettings.EditFieldOption
                );
            }
            float newTrackLength = EditorGUILayout.FloatField(
                new GUIContent(
                    "Track Length: ",
                    "Total length of the pre-generated random trial sequence in Unity units. "
                        + "Should overestimate the distance the animal will actually travel in a session."
                ),
                task.trackLength,
                LayoutSettings.EditFieldOption
            );
            int newTrackSeed = EditorGUILayout.IntField(
                new GUIContent(
                    "Track Seed: ",
                    "Seed for the random trial-sequence generator. A specific seed reproduces the same "
                        + "sequence; use -1 for a nondeterministic seed each run."
                ),
                task.trackSeed,
                LayoutSettings.EditFieldOption
            );

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(task, "Edit Task Settings");
                task.requireLick = newRequireLick;
                task.requireWait = newRequireWait;
                task.trackLength = newTrackLength;
                task.trackSeed = newTrackSeed;
                EditorUtility.SetDirty(task);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            GUI.enabled = true;
            EditorGUILayout.EndVertical();
        }

        /// <summary>Renders the Actor section that exposes the active scene's Actor properties.</summary>
        private void DrawActorSection()
        {
            EditorGUILayout.BeginVertical(LayoutSettings.MainBoxStyle.Style);
            EditorGUILayout.LabelField("Actor", LayoutSettings.SectionLabel);

            ActorObject actor = FindAnyObjectByType<ActorObject>();
            if (actor == null)
            {
                EditorGUILayout.HelpBox(
                    "No Actor in the active scene. Close and reopen this window to auto-create one.",
                    MessageType.Info
                );
            }
            else
            {
                actor.EditMenu();
            }
            EditorGUILayout.EndVertical();
        }

        /// <summary>Renders the Display section for brightness and height of the active scene's Display.</summary>
        private void DrawDisplaySection()
        {
            EditorGUILayout.BeginVertical(LayoutSettings.MainBoxStyle.Style);
            EditorGUILayout.LabelField("Display", LayoutSettings.SectionLabel);

            DisplayObject display = FindAnyObjectByType<DisplayObject>();
            if (display == null)
            {
                EditorGUILayout.HelpBox(
                    "No Display in the active scene. Close and reopen this window to auto-create one.",
                    MessageType.Info
                );
                EditorGUILayout.EndVertical();
                return;
            }

            GUIContent blankShowTooltip = new GUIContent(
                "",
                "Set brightness to 0 (Blank) or restore the configured brightness (Show)."
            );
            EditorGUILayout.BeginHorizontal();
            if (display.currentBrightness > 0)
            {
                blankShowTooltip.text = "Blank Display";
                if (GUILayout.Button(blankShowTooltip))
                {
                    display.currentBrightness = 0;
                }
            }
            else
            {
                blankShowTooltip.text = "Show Display";
                if (GUILayout.Button(blankShowTooltip))
                {
                    display.currentBrightness = display.settings.brightness;
                }
            }
            EditorGUILayout.EndHorizontal();

            SerializedObject serializedSettings = new SerializedObject(display.settings);
            float previousHeight = display.settings.heightInVR;
            float previousBrightness = display.settings.brightness;
            EditorGUILayout.PropertyField(
                serializedSettings.FindProperty("brightness"),
                includeChildren: true,
                LayoutSettings.EditFieldOption
            );
            EditorGUILayout.PropertyField(
                serializedSettings.FindProperty("heightInVR"),
                includeChildren: true,
                LayoutSettings.EditFieldOption
            );
            serializedSettings.ApplyModifiedProperties();
            if (previousHeight != display.settings.heightInVR)
            {
                display.transform.localPosition = new Vector3(0, display.settings.heightInVR, 0);
            }
            if (previousBrightness != display.settings.brightness)
            {
                display.currentBrightness = display.settings.brightness;
            }
            EditorGUILayout.EndVertical();
        }

        /// <summary>Renders the Camera Mapping section that wires display cameras to physical monitors.</summary>
        private void DrawCameraMappingSection()
        {
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
        }

        /// <summary>Renders the MQTT section with broker IP/port and connection test.</summary>
        private void DrawMQTTSection()
        {
            if (EditorApplication.isPlaying)
            {
                GUI.enabled = false;
            }
            EditorGUILayout.BeginVertical(LayoutSettings.MainBoxStyle.Style);
            EditorGUILayout.LabelField("MQTT", LayoutSettings.SectionLabel);
            _client.ipAddress = EditorGUILayout.TextField(
                new GUIContent(
                    "ip: ",
                    "IP address of the MQTT broker that bridges this Unity scene to the experiment hardware."
                ),
                _client.ipAddress,
                LayoutSettings.EditFieldOption
            );

            string portText = EditorGUILayout.TextField(
                new GUIContent(
                    "port: ",
                    "TCP port of the MQTT broker that bridges this Unity scene to the experiment hardware."
                ),
                _client.port.ToString(),
                LayoutSettings.EditFieldOption
            );
            if (int.TryParse(portText, out int parsedPort))
            {
                _client.port = parsedPort;
            }

            if (GUI.changed)
            {
                EditorPrefs.SetString("SollertiaVR_MQTT_IP", _client.ipAddress);
                EditorPrefs.SetInt("SollertiaVR_MQTT_Port", _client.port);
            }
            if (
                GUILayout.Button(
                    new GUIContent(
                        "Test Connection",
                        "Check whether the MQTT broker is reachable at the specified ip and port."
                    )
                )
            )
            {
                _client.Connect(verbose: true);
                _client.Disconnect();
            }

            GUI.enabled = true;
            EditorGUILayout.EndVertical();
        }

        /// <summary>Ensures required GameObjects and folders exist in the scene.</summary>
        private void InitializeScene()
        {
            if (!AssetDatabase.IsValidFolder("Assets/VRSettings"))
            {
                AssetDatabase.CreateFolder("Assets", "VRSettings");
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
                    EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
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

            RemoveDefaultMainCamera();
            EnsureActorAndDisplay();
            EnsureControllers();

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
        }

        /// <summary>Removes every default Unity "Main Camera" GameObject from the active scene.</summary>
        /// <remarks>
        /// The auto-created Display owns the per-monitor cameras (via PerspectiveProjection) and the Actor
        /// owns the third-person tracking camera, so the Unity-default "Main Camera" left over by the new
        /// scene template renders nothing useful while still consuming display slot 0. Nothing in the C#
        /// code references <c>Camera.main</c> or the <c>MainCamera</c> tag, so removing it is safe. Uses
        /// <see cref="UnityEngine.Object.FindObjectsByType{T}(FindObjectsInactive, FindObjectsSortMode)"/>
        /// instead of <c>GameObject.Find</c> so inactive and duplicate instances are also cleaned up.
        /// </remarks>
        private static void RemoveDefaultMainCamera()
        {
            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            bool anyRemoved = false;
            foreach (Camera camera in cameras)
            {
                if (
                    camera.gameObject.CompareTag("MainCamera")
                    || string.Equals(camera.gameObject.name, "Main Camera", System.StringComparison.Ordinal)
                )
                {
                    DestroyImmediate(camera.gameObject);
                    anyRemoved = true;
                }
            }
            if (anyRemoved)
            {
                Debug.Log("Removed default Main Camera (unused; Display cameras handle monitor rendering).");
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }
        }

        /// <summary>Ensures the active scene contains exactly one Actor and one Display, wired together.</summary>
        /// <remarks>
        /// Creates a default Actor with the first prefab under <c>Resources/Actors/Prefabs/</c> and a default
        /// Display from the first prefab under <c>Resources/Displays/</c> whenever the active scene lacks
        /// them. Existing instances are left untouched. The Actor is linked to the Display via
        /// <see cref="ActorObject.Display"/> so the projection cameras render through the Actor's view.
        /// </remarks>
        private void EnsureActorAndDisplay()
        {
            ActorObject actor = FindAnyObjectByType<ActorObject>();
            if (actor == null)
            {
                GameObject[] actorModels = Resources.LoadAll<GameObject>("Actors/Prefabs");
                string defaultModel = actorModels.Length > 0 ? actorModels[0].name : "None";
                GameObject actorGameObject = new GameObject("Actor");
                actor = actorGameObject.AddComponent<ActorObject>();
                actor.InitiateActor(defaultModel, trackCamera: true);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            DisplayObject display = FindAnyObjectByType<DisplayObject>();
            if (display == null)
            {
                GameObject[] displayModels = Resources.LoadAll<GameObject>("Displays");
                if (displayModels.Length == 0)
                {
                    Debug.LogError(
                        "MainWindow.EnsureActorAndDisplay: no display prefabs found under Resources/Displays."
                    );
                    return;
                }
                display = DisplayObject.Create("Display", displayModels[0].name, DisplayType.Monitor);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            if (actor.Display != display)
            {
                actor.Display = display;
            }
        }

        /// <summary>Ensures the active scene contains one controller GameObject per supported ControllerTypes.</summary>
        /// <remarks>
        /// Iterates every <see cref="ControllerTypes"/> enum value, resolves it to a concrete subclass of
        /// <see cref="ControllerObject"/> via the <see cref="ControllerObject"/> assembly, and creates a
        /// matching GameObject under the scene's "Controllers" root when none of that exact type already
        /// exists. The created GameObject is named after the enum value and gets a settings asset via
        /// <see cref="ControllerObject.InitiateController"/>, which reuses any existing asset at the matching
        /// path. The Actor.Controller assignment is left untouched so user-chosen swaps survive auto-create.
        /// </remarks>
        public static void EnsureControllers()
        {
            GameObject controllersRoot = GameObject.Find("Controllers");
            if (controllersRoot == null)
            {
                return;
            }

            ControllerObject[] existingControllers = FindObjectsByType<ControllerObject>(FindObjectsSortMode.None);
            bool createdAny = false;

            foreach (ControllerTypes controllerType in System.Enum.GetValues(typeof(ControllerTypes)))
            {
                System.Type resolvedType = typeof(ControllerObject).Assembly.GetType($"Gimbl.{controllerType}");
                if (resolvedType == null)
                {
                    Debug.LogError(
                        $"MainWindow.EnsureControllers: could not resolve controller type 'Gimbl.{controllerType}'."
                    );
                    continue;
                }
                if (existingControllers.Any(existing => existing.GetType() == resolvedType))
                {
                    continue;
                }

                string displayName = controllerType switch
                {
                    ControllerTypes.LinearTreadmill => "Linear",
                    ControllerTypes.SimulatedLinearTreadmill => "Simulated Linear",
                    _ => controllerType.ToString(),
                };
                GameObject controllerGameObject = new GameObject(displayName);
                ControllerObject controller = (ControllerObject)controllerGameObject.AddComponent(resolvedType);
                controller.InitiateController();
                ControllerOutput output = controllerGameObject.AddComponent<ControllerOutput>();
                output.master = controller;
                createdAny = true;
            }

            if (createdAny)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }
        }
    }
}
