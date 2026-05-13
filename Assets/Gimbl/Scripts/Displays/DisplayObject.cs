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

        /// <summary>Parents this display to an actor and configures camera culling.</summary>
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
    }
}
