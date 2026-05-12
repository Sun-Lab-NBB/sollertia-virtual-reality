/// <summary>
/// Provides the FullScreenViewsSaved class for persisting camera assignments.
/// </summary>
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gimbl
{
    /// <summary>
    /// Stores camera-to-monitor assignments for persistence across sessions.
    /// </summary>
    [Serializable]
    public class FullScreenViewsSaved : ScriptableObject
    {
        /// <summary>The list of camera GameObject paths assigned to each monitor.</summary>
        public List<string> cameraNames;

        /// <summary>Initializes the camera names list when enabled.</summary>
        private void OnEnable()
        {
            if (cameraNames == null)
            {
                cameraNames = new List<string>();
            }
        }
    }
}
