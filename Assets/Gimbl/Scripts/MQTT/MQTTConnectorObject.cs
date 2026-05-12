/// <summary>
/// Provides the MQTTConnectorObject class that automatically connects to the MQTT broker on scene start.
/// </summary>
using UnityEngine;

namespace Gimbl
{
    /// <summary>
    /// Automatically establishes MQTT broker connection when the scene starts.
    /// </summary>
    /// <remarks>
    /// This MonoBehaviour should be attached to a GameObject in scenes that require MQTT connectivity.
    /// It triggers the MQTTClient.Connect() method during OnEnable.
    /// </remarks>
    public class MQTTConnectorObject : MonoBehaviour
    {
        /// <summary>Connects to the MQTT broker when the object is enabled.</summary>
        private void OnEnable()
        {
            if (MQTTClient.Instance == null)
            {
                Debug.LogError("MQTTConnectorObject: MQTTClient.Instance not available");
                return;
            }
            MQTTClient.Instance.Connect(verbose: false);
        }
    }
}
