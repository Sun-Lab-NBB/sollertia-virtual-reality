/// <summary>
/// Provides the ResetZone class that resets all stimulus, occupancy, and guidance zones when the animal
/// completes a lap.
/// </summary>
using UnityEngine;

namespace SL.Tasks
{
    /// <summary>
    /// Resets all zone instances when the animal enters this zone.
    /// Placed at the start of each segment to prepare zones for the next lap.
    /// </summary>
    public class ResetZone : MonoBehaviour
    {
        /// <summary>The array of all StimulusTriggerZone instances in the scene.</summary>
        private StimulusTriggerZone[] _stimulusTriggerZones;

        /// <summary>The array of all OccupancyZone instances in the scene.</summary>
        private OccupancyZone[] _occupancyZones;

        /// <summary>The array of all OccupancyGuidanceZone instances in the scene.</summary>
        private OccupancyGuidanceZone[] _occupancyGuidanceZones;

        /// <summary>Finds all zone instances in the scene at startup.</summary>
        private void Start()
        {
            _stimulusTriggerZones = FindObjectsByType<StimulusTriggerZone>(FindObjectsSortMode.None);
            _occupancyZones = FindObjectsByType<OccupancyZone>(FindObjectsSortMode.None);
            _occupancyGuidanceZones = FindObjectsByType<OccupancyGuidanceZone>(FindObjectsSortMode.None);
        }

        /// <summary>Resets all zones to their initial state when the animal enters the reset zone collider.</summary>
        /// <param name="collider">The collider that entered the reset zone.</param>
        private void OnTriggerEnter(Collider collider)
        {
            foreach (StimulusTriggerZone zone in _stimulusTriggerZones)
            {
                zone.ResetState();
            }

            foreach (OccupancyZone zone in _occupancyZones)
            {
                zone.ResetState();
            }

            foreach (OccupancyGuidanceZone zone in _occupancyGuidanceZones)
            {
                zone.ResetState();
            }
        }
    }
}
