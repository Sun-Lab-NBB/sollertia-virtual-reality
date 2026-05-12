/// <summary>
/// Provides the TaskTemplate class that defines a VR task template for prefab generation and runtime configuration.
///
/// These classes mirror the Python task template classes from sollertia-shared-assets, containing
/// the data needed by Unity for VR corridor system prefab generation and runtime.
/// </summary>
using System;
using System.Collections.Generic;
using System.Linq;

namespace SL.Config
{
    /// <summary>
    /// Defines a VR task template used by Unity for prefab generation and runtime configuration.
    /// This mirrors the TaskTemplate class from sollertia-shared-assets task_template_data module.
    /// The template name is derived from the YAML filename during loading.
    /// </summary>
    [Serializable]
    public class TaskTemplate
    {
        /// <summary>The list of visual cues used in the task.</summary>
        public List<Cue> cues;

        /// <summary>The list of visual segments for the Unity corridor system.</summary>
        public List<Segment> segments;

        /// <summary>
        /// The dictionary of trial structures mapping trial names to their spatial configurations.
        /// Keys are trial names (e.g., 'ABC'), values contain zone positions and visibility settings.
        /// </summary>
        public Dictionary<string, TrialStructure> trialStructures;

        /// <summary>The configuration for the Unity VR corridor system.</summary>
        public VREnvironment vrEnvironment;

        /// <summary>
        /// The offset of the animal's starting position relative to the VR environment's
        /// cue sequence origin, in centimeters.
        /// </summary>
        public float cueOffsetCm;

        /// <summary>
        /// The template name, derived from the YAML filename during loading.
        /// Also corresponds to the Unity scene name.
        /// </summary>
        public string templateName;

        /// <summary>Returns a map of cue name to byte code for MQTT encoding.</summary>
        /// <returns>A dictionary mapping cue names to their byte codes.</returns>
        public Dictionary<string, byte> GetCueNameToCode()
        {
            return cues.ToDictionary(cue => cue.name, cue => (byte)cue.code);
        }

        /// <summary>Returns a map of cue name to Cue.</summary>
        /// <returns>A dictionary mapping cue names to their Cue instances.</returns>
        public Dictionary<string, Cue> GetCueByName()
        {
            return cues.ToDictionary(cue => cue.name, cue => cue);
        }

        /// <summary>Returns segment lengths in Unity units as an array.</summary>
        /// <returns>An array of segment lengths in Unity units.</returns>
        public float[] GetSegmentLengthsUnity()
        {
            Dictionary<string, Cue> cueMap = GetCueByName();
            float cmPerUnit = vrEnvironment.cmPerUnityUnit;
            return segments
                .Select(segment => segment.cueSequence.Sum(cueName => cueMap[cueName].LengthUnity(cmPerUnit)))
                .ToArray();
        }

        /// <summary>Returns cue lengths in Unity units as an array.</summary>
        /// <returns>An array of cue lengths in Unity units.</returns>
        public float[] GetCueLengthsUnity()
        {
            float cmPerUnit = vrEnvironment.cmPerUnityUnit;
            return cues.Select(cue => cue.LengthUnity(cmPerUnit)).ToArray();
        }

        /// <summary>Returns a segment by name lookup dictionary.</summary>
        /// <returns>A dictionary mapping segment names to their Segment instances.</returns>
        public Dictionary<string, Segment> GetSegmentByName()
        {
            return segments.ToDictionary(segment => segment.name, segment => segment);
        }

        /// <summary>Calculates the total length of a segment in Unity units.</summary>
        /// <param name="segmentName">The name of the segment to measure.</param>
        /// <returns>The total length of the segment in Unity units.</returns>
        public float GetSegmentLengthUnity(string segmentName)
        {
            Segment segment = GetSegmentByName()[segmentName];
            Dictionary<string, Cue> cueMap = GetCueByName();
            float cmPerUnit = vrEnvironment.cmPerUnityUnit;
            return segment.cueSequence.Sum(cueName => cueMap[cueName].LengthUnity(cmPerUnit));
        }

        /// <summary>
        /// Returns the trial structure associated with the given segment name, or null if none exists.
        /// </summary>
        /// <param name="segmentName">The name of the segment to look up.</param>
        /// <returns>The matching TrialStructure, or null if no trial references the segment.</returns>
        public TrialStructure GetTrialStructureForSegment(string segmentName)
        {
            if (trialStructures == null)
            {
                return null;
            }

            foreach (TrialStructure trial in trialStructures.Values)
            {
                if (string.Equals(trial.segmentName, segmentName, StringComparison.Ordinal))
                {
                    return trial;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns whether the stimulus collision boundary should be visible for a given segment.
        /// Looks up the trial that references this segment and returns its visibility setting.
        /// Returns false if no trial references this segment.
        /// </summary>
        /// <param name="segmentName">The name of the segment to check visibility for.</param>
        /// <returns>True if the stimulus collision boundary should be visible, false otherwise.</returns>
        public bool GetSegmentMarkerVisibility(string segmentName)
        {
            if (trialStructures == null)
            {
                return false;
            }

            foreach (TrialStructure trial in trialStructures.Values)
            {
                if (string.Equals(trial.segmentName, segmentName, StringComparison.Ordinal))
                {
                    return trial.showStimulusCollisionBoundary;
                }
            }

            return false;
        }
    }
}
