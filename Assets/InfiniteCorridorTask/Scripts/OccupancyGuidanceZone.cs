/// <summary>
/// Provides the OccupancyGuidanceZone class that triggers brake activation in occupancy guidance mode.
///
/// Used as a child of OccupancyZone to define where guidance mode activates the brake.
/// When the animal enters this zone in guidance mode (!requireWait), sends a TriggerDelay message
/// to sollertia-experiment instructing it to lock the brake for the remaining occupancy duration.
/// </summary>
using Gimbl;
using UnityEngine;

namespace SL.Tasks
{
    /// <summary>
    /// Handles occupancy guidance mode as a secondary trigger zone for OccupancyZone.
    /// When guidance mode is active and the animal enters, sends brake activation message with remaining duration.
    /// </summary>
    public class OccupancyGuidanceZone : MonoBehaviour, IResettable
    {
        /// <summary>Determines whether the animal is currently inside this guidance zone.</summary>
        [HideInInspector]
        public bool inZone = false;

        /// <summary>The reference to the Task for checking guidance mode state.</summary>
        private Task _task;

        /// <summary>
        /// The reference to the parent OccupancyZone, used to read occupancyMet and compute the remaining
        /// occupancy duration.
        /// </summary>
        private OccupancyZone _parentOccupancyZone;

        /// <summary>The MQTT channel for sending brake activation delay messages.</summary>
        private MQTTChannel<TriggerDelayMessage> _triggerDelayChannel;

        /// <summary>Determines whether the guidance trigger has already fired this lap.</summary>
        private bool _hasTriggered = false;

        /// <summary>Initializes references and sets up the MQTT channel.</summary>
        private void Start()
        {
            _task = FindAnyObjectByType<Task>();
            if (_task == null)
            {
                Debug.LogError($"OccupancyGuidanceZone ({gameObject.name}): No Task found in scene.");
                enabled = false;
                return;
            }

            _parentOccupancyZone = GetComponentInParent<OccupancyZone>();
            if (_parentOccupancyZone == null)
            {
                Debug.LogError($"OccupancyGuidanceZone ({gameObject.name}): No parent OccupancyZone found.");
                enabled = false;
                return;
            }

            _triggerDelayChannel = new MQTTChannel<TriggerDelayMessage>(MQTTTopics.Delay, isListener: false);
        }

        /// <summary>
        /// Marks the zone as occupied (inZone = true) when the animal enters the guidance zone collider.
        /// </summary>
        /// <param name="other">The object that entered the trigger zone.</param>
        private void OnTriggerEnter(Collider other)
        {
            inZone = true;

            // Only triggers in guidance mode (!requireWait), if not already triggered this lap, and if the
            // occupancy requirement is not yet met (so the brake can guide the animal to complete it).
            if (!_task.requireWait && !_hasTriggered && !_parentOccupancyZone.occupancyMet)
            {
                TriggerBrakeActivation();
            }
        }

        /// <summary>
        /// Marks the zone as no longer occupied (inZone = false) when the animal exits the guidance zone collider.
        /// </summary>
        /// <param name="other">The object that exited the trigger zone.</param>
        private void OnTriggerExit(Collider other)
        {
            inZone = false;
        }

        /// <summary>Resets the guidance zone state for a new lap.</summary>
        /// <remarks>Invoked by ResetZone when the animal enters the reset zone.</remarks>
        public void ResetState()
        {
            inZone = false;
            _hasTriggered = false;
        }

        /// <summary>Sends the TriggerDelay message with remaining occupancy duration to activate the brake.</summary>
        private void TriggerBrakeActivation()
        {
            // Clamps the remaining duration to zero so an overrun never produces a negative delay.
            long elapsedMilliseconds = _parentOccupancyZone.GetElapsedMilliseconds();
            uint remainingMilliseconds = (uint)
                Mathf.Max(0f, _parentOccupancyZone.occupancyDurationMs - elapsedMilliseconds);

            Debug.Log($"OccupancyGuidanceZone: Triggering brake for {remainingMilliseconds}ms.");

            _triggerDelayChannel.Send(new TriggerDelayMessage { delayMilliseconds = remainingMilliseconds });
            _hasTriggered = true;
        }

        /// <summary>Wraps trigger delay duration for MQTT transmission.</summary>
        public class TriggerDelayMessage
        {
            /// <summary>The delay duration in milliseconds before the brake releases.</summary>
            public uint delayMilliseconds;
        }
    }
}
