/// <summary>
/// Provides the MQTTTopics static class enumerating every MQTT topic literal published or subscribed
/// to by sollertia-unity-tasks.
/// </summary>
/// <remarks>
/// Centralizing the topic strings here makes the publish/subscribe contract auditable from one
/// location and makes future renames or additions a single-edit change. Every topic carries a
/// <c>Direction</c>, <c>Payload</c>, and <c>Callers</c> remark so the routing graph is discoverable
/// without grepping the codebase. Topics are <c>const string</c> rather than <c>static readonly</c>
/// so the C# compiler inlines the value at every call site, matching the literal-string code paths
/// that existed before centralization. Any rename to a topic here must also be applied to the
/// matching publisher/subscriber catalog on sollertia-experiment's side in the same release.
/// </remarks>
namespace Gimbl
{
    /// <summary>
    /// Catalogs every MQTT topic the project publishes or subscribes to.
    /// </summary>
    /// <remarks>
    /// The catalog is intentionally flat — each topic is a single PascalCase identifier with no
    /// hierarchical separators. MQTT brokers treat <c>X</c> and <c>X/</c> as distinct topics, so the
    /// flat convention removes a class of accidental routing mismatches between Unity and external
    /// publishers.
    /// </remarks>
    public static class MQTTTopics
    {
        /// <summary>Session-start lifecycle marker published by Unity when the MQTT client starts.</summary>
        /// <remarks>
        /// Direction: Unity publishes; sollertia-experiment subscribes.
        /// Payload: empty trigger (no body).
        /// Callers (publish): <see cref="MQTTClient"/> via <c>StartSessionAsync</c>.
        /// </remarks>
        public const string SessionStart = "SessionStart";

        /// <summary>Session-stop lifecycle marker published by Unity on application quit.</summary>
        /// <remarks>
        /// Direction: Unity publishes; sollertia-experiment subscribes.
        /// Payload: empty trigger (no body).
        /// Callers (publish): <see cref="MQTTClient"/> via <c>OnApplicationQuit</c>.
        /// </remarks>
        public const string SessionStop = "SessionStop";

        /// <summary>Hardware treadmill movement payload received from the physical rig.</summary>
        /// <remarks>
        /// Direction: Unity listens; sollertia-experiment (or the treadmill driver) publishes.
        /// Payload: <see cref="LinearTreadmill.TreadmillMessage"/> wrapping <c>float movement</c>.
        /// Callers (subscribe): <see cref="LinearTreadmill"/>. <see cref="SimulatedLinearTreadmill"/>
        /// intentionally does not subscribe — the simulated rig drives movement from keyboard input
        /// instead.
        /// </remarks>
        public const string Motion = "Motion";

        /// <summary>Lick-port event indicating the animal licked the spout.</summary>
        /// <remarks>
        /// Direction: bidirectional. Hardware lickports publish from sollertia-experiment;
        /// <see cref="SimulatedLinearTreadmill"/> publishes synthetic licks on the Jump action
        /// (spacebar) for keyboard-only test runs.
        /// Payload: empty trigger (no body).
        /// Callers (subscribe): <see cref="SL.UI.LickStimulusSpawner"/>,
        /// <see cref="SL.Tasks.StimulusTriggerZone"/>.
        /// Callers (publish): <see cref="SimulatedLinearTreadmill"/>.
        /// </remarks>
        public const string Lick = "Lick";

        /// <summary>Stimulus delivery event published when a stimulus trigger zone fires.</summary>
        /// <remarks>
        /// Direction: bidirectional. Unity publishes when a trigger zone fires; sollertia-experiment
        /// subscribes to log the event and command the stimulus hardware. The UI spawner also
        /// subscribes locally to render an on-screen indicator.
        /// Payload: empty trigger (no body).
        /// Callers (publish): <see cref="SL.Tasks.StimulusTriggerZone"/> via <c>TriggerStimulus</c>.
        /// Callers (subscribe): <see cref="SL.UI.LickStimulusSpawner"/>.
        /// </remarks>
        public const string Stimulus = "Stimulus";

