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
        /// Determines whether the animal must lick to trigger the stimulus (lick guidance mode toggle).
        /// </summary>
        [HideInInspector]
        public bool requireLick = false;

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

        /// <summary>The MQTT channel for enabling lick requirement (lick guidance mode off).</summary>
        private MQTTChannel _requireLickTrue;

        /// <summary>The MQTT channel for disabling lick requirement (lick guidance mode on).</summary>
        private MQTTChannel _requireLickFalse;

        /// <summary>The MQTT channel for enabling wait requirement (occupancy guidance mode off).</summary>
        private MQTTChannel _requireWaitTrue;

        /// <summary>The MQTT channel for disabling wait requirement (occupancy guidance mode on).</summary>
        private MQTTChannel _requireWaitFalse;

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
        /// Maps corridor ID string to (x-position, first segment length).
        /// Used for teleporting the animal between corridors.
        /// </summary>
        private Dictionary<string, (float xPosition, float firstSegmentLength)> _corridorMap;

        /// <summary>The current corridor segment indices.</summary>
        private List<int> _currentSegment;

        /// <summary>The cached actor position for updates.</summary>
        private Vector3 _position;

        /// <summary>Validates and auto-assigns the actor reference in the editor.</summary>
        private void OnValidate()
        {
            if (actor == null)
            {
                actor = FindAnyObjectByType<ActorObject>();
            }
        }

        /// <summary>Initializes the task, loads configuration, and sets up MQTT channels.</summary>
        private void Start()
        {
            // Warns if Task is not at origin.
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

            // Loads and validates task template.
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

            // Extracts configuration values.
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

            // Builds corridor map for teleportation.
            // Maps corridor segment combination to (x-position, first segment length).
            _corridorMap = new Dictionary<string, (float xPosition, float firstSegmentLength)>();

            int[] corridorSegments = new int[_depth];
            float currentCorridorX = 0;
            float corridorXShift = _template.vrEnvironment.CorridorSpacingUnity;

            for (int i = 0; i < Mathf.Pow(_trialCount, _depth); i++)
            {
                // Generates segment combination for current corridor index.
                for (int j = 0; j < _depth; j++)
                {
                    corridorSegments[j] = i / (int)Mathf.Pow(_trialCount, _depth - j - 1) % _trialCount;
                }

                _corridorMap[string.Join("-", corridorSegments)] = (
                    currentCorridorX,
                    _segmentLengths[corridorSegments[0]]
                );
                currentCorridorX += corridorXShift;
            }

            // Generates random maze sequence.
            (_segmentSequenceArray, _cueSequenceArray) = GenerateRandomMaze(trackLength, trackSeed);

            // Initializes current segment tracking.
            _currentSegmentIndex = 0;
            _currentSegment = new List<int>(_segmentSequenceArray.Take(_depth));

            // Positions actor at the first corridor.
            if (actor != null)
            {
                string corridorKey = string.Join("-", _currentSegment);
                if (_corridorMap.TryGetValue(corridorKey, out var corridorData))
                {
                    _position = actor.transform.position;
                    _position.x = corridorData.xPosition;
                    actor.transform.position = _position;
                }
                else
                {
                    Debug.LogError($"Task: Corridor key '{corridorKey}' not found in corridor map.");
                }
            }

            // Sets up MQTT channels for cue sequence requests.
            _cueSequenceTrigger = new MQTTChannel("CueSequenceTrigger/", isListener: true);
            _cueSequenceTrigger.receivedEvent.AddListener(OnCueSequenceTrigger);
            _cueSequenceChannel = new MQTTChannel<SequenceMessage>("CueSequence/", isListener: false);

            // Sets up MQTT channels for scene name requests.
            _sceneName = SceneManager.GetActiveScene().name;
            _sceneNameTrigger = new MQTTChannel("SceneNameTrigger/", isListener: true);
            _sceneNameTrigger.receivedEvent.AddListener(OnSceneNameTrigger);
            _sceneNameChannel = new MQTTChannel<SceneNameMessage>("SceneName/", isListener: false);

            // Sets up MQTT channels for lick guidance mode control.
            _requireLickTrue = new MQTTChannel("RequireLick/True/", isListener: true);
            _requireLickTrue.receivedEvent.AddListener(SetRequireLickTrue);

            _requireLickFalse = new MQTTChannel("RequireLick/False/", isListener: true);
            _requireLickFalse.receivedEvent.AddListener(SetRequireLickFalse);

            // Sets up MQTT channels for occupancy guidance mode control.
            _requireWaitTrue = new MQTTChannel("RequireWait/True/", isListener: true);
            _requireWaitTrue.receivedEvent.AddListener(SetRequireWaitTrue);

            _requireWaitFalse = new MQTTChannel("RequireWait/False/", isListener: true);
            _requireWaitFalse.receivedEvent.AddListener(SetRequireWaitFalse);
        }

        /// <summary>Checks animal position and handles corridor transitions each frame.</summary>
        private void Update()
        {
            if (actor == null)
                return;

            string corridorKey = string.Join("-", _currentSegment);
            if (!_corridorMap.TryGetValue(corridorKey, out var corridorData))
            {
                Debug.LogError($"Task: Corridor key '{corridorKey}' not found in corridor map.");
                return;
            }

            _position = actor.transform.position;

            // Checks if animal has traveled through the current segment.
            if (_position.z > corridorData.firstSegmentLength)
            {
                // Teleports animal back to start of corridor.
                _position.z -= corridorData.firstSegmentLength;

                // Advances to next corridor based on future segments.
                _currentSegmentIndex++;
                if (_currentSegmentIndex <= _segmentSequenceArray.Length - _depth)
                {
                    _currentSegment.RemoveAt(0);
                    _currentSegment.Add(_segmentSequenceArray[_currentSegmentIndex + _depth - 1]);
                }
                else
                {
                    Debug.LogError("Animal ran through all generated segments.");
                    return;
                }

                // Teleports to new corridor.
                string newCorridorKey = string.Join("-", _currentSegment);
                if (_corridorMap.TryGetValue(newCorridorKey, out var newCorridorData))
                {
                    _position.x = newCorridorData.xPosition;
                    actor.transform.position = _position;
                }
                else
                {
                    Debug.LogError($"Task: New corridor key '{newCorridorKey}' not found in corridor map.");
                }
            }
        }

        /// <summary>Removes all MQTT event listeners when the component is destroyed.</summary>
        private void OnDestroy()
        {
            _cueSequenceTrigger?.receivedEvent.RemoveListener(OnCueSequenceTrigger);
            _sceneNameTrigger?.receivedEvent.RemoveListener(OnSceneNameTrigger);
            _requireLickTrue?.receivedEvent.RemoveListener(SetRequireLickTrue);
            _requireLickFalse?.receivedEvent.RemoveListener(SetRequireLickFalse);
            _requireWaitTrue?.receivedEvent.RemoveListener(SetRequireWaitTrue);
            _requireWaitFalse?.receivedEvent.RemoveListener(SetRequireWaitFalse);
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

        /// <summary>MQTT callback that enables lick requirement (disables lick guidance mode).</summary>
        private void SetRequireLickTrue()
        {
            requireLick = true;
        }

        /// <summary>MQTT callback that disables lick requirement (enables lick guidance mode).</summary>
        private void SetRequireLickFalse()
        {
            requireLick = false;
        }

        /// <summary>MQTT callback that enables wait requirement (disables occupancy guidance mode).</summary>
        private void SetRequireWaitTrue()
        {
            requireWait = true;
        }

        /// <summary>MQTT callback that disables wait requirement (enables occupancy guidance mode).</summary>
        private void SetRequireWaitFalse()
        {
            requireWait = false;
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

            List<int> segmentSequence = new List<int>();
            List<byte> cueSequence = new List<byte>();

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
    }
}
