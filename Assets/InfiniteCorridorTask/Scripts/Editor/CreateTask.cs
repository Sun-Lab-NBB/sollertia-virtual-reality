/// <summary>
/// Provides the CreateTask class that generates Task prefabs and matching test scenes from YAML configuration
/// files via the Unity Editor menu. Mirrors the agentic create_task pipeline in a single Editor entry point
/// so a YAML edit produces a runnable scene without leaving the Editor.
/// </summary>
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Gimbl;
using SL.Config;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SL.Tasks
{
    /// <summary>
    /// Creates Task prefabs from task template files via Unity Editor.
    /// Generates all corridor combinations by instantiating segment prefabs and configuring zones.
    /// </summary>
    public static class CreateTask
    {
        /// <summary>The tolerance for comparing measured prefab lengths against configured lengths.</summary>
        private const float LengthComparisonEpsilon = 0.01f;

        /// <summary>The project-relative folder where generated task prefabs are saved.</summary>
        private const string TasksFolder = "Assets/InfiniteCorridorTask/Tasks";

        /// <summary>The project-relative folder where generated task scenes are saved.</summary>
        private const string ScenesFolder = "Assets/Scenes";

        /// <summary>
        /// The project-relative path to the canonical scene template that scene generation copies from.
        /// </summary>
        /// <remarks>
        /// The template is hand-authored and contains the Display rig, Actor, and any other scene-wide
        /// infrastructure that every task scene needs. ``McpBridge`` protects this path from deletion via
        /// its protected-paths list so a regenerated scene always has a known-good source. Updating the
        /// path here requires a matching update to the protected-paths list in
        /// <c>McpBridge.DeleteProtectedPaths</c>.
        /// </remarks>
        private const string TemplateScenePath = "Assets/Scenes/ExperimentTemplate.unity";

        /// <summary>The project-relative root folder for every InfiniteCorridorTask-owned asset.</summary>
        private const string BaseFolder = "Assets/InfiniteCorridorTask";

        /// <summary>The project-relative folder containing per-task YAML configuration templates.</summary>
        private const string ConfigurationsFolder = BaseFolder + "/Configurations";

        /// <summary>The project-relative folder holding generated and hand-authored segment prefabs.</summary>
        private const string PrefabsFolder = BaseFolder + "/Prefabs";

        /// <summary>The project-relative folder holding shared cue prefabs.</summary>
        private const string CuesFolder = BaseFolder + "/Cues";

        /// <summary>The project-relative folder holding shared cue, floor, and wall materials.</summary>
        private const string MaterialsFolder = BaseFolder + "/Materials";

        /// <summary>The project-relative folder holding cue textures referenced by templates.</summary>
        private const string TexturesFolder = BaseFolder + "/Textures";

        /// <summary>The canonical reference material whose shader is reused by all generated cue materials.</summary>
        /// <remarks>
        /// This material lives in source control and is protected from deletion via the McpBridge's
        /// protected-paths list. Its shader (built-in fileID 10708 — a legacy diffuse variant) renders
        /// both walls of a cue correctly even when the Right wall uses a negative geometry scale to
        /// mirror its texture; the Standard shader breaks under negative scales, and Unlit shaders drop
        /// lighting altogether. The reference is loaded by <see cref="LoadReferenceCueShader"/> so a
        /// fresh project clone produces visually identical cues without needing a pre-existing legacy
        /// material to bootstrap from.
        /// </remarks>
        private const string CueShaderReferenceMaterialPath = MaterialsFolder + "/_CueShaderReference.mat";

        /// <summary>The vertical offset for trigger-zone GameObjects, slightly above the segment floor.</summary>
        private const float ZoneVerticalOffset = 0.505f;

        /// <summary>The vertical center for cue walls, segment walls, and the reset-zone marker.</summary>
        private const float WallVerticalCenter = 0.5f;

        /// <summary>
        /// The Z-axis depth of guidance-zone box colliders in interaction and occupancy zones, and of the
        /// thin boundary wall collider in collision zones.
        /// </summary>
        private const float GuidanceColliderDepth = 0.4f;

        /// <summary>
        /// Formats a cue's centimeter length as the suffix used in cue prefab and material filenames.
        /// Returns a culture-invariant string with up to two decimals and no trailing zeros (e.g., "30", "37.5").
        /// </summary>
        /// <param name="lengthCm">The cue length in centimeters.</param>
        /// <returns>The length label used inside ``Cue_{name}_{label}cm`` asset filenames.</returns>
        public static string FormatCueLengthLabel(float lengthCm) =>
            lengthCm.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>
        /// Computes the canonical prefab name for a trial's segment as ``TemplateName_TrialName``. The template
        /// name comes from the YAML filename (without extension) and the trial name is the key under
        /// ``trial_structures`` — both validated for filesystem-safe characters at template load time. Segments
        /// are therefore globally unique by construction across all templates and trivially identifiable in the
        /// Project window. Two trials that share identical geometry no longer collapse to a single prefab; the
        /// always-regenerate build flow makes that a deliberate non-issue.
        /// </summary>
        /// <param name="template">The task template owning the trial; supplies the template name.</param>
        /// <param name="trialName">The trial key under ``trial_structures``.</param>
        /// <returns>The canonical segment prefab name (without the ``.prefab`` extension).</returns>
        public static string CanonicalSegmentName(TaskTemplate template, string trialName) =>
            $"{template.templateName}_{trialName}";

        /// <summary>
        /// Deletes every segment prefab the supplied template claims ownership of so the subsequent build
        /// always produces a fresh segment tree, even if trial parameters changed under an unchanged trial
        /// name. Cue prefabs and cue materials are intentionally **not** removed: they are keyed by cue name
        /// and length only and are shared by every template that declares a matching cue, so deleting them
        /// here would clobber assets owned by other templates and invalidate their segment prefabs' cue
        /// references. Hand-authored prefabs (Padding, ResetZone, StimulusTriggerZone, OccupancyTriggerZone)
        /// are never derived from template data and are therefore also left untouched.
        /// </summary>
        /// <param name="template">The template whose owned segment prefabs are removed.</param>
        private static void CleanGeneratedSegments(TaskTemplate template)
        {
            // The final AssetDatabase.SaveAssets + Refresh in BuildSegmentPrefabs flushes these deletions
            // along with the subsequent cue and segment writes; skipping intermediate SaveAssets calls
            // here keeps the generation pipeline to a single project-wide reimport rather than three.
            foreach (KeyValuePair<string, TrialStructure> trialEntry in template.trialStructures)
            {
                string segmentName = CanonicalSegmentName(template, trialEntry.Key);
                AssetDatabase.DeleteAsset(Path.Combine(PrefabsFolder, $"{segmentName}.prefab"));
            }
        }

        /// <summary>
        /// Scans every template under ``Assets/InfiniteCorridorTask/Configurations/`` and verifies that no
        /// two templates declare a cue with the same ``(name, lengthCm)`` identity but different textures.
        /// </summary>
        /// <remarks>
        /// Cue prefabs and materials are filesystem-keyed as ``Cue_{name}_{length}cm`` and are deliberately
        /// shared across templates so generation stays cheap. The shared-asset model breaks down only when
        /// two templates declare the same cue identity with conflicting textures: whichever template runs
        /// first wins, the second template silently inherits the wrong texture, and downstream prefabs
        /// look correct on disk while rendering the wrong cue at runtime. The preflight closes that hole
        /// by failing the generation request before any cue prefab is written. The check is cheap (one
        /// YAML deserialization per template; the catalog is small) and runs on every generation call so
        /// drift introduced between runs is caught at the earliest possible moment.
        /// </remarks>
        /// <param name="errorMessage">
        /// Receives a human-readable description of every detected conflict on failure.
        /// </param>
        /// <returns>True when no conflicts are detected; false otherwise.</returns>
        private static bool ValidateCueDefinitionsAcrossTemplates(out string errorMessage)
        {
            errorMessage = null;

            string configurationsDirectory = Path.Combine(
                Application.dataPath,
                "InfiniteCorridorTask",
                "Configurations"
            );

            if (!Directory.Exists(configurationsDirectory))
            {
                // No configurations folder yet means there are no templates to compare; the per-template
                // load path will surface a clearer error if the folder is missing for the active request.
                return true;
            }

            string[] templateFiles = Directory
                .GetFiles(configurationsDirectory, "*.yaml", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(configurationsDirectory, "*.yml", SearchOption.TopDirectoryOnly))
                .ToArray();

            // Maps a canonical cue identity (``cueName|lengthLabel``) to the list of templates that
            // declare it, capturing each declaration's texture. A list (rather than a single slot) lets
            // the reporter list every contributing template when three or more templates collide on the
            // same identity, instead of silently dropping later declarations.
            Dictionary<string, List<(string Texture, string TemplateName)>> cueDefinitions =
                new Dictionary<string, List<(string, string)>>();

            foreach (string templateFile in templateFiles)
            {
                TaskTemplate template;
                try
                {
                    template = ConfigLoader.LoadTemplate(templateFile);
                }
                catch (Exception exception)
                {
                    errorMessage =
                        $"Cross-template cue-texture preflight aborted: failed to load "
                        + $"'{templateFile}': {exception.Message}";
                    return false;
                }

                foreach (Cue cue in template.cues)
                {
                    string key = $"{cue.name}|{FormatCueLengthLabel(cue.lengthCm)}cm";
                    if (!cueDefinitions.TryGetValue(key, out List<(string, string)> declarations))
                    {
                        declarations = new List<(string, string)>();
                        cueDefinitions[key] = declarations;
                    }
                    declarations.Add((cue.texture, template.templateName));
                }
            }

            List<string> conflicts = new List<string>();
            foreach (KeyValuePair<string, List<(string Texture, string TemplateName)>> entry in cueDefinitions)
            {
                HashSet<string> distinctTextures = new HashSet<string>(
                    entry.Value.Select(declaration => declaration.Texture),
                    StringComparer.Ordinal
                );

                if (distinctTextures.Count <= 1)
                {
                    continue;
                }

                string details = string.Join(
                    ", ",
                    entry.Value.Select(declaration => $"{declaration.TemplateName} -> '{declaration.Texture}'")
                );
                string identity = entry.Key.Replace("|", " at ");
                conflicts.Add($"Cue '{identity}': {details}");
            }

            if (conflicts.Count == 0)
            {
                return true;
            }

            errorMessage =
                "Cross-template cue-texture conflict detected. The following cue identities are declared "
                + "with more than one texture across the Configurations catalog; either rename the cue, "
                + "change its length, or unify the textures before regenerating:\n  - "
                + string.Join("\n  - ", conflicts);
            return false;
        }

        /// <summary>
        /// Creates a new Task prefab and a matching scene from a selected YAML configuration file. Save
        /// paths and asset names are auto-resolved from the template filename so the menu flow matches the
        /// MCP-driven pipeline: the prefab lands at ``Assets/InfiniteCorridorTask/Tasks/{templateName}.prefab``
        /// and the scene at ``Assets/Scenes/{templateName}.unity``. The user is only prompted for the
        /// template selection and, when an existing prefab or scene would be overwritten, a single
        /// confirmation dialog before any mutation occurs.
        /// </summary>
        /// <remarks>
        /// Rejects template selections outside ``Assets/InfiniteCorridorTask/Configurations/`` so the
        /// Editor surface matches the MCP surface (which is already hard-coded to that folder) and so the
        /// cross-template cue-texture preflight in <see cref="CreateFromTemplate"/> sees every template
        /// that can drive generation. Constraining the menu also keeps the runtime-resolved
        /// ``relativeConfigPath`` stored on the Task component well-formed: a YAML selected from outside
        /// ``Application.dataPath`` would otherwise yield a malformed path that breaks the runtime template
        /// lookup later. The scene generation step is the final phase so the user sees the prefab result
        /// before scene work begins, and so a failed prefab build short-circuits before any scene is touched.
        /// </remarks>
        [MenuItem("CreateTask/New Task")]
        public static void CreateNewTask()
        {
            // Normalizes to forward slashes so the prefix check works uniformly on Windows, where
            // ``Path.Combine`` returns mixed separators but ``EditorUtility.OpenFilePanel`` returns
            // forward slashes per Unity's documented behavior.
            string dataPath = Application.dataPath.Replace('\\', '/');
            string configurationsDirectory = Path.Combine(dataPath, "InfiniteCorridorTask", "Configurations")
                .Replace('\\', '/');

            string absoluteSelectedPath = EditorUtility
                .OpenFilePanel("Select Task Template YAML", configurationsDirectory, "yaml,yml")
                .Replace('\\', '/');

            if (string.IsNullOrEmpty(absoluteSelectedPath))
            {
                Debug.LogError("No configuration YAML file selected.");
                return;
            }

            // Enforces that templates live under ``Configurations/``. A trailing slash is required so a
            // sibling directory whose name begins with ``Configurations`` cannot satisfy the prefix.
            string configurationsPrefix = configurationsDirectory.TrimEnd('/') + "/";
            if (!absoluteSelectedPath.StartsWith(configurationsPrefix, StringComparison.Ordinal))
            {
                string message =
                    $"Selected template '{absoluteSelectedPath}' is outside the canonical Configurations "
                    + $"directory '{configurationsDirectory}'. Move the template into Configurations/ "
                    + "before generating; only files under that folder are visible to MCP-driven "
                    + "generation and to the cross-template cue-texture preflight.";
                Debug.LogError(message);
                return;
            }

            // Auto-resolves every downstream path from the template filename. The template name is also the
            // prefab basename, the scene basename, and the segment-prefab prefix, matching the MCP flow's
            // conventions so menu-generated and agent-generated assets are byte-equivalent.
            string templateName = Path.GetFileNameWithoutExtension(absoluteSelectedPath);
            string configPath = absoluteSelectedPath.Substring(dataPath.Length).TrimStart('/');
            string prefabSavePath = Path.Combine(TasksFolder, $"{templateName}.prefab").Replace('\\', '/');
            string sceneSavePath = Path.Combine(ScenesFolder, $"{templateName}.unity").Replace('\\', '/');

            // Confirms overwrite up front when either auto-resolved target already exists. Doing this
            // before any mutation lets the user cancel without leaving the project in a half-regenerated
            // state and keeps the destructive nature of the flow visible — auto-resolution removes the OS
            // file-panel's built-in overwrite prompt, so we have to surface it ourselves.
            List<string> existingTargets = new List<string>();
            if (File.Exists(prefabSavePath))
            {
                existingTargets.Add(prefabSavePath);
            }
            if (File.Exists(sceneSavePath))
            {
                existingTargets.Add(sceneSavePath);
            }
            if (existingTargets.Count > 0)
            {
                string dialogMessage =
                    $"The following assets will be replaced:\n\n  {string.Join("\n  ", existingTargets)}"
                    + "\n\nAny hand-edits to those files will be lost. Continue?";
                bool proceed = EditorUtility.DisplayDialog(
                    title: $"Regenerate '{templateName}' Assets",
                    message: dialogMessage,
                    ok: "Replace",
                    cancel: "Cancel"
                );
                if (!proceed)
                {
                    Debug.Log("Task generation cancelled.");
                    return;
                }
            }

            // Ensures the Tasks output folder exists before CreateFromTemplate writes the prefab. The
            // Scenes folder is part of the project skeleton and is assumed to exist; if it does not,
            // CreateSceneFromTemplate's CopyAsset call will surface the failure.
            if (!AssetDatabase.IsValidFolder(TasksFolder))
            {
                AssetDatabase.CreateFolder(BaseFolder, "Tasks");
            }

            string absoluteTemplatePath = Path.Combine(Application.dataPath, configPath);
            string prefabResult = CreateFromTemplate(absoluteTemplatePath, configPath, prefabSavePath);
            Debug.Log(prefabResult);

            // Skips scene generation when prefab creation failed. The scene step depends on the prefab
            // existing on disk and would otherwise emit a confusing "task prefab not found" warning.
            if (!prefabResult.StartsWith("success:", StringComparison.Ordinal))
            {
                return;
            }

            // Defers to Unity's built-in unsaved-changes dialog before opening the new scene. Returning
            // false means the user pressed Cancel, which we treat as an abort of just the scene step; the
            // already-generated prefab is left in place so a follow-up run can finish the scene.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                string message =
                    $"Scene generation cancelled — unsaved changes in the active scene were not handled. "
                    + $"The prefab is at {prefabSavePath}; rerun the menu to regenerate the scene.";
                Debug.Log(message);
                return;
            }

            SceneCreationResult sceneResult = CreateSceneFromTemplate(
                sceneSavePath: sceneSavePath,
                taskPrefabPath: prefabSavePath,
                overwriteExisting: true
            );

            if (!sceneResult.Success)
            {
                Debug.LogError($"error: {sceneResult.Message}");
                return;
            }

            Debug.Log($"success: {sceneResult.Message}");
        }

        /// <summary>
        /// Creates a Task prefab from a YAML template file and saves it to the specified path.
        /// This is the parameterized entry point used by both the Editor menu and the MCP bridge.
        /// </summary>
        /// <param name="absoluteTemplatePath">The absolute path to the YAML template file.</param>
        /// <param name="relativeConfigPath">
        /// The config path relative to Application.dataPath, stored on the Task component for runtime loading.
        /// </param>
        /// <param name="savePath">
        /// The project-relative path where the prefab will be saved (e.g., "Assets/.../Task.prefab").
        /// </param>
        /// <returns>A status message describing success or the error encountered.</returns>
        public static string CreateFromTemplate(string absoluteTemplatePath, string relativeConfigPath, string savePath)
        {
            // Runs the cross-template cue-texture preflight before any mutation. Cue prefabs are shared
            // across templates by ``(name, lengthCm)`` only, so two templates that declare a cue with the
            // same name and length but different textures would silently overwrite each other depending
            // on generation order. The preflight aborts here, before ``CleanGeneratedSegments`` or any
            // cue/segment build runs, so the project state stays consistent until the conflict is resolved.
            if (!ValidateCueDefinitionsAcrossTemplates(out string preflightError))
            {
                return $"error: {preflightError}";
            }

            TaskTemplate template;
            try
            {
                template = ConfigLoader.LoadTemplate(absoluteTemplatePath);
            }
            catch (Exception exception)
            {
                return $"error: {exception.Message}";
            }

            // Wipes any segment prefabs this template previously generated so trial-parameter edits never
            // result in stale segment geometry surviving under an unchanged ``TemplateName_TrialName`` filename.
            // Cue prefabs and materials are deliberately preserved because they are shared across templates
            // by cue name and length; ``BuildCuePrefabs`` rebuilds only the cues that are still missing.
            CleanGeneratedSegments(template);

            if (!BuildCuePrefabs(template))
            {
                return "error: Failed to build cue prefabs.";
            }

            if (!BuildSegmentPrefabs(template))
            {
                return "error: Failed to build segment prefabs.";
            }

            string paddingPath = Path.Combine(PrefabsFolder, $"{template.vrEnvironment.paddingPrefabName}.prefab");
            GameObject padding = AssetDatabase.LoadAssetAtPath<GameObject>(paddingPath);

            if (padding == null)
            {
                return $"error: No padding found at {paddingPath}";
            }

            string[] trialNames = template.GetTrialNames();
            int trialCount = trialNames.Length;

            // Loads segment prefabs by their canonical ``TemplateName_TrialName`` filename.
            GameObject[] segmentPrefabs = new GameObject[trialCount];
            TrialStructure[] trials = new TrialStructure[trialCount];
            for (int i = 0; i < trialCount; i++)
            {
                trials[i] = template.trialStructures[trialNames[i]];
                string canonicalName = CanonicalSegmentName(template, trialNames[i]);
                string segmentPath = Path.Combine(PrefabsFolder, $"{canonicalName}.prefab");
                segmentPrefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>(segmentPath);

                if (segmentPrefabs[i] == null)
                {
                    return $"error: No segment found at {segmentPath}";
                }
            }

            float[] measuredSegmentLengths = new float[segmentPrefabs.Length];
            for (int i = 0; i < segmentPrefabs.Length; i++)
            {
                measuredSegmentLengths[i] = Utility.GetPrefabLength(segmentPrefabs[i]);
            }
            float[] segmentLengths = template.GetSegmentLengthsUnity();

            for (int i = 0; i < trialCount; i++)
            {
                if (Mathf.Abs(measuredSegmentLengths[i] - segmentLengths[i]) > LengthComparisonEpsilon)
                {
                    string message =
                        $"For trial {trialNames[i]}, there is a mismatch between the prefab "
                        + $"length ({measuredSegmentLengths[i]}) and the sum of all the cue "
                        + $"lengths ({segmentLengths[i]}). Using {segmentLengths[i]} for the "
                        + "length of the segment.";
                    Debug.LogWarning(message);
                }
            }

            int depth = template.vrEnvironment.segmentsPerCorridor;
            float paddingZShift = depth * Mathf.Min(segmentLengths) - 1;

            string taskName = Path.GetFileNameWithoutExtension(savePath);
            GameObject taskGameObject = new GameObject(taskName);
            Task taskScript = taskGameObject.AddComponent<Task>();
            taskScript.requireInteraction = true;
            taskScript.configPath = relativeConfigPath;

            int[] corridorSegments = new int[depth];
            float currentCorridorX = 0;
            float corridorXShift = template.vrEnvironment.CorridorSpacingUnity;

            for (int i = 0; i < Mathf.Pow(trialCount, depth); i++)
            {
                for (int j = 0; j < depth; j++)
                {
                    corridorSegments[j] = i / (int)Mathf.Pow(trialCount, depth - j - 1) % trialCount;
                }

                GameObject corridor = new GameObject($"Corridor{string.Join("", corridorSegments)}");
                corridor.transform.SetParent(taskGameObject.transform);
                corridor.transform.localPosition = new Vector3(currentCorridorX, 0, 0);

                float zShift = 0;
                for (int j = 0; j < depth; j++)
                {
                    int segment = corridorSegments[j];
                    GameObject instance = PrefabUtility.InstantiatePrefab(segmentPrefabs[segment]) as GameObject;

                    // Only the first segment in each corridor should have a stimulus trigger zone
                    // and reset zone since the later segments are just for visual illusion
                    if (j > 0)
                    {
                        StimulusTriggerZone stimulusTriggerZone =
                            instance.GetComponentInChildren<StimulusTriggerZone>();
                        if (stimulusTriggerZone != null)
                        {
                            UnityEngine.Object.DestroyImmediate(stimulusTriggerZone.gameObject);
                        }

                        ResetZone resetZone = instance.GetComponentInChildren<ResetZone>();
                        if (resetZone != null)
                        {
                            UnityEngine.Object.DestroyImmediate(resetZone.gameObject);
                        }
                    }
                    else
                    {
                        // For the first segment, sets showBoundary from the trial's visibility setting.
                        StimulusTriggerZone stimulusTriggerZone =
                            instance.GetComponentInChildren<StimulusTriggerZone>();
                        if (stimulusTriggerZone != null)
                        {
                            stimulusTriggerZone.showBoundary = trials[segment].showStimulusCollisionBoundary;
                        }
                    }

                    instance.transform.SetParent(corridor.transform, worldPositionStays: false);
                    instance.transform.localPosition += new Vector3(0, 0, zShift);
                    zShift += segmentLengths[segment];
                }

                GameObject paddingInstance = PrefabUtility.InstantiatePrefab(padding) as GameObject;
                paddingInstance.transform.SetParent(corridor.transform, worldPositionStays: false);
                paddingInstance.transform.localPosition += new Vector3(0, 0, paddingZShift);

                currentCorridorX += corridorXShift;
            }

            PrefabUtility.SaveAsPrefabAsset(taskGameObject, savePath);
            UnityEngine.Object.DestroyImmediate(taskGameObject);

            return $"success: Task prefab saved to {savePath}";
        }

        /// <summary>
        /// Creates a new scene by copying the canonical experiment template scene, optionally instantiating a
        /// task prefab into it, and ensuring every supported controller (LinearTreadmill and
        /// SimulatedLinearTreadmill) is present in the scene so it can be driven by either real or keyboard
        /// input out of the box. Controller creation runs through <see cref="MainWindow.EnsureControllers"/>
        /// so the new and Task-Parameters-driven paths share the same logic. The new scene is opened in the
        /// Editor and saved on disk before the call returns. Callers are responsible for resolving any
        /// unsaved changes in the currently open scene before invoking this method, since the menu flow uses
        /// Unity's native dialog while the MCP flow uses an explicit policy argument.
        /// </summary>
        /// <param name="sceneSavePath">The project-relative path where the new scene file is written.</param>
        /// <param name="taskPrefabPath">
        /// The project-relative path to a task prefab to instantiate in the scene, or an empty string to
        /// create the scene without any task prefab. A non-empty path that does not resolve to a loadable
        /// prefab still yields a successful result with <see cref="SceneCreationResult.TaskPrefabNotFound"/>
        /// set so callers can surface the discrepancy without rolling back the scene.
        /// </param>
        /// <param name="overwriteExisting">
        /// When true, an existing scene at <paramref name="sceneSavePath"/> is deleted before the template is
        /// copied. Use this from interactive flows that have already confirmed the overwrite with the user;
        /// pass false from automated callers that should refuse to clobber existing scenes.
        /// </param>
        /// <returns>A <see cref="SceneCreationResult"/> describing the outcome.</returns>
        public static SceneCreationResult CreateSceneFromTemplate(
            string sceneSavePath,
            string taskPrefabPath,
            bool overwriteExisting
        )
        {
            SceneCreationResult result = new SceneCreationResult();

            if (string.IsNullOrEmpty(sceneSavePath))
            {
                result.Message = "Scene save path must not be null or empty.";
                return result;
            }

            if (!File.Exists(TemplateScenePath))
            {
                result.Message = $"Template scene not found at: {TemplateScenePath}";
                return result;
            }

            if (File.Exists(sceneSavePath))
            {
                if (!overwriteExisting)
                {
                    result.Message = $"Scene already exists at: {sceneSavePath}";
                    return result;
                }

                if (!AssetDatabase.DeleteAsset(sceneSavePath))
                {
                    result.Message = $"Failed to delete existing scene at: {sceneSavePath}";
                    return result;
                }
            }

            if (!AssetDatabase.CopyAsset(TemplateScenePath, sceneSavePath))
            {
                result.Message = $"Failed to copy template scene to: {sceneSavePath}";
                return result;
            }
            AssetDatabase.Refresh();

            EditorSceneManager.OpenScene(sceneSavePath);

            // Instantiates the task prefab when one was requested. A missing prefab is reported as a
            // non-fatal warning so the rest of the pipeline (controller add, scene save) still runs and the
            // user is left with a usable scene that just lacks the task hierarchy.
            if (!string.IsNullOrEmpty(taskPrefabPath))
            {
                GameObject taskPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(taskPrefabPath);
                if (taskPrefab != null)
                {
                    PrefabUtility.InstantiatePrefab(taskPrefab);
                }
                else
                {
                    result.TaskPrefabNotFound = true;
                }
            }

            Scene activeScene = SceneManager.GetActiveScene();
            bool simulatedExistedBeforeEnsure = Resources
                .FindObjectsOfTypeAll<SimulatedLinearTreadmill>()
                .Any(existing => existing.gameObject.scene == activeScene);
            MainWindow.EnsureControllers();
            // Applies defaults synchronously so the new scene is fully defaulted before this method returns.
            MainWindow.EnsureMqttDefaults();
            MainWindow.SyncDisplayBrightnessToSettings();
            result.SimulatedControllerAdded = !simulatedExistedBeforeEnsure;

            EditorSceneManager.SaveScene(activeScene);

            result.Success = true;
            if (result.TaskPrefabNotFound)
            {
                result.Message = $"Scene saved to {sceneSavePath} but task prefab was not found at: {taskPrefabPath}";
            }
            else
            {
                result.Message = $"Scene saved to {sceneSavePath}";
            }
            return result;
        }

        /// <summary>
        /// Resolves the shader used by generated cue materials, preferring the committed reference
        /// material at <see cref="CueShaderReferenceMaterialPath"/>. Falls back to any pre-existing
        /// hand-authored ``Cue*.mat`` material if the reference is missing, then to
        /// ``Shader.Find("Legacy Shaders/Diffuse")``, and finally to the default Standard shader. The
        /// fallbacks exist for resilience; the committed reference material is the canonical source.
        /// </summary>
        /// <param name="materialsPath">The directory under which to search for fallback materials.</param>
        /// <returns>The shader to use for newly generated cue materials.</returns>
        private static Shader LoadReferenceCueShader(string materialsPath)
        {
            Material reference = AssetDatabase.LoadAssetAtPath<Material>(CueShaderReferenceMaterialPath);
            if (reference != null && reference.shader != null)
            {
                return reference.shader;
            }
            string message =
                $"BuildCuePrefabs: canonical shader reference '{CueShaderReferenceMaterialPath}' is missing; "
                + "falling back to a hand-authored Cue*.mat material or Shader.Find. Restore the "
                + "reference material to guarantee consistent cue rendering across machines.";
            Debug.LogWarning(message);

            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { materialsPath.TrimEnd('/') });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (
                    fileName.StartsWith("Cue", StringComparison.Ordinal)
                    && !fileName.StartsWith("Cue_", StringComparison.Ordinal)
                )
                {
                    Material fallback = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (fallback != null && fallback.shader != null)
                    {
                        return fallback.shader;
                    }
                }
            }
            return Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Standard");
        }

        /// <summary>
        /// Creates cue prefabs and accompanying materials for cues that do not yet exist under the
        /// ``Cues/`` and ``Materials/`` folders. Cue assets are deliberately shared across templates by cue
        /// name and length, so this method is idempotent: a cue already on disk is left untouched and reused
        /// by every template that declares it.
        /// </summary>
        /// <param name="template">The loaded task template.</param>
        /// <returns>True if all required cue prefabs were built or already exist, false on error.</returns>
        private static bool BuildCuePrefabs(TaskTemplate template)
        {
            float cmPerUnit = template.vrEnvironment.cmPerUnityUnit;

            // Inherits the shader from the project's historical hand-authored cue materials so generated
            // materials render identically to the originals. The reference material lives in the same
            // Materials/ folder and uses the legacy diffuse shader that handles the Right wall's negative
            // X scale correctly (the modern Standard and Unlit shaders both fail at this — Standard
            // breaks lit normals under inverted geometry, Unlit drops lighting entirely).
            Shader cueShader = LoadReferenceCueShader(MaterialsFolder + "/");

            // Ensures the Cues directory exists
            if (!AssetDatabase.IsValidFolder(CuesFolder))
            {
                AssetDatabase.CreateFolder(BaseFolder, "Cues");
            }

            Mesh quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

            foreach (Cue cue in template.cues)
            {
                // Encodes the cue length in the asset filenames so cues that share a letter across templates
                // (e.g., A at 30 cm in MF vs A at 40 cm in SSO) resolve to distinct prefabs and materials.
                string lengthLabel = FormatCueLengthLabel(cue.lengthCm);
                string cueAssetStem = $"Cue_{cue.name}_{lengthLabel}cm";
                string cuePrefabPath = Path.Combine(CuesFolder, $"{cueAssetStem}.prefab");

                if (AssetDatabase.LoadAssetAtPath<GameObject>(cuePrefabPath) != null)
                {
                    continue;
                }

                float lengthUnity = cue.LengthUnity(cmPerUnit);

                // Loads the shared texture once for both material variants.
                Texture2D cueTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    Path.Combine(TexturesFolder, cue.texture)
                );
                if (cueTexture == null)
                {
                    Debug.LogError($"BuildCuePrefabs: Failed to load texture '{cue.texture}'.");
                    return false;
                }

                // Matches the assembly used by the project's originally hand-authored cue prefabs:
                // Legacy Shaders/Diffuse renders both walls with consistent per-pixel diffuse lighting
                // even when the Right wall uses a negative geometry scale to mirror the texture. The
                // modern Standard shader breaks under negative scales (the lit normal does not invert
                // with mesh winding), and Unlit shaders drop lighting entirely. Legacy/Diffuse retains
                // the lit corridor feel while keeping the texture-mirror trick a single-material affair.
                string materialPath = Path.Combine(MaterialsFolder, $"{cueAssetStem}.mat");
                Material cueMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (cueMaterial == null)
                {
                    cueMaterial = new Material(cueShader);
                    cueMaterial.name = cueAssetStem;
                    cueMaterial.SetTexture("_MainTex", cueTexture);
                    AssetDatabase.CreateAsset(cueMaterial, materialPath);
                }

                // Creates cue GameObject with Left and Right Quad children. The Right wall uses a
                // negative X scale to mirror the texture along the horizontal axis so directional
                // patterns read forward from both sides of the corridor; Legacy/Diffuse keeps the
                // wall correctly lit despite the inverted geometry.
                GameObject cueGameObject = new GameObject(cueAssetStem);

                GameObject right = new GameObject("Right");
                right.transform.SetParent(cueGameObject.transform);
                right.transform.localPosition = new Vector3(0.49f, WallVerticalCenter, lengthUnity / 2f);
                right.transform.localRotation = Quaternion.Euler(0, 90, 0);
                right.transform.localScale = new Vector3(-lengthUnity, 1, 1);
                right.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                right.AddComponent<MeshRenderer>().sharedMaterial = cueMaterial;

                GameObject left = new GameObject("Left");
                left.transform.SetParent(cueGameObject.transform);
                left.transform.localPosition = new Vector3(-0.49f, WallVerticalCenter, lengthUnity / 2f);
                left.transform.localRotation = Quaternion.Euler(0, -90, 0);
                left.transform.localScale = new Vector3(lengthUnity, 1, 1);
                left.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                left.AddComponent<MeshRenderer>().sharedMaterial = cueMaterial;

                PrefabUtility.SaveAsPrefabAsset(cueGameObject, cuePrefabPath);
                UnityEngine.Object.DestroyImmediate(cueGameObject);

                Debug.Log($"BuildCuePrefabs: Created {cuePrefabPath}");
            }

            // The cue prefabs are immediately discoverable via AssetDatabase.LoadAssetAtPath because
            // PrefabUtility.SaveAsPrefabAsset registers each new asset on the spot. The project-wide
            // SaveAssets + Refresh that BuildSegmentPrefabs runs at the end of the pipeline flushes
            // every cue and segment write together, so the intermediate flush that used to live here
            // is redundant.
            return true;
        }

        /// <summary>
        /// Creates a segment prefab for every trial structure declared by the template, naming each one
        /// ``TemplateName_TrialName.prefab``. Each segment prefab contains cue instances, floor, walls, and
        /// trigger/reset zones derived from the trial structure. Callers must invoke
        /// ``CleanGeneratedSegments`` first; this method unconditionally writes to the segment prefab path
        /// and assumes nothing exists at that location.
        /// </summary>
        /// <param name="template">The loaded task template.</param>
        /// <returns>True if all segment prefabs were built successfully, false on error.</returns>
        private static bool BuildSegmentPrefabs(TaskTemplate template)
        {
            float cmPerUnit = template.vrEnvironment.cmPerUnityUnit;
            float cueOffsetUnity = template.vrEnvironment.CueOffsetUnity;
            Dictionary<string, Cue> cueMap = template.GetCueByName();

            Mesh quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            Mesh planeMesh = Resources.GetBuiltinResource<Mesh>("New-Plane.fbx");

            Material floorMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                Path.Combine(MaterialsFolder, "Floor.mat")
            );
            Material wallMaterial = AssetDatabase.LoadAssetAtPath<Material>(Path.Combine(MaterialsFolder, "Wall.mat"));

            if (floorMaterial == null || wallMaterial == null)
            {
                Debug.LogError("BuildSegmentPrefabs: Missing Floor.mat or Wall.mat.");
                return false;
            }

            GameObject stimulusZonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                Path.Combine(PrefabsFolder, "StimulusTriggerZone.prefab")
            );
            GameObject occupancyZonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                Path.Combine(PrefabsFolder, "OccupancyTriggerZone.prefab")
            );
            GameObject resetZonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                Path.Combine(PrefabsFolder, "ResetZone.prefab")
            );

            foreach (KeyValuePair<string, TrialStructure> trialEntry in template.trialStructures)
            {
                string trialName = trialEntry.Key;
                TrialStructure trial = trialEntry.Value;
                string canonicalSegmentName = CanonicalSegmentName(template, trialName);
                string segmentPrefabPath = Path.Combine(PrefabsFolder, $"{canonicalSegmentName}.prefab");

                float totalLengthUnity = trial.cueSequence.Sum(cueName => cueMap[cueName].LengthUnity(cmPerUnit));

                // Creates segment root with cue offset; the root takes the canonical prefab name so the
                // in-prefab m_Name matches the filename, matching the cue-side convention.
                GameObject segmentGameObject = new GameObject(canonicalSegmentName);
                segmentGameObject.transform.localPosition = new Vector3(0, 0, -cueOffsetUnity);

                float cumulativeZ = 0f;
                foreach (string cueName in trial.cueSequence)
                {
                    Cue cue = cueMap[cueName];
                    float cueLengthUnity = cue.LengthUnity(cmPerUnit);

                    string lengthLabel = FormatCueLengthLabel(cue.lengthCm);
                    string cuePrefabPath = Path.Combine(CuesFolder, $"Cue_{cueName}_{lengthLabel}cm.prefab");
                    GameObject cuePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cuePrefabPath);

                    if (cuePrefab == null)
                    {
                        Debug.LogError($"BuildSegmentPrefabs: Missing cue prefab at {cuePrefabPath}.");
                        UnityEngine.Object.DestroyImmediate(segmentGameObject);
                        return false;
                    }

                    GameObject cueInstance = PrefabUtility.InstantiatePrefab(cuePrefab) as GameObject;
                    cueInstance.name = $"Cue{cueName}";
                    cueInstance.transform.SetParent(segmentGameObject.transform);
                    cueInstance.transform.localPosition = new Vector3(0, 0, cumulativeZ);

                    cumulativeZ += cueLengthUnity;
                }

                GameObject floor = new GameObject("Floor");
                floor.transform.SetParent(segmentGameObject.transform);
                floor.transform.localPosition = new Vector3(0, 0, totalLengthUnity / 2f);
                floor.transform.localScale = new Vector3(0.1f, 1, totalLengthUnity / 10f);
                floor.AddComponent<MeshFilter>().sharedMesh = planeMesh;
                floor.AddComponent<MeshRenderer>().sharedMaterial = floorMaterial;

                GameObject walls = new GameObject("Walls");
                walls.transform.SetParent(segmentGameObject.transform);
                walls.transform.localPosition = Vector3.zero;

                GameObject leftWall = new GameObject("LeftWall");
                leftWall.transform.SetParent(walls.transform);
                leftWall.transform.localPosition = new Vector3(-0.5f, WallVerticalCenter, totalLengthUnity / 2f);
                leftWall.transform.localRotation = Quaternion.Euler(0, -90, 0);
                leftWall.transform.localScale = new Vector3(totalLengthUnity, 1, 1);
                leftWall.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                leftWall.AddComponent<MeshRenderer>().sharedMaterial = wallMaterial;

                GameObject rightWall = new GameObject("RightWall");
                rightWall.transform.SetParent(walls.transform);
                rightWall.transform.localPosition = new Vector3(0.5f, WallVerticalCenter, totalLengthUnity / 2f);
                rightWall.transform.localRotation = Quaternion.Euler(0, 90, 0);
                rightWall.transform.localScale = new Vector3(totalLengthUnity, 1, 1);
                rightWall.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                rightWall.AddComponent<MeshRenderer>().sharedMaterial = wallMaterial;

                float zoneStartUnity = trial.stimulusTriggerZoneStartCm / cmPerUnit;
                float zoneEndUnity = trial.stimulusTriggerZoneEndCm / cmPerUnit;
                float zoneCenterUnity = (zoneStartUnity + zoneEndUnity) / 2f;
                float zoneSizeUnity = zoneEndUnity - zoneStartUnity;
                float stimulusLocationUnity = trial.stimulusLocationCm / cmPerUnit;

                if (
                    string.Equals(trial.triggerType, "interaction", StringComparison.Ordinal)
                    && stimulusZonePrefab != null
                )
                {
                    PlaceInteractionZone(
                        parent: segmentGameObject,
                        zonePrefab: stimulusZonePrefab,
                        trialName: trialName,
                        zoneCenterUnity: zoneCenterUnity,
                        zoneSizeUnity: zoneSizeUnity,
                        stimulusLocationUnity: stimulusLocationUnity,
                        showBoundary: trial.showStimulusCollisionBoundary
                    );
                }
                else if (
                    string.Equals(trial.triggerType, "collision", StringComparison.Ordinal)
                    && stimulusZonePrefab != null
                )
                {
                    PlaceCollisionZone(
                        parent: segmentGameObject,
                        zonePrefab: stimulusZonePrefab,
                        trialName: trialName,
                        stimulusLocationUnity: stimulusLocationUnity,
                        showBoundary: trial.showStimulusCollisionBoundary
                    );
                }
                else if (
                    occupancyZonePrefab != null
                    && (
                        string.Equals(trial.triggerType, "occupancy_disarm", StringComparison.Ordinal)
                        || string.Equals(trial.triggerType, "occupancy_arm", StringComparison.Ordinal)
                        || string.Equals(trial.triggerType, "occupancy_trigger", StringComparison.Ordinal)
                    )
                )
                {
                    PlaceOccupancyZone(
                        parent: segmentGameObject,
                        zonePrefab: occupancyZonePrefab,
                        trialName: trialName,
                        triggerMode: ResolveOccupancyTriggerMode(trial.triggerType),
                        zoneCenterUnity: zoneCenterUnity,
                        zoneSizeUnity: zoneSizeUnity,
                        stimulusLocationUnity: stimulusLocationUnity,
                        occupancyDurationMs: trial.occupancyDurationMs,
                        showBoundary: trial.showStimulusCollisionBoundary
                    );
                }

                // Places ResetZone at the animal's per-corridor spawn point. The segment root is shifted
                // upstream by cueOffsetUnity, so a local Z of cueOffsetUnity places the reset zone at
                // world Z = 0 — exactly where the actor teleports to on every lap restart.
                if (resetZonePrefab != null)
                {
                    GameObject resetZone = PrefabUtility.InstantiatePrefab(resetZonePrefab) as GameObject;
                    resetZone.transform.SetParent(segmentGameObject.transform);
                    resetZone.transform.localPosition = new Vector3(0, WallVerticalCenter, cueOffsetUnity);
                }

                PrefabUtility.SaveAsPrefabAsset(segmentGameObject, segmentPrefabPath);
                UnityEngine.Object.DestroyImmediate(segmentGameObject);

                Debug.Log($"BuildSegmentPrefabs: Created {segmentPrefabPath}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return true;
        }

        /// <summary>
        /// Instantiates and configures a StimulusTriggerZone (interaction mode) within a segment.
        /// Positions the root collider to span the trigger zone and starts the GuidanceRegion at the stimulus
        /// location, so entering the region under guidance delivers the stimulus at that exact location.
        /// </summary>
        /// <param name="parent">The parent segment GameObject.</param>
        /// <param name="zonePrefab">The StimulusTriggerZone prefab to instantiate.</param>
        /// <param name="trialName">The owning trial's name; published as the stimulus identifier when fired.</param>
        /// <param name="zoneCenterUnity">The center position of the trigger zone in Unity units.</param>
        /// <param name="zoneSizeUnity">The size of the trigger zone in Unity units.</param>
        /// <param name="stimulusLocationUnity">The stimulus location in Unity units.</param>
        /// <param name="showBoundary">Determines whether the zone boundary is visible.</param>
        private static void PlaceInteractionZone(
            GameObject parent,
            GameObject zonePrefab,
            string trialName,
            float zoneCenterUnity,
            float zoneSizeUnity,
            float stimulusLocationUnity,
            bool showBoundary
        )
        {
            GameObject zone = PrefabUtility.InstantiatePrefab(zonePrefab) as GameObject;
            zone.transform.SetParent(parent.transform);
            zone.transform.localPosition = new Vector3(0, ZoneVerticalOffset, zoneCenterUnity);

            ConfigureRootZoneCollider(zone, zoneSizeUnity);

            GuidanceZone guidanceZone = zone.GetComponentInChildren<GuidanceZone>();
            if (guidanceZone != null)
            {
                BoxCollider guidanceCollider = guidanceZone.GetComponent<BoxCollider>();
                if (guidanceCollider != null)
                {
                    // Anchors the guidance region's leading edge on the stimulus location, so an animal running
                    // under guidance receives the stimulus at exactly the location the template declares. The
                    // region extends forward from there, and its far edge carries no behavioral meaning.
                    guidanceCollider.size = new Vector3(1, 1, GuidanceColliderDepth);
                    guidanceCollider.center = new Vector3(
                        0,
                        0,
                        stimulusLocationUnity - zoneCenterUnity + GuidanceColliderDepth / 2f
                    );
                }
            }

            StimulusTriggerZone stimulusZone = zone.GetComponent<StimulusTriggerZone>();
            if (stimulusZone != null)
            {
                stimulusZone.triggerMode = TriggerMode.Interaction;
                stimulusZone.showBoundary = showBoundary;
                stimulusZone.trialName = trialName;
            }
        }

        /// <summary>Maps an occupancy trigger_type literal to its matching occupancy TriggerMode.</summary>
        /// <param name="triggerType">The trial's trigger_type string (an occupancy literal).</param>
        /// <returns>The matching occupancy TriggerMode; defaults to OccupancyDisarm.</returns>
        private static TriggerMode ResolveOccupancyTriggerMode(string triggerType) =>
            triggerType switch
            {
                "occupancy_arm" => TriggerMode.OccupancyArm,
                "occupancy_trigger" => TriggerMode.OccupancyTrigger,
                _ => TriggerMode.OccupancyDisarm,
            };

        /// <summary>
        /// Instantiates and configures a StimulusTriggerZone in collision mode within a segment. Reuses the
        /// interaction prefab, strips its GuidanceRegion child, and positions the root collider as a thin
        /// invisible wall at the stimulus location that fires the stimulus unconditionally on crossing.
        /// </summary>
        /// <param name="parent">The parent segment GameObject.</param>
        /// <param name="zonePrefab">The StimulusTriggerZone prefab to instantiate.</param>
        /// <param name="trialName">The owning trial's name; published as the stimulus identifier when fired.</param>
        /// <param name="stimulusLocationUnity">The wall (stimulus) location in Unity units.</param>
        /// <param name="showBoundary">Determines whether the wall is visible.</param>
        private static void PlaceCollisionZone(
            GameObject parent,
            GameObject zonePrefab,
            string trialName,
            float stimulusLocationUnity,
            bool showBoundary
        )
        {
            GameObject zone = PrefabUtility.InstantiatePrefab(zonePrefab) as GameObject;
            zone.transform.SetParent(parent.transform);
            zone.transform.localPosition = new Vector3(0, ZoneVerticalOffset, stimulusLocationUnity);

            // Collision uses a thin invisible wall at the stimulus location as its root trigger collider.
            ConfigureRootZoneCollider(zone, GuidanceColliderDepth);

            // Collision has no sensor or guidance, so removes the interaction prefab's GuidanceRegion child.
            GuidanceZone guidanceZone = zone.GetComponentInChildren<GuidanceZone>();
            if (guidanceZone != null)
            {
                UnityEngine.Object.DestroyImmediate(guidanceZone.gameObject);
            }

            StimulusTriggerZone stimulusZone = zone.GetComponent<StimulusTriggerZone>();
            if (stimulusZone != null)
            {
                stimulusZone.triggerMode = TriggerMode.Collision;
                stimulusZone.showBoundary = showBoundary;
                stimulusZone.trialName = trialName;
            }
        }

        /// <summary>
        /// Instantiates and configures an OccupancyTriggerZone within a segment.
        /// The root is positioned at the stimulus boundary (past the occupancy zone).
        /// The OccupancyRegion child covers the start-to-end range where the animal must wait.
        /// </summary>
        /// <param name="parent">The parent segment GameObject.</param>
        /// <param name="zonePrefab">The OccupancyTriggerZone prefab to instantiate.</param>
        /// <param name="trialName">The owning trial's name; published as the stimulus identifier when fired.</param>
        /// <param name="triggerMode">The occupancy trigger mode (disarm, arm, or trigger) applied to the zone.</param>
        /// <param name="zoneCenterUnity">The center position of the occupancy zone in Unity units.</param>
        /// <param name="zoneSizeUnity">The size of the occupancy zone in Unity units.</param>
        /// <param name="stimulusLocationUnity">The stimulus location in Unity units.</param>
        /// <param name="occupancyDurationMs">
        /// The occupancy duration in milliseconds applied to the OccupancyZone.
        /// </param>
        /// <param name="showBoundary">Determines whether the zone boundary is visible.</param>
        private static void PlaceOccupancyZone(
            GameObject parent,
            GameObject zonePrefab,
            string trialName,
            TriggerMode triggerMode,
            float zoneCenterUnity,
            float zoneSizeUnity,
            float stimulusLocationUnity,
            float occupancyDurationMs,
            bool showBoundary
        )
        {
            // Root position: stimulus boundary area, starting at stimulus_location and extending
            float rootZ = stimulusLocationUnity + zoneSizeUnity / 2f;

            GameObject zone = PrefabUtility.InstantiatePrefab(zonePrefab) as GameObject;
            zone.transform.SetParent(parent.transform);
            zone.transform.localPosition = new Vector3(0, ZoneVerticalOffset, rootZ);

            ConfigureRootZoneCollider(zone, zoneSizeUnity);

            float occupancyCenterOffset = zoneCenterUnity - rootZ;

            OccupancyZone occupancyZone = zone.GetComponentInChildren<OccupancyZone>();
            if (occupancyZone != null)
            {
                occupancyZone.occupancyDurationMs = occupancyDurationMs;

                BoxCollider occupancyCollider = occupancyZone.GetComponent<BoxCollider>();
                if (occupancyCollider != null)
                {
                    occupancyCollider.size = new Vector3(1, 1, zoneSizeUnity);
                    occupancyCollider.center = new Vector3(0, 0, occupancyCenterOffset);
                }
            }

            OccupancyGuidanceZone occupancyGuidanceZone = zone.GetComponentInChildren<OccupancyGuidanceZone>();
            if (occupancyGuidanceZone != null)
            {
                BoxCollider occupancyGuidanceCollider = occupancyGuidanceZone.GetComponent<BoxCollider>();
                if (occupancyGuidanceCollider != null)
                {
                    occupancyGuidanceCollider.size = new Vector3(1, 1, GuidanceColliderDepth);
                    occupancyGuidanceCollider.center = new Vector3(
                        0,
                        0,
                        occupancyCenterOffset + zoneSizeUnity / 2f - GuidanceColliderDepth / 2f
                    );
                }
            }

            StimulusTriggerZone stimulusZone = zone.GetComponent<StimulusTriggerZone>();
            if (stimulusZone != null)
            {
                stimulusZone.triggerMode = triggerMode;
                stimulusZone.showBoundary = showBoundary;
                stimulusZone.trialName = trialName;
            }
        }

        /// <summary>
        /// Resizes a zone GameObject's root <see cref="BoxCollider"/> to span the supplied length and
        /// recenters it on the local origin. Used by both <see cref="PlaceInteractionZone"/> and
        /// <see cref="PlaceOccupancyZone"/> to apply identical root-collider geometry.
        /// </summary>
        /// <param name="zone">The zone GameObject whose root BoxCollider is being adjusted.</param>
        /// <param name="zoneSizeUnity">The desired Z-axis length of the zone in Unity units.</param>
        private static void ConfigureRootZoneCollider(GameObject zone, float zoneSizeUnity)
        {
            BoxCollider rootCollider = zone.GetComponent<BoxCollider>();
            if (rootCollider != null)
            {
                rootCollider.size = new Vector3(1, 1, zoneSizeUnity);
                rootCollider.center = Vector3.zero;
            }
        }

        /// <summary>
        /// Reports the outcome of <see cref="CreateSceneFromTemplate"/>. Returned in lieu of a string-prefix
        /// protocol because the result carries facts that callers route differently: success or error, whether
        /// the requested task prefab was found, and whether a SimulatedLinearTreadmill was added. The MCP bridge
        /// surfaces success and SimulatedControllerAdded as discrete response fields; the task-prefab-found state
        /// is conveyed only through the message text and is not exercised on the GenerateTask path, which always
        /// supplies a freshly generated prefab. The menu flow only logs the message.
        /// </summary>
        public class SceneCreationResult
        {
            /// <summary>Determines whether the scene file was successfully created and saved.</summary>
            public bool Success { get; set; }

            /// <summary>The human-readable description of the outcome, including any error detail.</summary>
            public string Message { get; set; }

            /// <summary>Determines whether a SimulatedLinearTreadmill GameObject was added to the new scene.</summary>
            public bool SimulatedControllerAdded { get; set; }

            /// <summary>
            /// Determines whether a non-empty task prefab path was supplied but no asset could be loaded
            /// from it. The scene is still created and saved when this flag is set; callers may surface a
            /// warning to the user but should not treat it as a fatal error.
            /// </summary>
            public bool TaskPrefabNotFound { get; set; }
        }
    }
}
