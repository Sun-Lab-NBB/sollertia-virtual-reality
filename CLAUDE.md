# Claude Code Instructions

## Session start behavior

At the beginning of each coding session, before making any code changes, you should build a comprehensive
understanding of the codebase by invoking the `/explore-codebase` skill (automation plugin).

This ensures you:
- Understand the Unity project architecture before modifying code
- Follow existing patterns and conventions
- Don't introduce inconsistencies or break MQTT integrations

## Style guide compliance

Before writing, modifying, or reviewing code or documentation, invoke the style skill that matches the file type.
These skills live in the ataraxis marketplace **automation** plugin:

| File type                  | Skill             |
|----------------------------|-------------------|
| C# source files (`.cs`)    | `/csharp-style`   |
| README (`README.md`)       | `/readme-style`   |
| Skill / CLAUDE.md files    | `/skill-design`   |
| Git commit messages        | `/commit`         |
| Project directory layout   | `/project-layout` |

Every contribution must follow the applicable style conventions and every review must check for compliance. Key C#
conventions include:
- XML documentation comments with `<summary>`, `<param>`, `<returns>` tags
- Private fields with `_camelCase`, public members with `PascalCase`
- Allman brace style (braces on new lines)
- Third person imperative mood for comments and documentation
- 120 character line limit enforced by CSharpier
- Commit messages use past tense verbs (Added, Fixed, Updated) and end with periods

## Cross-referenced library verification

Sollertia platform projects often depend on other `ataraxis-*` or `sollertia-*` libraries. These libraries may be stored
locally in the same parent directory as this project (`/home/cyberaxolotl/Desktop/GitHubRepos/`).

**Before writing code that interacts with a cross-referenced library, you MUST:**

1. **Check for local version**: Look for the library in the parent directory (e.g.,
   `../sollertia-shared-assets/`, `../sollertia-experiment/`).

2. **Compare versions**: If a local copy exists, compare its version against the latest release or
   main branch on GitHub:
   - Read the local package.json or version file to get the current version
   - Use `gh api repos/Sun-Lab-NBB/{repo-name}/releases/latest` to check the latest release

3. **Handle version mismatches**: If the local version differs from the latest release, notify the
   user with the following options:
   - **Use online version**: Fetch documentation and API details from the GitHub repository
   - **Update local copy**: The user will pull the latest changes locally before proceeding

4. **Proceed with correct source**: Use whichever version the user selects as the authoritative
   reference for API usage, patterns, and documentation.

**Why this matters**: Skills and documentation may reference outdated APIs. Always verify against the
actual library state to prevent integration errors.

## Available skills

Agentic coverage for this project is split across three plugin sources. Install the sollertia marketplace's `unity`
and `assets` plugins together with the ataraxis marketplace's `automation` plugin to make all skills available.

### Unity Editor skills (sollertia marketplace, `unity` plugin)

| Skill                            | Purpose                                                                    |
|----------------------------------|----------------------------------------------------------------------------|
| `/unity-mcp-environment-setup`   | Diagnoses the `localhost:8090` McpBridge relay                             |
| `/scenes`                        | Lists / opens / creates scenes and enumerates Unity assets                 |
| `/task-prefabs`                  | Generates, inspects, and validates task prefabs from YAML templates        |
| `/play-mode`                     | Enters, exits, and queries Editor Play Mode                                |
| `/mqtt-contract`                 | Documents every MQTT topic Unity publishes or subscribes to                |
| `/task-generator`                | Documents the `CreateTask` pipeline and hand-authored segment prefab layout|
| `/gimbl-framework`               | Reference for the embedded GIMBL VR framework (Actor, MQTT, Displays)      |
| `/scene-setup`                   | Configures Display rig, `SimulatedLinearTreadmill`, and UI feedback canvas |

### Configuration and experiment skills (sollertia marketplace, `assets` plugin)

| Skill                         | Purpose                                                                    |
|-------------------------------|----------------------------------------------------------------------------|
| `/task-templates`             | Authors and validates `TaskTemplate` YAML files under `Configurations/`    |
| `/experiment-configuration`   | Authors per-project experiment configurations that reference a template    |
| `/assets-mcp-environment-setup`     | Diagnoses the backing `slsa mcp` MCP server                                |

