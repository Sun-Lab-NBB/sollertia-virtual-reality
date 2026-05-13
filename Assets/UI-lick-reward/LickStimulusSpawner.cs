/// <summary>
/// Provides the LickStimulusSpawner class that spawns UI indicators for lick and stimulus MQTT events.
/// </summary>
using Gimbl;
using UnityEngine;

namespace SL.UI
{
    /// <summary>
    /// Spawns UI indicator prefabs on a canvas in response to lick and stimulus MQTT messages.
    /// </summary>
    public class LickStimulusSpawner : MonoBehaviour
    {
        /// <summary>The prefab to instantiate when a lick is detected.</summary>
        public GameObject lickPrefab;

        /// <summary>The prefab to instantiate when a stimulus is delivered.</summary>
        public GameObject stimulusPrefab;

        /// <summary>The canvas where UI indicator prefabs will be spawned.</summary>
        public Canvas canvas;

        /// <summary>The MQTT channel for receiving lick detection messages.</summary>
        private MQTTChannel _lick;

        /// <summary>The MQTT channel for receiving stimulus delivery messages.</summary>
        private MQTTChannel _stimulus;

        /// <summary>Determines whether a lick indicator should be shown on the next Update.</summary>
        private bool _showLick = false;

        /// <summary>Determines whether a stimulus indicator should be shown on the next Update.</summary>
        private bool _showStimulus = false;

        /// <summary>Sets up MQTT channels and registers event listeners.</summary>
        private void Start()
        {
            _lick = new MQTTChannel("LickPort/", isListener: true);
            _lick.receivedEvent.AddListener(OnLick);
            _stimulus = new MQTTChannel("Gimbl/Stimulus/", isListener: true);
            _stimulus.receivedEvent.AddListener(OnStimulus);
        }

        /// <summary>Checks for pending indicators and spawns UI prefabs on the main thread.</summary>
        private void Update()
        {
            if (_showLick)
            {
                CreateLickMessage();
            }
            if (_showStimulus)
            {
                CreateStimulusMessage();
            }
        }

        /// <summary>Removes MQTT event listeners when this component is destroyed.</summary>
        private void OnDestroy()
        {
            _lick?.receivedEvent.RemoveListener(OnLick);
            _stimulus?.receivedEvent.RemoveListener(OnStimulus);
        }

        /// <summary>Flags a lick indicator to be shown on the next Update cycle.</summary>
        private void OnLick()
        {
            _showLick = true;
        }

        /// <summary>Flags a stimulus indicator to be shown on the next Update cycle.</summary>
        private void OnStimulus()
        {
            _showStimulus = true;
        }

        /// <summary>Instantiates the lick indicator prefab on the canvas.</summary>
        private void CreateLickMessage()
        {
            _showLick = false;
            Instantiate(lickPrefab, canvas.transform);
        }

        /// <summary>Instantiates the stimulus indicator prefab on the canvas.</summary>
        private void CreateStimulusMessage()
        {
            _showStimulus = false;
            Instantiate(stimulusPrefab, canvas.transform);
        }
    }
}
