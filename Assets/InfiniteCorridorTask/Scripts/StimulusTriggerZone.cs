/// <summary>
/// Provides the StimulusTriggerZone class that manages stimulus delivery based on animal behavior.
///
/// Supports two trigger modes determined by the presence of child zones. The trigger mode specifies
/// how a stimulus is triggered, not what stimulus is delivered. Any stimulus type can be paired with
/// either trigger mode; the experiment driver resolves the outcome from the trial name this zone
/// publishes when it fires.
///
/// In interaction mode (with GuidanceZone child), when guidance is disabled the animal must engage an
/// interaction sensor inside the zone to trigger the stimulus. When guidance is enabled, the stimulus
/// is delivered when the animal reaches the GuidanceZone.
///
/// In occupancy mode (with OccupancyZone child), the animal must occupy the zone for the required
/// duration to disarm the boundary. Boundary collision triggers stimulus only when the boundary is armed.
/// </summary>
using Gimbl;
using UnityEngine;

namespace SL.Tasks
{
    /// <summary>
    /// Manages stimulus delivery based on animal behavior within the trigger zone.
    /// The trigger mode is determined by the presence of child GuidanceZone or OccupancyZone components.
    /// </summary>
    public class StimulusTriggerZone : MonoBehaviour, IResettable
    {
        /// <summary>
        /// Determines whether the stimulus boundary should be visible when this zone is active.
        /// Set at task creation time from the task template's showStimulusCollisionBoundary per trial type.
        /// </summary>
        public bool showBoundary = false;

        /// <summary>
        /// Determines whether this zone is active (only triggers once per lap). Reset by ResetZone.
        /// </summary>
        public bool isActive = true;

        /// <summary>
        /// The stimulus identifier published on the Stimulus topic when this zone fires. Equals the
        /// owning trial's name and is set at task creation time. The experiment driver joins on it to
        /// resolve the stimulus outcome.
        /// </summary>
        public string trialName = "";

        /// <summary>Determines whether the animal is currently inside this trigger zone.</summary>
        private bool _inZone = false;

        /// <summary>Determines whether an interaction was detected while the animal was in the zone.</summary>
        private bool _interactionDetectedInZone = false;

        /// <summary>The child GuidanceZone that determines interaction mode behavior, if present.</summary>
        private GuidanceZone _guidanceZone;

        /// <summary>The child OccupancyZone that determines occupancy mode behavior, if present.</summary>
        private OccupancyZone _occupancyZone;

        /// <summary>The reference to the Task for checking guidance mode state.</summary>
        private Task _task;

        /// <summary>The MQTT channel for publishing stimulus trigger messages carrying the trial name.</summary>
        private MQTTChannel<StimulusMessage> _stimulusTrigger;

        /// <summary>The MQTT channel for receiving interaction-sensor messages.</summary>
        private MQTTChannel _interactionTrigger;

        /// <summary>The cached MeshRenderer used to render the boundary indicator, if attached.</summary>
        private MeshRenderer _boundaryRenderer;

        /// <summary>
        /// Determines whether this zone operates in occupancy mode based on presence of OccupancyZone child.
        /// </summary>
        private bool IsOccupancyMode => _occupancyZone != null;

        /// <summary>Initializes the zone by finding child zones and setting up MQTT channels.</summary>
        private void Start()
        {
            _task = FindAnyObjectByType<Task>();
            if (_task == null)
            {
                Debug.LogError($"StimulusTriggerZone ({gameObject.name}): No Task found in scene.");
                enabled = false;
                return;
            }

            // Finds child zones that determine behavior mode.
            _guidanceZone = GetComponentInChildren<GuidanceZone>();
            _occupancyZone = GetComponentInChildren<OccupancyZone>();

            // Caches the boundary renderer so per-lap and per-trigger toggles avoid TryGetComponent.
            TryGetComponent(out _boundaryRenderer);

            // Sets up MQTT channels.
            _stimulusTrigger = new MQTTChannel<StimulusMessage>(MQTTTopics.Stimulus, isListener: false);
            _interactionTrigger = new MQTTChannel(MQTTTopics.Interaction, isListener: true);
            _interactionTrigger.receivedEvent.AddListener(OnInteractionDetected);
        }

