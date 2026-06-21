/// <summary>
/// Provides the StimulusTriggerZone class that manages stimulus delivery based on animal behavior.
///
/// The trigger mechanism is selected by the triggerMode field (set by CreateTask from the trial's
/// trigger_type), not by what stimulus is delivered. Any stimulus type can be paired with any mode, and
/// the experiment driver resolves the appetitive-or-aversive valence from the trial name; this zone reports
/// only the valence-agnostic outcome.
///
/// Each resolved trial publishes exactly one StimulusMessage carrying delivered (whether the physical
/// stimulus fired) and cause (the animal's own behavior or the guidance fallback). Interaction mode delivers
/// on a sensor interaction in the zone; with guidance enabled it also delivers on reaching the guidance zone
/// (or on bare zone entry when no GuidanceZone is present), and it omits a failure outcome when the animal
/// leaves the zone without interacting. Collision mode delivers unconditionally on the boundary crossing. The
/// occupancy modes (OccupancyZone child) resolve on the boundary crossing from whether the animal occupied
/// the zone for the required duration: OccupancyDisarm delivers when occupancy is not met and omits when it
/// is, OccupancyArm does the reverse, and OccupancyTrigger delivers immediately when occupancy is met.
/// Occupancy outcomes report cause guidance when the child OccupancyGuidanceZone fired the brake this lap.
/// </summary>
using Gimbl;
using UnityEngine;

namespace SL.Tasks
{
    /// <summary>
    /// Manages stimulus delivery based on animal behavior within the trigger zone.
    /// The trigger mechanism is selected by the triggerMode field set by CreateTask at generation.
    /// </summary>
    public class StimulusTriggerZone : MonoBehaviour, IResettable
    {
        /// <summary>The cause value published when the animal's own behavior produced the outcome.</summary>
        private const string BehaviorCause = "behavior";

        /// <summary>The cause value published when the guidance fallback produced the outcome.</summary>
        private const string GuidanceCause = "guidance";

        /// <summary>
        /// The trigger mechanism this zone uses. Set at task creation time from the trial's trigger_type.
        /// </summary>
        public TriggerMode triggerMode = TriggerMode.Interaction;

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

        /// <summary>
        /// The child GuidanceZone that supplies the guided fire path in interaction mode, if present.
        /// </summary>
        private GuidanceZone _guidanceZone;

        /// <summary>
        /// The child OccupancyZone that supplies the occupancy state in the occupancy modes, if present.
        /// </summary>
        private OccupancyZone _occupancyZone;

        /// <summary>
        /// The child OccupancyGuidanceZone that reports whether the brake fired this lap, if present.
        /// </summary>
        private OccupancyGuidanceZone _occupancyGuidanceZone;

        /// <summary>The reference to the Task for checking guidance mode state.</summary>
        private Task _task;

        /// <summary>The MQTT channel for publishing stimulus trigger messages carrying the trial name.</summary>
        private MQTTChannel<StimulusMessage> _stimulusTrigger;

        /// <summary>The MQTT channel for receiving interaction-sensor messages.</summary>
        private MQTTChannel _interactionTrigger;

        /// <summary>The cached MeshRenderer used to render the boundary indicator, if attached.</summary>
        private MeshRenderer _boundaryRenderer;

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

            // Finds child zones that supply the guidance and occupancy state for the active mode.
            _guidanceZone = GetComponentInChildren<GuidanceZone>();
            _occupancyZone = GetComponentInChildren<OccupancyZone>();
            _occupancyGuidanceZone = GetComponentInChildren<OccupancyGuidanceZone>();

            // Caches the boundary renderer so per-lap and per-trigger toggles avoid TryGetComponent.
            TryGetComponent(out _boundaryRenderer);

            _stimulusTrigger = new MQTTChannel<StimulusMessage>(MQTTTopics.Stimulus, isListener: false);
            _interactionTrigger = new MQTTChannel(MQTTTopics.Interaction, isListener: true);
            _interactionTrigger.receivedEvent.AddListener(OnInteractionDetected);
        }

