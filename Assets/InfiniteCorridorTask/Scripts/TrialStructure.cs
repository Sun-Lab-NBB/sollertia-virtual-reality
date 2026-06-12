/// <summary>
/// Provides the TrialStructure class that defines the spatial configuration of a trial structure for Unity prefabs.
/// </summary>
using System;
using System.Collections.Generic;

namespace SL.Config
{
    /// <summary>
    /// Defines the spatial configuration of a trial structure for Unity prefabs.
    /// Contains the trial's cue sequence, zone positions, optional transition probabilities, and visibility settings.
    /// Mirrors the TrialStructure class from sollertia-shared-assets vr_configuration module.
    /// </summary>
    [Serializable]
    public class TrialStructure
    {
        /// <summary>The ordered sequence of cue names that comprise this trial's segment.</summary>
        public List<string> cueSequence;

        /// <summary>The position of the trial stimulus trigger zone starting boundary, in centimeters.</summary>
        public float stimulusTriggerZoneStartCm;

        /// <summary>The position of the trial stimulus trigger zone ending boundary, in centimeters.</summary>
        public float stimulusTriggerZoneEndCm;

        /// <summary>
        /// The position of the stimulus boundary. The collision, occupancy_disarm, and occupancy_arm modes
        /// fire on collision with it; interaction and occupancy_trigger modes do not use boundary collision.
        /// </summary>
        public float stimulusLocationCm;

        /// <summary>
        /// Determines whether the stimulus collision boundary is visible to the animal during this trial type.
        /// When true, the boundary marker is displayed in the VR environment at the stimulus location.
        /// </summary>
        public bool showStimulusCollisionBoundary = false;

        /// <summary>
        /// The trigger mode for the stimulus zone. "interaction" and "collision" use the StimulusTriggerZone
        /// prefab; "collision" strips the GuidanceRegion child (the child carrying the GuidanceZone script) and
        /// fires on a thin boundary wall. "occupancy_disarm", "occupancy_arm", and "occupancy_trigger" use the
        /// OccupancyTriggerZone prefab whose OccupancyZone child carries a nested OccupancyGuidanceZone child.
        /// </summary>
        public string triggerType;

        /// <summary>
        /// The duration in milliseconds the animal must occupy the zone for occupancy trigger modes. Applied
        /// to the OccupancyZone at task creation time and ignored for non-occupancy trigger modes.
        /// </summary>
        public float occupancyDurationMs = 1000f;

        /// <summary>
        /// The optional probability distribution over the trial names that may follow this trial during corridor
        /// traversal. Keys must reference other trial names defined on the same TaskTemplate. If provided, the
        /// values must sum to 1.0. Sparse: omitted keys carry implicit zero probability. When null or empty, the
        /// Task samples the next trial uniformly at random over all defined trial names.
        /// </summary>
        public Dictionary<string, float> transitions;

        /// <summary>Determines whether transition probabilities are defined for this trial.</summary>
        public bool HasTransitions => transitions != null && transitions.Count > 0;
    }
}
