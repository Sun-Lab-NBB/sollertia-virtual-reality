# sollertia-unity-tasks

Provides assets for creating and executing Virtual Reality (VR) tasks for Sollertia platform data acquisition systems.

[![C#](https://tinyurl.com/bdd689s9)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![Unity](https://img.shields.io/badge/Unity-6000.3.15f1_LTS-000000?logo=unity&logoColor=white)](https://unity.com/)
[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)

___

## Detailed Description

This project is part of the [Sollertia](https://github.com/Sun-Lab-NBB/sollertia) AI-assisted scientific data
acquisition and processing platform, built on the [Ataraxis](https://github.com/Sun-Lab-NBB/ataraxis) framework and
developed in the Sun (NeuroAI) lab at Cornell University. It provides the Unity-side assets and runtime bindings for
building VR tasks consumed by Sollertia platform data acquisition systems. The current task surface targets an
**infinite linear corridor** environment displayed to the animal during runtime across a three-monitor VR rig.

This project is the Unity counterpart of [sollertia-experiment](https://github.com/Sun-Lab-NBB/sollertia-experiment),
the Python acquisition runtime. The two libraries communicate over an [MQTT 5.0](https://mqtt.org/) broker:
sollertia-experiment publishes treadmill motion, lick events, and runtime toggles; this project publishes cue sequences,
scene metadata, stimulus events, and brake-activation requests. Task templates, experiment configurations, and the data
schema are owned by [sollertia-shared-assets](https://github.com/Sun-Lab-NBB/sollertia-shared-assets), whose `slsa mcp`
server doubles as the agentic Unity Editor relay for AI-driven task authoring.

The Unity-side runtime is a ground-up refactor of the [GIMBL](https://github.com/winnubstj/Gimbl) VR framework,
inlined under `Assets/Gimbl/`. The refactor preserves GIMBL's core actor, controller, and display abstractions with
extensive editor, MQTT, and runtime enhancements focused on agentic task authoring and Sollertia platform integration.

___

## Features

- Generates infinite-corridor VR tasks from YAML templates via the Editor menu or the `slsa mcp` Unity relay.
- Supports five stimulus trigger modes (interaction, collision, occupancy-disarm, occupancy-arm, occupancy-trigger)
  with optional guidance modes.
- Supports probablistic transitions between trial structures within a single task template.
- Exposes HTTP-based McpBridge that exposes 13 Editor operations to AI agents (task lifecycle, scene management,
  asset inspection, Play Mode control, parameter read/write).
- Maintains bidirectional MQTT 5.0 contract with 
  [sollertia-experiment](https://github.com/Sun-Lab-NBB/sollertia-experiment), centralized in a single `MQTTTopics` 
  constant set.
- Apache 2.0 License.

___

## Table of Contents

- [Dependencies](#dependencies)
- [Installation](#installation)
- [Usage](#usage)
  - [Project Structure](#project-structure)
  - [Task Runtime Structure](#task-runtime-structure)
  - [Task Asset Hierarchy](#task-asset-hierarchy)
  - [Authoring Task Templates](#authoring-task-templates)
  - [Creating Tasks](#creating-tasks)
  - [Loading and Running Tasks](#loading-and-running-tasks)
  - [Task Parameters Window](#task-parameters-window)
  - [MQTT Contract](#mqtt-contract)
  - [Editor MCP Bridge](#editor-mcp-bridge)
- [Developer Notes](#developer-notes)
  - [Project Layout Conventions](#project-layout-conventions)
  - [Formatting and Style](#formatting-and-style)
  - [Extending the Library](#extending-the-library)
  - [AI-Assisted Development](#ai-assisted-development)
- [Versioning](#versioning)
- [Authors](#authors)
- [License](#license)
- [Acknowledgments](#acknowledgments)

___

## Dependencies

External requirements that must be installed before working with this Unity project:

- [Unity Game Engine](https://unity.com/products/unity-engine) **6000.3.15f1 LTS** (Unity 6). Installed via
  [Unity Hub](https://unity.com/download).
- An [MQTT broker](https://mosquitto.org/) supporting **MQTT 5.0**, such as Mosquitto 2.0 or later. The project
  defaults to `127.0.0.1:1883` for the broker; both the IP and port are configurable from the Task Parameters window.
- [Blender](https://www.blender.org/download/) **4.5.0 LTS** is required only for authoring or modifying 3D assets
  (corridor models). It is not required to build or run existing tasks.
- [.NET SDK](https://dotnet.microsoft.com/download) **8.0 or later** and
  [CSharpier](https://csharpier.com/) only when contributing source changes (see
  [Formatting and Style](#formatting-and-style)).

Two managed dependencies ship as committed DLLs under `Assets/Plugins/` and require no separate installation:

- [MQTTnet](https://github.com/dotnet/MQTTnet) — the MQTT 5.0 client used by `MQTTClient.cs`.
- [YamlDotNet](https://github.com/aaubry/YamlDotNet) — the YAML deserializer used by `ConfigLoader.cs`.

___

## Installation

This project is a Unity 6 application that is not distributed via package managers. To install:

1. Install [Unity Hub](https://unity.com/download) and use it to install the required Unity Editor version
   (**6000.3.15f1 LTS**).
2. Download this repository to the local machine using the preferred method, such as git-cloning. Use one of the
   [stable releases](https://github.com/Sun-Lab-NBB/sollertia-unity-tasks/tags) when available.
3. From Unity Hub, select **Add project from disk** and navigate to the local folder containing the downloaded
   repository:
   <br>
   ![Adding the project to Unity Hub](imgs/AddProjectFromDisk.png)

***Note,*** if the correct Unity version is not installed when the project is imported, Unity Hub displays a warning
next to the project name. Selecting the warning offers to install the recommended Unity version:
<br>
![Unity Hub installing the recommended version](imgs/InstallRecommendedVersion.png)

___

## Usage

### Project Structure

The runtime and editor code lives under `Assets/`. Hand-authored assets are protected from agentic deletion via the
McpBridge's protected-paths list; generated assets live under separate folders and are rebuilt on demand.

| Directory                                     | Purpose                                                                    |
|-----------------------------------------------|----------------------------------------------------------------------------|
| `Assets/InfiniteCorridorTask/Scripts/`        | Runtime C# (`Task`, zone scripts, `ConfigLoader`, schema mirror classes)   |
| `Assets/InfiniteCorridorTask/Scripts/Editor/` | `CreateTask` pipeline, `McpBridge` HTTP listener, `TaskEditor`, `MiniJson` |
| `Assets/InfiniteCorridorTask/Configurations/` | YAML task templates                                                        |
| `Assets/InfiniteCorridorTask/Cues/`           | Generated cue prefabs (length-suffixed, shared across templates)           |
| `Assets/InfiniteCorridorTask/Prefabs/`        | Hand-authored zone prefabs and generated segment prefabs                   |
| `Assets/InfiniteCorridorTask/Tasks/`          | Generated task prefabs (one per template)                                  |
| `Assets/InfiniteCorridorTask/Materials/`      | Generated cue materials and the canonical `_CueShaderReference.mat`        |
| `Assets/InfiniteCorridorTask/Textures/`       | Cue textures referenced by YAML templates                                  |
| `Assets/UI-lick-reward/`                      | On-screen lick and stimulus feedback canvas                                |
| `Assets/Gimbl/`                               | Inlined GIMBL runtime and the consolidated Task Parameters window          |
| `Assets/Scenes/`                              | `ExperimentTemplate.unity` plus per-task generated scenes                  |
| `Assets/Plugins/`                             | Inlined `MQTTnet.dll` and `YamlDotNet.dll`                                 |

### Task Runtime Structure

A task represents an **infinite linear corridor sequence** built from a fixed catalog of reusable (prefab) parts. 
Under this hierarchy, a **prefab** is Unity's serializable template for a hierarchy of GameObjects — a piece of a scene 
saved as a file so it can be instantiated repeatedly and updated in one place.

Any task hierarchy can be described in terms of four distinc levels, finest to coarsest:

```text
Task
  │
  ├── Corridor          ─┐  A corridor is a fixed sequence of segments stacked along the animal's
  ├── Corridor           │  motion axis. A task pre-instantiates one corridor for every possible
  ├── Corridor           │  combination of segments, so corridors enumerate the configuration space
  └── … (every          ─┘  the task can take.
       combination)

      one Corridor
        ├── Segment          ← active: the trial currently driving behavior
        ├── Segment          ← lookahead: visible only, no behavior
        └── Segment          ← lookahead: visible only, no behavior

      one Segment (= one trial)
        ├── Cue
        ├── Cue              ← the cue sequence declared by this trial, laid out along the corridor
        └── …
```

- **Cues** are individual visual panels displayed along the walls of the corridor. They are the smallest unit of the
  corridor and are shared across every trial — and every task — that declares the same cue identity.
- **Segments are trials.** Each trial declared by the task produces exactly one segment. A segment owns its cue
  sequence and the behavioral element associated with that trial (the stimulus trigger zone, the reward or aversive
  contingency, etc.).
- **Corridors** are fixed-length windows of segments. The first segment in a corridor is the **active** trial — it
  drives behavior. The remaining segments are pure visual lookahead, so the animal can see what is coming without yet
  experiencing it. The number of segments per corridor is a per-task parameter; setting it to one collapses corridor
  and segment into the same thing.
- **Tasks** are the full set of corridors plus a transition graph that describes how trials chain during a session.
  Each trial may declare a probability distribution over the trials that can follow it; trials without an explicit
  distribution are followed by a uniformly random trial.

**Iterative corridor traversal.** At session start, the task walks its trial-transition graph to build a flat
sequence of trials that overshoots the configured track length. The runtime then slides a window the size of the
corridor (in segments) over that sequence. The current window names one corridor in the pre-built catalog; the
animal is teleported to that corridor's start. Whenever the animal finishes the first segment of the current
corridor, the window slides one trial forward and the animal jumps to the corridor that matches the new window.
Adjacent corridors share all but one segment, so the visible cue sequence stays continuous across the teleport and
the animal experiences a single infinite track.

The corridor count grows exponentially with the number of segments per corridor, so raising the lookahead depth is a
deliberate choice: it adds visual context at the cost of an exponentially larger task. Most paradigms use one or two
segments per corridor.

### Task Asset Hierarchy

On disk, a task lives as three artifacts that share the same basename. The template is the authoritative
description; the task prefab and the task scene are derived from it, and the three files together represent one
task end to end:

```text
<name>.yaml      ─┐  the template — an abstract description of the task's cues, trials, and transitions
                  ▼
<name>.prefab    ─┐  the task prefab — the runtime hierarchy of corridors, segments, and cues
                  ▼  introduced in the previous section, built from the template
<name>.unity     ─┐  the task scene — a runnable scene that instantiates the task prefab and wraps
                  ▼  it in the auxiliary infrastructure a session needs
```

- The **template** is the only artifact authored by hand. It is a plain text description of the task — its cues,
  the trials those cues compose into, and the transition probabilities between trials.
- The **task prefab** is the runtime hierarchy described in the previous section: every corridor the task can take,
  with the segments and cues that fill them. It is fully regenerable — the same template always produces the same
  task prefab.
- The **task scene** wraps one instance of the task prefab in the auxiliary GameObjects a session needs (the animal
  avatar, the display rig, the broker client, the controllers that drive avatar motion). Play mode runs against the
  task scene, not the bare prefab. One hand-authored base scene serves as the template that every task scene is
  copied from; that base is the only scene that is not a task scene.

The basename convention is enforced end to end: regenerating from a template named `<name>.yaml` always produces a
`<name>.prefab` and a `<name>.unity`. One name identifies the task across all three layers.

Two more file types complete the picture:

- **Segment prefabs** live alongside the task prefab and are owned by their parent template — their filenames embed
  both the template name and the trial name, so each task's segments are addressable on their own without colliding
  with any other task's segments.
- **Cue prefabs** are the one shared layer. They are keyed by cue identity and cue length, so two tasks that declare
  the same cue identity reuse the same cue prefab file. The generation pipeline aborts up front if two tasks declare
  the same cue identity with different visuals, so the shared file can never silently corrupt a sibling task.

### Authoring Task Templates

Task templates are YAML files under `Assets/InfiniteCorridorTask/Configurations/`. The schema mirrors the Python
`TaskTemplate` dataclasses in [sollertia-shared-assets](https://github.com/Sun-Lab-NBB/sollertia-shared-assets), and
the deserializer in `ConfigLoader.cs` uses `UnderscoredNamingConvention` to map snake_case YAML keys to camelCase C#
fields.

A task template defines:

- **cues**: A list of visual cue panels. Each cue has a unique `name`, a `code` (0–255 byte) used for MQTT and
  downstream data analysis, a `length_cm`, and a `texture` filename resolved against 
  `Assets/InfiniteCorridorTask/Textures/`.
- **vr_environment**: The Unity corridor configuration — `corridor_spacing_cm`, `segments_per_corridor`,
  `padding_prefab_name`, `cm_per_unity_unit`, and `cue_offset_cm`.
- **trial_structures**: A dictionary mapping trial names (e.g., `ABCD`) to their spatial configuration: the cue
  sequence, the stimulus trigger zone start and end positions, the stimulus location, an optional collision-boundary
  visibility flag, a trigger type (one of `"interaction"`, `"collision"`, `"occupancy_disarm"`, `"occupancy_arm"`, or
  `"occupancy_trigger"`), and an optional probability distribution over successor trials.

The five trigger modes share the same `Stimulus` event but differ in how that event is fired:

- **interaction**: an animal interaction (e.g., a lick) detected while inside the trigger zone fires the stimulus.
- **collision**: crossing an invisible boundary wall — a thin collider at `stimulus_location` — fires the stimulus
  unconditionally, with no sensor and no occupancy requirement. The `showStimulusCollisionBoundary` flag toggles the
  boundary's visibility.
- **occupancy_disarm**: colliding with the boundary while the occupancy requirement is **not** met fires the stimulus.
- **occupancy_arm**: the inverse of `occupancy_disarm` — occupying the zone for the required duration arms the
  boundary, and colliding with the now-armed boundary fires the stimulus.
- **occupancy_trigger**: occupying the zone for the required duration fires the stimulus immediately, with no boundary
  collision.

All three occupancy modes keep the occupancy-guidance brake: the `OccupancyGuidanceZone` publishes `Delay` to guide
the animal toward completing the occupancy requirement when running in guidance mode.

Template filenames follow the `ProjectAbbreviation_TaskDescription.yaml` convention (for example, `SSO_Merging.yaml`).
The template name is derived from the filename and is reused verbatim as the Unity scene name, the task prefab name,
and the prefix for every generated segment prefab. Each template must include a YAML comment header with `Project`,
`Purpose`, `Layout`, and `Related` fields:

```yaml
# Project: StateSpaceOdyssey
# Purpose: Merges ABC and AGFE trial structures by sharing the A cue.
# Layout:  Segment ABC with the rewarding stimulus trigger zone in cue C.
#          Segment AGFE with the rewarding stimulus trigger zone in cue E.
# Related: SSO_Shared_Base (ABC base training), SSO_Merging_Base (AGFE base training)
```

***Note,*** detailed schema authoring guidance is owned by the `/task-templates` skill in the sollertia marketplace's
**assets** plugin. See [sollertia-shared-assets](https://github.com/Sun-Lab-NBB/sollertia-shared-assets) for the
authoritative dataclass definitions.

### Creating Tasks

Tasks can be generated from the Editor menu or programmatically via the McpBridge.

**Editor menu flow.** Select `CreateTask → New Task` from the Unity menu bar. A file dialog seeded at
`Assets/InfiniteCorridorTask/Configurations/` opens; only templates inside that directory are accepted. After selecting
a template, the pipeline:

1. Runs a cross-template cue-texture preflight (`ValidateCueDefinitionsAcrossTemplates`) — if two templates declare the
   same `(cue name, length_cm)` pair with different textures, the generation aborts before any asset is written.
2. Wipes every segment prefab the template owns (`CleanGeneratedSegments`) so trial-parameter edits take effect.
3. Builds or reuses cue prefabs keyed by `(name, length_cm)` under `Assets/InfiniteCorridorTask/Cues/`.
4. Builds every segment prefab from scratch under 
   `Assets/InfiniteCorridorTask/Prefabs/<TemplateName>_<TrialName>.prefab`.
5. Assembles the task prefab at `Assets/InfiniteCorridorTask/Tasks/<TemplateName>.prefab`.
6. Copies `Assets/Scenes/ExperimentTemplate.unity` to `Assets/Scenes/<TemplateName>.unity`, instantiates the task
   prefab into it, and runs `MainWindow.EnsureControllers` so both the `LinearTreadmill` (hardware) and
   `SimulatedLinearTreadmill` (keyboard testing) controllers are present.

**MCP-driven flow.** The same pipeline is reachable via AI agents over the `slsa mcp` server's Unity relay. A single
`create_task_tool` call builds both the task prefab and the matching scene from the same `CreateTask.CreateFromTemplate`
and `CreateTask.CreateSceneFromTemplate` methods used by the Editor menu, so the resulting assets are byte-equivalent.

### Loading and Running Tasks

When a task is generated via the Editor menu, the scene is created and opened automatically. To load an existing task
into a new scene manually:

1. Open the scene at `Assets/Scenes/<TemplateName>.unity` via `File → Open Scene` (the file already contains the task
   prefab instance, the Display rig, the Actor, both controllers, and the MQTT client).
2. Configure displays and per-scene parameters via the [Task Parameters window](#task-parameters-window).
3. Enter Play Mode (`Window → Task Parameters` is always available, and the Play button starts execution).

***Note,*** scene-specific settings such as the camera-to-monitor mapping are scene-bound. Always verify the mapping
after every reboot, since the operating system may reorder display ports.

### Task Parameters Window

`Window → Task Parameters` opens the consolidated editor surface for every per-scene configuration. The window is
docked next to the Inspector tab and auto-opens on Editor start, scene open, and Play Mode entry. The surface exposes
five sections:

| Section          | Controls                                                                                                                                    |
|------------------|---------------------------------------------------------------------------------------------------------------------------------------------|
| Actor            | Animal model selection and active controller (LinearTreadmill or SimulatedLinearTreadmill)                                                  |
| MQTT             | Broker IP and port; the Test Connection button performs a one-shot connect/disconnect probe                                                 |
| Display          | Brightness, height in VR, and a Blank/Show toggle for the active display                                                                    |
| Camera Mapping   | Refresh Monitor Positions plus a per-monitor row (one per OS-detected monitor) with a camera dropdown (`Left View`, `Center View`, `Right View` for the default display rig) and a Show Full-Screen Views action |
| Task             | Require Interaction, Require Wait, Track Length, and Track Seed for the active scene's `Task` component                                     |

The `Task` component's public fields are marked `[HideInInspector]`; `TaskEditor` replaces the default Inspector with
a HelpBox pointing at this window. Configure every task field through Task Parameters, not the Inspector. The
`Require Interaction` and `Require Wait` controls are hidden when the active scene lacks the corresponding
`GuidanceZone` or `OccupancyZone`.

***Warning!*** Verify monitor assignments after every system reboot. The operating system can reassign display ports,
and the camera-to-monitor mapping is scene-bound — a mismatch causes the wrong camera to render to each physical
monitor.

For manual testing without hardware, select **Simulated Linear** as the Actor's controller. The
`SimulatedLinearTreadmill` reads keyboard input via the Unity Input System and publishes a synthetic `Interaction`
message on every press of the Jump action (spacebar).

### MQTT Contract

This project communicates with [sollertia-experiment](https://github.com/Sun-Lab-NBB/sollertia-experiment) over MQTT
5.0. All topics are flat PascalCase identifiers (no hierarchical separators), declared as `public const string` values
in `Assets/Gimbl/Scripts/MQTT/MQTTTopics.cs`.

| Topic                | Direction (Unity)   | Payload                                       |
|----------------------|---------------------|-----------------------------------------------|
| `SessionStart`       | Publish             | Empty trigger                                 |
| `SessionStop`        | Publish             | Empty trigger                                 |
| `Motion`             | Subscribe           | `{movement: float}`                           |
| `Interaction`        | Publish + Subscribe | Empty trigger                                 |
| `Stimulus`           | Publish + Subscribe | `{trialName: string}`                         |
| `Delay`              | Publish             | `{delayMilliseconds: uint}`                   |
| `CueSequenceTrigger` | Subscribe           | Empty trigger                                 |
| `CueSequence`        | Publish             | `{cueSequence: byte[]}`                       |
| `SceneNameTrigger`   | Subscribe           | Empty trigger                                 |
| `SceneName`          | Publish             | `{name: string}`                              |
| `RequireInteraction` | Subscribe           | `{value: bool}`                               |
| `RequireWait`        | Subscribe           | `{value: bool}`                               |

When the broker is unreachable, `MQTTClient.Publish` routes messages in-process so keyboard-only test runs still reach
local subscribers (for example, the on-screen `LickStimulusSpawner` indicator). Production runs require a real MQTT 5.0
broker because sollertia-experiment is the counterparty for every non-`Interaction` and non-`Stimulus` topic.

***Note,*** the `/mqtt-contract` skill in the sollertia marketplace's **unity** plugin is the canonical reference for
topic ownership and payload shape. Any topic addition or rename must be coordinated with sollertia-experiment in the
same release.

### Editor MCP Bridge

The `McpBridge` editor plugin (`Assets/InfiniteCorridorTask/Scripts/Editor/McpBridge.cs`) starts an `HttpListener` on
`127.0.0.1:8090`, `[::1]:8090`, and `localhost:8090` when the Unity Editor loads. The listener exposes Editor
operations to AI agents via JSON request/response. The backing MCP server (`slsa mcp` from
[sollertia-shared-assets](https://github.com/Sun-Lab-NBB/sollertia-shared-assets)) relays each agent tool call to this
bridge over HTTP.

The bridge dispatches **13 tools**:

| Tool                    | Description                                                                       |
|-------------------------|-----------------------------------------------------------------------------------|
| `create_task`           | Builds the task prefab and the matching scene from a YAML template in one call    |
| `delete_task`           | Removes the scene + companion + task prefab + every segment prefab for a template |
| `inspect_prefab`        | Returns hierarchy, components, transforms, and collider details                   |
| `delete_asset`          | Deletes a regenerable non-scene asset (refuses hand-authored protected paths)     |
| `list_assets`           | Lists Unity assets by type filter within a search path                            |
| `list_scenes`           | Enumerates every `.unity` asset and reports the active scene                      |
| `open_scene`            | Opens a scene with explicit `unsaved_changes` policy                              |
| `inspect_scene`         | Returns the active scene's root hierarchy and dirty flag                          |
| `enter_play_mode`       | Triggers `EditorApplication.EnterPlaymode`                                        |
| `exit_play_mode`        | Triggers `EditorApplication.ExitPlaymode`                                         |
| `get_play_state`        | Returns `playing`, `compiling`, or `edit` plus the active scene name              |
| `read_task_parameters`  | Snapshots Actor, MQTT, Display, Camera Mapping, and Task fields                   |
| `write_task_parameters` | Applies a subset of Task Parameters fields and returns a new snapshot             |

All responses are JSON objects carrying a `success` boolean plus a payload or error string. `delete_asset` is bounded
by an allow-prefix list (`Assets/InfiniteCorridorTask/Tasks/`, `Prefabs/`, `Cues/`, `Materials/`) and rejects scene
paths under `Assets/Scenes/` — scene cleanup goes through `delete_task` exclusively so the cascade-delete of the
matching `Assets/VRSettings/Displays/<scene>-savedFullScreenViews.asset` companion can never be bypassed. A
protected-paths set covers the four hand-authored prefabs (`StimulusTriggerZone.prefab`, `OccupancyTriggerZone.prefab`,
`ResetZone.prefab`, `Padding.prefab`), the four hand-authored materials (`_CueShaderReference.mat`, `Floor.mat`,
`Wall.mat`, `TargetMat.mat`), and the scene base template (`ExperimentTemplate.unity`). Path traversal sequences and
absolute paths are rejected.

***Note,*** AI agents do not call this bridge directly. They use the `slsa mcp` server's Unity relay tools, which are
listed in the [sollertia-shared-assets](https://github.com/Sun-Lab-NBB/sollertia-shared-assets) README. The bridge
exists so that any MCP-driven action against this project shares the same code paths as the Editor menu.

___

## Developer Notes

These notes apply to project developers and task authors who modify source code, segment prefabs, or templates.

### Project Layout Conventions

Three categories of assets coexist in this project:

- **Hand-authored** (protected): `StimulusTriggerZone.prefab`, `OccupancyTriggerZone.prefab`, `ResetZone.prefab`,
  `Padding.prefab`, `Materials/_CueShaderReference.mat`, `Materials/Floor.mat`, `Materials/Wall.mat`,
  `Materials/TargetMat.mat`, and `Scenes/ExperimentTemplate.unity`. These are the source templates and shared assets
  that `CreateTask` references at generation time (and that the trigger zone prefabs reference in turn via
  `TargetMat.mat`). They must remain untouched; `McpBridge.DeleteProtectedPaths` refuses to delete them.
- **Generated** (regenerable): every cue prefab under `Cues/`, every segment prefab under `Prefabs/` matching
  `<TemplateName>_<TrialName>.prefab`, every cue material under `Materials/Cue_*_*cm.mat`, every prefab under `Tasks/`,
  and every scene other than `ExperimentTemplate.unity`. These are produced by `CreateTask` and are safe to delete via
  `delete_task` (whole-task cleanup) or `delete_asset` (individual cue prefab / material) so the next generation pass
  rebuilds them.
- **Shared inputs**: the YAML templates under `Configurations/` and the cue textures under `Textures/`. Templates are
  the source of truth for trial structure; textures are imported from external sources (for example,
  [vr-visual-cues](https://github.com/sprustonlab/vr-visual-cues)) and referenced by the `cues[].texture` field.

***Note,*** cue prefabs are shared across templates by `(name, length_cm)`. Editing a cue's texture without renaming
the cue requires deleting the affected `Cue_<name>_<length>cm.prefab` and `Cue_<name>_<length>cm.mat` before
regenerating; the cross-template cue-texture preflight catches conflicts before they corrupt downstream assets.

### Formatting and Style

This project uses [CSharpier](https://csharpier.com/) for code formatting and an `.editorconfig` for naming, brace,
and spacing conventions. Run `csharpier .` before committing, or `csharpier --check .` to verify without modifying.
See the `/csharp-style` skill in the ataraxis marketplace's **automation** plugin for the complete C# convention
reference.

### Extending the Library

The project exposes six concentrated extension points. Each has a matching skill in the sollertia marketplace's
**unity** or **assets** plugin, listed alongside the touch points below.

| Extension                | Touch points                                                                                    | Owner skill        |
|--------------------------|-------------------------------------------------------------------------------------------------|--------------------|
| New task template        | YAML in `Configurations/`; generate via `/task-prefabs`                                         | `/task-templates`  |
| New cue texture          | PNG in `Textures/`; reference it from a YAML `texture` field                                    | `/task-templates`  |
| New trigger zone type    | New zone script + prefab (via `/zone-prefabs`) + `ConfigLoader` literal + `CreateTask` branch   | `/zone-prefabs`    |
| New MQTT topic           | `MQTTTopics` constant + matching publisher / subscriber on Unity and sollertia-experiment sides | `/mqtt-contract`   |
| New `McpBridge` tool     | `Dispatch` switch case + handler method + `@mcp.tool()` wrapper in `unity_tools.py`             | n/a (manual)       |
| New treadmill controller | `ControllerObject` subclass + `ControllerTypes` enum entry                                      | `/gimbl-framework` |

**Adding a new trigger zone type** is the most cross-cutting extension. The `/zone-prefabs` skill documents the
copy-and-edit workflow for the prefab itself: start from `StimulusTriggerZone.prefab` (interaction and collision modes)
or `OccupancyTriggerZone.prefab` (the three occupancy modes) under `Prefabs/`, swap the modifier script GUIDs, rename
regions, and override field defaults. The new prefab path must then be added to `McpBridge.DeleteProtectedPaths`, a new
branch must be added in `CreateTask.BuildSegmentPrefabs` with a matching `Place...Zone` helper, and 
`ConfigLoader.ValidateTemplate` must accept the new `trigger_type` literal. `CreateTask` sets the `TriggerMode` enum
field on `StimulusTriggerZone` from the `trigger_type`, and the zone dispatches on that enum. The Python side requires
a matching `TriggerType` registry update via the `/library-extension` skill in the **assets** plugin. Adding a
`TriggerType` member does **not** require a `from_task_template` branch in every acquisition system: the platform
`TriggerType` enum carries all members, but each system maps only the subset it supports and may leave a mode
unmapped. A config that uses an unmapped mode raises a clear "not mapped to a runtime trial class" error. The
Mesoscope-VR system, for example, maps `interaction` (`WaterRewardTrial`) and `occupancy_disarm` (`GasPuffTrial`), and
does not map `collision`, `occupancy_arm`, or `occupancy_trigger`.

**Adding a new MQTT topic** requires the constant in `MQTTTopics.cs` (with `Direction`, `Payload`, and `Callers`
remarks), a runtime script that publishes or subscribes, an in-lockstep update in sollertia-experiment, and a refresh
of the `/mqtt-contract` skill catalog.

**Adding a new McpBridge tool** requires a new switch case in `McpBridge.Dispatch`, a handler method that returns
`Ok(...)` or `Error(...)`, optional integration with `AcquireSceneComponents` and `BuildSnapshot` when the tool reads
or writes scene state, and a matching `@mcp.tool()` wrapper in
`sollertia-shared-assets/src/sollertia_shared_assets/interfaces/unity_tools.py`.

### AI-Assisted Development

Claude Code skills and AI development assets for this project are distributed through two marketplaces:

- [sollertia](https://github.com/Sun-Lab-NBB/sollertia) marketplace:
  - **unity** plugin — Unity Editor skills that drive McpBridge tools, document the MQTT contract, document the
    `CreateTask` pipeline, and guide manufacturing of new trigger zone prefabs.
  - **assets** plugin — registers the `slsa mcp` server (which fronts the Unity relay), and provides configuration and
    experiment-authoring skills (task templates, experiment configurations, library extension).
- [ataraxis](https://github.com/Sun-Lab-NBB/ataraxis) marketplace:
  - **automation** plugin — shared development skills that enforce coding conventions (C# style, README style, commit
    messages, project layout) and general-purpose codebase exploration tools.

Install all three plugins to make the full skill set available to compatible AI coding agents. The **unity** plugin
depends on the **assets** plugin for the backing MCP server that drives the Unity Editor relay.

___

## Versioning

This project uses [semantic versioning](https://semver.org/). See the
[tags on this repository](https://github.com/Sun-Lab-NBB/sollertia-unity-tasks/tags) for the available project
releases.

___

## Authors

- Ivan Kondratyev ([Inkaros](https://github.com/Inkaros))
- Jacob Groner ([Jgroner11](https://github.com/Jgroner11))

___

## License

This project is licensed under the Apache 2.0 License: see the [LICENSE](LICENSE) file for details.

___

## Acknowledgments

- All Sun lab [members](https://neuroai.github.io/sunlab/people) for providing the inspiration and comments during the
  development of this library.
- The creators of the original [GIMBL](https://github.com/winnubstj/Gimbl) package, whose framework is inlined under
  `Assets/Gimbl/`.
- The creators of [MQTTnet](https://github.com/dotnet/MQTTnet) and [YamlDotNet](https://github.com/aaubry/YamlDotNet),
  whose libraries ship as committed DLLs under `Assets/Plugins/`.
- The [vr-visual-cues](https://github.com/sprustonlab/vr-visual-cues) project for the cue texture set.
