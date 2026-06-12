/// <summary>
/// Provides the VREnvironment class that defines the Unity VR corridor system configuration.
/// </summary>
using System;

namespace SL.Config
{
    /// <summary>
    /// Defines the Unity VR corridor system configuration.
    /// </summary>
    [Serializable]
    public class VREnvironment
    {
        /// <summary>The horizontal spacing between corridor instances in centimeters.</summary>
        public float corridorSpacingCm = 20.0f;

        /// <summary>The number of segments visible in each corridor instance (corridor depth).</summary>
        public int segmentsPerCorridor = 3;

        /// <summary>The name of the Unity prefab used for corridor padding.</summary>
        public string paddingPrefabName = "Padding";

        /// <summary>
        /// The number of centimeters represented by one Unity unit (centimeters-per-Unity-unit); divide a centimeter
        /// value by this to obtain Unity units.
        /// </summary>
        public float cmPerUnityUnit = 10.0f;

        /// <summary>
        /// The offset of the animal's starting position relative to each corridor's cue sequence origin,
        /// in centimeters. Drives both the upstream shift applied to every segment prefab's local origin
        /// and the position of the per-segment ResetZone.
        /// </summary>
        public float cueOffsetCm = 0.0f;

        /// <summary>Returns the corridor spacing in Unity units.</summary>
        public float CorridorSpacingUnity => corridorSpacingCm / cmPerUnityUnit;

        /// <summary>Returns the cue offset in Unity units.</summary>
        public float CueOffsetUnity => cueOffsetCm / cmPerUnityUnit;
    }
}
