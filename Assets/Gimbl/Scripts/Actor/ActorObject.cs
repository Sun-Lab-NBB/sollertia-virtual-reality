/// <summary>
/// Provides the ActorObject class representing an animal in the VR environment.
///
/// Manages the actor's display, controller, and settings references with validation
/// to ensure proper linkage between components.
/// </summary>
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Gimbl
{
    /// <summary>
    /// Represents an animal actor in the VR environment with linked display and controller.
    /// </summary>
    [System.Serializable]
    public partial class ActorObject : MonoBehaviour
    {
        /// <summary>Determines whether actor movement is enabled.</summary>
        public bool isActive = true;

        /// <summary>The actor's configuration settings asset.</summary>
        public ActorSettings settings;

        /// <summary>The serialized backing field for the Display property.</summary>
        [SerializeField]
        private DisplayObject _display;

        /// <summary>The serialized backing field for the Controller property.</summary>
        [SerializeField]
        private ControllerOutput _controller;

        /// <summary>The display object rendering the VR view for this actor.</summary>
        public DisplayObject Display
        {
            get { return _display; }
            set
            {
                if (value != _display)
                {
                    // Parents new display to this actor.
                    if (value != null)
                    {
                        value.ParentToActor(this);
                    }

                    // Unparents previous display if it existed.
                    if (_display != null)
                    {
                        _display.Unparent();
                    }

                    _display = value;
                }
            }
        }

        /// <summary>
        /// The controller providing input for this actor. Only one controller can be linked at a time.
        /// </summary>
        public ControllerOutput Controller
        {
            get { return _controller; }
            set
            {
                if (_controller != value)
                {
                    // Abandons previous controller.
                    if (_controller != null && _controller.master != null)
                    {
                        _controller.master.actor = null;
                    }

                    _controller = value;

                    if (value != null && value.master != null)
                    {
                        value.master.actor = this;

                        // Ensures other actors are no longer coupled to this controller.
                        foreach (ActorObject actor in FindObjectsByType<ActorObject>(FindObjectsSortMode.None))
                        {
                            if (actor.Controller == value && actor != this)
                            {
                                Debug.LogWarning(
                                    $"Switched Controller {value.gameObject.name} from {actor.gameObject.name} to {gameObject.name}"
                                );
                                actor._controller = null;
                            }
                        }
                    }

#if UNITY_EDITOR
                    if (!EditorApplication.isPlaying)
                    {
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                            UnityEngine.SceneManagement.SceneManager.GetActiveScene()
                        );
                    }
#endif
                }
            }
        }

#if UNITY_EDITOR
        /// <summary>Initializes a new actor with the specified model and optional tracking camera.</summary>
        /// <param name="modelName">The name of the model prefab to load, or "None" for no model.</param>
        /// <param name="trackCamera">If true, creates a tracking camera for this actor.</param>
        public void InitiateActor(string modelName, bool trackCamera)
        {
            gameObject.transform.SetParent(GameObject.Find("Actors").transform);

            ActorSettings asset = ScriptableObject.CreateInstance<ActorSettings>();
            AssetDatabase.CreateAsset(asset, $"Assets/VRSettings/Actors/{gameObject.name}.asset");
            settings = asset;

            // Adds character controller for collision detection.
            CharacterController characterController = gameObject.AddComponent<CharacterController>();
            characterController.slopeLimit = 45;
            characterController.stepOffset = 0.000001f;
            characterController.skinWidth = 0.05f;
            characterController.minMoveDistance = 0.001f;
            characterController.center = new Vector3(0, 0.55f, 0);
            characterController.radius = 0.5f;
            characterController.height = 0.1f;

            // Creates render layer for this actor.
            TagsAndLayers.AddLayer(gameObject.name);

            // Instantiates the model if specified.
            if (modelName != "None")
            {
                Object modelObject = Resources.Load($"Actors/Prefabs/{modelName}");
                GameObject model = Instantiate(modelObject) as GameObject;
                model.name = $"Model {modelName}";
                model.transform.SetParent(gameObject.transform);
                model.layer = LayerMask.NameToLayer(gameObject.name);
            }

            // Creates tracking camera if requested.
            if (trackCamera)
            {
                // Finds currently used displays to avoid conflicts.
                List<int> usedDisplays = new List<int>();
                List<int> availableDisplays = new List<int>() { 0, 1, 2, 3, 4, 5, 6, 7 };
                TagsAndLayers.AddTag("TrackCam");

                foreach (GameObject trackObject in GameObject.FindGameObjectsWithTag("TrackCam"))
                {
                    if (trackObject.TryGetComponent<Camera>(out Camera existingCamera))
                    {
                        usedDisplays.Add(existingCamera.targetDisplay);
                    }
                }

                int[] displays = availableDisplays.Except(usedDisplays).ToArray();
                int nextDisplay = displays.Length > 0 ? displays[0] : 7;

                // Creates the tracking camera.
                GameObject cameraObject = new GameObject($"Track Cam: {settings.name}");
                Camera cameraComponent = cameraObject.AddComponent<Camera>();
                cameraObject.transform.parent = gameObject.transform;
                cameraObject.transform.localPosition = new Vector3(0, 1, -1.3f);
                cameraObject.transform.eulerAngles = new Vector3(20, 0, 0);
                cameraComponent.clearFlags = CameraClearFlags.Skybox;
                cameraComponent.backgroundColor = Color.black;
                cameraObject.tag = "TrackCam";
                cameraComponent.targetDisplay = nextDisplay;
            }

            Undo.RegisterCreatedObjectUndo(gameObject, "Create Actor");
        }

        /// <summary>Deletes this actor after user confirmation.</summary>
        public void DeleteActor()
        {
            bool accept = EditorUtility.DisplayDialog(
                $"Remove Actor {name}?",
                $"Are you sure you want to delete Actor {name}?",
                "Delete",
                "Cancel"
            );

            if (accept)
            {
                TagsAndLayers.RemoveLayer(name);

                // Unparents attached displays before deletion.
                PerspectiveProjection perspectiveProjection = GetComponentInChildren<PerspectiveProjection>();
                if (perspectiveProjection != null)
                {
                    perspectiveProjection.transform.parent.transform.SetParent(parent: null, worldPositionStays: true);
                }

                Undo.DestroyObjectImmediate(gameObject);
            }
        }

        /// <summary>Renders the editor GUI for editing actor properties.</summary>
        public void EditMenu()
        {
            EditorGUILayout.BeginVertical(LayoutSettings.SubBoxStyle.style);

            // Controller field.
            EditorGUILayout.BeginHorizontal();
            if (Controller != null)
            {
                EditorGUILayout.LabelField(
                    "<color=#66CC00>Controller: </color>",
                    LayoutSettings.LinkFieldStyle,
                    LayoutSettings.LinkFieldLayout
                );
            }
            else
            {
                EditorGUILayout.LabelField(
                    "<color=#EE0000>Controller: </color>",
                    LayoutSettings.LinkFieldStyle,
                    LayoutSettings.LinkFieldLayout
                );
            }

            Controller = (ControllerOutput)
                EditorGUILayout.ObjectField(
                    Controller,
                    typeof(ControllerOutput),
                    allowSceneObjects: true,
                    LayoutSettings.LinkObjectLayout
                );
            EditorGUILayout.EndHorizontal();

            // Display field.
            EditorGUILayout.BeginHorizontal();
            if (Display != null)
            {
                EditorGUILayout.LabelField(
                    "<color=#66CC00>Display: </color>",
                    LayoutSettings.LinkFieldStyle,
                    LayoutSettings.LinkFieldLayout
                );
            }
            else
            {
                EditorGUILayout.LabelField(
                    "<color=#EE0000>Display: </color>",
                    LayoutSettings.LinkFieldStyle,
                    LayoutSettings.LinkFieldLayout
                );
            }

            Display = (DisplayObject)
                EditorGUILayout.ObjectField(
                    Display,
                    typeof(DisplayObject),
                    allowSceneObjects: true,
                    LayoutSettings.LinkObjectLayout
                );
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }
#endif
    }
}
