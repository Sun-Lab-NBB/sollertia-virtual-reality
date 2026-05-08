/// <summary>
/// Provides the McpBridge editor plugin that exposes Unity Editor operations to external MCP relay servers.
///
/// Starts an HTTP listener on localhost when the Editor loads, accepting JSON tool call requests from the
/// sollertia-unity-tasks MCP relay. Each request specifies a tool name and arguments; the bridge dispatches
/// to the corresponding Unity Editor API and returns a JSON result.
/// </summary>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using SL.Config;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SL.Tasks;

/// <summary>
/// HTTP listener that bridges external MCP relay requests to Unity Editor API calls.
/// Initialized automatically when the Editor loads via <see cref="InitializeOnLoadAttribute"/>.
/// </summary>
[InitializeOnLoad]
public static class McpBridge
{
    /// <summary>The port on which the bridge listens for incoming HTTP requests.</summary>
    private const int Port = 8090;

    /// <summary>The tolerance for comparing measured prefab lengths against configured lengths.</summary>
    private const float LengthComparisonEpsilon = 0.01f;

    /// <summary>The set of project-relative directory prefixes under which assets may be deleted.</summary>
    private static readonly string[] DeleteAllowedPrefixes =
    {
        "Assets/InfiniteCorridorTask/Tasks/",
        "Assets/InfiniteCorridorTask/Prefabs/",
        "Assets/InfiniteCorridorTask/Cues/",
        "Assets/InfiniteCorridorTask/Materials/",
        "Assets/Scenes/",
    };

    /// <summary>The set of hand-authored asset paths that are protected from deletion.</summary>
    private static readonly HashSet<string> DeleteProtectedPaths = new HashSet<string>(StringComparer.Ordinal)
    {
        "Assets/InfiniteCorridorTask/Prefabs/StimulusTriggerZone.prefab",
        "Assets/InfiniteCorridorTask/Prefabs/OccupancyTriggerZone.prefab",
        "Assets/InfiniteCorridorTask/Prefabs/ResetZone.prefab",
        "Assets/Scenes/ExperimentTemplate.unity",
    };

    /// <summary>The HTTP listener instance.</summary>
    private static HttpListener _listener;

