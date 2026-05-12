/// <summary>
/// Provides the Segment class that defines a visual segment composed of a sequence of cues.
/// </summary>
using System;
using System.Collections.Generic;

namespace SL.Config
{
    /// <summary>
    /// Defines a visual segment composed of a sequence of cues for the Unity corridor system.
    /// Segments are the building blocks of the infinite corridor, each containing a sequence of visual cues
    /// and optional transition probabilities for segment-to-segment transitions.
    /// </summary>
    [Serializable]
    public class Segment
    {
        /// <summary>The segment identifier, must match the Unity prefab name.</summary>
        public string name;

        /// <summary>The ordered sequence of cue names that comprise this segment.</summary>
        public List<string> cueSequence;

        /// <summary>The optional transition probabilities to other segments. If provided, must sum to 1.0.</summary>
        public List<float> transitionProbabilities;

        /// <summary>Determines whether transition probabilities are defined for this segment.</summary>
        public bool HasTransitionProbabilities => transitionProbabilities != null && transitionProbabilities.Count > 0;
    }
}
