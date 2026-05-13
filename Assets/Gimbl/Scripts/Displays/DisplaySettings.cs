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
        /// <summary>Determines whether this display is active.</summary>
        public bool isActive = true;

        /// <summary>The brightness level for this display (0-100).</summary>
        public float brightness = 100f;

        /// <summary>The height of the display view in VR space.</summary>
        public float heightInVR = 0.2f;
    }
}
