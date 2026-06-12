/// <summary>
/// Provides the DisplayObject class for VR display management.
///
/// Manages display attachment to actors and camera culling mask configuration
/// for proper VR rendering.
/// </summary>
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Gimbl
{
    /// <summary>
    /// Manages a VR display with brightness control and actor attachment.
    /// </summary>
    public class DisplayObject : MonoBehaviour
    {
        /// <summary>The configuration settings for this display.</summary>
        public DisplaySettings settings;

        /// <summary>The current brightness level (0-100).</summary>
        public float currentBrightness = 100f;

        /// <summary>
        /// Parents this display to an actor, offsets it to the configured VR eye height
        /// (<c>settings.heightInVR</c>, defaulting to 0 when settings is null), and configures camera culling.
        /// </summary>
        /// <param name="actor">The actor to attach this display to.</param>
        public void ParentToActor(ActorObject actor)
        {
            gameObject.transform.SetParent(actor.transform, false);
            float heightInVR = settings?.heightInVR ?? 0f;
            gameObject.transform.localPosition = new Vector3(0f, heightInVR, 0f);

            foreach (Camera displayCamera in gameObject.GetComponentsInChildren<Camera>())
            {
                displayCamera.cullingMask = -1;
                displayCamera.cullingMask &= ~(1 << LayerMask.NameToLayer(actor.name));
            }
        }

        /// <summary>Detaches this display from its parent actor and resets camera culling.</summary>
        public void Unparent()
        {
            gameObject.transform.SetParent(null);
            foreach (Camera displayCamera in gameObject.GetComponentsInChildren<Camera>())
            {
                displayCamera.cullingMask = -1;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Instantiates a new display from a Resources model prefab and reuses or creates its settings asset.
        /// </summary>
        /// <param name="displayName">The name to assign to the new display GameObject and its settings asset.</param>
        /// <param name="modelName">The prefab name under <c>Resources/Displays/</c> to instantiate.</param>
        /// <returns>The created <see cref="DisplayObject"/>, or null when the model prefab cannot be found.</returns>
        public static DisplayObject Create(string displayName, string modelName)
        {
            UnityEngine.Object modelPrefab = Resources.Load($"Displays/{modelName}");
            if (modelPrefab == null)
            {
                Debug.LogError($"DisplayObject.Create: model '{modelName}' not found under Resources/Displays.");
                return null;
            }

            GameObject displayGameObject = Instantiate(modelPrefab) as GameObject;
            displayGameObject.name = displayName;
            DisplayObject display = displayGameObject.AddComponent<DisplayObject>();
            displayGameObject.tag = "VRDisplay";

            // Reuses any pre-existing settings asset so user customizations (brightness, heightInVR) survive
            // scene rebuilds.
            string settingsAssetPath = $"Assets/VRSettings/Displays/{displayName}.asset";
            DisplaySettings displaySettings = AssetDatabase.LoadAssetAtPath<DisplaySettings>(settingsAssetPath);
            if (displaySettings == null)
            {
                displaySettings = ScriptableObject.CreateInstance<DisplaySettings>();
                AssetDatabase.CreateAsset(displaySettings, settingsAssetPath);
            }
            display.settings = displaySettings;

            foreach (MeshRenderer mesh in displayGameObject.GetComponentsInChildren<MeshRenderer>())
            {
                mesh.GetComponent<MeshCollider>().enabled = false;
                // Mesh prefab names follow *Monitor; rename the camera to "<role> View" so the
                // dropdown in Camera Mapping surfaces the role (Left View, Right View, Center View).
                string cameraName = mesh.name.Replace("Monitor", " View");
                GameObject cameraObject = new GameObject(cameraName);
                cameraObject.transform.SetParent(mesh.transform.parent);
                cameraObject.transform.localPosition = Vector3.zero;
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

            Undo.RegisterCreatedObjectUndo(displayGameObject, "Create Display");
            return display;
        }
#endif
    }
}