        /// <summary>Brake activation request carrying the remaining occupancy duration in milliseconds.</summary>
        /// <remarks>
        /// Direction: Unity publishes; sollertia-experiment subscribes and activates the brake.
        /// Payload: <see cref="SL.Tasks.OccupancyGuidanceZone.TriggerDelayMessage"/> wrapping
        /// <c>uint delayMilliseconds</c>.
        /// Callers (publish): <see cref="SL.Tasks.OccupancyGuidanceZone"/> via
        /// <c>TriggerBrakeActivation</c>.
        /// </remarks>
        public const string Delay = "Delay";

        /// <summary>Request for the active task's flattened cue sequence.</summary>
        /// <remarks>
        /// Direction: Unity listens; sollertia-experiment publishes when it wants the sequence.
        /// Payload: empty trigger (no body).
        /// Callers (subscribe): <see cref="SL.Tasks.Task"/> via <c>OnCueSequenceTrigger</c>, which
        /// replies on <see cref="CueSequence"/>.
        /// </remarks>
        public const string CueSequenceTrigger = "CueSequenceTrigger";

        /// <summary>Flattened cue sequence reply sent in response to <see cref="CueSequenceTrigger"/>.</summary>
        /// <remarks>
        /// Direction: Unity publishes; sollertia-experiment subscribes to receive the byte-encoded
        /// cue sequence covering the entire pre-generated track.
        /// Payload: <see cref="SL.Tasks.Task.SequenceMessage"/> wrapping <c>byte[] cueSequence</c>.
        /// Callers (publish): <see cref="SL.Tasks.Task"/> via <c>OnCueSequenceTrigger</c>.
        /// </remarks>
        public const string CueSequence = "CueSequence";

        /// <summary>Request for the active Unity scene name.</summary>
        /// <remarks>
        /// Direction: Unity listens; sollertia-experiment publishes when it wants the name.
        /// Payload: empty trigger (no body).
        /// Callers (subscribe): <see cref="SL.Tasks.Task"/> via <c>OnSceneNameTrigger</c>, which
        /// replies on <see cref="SceneName"/>.
        /// </remarks>
        public const string SceneNameTrigger = "SceneNameTrigger";

        /// <summary>Active Unity scene name reply sent in response to <see cref="SceneNameTrigger"/>.</summary>
        /// <remarks>
        /// Direction: Unity publishes; sollertia-experiment subscribes to discover which task scene
        /// is currently loaded.
        /// Payload: <see cref="SL.Tasks.Task.SceneNameMessage"/> wrapping <c>string name</c>.
        /// Callers (publish): <see cref="SL.Tasks.Task"/> via <c>OnSceneNameTrigger</c>.
        /// </remarks>
        public const string SceneName = "SceneName";

        /// <summary>Lick-requirement toggle command. <c>value=true</c> enables, <c>value=false</c> disables.</summary>
        /// <remarks>
        /// Direction: Unity listens; sollertia-experiment publishes.
        /// Payload: <see cref="SL.Tasks.Task.BoolMessage"/> wrapping <c>bool value</c>.
        /// Callers (subscribe): <see cref="SL.Tasks.Task"/> via <c>OnRequireLick</c>.
        /// </remarks>
        public const string RequireLick = "RequireLick";

        /// <summary>Wait-requirement toggle command. <c>value=true</c> enables, <c>value=false</c> disables.</summary>
        /// <remarks>
        /// Direction: Unity listens; sollertia-experiment publishes.
        /// Payload: <see cref="SL.Tasks.Task.BoolMessage"/> wrapping <c>bool value</c>.
        /// Callers (subscribe): <see cref="SL.Tasks.Task"/> via <c>OnRequireWait</c>.
        /// </remarks>
        public const string RequireWait = "RequireWait";
    }
}
