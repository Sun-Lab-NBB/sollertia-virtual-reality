/// <summary>
/// Provides the GuidanceZone class that tracks whether an animal has entered a guidance trigger area.
///
/// Used as a child of StimulusTriggerZone to define where guidance mode delivers automatic stimulus.
/// When the animal reaches this zone in guidance mode, the parent StimulusTriggerZone delivers the stimulus.
/// </summary>
using UnityEngine;

namespace SL.Tasks
{
    /// <summary>
    /// Tracks whether the animal is inside the guidance zone collider.
    /// Used by parent StimulusTriggerZone to determine when to deliver automatic stimulus in guidance mode.
    /// </summary>
    public class GuidanceZone : MonoBehaviour
    {
        /// <summary>Determines whether the animal is currently inside this guidance zone.</summary>
        [HideInInspector]
        public bool inZone = false;

        /// <summary>Sets the zone state to active when the animal enters the guidance zone collider.</summary>
        /// <param name="other">The object that entered the trigger zone.</param>
        private void OnTriggerEnter(Collider other)
        {
            inZone = true;
        }

        /// <summary>Sets the zone state to inactive when the animal exits the guidance zone collider.</summary>
        /// <param name="other">The object that exited the trigger zone.</param>
        private void OnTriggerExit(Collider other)
        {
            inZone = false;
        }
    }
}
