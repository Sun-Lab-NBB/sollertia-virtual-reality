/// <summary>
/// Provides the Cue class that defines a single visual cue used in the VR environment.
/// </summary>
using System;

namespace SL.Config
{
    /// <summary>
    /// Defines a single visual cue used in the VR environment.
    /// Each cue has a unique name (used in segment definitions) and a unique uint8 code (for MQTT communication).
    /// Cues are not loaded as individual prefabs - they are baked into segment prefabs.
    /// </summary>
    [Serializable]
    public class Cue
    {
        /// <summary>The visual identifier for the cue (e.g., 'A', 'B', 'Gray'). Used in segment cue sequences.</summary>
        public string name;

        /// <summary>The unique uint8 code (0-255) used for MQTT communication and data analysis.</summary>
        public int code;

        /// <summary>The length of the cue in centimeters.</summary>
        public float lengthCm;

        /// <summary>
        /// The texture filename (e.g., "Cue 001 - 2x1 repeat.png") located in
        /// Assets/InfiniteCorridorTask/Textures/. Applied 1:1 to the cue wall panels.
        /// </summary>
        public string texture;

        /// <summary>Returns the length in Unity units given a cm-per-unit conversion factor.</summary>
        /// <param name="cmPerUnit">The centimeters-per-Unity-unit conversion factor.</param>
        /// <returns>The cue length in Unity units.</returns>
        public float LengthUnity(float cmPerUnit) => lengthCm / cmPerUnit;
    }
}
