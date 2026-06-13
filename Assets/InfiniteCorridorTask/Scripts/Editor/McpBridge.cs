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

        /// <summary>The shared error-protocol prefix returned by <see cref="CreateTask.CreateFromTemplate"/>.</summary>
        private const string CreateTaskErrorPrefix = "error: ";

        /// <summary>
        /// The set of project-relative directory prefixes under which non-scene assets may be deleted via
        /// <c>delete_asset</c>.
        /// </summary>
        /// <remarks>
        /// Scenes are intentionally absent — they are deleted exclusively through <see cref="DestroyTask"/>,
        /// which also cascade-deletes the per-scene <c>savedFullScreenViews</c> companion. Adding a scenes
        /// entry here would let scene paths bypass that cascade.
        /// </remarks>
        private static readonly string[] DeleteAllowedPrefixes =
        {
            "Assets/InfiniteCorridorTask/Tasks/",
            "Assets/InfiniteCorridorTask/Prefabs/",
            "Assets/InfiniteCorridorTask/Cues/",
            "Assets/InfiniteCorridorTask/Materials/",
        };

        /// <summary>The set of hand-authored asset paths that are protected from deletion.</summary>
        /// <remarks>
        /// Covers every hand-authored asset that the CreateTask pipeline (or the generated zone
        /// prefabs themselves) load by hardcoded path or by serialized reference. Removing any
        /// one of these breaks task generation or leaves a regenerated prefab rendering with a
        /// missing material, so the bridge refuses to delete them even when they sit under an
        /// allowed prefix.
        /// </remarks>
        private static readonly HashSet<string> DeleteProtectedPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            "Assets/InfiniteCorridorTask/Prefabs/StimulusTriggerZone.prefab",
            "Assets/InfiniteCorridorTask/Prefabs/OccupancyTriggerZone.prefab",
            "Assets/InfiniteCorridorTask/Prefabs/ResetZone.prefab",
            "Assets/InfiniteCorridorTask/Prefabs/Padding.prefab",
            "Assets/InfiniteCorridorTask/Materials/_CueShaderReference.mat",
            "Assets/InfiniteCorridorTask/Materials/Floor.mat",
            "Assets/InfiniteCorridorTask/Materials/Wall.mat",
            "Assets/InfiniteCorridorTask/Materials/TargetMat.mat",
            "Assets/Scenes/ExperimentTemplate.unity",
        };

        /// <summary>The canonical hand-authored zone prefabs that may serve as a clone source.</summary>
        /// <remarks>
        /// Restricting the source to the two protected base prefabs keeps every generated zone descended from a
        /// known-good, hand-authored structure, so the handler validates against a fixed shape and the
        /// hand-authored-versus-generated boundary stays crisp. A third sanctioned base would be added here.
        /// </remarks>
        private static readonly HashSet<string> CloneSourcePrefabs = new HashSet<string>(StringComparer.Ordinal)
        {
            "Assets/InfiniteCorridorTask/Prefabs/StimulusTriggerZone.prefab",
            "Assets/InfiniteCorridorTask/Prefabs/OccupancyTriggerZone.prefab",
        };

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
                // Uses TryGetValue + null check so a JSON-null value for "tool" does not NRE on ToString();
                // a missing or non-dictionary "args" value falls back to an empty dict so dispatched tools
                // always receive a well-formed args parameter.
                string tool =
                    request.TryGetValue("tool", out object toolObject) && toolObject != null
                        ? toolObject.ToString()
                        : string.Empty;
                Dictionary<string, object> args =
                    request.TryGetValue("args", out object argsObject)
                    && argsObject is Dictionary<string, object> argsDict
                        ? argsDict
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
                "create_task" => GenerateTask(args),
                "delete_task" => DestroyTask(args),
                "inspect_prefab" => InspectPrefab(args),
                "clone_zone_prefab" => CloneZonePrefab(args),
                "delete_asset" => DeleteAsset(args),
                "list_assets" => ListAssets(args),
                "list_scenes" => ListScenes(),
                "open_scene" => OpenScene(args),
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
        /// Generates a Task end-to-end from a YAML template: builds the task prefab and the matching scene
        /// in one call by chaining <see cref="CreateTask.CreateFromTemplate"/> and
        /// <see cref="CreateTask.CreateSceneFromTemplate"/>.
        /// </summary>
        /// <remarks>
        /// Mirrors the <c>CreateTask/New Task</c> Editor menu so the agentic and manual paths produce
        /// byte-equivalent assets. The prefab lands at <c>Assets/InfiniteCorridorTask/Tasks/&lt;template&gt;.prefab</c>
        /// and the scene at <c>Assets/Scenes/&lt;template&gt;.unity</c>; both paths are auto-resolved from the
        /// template basename to eliminate the agentic surface's need to manage them separately. Refuses to
        /// clobber an existing scene at the resolved path so an automated client never silently destroys a
        /// hand-edited scene — use <c>delete_task</c> first to regenerate. The prefab itself is always
        /// regenerated because the template is authoritative.
        /// </remarks>
        /// <param name="args">The tool arguments containing template_name.</param>
        /// <returns>A JSON response with the generated prefab and scene paths or an error message.</returns>
        private static string GenerateTask(Dictionary<string, object> args)
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

            // The path is stored on the Task component and resolved at runtime as
            // ``Path.Combine(Application.dataPath, configPath)``; a leading ``/`` would make
            // Path.Combine treat the value as absolute on Linux/macOS and discard the data path.
            string relativeConfigPath = Path.Combine("InfiniteCorridorTask", "Configurations", $"{templateName}.yaml");

            string prefabSavePath = Path.Combine("Assets", "InfiniteCorridorTask", "Tasks", $"{templateName}.prefab");
            string sceneSavePath = Path.Combine("Assets", "Scenes", $"{templateName}.unity");

            // Refuses to clobber an existing scene before generating the prefab so a regeneration cycle
            // is an explicit two-step action: delete_task first, then create_task. Checking up front
            // avoids leaving a regenerated prefab behind without the matching scene on overwrite refusal.
            if (File.Exists(sceneSavePath))
            {
                string message = $"Scene already exists at: {sceneSavePath}. Call delete_task first to regenerate.";
                return Error(message);
            }

            // Ensures the Tasks output directory exists before CreateFromTemplate writes the prefab.
            string tasksDirectory = Path.GetDirectoryName(prefabSavePath);
            if (!string.IsNullOrEmpty(tasksDirectory) && !AssetDatabase.IsValidFolder(tasksDirectory))
            {
                string parent = Path.GetDirectoryName(tasksDirectory);
                string folder = Path.GetFileName(tasksDirectory);
                if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(folder))
                {
                    AssetDatabase.CreateFolder(parent, folder);
                }
            }

            string prefabResult = CreateTask.CreateFromTemplate(
                absoluteTemplatePath,
                relativeConfigPath,
                prefabSavePath
            );

            if (prefabResult.StartsWith(CreateTaskErrorPrefix, StringComparison.Ordinal))
            {
                return Error(prefabResult.Substring(CreateTaskErrorPrefix.Length).Trim());
            }

            CreateTask.SceneCreationResult sceneResult = CreateTask.CreateSceneFromTemplate(
                sceneSavePath: sceneSavePath,
                taskPrefabPath: prefabSavePath,
                overwriteExisting: false
            );

            if (!sceneResult.Success)
            {
                string message =
                    $"Prefab generated at {prefabSavePath} but scene creation failed: {sceneResult.Message}";
                return Error(message);
            }

            Dictionary<string, object> response = new Dictionary<string, object>
            {
                { "message", sceneResult.Message },
                { "template_name", templateName },
                { "prefab_path", prefabSavePath },
                { "scene_path", sceneSavePath },
                { "simulated_controller_added", sceneResult.SimulatedControllerAdded },
            };

            return Ok(response);
        }

        /// <summary>
        /// Removes every Unity artifact that <see cref="GenerateTask"/> produces for a given template in a
        /// single call: the scene plus its <c>savedFullScreenViews</c> companion, the task prefab, and
        /// every segment prefab whose filename begins with the template basename.
        /// </summary>
        /// <remarks>
        /// Mirrors <c>create_task</c> so the two tools cover the full lifecycle of a task's generated
        /// artifacts. Cue prefabs and cue materials are intentionally **not** removed because they are
        /// shared across every template that declares a matching <c>(name, length_cm)</c> identity;
        /// deleting them would corrupt sibling tasks. Use <c>delete_asset</c> for individual cue
        /// cleanup. The template YAML is also preserved as the source of truth.
        /// </remarks>
        /// <param name="args">The tool arguments containing template_name.</param>
        /// <returns>A JSON response listing every deleted path or an error message.</returns>
        private static string DestroyTask(Dictionary<string, object> args)
        {
            string templateName = GetString(args, "template_name");

            if (string.IsNullOrEmpty(templateName))
            {
                return Error("Missing required argument: template_name");
            }

            string scenePath = Path.Combine("Assets", "Scenes", $"{templateName}.unity");
            string prefabPath = Path.Combine("Assets", "InfiniteCorridorTask", "Tasks", $"{templateName}.prefab");
            string segmentPrefix = $"Assets/InfiniteCorridorTask/Prefabs/{templateName}_";

            List<string> deletedPaths = new List<string>();
            string companionDeleted = null;

            // Deletes the scene first so Unity can release the active-scene lock before any prefab the
            // scene instantiates is removed. The active-scene swap below is part of this same delete flow.
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(scenePath) != null)
            {
                Scene activeScene = SceneManager.GetActiveScene();
                if (string.Equals(activeScene.path, scenePath, StringComparison.Ordinal))
                {
                    EditorSceneManager.OpenScene("Assets/Scenes/ExperimentTemplate.unity", OpenSceneMode.Single);
                }
                if (AssetDatabase.DeleteAsset(scenePath))
                {
                    deletedPaths.Add(scenePath);
                    companionDeleted = TryDeleteScenePerSceneCompanions(scenePath);
                }
            }

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabPath) != null)
            {
                if (AssetDatabase.DeleteAsset(prefabPath))
                {
                    deletedPaths.Add(prefabPath);
                }
            }

            // Sweeps every segment prefab whose filename begins with ``<template>_``. Filenames are owned
            // outright by the template basename + trial key, so a prefix match is sufficient to identify
            // segment prefabs without cross-checking the template YAML.
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/InfiniteCorridorTask/Prefabs" });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith(segmentPrefix, StringComparison.Ordinal))
                {
                    continue;
                }
                if (AssetDatabase.DeleteAsset(path))
                {
                    deletedPaths.Add(path);
                }
            }

            AssetDatabase.Refresh();

            if (deletedPaths.Count == 0)
            {
                return Error($"No artifacts found for template '{templateName}'.");
            }

            Dictionary<string, object> response = new Dictionary<string, object>
            {
                { "message", $"Deleted task: {templateName}" },
                { "template_name", templateName },
                { "deleted_paths", deletedPaths },
                { "deleted", true },
            };
            if (companionDeleted != null)
            {
                response["companion_deleted"] = companionDeleted;
            }
            return Ok(response);
        }

        /// <summary>Reads a prefab and returns its hierarchy, components, and BoxCollider details.</summary>
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

        /// <summary>Clones a canonical zone prefab into a new trigger-zone prefab.</summary>
        /// <remarks>
        /// Performs the prefab-authoring step of adding a new trigger zone through Unity's serialization layer rather
        /// than by hand-editing YAML, so fileIDs, script references, and parent-child wiring are assigned by Unity and
        /// cannot drift. The requested MonoBehaviour scripts must already be authored and compiled. The handler only
        /// produces the prefab; wiring it into ConfigLoader, CreateTask, the protected-path set, and the Python
        /// TriggerType registry remains the documented recipe. Unity names the new prefab's root after the
        /// destination filename.
        /// </remarks>
        /// <param name="args">
        /// The tool arguments: source_prefab, destination_prefab, and optional root_script, regions, and overwrite.
        /// </param>
        /// <returns>A JSON response with the destination path and resulting hierarchy, or an error message.</returns>
        private static string CloneZonePrefab(Dictionary<string, object> args)
        {
            string sourcePrefab = GetString(args, "source_prefab");
            string destinationPrefab = GetString(args, "destination_prefab");
            string rootScript = GetString(args, "root_script");
            bool overwrite = GetBool(args, "overwrite", defaultValue: false);

            if (string.IsNullOrEmpty(sourcePrefab) || string.IsNullOrEmpty(destinationPrefab))
            {
                return Error("Missing required arguments: source_prefab and destination_prefab.");
            }

            if (!CloneSourcePrefabs.Contains(sourcePrefab))
            {
                string allowed = string.Join(", ", CloneSourcePrefabs);
                return Error($"source_prefab must be a canonical base zone prefab ({allowed}).");
            }

            string destinationError = ValidateCloneDestination(destinationPrefab);
            if (destinationError != null)
            {
                return Error(destinationError);
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(destinationPrefab) != null)
            {
                if (!overwrite)
                {
                    return Error(
                        $"A prefab already exists at '{destinationPrefab}'. Pass overwrite=true to replace it."
                    );
                }

                AssetDatabase.DeleteAsset(destinationPrefab);
            }

            // Resolves requested scripts up front so a bad name fails before any asset is written.
            string resolveError = ResolveCloneScripts(
                rootScript,
                GetList(args, "regions"),
                out Type rootScriptType,
                out List<(Dictionary<string, object> Spec, Type ScriptType)> regionEdits
            );
            if (resolveError != null)
            {
                return Error(resolveError);
            }

            if (!AssetDatabase.CopyAsset(sourcePrefab, destinationPrefab))
            {
                return Error($"Failed to copy '{sourcePrefab}' to '{destinationPrefab}'.");
            }

            string editError = null;
            GameObject root = PrefabUtility.LoadPrefabContents(destinationPrefab);
            try
            {
                if (rootScriptType != null)
                {
                    editError = SwapZoneScript(root, rootScriptType, typeof(StimulusTriggerZone), fields: null);
                }

                for (int i = 0; editError == null && i < regionEdits.Count; i++)
                {
                    editError = ApplyRegionEdit(root, regionEdits[i]);
                }

                if (editError == null)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, destinationPrefab);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            if (editError != null)
            {
                AssetDatabase.DeleteAsset(destinationPrefab);
                return Error(editError);
            }

            AssetDatabase.Refresh();

            GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(destinationPrefab);
            Dictionary<string, object> response = new Dictionary<string, object>
            {
                { "destination_prefab", destinationPrefab },
                { "hierarchy", InspectGameObject(saved) },
                {
                    "warning",
                    "Prefab created. Still required to make it usable: add the path to "
                        + "McpBridge.DeleteProtectedPaths, add a Place...Zone branch in CreateTask, "
                        + "accept the new trigger_type literal in ConfigLoader, and register the "
                        + "TriggerType member in sollertia-shared-assets."
                },
            };
            return Ok(response);
        }

        /// <summary>Resolves the root and region script names to compiled types before any asset is written.</summary>
        /// <param name="rootScript">The root script type name, or null to keep the source root script.</param>
        /// <param name="regions">The raw region edit specifications from the request.</param>
        /// <param name="rootScriptType">The resolved root script type, or null when none was requested.</param>
        /// <param name="regionEdits">Validated region specs paired with resolved script types.</param>
        /// <returns>An error message when a name fails to resolve or a region is malformed, otherwise null.</returns>
        private static string ResolveCloneScripts(
            string rootScript,
            List<object> regions,
            out Type rootScriptType,
            out List<(Dictionary<string, object> Spec, Type ScriptType)> regionEdits
        )
        {
            rootScriptType = null;
            regionEdits = new List<(Dictionary<string, object>, Type)>();

            if (!string.IsNullOrEmpty(rootScript))
            {
                string error = ResolveMonoBehaviourType(rootScript, out rootScriptType);
                if (error != null)
                {
                    return error;
                }
            }

            foreach (object regionObject in regions)
            {
                if (regionObject is not Dictionary<string, object> spec)
                {
                    return "Each entry in 'regions' must be an object.";
                }

                if (string.IsNullOrEmpty(GetString(spec, "match")))
                {
                    return "Each region edit must specify 'match' (the name of the region to modify).";
                }

                Type scriptType = null;
                string scriptName = GetString(spec, "script");
                if (!string.IsNullOrEmpty(scriptName))
                {
                    string error = ResolveMonoBehaviourType(scriptName, out scriptType);
                    if (error != null)
                    {
                        return error;
                    }
                }

                regionEdits.Add((spec, scriptType));
            }

            return null;
        }

        /// <summary>Resolves a MonoBehaviour type by its simple name across compiled assemblies.</summary>
        /// <param name="typeName">The simple class name to resolve.</param>
        /// <param name="resolved">The resolved type on success, otherwise null.</param>
        /// <returns>An error message when the name is unknown or ambiguous, otherwise null.</returns>
        private static string ResolveMonoBehaviourType(string typeName, out Type resolved)
        {
            resolved = null;
            List<Type> matches = TypeCache
                .GetTypesDerivedFrom<MonoBehaviour>()
                .Where(type => string.Equals(type.Name, typeName, StringComparison.Ordinal))
                .ToList();

            if (matches.Count == 0)
            {
                return $"Script type '{typeName}' not found. Author the script and let the project compile.";
            }

            if (matches.Count > 1)
            {
                return $"Script type '{typeName}' is ambiguous ({matches.Count} matches). Use a unique class name.";
            }

            resolved = matches[0];
            return null;
        }

        /// <summary>Validates that a clone destination is a safe, unprotected path under Prefabs/.</summary>
        /// <param name="destinationPrefab">The requested destination asset path.</param>
        /// <returns>An error message when the path is unsafe, misplaced, or protected, otherwise null.</returns>
        private static string ValidateCloneDestination(string destinationPrefab)
        {
            if (destinationPrefab.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(destinationPrefab))
            {
                return $"Invalid destination_prefab '{destinationPrefab}': traversal and absolute paths are rejected.";
            }

            if (!destinationPrefab.StartsWith("Assets/InfiniteCorridorTask/Prefabs/", StringComparison.Ordinal))
            {
                return "destination_prefab must be under Assets/InfiniteCorridorTask/Prefabs/.";
            }

            if (!destinationPrefab.EndsWith(".prefab", StringComparison.Ordinal))
            {
                return "destination_prefab must end with .prefab.";
            }

            if (DeleteProtectedPaths.Contains(destinationPrefab) || CloneSourcePrefabs.Contains(destinationPrefab))
            {
                return $"destination_prefab '{destinationPrefab}' is a protected base prefab.";
            }

            return null;
        }

        /// <summary>Applies one region edit (rename, script swap, field overrides) to a cloned prefab.</summary>
        /// <param name="root">The root GameObject of the loaded prefab contents.</param>
        /// <param name="edit">The validated region specification paired with its resolved script type.</param>
        /// <returns>An error message when the region cannot be located or edited, otherwise null.</returns>
        private static string ApplyRegionEdit(GameObject root, (Dictionary<string, object> Spec, Type ScriptType) edit)
        {
            GameObject region = FindUniqueDescendant(root, GetString(edit.Spec, "match"), out string findError);
            if (findError != null)
            {
                return findError;
            }

            string rename = GetString(edit.Spec, "rename");
            if (!string.IsNullOrEmpty(rename))
            {
                region.name = rename;
            }

            Dictionary<string, object> fields = GetDict(edit.Spec, "fields");

            if (edit.ScriptType != null)
            {
                return SwapZoneScript(region, edit.ScriptType, requireBaseType: null, fields: fields);
            }

            if (fields.Count > 0)
            {
                MonoBehaviour modifier = FindSingleZoneModifier(region, out string modifierError);
                if (modifierError != null)
                {
                    return modifierError;
                }

                return ApplyFieldOverrides(modifier, fields);
            }

            return null;
        }

        /// <summary>Replaces a GameObject's single modifier script, preserving shared field values.</summary>
        /// <param name="target">The GameObject whose modifier script is replaced.</param>
        /// <param name="scriptType">The replacement MonoBehaviour type.</param>
        /// <param name="requireBaseType">A base type the replacement must derive from, or null to allow any.</param>
        /// <param name="fields">Field overrides to apply after the swap, or null to apply none.</param>
        /// <returns>An error message when the swap or overrides fail, otherwise null.</returns>
        private static string SwapZoneScript(
            GameObject target,
            Type scriptType,
            Type requireBaseType,
            Dictionary<string, object> fields
        )
        {
            if (requireBaseType != null && !requireBaseType.IsAssignableFrom(scriptType))
            {
                return $"Root script '{scriptType.Name}' must derive from {requireBaseType.Name}.";
            }

            MonoBehaviour existing = FindSingleZoneModifier(target, out string modifierError);
            if (modifierError != null)
            {
                return modifierError;
            }

            Component added = target.AddComponent(scriptType);
            CopyMatchingSerializedFields(existing, added);
            UnityEngine.Object.DestroyImmediate(existing, allowDestroyingAssets: true);

            if (fields != null && fields.Count > 0)
            {
                return ApplyFieldOverrides(added, fields);
            }

            return null;
        }

        /// <summary>Finds the single modifier MonoBehaviour on a GameObject.</summary>
        /// <param name="target">The GameObject to inspect.</param>
        /// <param name="error">An error message when the modifier count is not exactly one, otherwise null.</param>
        /// <returns>The single MonoBehaviour, or null when the count is not exactly one.</returns>
        private static MonoBehaviour FindSingleZoneModifier(GameObject target, out string error)
        {
            error = null;
            MonoBehaviour[] behaviours = target.GetComponents<MonoBehaviour>();
            if (behaviours.Length != 1)
            {
                error = $"Expected exactly one modifier script on '{target.name}', but found {behaviours.Length}.";
                return null;
            }

            return behaviours[0];
        }

        /// <summary>Finds the single named descendant GameObject, rejecting an absent or ambiguous match.</summary>
        /// <param name="root">The root GameObject to search beneath.</param>
        /// <param name="name">The descendant name to match.</param>
        /// <param name="error">An error message when the match count is not exactly one, otherwise null.</param>
        /// <returns>The matched GameObject, or null when the match count is not exactly one.</returns>
        private static GameObject FindUniqueDescendant(GameObject root, string name, out string error)
        {
            error = null;
            List<Transform> matches = root.GetComponentsInChildren<Transform>(includeInactive: true)
                .Where(child => child != root.transform && string.Equals(child.name, name, StringComparison.Ordinal))
                .ToList();

            if (matches.Count == 0)
            {
                error = $"No region named '{name}' was found under the cloned prefab.";
                return null;
            }

            if (matches.Count > 1)
            {
                error = $"Region name '{name}' is ambiguous ({matches.Count} matches) under the cloned prefab.";
                return null;
            }

            return matches[0].gameObject;
        }

        /// <summary>Copies serialized values between two components for every property they share by path.</summary>
        /// <param name="from">The component to read values from.</param>
        /// <param name="to">The component to write matching values to.</param>
        private static void CopyMatchingSerializedFields(Component from, Component to)
        {
            SerializedObject source = new SerializedObject(from);
            SerializedObject destination = new SerializedObject(to);

            SerializedProperty property = source.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (string.Equals(property.name, "m_Script", StringComparison.Ordinal))
                {
                    continue;
                }

                if (destination.FindProperty(property.propertyPath) != null)
                {
                    destination.CopyFromSerializedProperty(property);
                }
            }

            destination.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Applies field overrides onto a component, rejecting unknown or mistyped fields.</summary>
        /// <param name="target">The component whose serialized fields are overridden.</param>
        /// <param name="fields">The field-name to value map to apply.</param>
        /// <returns>An error message when a field is unknown or cannot be assigned, otherwise null.</returns>
        private static string ApplyFieldOverrides(Component target, Dictionary<string, object> fields)
        {
            SerializedObject serialized = new SerializedObject(target);
            foreach (KeyValuePair<string, object> field in fields)
            {
                SerializedProperty property = serialized.FindProperty(field.Key);
                if (property == null)
                {
                    return $"Field '{field.Key}' does not exist on {target.GetType().Name}.";
                }

                string error = SetSerializedProperty(property, field.Value);
                if (error != null)
                {
                    return error;
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return null;
        }

        /// <summary>Assigns a boxed value to a serialized property, matching its type.</summary>
        /// <param name="property">The serialized property to assign.</param>
        /// <param name="value">The boxed value from the request payload.</param>
        /// <returns>An error message when the type is unsupported or the conversion fails, otherwise null.</returns>
        private static string SetSerializedProperty(SerializedProperty property, object value)
        {
            try
            {
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        property.intValue = Convert.ToInt32(value);
                        return null;
                    case SerializedPropertyType.Boolean:
                        property.boolValue = Convert.ToBoolean(value);
                        return null;
                    case SerializedPropertyType.Float:
                        property.floatValue = Convert.ToSingle(value);
                        return null;
                    case SerializedPropertyType.String:
                        property.stringValue = value.ToString();
                        return null;
                    case SerializedPropertyType.Enum:
                        property.intValue = Convert.ToInt32(value);
                        return null;
                    default:
                        return $"Field '{property.name}' has unsupported type {property.propertyType}.";
                }
            }
            catch (Exception exception)
                when (exception is FormatException
                    || exception is InvalidCastException
                    || exception is OverflowException
                )
            {
                return $"Failed to set field '{property.name}': {exception.Message}";
            }
        }

        /// <summary>Retrieves a boolean value from the arguments dictionary with an optional default.</summary>
        /// <param name="args">The arguments dictionary to search.</param>
        /// <param name="key">The key to look up.</param>
        /// <param name="defaultValue">The default value when the key is absent or unparseable.</param>
        /// <returns>The parsed boolean value, or the default.</returns>
        private static bool GetBool(Dictionary<string, object> args, string key, bool defaultValue = false)
        {
            if (args.TryGetValue(key, out object value) && value != null)
            {
                if (value is bool boolValue)
                {
                    return boolValue;
                }

                if (bool.TryParse(value.ToString(), out bool parsed))
                {
                    return parsed;
                }
            }

            return defaultValue;
        }

        /// <summary>Retrieves a list value from the arguments dictionary, or an empty list when absent.</summary>
        /// <param name="args">The arguments dictionary to search.</param>
        /// <param name="key">The key to look up.</param>
        /// <returns>The list value, or an empty list when the key is absent or not a list.</returns>
        private static List<object> GetList(Dictionary<string, object> args, string key)
        {
            if (args.TryGetValue(key, out object value) && value is List<object> list)
            {
                return list;
            }

            return new List<object>();
        }

        /// <summary>Retrieves a nested object from the arguments dictionary, or empty when absent.</summary>
        /// <param name="args">The arguments dictionary to search.</param>
        /// <param name="key">The key to look up.</param>
        /// <returns>The dictionary value, or an empty dictionary when the key is absent or not an object.</returns>
        private static Dictionary<string, object> GetDict(Dictionary<string, object> args, string key)
        {
            if (args.TryGetValue(key, out object value) && value is Dictionary<string, object> dict)
            {
                return dict;
            }

            return new Dictionary<string, object>();
        }

        /// <summary>Deletes a Unity asset within an allowed directory and refreshes the AssetDatabase.</summary>
        /// <remarks>
        /// Scoped to regenerable non-scene assets — primarily cue prefabs and cue materials that the
        /// <see cref="GenerateTask"/> pipeline shares across templates and therefore cannot scrub
        /// per-task. Scene deletion is handled exclusively by <see cref="DestroyTask"/>, which removes
        /// the scene plus its <c>savedFullScreenViews</c> companion atomically; scene paths submitted
        /// here are rejected with a pointer at <c>delete_task</c> so scene cleanup never bypasses the
        /// companion cascade.
        /// </remarks>
        /// <param name="args">The tool arguments containing asset_path.</param>
        /// <returns>A JSON response confirming deletion or an error message.</returns>
        private static string DeleteAsset(Dictionary<string, object> args)
        {
            string assetPath = GetString(args, "asset_path");

            if (string.IsNullOrEmpty(assetPath))
            {
                return Error("Missing required argument: asset_path");
            }

            if (
                assetPath.StartsWith("Assets/Scenes/", StringComparison.Ordinal)
                && assetPath.EndsWith(".unity", StringComparison.Ordinal)
            )
            {
                string message =
                    $"Refusing to delete scene '{assetPath}' via delete_asset. Use delete_task to remove a "
                    + "task's scene together with its task prefab and segment prefabs in one atomic call.";
                return Error(message);
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

        /// <summary>Deletes per-scene companion assets when a scene under Assets/Scenes/ is removed.</summary>
        /// <remarks>
        /// Bypasses the standard <see cref="IsDeleteAllowed"/> prefix check because the companion path is
        /// derived from the just-validated scene path, never user-supplied. Currently covers the saved
        /// full-screen-views asset; extend this method when new per-scene companion assets are introduced.
        /// </remarks>
        /// <param name="scenePath">The project-relative path of the scene that was just deleted.</param>
        /// <returns>The companion path that was deleted, or null when no companion existed.</returns>
        private static string TryDeleteScenePerSceneCompanions(string scenePath)
        {
            if (
                !scenePath.StartsWith("Assets/Scenes/", StringComparison.Ordinal)
                || !scenePath.EndsWith(".unity", StringComparison.Ordinal)
            )
            {
                return null;
            }
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            string companionPath = $"Assets/VRSettings/Displays/{sceneName}-savedFullScreenViews.asset";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(companionPath) == null)
            {
                return null;
            }
            return AssetDatabase.DeleteAsset(companionPath) ? companionPath : null;
        }

        /// <summary>
        /// Lists Unity assets of a given type filter (e.g., "Prefab", "Scene", "Material").
        /// </summary>
        /// <param name="args">The tool arguments containing optional asset_type and search_path filters.</param>
        /// <returns>A JSON response with matching asset paths.</returns>
        private static string ListAssets(Dictionary<string, object> args)
        {
            string assetType = GetString(args, "asset_type", defaultValue: "Prefab");
            string searchPath = GetString(args, "search_path", defaultValue: "Assets/InfiniteCorridorTask");

            string[] guids = AssetDatabase.FindAssets($"t:{assetType}", new[] { searchPath });
            List<string> paths = guids.Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path).ToList();

            return Ok(
                new Dictionary<string, object>
                {
                    { "asset_type", assetType },
                    { "search_path", searchPath },
                    { "assets", paths },
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

            return Ok(new Dictionary<string, object> { { "scenes", paths }, { "active_scene", activeScene } });
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

        /// <summary>
        /// Returns a single-scan snapshot of every Task Parameters field plus options and visibility.
        /// </summary>
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
            return Ok(BuildSnapshot(AcquireSceneComponents()));
        }

        /// <summary>Applies the supplied parameter subset and returns the post-write snapshot.</summary>
        /// <remarks>
        /// Each section is optional and individual fields within a section are also optional. Validation
        /// rejects values outside the enumeration reported by <see cref="ReadTaskParameters"/>, and rejects
        /// require_interaction / require_wait writes when the corresponding zone is absent from the scene so the
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
            SceneComponents components = AcquireSceneComponents();
            bool dirty = false;

            string error = TryWriteActorSection(args, components, ref dirty);
            if (error != null)
            {
                return Error(error);
            }

            error = TryWriteMqttSection(args, components, ref dirty);
            if (error != null)
            {
                return Error(error);
            }

            error = TryWriteDisplaySection(args, components, ref dirty);
            if (error != null)
            {
                return Error(error);
            }

            error = TryWriteCameraMappingSection(args, components, ref dirty);
            if (error != null)
            {
                return Error(error);
            }

            error = TryWriteTaskSection(args, components, ref dirty);
            if (error != null)
            {
                return Error(error);
            }

            if (dirty)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            return Ok(BuildSnapshot(components));
        }

        /// <summary>
        /// Aggregates the per-scene component references read by both <see cref="ReadTaskParameters"/> and
        /// <see cref="WriteTaskParameters"/>. Built once per request via <see cref="AcquireSceneComponents"/>
        /// so each tool invocation walks the scene exactly once, regardless of how many sections the writer
        /// touches.
        /// </summary>
        private struct SceneComponents
        {
            /// <summary>The active scene's actor, or null when absent.</summary>
            public ActorObject Actor;

            /// <summary>The active scene's display, or null when absent.</summary>
            public DisplayObject Display;

            /// <summary>The active scene's Task component, or null when absent.</summary>
            public Task Task;

            /// <summary>The active scene's MQTT client singleton, or null when absent.</summary>
            public MQTTClient Client;

            /// <summary>Every <see cref="ControllerOutput"/> in the active scene.</summary>
            public ControllerOutput[] Controllers;

            /// <summary>Every assignable display camera (MainCamera excluded) in the active scene.</summary>
            public Camera[] DisplayCameras;

            /// <summary>Determines whether the scene contains at least one <see cref="GuidanceZone"/>.</summary>
            public bool HasInteractionZone;

            /// <summary>Determines whether the scene contains at least one <see cref="OccupancyZone"/>.</summary>
            public bool HasOccupancyZone;

            /// <summary>
            /// The shared <see cref="FullScreenViewManager"/> used by camera-mapping reads and writes.
            /// </summary>
            public FullScreenViewManager FullScreenManager;
        }

        /// <summary>
        /// Performs the single scene walk shared by <see cref="ReadTaskParameters"/> and
        /// <see cref="WriteTaskParameters"/>.
        /// </summary>
        /// <returns>A snapshot of every component the Task Parameters endpoints consume.</returns>
        private static SceneComponents AcquireSceneComponents()
        {
            return new SceneComponents
            {
                Actor = UnityEngine.Object.FindAnyObjectByType<ActorObject>(),
                Display = UnityEngine.Object.FindAnyObjectByType<DisplayObject>(),
                Task = UnityEngine.Object.FindAnyObjectByType<Task>(),
                Client = UnityEngine.Object.FindAnyObjectByType<MQTTClient>(),
                Controllers = UnityEngine.Object.FindObjectsByType<ControllerOutput>(FindObjectsSortMode.None),
                DisplayCameras = GetDisplayCameras(),
                HasInteractionZone = UnityEngine.Object.FindAnyObjectByType<GuidanceZone>() != null,
                HasOccupancyZone = UnityEngine.Object.FindAnyObjectByType<OccupancyZone>() != null,
                FullScreenManager = AcquireFullScreenManager(),
            };
        }

        /// <summary>Returns every assignable display camera in the active scene (Main Camera excluded).</summary>
        /// <remarks>
        /// Mirrors the filter applied by <see cref="FullScreenViewManager"/>'s dropdown so the agent surface
        /// and the GUI agree on which cameras can be bound to monitors.
        /// </remarks>
        private static Camera[] GetDisplayCameras()
        {
            return UnityEngine
                .Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
                .Where(camera =>
                    !camera.CompareTag("MainCamera")
                    && !string.Equals(camera.gameObject.name, "Main Camera", StringComparison.Ordinal)
                )
                .ToArray();
        }

        /// <summary>Returns every valid actor model name (every Resources/Actors/Prefabs entry plus "None").</summary>
        private static string[] GetValidActorModels()
        {
            return Resources
                .LoadAll<GameObject>("Actors/Prefabs")
                .Select(prefab => prefab.name)
                .Append("None")
                .ToArray();
        }

        /// <summary>
        /// Builds the nested state/options/visibility dictionary that <see cref="ReadTaskParameters"/> returns.
        /// </summary>
        /// <remarks>
        /// Takes a pre-acquired <see cref="SceneComponents"/> rather than re-walking the scene so the
        /// post-write response from <see cref="WriteTaskParameters"/> reuses the same component references
        /// it already validated against, avoiding a third scene scan per request.
        /// </remarks>
        /// <param name="components">The pre-acquired scene component snapshot.</param>
        /// <returns>The response payload ready for <see cref="Ok"/>.</returns>
        private static Dictionary<string, object> BuildSnapshot(SceneComponents components)
        {
            Dictionary<string, object> actorState = null;
            if (components.Actor != null)
            {
                string currentModel = "None";
                foreach (Transform child in components.Actor.transform)
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
                    {
                        "controller",
                        components.Actor.Controller == null ? "None" : components.Actor.Controller.gameObject.name
                    },
                };
            }

            Dictionary<string, object> mqttState =
                components.Client == null
                    ? null
                    : new Dictionary<string, object>
                    {
                        { "ip", components.Client.ipAddress },
                        { "port", components.Client.port },
                    };

            Dictionary<string, object> displayState =
                components.Display == null
                    ? null
                    : new Dictionary<string, object>
                    {
                        { "current_brightness", components.Display.currentBrightness },
                        {
                            "brightness",
                            components.Display.settings != null ? components.Display.settings.brightness : 100f
                        },
                        {
                            "height_in_vr",
                            components.Display.settings != null ? components.Display.settings.heightInVR : 0f
                        },
                    };

            List<Dictionary<string, object>> cameraMappingState = new List<Dictionary<string, object>>();
            for (int monitorIndex = 0; monitorIndex < components.FullScreenManager.monitors.Count; monitorIndex++)
            {
                Monitor monitor = components.FullScreenManager.monitors[monitorIndex];
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
                components.Task == null
                    ? null
                    : new Dictionary<string, object>
                    {
                        { "require_interaction", components.Task.requireInteraction },
                        { "require_wait", components.Task.requireWait },
                        { "track_length", components.Task.trackLength },
                        { "track_seed", components.Task.trackSeed },
                    };

            List<string> modelOptions = new List<string>(GetValidActorModels());

            List<string> controllerOptions = new List<string> { "None" };
            controllerOptions.AddRange(components.Controllers.Select(controller => controller.gameObject.name));

            List<string> cameraOptions = new List<string> { "None" };
            cameraOptions.AddRange(components.DisplayCameras.Select(camera => camera.name));

            return new Dictionary<string, object>
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
                                { "require_interaction", components.HasInteractionZone },
                                { "require_wait", components.HasOccupancyZone },
                            }
                        },
                    }
                },
            };
        }

        /// <summary>Applies any "actor" subsection from <paramref name="args"/>.</summary>
        /// <returns>An error message when the actor section is invalid, otherwise null.</returns>
        private static string TryWriteActorSection(
            Dictionary<string, object> args,
            SceneComponents components,
            ref bool dirty
        )
        {
            if (!TryGetSection(args, "actor", out Dictionary<string, object> actorArgs) || components.Actor == null)
            {
                return null;
            }
            if (actorArgs.TryGetValue("model", out object modelObject) && modelObject is string newModel)
            {
                string[] validModels = GetValidActorModels();
                if (!validModels.Contains(newModel))
                {
                    return $"Invalid model '{newModel}'. Valid: {string.Join(", ", validModels)}";
                }
                components.Actor.SetModel(newModel);
                dirty = true;
            }
            if (
                actorArgs.TryGetValue("controller", out object controllerObject)
                && controllerObject is string newController
            )
            {
                if (string.Equals(newController, "None", StringComparison.Ordinal))
                {
                    components.Actor.Controller = null;
                }
                else
                {
                    ControllerOutput target = components.Controllers.FirstOrDefault(controller =>
                        controller.gameObject.name == newController
                    );
                    if (target == null)
                    {
                        string controllerNames = string.Join(
                            ", ",
                            components.Controllers.Select(controller => controller.gameObject.name)
                        );
                        return $"Invalid controller '{newController}'. Valid: None, {controllerNames}";
                    }
                    components.Actor.Controller = target;
                }
                dirty = true;
            }
            return null;
        }

        /// <summary>Applies any "mqtt" subsection from <paramref name="args"/>.</summary>
        /// <returns>An error message when the mqtt section is invalid, otherwise null.</returns>
        private static string TryWriteMqttSection(
            Dictionary<string, object> args,
            SceneComponents components,
            ref bool dirty
        )
        {
            if (!TryGetSection(args, "mqtt", out Dictionary<string, object> mqttArgs) || components.Client == null)
            {
                return null;
            }
            if (mqttArgs.TryGetValue("ip", out object ipObject) && ipObject is string newIp)
            {
                components.Client.ipAddress = newIp;
                EditorPrefs.SetString("SollertiaVR_MQTT_IP", newIp);
                dirty = true;
            }
            if (mqttArgs.TryGetValue("port", out object portObject))
            {
                int newPort = Convert.ToInt32(portObject);
                components.Client.port = newPort;
                EditorPrefs.SetInt("SollertiaVR_MQTT_Port", newPort);
                dirty = true;
            }
            return null;
        }

        /// <summary>Applies any "display" subsection from <paramref name="args"/>.</summary>
        /// <returns>An error message when the display section is invalid, otherwise null.</returns>
        private static string TryWriteDisplaySection(
            Dictionary<string, object> args,
            SceneComponents components,
            ref bool dirty
        )
        {
            if (
                !TryGetSection(args, "display", out Dictionary<string, object> displayArgs)
                || components.Display == null
            )
            {
                return null;
            }
            if (displayArgs.TryGetValue("current_brightness", out object currentBrightnessObject))
            {
                components.Display.currentBrightness = Convert.ToSingle(currentBrightnessObject);
                dirty = true;
            }
            if (components.Display.settings != null)
            {
                if (displayArgs.TryGetValue("brightness", out object brightnessObject))
                {
                    components.Display.settings.brightness = Convert.ToSingle(brightnessObject);
                    EditorUtility.SetDirty(components.Display.settings);
                    dirty = true;
                }
                if (displayArgs.TryGetValue("height_in_vr", out object heightObject))
                {
                    components.Display.settings.heightInVR = Convert.ToSingle(heightObject);
                    components.Display.transform.localPosition = new Vector3(
                        0,
                        components.Display.settings.heightInVR,
                        0
                    );
                    EditorUtility.SetDirty(components.Display.settings);
                    dirty = true;
                }
            }
            return null;
        }

        /// <summary>Applies any "camera_mapping" subsection from <paramref name="args"/>.</summary>
        /// <returns>An error message when the camera_mapping section is invalid, otherwise null.</returns>
        private static string TryWriteCameraMappingSection(
            Dictionary<string, object> args,
            SceneComponents components,
            ref bool dirty
        )
        {
            if (
                !args.TryGetValue("camera_mapping", out object cameraMappingObject)
                || cameraMappingObject is not List<object> cameraMappingList
            )
            {
                return null;
            }

            FullScreenViewManager fullScreenManager = components.FullScreenManager;
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
                    return $"Invalid monitor index {monitorIndex + 1}; scene has "
                        + $"{fullScreenManager.monitors.Count} monitors.";
                }
                if (!rowDict.TryGetValue("camera", out object cameraObject) || cameraObject is not string cameraName)
                {
                    continue;
                }
                if (string.Equals(cameraName, "None", StringComparison.Ordinal))
                {
                    fullScreenManager.monitors[monitorIndex].cameraEntityId = EntityId.None;
                }
                else
                {
                    Camera target = components.DisplayCameras.FirstOrDefault(camera => camera.name == cameraName);
                    if (target == null)
                    {
                        return $"Invalid camera '{cameraName}' for monitor {monitorIndex + 1}. Valid: None, "
                            + string.Join(", ", components.DisplayCameras.Select(camera => camera.name));
                    }
                    fullScreenManager.monitors[monitorIndex].cameraEntityId = target.GetEntityId();
                }
            }
            fullScreenManager.SaveCameras();
            dirty = true;
            return null;
        }

        /// <summary>Applies any "task" subsection from <paramref name="args"/>.</summary>
        /// <returns>An error message when the task section is invalid, otherwise null.</returns>
        private static string TryWriteTaskSection(
            Dictionary<string, object> args,
            SceneComponents components,
            ref bool dirty
        )
        {
            if (!TryGetSection(args, "task", out Dictionary<string, object> taskArgs) || components.Task == null)
            {
                return null;
            }
            if (taskArgs.ContainsKey("require_interaction") && !components.HasInteractionZone)
            {
                return "Cannot set require_interaction: the active scene has no GuidanceZone, so the control is "
                    + "hidden in the Parameters window and the flag has no runtime effect.";
            }
            if (taskArgs.ContainsKey("require_wait") && !components.HasOccupancyZone)
            {
                return "Cannot set require_wait: the active scene has no OccupancyZone, so the control is "
                    + "hidden in the Parameters window and the flag has no runtime effect.";
            }

            Undo.RecordObject(components.Task, "Write Task Parameters");
            if (taskArgs.TryGetValue("require_interaction", out object requireInteractionObject))
            {
                components.Task.requireInteraction = Convert.ToBoolean(requireInteractionObject);
                dirty = true;
            }
            if (taskArgs.TryGetValue("require_wait", out object requireWaitObject))
            {
                components.Task.requireWait = Convert.ToBoolean(requireWaitObject);
                dirty = true;
            }
            if (taskArgs.TryGetValue("track_length", out object trackLengthObject))
            {
                components.Task.trackLength = Convert.ToSingle(trackLengthObject);
                dirty = true;
            }
            if (taskArgs.TryGetValue("track_seed", out object trackSeedObject))
            {
                components.Task.trackSeed = Convert.ToInt32(trackSeedObject);
                dirty = true;
            }
            EditorUtility.SetDirty(components.Task);
            return null;
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

            Component[] components = gameObject.GetComponents<Component>();
            List<string> componentNames = components
                .Where(component => component != null)
                .Select(component => component.GetType().Name)
                .ToList();
            result["components"] = componentNames;

            BoxCollider collider = gameObject.GetComponent<BoxCollider>();
            if (collider != null)
            {
                result["collider_center"] = FormatVector3(collider.center);
                result["collider_size"] = FormatVector3(collider.size);
                result["collider_is_trigger"] = collider.isTrigger;
            }

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
