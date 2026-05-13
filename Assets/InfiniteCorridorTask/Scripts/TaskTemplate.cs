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
    /// This mirrors the TaskTemplate class from sollertia-shared-assets vr_configuration module.
    /// The template name is derived from the YAML filename during loading.
    /// </summary>
    [Serializable]
    public class TaskTemplate
    {
        /// <summary>The list of visual cues used in the task.</summary>
        public List<Cue> cues;

        /// <summary>The configuration for the Unity VR corridor system.</summary>
        public VREnvironment vrEnvironment;

        /// <summary>
        /// The dictionary of trial structures mapping trial names to their spatial configurations.
        /// Keys are trial names (e.g., 'ABC'), values contain the cue sequence, zone positions, transitions,
        /// and visibility settings.
        /// </summary>
        public Dictionary<string, TrialStructure> trialStructures;

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

        /// <summary>Returns cue lengths in Unity units as an array.</summary>
        /// <returns>An array of cue lengths in Unity units.</returns>
        public float[] GetCueLengthsUnity()
        {
            float cmPerUnit = vrEnvironment.cmPerUnityUnit;
            return cues.Select(cue => cue.LengthUnity(cmPerUnit)).ToArray();
        }

        /// <summary>
        /// Returns the lengths of each trial's segment in Unity units, ordered by the trial_structures dictionary
        /// iteration order. The returned array indexes positionally match the order of trial names returned by
        /// <see cref="GetTrialNames"/>.
        /// </summary>
        /// <returns>An array of segment lengths in Unity units, one entry per trial.</returns>
        public float[] GetSegmentLengthsUnity()
        {
            Dictionary<string, Cue> cueMap = GetCueByName();
            float cmPerUnit = vrEnvironment.cmPerUnityUnit;
            return trialStructures
                .Values.Select(trial => trial.cueSequence.Sum(cueName => cueMap[cueName].LengthUnity(cmPerUnit)))
                .ToArray();
        }

        /// <summary>
        /// Returns the trial names in the order they appear in the trial_structures dictionary. Used together
        /// with <see cref="GetSegmentLengthsUnity"/> for positional indexing of trials.
        /// </summary>
        /// <returns>An array of trial names matching the trial_structures iteration order.</returns>
        public string[] GetTrialNames()
        {
            return trialStructures.Keys.ToArray();
        }

        /// <summary>Calculates the total length of a single trial's segment in Unity units.</summary>
        /// <param name="trialName">The name of the trial whose segment length to compute.</param>
        /// <returns>The total length of the trial's segment in Unity units.</returns>
        public float GetTrialLengthUnity(string trialName)
        {
            TrialStructure trial = trialStructures[trialName];
            Dictionary<string, Cue> cueMap = GetCueByName();
            float cmPerUnit = vrEnvironment.cmPerUnityUnit;
            return trial.cueSequence.Sum(cueName => cueMap[cueName].LengthUnity(cmPerUnit));
        }
    }
}
