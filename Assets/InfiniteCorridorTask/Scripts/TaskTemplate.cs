/// <summary>
/// Provides the TaskTemplate class that defines a VR task template for prefab generation and runtime configuration.
///
/// This class mirrors the Python TaskTemplate class from sollertia-shared-assets, containing
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
    /// <remarks>
    /// Getter results are lazily computed once per template instance and cached in private fields.
    /// The template is expected to be immutable after deserialization, so the caches never need
    /// invalidation; any mutation of <see cref="cues"/>, <see cref="vrEnvironment"/>, or
    /// <see cref="trialStructures"/> after first access will produce stale results.
    /// </remarks>
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

        /// <summary>The cached cue-name to byte-code lookup, built on first access.</summary>
        private Dictionary<string, byte> _cueNameToCodeCache;

        /// <summary>The cached cue-name to Cue lookup, built on first access.</summary>
        private Dictionary<string, Cue> _cueByNameCache;

        /// <summary>The cached cue lengths in Unity units, built on first access.</summary>
        private float[] _cueLengthsUnityCache;

        /// <summary>The cached segment lengths in Unity units, built on first access.</summary>
        private float[] _segmentLengthsUnityCache;

        /// <summary>The cached trial-names array, built on first access.</summary>
        private string[] _trialNamesCache;

        /// <summary>The cached per-trial segment-length lookup in Unity units, built on first access.</summary>
        private Dictionary<string, float> _trialLengthsUnityCache;

        /// <summary>Returns a map of cue name to byte code for MQTT encoding.</summary>
        /// <returns>Maps each cue name to its byte code.</returns>
        public Dictionary<string, byte> GetCueNameToCode()
        {
            return _cueNameToCodeCache ??= cues.ToDictionary(cue => cue.name, cue => (byte)cue.code);
        }

        /// <summary>Returns a map of cue name to Cue.</summary>
        /// <returns>Maps each cue name to its Cue instance.</returns>
        public Dictionary<string, Cue> GetCueByName()
        {
            return _cueByNameCache ??= cues.ToDictionary(cue => cue.name, cue => cue);
        }

        /// <summary>Returns cue lengths in Unity units as an array.</summary>
        /// <returns>Cue lengths in Unity units, indexed to match the cue order.</returns>
        public float[] GetCueLengthsUnity()
        {
            if (_cueLengthsUnityCache == null)
            {
                float cmPerUnit = vrEnvironment.cmPerUnityUnit;
                _cueLengthsUnityCache = cues.Select(cue => cue.LengthUnity(cmPerUnit)).ToArray();
            }
            return _cueLengthsUnityCache;
        }

        /// <summary>
        /// Returns the lengths of each trial's segment in Unity units, ordered by the trial_structures dictionary
        /// iteration order. The returned array indexes positionally match the order of trial names returned by
        /// <see cref="GetTrialNames"/>.
        /// </summary>
        /// <returns>Segment lengths in Unity units, one entry per trial.</returns>
        public float[] GetSegmentLengthsUnity()
        {
            if (_segmentLengthsUnityCache == null)
            {
                Dictionary<string, Cue> cueMap = GetCueByName();
                float cmPerUnit = vrEnvironment.cmPerUnityUnit;
                _segmentLengthsUnityCache = trialStructures
                    .Values.Select(trial => trial.cueSequence.Sum(cueName => cueMap[cueName].LengthUnity(cmPerUnit)))
                    .ToArray();
            }
            return _segmentLengthsUnityCache;
        }

        /// <summary>
        /// Returns the trial names in the order they appear in the trial_structures dictionary. Used together
        /// with <see cref="GetSegmentLengthsUnity"/> for positional indexing of trials.
        /// </summary>
        /// <returns>Trial names in the trial_structures iteration order.</returns>
        public string[] GetTrialNames()
        {
            return _trialNamesCache ??= trialStructures.Keys.ToArray();
        }

        /// <summary>Calculates the total length of a single trial's segment in Unity units.</summary>
        /// <param name="trialName">The name of the trial whose segment length to compute.</param>
        /// <returns>The total length of the trial's segment in Unity units.</returns>
        public float GetTrialLengthUnity(string trialName)
        {
            if (_trialLengthsUnityCache == null)
            {
                Dictionary<string, Cue> cueMap = GetCueByName();
                float cmPerUnit = vrEnvironment.cmPerUnityUnit;
                _trialLengthsUnityCache = trialStructures.ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value.cueSequence.Sum(cueName => cueMap[cueName].LengthUnity(cmPerUnit))
                );
            }
            return _trialLengthsUnityCache[trialName];
        }
    }
}
