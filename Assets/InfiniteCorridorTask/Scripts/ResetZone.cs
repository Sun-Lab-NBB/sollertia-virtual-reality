/// <summary>
/// Provides the ResetZone class that resets all stimulus, occupancy, and guidance zones when the animal
/// completes a lap.
/// </summary>
using System.Collections.Generic;
using UnityEngine;

namespace SL.Tasks
{
    /// <summary>
    /// Resets all zone instances when the animal enters this zone.
    /// Placed at the start of each segment to prepare zones for the next lap.
    /// </summary>
    public class ResetZone : MonoBehaviour
    {
        /// <summary>The cached list of every <see cref="IResettable"/> in the scene at startup.</summary>
        /// <remarks>
        /// Discovered via typed <see cref="UnityEngine.Object.FindObjectsByType{T}"/> calls so the scan
        /// stays scoped to the three known resettable zone types rather than walking every MonoBehaviour.
        /// </remarks>
        private IResettable[] _resettables;

        /// <summary>Finds all resettable zone instances in the scene at startup.</summary>
        private void Start()
        {
            List<IResettable> resettables = new List<IResettable>();
            resettables.AddRange(FindObjectsByType<StimulusTriggerZone>(FindObjectsSortMode.None));
            resettables.AddRange(FindObjectsByType<OccupancyZone>(FindObjectsSortMode.None));
            resettables.AddRange(FindObjectsByType<OccupancyGuidanceZone>(FindObjectsSortMode.None));
            _resettables = resettables.ToArray();
        }

        /// <summary>Resets all zones to their initial state when the animal enters the reset zone collider.</summary>
        /// <param name="other">The collider that entered the reset zone.</param>
        private void OnTriggerEnter(Collider other)
        {
            foreach (IResettable zone in _resettables)
            {
                zone.ResetState();
            }
        }
    }
}
