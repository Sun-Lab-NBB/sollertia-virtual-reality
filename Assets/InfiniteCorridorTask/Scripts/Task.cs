/// <summary>
/// Provides the Task class that manages the infinite corridor VR environment for mesoscope experiments.
///
/// Controls the generation and cycling of random maze segments, manages animal position
/// within the corridor system, and handles MQTT communication for cue sequences and scene information.
/// </summary>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gimbl;
using SL.Config;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SL.Tasks
{
    /// <summary>
    /// Controls the infinite corridor VR task, managing segment generation, animal positioning, and MQTT
    /// communication.
    /// </summary>
    /// <remarks>
    /// A "cue" refers to a visual pattern displayed on the corridor walls. A "trial" is a named entry in the
    /// task template's trial_structures dictionary; it carries the trial's cue sequence, trigger zone, and
    /// optional transition probabilities. A "segment" is the Unity prefab generated for a trial. A "corridor"
    /// is a grouping of adjacent segments forming a visual unit.
    /// </remarks>
    public class Task : MonoBehaviour
    {
        /// <summary>The sentinel value for <see cref="trackSeed"/> requesting a nondeterministic seed.</summary>
        public const int RandomSeedSentinel = -1;

        /// <summary>The actor (animal) being tracked in the VR environment.</summary>
        [HideInInspector]
        public ActorObject actor;

        /// <summary>
        /// Determines whether the animal must interact with a sensor to trigger the stimulus (guidance toggle).
        /// </summary>
        [HideInInspector]
        public bool requireInteraction = false;

        /// <summary>
        /// Determines whether the animal must wait in the occupancy zone (occupancy guidance mode toggle).
        /// </summary>
        [HideInInspector]
        public bool requireWait = false;

        /// <summary>
        /// The total length of the pre-generated random segment sequence.
        /// Should overestimate the distance the animal will actually travel.
        /// </summary>
        [HideInInspector]
        public float trackLength = 15000f;

        /// <summary>
        /// The seed for random segment generation. A specific seed produces the same cue pattern.
        /// Set to <see cref="RandomSeedSentinel"/> to use a nondeterministic seed.
        /// </summary>
        [HideInInspector]
        public int trackSeed = RandomSeedSentinel;

        /// <summary>The path to the YAML configuration file, relative to Application.dataPath.</summary>
        [HideInInspector]
        public string configPath;

        /// <summary>The current index in the segment sequence array.</summary>
        private int _currentSegmentIndex;

        /// <summary>The array holding the order of randomly generated segments.</summary>
        private int[] _segmentSequenceArray;

        /// <summary>The array holding the flattened cue codes for the entire sequence.</summary>
        private byte[] _cueSequenceArray;

        /// <summary>The MQTT channel that triggers sending the cue sequence.</summary>
        private MQTTChannel _cueSequenceTrigger;

        /// <summary>The MQTT channel for sending the cue sequence data.</summary>
        private MQTTChannel<SequenceMessage> _cueSequenceChannel;

        /// <summary>The name of the currently active Unity scene.</summary>
        private string _sceneName;

        /// <summary>The MQTT channel that triggers sending the scene name.</summary>
        private MQTTChannel _sceneNameTrigger;

        /// <summary>The MQTT channel for sending the scene name data.</summary>
        private MQTTChannel<SceneNameMessage> _sceneNameChannel;

        /// <summary>The MQTT channel that toggles the interaction requirement; payload true enables it.</summary>
        private MQTTChannel<BoolMessage> _requireInteractionChannel;

        /// <summary>The MQTT channel that toggles the wait requirement; payload value of true enables it.</summary>
        private MQTTChannel<BoolMessage> _requireWaitChannel;

        /// <summary>The number of segments visible in each corridor (corridor depth).</summary>
        private int _depth;

        /// <summary>The total number of unique trial types (and the matching segment prefabs).</summary>
        private int _trialCount;

        /// <summary>The loaded task template.</summary>
        private TaskTemplate _template;

        /// <summary>
        /// The ordered array of trial names matching the trial_structures dictionary iteration order.
        /// </summary>
        private string[] _trialNames;

        /// <summary>
        /// The reverse lookup mapping each trial name to its positional index in <see cref="_trialNames"/>.
        /// </summary>
        private Dictionary<string, int> _trialNameToIndex;

        /// <summary>The mapping of cue names to their byte codes.</summary>
        private Dictionary<string, byte> _cueIds;

        /// <summary>The lengths of each segment type in Unity units, indexed positionally by trial.</summary>
        private float[] _segmentLengths;

        /// <summary>The lengths of each cue type in Unity units.</summary>
        private float[] _cueLengths;

        /// <summary>
        /// Maps a base-<see cref="_trialCount"/> encoding of the current corridor's segment indices to its
        /// (x-position, first segment length). Indexed by <see cref="ComputeCorridorKey"/>; the encoding lets
        /// the runtime avoid allocating a string key every frame.
        /// </summary>
        private (float xPosition, float firstSegmentLength)[] _corridorMap;

        /// <summary>The current corridor segment indices.</summary>
        private List<int> _currentSegment;

        /// <summary>
        /// The cached integer corridor key for <see cref="_currentSegment"/>, refreshed only on advance.
        /// </summary>
        private int _currentCorridorKey;

        /// <summary>The cached actor position for updates.</summary>
        private Vector3 _position;

#if UNITY_EDITOR
        /// <summary>Editor-only validation that auto-assigns the actor reference when missing.</summary>
        /// <remarks>
        /// Skips while assemblies are recompiling because Unity batches OnValidate callbacks across every
        /// modified script and a scene-wide find runs once per Task per modified script during a compile.
        /// </remarks>
        private void OnValidate()
        {
            if (UnityEditor.EditorApplication.isCompiling)
            {
                return;
            }
            if (actor == null)
            {
                actor = FindAnyObjectByType<ActorObject>();
            }
        }
#endif

        /// <summary>Initializes the task, loads configuration, and sets up MQTT channels.</summary>
        private void Start()
        {
            if (transform.position != Vector3.zero)
            {
                string message =
                    $"Task is positioned at {transform.position}. Automatically Setting Task position to "
                    + "(0,0,0) for this runtime but it is recommended to permanently set the task position to "
                    + "(0,0,0) in Editor Mode.";
                Debug.LogWarning(message);
                transform.position = Vector3.zero;
            }

            // Strips any leading separator so Path.Combine appends the value to Application.dataPath
            // instead of treating it as an absolute path on Linux/macOS.
            string sanitizedConfigPath = configPath?.TrimStart('/', '\\') ?? string.Empty;
            string globalConfigPath = Path.Combine(Application.dataPath, sanitizedConfigPath);

            if (string.IsNullOrEmpty(sanitizedConfigPath) || !File.Exists(globalConfigPath))
            {
                string message =
                    $"Task: configuration YAML not found. configPath='{configPath}', resolved='{globalConfigPath}'. "
                    + "Disabling Task to prevent runtime errors. Regenerate this task prefab via the "
                    + "CreateTask pipeline or assign a valid template path.";
                Debug.LogError(message);
                enabled = false;
                return;
            }

            try
            {
                _template = ConfigLoader.LoadTemplate(globalConfigPath);
            }
            catch (Exception exception)
            {
                string message =
                    $"Failed to load task template from YAML file '{globalConfigPath}': {exception.Message}. "
                    + "Disabling Task to prevent runtime errors.";
                Debug.LogError(message);
                enabled = false;
                return;
            }

            _trialNames = _template.GetTrialNames();
            _trialCount = _trialNames.Length;
            _trialNameToIndex = new Dictionary<string, int>(_trialCount);
            for (int i = 0; i < _trialCount; i++)
            {
                _trialNameToIndex[_trialNames[i]] = i;
            }
            _cueIds = _template.GetCueNameToCode();
            _segmentLengths = _template.GetSegmentLengthsUnity();
            _cueLengths = _template.GetCueLengthsUnity();
            _depth = _template.vrEnvironment.segmentsPerCorridor;

            // Builds corridor map for teleportation. The outer loop index `i` IS the encoded corridor
            // key for the iteration's segment combination, by construction: the inner loop decomposes
            // `i` into base-_trialCount digits, which is the inverse of <see cref="ComputeCorridorKey"/>.
            int corridorCount = (int)Mathf.Pow(_trialCount, _depth);
            _corridorMap = new (float xPosition, float firstSegmentLength)[corridorCount];

            int[] corridorSegments = new int[_depth];
            float currentCorridorX = 0;
            float corridorXShift = _template.vrEnvironment.CorridorSpacingUnity;

            for (int i = 0; i < corridorCount; i++)
            {
                for (int j = 0; j < _depth; j++)
                {
                    corridorSegments[j] = i / (int)Mathf.Pow(_trialCount, _depth - j - 1) % _trialCount;
                }

                _corridorMap[i] = (currentCorridorX, _segmentLengths[corridorSegments[0]]);
                currentCorridorX += corridorXShift;
            }

            (_segmentSequenceArray, _cueSequenceArray) = GenerateRandomMaze(trackLength, trackSeed);

            _currentSegmentIndex = 0;
            _currentSegment = new List<int>(_segmentSequenceArray.Take(_depth));
            _currentCorridorKey = ComputeCorridorKey(_currentSegment);

            if (actor != null)
            {
                if (_currentCorridorKey >= 0 && _currentCorridorKey < _corridorMap.Length)
                {
                    (float xPosition, float firstSegmentLength) corridorData = _corridorMap[_currentCorridorKey];
                    _position = actor.transform.position;
                    _position.x = corridorData.xPosition;
                    actor.transform.position = _position;
                }
                else
                {
                    Debug.LogError(
                        $"Task: Corridor key '{_currentCorridorKey}' out of bounds [0, {_corridorMap.Length})."
                    );
                }
            }

            _cueSequenceTrigger = new MQTTChannel(MQTTTopics.CueSequenceTrigger, isListener: true);
            _cueSequenceTrigger.receivedEvent.AddListener(OnCueSequenceTrigger);
            _cueSequenceChannel = new MQTTChannel<SequenceMessage>(MQTTTopics.CueSequence, isListener: false);

            _sceneName = SceneManager.GetActiveScene().name;
            _sceneNameTrigger = new MQTTChannel(MQTTTopics.SceneNameTrigger, isListener: true);
            _sceneNameTrigger.receivedEvent.AddListener(OnSceneNameTrigger);
            _sceneNameChannel = new MQTTChannel<SceneNameMessage>(MQTTTopics.SceneName, isListener: false);

            // Sets up the MQTT channel for interaction guidance mode control. The payload's bool value sets
            // the requirement flag directly: true enables (interaction required), false disables (guidance).
            _requireInteractionChannel = new MQTTChannel<BoolMessage>(MQTTTopics.RequireInteraction, isListener: true);
            _requireInteractionChannel.receivedEvent.AddListener(OnRequireInteraction);

            // Sets up the MQTT channel for occupancy guidance mode control. The payload's bool value sets
            // the wait-requirement flag directly: true enables (wait required), false disables (guidance).
            _requireWaitChannel = new MQTTChannel<BoolMessage>(MQTTTopics.RequireWait, isListener: true);
            _requireWaitChannel.receivedEvent.AddListener(OnRequireWait);
        }

        /// <summary>Checks animal position and handles corridor transitions each frame.</summary>
        private void Update()
        {
            if (actor == null)
                return;

            if (_currentCorridorKey < 0 || _currentCorridorKey >= _corridorMap.Length)
            {
                Debug.LogError($"Task: Corridor key '{_currentCorridorKey}' out of bounds [0, {_corridorMap.Length}).");
                return;
            }
            (float xPosition, float firstSegmentLength) corridorData = _corridorMap[_currentCorridorKey];

            _position = actor.transform.position;

            if (_position.z > corridorData.firstSegmentLength)
            {
                _position.z -= corridorData.firstSegmentLength;

                _currentSegmentIndex++;
                if (_currentSegmentIndex <= _segmentSequenceArray.Length - _depth)
                {
                    _currentSegment.RemoveAt(0);
                    _currentSegment.Add(_segmentSequenceArray[_currentSegmentIndex + _depth - 1]);
                    _currentCorridorKey = ComputeCorridorKey(_currentSegment);
                }
                else
                {
                    Debug.LogError("Animal ran through all generated segments.");
                    return;
                }

                if (_currentCorridorKey >= 0 && _currentCorridorKey < _corridorMap.Length)
                {
                    (float xPosition, float firstSegmentLength) newCorridorData = _corridorMap[_currentCorridorKey];
                    _position.x = newCorridorData.xPosition;
                    actor.transform.position = _position;
                }
                else
                {
                    Debug.LogError(
                        $"Task: New corridor key '{_currentCorridorKey}' out of bounds [0, {_corridorMap.Length})."
                    );
                }
            }
        }

        /// <summary>Removes all MQTT event listeners when the component is destroyed.</summary>
        private void OnDestroy()
        {
            _cueSequenceTrigger?.receivedEvent.RemoveListener(OnCueSequenceTrigger);
            _sceneNameTrigger?.receivedEvent.RemoveListener(OnSceneNameTrigger);
            _requireInteractionChannel?.receivedEvent.RemoveListener(OnRequireInteraction);
            _requireWaitChannel?.receivedEvent.RemoveListener(OnRequireWait);
        }

        /// <summary>MQTT callback that sends the cue sequence when requested.</summary>
        private void OnCueSequenceTrigger()
        {
            Debug.Log("Task: Received request for cue sequence.");
            _cueSequenceChannel.Send(new SequenceMessage() { cueSequence = _cueSequenceArray });
        }

        /// <summary>MQTT callback that sends the scene name when requested.</summary>
        private void OnSceneNameTrigger()
        {
            _sceneNameChannel.Send(new SceneNameMessage() { name = _sceneName });
        }

        /// <summary>MQTT callback that applies the interaction-requirement toggle from the message payload.</summary>
        /// <param name="message">The boolean payload; true enables the requirement, false disables it.</param>
        private void OnRequireInteraction(BoolMessage message)
        {
            requireInteraction = message.value;
        }

        /// <summary>MQTT callback that applies the wait-requirement toggle from the message payload.</summary>
        /// <param name="message">The boolean payload; true enables the requirement, false disables it.</param>
        private void OnRequireWait(BoolMessage message)
        {
            requireWait = message.value;
        }

        /// <summary>
        /// Encodes the segment indices of a corridor as a single integer for indexing into
        /// <see cref="_corridorMap"/>. Reads the sequence as digits of a base-<see cref="_trialCount"/> number
        /// so that the corridor-map build loop (which decomposes its outer index back into digits) and the
        /// runtime lookup produce identical integers.
        /// </summary>
        /// <param name="segments">The ordered segment indices that identify a corridor.</param>
        /// <returns>The integer key matching the build-time index in <see cref="_corridorMap"/>.</returns>
        private int ComputeCorridorKey(IList<int> segments)
        {
            int key = 0;
            int count = segments.Count;
            for (int i = 0; i < count; i++)
            {
                key = key * _trialCount + segments[i];
            }
            return key;
        }

        /// <summary>Samples a trial name from a named probability distribution over trial transitions.</summary>
        /// <param name="transitions">The trial-name keyed transition dictionary; values must sum to 1.0.</param>
        /// <param name="random">The random number generator instance.</param>
        /// <returns>The sampled trial name.</returns>
        private static string SampleFromTransitions(Dictionary<string, float> transitions, System.Random random)
        {
            float randomValue = (float)random.NextDouble();
            float cumulative = 0f;
            string lastKey = null;

            foreach (KeyValuePair<string, float> entry in transitions)
            {
                cumulative += entry.Value;
                lastKey = entry.Key;
                if (randomValue < cumulative)
                    return entry.Key;
            }

            return lastKey;
        }

        /// <summary>Generates a random sequence of trials based on the specified length and optional seed.</summary>
        /// <param name="length">The total desired length of the maze sequence in Unity units.</param>
        /// <param name="seed">
        /// The optional seed for the random number generator. Use <see cref="RandomSeedSentinel"/> for a
        /// nondeterministic seed.
        /// </param>
        /// <returns>A tuple containing (trial indices array, flattened cue codes array).</returns>
        private (int[], byte[]) GenerateRandomMaze(float length, int seed)
        {
            float sequenceLength = 0;

            System.Random random = seed == RandomSeedSentinel ? new System.Random() : new System.Random(seed);

            // Estimates the number of segments the loop will append so the backing arrays grow at most once
            // during generation. The estimate uses the shortest segment to overshoot rather than undershoot.
            float minSegmentLength = _segmentLengths.Min();
            int estimatedSegmentCount =
                minSegmentLength > 0f ? Mathf.Max(16, Mathf.CeilToInt(length / minSegmentLength) + _depth) : 16;
            List<int> segmentSequence = new List<int>(estimatedSegmentCount);
            List<byte> cueSequence = new List<byte>(estimatedSegmentCount * 4);

            int choice = random.Next(_trialCount);

            while (sequenceLength < length)
            {
                segmentSequence.Add(choice);

                TrialStructure trial = _template.trialStructures[_trialNames[choice]];
                foreach (string cue in trial.cueSequence)
                {
                    cueSequence.Add(_cueIds[cue]);
                }

                sequenceLength += _segmentLengths[choice];

                // Uses transition probabilities if defined, otherwise uniform random over all trials.
                if (trial.HasTransitions)
                {
                    string nextTrialName = SampleFromTransitions(trial.transitions, random);
                    choice = _trialNameToIndex[nextTrialName];
                }
                else
                {
                    choice = random.Next(_trialCount);
                }
            }

            return (segmentSequence.ToArray(), cueSequence.ToArray());
        }

        /// <summary>Wraps cue sequence data for MQTT transmission.</summary>
        public class SequenceMessage
        {
            /// <summary>The byte array containing the encoded cue sequence for the entire track.</summary>
            public byte[] cueSequence;
        }

        /// <summary>Wraps scene name data for MQTT transmission.</summary>
        public class SceneNameMessage
        {
            /// <summary>The name of the currently active Unity scene.</summary>
            public string name;
        }

        /// <summary>Wraps a single boolean payload for MQTT toggle channels.</summary>
        /// <remarks>
        /// Required because <see cref="UnityEngine.JsonUtility"/> cannot serialize or deserialize bare
        /// primitives at the top level; the wrapper makes the value addressable via the JSON object
        /// <c>{"value": true|false}</c> contract that <see cref="MQTTChannel{TMessage}"/> uses.
        /// </remarks>
        public class BoolMessage
        {
            /// <summary>Determines whether the toggled requirement is enabled.</summary>
            public bool value;
        }
    }
}
