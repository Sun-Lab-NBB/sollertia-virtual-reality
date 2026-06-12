/// <summary>
/// Provides the OccupancyZone class that tracks whether an animal has occupied a zone for a required duration.
///
/// Used by the occupancy trigger modes (occupancy_disarm, occupancy_arm, occupancy_trigger). The occupancy
/// mode specifies how a stimulus is triggered, not what stimulus is delivered.
///
/// When the animal enters the zone, a high-precision timer starts. If the animal stays for the configured
/// occupancy duration, the occupancy requirement is met. If the animal leaves early, it remains unmet. The
/// parent StimulusTriggerZone reads the occupancyMet state and interprets it per trigger mode.
/// </summary>
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace SL.Tasks
{
    /// <summary>
    /// Tracks animal occupancy duration within a zone and exposes whether the occupancy requirement was met.
    /// </summary>
    public class OccupancyZone : MonoBehaviour, IResettable
    {
        /// <summary>
        /// The duration in milliseconds that the animal must occupy the zone to meet the occupancy requirement.
        /// Set at task creation time from the task template.
        /// </summary>
        public float occupancyDurationMs = 1000f;

        /// <summary>Determines whether the animal is currently inside this zone.</summary>
        [HideInInspector]
        public bool inZone = false;

        /// <summary>
        /// Determines whether the animal has met the occupancy requirement (occupied for the required duration).
        /// Reset to false by ResetZone at lap start. The parent StimulusTriggerZone interprets it per trigger mode.
        /// </summary>
        [HideInInspector]
        public bool occupancyMet = false;

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
            if (!isActive || occupancyMet)
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
        /// <param name="other">The object that entered the trigger zone.</param>
        private void OnTriggerEnter(Collider other)
        {
            if (!isActive || occupancyMet)
                return;

            inZone = true;
            _occupancyTimer.Restart();
            Debug.Log("OccupancyZone: Animal entered, timer started.");
        }

        /// <summary>Stops the timer and checks the result when the animal exits the zone collider.</summary>
        /// <param name="other">The object that exited the trigger zone.</param>
        private void OnTriggerExit(Collider other)
        {
            if (!isActive)
                return;

            inZone = false;
            _occupancyTimer.Stop();

            if (!occupancyMet)
            {
                OnOccupancyFailed();
            }
        }

        /// <summary>Resets the occupancy zone state for a new lap.</summary>
        /// <remarks>Invoked by ResetZone when the animal enters the reset zone.</remarks>
        public void ResetState()
        {
            isActive = true;
            occupancyMet = false;
            inZone = false;
            _occupancyTimer.Reset();
        }

        /// <summary>Returns the elapsed time in milliseconds since the occupancy timer started.</summary>
        internal long GetElapsedMilliseconds()
        {
            return _occupancyTimer.ElapsedMilliseconds;
        }

        /// <summary>Marks the occupancy requirement as met once the animal has occupied the zone long enough.</summary>
        private void OnOccupancyMet()
        {
            Debug.Log("OccupancyZone: Occupancy requirement met.");
            occupancyMet = true;
            _occupancyTimer.Stop();
        }

        /// <summary>Logs a message when the animal leaves the zone before meeting the occupancy requirement.</summary>
        private void OnOccupancyFailed()
        {
            Debug.Log("OccupancyZone: Occupancy failed - animal left early.");
        }
    }
}
