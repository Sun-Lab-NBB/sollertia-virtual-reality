/// <summary>
/// Provides the LinearTreadmillSettings class for treadmill controller configuration.
/// </summary>
using UnityEngine;

namespace Gimbl
{
    /// <summary>
    /// Stores configuration settings for a linear treadmill controller.
    /// </summary>
    [System.Serializable]
    public class LinearTreadmillSettings : ScriptableObject
    {
        /// <summary>The MQTT device name for receiving treadmill data.</summary>
        public string deviceName = "LinearTreadmill";

        /// <summary>Determines whether this controller is active.</summary>
        public bool isActive = true;
    }
}
