/// <summary>
/// Provides the ActorSettings class for storing actor configuration data.
/// </summary>
using UnityEngine;

namespace Gimbl
{
    /// <summary>
    /// Stores configuration settings for an actor as a ScriptableObject asset.
    /// </summary>
    /// <remarks>
    /// Settings are stored in Assets/VRSettings/Actors/.
    /// Currently a placeholder that can be extended to add actor-specific settings.
    /// </remarks>
    [System.Serializable]
    public class ActorSettings : ScriptableObject { }
}