        /// <summary>
        /// Updates the zone state each frame, dispatching to the handler for the active trigger mode.
        /// </summary>
        private void Update()
        {
            if (!isActive)
                return;

            switch (triggerMode)
            {
                case TriggerMode.Interaction:
                    UpdateInteractionMode();
                    break;
                case TriggerMode.Collision:
                    UpdateCollisionMode();
                    break;
                case TriggerMode.OccupancyDisarm:
                case TriggerMode.OccupancyArm:
                case TriggerMode.OccupancyTrigger:
                    UpdateOccupancyMode();
                    break;
                default:
                    break;
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

            // Resolves an interaction trial the animal left without interacting as a failure (stimulus
            // omitted, animal's own behavior). The isActive guard ensures this runs only when no delivery
            // path already resolved the trial this lap, keeping the contract at one message per trial.
            if (isActive && triggerMode == TriggerMode.Interaction)
            {
                TriggerStimulus(delivered: false, cause: BehaviorCause);
            }
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
                    TriggerStimulus(delivered: true, cause: BehaviorCause);
                }
            }
            else if (_guidanceZone != null)
            {
                // Delivers when the animal interacts anywhere in the trigger zone.
                if (_inZone && _interactionDetectedInZone)
                {
                    TriggerStimulus(delivered: true, cause: BehaviorCause);
                }
                // Delivers via the guidance fallback when the animal reaches the guidance zone instead.
                else if (_guidanceZone.inZone)
                {
                    TriggerStimulus(delivered: true, cause: GuidanceCause);
                }
            }
            else
            {
                // Delivers via the guidance fallback as soon as the animal enters the stimulus zone.
                if (_inZone)
                {
                    TriggerStimulus(delivered: true, cause: GuidanceCause);
                }
            }
        }

        /// <summary>
        /// Handles collision mode behavior. The animal fires the stimulus unconditionally by crossing the
        /// invisible boundary wall (the root trigger collider), with no sensor or occupancy requirement.
        /// </summary>
        private void UpdateCollisionMode()
        {
            if (_inZone)
            {
                TriggerStimulus(delivered: true, cause: BehaviorCause);
            }
        }

        /// <summary>
        /// Handles the occupancy modes. The child OccupancyZone tracks whether the animal occupied the zone
        /// for the required duration (occupancyMet), and the child OccupancyGuidanceZone reports whether the
        /// brake fired this lap. OccupancyDisarm and OccupancyArm resolve on the boundary crossing, delivering
        /// the stimulus for one occupancy outcome and omitting it for the other; OccupancyTrigger delivers
        /// immediately when occupancy is met and leaves its not-met case for the driver to infer.
        /// </summary>
        private void UpdateOccupancyMode()
        {
            if (_occupancyZone == null)
            {
                return;
            }

            bool occupancyMet = _occupancyZone.occupancyMet;
            bool brakeGuided = _occupancyGuidanceZone != null && _occupancyGuidanceZone.BrakeTriggered;
            string cause = brakeGuided ? GuidanceCause : BehaviorCause;
            switch (triggerMode)
            {
                case TriggerMode.OccupancyDisarm:
                    // The boundary crossing resolves the trial: a still-armed boundary (occupancy not met)
                    // delivers the stimulus, a disarmed boundary (occupancy met) omits it.
                    if (_inZone)
                    {
                        TriggerStimulus(delivered: !occupancyMet, cause: cause);
                    }
                    break;
                case TriggerMode.OccupancyArm:
                    // The boundary crossing resolves the trial: a newly-armed boundary (occupancy met)
                    // delivers the stimulus, a still-disarmed boundary (occupancy not met) omits it.
                    if (_inZone)
                    {
                        TriggerStimulus(delivered: occupancyMet, cause: cause);
                    }
                    break;
                case TriggerMode.OccupancyTrigger:
                    if (occupancyMet)
                    {
                        TriggerStimulus(delivered: true, cause: cause);
                    }
                    break;
                default:
                    break;
            }
        }

        /// <summary>Resolves the trial by publishing its outcome over MQTT, then deactivates the zone.</summary>
        /// <param name="delivered">Determines whether the physical stimulus was delivered or omitted.</param>
        /// <param name="cause">The outcome cause: the animal's own behavior or the guidance fallback.</param>
        private void TriggerStimulus(bool delivered, string cause)
        {
            Debug.Log(
                $"StimulusTriggerZone: Trial '{trialName}' resolved (delivered={delivered}, cause={cause})."
            );
            UpdateBoundaryVisibility(false);
            _stimulusTrigger.Send(
                new StimulusMessage
                {
                    trialName = trialName,
                    delivered = delivered,
                    cause = cause,
                }
            );
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
            if (isActive && _inZone && triggerMode == TriggerMode.Interaction)
            {
                _interactionDetectedInZone = true;
            }
        }

        /// <summary>Wraps a trial's resolved outcome for MQTT transmission on the Stimulus topic.</summary>
        public class StimulusMessage
        {
            /// <summary>The identifier of the trial whose stimulus trigger zone resolved.</summary>
            public string trialName;

            /// <summary>Determines whether the physical stimulus was delivered (true) or omitted (false).</summary>
            public bool delivered;

            /// <summary>The outcome cause: "behavior" (the animal's action) or "guidance" (the fallback).</summary>
            public string cause;
        }
    }
}
