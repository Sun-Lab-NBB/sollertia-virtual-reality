/// <summary>
/// Provides the DisplaySettings class for display configuration storage.
/// </summary>
using UnityEngine;

namespace Gimbl
{
    /// <summary>
    /// Stores configuration settings for a VR display.
    /// </summary>
    [System.Serializable]
    public class DisplaySettings : ScriptableObject
    {
        /// <summary>The brightness level for this display (0-100).</summary>
        [Tooltip("Default brightness (0-100) the display renders at while not blanked.")]
        public float brightness = 100f;

        /// <summary>The height of the display view in VR space.</summary>
        [Tooltip(
            "Vertical offset of the actor camera view relative to the actor's base position, in Unity units. "
                + "Controls the actor's 'eye level' perspective."
        )]
        public float heightInVR = 0.2f;
    }
}
