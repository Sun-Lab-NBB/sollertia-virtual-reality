/// <summary>
/// Provides the ControllerOutput class for linking controllers to actors in the scene.
/// </summary>
using UnityEngine;

namespace Gimbl
{
    /// <summary>
    /// Lightweight component that holds a typed reference to the active <see cref="ControllerObject"/>.
    /// </summary>
    /// <remarks>
    /// The <see cref="ActorObject.Controller"/> inspector field is typed as <see cref="ControllerOutput"/>
    /// rather than the concrete <see cref="ControllerObject"/> subclass so that swapping controller types
    /// (LinearTreadmill ↔ SimulatedLinearTreadmill, or any future subclass) does not invalidate the scene's
    /// serialized reference. Unity treats subclass slots as incompatible when the underlying type changes;
    /// the <see cref="master"/> indirection erases that distinction by funneling every controller through a
    /// single stable component type. <c>MainWindow.EnsureControllers</c> attaches one of these to each
    /// generated controller GameObject and points <see cref="master"/> at the sibling controller component
    /// at scene initialization.
    /// </remarks>
    public class ControllerOutput : MonoBehaviour
    {
        /// <summary>
        /// The <see cref="ControllerObject"/> subclass driving this output. Wired by
        /// <c>MainWindow.EnsureControllers</c> and consumed by the <see cref="ActorObject.Controller"/>
        /// setter to establish the bidirectional actor↔controller reference.
        /// </summary>
        public ControllerObject master;
    }
}