        /// <summary>Updates the zone state each frame, handling stimulus trigger logic based on mode.</summary>
        private void Update()
        {
            if (!isActive)
                return;

            if (IsOccupancyMode)
            {
                UpdateOccupancyMode();
            }
            else
            {
                UpdateInteractionMode();
            }
        }

        /// <summary>Sets the zone state to active when the animal enters the trigger zone collider.</summary>
        /// <param name="other">The collider that entered the trigger zone.</param>
        private void OnTriggerEnter(Collider other)
        {
            _inZone = true;
        }

        /// <summary>Sets the zone state to inactive when the animal exits the trigger zone collider.</summary>
        /// <param name="other">The collider that exited the trigger zone.</param>
        private void OnTriggerExit(Collider other)
        {
            _inZone = false;
        }

        /// <summary>Removes MQTT event listeners when the component is destroyed.</summary>
        private void OnDestroy()
        {
            _interactionTrigger?.receivedEvent.RemoveListener(OnInteractionDetected);
        }

        /// <summary>Resets the zone state for a new lap.</summary>
        /// <remarks>Invoked by ResetZone when the animal enters the reset zone.</remarks>
        public void ResetState()
        {
            isActive = true;
            _interactionDetectedInZone = false;
            _inZone = false;
            UpdateBoundaryVisibility(showBoundary);
        }

        /// <summary>Toggles the cached boundary renderer, no-op when the renderer is absent.</summary>
        /// <param name="visible">The desired renderer enabled state.</param>
        private void UpdateBoundaryVisibility(bool visible)
        {
            if (_boundaryRenderer != null)
            {
                _boundaryRenderer.enabled = visible;
            }
        }

        /// <summary>
        /// Handles interaction mode behavior. When guidance is disabled, the animal must engage an
        /// interaction sensor in the zone. When guidance is enabled with a GuidanceZone, the animal can
        /// interact in the zone or reach the guidance zone. When guidance is enabled without a
        /// GuidanceZone, the stimulus triggers on zone entry.
        /// </summary>
        private void UpdateInteractionMode()
        {
            if (_task.requireInteraction)
            {
                if (_inZone && _interactionDetectedInZone)
                {
                    TriggerStimulus();
                }
            }
            else if (_guidanceZone != null)
            {
                // Animal can receive stimulus by interacting anywhere in the trigger zone.
                if (_inZone && _interactionDetectedInZone)
                {
                    TriggerStimulus();
                }
                // Or if animal reaches the guidance zone, delivers the stimulus anyway.
                else if (_guidanceZone.inZone)
                {
                    TriggerStimulus();
                }
            }
            else
            {
                // Animal gets stimulus as soon as it enters the stimulus zone.
                if (_inZone)
                {
                    TriggerStimulus();
                }
            }
        }

        /// <summary>
        /// Handles occupancy mode behavior. The animal must occupy the OccupancyZone for the required
        /// duration to disarm the boundary. Boundary collision only triggers stimulus when the boundary
        /// is armed. In guidance mode, occupancy failure triggers movement blocking via MQTT.
        /// </summary>
        private void UpdateOccupancyMode()
        {
            // Boundary collision triggers stimulus only when boundary is armed (occupancy requirement not met).
            if (_inZone && _occupancyZone != null && !_occupancyZone.boundaryDisarmed)
            {
                TriggerStimulus();
            }
        }

        /// <summary>Triggers the stimulus, hides the boundary, and publishes the trial name over MQTT.</summary>
        private void TriggerStimulus()
        {
            Debug.Log($"StimulusTriggerZone: Stimulus triggered for trial '{trialName}'.");
            UpdateBoundaryVisibility(false);
            _stimulusTrigger.Send(new StimulusMessage { trialName = trialName });
            isActive = false;
            _interactionDetectedInZone = false;
        }

        /// <summary>
        /// Records that an interaction occurred while in the zone.
        /// Only relevant in interaction mode when the zone is active.
        /// </summary>
        private void OnInteractionDetected()
        {
            Debug.Log("StimulusTriggerZone: Interaction detected.");
            if (isActive && _inZone && !IsOccupancyMode)
            {
                _interactionDetectedInZone = true;
            }
        }

        /// <summary>Wraps the firing trial's name for MQTT transmission on the Stimulus topic.</summary>
        public class StimulusMessage
        {
            /// <summary>The identifier of the trial whose stimulus trigger zone fired.</summary>
            public string trialName;
        }
    }
}
