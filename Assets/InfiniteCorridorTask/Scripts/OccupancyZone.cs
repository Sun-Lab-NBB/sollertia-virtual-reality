/// <summary>
/// Provides the OccupancyZone class that tracks whether an animal has occupied a zone for a required duration.
///
/// Used for trial types that require occupancy-based stimulus disarming. The occupancy mode specifies
/// how a stimulus is triggered, not what stimulus is delivered.
///
/// When the animal enters the zone, a high-precision timer starts. If the animal stays for the configured
/// occupancy duration, the boundary is disarmed. If the animal leaves early, the boundary remains armed.
/// The parent StimulusTriggerZone reads the boundaryDisarmed state to determine collision behavior.
/// </summary>
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace SL.Tasks
{
    /// <summary>
    /// Tracks animal occupancy duration within a zone and manages boundary arm/disarm state.
    /// </summary>
    public class OccupancyZone : MonoBehaviour, IResettable
    {
        /// <summary>
        /// The duration in milliseconds that the animal must occupy the zone to disarm the boundary.
        /// Set at task creation time from the task template.
        /// </summary>
        public float occupancyDurationMs = 1000f;

        /// <summary>Determines whether the animal is currently inside this zone.</summary>
        [HideInInspector]
        public bool inZone = false;

        /// <summary>
        /// Determines whether the boundary has been disarmed by meeting the occupancy requirement.
        /// Reset to false by ResetZone at lap start.
        /// </summary>
        [HideInInspector]
        public bool boundaryDisarmed = false;

        /// <summary>Determines whether this zone is active (only checks once per lap). Reset by ResetZone.</summary>
        public bool isActive = true;

        /// <summary>The high-precision stopwatch for accurate millisecond timing.</summary>
        private Stopwatch _occupancyTimer;

        /// <summary>Initializes the occupancy timer.</summary>
        private void Start()
        {
            _occupancyTimer = new Stopwatch();
        }

        /// <summary>Checks if the occupancy duration has been met while the animal is in the zone.</summary>
        private void Update()
        {
            if (!isActive || boundaryDisarmed)
                return;

            if (_occupancyTimer.IsRunning && inZone)
            {
                if (_occupancyTimer.ElapsedMilliseconds >= occupancyDurationMs)
                {
                    OnOccupancyMet();
                }
            }
        }

        /// <summary>Starts the occupancy timer when the animal enters the zone collider.</summary>
        /// <param name="other">The collider that entered the trigger zone.</param>
        private void OnTriggerEnter(Collider other)
        {
            if (!isActive || boundaryDisarmed)
                return;

            inZone = true;
            _occupancyTimer.Restart();
            Debug.Log("OccupancyZone: Animal entered, timer started.");
        }

        /// <summary>Stops the timer and checks the result when the animal exits the zone collider.</summary>
        /// <param name="other">The collider that exited the trigger zone.</param>
        private void OnTriggerExit(Collider other)
        {
            if (!isActive)
                return;

            inZone = false;
            _occupancyTimer.Stop();

            if (!boundaryDisarmed)
            {
                OnOccupancyFailed();
            }
        }

        /// <summary>Returns the elapsed time in milliseconds since the occupancy timer started.</summary>
        internal long GetElapsedMilliseconds()
        {
            return _occupancyTimer.ElapsedMilliseconds;
        }

        /// <summary>Resets the occupancy zone state for a new lap.</summary>
        /// <remarks>Invoked by ResetZone when the animal enters the reset zone.</remarks>
        public void ResetState()
        {
            isActive = true;
            boundaryDisarmed = false;
            inZone = false;
            _occupancyTimer.Reset();
        }

        /// <summary>Disarms the boundary when the animal has occupied the zone for the required duration.</summary>
        private void OnOccupancyMet()
        {
            Debug.Log("OccupancyZone: Occupancy met - boundary disarmed.");
            boundaryDisarmed = true;
            _occupancyTimer.Stop();
        }

        /// <summary>Logs a message when the animal leaves the zone before meeting the occupancy requirement.</summary>
        private void OnOccupancyFailed()
        {
            Debug.Log("OccupancyZone: Occupancy failed - animal left early.");
        }
    }
}
