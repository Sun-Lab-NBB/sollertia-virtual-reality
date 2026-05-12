/// <summary>
/// Provides the LickMessage class that displays a temporary UI indicator for lick events.
/// </summary>
using UnityEngine;

namespace SL.UI
{
    /// <summary>
    /// Displays a temporary UI indicator when a lick event occurs and destroys itself after a configurable delay.
    /// </summary>
    public class LickMessage : MonoBehaviour
    {
        /// <summary>The time in seconds before this indicator is destroyed.</summary>
        public float destroyTime = 1.0f;

        /// <summary>Schedules the destruction of this game object after the specified delay.</summary>
        private void Start()
        {
            Destroy(gameObject, destroyTime);
        }
    }
}
