# Claude Code Instructions

## Session start behavior

At the beginning of each coding session, before making any code changes, you should build a comprehensive understanding
of the codebase by invoking the `/explore-codebase` skill (`automation` plugin).

This ensures you:
- Understand the Unity project architecture before modifying code
- Follow existing patterns and conventions
- Do not introduce inconsistencies or break the MQTT contract with `sollertia-experiment`

## Autonomy boundaries

This project supports exactly one VR paradigm: the **infinite linear corridor**. The boundary below is about whether
an author-derived recipe exists, not about whether you are capable. You are NOT forbidden from helping past the
boundary — you simply have no deterministic recipe there, so you MUST consult the human supervisor and proceed in a
generative, collaborative mode (co-design, get sign-off, co-implement) rather than executing autonomously.

**Within-corridor work is agent-autonomous.** These extensions have an author-derived recipe in the skills and run
end-to-end through templates and the `McpBridge` relay without human intervention:
- A new corridor task variant — author the YAML template and materialize it with `create_task_tool` (see assets
  plugin `/task-templates` and `/task-prefabs`).
- Selecting any of the five existing `trigger_type` modes per trial (`interaction`, `collision`, `occupancy_disarm`,
  `occupancy_arm`, `occupancy_trigger`).
- Generating the matching corridor scene (bundled into `create_task_tool`), configuring it through
  `Window → Task Parameters` (see `/scene-setup`, `/task-parameters`), inspecting prefabs, and driving Play Mode.

**New trigger zone types are agent-led but recipe-bound — not pure template work.** Authoring a new trigger mode has an
author-derived recipe even when the firing behavior is genuinely novel: `/zone-prefabs` Steps 1–7 plus its worked
examples (a speed-gated interaction reward and a cumulative-occupancy variant), the `/task-generator` pipeline edits,
and the `/library-extension` Python `TriggerType` registration. The recipe holds as long as the new mode is a zone
modifier (subclass an existing zone, or a standalone `IResettable` registered in `ResetZone`) on a copied zone prefab
whose root subclasses `StimulusTriggerZone` and publishes the standard `Stimulus{trialName}` event. This tier authors
C# and hand-edits prefab YAML (`fileID` / GUID bookkeeping), so it carries friction and MUST be verified with
`inspect_prefab_tool` — but it is agent-doable, not an escalation.

**Beyond the recipe, you MUST escalate to the human supervisor.** The items below have no author-derived recipe; treat
them as collaborative, human-supervised, generative work and do NOT attempt them autonomously:
- A new VR paradigm or topology (T-maze, Y-maze, open field, branching or 2D mazes). The corridor invariants are baked
  into `Task.cs` (forward-only Z traversal, single-axis teleport, base-`trialCount` corridor encoding) and
  `CreateTask.cs` (segment concatenation along Z, corridor spacing along X).
- Any change to `Task.cs` runtime traversal mechanics, the corridor encoding, or `CreateTask` corridor assembly.
- A new scene topology or Display rig other than the corridor scene `create_task_tool` copies from
  `ExperimentTemplate.unity`.
- A trigger behavior that cannot be expressed as a zone modifier publishing the standard `Stimulus{trialName}` event —
  one that needs a new MQTT topic, new `Task.cs` runtime mechanics, or geometry outside a single corridor segment. (A
  new trigger *mode* that fits the zone-modifier architecture is recipe-covered — see the tier above.)
- A new `TaskTemplate` / `VREnvironment` field or class — a coordinated two-repo schema change with the Python
  originals in `sollertia-shared-assets` (see Downstream library integration).

**New cue textures hand off cleanly to the user.** You cannot author PNG or other binary texture assets. When a
template needs a texture that is not already under `Assets/InfiniteCorridorTask/Textures/`, you MUST stop and hand the
requirement to the user — state the intended cue `name`, `code`, `length_cm`, and target filename — then let the user
supply the asset and loop you back to finish generation. You MUST NOT let generation dead-end in a
`Failed to load texture` error.

## Style guide compliance

You MUST invoke the appropriate skill before performing ANY of the following tasks:

| Task                                       | Skill to invoke   |
|--------------------------------------------|-------------------|
| Writing or modifying C# code               | `/csharp-style`   |
| Writing or modifying README files          | `/readme-style`   |
| Writing git commit messages                | `/commit`         |
| Writing or modifying skill files / this MD | `/skill-design`   |
| Creating or verifying project structure    | `/project-layout` |

