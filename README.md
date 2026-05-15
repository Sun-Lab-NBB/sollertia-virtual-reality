# sollertia-unity-tasks

Provides assets to create and execute Virtual Reality (VR) tasks for Sollertia platform data acquisition systems.

[![C#](https://tinyurl.com/bdd689s9)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![Unity](https://img.shields.io/badge/Unity-6000.3.3f1_LTS-000000?logo=unity&logoColor=white)](https://unity.com/)
[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)

___

## Detailed Description

This project is part of the [Sollertia](https://github.com/Sun-Lab-NBB/sollertia) AI-assisted scientific data
acquisition and processing platform, built on the [Ataraxis](https://github.com/Sun-Lab-NBB/ataraxis) framework and
developed in the Sun (NeuroAI) lab at Cornell University. It provides assets and bindings for building Virtual Reality
(VR) tasks used by Sollertia platform data acquisition systems to conduct experiments. Primarily, the project is
designed to construct an **infinite linear corridor** environment and display it to the animal during runtime using a
set of three Virtual Reality monitors (screens).

This project is specialized to work with the
[sollertia-experiment](https://github.com/Sun-Lab-NBB/sollertia-experiment) library used by all Sollertia platform data
acquisition systems. It uses [MQTT](https://mqtt.org/) to bidirectionally communicate with the sollertia-experiment
runtimes and relies on sollertia-experiment to provide it with the data on animal's behavior during the VR task
execution.

This project extends the original [GIMBL](https://github.com/winnubstj/Gimbl) VR framework, which has been inlined
into the project under `Assets/Gimbl/`. The refactored framework provides an interface for building and modifying tasks
using prefabricated assets ('prefabs'), deprecates GIMBL functionality now handled by sollertia-experiment (logging,
unused MQTT topics), and removes legacy technical debt.

___

## Features

- Runs on Windows, Linux, and macOS.
- Supports tasks with multiple corridor segments and probabilistic transitions between them.
- Includes agentic coding support with Claude Code skills for codebase exploration and style guide compliance.
- Provides automated task structure verification to validate prefab positions against YAML template constants.
- Exposes an HTTP-based MCP bridge for AI agent integration with the Unity Editor.
- Apache 2.0 License.

___

## Table of Contents

- [Dependencies](#dependencies)
- [Installation](#installation)
- [Usage](#usage)
  - [Creating New Tasks](#creating-new-tasks)
  - [Loading Existing Tasks](#loading-existing-tasks)
- [Developer Notes](#developer-notes)
- [Versioning](#versioning)
- [Authors](#authors)
- [License](#license)
- [Acknowledgments](#acknowledgments)

___

## Dependencies

### Internal Dependencies

These dependencies are included in the project as .dll files in `Assets/Plugins/`:

- [MQTTnet](https://github.com/dotnet/MQTTnet) — MQTT 5.0 client for broker communication.
- [YamlDotNet](https://github.com/aaubry/YamlDotNet) — YAML parser for task template loading.

### External Dependencies

The following dependencies must be installed before working with this Unity project:

- [MQTT broker](https://mosquitto.org/) version **2.0.21**. This project was tested with the broker running locally,
  using the **default** IP (127.0.0.1) and Port (1883) configuration.
- [Unity Game Engine](https://unity.com/products/unity-engine) version **6000.3.3f1 LTS**.
- [Blender](https://www.blender.org/download/) version **4.5.0 LTS**. Only required for creating or modifying 3D
  assets (corridor models). Not needed to run existing tasks.

___

## Installation

### Source

1. Install the [Unity hub](https://unity.com/download) and use it to install the required Unity Game Engine version.
2. Download this repository to a local machine using a preferred method, such as Git-cloning. Use one of the stable
   releases from [GitHub](https://github.com/Sun-Lab-NBB/sollertia-unity-tasks/releases).
3. From the Unity Hub, select `add project from disk` and navigate to the local folder containing the downloaded
   repository: <br> <img src="imgs/AddProjectFromDisk.png" width="300"/>

**Hint.** If the correct Unity version is not installed when the project is imported, the Unity Hub displays a warning
next to the project name. Click on the warning and install the recommended Unity version:
<br> <img src="imgs/InstallRecommendedVersion.png" width="600"/>

___

## Usage

This section discusses how to use existing tasks to conduct experiments and create new tasks using the project.
**Note!** This library is specifically written to work with the
[sollertia-experiment](https://github.com/Sun-Lab-NBB/sollertia-experiment) library and will likely not work in other 
contexts without modification.

### Creating New Tasks

The key feature of this project is the **task creator**: a system for quickly making any infinite corridor task with or
without probabilistic transitions between corridor segments.

#### Task Definition

Each **task** can be conceptualized as a set of infinite corridor **segments** and the **transition probabilities**
between them. Each segment is split into **cues**, which are portions of the corridor walls that have different
colors/textures. Since each task segment typically contains a stimulus trigger zone that conditionally delivers stimuli
to the animal (water rewards, air puffs, etc.), traversing each segment typically constitutes a single **experiment
trial**. Therefore, the sequence of wall cues that makes each segment is referred to as the **cue sequence** in task
configuration files.

Overall, a set of segments can represent any task graph depicting transitions between infinite corridor cues. For
example, the cue graph below can be represented by two segments with uniform transition probabilities between
each other:

<!--suppress CheckImageSize -->
<img src="imgs/cue_graph.png" width="233" alt="graph picture">

1. **Segment 1**: A, B, C
2. **Segment 2**: A, B, D, C

During experiment, both segments are typically reused many times to create a long sequence of segments to be experienced
by the animal during runtime.

In addition to the general task structure, there are additional parameters to be considered for each task, including:

- The length of each cue region.
- The length of non-cue ('gray') wall regions between the cue regions.
- The graphical texture (pattern) of each wall cue.
- The graphical texture of non-cue wall regions (usually gray color, hence the name **gray regions**).
- The graphical texture of the corridor floor.
- The stimulus trigger zone locations and the conditions for the animal to receive the stimulus.

#### Implementation

To create a task according to the desired specification, two assets need to be generated: a Unity prefab for each
segment and a YAML configuration file. The easiest way to create these assets is to start with an already existing
task and modify it to match the desired parameters. Use ctrl/cmd D to duplicate existing segment prefabs and copy
existing `.yaml` files from `Assets/InfiniteCorridorTask/Configurations/`.

#### Segment Prefabs

All segment prefabs must be placed in the directory **Assets/InfiniteCorridorTask/Prefabs**. Double-clicking on a prefab
opens up Unity's prefab editor. **Hint!** To verify that the file being edited is a prefab and not a GameObject, ensure
that the scene has a **blue** background.

<img src="imgs/segment_prefab.png" width="600" alt="">

Each prefab includes several key elements:

- **Stimulus Trigger Zone**: The parent zone that manages stimulus delivery. Its behavior depends on which child zone
  is present (Guidance Zone or Occupancy Zone).
- **Guidance Zone**: A child of the Stimulus Trigger Zone used in lick mode trials.
- **Reset Zone**: After successfully triggering a stimulus delivery, the animal must pass through this zone before
  another stimulus can be triggered.
- **Occupancy Zone** *(optional)*: A child of the Stimulus Trigger Zone used in occupancy mode trials.

**Zone Behavior Modes:**

The Stimulus Trigger Zone operates in one of two modes based on which child zone is present. The trigger mode
determines *how* a stimulus is delivered, not *what* stimulus is delivered. Any stimulus type (water, air puff, etc.)
can be paired with either trigger mode.

1. **Lick Mode** (with Guidance Zone child):
   - When **Require Lick** is enabled: The animal must lick within the Stimulus Trigger Zone to receive the stimulus.
   - When **Require Lick** is disabled (guidance mode): The stimulus is delivered automatically when the animal reaches
     the Guidance Zone. The animal can still lick anywhere in the Stimulus Trigger Zone to receive the stimulus early.

2. **Occupancy Mode** (with Occupancy Zone child):
   - The animal must remain in the Occupancy Zone for the required duration to **disarm** the trigger zone's boundary.
   - If the animal leaves early and collides with the boundary while it is still **armed**, the stimulus is delivered.
   - When **Require Wait** is disabled (guidance mode): The library sends an MQTT message requesting the treadmill
     brakes to lock, enforcing the occupancy requirement by preventing the animal from leaving early.

**Note:** By convention, lick mode is typically used for reward delivery (water) and occupancy mode for aversion
stimuli (air puff), but this pairing is not a technical requirement. Future experiments may use different
stimulus-trigger combinations.

Once each prefab segment is created, an additional prefab must be made for padding. This padding prefab should be a long
empty corridor, and it is used during task runtime to give the animal an illusion that the corridor is infinite.

#### YAML Configuration File

The **task configuration file** ties the segment prefabs together and is required for creating and running tasks. These
files are stored in `Assets/InfiniteCorridorTask/Configurations/` with a `.yaml` extension.

**Note:** The configuration schema is derived from the
[sollertia-shared-assets](https://github.com/Sun-Lab-NBB/sollertia-shared-assets) library and always matches the 
current state of that library's data classes. See `task_template_data.py` in sollertia-shared-assets for the 
authoritative schema definition.

**File Naming Convention:**

Template files follow the pattern `ProjectAbbreviation_TaskDescription.yaml`:

| Abbreviation | Project Name      |
|--------------|-------------------|
| MF           | MaalstroomicFlow  |
| SSO          | StateSpaceOdyssey |

- Use `_Base` suffix for single-segment training configurations (e.g., `SSO_Shared_Base.yaml`).
- Capitalize each word in the task description.
- The template name and Unity scene name are derived from the filename (without `.yaml`).

**Header Format:**

Each template file must begin with a YAML comment header containing four fields:

```yaml
# Project: [Full project name]
# Purpose: [Single sentence describing the task structure]
# Layout:  [Segment names with cue letters and zone placements]
# Related: [Related template file (parenthetical explanation)]
```

For multi-line fields, align continuation text with the first character after the field name:

```yaml
# Layout:  Segment ABC with the rewarding stimulus (water) trigger zone in cue C.
#          Segment ABDC with the rewarding stimulus (water) trigger zone in cue C.
```

**Schema:**

The structure is:

- **cues** *(array\<Cue>)*: The list of all cues used by any trial.
    - **Cue**
        - **name** *(string)*: The unique human-readable label for the cue (e.g., `"A"`, `"Gray"`).
        - **code** *(integer, 0-255)*: The unique integer code for the cue used in logging.
        - **length_cm** *(number)*: The length of the cue in centimeters.
        - **texture** *(string)*: The filename of the texture image in `Assets/InfiniteCorridorTask/Textures/`
          (e.g., `"Cue 016 - 4x1.png"`).

- **vr_environment** *(object)*: VR corridor configuration.
    - **corridor_spacing_cm** *(number)*: Distance between consecutive corridors in centimeters.
    - **segments_per_corridor** *(integer)*: Number of segments per corridor. Setting this to 3 is generally enough to
      give the illusion of an infinite corridor.
    - **padding_prefab_name** *(string)*: The name of the padding prefab (usually `"Padding"`).
    - **cm_per_unity_unit** *(number)*: Conversion factor from centimeters to Unity units.
    - **cue_offset_cm** *(number)*: The offset of the animal's starting position relative to each corridor's cue
      sequence origin, in centimeters. Drives both the upstream shift applied to every segment prefab's local origin
      and the position of the per-segment ResetZone.

- **trial_structures** *(dict\<string, TrialStructure>)*: Maps trial names to their spatial configurations. Each trial
  generates a single segment prefab named `<template>_<trial>.prefab` (e.g. `MF_Reward_Base_ABCD.prefab`); trial names
  must therefore match `^[A-Za-z0-9_]+$`. Segment prefabs are always regenerated on each `generate_task_prefab_tool`
  call so trial-parameter edits take effect without manual prefab deletion.
    - **TrialStructure**
        - **cue_sequence** *(string[])*: The ordered list of cue names that comprise the trial's segment.
        - **stimulus_trigger_zone_start_cm** *(number)*: Start of the stimulus trigger zone in centimeters.
        - **stimulus_trigger_zone_end_cm** *(number)*: End of the stimulus trigger zone in centimeters.
        - **stimulus_location_cm** *(number)*: Position of the stimulus boundary in centimeters.
        - **show_stimulus_collision_boundary** *(boolean)*: Determines whether to show the stimulus boundary to the
          animal.
        - **trigger_type** *(string)*: The trigger mode for the zone. Must be `"lick"` for trials with a Guidance Zone
          child or `"occupancy"` for trials with an Occupancy Zone child. This field specifies the trigger mechanism,
          not the stimulus type.
        - **transitions** *(dict\<string, number>)*: Optional probability distribution over the trial names that may
          follow this trial during corridor traversal. Keys must reference other trial names defined on the same
          template; values must sum to 1.0. Omitted keys carry implicit zero probability. When null or omitted,
          successors are sampled uniformly at random over all defined trials.

See existing configuration files in `Assets/InfiniteCorridorTask/Configurations/` for examples.

***Note,*** the [sollertia-shared-assets](https://github.com/Sun-Lab-NBB/sollertia-shared-assets) MCP server provides a
`validate_prefab_against_template` tool that validates configuration template files against existing prefabs via the
McpBridge. Developers and AI agents are highly encouraged to use this tool when creating or modifying configuration
files. The validator reports per-cue prefab existence and per-segment match flags for cue ordering, segment Z-length,
and zone positions, so spatial drift between template and prefab surfaces immediately.

#### 'CreateTask' Tab

Once the YAML configuration file is created, use the **CreateTask → New Task** command. This will open a file window to
select the YAML configuration file. Once the file is selected, a secondary prompt will open to name and save the prefab.
Once created, the prefab can be loaded and executed as described in [Loading Existing Tasks](#loading-existing-tasks).

<img src="imgs/createTask.png" width="700" alt="">

### Loading Existing Tasks

Task prefabs are generated locally using the CreateTask editor tool and saved to
`Assets/InfiniteCorridorTask/Tasks/`. This directory does not exist in a fresh checkout and is created when the first
task is generated. To load a task into a scene, follow these steps:

1. Create a new scene by clicking File → New Scene. Instead of using the default scene template, select
   **ExperimentTemplate** as the template. ***Note,*** the first time this Unity project opens, it uses an empty scene.
   If prompted, do ***not*** save this empty scene.
   <br> <img src="imgs/newScene.png" width="600">
2. Navigate to **Assets/InfiniteCorridorTask/Tasks**. This folder contains task prefabs generated via the CreateTask
   editor tool. Drag the prefab for the desired task into
   the hierarchy window and wait for it to be loaded into the scene. **Note!** If Preferences > Scene View > 3D
   Placement Mode is set to "World Origin," then dragging the prefab into the hierarchy window will automatically
   position the task correctly.
   <br> <img src="imgs/hierarchy_window.png" width="800">
3. Select the task's **GameObject** in the **Hierarchy** window and view the **Inspector** window. The **Inspector**
   window reveals the **Transform** component and the **Task** script. There are two things that must be verified at
   this point:
    1. That the transform's position is set to (0, 0, 0).
    2. That the **Actor** parameter is set. If it is None, use the dropdown menu to set it to the **Actor Object** in
       the scene.
4. The *Task* script contains additional parameters which should not need to be modified:
    - **Require Lick**: Determines whether the animal must lick within the stimulus trigger zone to receive the
      stimulus. If disabled (guidance mode), the stimulus is delivered automatically when the animal reaches the
      guidance zone. **Note!** During sollertia-experiment runtimes, this parameter is automatically overridden by the
      sollertia-experiment GUI and runtime logic.
    - **Require Wait**: Determines whether the animal must remain in the occupancy zone for the required duration to
      disarm the trigger zone's start boundary. If disabled (guidance mode), the library sends an MQTT message
      requesting the treadmill brakes to lock, enforcing the occupancy requirement.
      ***Note,*** during sollertia-experiment runtimes, this parameter is automatically overridden by the
      sollertia-experiment GUI and runtime logic.
    - **Track Length**: The length of the track's wall cue sequence, in Unity units, to pre-create before runtime. This
      is most relevant for tasks with multiple segments and random transitions between them. Pre-creating the cue
      sequence before runtime allows sollertia-experiment to accurately track transitions between trials and support
      trial-specific logic while treating the experiment runtime as a monolithic sequence of trials. **Note!** If the
      animal traverses the entire pregenerated track, the Unity task starts making on the fly decisions about which
      segment the animal enters at the end of each trial. Likely, this will cause sollertia-experiment to abort
      with an error,
      as it is not notified of these additional trials. Therefore, **it is advised to pre-generate a long cue sequence
      at each runtime, guaranteeing the animal is not able to fully traverse it at runtime**.
    - **Track seed**: The seed to use for resolving random transitions between segments. This is helpful when running
      many experiments with the exact same pattern of segment transitions. If set to -1, then no seed is used and
      transitions are randomized at each task runtime.
    - **Config Path**: The file path to the YAML configuration file associated with the task. **Note!** If the
      configuration file specified by this parameter is no longer found at the target path, the game becomes
      non-functional. To fix this, change this parameter to specify the correct path (relative to the local root) or
      recreate the task. See the ['creating new tasks'](#creating-new-tasks) section for more details about this file.
5. Select File > Save As to save the scene in *Assets/Scenes*.
6. Select the **Parameters** tab located to the right of the Inspector tab. If the tab is not present, reopen it
   by selecting Window > Task Parameters. Press `Refresh Monitor Positions` in the Camera Mapping section. This reveals a list of the monitors connected
   to the computer. Assign **Camera: LeftMonitor**, **Camera: RightMonitor**, and **Camera: CenterMonitor** to the
   corresponding monitors used for display to the animal. To verify that the monitors were assigned correctly, press
   `Show Full-Screen Views`. For more information about configuring displays, consult the
   [original GIMBL repository](https://github.com/winnubstj/Gimbl?tab=readme-ov-file#setting-up-the-actor).
   **Warning!** Since rebooting the system frequently changes the Monitor output ports, always verify
   monitor assignments before running experiment tasks.
   <br> <img src="imgs/display_tab.png" width="300">
7. Press the play button to run the VR task. Verify that there are no errors displayed in the console window after
   starting (playing) the task. **Hint!** If errors appear, start debugging by examining the **first** error
   printed, which is likely the true error. Subsequent errors are likely a result of running a broken game loop after
   the initial error. **Note!** The template environment is designed for experiments, where motion and licks should be
   sent over the MQTT protocol. To test the task manually, replace the *linear controller* with a *simulated linear
   controller*. Consult
   [Setting Up the Actor](https://github.com/winnubstj/Gimbl?tab=readme-ov-file#setting-up-the-actor)
   for instructions on this process.

___

## Developer Notes

These notes are primarily directed to project developers and task creators.

* Be careful about modifying segment prefabs. Even after task creation, the task prefab relies on the existence of the
  segment prefabs to run as expected. This means that if segment prefabs are modified later, it will also modify all
  tasks using that prefab. To make small changes to many tasks, use the same segment prefab multiple times to
  automatically synchronize the changes across all modified tasks. To modify one task without changing other tasks
  that use the same prefab, make a new prefab that is a duplicate of the old one and modify the YAML configuration files
  accordingly.
* Most changes to the task structure can be implemented by modifying the segment prefabs. However, modifying a prefab
  may invalidate all configuration files using that prefab. The YAML configuration file contains a lot of
  information that needs to match the exact state of each prefab, so it is a good practice to ensure the validity
  of all configuration files after modifying the prefab. Also, it is good practice to recreate the task from the
  YAML configuration file following
  prefab modification. If the newly created task uses the same name as the old task, it will replace the old task
  prefab.
* The [Loading Existing Tasks](#loading-existing-tasks) section explains how to create a scene to hold the desired task.
  When running multiple experiments (using different tasks) from the same computer, it may be cumbersome to maintain
  multiple Unity projects or to have one Unity project and switch the active task between experiments (within the same
  scene). The best practice is to create a separate scene for each experiment as part of the same Unity project and
  switch between scenes by double-clicking on them. When starting a new experiment, open the desired scene and run the
  task. **Note!** The display configurations are scene-specific, so displays must be reconfigured separately for each
  scene.
* Be cautious when pushing and pulling code with GitHub. Merging branch conflicts is challenging with Unity and will
  likely require changing one of the conflicting branches completely. Try to avoid merge conflicts and focus on making
  changes to assets (prefabs) while avoiding making large changes to the scene. Additionally, it is a good practice to
  close the Unity project before pushing/pulling.
* The original GIMBL package was designed to log all non-brain-activity experiment data. Since this project is
  explicitly designed to work with sollertia-experiment that now does all logging, **all Unity logging has been removed 
  from this project**.
* For information on how to send MQTT messages to Unity, see
  [here](https://github.com/winnubstj/Gimbl/wiki/Example-code-of-MQTT-subscribing-and-publishing).

* Additional cue textures can be found [here](https://github.com/sprustonlab/vr-visual-cues). To use a new cue:
    1. Convert an `.ai` file to `.png`.
    2. Import the `.png` into Unity as an asset. Place the asset in the `Assets/InfiniteCorridorTask/Textures/` folder.
    3. Reference the texture filename in the YAML configuration file's `texture` field for the desired cue entry.
    4. The CreateTask editor tool automatically generates cue prefabs (with Left and Right Quad children) and materials
       from the texture references in the YAML file. Existing cue prefabs and materials in
       `Assets/InfiniteCorridorTask/Cues/` and `Assets/InfiniteCorridorTask/Materials/` are reused if already present.

### MCP Bridge

The project includes an MCP bridge plugin (`McpBridge`) that starts an HTTP listener on `localhost:8090` when the
Unity Editor loads. This bridge enables AI agents (via the
[sollertia-shared-assets](https://github.com/Sun-Lab-NBB/sollertia-shared-assets) MCP server) to control the Unity
Editor programmatically. The following tools are available through the bridge:

| Tool                              | Description                                                                            |
|-----------------------------------|----------------------------------------------------------------------------------------|
| `generate_task_prefab`            | Creates a task prefab from a YAML template                                             |
| `inspect_prefab`                  | Returns hierarchy, components, and zone details of a prefab                            |
| `validate_prefab_against_template`| Validates cue inventory, segment geometry, cue ordering, and zone positions            |
| `delete_unity_asset`              | Deletes a regenerable asset under InfiniteCorridorTask or Scenes (protects hand-authored assets) |
| `list_unity_assets`               | Lists Unity assets by type within a search path                                        |
| `list_scenes`                     | Lists all scene assets and identifies the active scene                                 |
| `open_scene`                      | Opens a scene in the Editor with explicit unsaved-edits handling                       |
| `create_scene`                    | Creates a new scene from the ExperimentTemplate with explicit unsaved-edits handling   |
| `inspect_scene`                   | Returns the active scene's metadata, dirty flag, and recursive root hierarchy          |
| `enter_play_mode`                 | Enters Play Mode                                                                       |
| `exit_play_mode`                  | Exits Play Mode                                                                        |
| `get_play_state`                  | Returns the current play state and active scene name                                   |

### AI-Assisted Development

Claude Code skills and AI development assets for this project are distributed through two marketplaces:

- [sollertia](https://github.com/Sun-Lab-NBB/sollertia) marketplace:
    - **assets** plugin — registers the `sollertia-shared-assets` MCP server (which also relays commands to the
      Unity Editor's `McpBridge`) and provides skills for authoring task template YAMLs and experiment configurations.
    - **unity** plugin — provides skills that drive Unity Editor operations through the `McpBridge` relay (prefab
      generation, scene management, Play Mode control, asset enumeration, MQTT topic reference, Display rig setup,
      and segment prefab authoring).
- [ataraxis](https://github.com/Sun-Lab-NBB/ataraxis) marketplace: Provides shared development skills that enforce
  coding conventions (C# style, README style, commit messages) and general-purpose codebase exploration tools via the
  **automation** plugin.

Install all three plugins (`assets`, `unity`, and `automation`) to make the full skill set available to compatible AI
coding agents. The `unity` plugin depends on the `assets` plugin for the backing MCP server.

___

## Versioning

This project uses [Semantic Versioning](https://semver.org/). For available versions, see the
[tags on this repository](https://github.com/Sun-Lab-NBB/sollertia-unity-tasks/tags).

___

## Authors

- Jacob Groner ([Jgroner11](https://github.com/Jgroner11))
- Ivan Kondratyev ([Inkaros](https://github.com/Inkaros))

___

## License

This project is licensed under the Apache 2.0 License: see the [LICENSE](LICENSE) file for details.

___

## Acknowledgments

- All Sun Lab [members](https://neuroai.github.io/sunlab/people) for providing the inspiration and comments during the
  development of this library.
- The creators of the original [GIMBL](https://github.com/winnubstj/Gimbl) package and all dependencies used by that
  package.