### Shared development skills (ataraxis marketplace, `automation` plugin)

| Skill                | Purpose                                                        |
|----------------------|----------------------------------------------------------------|
| `/explore-codebase`  | Builds a comprehensive codebase understanding at session start |
| `/csharp-style`      | Applies C# conventions, including Unity-specific patterns      |
| `/readme-style`      | Applies README conventions                                     |
| `/commit`            | Drafts style-compliant commit messages                         |
| `/skill-design`      | Authors new skills and CLAUDE.md files                         |
| `/project-layout`    | Applies directory-structure conventions (C# Unity archetype)   |

## Skill workflow guide

Combine skills for common tasks:

| Task type                        | Skills to invoke (in order)                                                |
|----------------------------------|----------------------------------------------------------------------------|
| Session start                    | `/explore-codebase`                                                        |
| Writing C# code                  | `/csharp-style` (plus `/mqtt-contract` for MQTT-adjacent changes)          |
| Authoring a new YAML template    | `/task-templates` (assets plugin), then `/task-prefabs` to validate        |
| Creating a task prefab           | `/task-prefabs` (generate → inspect → validate)                            |
| Modifying a segment prefab       | `/task-generator`, then `/task-prefabs` to revalidate                      |
| Adding or changing MQTT topics   | `/mqtt-contract` (cross-check with sollertia-experiment expectations)      |
| Setting up a scene for testing   | `/scenes`, then `/scene-setup` for displays + simulated treadmill          |
| Exercising a task in Play Mode   | `/play-mode`                                                               |
| Writing / updating README        | `/readme-style`                                                            |
| Writing commit messages          | `/commit`                                                                  |
| Creating or modifying skills     | `/skill-design`                                                            |

**Workflow examples:**

1. **New coding session**: Invoke `/explore-codebase` first to understand the project, then the style skill that
   matches the file type you will edit.

2. **Adding a new task template**: Invoke `/task-templates` (assets plugin) to author the YAML, then `/task-prefabs`
   to generate and validate the corresponding Unity prefab.

3. **Fixing a bug in `Task.cs`**: If unfamiliar with the codebase, invoke `/explore-codebase`. Then invoke
   `/csharp-style` and `/mqtt-contract` (if MQTT wiring is involved) before editing.

4. **Updating README documentation**: Invoke `/explore-codebase` to understand the current implementation, then
   `/readme-style`. Cross-reference every technical claim against actual source files before publishing.

## Related libraries

This project integrates with other Sollertia platform libraries:

| Library                      | Relationship          | Integration points                                    |
|------------------------------|-----------------------|-------------------------------------------------------|
| `sollertia-experiment`       | Data acquisition      | MQTT communication, cue sequence exchange, scene info |
| `sollertia-shared-assets`    | Configuration schemas | Task template and experiment configuration classes    |
| `GIMBL` (inlined)            | VR framework          | `ActorObject`, `MQTTChannel`, `Display` system        |

**When working on MQTT integration**, invoke the `/mqtt-contract` skill first — it is the source of truth for topic
names, payload shapes, and direction (publisher vs listener). The same topics must match `sollertia-experiment`
expectations on the other side.

## Project context

This is **sollertia-unity-tasks**, a Unity 6 project that provides VR behavioral experiment tasks for the Sollertia
platform's mesoscope data acquisition systems. It creates infinite corridor environments where animals navigate through
visual cue sequences while receiving stimuli based on behavior.

### Key areas

| Directory                                     | Purpose                                     |
|-----------------------------------------------|---------------------------------------------|
| `Assets/InfiniteCorridorTask/Scripts/`        | Core C# scripts for task logic              |
| `Assets/InfiniteCorridorTask/Scripts/Editor/` | `McpBridge` HTTP listener and MiniJson      |
| `Assets/InfiniteCorridorTask/Configurations/` | YAML task template files                    |
| `Assets/InfiniteCorridorTask/Prefabs/`        | Segment and zone prefabs                    |
| `Assets/InfiniteCorridorTask/Tasks/`          | Generated task prefabs                      |
| `Assets/UI-lick-reward/`                      | UI feedback system for lick/stimulus events |
| `Assets/Gimbl/`                               | Inlined GIMBL VR framework                  |
| `Assets/Scenes/`                              | Scene assets (including `ExperimentTemplate.unity`) |

### Architecture

- **Task system**: MonoBehaviour-based controller managing corridor generation and animal tracking
- **Zone system**: Hierarchical zone components (`StimulusTriggerZone`, `GuidanceZone`, `OccupancyZone`)
- **MQTT integration**: Type-safe channels for communication with sollertia-experiment
- **Configuration**: YAML-based task templates loaded at runtime
- **Prefab generation**: `CreateTask` editor tool and `McpBridge` relay create task prefabs from template files
- **Editor MCP bridge**: HTTP listener on `localhost:8090` that exposes 12 Unity Editor operations to MCP clients

### Key patterns

- **MQTT event system**: `MQTTChannel` and `MQTTChannel<T>` for type-safe messaging
- **Corridor teleportation**: Animals teleport between corridor combinations as they progress
- **Zone hierarchy**: Parent-child zone relationships determine stimulus behavior modes
- **Template-driven tasks**: YAML files define all task parameters; no hardcoded values

### Code standards

- CSharpier formatter with 120 character line limit
- EditorConfig enforcing Allman brace style and naming conventions
- XML documentation for all public and private members
- See `/csharp-style` for the complete convention reference

## Formatting

Run CSharpier before committing changes:

```bash
# Format all files
csharpier .

# Check without modifying (CI mode)
csharpier --check .
```

## Task template workflow

Task template files follow:
- **Naming convention**: `ProjectAbbreviation_TaskDescription.yaml` (e.g., `SSO_Merging.yaml`)
- **Header format**: Each file must include Project, Purpose, Layout, and Related fields as YAML comments
- **Template name derivation**: The template name and Unity scene name are derived from the filename

See `/task-templates` (assets plugin) for the complete YAML authoring reference, and `/task-prefabs` for the Unity
generation and validation surface.

### Validating templates against prefabs

The `unity:task-prefabs` skill owns `validate_prefab_against_template_tool`, which is the **only** mechanism for
verifying that prefabs match the template. The validator reports per-cue prefab existence plus per-segment match flags
for cue ordering, segment Z-length, and zone positions. Use it after any template or segment prefab change. Do not
attempt to reconstruct validation by reading prefab YAML manually — the MCP validator is the single source of truth.
Existing cue and segment prefabs are reused by `generate_task_prefab_tool`; force regeneration after a template edit
by deleting the affected prefabs via `delete_unity_asset_tool` (also owned by `/task-prefabs`) before re-running
generation.

## Creating new tasks

1. Create or modify a YAML task template file in `Assets/InfiniteCorridorTask/Configurations/`
2. Generate and validate via `/task-prefabs` (`generate_task_prefab_tool` → `validate_prefab_against_template_tool`)
3. Or use the Editor menu directly: `CreateTask → New Task`, select the YAML file, and save the generated prefab
4. Create a scene via `/scenes` (`create_scene_tool` with `task_prefab_path`) or from `ExperimentTemplate` in the Editor

## Testing workflow

1. Open a scene containing the task prefab via `/scenes` (`open_scene_tool`; pass `unsaved_changes="save"` or
   `"discard"` if the active scene is dirty — the tool returns an error otherwise so the user can choose)
2. Assign an Actor in the Task Inspector
3. Configure displays via `Window → Gimbl` (opens Settings, Actor, and Displays panels — see `/scene-setup`)
4. Use `SimulatedLinearTreadmill` for manual testing without hardware (see `/scene-setup`)
5. Enter Play Mode via `/play-mode` and exercise the task with keyboard input
6. Monitor MQTT topics with an external client for debugging (topic catalog in `/mqtt-contract`)