    /// <summary>Starts the HTTP listener and registers the editor update callback.</summary>
    static McpBridge()
    {
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
            EditorApplication.update += Poll;
            Debug.Log($"McpBridge: Listening on http://localhost:{Port}/");
        }
        catch (Exception exception)
        {
            Debug.LogError($"McpBridge: Failed to start HTTP listener: {exception.Message}");
        }
    }

    /// <summary>Checks for pending HTTP requests each editor frame and dispatches them.</summary>
    private static void Poll()
    {
        if (_listener == null || !_listener.IsListening)
        {
            return;
        }

        while (_listener.IsListening)
        {
            IAsyncResult asyncResult = _listener.BeginGetContext(null, null);
            if (!asyncResult.AsyncWaitHandle.WaitOne(0))
            {
                break;
            }

            HttpListenerContext context = _listener.EndGetContext(asyncResult);
            HandleRequest(context);
        }
    }

    /// <summary>
    /// Reads the request body, dispatches to the appropriate tool handler, and writes the response.
    /// </summary>
    /// <param name="context">
    /// The HTTP listener context containing the request and response objects.
    /// </param>
    private static void HandleRequest(HttpListenerContext context)
    {
        string responseJson;

        try
        {
            string body;
            using (StreamReader reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                body = reader.ReadToEnd();
            }

            Dictionary<string, object> request = MiniJson.Deserialize(body);
            string tool = request.ContainsKey("tool") ? request["tool"].ToString() : "";
            Dictionary<string, object> args = request.ContainsKey("args")
                ? request["args"] as Dictionary<string, object> ?? new Dictionary<string, object>()
                : new Dictionary<string, object>();

            responseJson = Dispatch(tool, args);
        }
        catch (Exception exception)
        {
            responseJson = Error($"Bridge error: {exception.Message}");
        }

        byte[] buffer = Encoding.UTF8.GetBytes(responseJson);
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = buffer.Length;
        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
        context.Response.Close();
    }

    /// <summary>Routes a tool call to the appropriate handler method.</summary>
    /// <param name="tool">The tool name to dispatch.</param>
    /// <param name="args">The tool arguments as a string-keyed dictionary.</param>
    /// <returns>A JSON response string.</returns>
    private static string Dispatch(string tool, Dictionary<string, object> args)
    {
        return tool switch
        {
            "generate_task_prefab" => GenerateTaskPrefab(args),
            "inspect_prefab" => InspectPrefab(args),
            "validate_prefab_against_template" => ValidatePrefabAgainstTemplate(args),
            "delete_unity_asset" => DeleteUnityAsset(args),
            "list_unity_assets" => ListUnityAssets(args),
            "list_scenes" => ListScenes(),
            "open_scene" => OpenScene(args),
            "create_scene" => CreateScene(args),
            "inspect_scene" => InspectScene(),
            "enter_play_mode" => EnterPlayMode(),
            "exit_play_mode" => ExitPlayMode(),
            "get_play_state" => GetPlayState(),
            _ => Error($"Unknown tool: {tool}"),
        };
    }

    /// <summary>
    /// Generates a Task prefab from a YAML template by delegating to CreateTask.CreateFromTemplate.
    /// </summary>
    /// <param name="args">The tool arguments containing template_name and optional save_path.</param>
    /// <returns>A JSON response with the generated prefab path or an error message.</returns>
    private static string GenerateTaskPrefab(Dictionary<string, object> args)
    {
        string templateName = GetString(args, "template_name");
        string savePath = GetString(args, "save_path", defaultValue: "");

        if (string.IsNullOrEmpty(templateName))
        {
            return Error("Missing required argument: template_name");
        }

        string absoluteTemplatePath = Path.Combine(
            Application.dataPath,
            "InfiniteCorridorTask",
            "Configurations",
            $"{templateName}.yaml"
        );

        if (!File.Exists(absoluteTemplatePath))
        {
            return Error($"Template not found: {absoluteTemplatePath}");
        }

        string relativeConfigPath = Path.Combine("/InfiniteCorridorTask", "Configurations", $"{templateName}.yaml");

        if (string.IsNullOrEmpty(savePath))
        {
            savePath = Path.Combine("Assets", "InfiniteCorridorTask", "Tasks", $"{templateName}.prefab");
        }

        // Ensures the Tasks output directory exists
        string tasksDirectory = Path.GetDirectoryName(savePath);
        if (!string.IsNullOrEmpty(tasksDirectory) && !AssetDatabase.IsValidFolder(tasksDirectory))
        {
            string parent = Path.GetDirectoryName(tasksDirectory);
            string folder = Path.GetFileName(tasksDirectory);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(folder))
            {
                AssetDatabase.CreateFolder(parent, folder);
            }
        }

        string result = CreateTask.CreateFromTemplate(absoluteTemplatePath, relativeConfigPath, savePath);

        if (result.StartsWith("error:", StringComparison.Ordinal))
        {
            return Error(result.Substring(7).Trim());
        }

        return Ok(
            new Dictionary<string, object>
            {
                { "message", result },
                { "prefab_path", savePath },
                { "template_name", templateName },
            }
        );
    }

    /// <summary>Reads a prefab and returns its hierarchy, components, and zone configuration.</summary>
    /// <param name="args">The tool arguments containing prefab_path.</param>
    /// <returns>A JSON response with the prefab hierarchy or an error message.</returns>
    private static string InspectPrefab(Dictionary<string, object> args)
    {
        string prefabPath = GetString(args, "prefab_path");

        if (string.IsNullOrEmpty(prefabPath))
        {
            return Error("Missing required argument: prefab_path");
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            return Error($"Prefab not found at: {prefabPath}");
        }

        Dictionary<string, object> hierarchy = InspectGameObject(prefab);

        return Ok(new Dictionary<string, object> { { "prefab_path", prefabPath }, { "hierarchy", hierarchy } });
    }

    /// <summary>
    /// Validates that the prefabs match the template's cue inventory, segment geometry, and zone positions.
    /// </summary>
    /// <param name="args">The tool arguments containing template_name.</param>
    /// <returns>A JSON response with cue prefab existence and per-segment validation results.</returns>
    private static string ValidatePrefabAgainstTemplate(Dictionary<string, object> args)
    {
        string templateName = GetString(args, "template_name");

        if (string.IsNullOrEmpty(templateName))
        {
            return Error("Missing required argument: template_name");
        }

        string absoluteTemplatePath = Path.Combine(
            Application.dataPath,
            "InfiniteCorridorTask",
            "Configurations",
            $"{templateName}.yaml"
        );

        if (!File.Exists(absoluteTemplatePath))
        {
            return Error($"Template not found: {absoluteTemplatePath}");
        }

        TaskTemplate template;
        try
        {
            template = ConfigLoader.LoadTemplate(absoluteTemplatePath);
        }
        catch (Exception exception)
        {
            return Error($"Failed to load template: {exception.Message}");
        }

        string prefabsPath = "Assets/InfiniteCorridorTask/Prefabs/";
        string cuesPath = "Assets/InfiniteCorridorTask/Cues/";
        float cmPerUnit = template.vrEnvironment.cmPerUnityUnit;
        float[] expectedSegmentLengthsUnity = template.GetSegmentLengthsUnity();

        // Reports cue prefab existence on disk so callers can spot missing assets without walking each segment.
        List<Dictionary<string, object>> cuePrefabResults = new List<Dictionary<string, object>>();
        foreach (Cue cue in template.cues)
        {
            string cuePrefabPath = Path.Combine(cuesPath, $"Cue_{cue.name}.prefab");
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(cuePrefabPath) != null;
            cuePrefabResults.Add(
                new Dictionary<string, object>
                {
                    { "cue_name", cue.name },
                    { "prefab_path", cuePrefabPath },
                    { "exists", exists },
                }
            );
        }

        List<Dictionary<string, object>> segmentResults = new List<Dictionary<string, object>>();
        for (int segmentIndex = 0; segmentIndex < template.segments.Count; segmentIndex++)
        {
            Segment segment = template.segments[segmentIndex];
            string segmentPath = Path.Combine(prefabsPath, $"{segment.name}.prefab");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(segmentPath);

            Dictionary<string, object> segmentResult = new Dictionary<string, object>
            {
                { "segment", segment.name },
                { "prefab_exists", prefab != null },
            };

            if (prefab == null)
            {
                segmentResults.Add(segmentResult);
                continue;
            }

            // Compares the cue ordering encoded in the prefab against the template's cue sequence.
            List<string> actualCueOrder = GetCueOrderFromSegmentPrefab(prefab);
            segmentResult["cue_order"] = actualCueOrder;
            segmentResult["expected_cue_order"] = segment.cueSequence;
            segmentResult["cue_order_match"] = actualCueOrder.SequenceEqual(segment.cueSequence);

            // Compares the prefab's measured z-axis length against the configured cue-sum length.
            float measuredLengthUnity = Utility.GetPrefabLength(prefab);
            float expectedLengthUnity = expectedSegmentLengthsUnity[segmentIndex];
            segmentResult["segment_length_unity"] = measuredLengthUnity;
            segmentResult["expected_segment_length_unity"] = expectedLengthUnity;
            segmentResult["segment_length_match"] =
                Mathf.Abs(measuredLengthUnity - expectedLengthUnity) < LengthComparisonEpsilon;

            // Compares the StimulusTriggerZone position and size against the trial structure if one exists.
            TrialStructure trial = template.GetTrialStructureForSegment(segment.name);
            if (trial != null)
            {
                StimulusTriggerZone zone = prefab.GetComponentInChildren<StimulusTriggerZone>();
                segmentResult["has_zone"] = zone != null;

                if (zone != null)
                {
                    float actualZ = zone.transform.localPosition.z;
                    BoxCollider collider = zone.GetComponent<BoxCollider>();
                    float actualSize = collider != null ? collider.size.z : 0f;

                    float expectedCenter =
                        (trial.stimulusTriggerZoneStartCm + trial.stimulusTriggerZoneEndCm) / (2f * cmPerUnit);
                    float expectedSize =
                        (trial.stimulusTriggerZoneEndCm - trial.stimulusTriggerZoneStartCm) / cmPerUnit;

                    segmentResult["zone_z"] = actualZ;
                    segmentResult["expected_zone_z"] = expectedCenter;
                    segmentResult["zone_size"] = actualSize;
                    segmentResult["expected_zone_size"] = expectedSize;
                    segmentResult["zone_z_match"] = Mathf.Abs(actualZ - expectedCenter) < LengthComparisonEpsilon;
                    segmentResult["zone_size_match"] = Mathf.Abs(actualSize - expectedSize) < LengthComparisonEpsilon;
                }
            }

            segmentResults.Add(segmentResult);
        }

        return Ok(
            new Dictionary<string, object>
            {
                { "template_name", templateName },
                { "cue_prefabs", cuePrefabResults },
                { "segments", segmentResults },
            }
        );
    }

    /// <summary>Deletes a Unity asset within an allowed directory and refreshes the AssetDatabase.</summary>
    /// <param name="args">The tool arguments containing asset_path.</param>
    /// <returns>A JSON response confirming deletion or an error message.</returns>
    private static string DeleteUnityAsset(Dictionary<string, object> args)
    {
        string assetPath = GetString(args, "asset_path");

        if (string.IsNullOrEmpty(assetPath))
        {
            return Error("Missing required argument: asset_path");
        }

        if (!IsDeleteAllowed(assetPath))
        {
            string allowedRoots = string.Join(", ", DeleteAllowedPrefixes);
            string message =
                $"Refusing to delete '{assetPath}'. Deletion is permitted only for individual assets under: "
                + $"{allowedRoots}. Hand-authored prefabs and the experiment template scene are protected.";
            return Error(message);
        }

        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) == null)
        {
            return Error($"Asset not found at: {assetPath}");
        }

        bool deleted = AssetDatabase.DeleteAsset(assetPath);
        if (!deleted)
        {
            return Error($"Failed to delete asset at: {assetPath}");
        }

        AssetDatabase.Refresh();

        return Ok(
            new Dictionary<string, object>
            {
                { "message", $"Deleted asset: {assetPath}" },
                { "asset_path", assetPath },
                { "deleted", true },
            }
        );
    }

    /// <summary>
    /// Lists Unity assets of a given type filter (e.g., "Prefab", "Scene", "Material").
    /// </summary>
    /// <param name="args">The tool arguments containing optional type and path filters.</param>
    /// <returns>A JSON response with matching asset paths.</returns>
    private static string ListUnityAssets(Dictionary<string, object> args)
    {
        string assetType = GetString(args, "type", defaultValue: "Prefab");
        string searchPath = GetString(args, "path", defaultValue: "Assets/InfiniteCorridorTask");

        string[] guids = AssetDatabase.FindAssets($"t:{assetType}", new[] { searchPath });
        List<string> paths = guids.Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path).ToList();

        return Ok(
            new Dictionary<string, object>
            {
                { "type", assetType },
                { "search_path", searchPath },
                { "assets", paths },
                { "count", paths.Count },
            }
        );
    }

    /// <summary>Lists all scene assets in the project.</summary>
    /// <returns>A JSON response with all scene paths and the active scene.</returns>
    private static string ListScenes()
    {
        string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
        List<string> paths = guids.Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path).ToList();

        string activeScene = SceneManager.GetActiveScene().path;

        return Ok(
            new Dictionary<string, object>
            {
                { "scenes", paths },
                { "active_scene", activeScene },
                { "count", paths.Count },
            }
        );
    }

    /// <summary>Opens a scene in the Editor after applying the unsaved-changes policy.</summary>
    /// <param name="args">The tool arguments containing scene_path and optional unsaved_changes.</param>
    /// <returns>A JSON response confirming the scene was opened or an error message.</returns>
    private static string OpenScene(Dictionary<string, object> args)
    {
        string scenePath = GetString(args, "scene_path");
        string unsavedChanges = GetString(args, "unsaved_changes", defaultValue: "");

        if (string.IsNullOrEmpty(scenePath))
        {
            return Error("Missing required argument: scene_path");
        }

        if (!File.Exists(scenePath))
        {
            return Error($"Scene not found at: {scenePath}");
        }

        string handlingError = HandleUnsavedChanges(unsavedChanges);
        if (handlingError != null)
        {
            return Error(handlingError);
        }

        EditorSceneManager.OpenScene(scenePath);

        return Ok(
            new Dictionary<string, object> { { "message", $"Opened scene: {scenePath}" }, { "scene_path", scenePath } }
        );
    }

    /// <summary>
    /// Creates a new scene by copying ExperimentTemplate.unity, optionally adding a task prefab to it.
    /// </summary>
    /// <param name="args">
    /// The tool arguments containing scene_name, optional task_prefab_path, and optional unsaved_changes.
    /// </param>
    /// <returns>A JSON response with the created scene path or an error message.</returns>
    private static string CreateScene(Dictionary<string, object> args)
    {
        string sceneName = GetString(args, "scene_name");
        string taskPrefabPath = GetString(args, "task_prefab_path", defaultValue: "");
        string unsavedChanges = GetString(args, "unsaved_changes", defaultValue: "");

        if (string.IsNullOrEmpty(sceneName))
        {
            return Error("Missing required argument: scene_name");
        }

        string templateScenePath = Path.Combine("Assets", "Scenes", "ExperimentTemplate.unity");
        string newScenePath = Path.Combine("Assets", "Scenes", $"{sceneName}.unity");

        if (!File.Exists(templateScenePath))
        {
            return Error($"Template scene not found at: {templateScenePath}");
        }

        if (File.Exists(newScenePath))
        {
            return Error($"Scene already exists at: {newScenePath}");
        }

        string handlingError = HandleUnsavedChanges(unsavedChanges);
        if (handlingError != null)
        {
            return Error(handlingError);
        }

        // Copies the template scene file
        AssetDatabase.CopyAsset(templateScenePath, newScenePath);
        AssetDatabase.Refresh();

        // Opens the new scene and adds the task prefab if specified
        EditorSceneManager.OpenScene(newScenePath);

        if (!string.IsNullOrEmpty(taskPrefabPath))
        {
            GameObject taskPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(taskPrefabPath);
            if (taskPrefab != null)
            {
                PrefabUtility.InstantiatePrefab(taskPrefab);
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            }
            else
            {
                return Ok(
                    new Dictionary<string, object>
                    {
                        { "message", $"Scene created but task prefab not found at: {taskPrefabPath}" },
                        { "scene_path", newScenePath },
                        { "warning", "task_prefab_not_found" },
                    }
                );
            }
        }

        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

        return Ok(
            new Dictionary<string, object>
            {
                { "message", $"Scene created: {newScenePath}" },
                { "scene_path", newScenePath },
            }
        );
    }

    /// <summary>Inspects the active scene and returns its root GameObject hierarchy.</summary>
    /// <returns>A JSON response with scene metadata and the recursive root object hierarchies.</returns>
    private static string InspectScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        GameObject[] rootObjects = activeScene.GetRootGameObjects();

        List<Dictionary<string, object>> rootHierarchies = new List<Dictionary<string, object>>();
        foreach (GameObject rootObject in rootObjects)
        {
            rootHierarchies.Add(InspectGameObject(rootObject));
        }

        return Ok(
            new Dictionary<string, object>
            {
                { "scene_path", activeScene.path },
                { "scene_name", activeScene.name },
                { "is_dirty", activeScene.isDirty },
                { "root_objects", rootHierarchies },
                { "root_count", rootObjects.Length },
            }
        );
    }

    /// <summary>Enters Play Mode in the Editor.</summary>
    /// <returns>A JSON response with the current play state.</returns>
    private static string EnterPlayMode()
    {
        if (EditorApplication.isPlaying)
        {
            return Ok(
                new Dictionary<string, object> { { "message", "Already in Play Mode." }, { "state", "playing" } }
            );
        }

        EditorApplication.EnterPlaymode();

        return Ok(
            new Dictionary<string, object> { { "message", "Entering Play Mode." }, { "state", "entering_play_mode" } }
        );
    }

    /// <summary>Exits Play Mode in the Editor.</summary>
    /// <returns>A JSON response with the current play state.</returns>
    private static string ExitPlayMode()
    {
        if (!EditorApplication.isPlaying)
        {
            return Ok(new Dictionary<string, object> { { "message", "Not in Play Mode." }, { "state", "edit" } });
        }

        EditorApplication.ExitPlaymode();

        return Ok(
            new Dictionary<string, object> { { "message", "Exiting Play Mode." }, { "state", "exiting_play_mode" } }
        );
    }

    /// <summary>Returns the current Editor play state.</summary>
    /// <returns>A JSON response with the current state and active scene name.</returns>
    private static string GetPlayState()
    {
        string state =
            EditorApplication.isPlaying ? "playing"
            : EditorApplication.isCompiling ? "compiling"
            : "edit";

        return Ok(
            new Dictionary<string, object>
            {
                { "state", state },
                { "active_scene", SceneManager.GetActiveScene().name },
            }
        );
    }

    /// <summary>Determines whether the given asset path is permitted for deletion.</summary>
    /// <param name="assetPath">The project-relative asset path to check.</param>
    /// <returns>True when the path lies under an allowed prefix and is not in the protected set.</returns>
    private static bool IsDeleteAllowed(string assetPath)
    {
        // Rejects path traversal sequences, absolute paths, and directory targets to bound the blast radius.
        if (
            assetPath.Contains("..", StringComparison.Ordinal)
            || Path.IsPathRooted(assetPath)
            || assetPath.EndsWith("/", StringComparison.Ordinal)
        )
        {
            return false;
        }

        if (DeleteProtectedPaths.Contains(assetPath))
        {
            return false;
        }

        foreach (string prefix in DeleteAllowedPrefixes)
        {
            if (assetPath.StartsWith(prefix, StringComparison.Ordinal) && assetPath.Length > prefix.Length)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Resolves how to handle unsaved changes in the active scene before switching scenes.</summary>
    /// <param name="unsavedChanges">
    /// The handling policy: "save" persists the active scene, "discard" abandons unsaved edits, and an
    /// empty string leaves the policy unspecified so the caller can prompt the user.
    /// </param>
    /// <returns>An error message when the active scene is dirty and no policy was provided, otherwise null.</returns>
    private static string HandleUnsavedChanges(string unsavedChanges)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.isDirty)
        {
            return null;
        }

        if (string.Equals(unsavedChanges, "save", StringComparison.Ordinal))
        {
            EditorSceneManager.SaveOpenScenes();
            return null;
        }

        if (string.Equals(unsavedChanges, "discard", StringComparison.Ordinal))
        {
            return null;
        }

        string message =
            $"Active scene '{activeScene.path}' has unsaved changes. Specify unsaved_changes='save' to "
            + "persist the current scene before switching, or unsaved_changes='discard' to abandon the "
            + "edits. Ask the user which behavior they prefer before retrying.";
        return message;
    }

    /// <summary>Returns the cue name sequence implied by the cue child GameObjects of a segment prefab.</summary>
    /// <param name="segmentPrefab">The segment prefab whose cue layout to extract.</param>
    /// <returns>The list of cue names ordered along the segment's local Z axis.</returns>
    private static List<string> GetCueOrderFromSegmentPrefab(GameObject segmentPrefab)
    {
        List<(string cueName, float localZ)> cueChildren = new List<(string, float)>();
        for (int childIndex = 0; childIndex < segmentPrefab.transform.childCount; childIndex++)
        {
            Transform child = segmentPrefab.transform.GetChild(childIndex);
            string childName = child.name;

            // The CreateTask pipeline names cue children "Cue<cueName>" (no underscore) while siblings such
            // as "Floor" or "Walls" use unrelated names; the prefix and length check discriminates cleanly.
            if (!childName.StartsWith("Cue", StringComparison.Ordinal) || childName.Length <= 3)
            {
                continue;
            }

            cueChildren.Add((childName.Substring(3), child.localPosition.z));
        }

        cueChildren.Sort((left, right) => left.localZ.CompareTo(right.localZ));
        return cueChildren.Select(child => child.cueName).ToList();
    }

    /// <summary>Recursively inspects a GameObject and returns its hierarchy as a dictionary.</summary>
    /// <param name="gameObject">The GameObject to inspect.</param>
    /// <returns>A dictionary describing the GameObject's transform, components, and children.</returns>
    private static Dictionary<string, object> InspectGameObject(GameObject gameObject)
    {
        Dictionary<string, object> result = new Dictionary<string, object>
        {
            { "name", gameObject.name },
            { "position", FormatVector3(gameObject.transform.localPosition) },
            { "rotation", FormatVector3(gameObject.transform.localEulerAngles) },
            { "scale", FormatVector3(gameObject.transform.localScale) },
        };

        // Lists component types
        Component[] components = gameObject.GetComponents<Component>();
        List<string> componentNames = components
            .Where(component => component != null)
            .Select(component => component.GetType().Name)
            .ToList();
        result["components"] = componentNames;

        // Includes BoxCollider details if present
        BoxCollider collider = gameObject.GetComponent<BoxCollider>();
        if (collider != null)
        {
            result["collider_center"] = FormatVector3(collider.center);
            result["collider_size"] = FormatVector3(collider.size);
            result["collider_is_trigger"] = collider.isTrigger;
        }

        // Recurses into children
        List<Dictionary<string, object>> children = new List<Dictionary<string, object>>();
        for (int i = 0; i < gameObject.transform.childCount; i++)
        {
            children.Add(InspectGameObject(gameObject.transform.GetChild(i).gameObject));
        }

        if (children.Count > 0)
        {
            result["children"] = children;
        }

        return result;
    }

    /// <summary>Formats a Vector3 as a serializable dictionary.</summary>
    /// <param name="vector">The Vector3 to format.</param>
    /// <returns>A dictionary with x, y, and z keys.</returns>
    private static Dictionary<string, float> FormatVector3(Vector3 vector)
    {
        return new Dictionary<string, float>
        {
            { "x", vector.x },
            { "y", vector.y },
            { "z", vector.z },
        };
    }

    /// <summary>Retrieves a string value from the arguments dictionary with an optional default.</summary>
    /// <param name="args">The arguments dictionary to search.</param>
    /// <param name="key">The key to look up.</param>
    /// <param name="defaultValue">The default value if the key is not found.</param>
    /// <returns>The string value, or the default if not found.</returns>
    private static string GetString(Dictionary<string, object> args, string key, string defaultValue = null)
    {
        if (args.ContainsKey(key) && args[key] != null)
        {
            return args[key].ToString();
        }

        return defaultValue;
    }

    /// <summary>Constructs a success JSON response.</summary>
    /// <param name="payload">The response payload dictionary.</param>
    /// <returns>A JSON string with success set to true.</returns>
    private static string Ok(Dictionary<string, object> payload)
    {
        payload["success"] = true;
        return MiniJson.Serialize(payload);
    }

    /// <summary>Constructs an error JSON response.</summary>
    /// <param name="message">The error message.</param>
    /// <returns>A JSON string with success set to false and the error message.</returns>
    private static string Error(string message)
    {
        return MiniJson.Serialize(new Dictionary<string, object> { { "success", false }, { "error", message } });
    }
}
