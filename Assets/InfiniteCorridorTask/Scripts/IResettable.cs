/// <summary>
/// Provides the IResettable interface implemented by zones that ResetZone notifies on lap start.
/// </summary>
namespace SL.Tasks
{
    /// <summary>
    /// Marks a zone component as resettable by <see cref="ResetZone"/> at each lap boundary.
    /// </summary>
    /// <remarks>
    /// The interface lets <see cref="ResetZone"/> drive every per-lap reset through a single typed loop
    /// instead of one loop per concrete zone class. Implementers are expected to be MonoBehaviours so
    /// scene-wide discovery via Unity's typed find helpers continues to work.
    /// </remarks>
    public interface IResettable
    {
        /// <summary>Resets the zone's per-lap state.</summary>
        void ResetState();
    }
}
