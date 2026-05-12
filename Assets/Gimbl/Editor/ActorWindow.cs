/// <summary>
/// Provides the ActorWindow class for actor and controller management in the editor.
///
/// Renders the editor window for creating, selecting, editing, and deleting actors
/// and controllers in the VR environment.
/// </summary>
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Gimbl
{
    /// <summary>
    /// Manages the editor window for actor and controller operations.
    /// </summary>
    public class ActorWindow : EditorWindow
    {
        /// <summary>The scroll position for the window content.</summary>
        private Vector2 _scrollPosition = Vector2.zero;

        /// <summary>The menu settings for actor management.</summary>
        [SerializeField]
        private ActorMenuSettings _actorSettings = new ActorMenuSettings() { typeName = "Actor" };

        /// <summary>The menu settings for controller management.</summary>
        [SerializeField]
        private ControllerMenuSettings _controllerSettings = new ControllerMenuSettings() { typeName = "Controller" };

        /// <summary>The available actor model names from Resources.</summary>
        private string[] _actorModels;

        /// <summary>The index of the selected model in the dropdown.</summary>
        private int _selectedModel = 0;

        /// <summary>Determines whether to add a tracking camera when creating actors.</summary>
        private bool _trackCamera = true;

        /// <summary>The selected controller type for creation.</summary>
        private ControllerTypes _controllerType = ControllerTypes.LinearTreadmill;

        /// <summary>The current editor window instance.</summary>
        private static EditorWindow _currentWindow;

        /// <summary>The delegate type for object creation functions.</summary>
        /// <typeparam name="T">The type of Unity Object to create.</typeparam>
        /// <param name="settings">The menu settings for the creation.</param>
        public delegate void CreateFunc<T>(MenuSettings<T> settings)
            where T : UnityEngine.Object;

        /// <summary>Shows the ActorWindow editor window.</summary>
        public static void ShowWindow()
        {
            _currentWindow = GetWindow<ActorWindow>("Actors", true, typeof(MainWindow));
            _currentWindow.Show();
        }

        /// <summary>Loads actor models from Resources when the window is enabled.</summary>
        private void OnEnable()
        {
            Resources.LoadAll<GameObject>("Actors/Mouse");
            UnityEngine.Object[] data = Resources.LoadAll<GameObject>("Actors/Prefabs");
            _actorModels = data.Select(model => model.name).ToArray();
            _actorModels = _actorModels.Union(new string[] { "None" }).ToArray();
        }

        /// <summary>Renders the actor and controller management GUI.</summary>
        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(
                _scrollPosition,
                GUILayout.Height(position.height),
                GUILayout.Width(position.width)
            );

            EditorGUILayout.BeginVertical(LayoutSettings.MainBoxStyle.style);
            EditorGUILayout.LabelField("Actors", LayoutSettings.SectionLabel);

            EditorGUILayout.BeginHorizontal(LayoutSettings.EditWidth);
            SelectMenu(_actorSettings);
            if (GUILayout.Button("Delete", LayoutSettings.ButtonOption))
            {
                _actorSettings.SelectedObject.DeleteActor();
            }
            EditorGUILayout.EndHorizontal();

            if (_actorSettings.SelectedObject != null)
            {
                _actorSettings.SelectedObject.EditMenu();
            }

            if (EditorApplication.isPlaying)
            {
                GUI.enabled = false;
            }
            _actorSettings.show[0] = EditorGUILayout.Foldout(_actorSettings.show[0], "Create");
            if (_actorSettings.show[0])
            {
                EditorGUILayout.BeginVertical(LayoutSettings.SubBoxStyle.style);
                EditorGUILayout.LabelField("Create Actor", EditorStyles.boldLabel);
                _actorSettings.name = EditorGUILayout.TextField(
                    "Actor Name: ",
                    _actorSettings.name,
                    LayoutSettings.EditFieldOption
                );
                _selectedModel = EditorGUILayout.Popup(
                    "Model: ",
                    _selectedModel,
                    _actorModels,
                    LayoutSettings.EditFieldOption
                );
                _trackCamera = EditorGUILayout.Toggle("Add Tracking Cam: ", _trackCamera);
                CreateButton(_actorSettings);
                EditorGUILayout.EndVertical();
            }
            GUI.enabled = true;
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(LayoutSettings.MainBoxStyle.style);
            EditorGUILayout.LabelField("Controllers", LayoutSettings.SectionLabel);

            EditorGUILayout.BeginHorizontal(LayoutSettings.EditWidth);
            SelectMenu(_controllerSettings);
            if (GUILayout.Button("Delete", LayoutSettings.ButtonOption))
            {
                _controllerSettings.SelectedObject.master.DeleteController();
            }
            EditorGUILayout.EndHorizontal();

            _controllerSettings.show[0] = EditorGUILayout.Foldout(_controllerSettings.show[0], "Edit");
            if (_controllerSettings.show[0])
            {
                if (_controllerSettings.SelectedObject != null)
                {
                    EditorGUILayout.BeginVertical(LayoutSettings.SubBoxStyle.style);
                    _controllerSettings.SelectedObject.master.EditMenu();
                    EditorGUILayout.Space();
                    EditorGUILayout.BeginHorizontal(LayoutSettings.EditFieldOption);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Save Controller Settings", GUILayout.Width(250)))
                    {
                        _controllerSettings.SelectedObject.master.SaveController();
                    }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                    if (EditorApplication.isPlaying)
                    {
                        GUI.enabled = false;
                    }
                    EditorGUILayout.BeginHorizontal(LayoutSettings.EditFieldOption);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Load Controller Settings", GUILayout.Width(250)))
                    {
                        _controllerSettings.SelectedObject.master.LoadController();
                    }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                    GUI.enabled = true;
                    EditorGUILayout.EndVertical();
                }
            }

            if (EditorApplication.isPlaying)
            {
                GUI.enabled = false;
            }
            _controllerSettings.show[1] = EditorGUILayout.Foldout(_controllerSettings.show[1], "Create");
            if (_controllerSettings.show[1])
            {
                EditorGUILayout.BeginVertical(LayoutSettings.SubBoxStyle.style);
                EditorGUILayout.LabelField("Create Controller", EditorStyles.boldLabel);
                _controllerSettings.name = EditorGUILayout.TextField(
                    "Controller Name: ",
                    _controllerSettings.name,
                    LayoutSettings.EditFieldOption
                );
                _controllerType = (ControllerTypes)
                    EditorGUILayout.EnumPopup("Type: ", _controllerType, LayoutSettings.EditFieldOption);
                CreateButton(_controllerSettings);
                EditorGUILayout.EndVertical();
            }
            GUI.enabled = true;
            EditorGUILayout.EndVertical();

            GUILayout.EndScrollView();
        }

        /// <summary>Renders the object selection field and handles selection recovery.</summary>
        /// <typeparam name="T">The type of Unity Object to select.</typeparam>
        /// <param name="settings">The menu settings containing the selection state.</param>
        private void SelectMenu<T>(MenuSettings<T> settings)
            where T : UnityEngine.Object
        {
            if (settings.SelectedObject == null)
            {
                T obj = null;
                if (settings.selectedEntityId != EntityId.None)
                {
                    try
                    {
                        obj = (T)EditorUtility.EntityIdToObject(settings.selectedEntityId);
                    }
                    catch (System.InvalidCastException)
                    {
                        obj = null;
                    }
                }
                if (obj == null)
                {
                    obj = FindAnyObjectByType<T>();
                }
                if (obj != null)
                {
                    settings.SelectedObject = obj;
                }
            }
            settings.SelectedObject = (T)
                EditorGUILayout.ObjectField(settings.SelectedObject, typeof(T), allowSceneObjects: true);
        }

        /// <summary>Renders the create button with validation for duplicate and empty names.</summary>
        /// <typeparam name="T">The type of Unity Object to create.</typeparam>
        /// <param name="settings">The menu settings containing the new object name.</param>
        private void CreateButton<T>(MenuSettings<T> settings)
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
                GameObject newObject = new GameObject(settings.name);

                if (typeof(T) == typeof(ControllerOutput))
                {
                    // System.Type.GetType only resolves types from mscorlib unless given an assembly-qualified
                    // name. The controller types live in the same assembly as ControllerObject, so look them up
                    // through that assembly directly.
                    System.Type resolvedType = typeof(ControllerObject).Assembly.GetType($"Gimbl.{_controllerType}");
                    if (resolvedType == null)
                    {
                        Debug.LogError($"ActorWindow: could not resolve controller type 'Gimbl.{_controllerType}'.");
                        return;
                    }
                    ControllerObject controller = (ControllerObject)newObject.AddComponent(resolvedType);
                    controller.InitiateController();
                    ControllerOutput controllerOutput = newObject.AddComponent<ControllerOutput>();
                    controllerOutput.master = controller;
                    settings.SelectedObject = controllerOutput as T;
                }

                if (typeof(T) == typeof(ActorObject))
                {
                    ActorObject actor = newObject.AddComponent<ActorObject>();
                    actor.InitiateActor(_actorModels[_selectedModel], _trackCamera);
                    settings.SelectedObject = actor as T;
                }

                settings.name = "";
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Stores menu state and selection for a generic Unity Object type.
        /// </summary>
        /// <typeparam name="T">The type of Unity Object this menu manages.</typeparam>
        public class MenuSettings<T>
            where T : UnityEngine.Object
        {
            /// <summary>The display name of the object type.</summary>
            public string typeName;

            /// <summary>The array of foldout visibility states.</summary>
            public bool[] show = { false, false, false, false, false };

            /// <summary>The name for creating new objects.</summary>
            public string name = "";

            /// <summary>The entity ID of the selected object for serialization.</summary>
            public EntityId selectedEntityId;

            /// <summary>The rectangle position for the editing window.</summary>
            public Rect editRect = new Rect();

            /// <summary>The backing field for the selected object.</summary>
            private T _selectedObject;

            /// <summary>The currently selected object.</summary>
            public T SelectedObject
            {
                get { return _selectedObject; }
                set
                {
                    if (!UnityEngine.Object.ReferenceEquals(value, _selectedObject))
                    {
                        _selectedObject = value;
                        if (value != null)
                        {
                            selectedEntityId = value.GetEntityId();
                        }
                        else
                        {
                            selectedEntityId = EntityId.None;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Stores serializable menu settings for ActorObject selection.
        /// </summary>
        [System.Serializable]
        public class ActorMenuSettings : MenuSettings<ActorObject> { }

        /// <summary>
        /// Stores serializable menu settings for ControllerOutput selection.
        /// </summary>
        [System.Serializable]
        public class ControllerMenuSettings : MenuSettings<ControllerOutput> { }
    }
}
