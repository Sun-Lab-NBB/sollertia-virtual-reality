/// <summary>
/// Provides the ActorObject class representing an animal in the VR environment.
///
/// Manages the actor's display and controller references, automatically re-parenting the
/// display and wiring the bidirectional actor-controller reference when these references change.
/// </summary>
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

namespace Gimbl
{
    /// <summary>
    /// Represents an animal actor in the VR environment with linked display and controller.
    /// </summary>
    [System.Serializable]
    public class ActorObject : MonoBehaviour
    {
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
                    if (value != null)
                    {
                        value.ParentToActor(this);
                    }

                    if (_display != null)
                    {
                        _display.Unparent();
                    }

                    _display = value;
                }
            }
        }

        /// <summary>The controller providing input for this actor.</summary>
        public ControllerOutput Controller
        {
            get { return _controller; }
            set
            {
                if (_controller != value)
                {
                    if (_controller != null && _controller.master != null)
                    {
                        _controller.master.actor = null;
                    }

                    _controller = value;

                    if (value != null && value.master != null)
                    {
                        value.master.actor = this;
                    }

#if UNITY_EDITOR
                    if (!EditorApplication.isPlaying)
                    {
                        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
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

            CharacterController characterController = gameObject.AddComponent<CharacterController>();
            characterController.slopeLimit = 45;
            characterController.stepOffset = 0.000001f;
            characterController.skinWidth = 0.05f;
            characterController.minMoveDistance = 0.001f;
            characterController.center = new Vector3(0, 0.55f, 0);
            characterController.radius = 0.5f;
            characterController.height = 0.1f;

            TagsAndLayers.AddLayer(gameObject.name);

            SetModel(modelName);

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

                GameObject cameraObject = new GameObject("Actor View");
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

        /// <summary>Swaps the actor's model prefab in place.</summary>
        /// <param name="modelName">
        /// The name of the model prefab under <c>Resources/Actors/Prefabs/</c>, or "None" to leave the actor
        /// without a visible model.
        /// </param>
        /// <remarks>
        /// Destroys any existing <c>Model *</c> children before instantiating the new prefab so swap operations
        /// leave a single model child. Assigns the actor's render layer to the new model so the actor's own
        /// view does not include its own mesh.
        /// </remarks>
        public void SetModel(string modelName)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name.StartsWith("Model ", StringComparison.Ordinal))
                {
                    DestroyImmediate(child.gameObject);
                }
            }

            if (modelName == "None")
            {
                return;
            }

            UnityEngine.Object modelObject = Resources.Load($"Actors/Prefabs/{modelName}");
            if (modelObject == null)
            {
                Debug.LogError($"ActorObject.SetModel: model '{modelName}' not found under Resources/Actors/Prefabs.");
                return;
            }

            GameObject model = Instantiate(modelObject) as GameObject;
            model.name = $"Model {modelName}";
            model.transform.SetParent(gameObject.transform);
            int layer = LayerMask.NameToLayer(gameObject.name);
            if (layer != -1)
            {
                model.layer = layer;
            }
        }

        /// <summary>Renders the editor GUI for editing actor properties.</summary>
        public void EditMenu()
        {
            string[] modelOptions = Resources
                .LoadAll<GameObject>("Actors/Prefabs")
                .Select(prefab => prefab.name)
                .Append("None")
                .ToArray();
            string currentModel = "None";
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child.name.StartsWith("Model ", StringComparison.Ordinal))
                {
                    currentModel = child.name.Substring("Model ".Length);
                    break;
                }
            }
            int currentIndex = Array.IndexOf(modelOptions, currentModel);
            if (currentIndex < 0)
            {
                currentIndex = Array.IndexOf(modelOptions, "None");
            }
            GUIContent modelLabel = new GUIContent(
                "Model: ",
                "3D Mesh rendered to represent the actor in the VR scene. Only visible in Actor View."
            );
            GUIContent[] modelGuiOptions = modelOptions.Select(name => new GUIContent(name)).ToArray();
            int newIndex = EditorGUILayout.Popup(modelLabel, currentIndex, modelGuiOptions);
            if (newIndex != currentIndex && newIndex >= 0)
            {
                Undo.RegisterFullObjectHierarchyUndo(gameObject, "Swap Actor Model");
                SetModel(modelOptions[newIndex]);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            ControllerOutput[] controllerOutputs = FindObjectsByType<ControllerOutput>(FindObjectsSortMode.None);
            string[] controllerOptions = new string[controllerOutputs.Length + 1];
            controllerOptions[0] = "None";
            for (int i = 0; i < controllerOutputs.Length; i++)
            {
                controllerOptions[i + 1] = controllerOutputs[i].gameObject.name;
            }
            int currentControllerIndex = 0;
            for (int i = 0; i < controllerOutputs.Length; i++)
            {
                if (Controller == controllerOutputs[i])
                {
                    currentControllerIndex = i + 1;
                    break;
                }
            }
            GUIContent controllerLabel = new GUIContent(
                "Controller: ",
                "The action input mode. Selecting None disables interfacing with the task, "
                    + "selecting Simulated enables interfacing via Keyboard."
            );
            GUIContent[] controllerGuiOptions = controllerOptions.Select(name => new GUIContent(name)).ToArray();
            int newControllerIndex = EditorGUILayout.Popup(
                controllerLabel,
                currentControllerIndex,
                controllerGuiOptions
            );
            if (newControllerIndex != currentControllerIndex)
            {
                Undo.RecordObject(this, "Swap Active Controller");
                Controller = newControllerIndex == 0 ? null : controllerOutputs[newControllerIndex - 1];
            }
        }
#endif
    }
}
