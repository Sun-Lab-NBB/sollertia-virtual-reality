/// <summary>
/// Provides the TrialStructure class that defines the spatial configuration of a trial structure for Unity prefabs.
/// </summary>
using System;

namespace SL.Config
{
    /// <summary>
    /// Defines the spatial configuration of a trial structure for Unity prefabs.
    /// Contains segment mapping, zone positions, and visibility settings.
    /// This mirrors the TrialStructure class from sollertia-shared-assets task_template_data module.
    /// </summary>
    [Serializable]
    public class TrialStructure
    {
        /// <summary>The name of the Unity Segment this trial structure is based on.</summary>
        public string segmentName;

        /// <summary>The position of the trial stimulus trigger zone starting boundary, in centimeters.</summary>
        public float stimulusTriggerZoneStartCm;

        /// <summary>The position of the trial stimulus trigger zone ending boundary, in centimeters.</summary>
        public float stimulusTriggerZoneEndCm;

        /// <summary>
        /// The location of the invisible boundary with which the animal must collide to elicit the stimulus.
        /// </summary>
        public float stimulusLocationCm;

        /// <summary>
        /// Determines whether the stimulus collision boundary is visible to the animal during this trial type.
        /// When True, the boundary marker is displayed in the VR environment at the stimulus location.
        /// </summary>
        public bool showStimulusCollisionBoundary = false;

        /// <summary>
        /// The trigger mode for the stimulus zone. "lick" uses the StimulusTriggerZone prefab with
        /// a GuidanceZone child. "occupancy" uses the OccupancyTriggerZone prefab with OccupancyZone
        /// and OccupancyGuidanceZone children.
        /// </summary>
        public string triggerType;
    }
}
