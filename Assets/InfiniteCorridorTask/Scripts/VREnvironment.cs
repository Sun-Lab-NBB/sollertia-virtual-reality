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

        /// <summary>The conversion factor from centimeters to Unity units.</summary>
        public float cmPerUnityUnit = 10.0f;

        /// <summary>Returns the corridor spacing in Unity units.</summary>
        public float CorridorSpacingUnity => corridorSpacingCm / cmPerUnityUnit;
    }
}