Each skill contains a verification checklist that you MUST complete before submitting any work. Failure to invoke the
appropriate skill results in style violations that block release.

## Cross-referenced library verification

This project depends on `sollertia-shared-assets` (which relays MCP tool calls into the `McpBridge` HTTP listener) and
exchanges MQTT 5.0 traffic with `sollertia-experiment`. Local clones of both libraries typically live alongside this
repository under `/home/cyberaxolotl/Desktop/GitHubRepos/`.

**Before writing code that interacts with a cross-referenced library, you MUST:**

1. **Check for local version**: Look for the library in the parent directory (e.g., `../sollertia-shared-assets/`,
   `../sollertia-experiment/`).

2. **Compare versions**: If a local copy exists, compare its version against the latest release or main branch on
   GitHub:
   - Read the local `pyproject.toml` to get the current version
   - Use `gh api repos/Sun-Lab-NBB/{repo-name}/releases/latest` to check the latest release
   - Alternatively, check the main branch version on GitHub

3. **Handle version mismatches**: If the local version differs from the latest release or main branch, notify the user
   with the following options:
   - **Use online version**: Fetch documentation and API details from the GitHub repository
   - **Update local copy**: The user will pull the latest changes locally before proceeding

4. **Proceed with correct source**: Use whichever version the user selects as the authoritative reference for API
   usage, patterns, and documentation.

**Why this matters**: Skills and documentation may reference outdated APIs. Always verify against the actual library
state to prevent integration errors.

## Available skills

Agentic coverage for this project is distributed across two marketplaces. Install the sollertia marketplace's `unity`
and `assets` plugins together with the ataraxis marketplace's `automation` plugin to make the full skill set available.
The `unity` plugin depends on the `assets` plugin for the backing `slsa mcp` server that drives the `McpBridge` relay.

### Unity Editor skills (sollertia marketplace, `unity` plugin)

| Skill                          | Description                                                                |
|--------------------------------|----------------------------------------------------------------------------|
| `/unity-mcp-environment-setup` | Diagnose the `localhost:8090` `McpBridge` HTTP relay                       |
| `/task-scenes`                 | List, open, and inspect Unity scenes; enumerate Unity assets               |
| `/task-prefabs`                | Generate, inspect, validate, and delete task prefabs from YAML templates   |
| `/zone-prefabs`                | Manufacture new hand-authored trigger zone prefabs by copying and editing  |
| `/task-parameters`             | Read and write the consolidated `Window → Task Parameters` editor surface  |
| `/play-mode`                   | Enter, exit, and query Editor Play Mode                                    |
| `/mqtt-contract`               | Authoritative catalog of every MQTT topic Unity publishes or subscribes to |
| `/task-generator`              | Reference for the `CreateTask` pipeline and hand-authored zone prefabs     |
| `/gimbl-framework`             | Reference for the inlined GIMBL VR framework (Actor, MQTT, Displays)       |
| `/scene-setup`                 | Configure the Display rig, controllers, and UI feedback via Task Parameters|

### Configuration and experiment skills (sollertia marketplace, `assets` plugin)

| Skill                           | Description                                                              |
|---------------------------------|--------------------------------------------------------------------------|
| `/task-templates`               | Author and validate reusable Unity `TaskTemplate` YAMLs                  |
| `/experiment-configuration`     | Author per-project experiment configurations that reference a template   |
| `/library-extension`            | Orchestrate cross-cutting changes to extend the shared-assets vocabulary |
| `/assets-mcp-environment-setup` | Diagnose and resolve `slsa mcp` server connectivity issues               |

The `assets` plugin ships further session, project, and data-management skills; only those relevant to
sollertia-unity-tasks appear above.

### Shared development skills (ataraxis marketplace, `automation` plugin)

