/// <summary>
/// Provides the TriggerMode enumeration that selects how a StimulusTriggerZone delivers its stimulus.
/// </summary>
namespace SL.Tasks
{
    /// <summary>
    /// Defines the supported stimulus trigger mechanisms for a StimulusTriggerZone.
    /// </summary>
    /// <remarks>
    /// The mechanism specifies how a stimulus is triggered, not what stimulus is delivered. CreateTask sets the
    /// mode on each generated zone from the trial's trigger_type. Interaction is the default (ordinal 0) so an
    /// unconfigured StimulusTriggerZone behaves as the interaction prefab that ships it.
    /// </remarks>
    public enum TriggerMode
    {
        /// <summary>
        /// The animal engages an interaction sensor in the zone, or reaches the guidance zone, to fire.
        /// </summary>
        Interaction,

        /// <summary>Crossing the invisible boundary wall fires the stimulus unconditionally.</summary>
        Collision,

        /// <summary>Occupying disarms the boundary; collide-while-armed fires (occupy to avoid the stimulus).</summary>
        OccupancyDisarm,

        /// <summary>Occupying arms the boundary; collide-while-armed fires (occupy to earn the stimulus).</summary>
        OccupancyArm,

        /// <summary>
        /// Occupying for the required duration fires the stimulus immediately, with no boundary collision.
        /// </summary>
        OccupancyTrigger,
    }
}
