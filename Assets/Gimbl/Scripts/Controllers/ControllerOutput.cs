/// <summary>
/// Provides the ControllerOutput class for linking controllers to actors in the scene.
/// </summary>
using UnityEngine;

namespace Gimbl
{
    /// <summary>
    /// Lightweight component that references the master controller object.
    /// </summary>
    /// <remarks>
    /// This component is attached to GameObjects for scene linking between actors and controllers.
    /// </remarks>
    public class ControllerOutput : MonoBehaviour
    {
        /// <summary>The master ControllerObject this output references.</summary>
        public ControllerObject master;
    }
}
