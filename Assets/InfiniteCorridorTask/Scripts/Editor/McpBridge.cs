/// <summary>
/// Provides the McpBridge editor plugin that exposes Unity Editor operations to external MCP relay servers.
///
/// Starts an HTTP listener on localhost when the Editor loads, accepting JSON tool call requests from the
/// sollertia-unity-tasks MCP relay. Each request specifies a tool name and arguments; the bridge dispatches
/// to the corresponding Unity Editor API and returns a JSON result.
/// </summary>
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Gimbl;
using SL.Config;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SL.Tasks
{
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
            "Assets/InfiniteCorridorTask/Materials/_CueShaderReference.mat",
            "Assets/Scenes/ExperimentTemplate.unity",
        };

        /// <summary>The shared error-protocol prefix returned by <see cref="CreateTask.CreateFromTemplate"/>.</summary>
        private const string CreateTaskErrorPrefix = "error: ";

        /// <summary>The HTTP listener instance.</summary>
        private static readonly HttpListener _listener = new HttpListener();

        /// <summary>The queue of HTTP requests captured on the listener thread, drained on the editor thread.</summary>
        private static readonly ConcurrentQueue<HttpListenerContext> _pendingContexts =
            new ConcurrentQueue<HttpListenerContext>();

        /// <summary>Starts the HTTP listener and registers the editor update callback.</summary>
        static McpBridge()
        {
            try
            {
                // Registers all three loopback hostnames because HttpListener performs exact
                // host-header matching: a client requesting "localhost" is rejected by 127.0.0.1
                // and [::1] prefixes, even though they resolve to the same socket. The explicit
                // numeric prefixes also work around Mono's IPv6-only resolution of "localhost".
                _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                _listener.Prefixes.Add($"http://[::1]:{Port}/");
                _listener.Prefixes.Add($"http://localhost:{Port}/");
                _listener.Start();
                _listener.BeginGetContext(OnContextReceived, null);
                EditorApplication.update += Poll;
                string message =
                    $"McpBridge: Listening on http://127.0.0.1:{Port}/, http://[::1]:{Port}/, "
                    + $"and http://localhost:{Port}/";
                Debug.Log(message);
            }
            catch (Exception exception)
            {
                Debug.LogError($"McpBridge: Failed to start HTTP listener: {exception.Message}");
            }
        }

        /// <summary>Thread-pool callback that captures a completed request and re-arms the listener.</summary>
        /// <param name="asyncResult">The asynchronous result for the completed BeginGetContext call.</param>
        private static void OnContextReceived(IAsyncResult asyncResult)
        {
            if (_listener == null || !_listener.IsListening)
            {
                return;
            }

            try
            {
                HttpListenerContext context = _listener.EndGetContext(asyncResult);
                _pendingContexts.Enqueue(context);
            }
            catch (Exception exception)
            {
                Debug.LogError($"McpBridge: EndGetContext failed: {exception.Message}");
            }

            try
            {
                _listener.BeginGetContext(OnContextReceived, null);
            }
            catch (Exception exception)
            {
                Debug.LogError($"McpBridge: Failed to re-arm listener: {exception.Message}");
            }
        }

        /// <summary>Drains queued HTTP requests on the editor thread and dispatches each one.</summary>
        private static void Poll()
        {
            while (_pendingContexts.TryDequeue(out HttpListenerContext context))
            {
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
                using StreamReader reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                string body = reader.ReadToEnd();

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
                "read_task_parameters" => ReadTaskParameters(),
                "write_task_parameters" => WriteTaskParameters(args),
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

            // The path is stored on the Task component and resolved at runtime as
            // ``Path.Combine(Application.dataPath, configPath)``; a leading ``/`` would make
            // Path.Combine treat the value as absolute on Linux/macOS and discard the data path.
            string relativeConfigPath = Path.Combine("InfiniteCorridorTask", "Configurations", $"{templateName}.yaml");

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

            if (result.StartsWith(CreateTaskErrorPrefix, StringComparison.Ordinal))
            {
                return Error(result.Substring(CreateTaskErrorPrefix.Length).Trim());
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
            // Mirrors the length-suffixed path produced by CreateTask.BuildCuePrefabs so cues that share a letter
            // across templates resolve to distinct assets.
            List<Dictionary<string, object>> cuePrefabResults = new List<Dictionary<string, object>>();
            foreach (Cue cue in template.cues)
            {
                string lengthLabel = CreateTask.FormatCueLengthLabel(cue.lengthCm);
                string cuePrefabPath = Path.Combine(cuesPath, $"Cue_{cue.name}_{lengthLabel}cm.prefab");
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

            List<Dictionary<string, object>> trialResults = new List<Dictionary<string, object>>();
            string[] trialNames = template.GetTrialNames();
            for (int trialIndex = 0; trialIndex < trialNames.Length; trialIndex++)
            {
                string trialName = trialNames[trialIndex];
                TrialStructure trial = template.trialStructures[trialName];
                // Segment prefab filenames follow ``TemplateName_TrialName`` so the canonical name is derived
                // directly from the template name and the trial key — no geometry encoding is involved.
                string canonicalSegmentName = CreateTask.CanonicalSegmentName(template, trialName);
                string segmentPath = Path.Combine(prefabsPath, $"{canonicalSegmentName}.prefab");
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(segmentPath);

                Dictionary<string, object> trialResult = new Dictionary<string, object>
                {
                    { "trial", trialName },
                    { "canonical_name", canonicalSegmentName },
                    { "prefab_exists", prefab != null },
                };

                if (prefab == null)
                {
                    trialResults.Add(trialResult);
                    continue;
                }

                // Compares the cue ordering encoded in the prefab against the trial's cue sequence.
                List<string> actualCueOrder = GetCueOrderFromSegmentPrefab(prefab);
                trialResult["cue_order"] = actualCueOrder;
                trialResult["expected_cue_order"] = trial.cueSequence;
                trialResult["cue_order_match"] = actualCueOrder.SequenceEqual(trial.cueSequence);

                // Compares the prefab's measured z-axis length against the configured cue-sum length.
                float measuredLengthUnity = Utility.GetPrefabLength(prefab);
                float expectedLengthUnity = expectedSegmentLengthsUnity[trialIndex];
                trialResult["segment_length_unity"] = measuredLengthUnity;
                trialResult["expected_segment_length_unity"] = expectedLengthUnity;
                trialResult["segment_length_match"] =
                    Mathf.Abs(measuredLengthUnity - expectedLengthUnity) < LengthComparisonEpsilon;

                // Compares the StimulusTriggerZone position and size against the trial's expected values.
                StimulusTriggerZone zone = prefab.GetComponentInChildren<StimulusTriggerZone>();
                trialResult["has_zone"] = zone != null;

                if (zone != null)
                {
                    float actualZ = zone.transform.localPosition.z;
                    BoxCollider zoneCollider = zone.GetComponent<BoxCollider>();
                    float actualSize = zoneCollider != null ? zoneCollider.size.z : 0f;

                    float expectedCenter =
                        (trial.stimulusTriggerZoneStartCm + trial.stimulusTriggerZoneEndCm) / (2f * cmPerUnit);
                    float expectedSize =
                        (trial.stimulusTriggerZoneEndCm - trial.stimulusTriggerZoneStartCm) / cmPerUnit;

                    trialResult["zone_z"] = actualZ;
                    trialResult["expected_zone_z"] = expectedCenter;
                    trialResult["zone_size"] = actualSize;
                    trialResult["expected_zone_size"] = expectedSize;
                    trialResult["zone_z_match"] = Mathf.Abs(actualZ - expectedCenter) < LengthComparisonEpsilon;
                    trialResult["zone_size_match"] = Mathf.Abs(actualSize - expectedSize) < LengthComparisonEpsilon;
                }

                trialResults.Add(trialResult);
            }

            return Ok(
                new Dictionary<string, object>
                {
                    { "template_name", templateName },
                    { "cue_prefabs", cuePrefabResults },
                    { "trials", trialResults },
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
                new Dictionary<string, object>
                {
                    { "message", $"Opened scene: {scenePath}" },
                    { "scene_path", scenePath },
                }
            );
        }

        /// <summary>
        /// Creates a new scene by copying ExperimentTemplate.unity, optionally adding a task prefab to it.
        /// Delegates the file copy, prefab instantiation, and SimulatedLinearTreadmill placement to
        /// <see cref="CreateTask.CreateSceneFromTemplate"/> so the MCP surface and the Editor menu
        /// produce identical scenes.
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

            string newScenePath = Path.Combine("Assets", "Scenes", $"{sceneName}.unity");

            // Refuses to clobber an existing scene at the requested path. The Editor menu uses Unity's
            // SaveFilePanel overwrite prompt and passes overwriteExisting=true to the shared helper; the
            // MCP surface deliberately rejects this case so an automated client never silently destroys a
            // hand-authored scene.
            if (File.Exists(newScenePath))
            {
                return Error($"Scene already exists at: {newScenePath}");
            }

            string handlingError = HandleUnsavedChanges(unsavedChanges);
            if (handlingError != null)
            {
                return Error(handlingError);
            }

            CreateTask.SceneCreationResult sceneResult = CreateTask.CreateSceneFromTemplate(
                sceneSavePath: newScenePath,
                taskPrefabPath: taskPrefabPath,
                overwriteExisting: false
            );

            if (!sceneResult.Success)
            {
                return Error(sceneResult.Message);
            }

            Dictionary<string, object> response = new Dictionary<string, object>
            {
                { "message", sceneResult.Message },
                { "scene_path", newScenePath },
                { "simulated_controller_added", sceneResult.SimulatedControllerAdded },
            };

            if (sceneResult.TaskPrefabNotFound)
            {
                response["warning"] = "task_prefab_not_found";
            }

            return Ok(response);
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
                new Dictionary<string, object>
                {
                    { "message", "Entering Play Mode." },
                    { "state", "entering_play_mode" },
                }
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

        /// <summary>Returns a single-scan snapshot of every Task Parameters field plus options and visibility.</summary>
        /// <remarks>
        /// State, options, and visibility are derived from a single scene walk so an agent that reads,
        /// modifies, and writes back values does not race against a separate enumeration pass. Cameras are
        /// filtered to match the GUI dropdown (Main Camera excluded). Monitor mapping is sourced from the
        /// open Parameters window's FullScreenViewManager when available, falling back to a fresh manager
        /// loaded from <c>savedFullScreenViews.asset</c> when the window is closed.
        /// </remarks>
        /// <returns>A JSON response with state, options, and visibility nested dictionaries.</returns>
        private static string ReadTaskParameters()
        {
            ActorObject actor = UnityEngine.Object.FindAnyObjectByType<ActorObject>();
            DisplayObject display = UnityEngine.Object.FindAnyObjectByType<DisplayObject>();
            Task task = UnityEngine.Object.FindAnyObjectByType<Task>();
            MQTTClient client = UnityEngine.Object.FindAnyObjectByType<MQTTClient>();
            ControllerOutput[] controllers = UnityEngine.Object.FindObjectsByType<ControllerOutput>(
                FindObjectsSortMode.None
            );
            Camera[] cameras = UnityEngine
                .Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                .Where(camera =>
                    !camera.CompareTag("MainCamera")
                    && !string.Equals(camera.gameObject.name, "Main Camera", StringComparison.Ordinal)
                )
                .ToArray();
            bool hasLickZone = UnityEngine.Object.FindAnyObjectByType<GuidanceZone>() != null;
            bool hasOccupancyZone = UnityEngine.Object.FindAnyObjectByType<OccupancyZone>() != null;
            FullScreenViewManager fullScreenManager = AcquireFullScreenManager();

            Dictionary<string, object> actorState = null;
            if (actor != null)
            {
                string currentModel = "None";
                foreach (Transform child in actor.transform)
                {
                    if (child.name.StartsWith("Model ", StringComparison.Ordinal))
                    {
                        currentModel = child.name.Substring("Model ".Length);
                        break;
                    }
                }
                actorState = new Dictionary<string, object>
                {
                    { "model", currentModel },
                    { "controller", actor.Controller == null ? "None" : actor.Controller.gameObject.name },
                };
            }

            Dictionary<string, object> mqttState =
                client == null
                    ? null
                    : new Dictionary<string, object> { { "ip", client.ipAddress }, { "port", client.port } };

            Dictionary<string, object> displayState =
                display == null
                    ? null
                    : new Dictionary<string, object>
                    {
                        { "current_brightness", display.currentBrightness },
                        { "brightness", display.settings != null ? display.settings.brightness : 100f },
                        { "height_in_vr", display.settings != null ? display.settings.heightInVR : 0f },
                    };

            List<Dictionary<string, object>> cameraMappingState = new List<Dictionary<string, object>>();
            for (int monitorIndex = 0; monitorIndex < fullScreenManager.monitors.Count; monitorIndex++)
            {
                Monitor monitor = fullScreenManager.monitors[monitorIndex];
                Camera assigned = (Camera)EditorUtility.EntityIdToObject(monitor.cameraEntityId);
                cameraMappingState.Add(
                    new Dictionary<string, object>
                    {
                        { "monitor", monitorIndex + 1 },
                        { "left", monitor.left },
                        { "top", monitor.top },
                        { "camera", assigned == null ? "None" : assigned.name },
                    }
                );
            }

            Dictionary<string, object> taskState =
                task == null
                    ? null
                    : new Dictionary<string, object>
                    {
                        { "require_lick", task.requireLick },
                        { "require_wait", task.requireWait },
                        { "track_length", task.trackLength },
                        { "track_seed", task.trackSeed },
                    };

            List<string> modelOptions = Resources
                .LoadAll<GameObject>("Actors/Prefabs")
                .Select(prefab => prefab.name)
                .Append("None")
                .ToList();

            List<string> controllerOptions = new List<string> { "None" };
            controllerOptions.AddRange(controllers.Select(controller => controller.gameObject.name));

            List<string> cameraOptions = new List<string> { "None" };
            cameraOptions.AddRange(cameras.Select(camera => camera.name));

            return Ok(
                new Dictionary<string, object>
                {
                    {
                        "state",
                        new Dictionary<string, object>
                        {
                            { "actor", actorState },
                            { "mqtt", mqttState },
                            { "display", displayState },
                            { "camera_mapping", cameraMappingState },
                            { "task", taskState },
                        }
                    },
                    {
                        "options",
                        new Dictionary<string, object>
                        {
                            {
                                "actor",
                                new Dictionary<string, object>
                                {
                                    { "model", modelOptions },
                                    { "controller", controllerOptions },
                                }
                            },
                            {
                                "camera_mapping",
                                new Dictionary<string, object> { { "camera", cameraOptions } }
                            },
                        }
                    },
                    {
                        "visibility",
                        new Dictionary<string, object>
                        {
                            {
                                "task",
                                new Dictionary<string, object>
                                {
                                    { "require_lick", hasLickZone },
                                    { "require_wait", hasOccupancyZone },
                                }
                            },
                        }
                    },
                }
            );
        }

        /// <summary>Applies the supplied parameter subset and returns the post-write snapshot.</summary>
        /// <remarks>
        /// Each section is optional and individual fields within a section are also optional. Validation
        /// rejects values outside the enumeration reported by <see cref="ReadTaskParameters"/>, and rejects
        /// require_lick / require_wait writes when the corresponding zone is absent from the scene so the
        /// agent contract matches the GUI's conditional rendering. Mutations flow through the same code
        /// paths the Parameters window uses, including <see cref="EditorUtility.SetDirty"/> on touched
        /// assets and a final <see cref="EditorSceneManager.MarkSceneDirty"/> when any write succeeded.
        /// </remarks>
        /// <param name="args">
        /// The dispatched tool arguments. Optional top-level keys are <c>actor</c>, <c>mqtt</c>,
        /// <c>display</c>, <c>camera_mapping</c>, and <c>task</c>, each carrying the field subset to write.
        /// </param>
        /// <returns>A JSON response carrying the post-write snapshot from <see cref="ReadTaskParameters"/>.</returns>
        private static string WriteTaskParameters(Dictionary<string, object> args)
        {
            ActorObject actor = UnityEngine.Object.FindAnyObjectByType<ActorObject>();
            DisplayObject display = UnityEngine.Object.FindAnyObjectByType<DisplayObject>();
            Task task = UnityEngine.Object.FindAnyObjectByType<Task>();
            MQTTClient client = UnityEngine.Object.FindAnyObjectByType<MQTTClient>();
            ControllerOutput[] controllers = UnityEngine.Object.FindObjectsByType<ControllerOutput>(
                FindObjectsSortMode.None
            );
            Camera[] cameras = UnityEngine
                .Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                .Where(camera =>
                    !camera.CompareTag("MainCamera")
                    && !string.Equals(camera.gameObject.name, "Main Camera", StringComparison.Ordinal)
                )
                .ToArray();

            bool dirty = false;

            if (TryGetSection(args, "actor", out Dictionary<string, object> actorArgs) && actor != null)
            {
                if (actorArgs.TryGetValue("model", out object modelObject) && modelObject is string newModel)
                {
                    string[] validModels = Resources
                        .LoadAll<GameObject>("Actors/Prefabs")
                        .Select(prefab => prefab.name)
                        .Append("None")
                        .ToArray();
                    if (!validModels.Contains(newModel))
                    {
                        return Error($"Invalid model '{newModel}'. Valid: {string.Join(", ", validModels)}");
                    }
                    actor.SetModel(newModel);
                    dirty = true;
                }
                if (
                    actorArgs.TryGetValue("controller", out object controllerObject)
                    && controllerObject is string newController
                )
                {
                    if (string.Equals(newController, "None", StringComparison.Ordinal))
                    {
                        actor.Controller = null;
                    }
                    else
                    {
                        ControllerOutput target = controllers.FirstOrDefault(controller =>
                            controller.gameObject.name == newController
                        );
                        if (target == null)
                        {
                            string message =
                                $"Invalid controller '{newController}'. Valid: None, "
                                + string.Join(", ", controllers.Select(controller => controller.gameObject.name));
                            return Error(message);
                        }
                        actor.Controller = target;
                    }
                    dirty = true;
                }
            }

            if (TryGetSection(args, "mqtt", out Dictionary<string, object> mqttArgs) && client != null)
            {
                if (mqttArgs.TryGetValue("ip", out object ipObject) && ipObject is string newIp)
                {
                    client.ipAddress = newIp;
                    EditorPrefs.SetString("SollertiaVR_MQTT_IP", newIp);
                    dirty = true;
                }
                if (mqttArgs.TryGetValue("port", out object portObject))
                {
                    int newPort = Convert.ToInt32(portObject);
                    client.port = newPort;
                    EditorPrefs.SetInt("SollertiaVR_MQTT_Port", newPort);
                    dirty = true;
                }
            }

            if (TryGetSection(args, "display", out Dictionary<string, object> displayArgs) && display != null)
            {
                if (displayArgs.TryGetValue("current_brightness", out object currentBrightnessObject))
                {
                    display.currentBrightness = Convert.ToSingle(currentBrightnessObject);
                    dirty = true;
                }
                if (display.settings != null)
                {
                    if (displayArgs.TryGetValue("brightness", out object brightnessObject))
                    {
                        display.settings.brightness = Convert.ToSingle(brightnessObject);
                        EditorUtility.SetDirty(display.settings);
                        dirty = true;
                    }
                    if (displayArgs.TryGetValue("height_in_vr", out object heightObject))
                    {
                        display.settings.heightInVR = Convert.ToSingle(heightObject);
                        display.transform.localPosition = new Vector3(0, display.settings.heightInVR, 0);
                        EditorUtility.SetDirty(display.settings);
                        dirty = true;
                    }
                }
            }

            if (
                args.TryGetValue("camera_mapping", out object cameraMappingObject)
                && cameraMappingObject is List<object> cameraMappingList
            )
            {
                FullScreenViewManager fullScreenManager = AcquireFullScreenManager();
                foreach (object row in cameraMappingList)
                {
                    if (row is not Dictionary<string, object> rowDict)
                    {
                        continue;
                    }
                    if (!rowDict.TryGetValue("monitor", out object monitorObject))
                    {
                        continue;
                    }
                    int monitorIndex = Convert.ToInt32(monitorObject) - 1;
                    if (monitorIndex < 0 || monitorIndex >= fullScreenManager.monitors.Count)
                    {
                        string message =
                            $"Invalid monitor index {monitorIndex + 1}; scene has "
                            + $"{fullScreenManager.monitors.Count} monitors.";
                        return Error(message);
                    }
                    if (
                        !rowDict.TryGetValue("camera", out object cameraObject) || cameraObject is not string cameraName
                    )
                    {
                        continue;
                    }
                    if (string.Equals(cameraName, "None", StringComparison.Ordinal))
                    {
                        fullScreenManager.monitors[monitorIndex].cameraEntityId = EntityId.None;
                    }
                    else
                    {
                        Camera target = cameras.FirstOrDefault(camera => camera.name == cameraName);
                        if (target == null)
                        {
                            string message =
                                $"Invalid camera '{cameraName}' for monitor {monitorIndex + 1}. Valid: None, "
                                + string.Join(", ", cameras.Select(camera => camera.name));
                            return Error(message);
                        }
                        fullScreenManager.monitors[monitorIndex].cameraEntityId = target.GetEntityId();
                    }
                }
                fullScreenManager.SaveCameras();
                dirty = true;
            }

            if (TryGetSection(args, "task", out Dictionary<string, object> taskArgs) && task != null)
            {
                bool hasLickZone = UnityEngine.Object.FindAnyObjectByType<GuidanceZone>() != null;
                bool hasOccupancyZone = UnityEngine.Object.FindAnyObjectByType<OccupancyZone>() != null;

                if (taskArgs.ContainsKey("require_lick") && !hasLickZone)
                {
                    string message =
                        "Cannot set require_lick: the active scene has no GuidanceZone, so the control is "
                        + "hidden in the Parameters window and the flag has no runtime effect.";
                    return Error(message);
                }
                if (taskArgs.ContainsKey("require_wait") && !hasOccupancyZone)
                {
                    string message =
                        "Cannot set require_wait: the active scene has no OccupancyZone, so the control is "
                        + "hidden in the Parameters window and the flag has no runtime effect.";
                    return Error(message);
                }

                Undo.RecordObject(task, "Write Task Parameters");
                if (taskArgs.TryGetValue("require_lick", out object requireLickObject))
                {
                    task.requireLick = Convert.ToBoolean(requireLickObject);
                    dirty = true;
                }
                if (taskArgs.TryGetValue("require_wait", out object requireWaitObject))
                {
                    task.requireWait = Convert.ToBoolean(requireWaitObject);
                    dirty = true;
                }
                if (taskArgs.TryGetValue("track_length", out object trackLengthObject))
                {
                    task.trackLength = Convert.ToSingle(trackLengthObject);
                    dirty = true;
                }
                if (taskArgs.TryGetValue("track_seed", out object trackSeedObject))
                {
                    task.trackSeed = Convert.ToInt32(trackSeedObject);
                    dirty = true;
                }
                EditorUtility.SetDirty(task);
            }

            if (dirty)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            return ReadTaskParameters();
        }

        /// <summary>Extracts a sub-dictionary at the given key from the args; returns false when absent.</summary>
        /// <param name="args">The dispatched tool arguments.</param>
        /// <param name="key">The top-level section key to look up.</param>
        /// <param name="section">The extracted sub-dictionary when present, otherwise null.</param>
        /// <returns>True when the section was found and is a dictionary.</returns>
        private static bool TryGetSection(
            Dictionary<string, object> args,
            string key,
            out Dictionary<string, object> section
        )
        {
            section = null;
            if (args.TryGetValue(key, out object value) && value is Dictionary<string, object> dict)
            {
                section = dict;
                return true;
            }
            return false;
        }

        /// <summary>Reuses the open Parameters window's FullScreenViewManager, else builds a fresh one.</summary>
        /// <remarks>
        /// Sharing the instance keeps the open Parameters tab in sync with API writes without an explicit
        /// reload round-trip. Falling back to a fresh manager (with cameras loaded from the saved asset)
        /// lets the bridge serve scenes where the window is currently closed. Uses
        /// <see cref="Resources.FindObjectsOfTypeAll{T}()"/> to locate an existing window instance without
        /// creating a new one as a side effect.
        /// </remarks>
        /// <returns>A FullScreenViewManager whose monitor list reflects the current scene state.</returns>
        private static FullScreenViewManager AcquireFullScreenManager()
        {
            MainWindow window = Resources.FindObjectsOfTypeAll<MainWindow>().FirstOrDefault();
            if (window != null && window.fullScreenManager != null)
            {
                return window.fullScreenManager;
            }
            FullScreenViewManager manager = new FullScreenViewManager();
            manager.LoadCameras();
            return manager;
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
        /// <returns>
        /// An error message when the active scene is dirty and no policy was provided, otherwise null.
        /// </returns>
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
            List<(string cueName, float localZ)> cueChildren = new List<(string cueName, float localZ)>();
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
}
