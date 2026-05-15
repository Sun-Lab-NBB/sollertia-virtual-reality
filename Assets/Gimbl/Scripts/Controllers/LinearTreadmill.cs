/// <summary>
/// Provides the LinearTreadmill class for handling physical treadmill input via MQTT.
///
/// Receives movement data from an external treadmill device and translates it
/// to actor position updates in the VR environment.
/// </summary>
using UnityEngine;

namespace Gimbl
{
    /// <summary>
    /// Handles linear treadmill input from MQTT and updates actor position.
    /// </summary>
    public class LinearTreadmill : ControllerObject
    {
        /// <summary>The accumulated movement since last frame.</summary>
        private float _moved;

        /// <summary>The cached actor position for updates.</summary>
        private Vector3 _position;

        /// <summary>The MQTT channel subscribed to incoming treadmill data; null for simulated treadmills.</summary>
        private MQTTChannel<TreadmillMessage> _dataChannel;

        /// <summary>Subscribes to the hardware-treadmill MQTT data topic on start.</summary>
        /// <remarks>
        /// <see cref="SimulatedLinearTreadmill"/> hides this method via its own private <c>Start</c> and
        /// intentionally does not chain to it, so the hardware subscription stays inactive for keyboard
        /// runs. The lifecycle contract relies on Unity dispatching <c>Start</c> per most-derived type
        /// only: promoting either <c>Start</c> to <c>protected virtual</c> or wiring a base call from the
        /// simulated subclass would double-subscribe to <see cref="MQTTTopics.Motion"/>; both are deliberate
        /// non-options. If a third controller subclass appears, follow the same pattern (hide via its own
        /// <c>Start</c>; do not chain).
        /// </remarks>
        private void Start()
        {
            _dataChannel = new MQTTChannel<TreadmillMessage>(MQTTTopics.Motion);
            _dataChannel.receivedEvent.AddListener(OnMessage);
        }

        /// <summary>Removes the MQTT listener so the treadmill stops receiving data after destruction.</summary>
        private void OnDestroy()
        {
            _dataChannel?.receivedEvent.RemoveListener(OnMessage);
        }

        /// <summary>Processes accumulated movement each frame.</summary>
        public virtual void Update()
        {
            ProcessMovement();
        }

        /// <summary>Applies accumulated movement to the actor's position.</summary>
        public void ProcessMovement()
        {
            lock (movement)
            {
                if (actor != null)
                {
                    _moved = movement.Sum();

                    _position = actor.transform.position;
                    _position.z += _moved;

                    actor.transform.position = _position;
                }

                movement.Clear();
            }
        }

        /// <summary>Receives movement data from the treadmill via MQTT callback.</summary>
        /// <param name="message">The message containing the movement value.</param>
        public void OnMessage(TreadmillMessage message)
        {
            lock (movement)
            {
                movement.Add(message.movement);
            }
        }

        /// <summary>Defines the MQTT message structure for treadmill movement data.</summary>
        public class TreadmillMessage
        {
            /// <summary>The movement value received from the treadmill device.</summary>
            public float movement;
        }
    }
}