| Skill               | Description                                                                           |
|---------------------|---------------------------------------------------------------------------------------|
| `/explore-codebase` | Perform in-depth codebase exploration at session start                                |
| `/csharp-style`     | Apply Sollertia platform C# coding conventions (REQUIRED for C# changes)              |
| `/readme-style`     | Apply Sollertia platform README conventions (REQUIRED for README changes)             |
| `/commit`           | Draft Sollertia platform style-compliant git commit messages                          |
| `/skill-design`     | Generate, update, and verify skill files and this CLAUDE.md                           |
| `/project-layout`   | Apply Sollertia platform project directory structure conventions (C# Unity archetype) |

You MUST invoke `/library-extension` (assets plugin) when adding a new `TriggerType` member or otherwise extending the
shared-assets template vocabulary, because the Python registry parity check on the `slsa mcp` side fails at import time
if any downstream entry is missing. The platform `TriggerType` enum carries all five members (`INTERACTION`,
`COLLISION`, `OCCUPANCY_DISARM`, `OCCUPANCY_ARM`, and `OCCUPANCY_TRIGGER`), and each acquisition system maps only the
subset it supports: a new `TriggerType` member does NOT require a `from_task_template` branch — a system may leave it
unsupported/unmapped. The Mesoscope-VR system's `from_task_template` maps `INTERACTION`
(→ `MesoscopeWaterRewardTrial`) and `OCCUPANCY_DISARM` (→ `MesoscopeGasPuffTrial`), and does not map `collision`,
`occupancy_arm`, or `occupancy_trigger`, so a
Mesoscope-VR config that uses one of those raises a clear "not mapped to a runtime trial class" error. The Unity
counterpart of such a change is captured
in the extension contracts table below.

## MCP server

This project does not host a standalone MCP server. Instead, the `McpBridge` editor plugin
(`Assets/InfiniteCorridorTask/Scripts/Editor/McpBridge.cs`) starts an HTTP listener on `127.0.0.1:8090`, `[::1]:8090`,
and `localhost:8090` when the Unity Editor loads. The backing MCP server is `slsa mcp` from `sollertia-shared-assets`;
its `interfaces/unity_tools.py` module relays each tool call to the bridge over HTTP and surfaces the JSON response back
to the agent.

The bridge dispatches **14 tools** in `McpBridge.Dispatch`. Tools are grouped here by concern but live side by side
in the same dispatcher:

| Category          | Tool                    | Description                                                                       |
|-------------------|-------------------------|-----------------------------------------------------------------------------------|
| Task lifecycle    | `create_task`           | Builds the task prefab and the matching scene from a template in one call         |
| Task lifecycle    | `delete_task`           | Removes the scene + companion + task prefab + every segment prefab for a template |
| Asset inspection  | `inspect_prefab`        | Returns hierarchy, components, and BoxCollider details for a prefab               |
| Asset authoring   | `clone_zone_prefab`     | Clones a base zone prefab into a new trigger-zone prefab (script + field swaps)    |
| Asset inspection  | `list_assets`           | Lists assets by type filter within a search path                                  |
| Asset lifecycle   | `delete_asset`          | Deletes a regenerable non-scene asset (refuses scene paths and protected paths)   |
| Scene management  | `list_scenes`           | Enumerates every `.unity` asset and reports the active scene                      |
| Scene management  | `open_scene`            | Opens a scene with explicit `unsaved_changes` policy                              |
| Scene management  | `inspect_scene`         | Returns the active scene's root hierarchy and dirty flag                          |
| Play Mode control | `enter_play_mode`       | Triggers `EditorApplication.EnterPlaymode`                                        |
| Play Mode control | `exit_play_mode`        | Triggers `EditorApplication.ExitPlaymode`                                         |
| Play Mode control | `get_play_state`        | Returns `playing`, `compiling`, or `edit` plus the active scene name              |
| Task Parameters   | `read_task_parameters`  | Snapshots Actor, MQTT, Display, Camera Mapping, and Task fields                   |
| Task Parameters   | `write_task_parameters` | Applies a subset of Task Parameters fields and returns the new snapshot           |

Project conventions for bridge tools:
- The HTTP listener captures requests on a worker thread and the editor thread drains a `ConcurrentQueue` via
  `EditorApplication.update`. Tool handlers run on the editor thread and may call Unity APIs freely.
- Every response is built through the shared `Ok(payload)` / `Error(message)` helpers, which serialize through
  `MiniJson` and always include a `success` boolean. Match this contract when adding new tools.
- `delete_asset` is bounded by `DeleteAllowedPrefixes` (regenerable non-scene directories) and
  `DeleteProtectedPaths` (hand-authored anchors) declared at the top of `McpBridge.cs`. Adding a regenerable
  directory requires extending the allow list; protecting a new hand-authored asset requires extending the
  protected set. The handler rejects scene paths under `Assets/Scenes/` and points callers at `delete_task` so
  scene cleanup always goes through the companion cascade. Both lists also reject path traversal and absolute
  paths.
- `delete_task` removes the scene + per-scene `savedFullScreenViews` companion + task prefab + every segment
  prefab whose filename begins with the template basename in one atomic call. When the deletion target is the
  active scene, the handler opens `ExperimentTemplate.unity` first so Unity will accept the delete. Cue prefabs
  and cue materials are deliberately preserved because they are shared across templates; use `delete_asset` for
  individual cue cleanup. Add any future per-scene companion to `McpBridge.TryDeleteScenePerSceneCompanions` in
  the same change that introduces it.
- `read_task_parameters` and `write_task_parameters` share a single `AcquireSceneComponents` walk per request so reads
  and writes operate on a consistent snapshot of the active scene.

For bridge connectivity issues (Editor not running, port 8090 not reachable), invoke `/unity-mcp-environment-setup`.
For backing `slsa mcp` issues, invoke `/assets-mcp-environment-setup`.

## Downstream library integration

This project is one corner of the Sollertia data-acquisition triangle. Changes to MQTT topics, YAML schema, or the
bridge surface ripple through the other two libraries:

- **sollertia-experiment** (acquisition runtime). The MQTT counterparty for every topic in `MQTTTopics`. Owns the
  publish side of `CueSequenceTrigger`, `SceneNameTrigger`, `RequireInteraction`, `RequireWait`, `Motion`, and the
  hardware side of `Interaction`. Subscribes to `SessionStart`, `SessionStop`, `Stimulus`, `Delay`, `CueSequence`, and
  `SceneName`. Topic renames here require an in-lockstep update on the experiment side; the `/mqtt-contract` skill is
  the canonical index for both ends.
- **sollertia-shared-assets** (configuration schema and MCP relay). Owns the Python `TaskTemplate` (a `YamlConfig`)
  plus the `Cue`, `TrialStructure`, and `VREnvironment` schema dataclasses it composes; the C# classes under
  `Assets/InfiniteCorridorTask/Scripts/` mirror that schema. `interfaces/unity_tools.py` is the HTTP client for
  `McpBridge`. Adding a new bridge tool requires a matching `@mcp.tool()` wrapper in `unity_tools.py`. Schema changes
  (a new YAML field) must land in both repositories before the templates that use them parse successfully.

You MUST treat the C# `TaskTemplate`, `Cue`, `TrialStructure`, and `VREnvironment` classes as a mirror of their Python
originals. When the Python schema gains a field, update the C# class with a matching `[Serializable]` field; the YAML
deserializer is configured for `UnderscoredNamingConvention`, so the C# member name must be the camelCase counterpart
of the underscored YAML key (e.g., `cue_offset_cm` becomes `cueOffsetCm`).

## Project context

This is **sollertia-unity-tasks**, a Unity 6 C# project that produces VR behavioral tasks for the Sollertia mesoscope
data-acquisition platform. It is part of the Sollertia AI-assisted scientific data acquisition and processing platform,
built on the Ataraxis framework, and developed in the Sun (NeuroAI) lab at Cornell University. Tasks are infinite linear
corridors built from prefabricated visual cue segments and driven over MQTT 5.0 by `sollertia-experiment`.

### Key areas

| Directory                                     | Purpose                                                                |
|-----------------------------------------------|------------------------------------------------------------------------|
| `Assets/InfiniteCorridorTask/Scripts/`        | Runtime C# (`Task`, zones, `ConfigLoader`, schema mirror classes)      |
| `Assets/InfiniteCorridorTask/Scripts/Editor/` | `CreateTask`, `McpBridge`, `TaskEditor`, `MiniJson`                    |
| `Assets/InfiniteCorridorTask/Configurations/` | YAML task templates                                                    |
| `Assets/InfiniteCorridorTask/Cues/`           | Generated cue prefabs (length-suffixed, shared across templates)       |
| `Assets/InfiniteCorridorTask/Prefabs/`        | Hand-authored zone prefabs and generated segment prefabs               |
| `Assets/InfiniteCorridorTask/Tasks/`          | Generated task prefabs (one per template)                              |
| `Assets/InfiniteCorridorTask/Materials/`      | Generated cue materials and the canonical `_CueShaderReference.mat`    |
| `Assets/InfiniteCorridorTask/Textures/`       | Cue textures referenced by YAML templates                              |
| `Assets/UI-lick-reward/`                      | On-screen lick and stimulus feedback canvas                            |
| `Assets/Gimbl/Scripts/`                       | Inlined GIMBL runtime (Actor, Controllers, Displays, MQTT)             |
| `Assets/Gimbl/Editor/`                        | `MainWindow` Task Parameters editor window                             |
| `Assets/Scenes/`                              | `ExperimentTemplate.unity` plus per-task generated scenes              |
| `Assets/Plugins/`                             | Inlined `MQTTnet.dll` and `YamlDotNet.dll`                             |

### Architecture

- **Schema mirror**: `TaskTemplate`, `Cue`, `TrialStructure`, and `VREnvironment` under
  `Assets/InfiniteCorridorTask/Scripts/` mirror the Python `YamlConfig` classes in `sollertia-shared-assets`.
  `ConfigLoader.LoadTemplate` deserializes via `YamlDotNet` using `UnderscoredNamingConvention` and validates cue
  codes, the trial-name pattern `^[A-Za-z0-9_]+$`, the `trigger_type` literal set (`interaction`, `collision`,
  `occupancy_disarm`, `occupancy_arm`, and `occupancy_trigger`), per-trial `cue_sequence` uniqueness within the
  template, a positive `occupancy_duration_ms`, transition-target existence, and the per-trial transition-probability
  sum. The per-mode geometric zone validation (`collision` checks only `stimulus_location`; `occupancy_trigger` only
  the trigger zone; the others the zone, boundary, and their ordering) lives in the shared-assets Python `TaskTemplate`,
  not in `ConfigLoader`.
- **Task runtime**: `Task` (`Assets/InfiniteCorridorTask/Scripts/Task.cs`) is a `MonoBehaviour` attached to the
  generated task prefab. `Start` loads the YAML, builds a `_corridorMap` keyed by a base-`trialCount` integer encoding
  of the current segment combination, pre-generates the random maze sequence with an optional seed, and opens MQTT
  channels for cue sequence requests, scene name requests, and interaction / wait toggles. `Update` checks the actor's
  local Z position against the current corridor's first-segment length and teleports the actor to the next corridor when
  the segment is traversed.
- **Zone composition**: `StimulusTriggerZone` carries a `TriggerMode` enum field (`Interaction`, `Collision`,
  `OccupancyDisarm`, `OccupancyArm`, `OccupancyTrigger`) set by `CreateTask` from the trial's `trigger_type`, and
  dispatches on this enum. It publishes
  `StimulusMessage { trialName }` on the `Stimulus` topic when it fires (its `trialName` field is set by `CreateTask` at
  generation, so the stimulus id equals the trial name); every mode publishes the same `Stimulus{trialName}` event and
  adds no MQTT topics. `collision` crosses an invisible boundary wall (a thin collider at
  `stimulus_location`) and fires unconditionally — no sensor, no occupancy — keeping the
  `showStimulusCollisionBoundary` visibility toggle. `occupancy_disarm` fires on a boundary collision while occupancy is
  NOT met; `occupancy_arm` is its inverse, where occupying the zone ARMS the boundary and colliding with the now-armed
  boundary (occupancy MET) fires; `occupancy_trigger` fires immediately once the required occupancy duration elapses, no
  boundary collision. `OccupancyZone` exposes a generic `occupancyMet` signal; the
  parent `StimulusTriggerZone` applies the per-mode firing rule. All three occupancy modes keep the occupancy-guidance
  brake: `OccupancyGuidanceZone` lives under `OccupancyZone` and publishes `Delay` (carrying the remaining occupancy
  duration in milliseconds) when the animal enters in guidance mode. `ResetZone` discovers every `IResettable` in the
  scene at `Start` and resets state on each lap.
- **CreateTask pipeline**: `CreateTask.CreateFromTemplate` runs a cross-template cue-texture preflight, regenerates
  every segment prefab the template owns, reuses or rebuilds cue prefabs keyed by `(name, lengthCm)`, instantiates the
  full corridor combination tree, places trigger and reset zones according to each trial's `trigger_type`, and saves a
  task prefab at `Assets/InfiniteCorridorTask/Tasks/{templateName}.prefab`. The dispatch covers all five
  `trigger_type` literals without adding any new prefab files: `PlaceCollisionZone` reuses `StimulusTriggerZone.prefab`
  (stripping its `GuidanceRegion` child and setting the root collider as a thin wall at `stimulus_location`), while
  `occupancy_arm` and `occupancy_trigger` reuse `OccupancyTriggerZone.prefab` (CreateTask only sets the occupancy
  sub-mode), so existing tasks rebuild identically. The Editor menu wraps this with a scene
  copy from `ExperimentTemplate.unity` and a `MainWindow.EnsureControllers` call that auto-adds both the hardware and
  the simulated linear treadmill controllers.
- **Task Parameters window**: `MainWindow` (`Assets/Gimbl/Editor/MainWindow.cs`) is a single consolidated editor window
  registered under `Window → Task Parameters` that exposes the Actor, MQTT, Display, Camera Mapping, and Task fields
  for the active scene. `TaskEditor` replaces the default `Task` Inspector with a HelpBox pointing at this window so
  every task field is configured through `MainWindow`, not the Inspector.
- **MQTT client**: `MQTTClient` (`Assets/Gimbl/Scripts/MQTT/MQTTClient.cs`) wraps `MQTTnet` in MQTT 5.0 mode
  (`MqttProtocolVersion.V500`), loads broker IP and port from `EditorPrefs`, and falls back to `127.0.0.1:1883` when
  unset. `MQTTChannel<TMessage>` deserializes JSON payloads via `UnityEngine.JsonUtility`. When the broker is
  unreachable, `MQTTClient.Publish` routes messages in-process so keyboard-only test runs still reach local subscribers
  (for example, `LickStimulusSpawner`).
- **HTTP MCP relay**: `McpBridge` is an `[InitializeOnLoad]` static class that drains an `HttpListener` queue on
  `EditorApplication.update`, deserializes the JSON request via `MiniJson`, dispatches to one of 14 tool handlers, and
  returns a JSON response built by `Ok(...)` or `Error(...)`. The relay surface is owned by this repository; the
  Python wrapper lives in `sollertia-shared-assets/src/sollertia_shared_assets/interfaces/unity_tools.py`.

### Extension contracts

The project exposes six concentrated extension points. Each one has a matching skill and, where applicable, a Python
counterpart in `sollertia-shared-assets`.

| Extension                | Touch points                                                                  | Skill              |
|--------------------------|-------------------------------------------------------------------------------|--------------------|
| New task template        | YAML in `Configurations/`; `/task-prefabs` to materialize cues and segments   | `/task-templates`  |
| New cue texture          | PNG in `Textures/`; reference it from a YAML `texture` field                  | `/task-templates`  |
| New trigger zone type    | Zone script + prefab + `ConfigLoader` literal + `CreateTask` branch + Python  | `/zone-prefabs`    |
| New MQTT topic           | `MQTTTopics` constant + publisher and subscriber on both Unity and experiment | `/mqtt-contract`   |
| New `McpBridge` tool     | `Dispatch` case + handler method + `@mcp.tool()` wrapper in `unity_tools.py`  | n/a (manual)       |
| New treadmill controller | `ControllerObject` subclass + `ControllerTypes` enum entry                    | `/gimbl-framework` |

Detailed checklists for the non-trivial extensions:

- **New task template**: Place the YAML under `Assets/InfiniteCorridorTask/Configurations/` using the
  `ProjectAbbreviation_TaskDescription.yaml` naming convention. The header must include `Project`, `Purpose`, `Layout`,
  and `Related` fields as YAML comments. Run `create_task_tool` from `/task-prefabs` to materialize the cue, segment,
  and task prefabs together with the matching scene, then `inspect_prefab_tool` from the same skill to confirm the
  generated hierarchy matches the template.
- **New trigger zone type**: Author the new modifier `MonoBehaviour` under
  `Assets/InfiniteCorridorTask/Scripts/` (implementing `IResettable` if it holds per-lap state). Invoke
  `/zone-prefabs` to copy the closest canonical template (`StimulusTriggerZone.prefab` for interaction or collision
  modes or `OccupancyTriggerZone.prefab` for the occupancy_disarm, occupancy_arm, or occupancy_trigger modes), swap the
  modifier script GUIDs, rename regions, and override
  field defaults; the skill owns the YAML-edit workflow and the `inspect_prefab_tool` validation step. Then wire the
  new prefab into the runtime: add a literal branch to `ConfigLoader.ValidateTemplate`; extend
  `McpBridge.DeleteProtectedPaths` to protect the new prefab; add a `Place...Zone` helper in `CreateTask.cs` and
  dispatch to it from `BuildSegmentPrefabs` (see `/task-generator`); invoke `/library-extension` (assets plugin) for
  the Python `TriggerType` registry update and the import-time parity check.
- **New MQTT topic**: Add a `public const string` constant to `Assets/Gimbl/Scripts/MQTT/MQTTTopics.cs` with
  `Direction`, `Payload`, and `Callers` remarks; subscribe or publish from the appropriate runtime script; coordinate a
  matching publisher or subscriber in `sollertia-experiment` in the same release; update the `/mqtt-contract` skill
  catalog so both repositories share a single source of truth.
- **New `McpBridge` tool**: Add a new switch case in `McpBridge.Dispatch`; implement a
  `private static string ToolName(Dictionary<string, object> args)` method that returns `Ok(...)` or `Error(...)`;
  if the tool reads or writes scene state, fold it into `AcquireSceneComponents` and `BuildSnapshot` so
  `read_task_parameters` keeps reflecting the full surface; add a wrapper `@mcp.tool()` function under
  `sollertia-shared-assets/src/sollertia_shared_assets/interfaces/unity_tools.py`; update this CLAUDE.md's MCP server
  table and the README's MCP Bridge table in the same change.
- **New treadmill controller**: Subclass `ControllerObject`; if the new controller adds an MQTT subscription, hide
  `Start` per the `LinearTreadmill` / `SimulatedLinearTreadmill` non-chaining contract documented inline in
  `LinearTreadmill.cs`; add an entry to the `ControllerTypes` enum (the only required registry — `MainWindow` resolves
  the runtime type via reflection from the enum name); add a display-name case to `MainWindow.BuildControllerSpecs`
  if the enum-to-display mapping is not the default `ToString` value.

### Code standards

- Unity `6000.3.15f1` (Unity 6); the project compiles against the `.NET 4.x` profile shipped with Unity.
- Apache 2.0 licensed; the license string lives in `LICENSE`.
- 120 character line limit enforced by CSharpier (`.csharpierrc.yaml`); naming, brace style, and spacing enforced by
  `.editorconfig`.
- Allman brace style; `_camelCase` private fields; PascalCase public properties and methods; camelCase Inspector
  fields (for example, `public bool requireInteraction;`); XML documentation on every public and private member.
- See `/csharp-style` for the complete conventions and verification checklist.

### Project-specific conventions

- **Hand-authored vs generated assets**: `Padding.prefab`, `ResetZone.prefab`, `StimulusTriggerZone.prefab`,
  `OccupancyTriggerZone.prefab`, `Materials/_CueShaderReference.mat`, `Materials/Floor.mat`, `Materials/Wall.mat`,
  `Materials/TargetMat.mat`, and `Scenes/ExperimentTemplate.unity` are hand-authored. Everything under `Cues/`, every
  segment prefab under `Prefabs/`, every `Cue_*_*cm.mat` material under `Materials/`, every prefab under `Tasks/`, and
  every scene other than `ExperimentTemplate.unity` is generated by `CreateTask`. All nine hand-authored assets are
  protected from `delete_asset` and `delete_task` by `McpBridge.DeleteProtectedPaths`; you MUST NOT remove entries from 
  that list to weaken the protections. Any new asset that the CreateTask pipeline or a generated prefab references by 
  hardcoded path or serialized link must be added to the protected set in the same change that introduces the reference.
- **Cue identity**: Cue prefabs and materials are keyed by `(cue.name, cue.length_cm)` and shared across templates.
  `CreateTask.ValidateCueDefinitionsAcrossTemplates` runs before any mutation and refuses to generate when two
  templates declare the same `(name, length)` pair with different textures. Resolve the conflict by renaming the cue,
  changing its length, or unifying the textures; do NOT bypass the preflight.
- **Segment regeneration**: `CreateTask.CleanGeneratedSegments` deletes every segment prefab the template owns before
  each generation pass so trial-parameter edits never produce stale geometry under an unchanged
  `TemplateName_TrialName` filename. Cue prefabs and materials are preserved across runs; only segments are
  always-regenerate.
- **MQTT topics**: Topics are flat PascalCase identifiers with no hierarchical separators, declared as
  `public const string` in `MQTTTopics.cs`. Match this convention when adding new topics, and update both the Unity
  publisher / subscriber list and the `sollertia-experiment` counterpart in the same release.
- **Inspector vs Parameters window**: The `Task` component's public fields are `[HideInInspector]` and `TaskEditor`
  replaces the default Inspector with a pointer to `Window → Task Parameters`. Configure every task field through
  `MainWindow`, not the Inspector.
- **MQTT 5.0 only**: The client connects with `MqttProtocolVersion.V500`. Brokers must accept MQTT 5.0 connections
  (Mosquitto `2.0+`). This matches the `sollertia-experiment` MQTT runtime.

### Workflow guidance

**Authoring a new task template:**

1. Invoke `/task-templates` (assets plugin) for the YAML schema and file-naming convention.
2. Place the YAML under `Assets/InfiniteCorridorTask/Configurations/` with the `Project / Purpose / Layout / Related`
   header comments.
3. Invoke `/task-prefabs` and run `create_task_tool` to materialize the cue, segment, and task prefabs together with
   the matching scene. The tool refuses to overwrite an existing scene; pair `delete_task_tool` → `create_task_tool`
   for a regeneration cycle.
4. Run `inspect_prefab_tool` from the same skill to spot-check the generated prefab against the template's cue and
   trial counts.

**Modifying a runtime zone or `Task.cs`:**

1. Invoke `/csharp-style` and, when the change touches MQTT, `/mqtt-contract`.
2. Read the affected script under `Assets/InfiniteCorridorTask/Scripts/`.
3. Preserve the `IResettable` contract on any zone that needs per-lap state reset.
4. Run CSharpier (`csharpier .`) before committing.

**Modifying the `CreateTask` pipeline or `McpBridge`:**

1. Invoke `/task-generator` for the prefab-generation pipeline; read `McpBridge.cs` directly for the relay surface.
2. Keep the cross-template cue-texture preflight intact; new branches must run after the preflight, not before.
3. New `delete_asset` paths require additions to `DeleteAllowedPrefixes`; new hand-authored assets require
   additions to `DeleteProtectedPaths`. Updating one without the other leaves the bridge unsafe.
4. For new tools, also update
   `sollertia-shared-assets/src/sollertia_shared_assets/interfaces/unity_tools.py`.

**Adding or modifying MQTT topics:**

1. Invoke `/mqtt-contract`. The skill is the source of truth for direction, payload shape, and counterparties.
2. Add or rename the constant in `Assets/Gimbl/Scripts/MQTT/MQTTTopics.cs` and update every publisher and subscriber
   that references it. The `Direction`, `Payload`, and `Callers` remarks on each constant must stay accurate.
3. Open an issue or matching PR on `sollertia-experiment` so the counterpart subscribes or publishes under the same
   name in the same release.

**Reading or writing Task Parameters programmatically:**

1. Invoke `/task-parameters`. The skill owns `read_task_parameters_tool` and `write_task_parameters_tool` and is the
   sole programmatic entry point for the Actor / MQTT / Display / Camera Mapping / Task fields (the Inspector is
   replaced by a HelpBox pointing at `Window → Task Parameters`).
2. Always read the current snapshot before writing — the response includes `options` (the allow-list for each enum
   field) and `visibility` (whether each conditionally-rendered control is currently rendered). Writes that violate
   either are rejected by the bridge with a descriptive error.
3. Editor-time writes to `task.require_interaction` / `task.require_wait` are zone-gated; for mid-run toggles, publish
   on the `RequireInteraction` / `RequireWait` MQTT topics instead (see `/mqtt-contract`).

**Running CSharpier and the Editor generation flow:**

```bash
csharpier .                       # Format every C# file under the repository
csharpier --check .               # Verify formatting without modifying (CI mode)
```

The Editor menu `CreateTask → New Task` invokes the full generation pipeline (template selection → prefab build →
scene copy → save). The MCP path is equivalent: a single `create_task_tool(template_name=…)` call produces both the
task prefab and the matching scene. Both flows share `CreateTask.CreateFromTemplate` and
`CreateTask.CreateSceneFromTemplate`, so the agentic and manual paths produce byte-equivalent assets.
