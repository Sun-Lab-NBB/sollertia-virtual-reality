/// <summary>
/// Provides the CreateTask class that generates Task prefabs from YAML configuration files via Unity Editor menu.
/// </summary>
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SL.Config;
using UnityEditor;
using UnityEngine;

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

        /// <summary>
        /// Formats a cue's centimeter length as the suffix used in cue prefab and material filenames.
        /// Returns a culture-invariant string with up to two decimals and no trailing zeros (e.g., "30", "37.5").
        /// </summary>
        /// <param name="lengthCm">The cue length in centimeters.</param>
        /// <returns>The length label used inside ``Cue_{name}_{label}cm`` asset filenames.</returns>
        public static string FormatCueLengthLabel(float lengthCm) =>
            lengthCm.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>
        /// Computes the base name a template author writes in a segment's ``name`` field, derived purely from
        /// the cue sequence. Returns the lowercase concatenation of cue letters in sequence order, with any
        /// cue named ``Gray`` excluded (treated as a structural separator). This is the human-facing identifier
        /// that ``trial_structures.*.segment_name`` references and matches the cue-side convention where each
        /// cue carries a simple ``name`` (e.g., ``"A"``) and only the full prefab name encodes geometry.
        /// </summary>
        /// <param name="segment">The segment whose base name is computed.</param>
        /// <returns>The base segment name as it should appear in the template YAML.</returns>
        public static string BaseSegmentName(Segment segment) =>
            string.Concat(
                segment
                    .cueSequence.Where(name => !string.Equals(name, "Gray", StringComparison.Ordinal))
                    .Select(name => name.ToLowerInvariant())
            );

        /// <summary>
        /// Computes the canonical prefab name for a segment from its base name, total cue length, and (when
        /// the segment has an associated trial structure) the trigger zone configuration baked into the prefab.
        /// Pattern: ``Segment_<base-name>[_g]_<total-length>cm[_<r|o><zone-center>cm]``. The ``_g`` marker
        /// appears when any Gray cue interleaves the sequence so layouts with and without Gray separators are
        /// visually distinct. The total length sums every cue in the sequence — Gray separators included — so
        /// asymmetric cue layouts still yield distinct identifiers. When a trial structure references the
        /// segment, a zone marker is appended: ``r`` for lick (reward) zones, ``o`` for occupancy (aversion)
        /// zones, followed by the zone center in centimeters. Templates write only the base name; the canonical
        /// prefab name is computed at generation time, mirroring the cue convention (``Cue_A_30cm.prefab``
        /// generated from a cue with ``name: "A"`` and ``length_cm: 30``).
        /// </summary>
        /// <param name="segment">The segment whose canonical name is computed.</param>
        /// <param name="template">The task template owning the segment; consulted for cue lengths and any
        /// trial structure referencing the segment.</param>
        /// <returns>The canonical segment prefab name (without the ``.prefab`` extension).</returns>
        public static string CanonicalSegmentName(Segment segment, TaskTemplate template)
        {
            Dictionary<string, Cue> cueMap = template.GetCueByName();
            bool hasGray = segment.cueSequence.Any(name => string.Equals(name, "Gray", StringComparison.Ordinal));
            string letters = BaseSegmentName(segment);
            float totalLengthCm = segment.cueSequence.Sum(name => cueMap[name].lengthCm);
            string graySuffix = hasGray ? "_g" : string.Empty;
            string geometryName = $"Segment_{letters}{graySuffix}_{FormatCueLengthLabel(totalLengthCm)}cm";

            TrialStructure trial = template.GetTrialStructureForSegment(segment.name);
            if (trial == null)
            {
                return geometryName;
            }

            string typeMarker = string.Equals(trial.triggerType, "occupancy", StringComparison.Ordinal) ? "o" : "r";
            float zoneCenterCm = (trial.stimulusTriggerZoneStartCm + trial.stimulusTriggerZoneEndCm) / 2f;
            return $"{geometryName}_{typeMarker}{FormatCueLengthLabel(zoneCenterCm)}cm";
        }

        /// <summary>
        /// Verifies every segment in the template uses the base-name convention: ``segment.name`` must equal
        /// the lowercase concatenation of non-Gray cue letters returned by <see cref="BaseSegmentName"/>.
        /// Logs an error for each mismatch and returns false when any segment fails the check. Runs before any
        /// prefab is built so callers can fail fast on a misnamed template.
        /// </summary>
        /// <param name="template">The loaded task template.</param>
        /// <returns>True when every segment name matches its derived base name, false otherwise.</returns>
        private static bool ValidateSegmentNaming(TaskTemplate template)
        {
            bool valid = true;
            foreach (Segment segment in template.segments)
            {
                string expected = BaseSegmentName(segment);
                if (!string.Equals(segment.name, expected, StringComparison.Ordinal))
                {
                    Debug.LogError(
                        $"ValidateSegmentNaming: Segment '{segment.name}' does not match its base name '{expected}'. "
                            + "The segment name should be the lowercase concatenation of non-Gray cue letters from "
                            + "cue_sequence. Update the template (and any trial_structures.*.segment_name references) "
                            + $"to use '{expected}'."
                    );
                    valid = false;
                }
            }
            return valid;
        }

        /// <summary>Creates a new Task prefab from a selected YAML configuration file via the Editor menu.</summary>
        [MenuItem("CreateTask/New Task")]
        public static void CreateNewTask()
        {
            // Opens file dialog for YAML task template file
            string configurationsDirectory = Path.Combine(
                Application.dataPath,
                "InfiniteCorridorTask",
                "Configurations"
            );
            string configPath = EditorUtility
                .OpenFilePanel("Select Task Template YAML", configurationsDirectory, "yaml,yml")
                .Replace(Application.dataPath, "", StringComparison.Ordinal)
                .TrimStart('/', '\\');

            if (string.IsNullOrEmpty(configPath))
            {
                Debug.LogError("No configuration YAML file selected.");
                return;
            }

            // Opens save file panel for user to specify location and name of prefab
            string tasksDirectory = Path.Combine(Application.dataPath, "InfiniteCorridorTask", "Tasks");
            string savePath = EditorUtility.SaveFilePanel(
                "Save Task Prefab",
                tasksDirectory,
                "newTask.prefab",
                "prefab"
            );

            if (string.IsNullOrEmpty(savePath))
            {
                Debug.LogError("User did not select a save location.");
                return;
            }

            savePath = FileUtil.GetProjectRelativePath(savePath);
            string absoluteTemplatePath = Path.Combine(Application.dataPath, configPath);
            string result = CreateFromTemplate(absoluteTemplatePath, configPath, savePath);
            Debug.Log(result);
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
            // Loads and validates task template
            TaskTemplate template;
            try
            {
                template = ConfigLoader.LoadTemplate(absoluteTemplatePath);
            }
            catch (Exception exception)
            {
                return $"error: {exception.Message}";
            }

            // Rejects templates that do not use canonical Segment_<letters>_<length>cm naming before any
            // prefab work begins so the caller sees the expected names without partially generated assets.
            if (!ValidateSegmentNaming(template))
            {
                return "error: One or more segments do not use the canonical Segment_<letters>_<length>cm naming. "
                    + "See console for the expected names.";
            }

            // Builds cue and segment prefabs from template data when they do not already exist
            if (!BuildCuePrefabs(template))
            {
                return "error: Failed to build cue prefabs.";
            }

            if (!BuildSegmentPrefabs(template))
            {
                return "error: Failed to build segment prefabs.";
            }

            string prefabsPath = "Assets/InfiniteCorridorTask/Prefabs/";

            // Loads padding prefab
            string paddingPath = Path.Combine(prefabsPath, $"{template.vrEnvironment.paddingPrefabName}.prefab");
            GameObject padding = AssetDatabase.LoadAssetAtPath<GameObject>(paddingPath);

            if (padding == null)
            {
                return $"error: No padding found at {paddingPath}";
            }

            int segmentCount = template.segments.Count;

            // Loads segment prefabs by their canonical (geometry-and-zone-encoded) prefab name.
            GameObject[] segmentPrefabs = new GameObject[segmentCount];
            for (int i = 0; i < segmentCount; i++)
            {
                string canonicalName = CanonicalSegmentName(template.segments[i], template);
                string segmentPath = Path.Combine(prefabsPath, $"{canonicalName}.prefab");
                segmentPrefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>(segmentPath);

                if (segmentPrefabs[i] == null)
                {
                    return $"error: No segment found at {segmentPath}";
                }
            }

            // Measures actual prefab lengths and compares with configuration
            float[] measuredSegmentLengths = Utility.GetSegmentLengths(segmentPrefabs);
            float[] segmentLengths = template.GetSegmentLengthsUnity();

            for (int i = 0; i < segmentCount; i++)
            {
                if (Mathf.Abs(measuredSegmentLengths[i] - segmentLengths[i]) > LengthComparisonEpsilon)
                {
                    Debug.LogWarning(
                        $"For {template.segments[i].name}, there is a mismatch between the prefab "
                            + $"length ({measuredSegmentLengths[i]}) and the sum of all the cue "
                            + $"lengths ({segmentLengths[i]}). Using {segmentLengths[i]} for the "
                            + "length of the segment."
                    );
                }
            }

            int depth = template.vrEnvironment.segmentsPerCorridor;
            float paddingZShift = depth * Mathf.Min(segmentLengths) - 1;

            // Creates task GameObject hierarchy
            string taskName = Path.GetFileNameWithoutExtension(savePath);
            GameObject task = new GameObject(taskName);
            Task taskScript = task.AddComponent<Task>();
            taskScript.requireLick = true;
            taskScript.configPath = relativeConfigPath;

            int[] corridorSegments = new int[depth];
            int segment;
            float currentCorridorX = 0;
            float corridorXShift = template.vrEnvironment.CorridorSpacingUnity;
            float zShift;

            // Iterates through all possible corridor combinations
            for (int i = 0; i < Mathf.Pow(segmentCount, depth); i++)
            {
                // Generates the combination for the current index
                for (int j = 0; j < depth; j++)
                {
                    corridorSegments[j] = i / (int)Mathf.Pow(segmentCount, depth - j - 1) % segmentCount;
                }

                GameObject corridor = new GameObject($"Corridor{string.Join("", corridorSegments)}");
                corridor.transform.SetParent(task.transform);
                corridor.transform.localPosition = new Vector3(currentCorridorX, 0, 0);

                zShift = 0;
                for (int j = 0; j < depth; j++)
                {
                    segment = corridorSegments[j];
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
                        // For the first segment, sets showBoundary from config's trial visibility
                        StimulusTriggerZone stimulusTriggerZone =
                            instance.GetComponentInChildren<StimulusTriggerZone>();
                        if (stimulusTriggerZone != null)
                        {
                            string segmentName = template.segments[segment].name;
                            stimulusTriggerZone.showBoundary = template.GetSegmentMarkerVisibility(segmentName);
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

            PrefabUtility.SaveAsPrefabAsset(task, savePath);
            UnityEngine.Object.DestroyImmediate(task);

            return $"success: Task prefab saved to {savePath}";
        }

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
        private const string CueShaderReferenceMaterialPath =
            "Assets/InfiniteCorridorTask/Materials/_CueShaderReference.mat";

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
            Debug.LogWarning(
                $"BuildCuePrefabs: canonical shader reference '{CueShaderReferenceMaterialPath}' is missing; "
                    + "falling back to a hand-authored Cue*.mat material or Shader.Find. Restore the "
                    + "reference material to guarantee consistent cue rendering across machines."
            );

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
        /// Creates cue prefabs for cues that do not yet have a prefab in the Cues directory.
        /// Each cue prefab contains Left and Right Quad children with the cue material applied.
        /// </summary>
        /// <param name="template">The loaded task template.</param>
        /// <returns>True if all cue prefabs were built or already exist, false on error.</returns>
        private static bool BuildCuePrefabs(TaskTemplate template)
        {
            string cuesPath = "Assets/InfiniteCorridorTask/Cues/";
            string materialsPath = "Assets/InfiniteCorridorTask/Materials/";
            string texturesPath = "Assets/InfiniteCorridorTask/Textures/";
            float cmPerUnit = template.vrEnvironment.cmPerUnityUnit;

            // Inherits the shader from the project's historical hand-authored cue materials so generated
            // materials render identically to the originals. The reference material lives in the same
            // Materials/ folder and uses the legacy diffuse shader that handles the Right wall's negative
            // X scale correctly (the modern Standard and Unlit shaders both fail at this — Standard
            // breaks lit normals under inverted geometry, Unlit drops lighting entirely).
            Shader cueShader = LoadReferenceCueShader(materialsPath);

            // Ensures the Cues directory exists
            if (!AssetDatabase.IsValidFolder("Assets/InfiniteCorridorTask/Cues"))
            {
                AssetDatabase.CreateFolder("Assets/InfiniteCorridorTask", "Cues");
            }

            Mesh quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

            foreach (Cue cue in template.cues)
            {
                // Encodes the cue length in the asset filenames so cues that share a letter across templates
                // (e.g., A at 30 cm in MF vs A at 40 cm in SSO) resolve to distinct prefabs and materials.
                string lengthLabel = FormatCueLengthLabel(cue.lengthCm);
                string cueAssetStem = $"Cue_{cue.name}_{lengthLabel}cm";
                string cuePrefabPath = Path.Combine(cuesPath, $"{cueAssetStem}.prefab");

                if (AssetDatabase.LoadAssetAtPath<GameObject>(cuePrefabPath) != null)
                {
                    continue;
                }

                float lengthUnity = cue.LengthUnity(cmPerUnit);

                // Loads the shared texture once for both material variants.
                Texture2D cueTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    Path.Combine(texturesPath, cue.texture)
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
                string materialPath = Path.Combine(materialsPath, $"{cueAssetStem}.mat");
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
                right.transform.localPosition = new Vector3(0.49f, 0.5f, lengthUnity / 2f);
                right.transform.localRotation = Quaternion.Euler(0, 90, 0);
                right.transform.localScale = new Vector3(-lengthUnity, 1, 1);
                right.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                right.AddComponent<MeshRenderer>().sharedMaterial = cueMaterial;

                GameObject left = new GameObject("Left");
                left.transform.SetParent(cueGameObject.transform);
                left.transform.localPosition = new Vector3(-0.49f, 0.5f, lengthUnity / 2f);
                left.transform.localRotation = Quaternion.Euler(0, -90, 0);
                left.transform.localScale = new Vector3(lengthUnity, 1, 1);
                left.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                left.AddComponent<MeshRenderer>().sharedMaterial = cueMaterial;

                PrefabUtility.SaveAsPrefabAsset(cueGameObject, cuePrefabPath);
                UnityEngine.Object.DestroyImmediate(cueGameObject);

                Debug.Log($"BuildCuePrefabs: Created {cuePrefabPath}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return true;
        }

        /// <summary>
        /// Creates segment prefabs for segments that do not yet have a prefab in the Prefabs directory.
        /// Each segment prefab contains cue instances, floor, walls, and trigger/reset zones.
        /// </summary>
        /// <param name="template">The loaded task template.</param>
        /// <returns>True if all segment prefabs were built or already exist, false on error.</returns>
        private static bool BuildSegmentPrefabs(TaskTemplate template)
        {
            string prefabsPath = "Assets/InfiniteCorridorTask/Prefabs/";
            string cuesPath = "Assets/InfiniteCorridorTask/Cues/";
            string materialsPath = "Assets/InfiniteCorridorTask/Materials/";
            float cmPerUnit = template.vrEnvironment.cmPerUnityUnit;
            Dictionary<string, Cue> cueMap = template.GetCueByName();

            Mesh quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            Mesh planeMesh = Resources.GetBuiltinResource<Mesh>("New-Plane.fbx");

            // Loads shared materials
            Material floorMaterial = AssetDatabase.LoadAssetAtPath<Material>(Path.Combine(materialsPath, "Floor.mat"));
            Material wallMaterial = AssetDatabase.LoadAssetAtPath<Material>(Path.Combine(materialsPath, "Wall.mat"));

            if (floorMaterial == null || wallMaterial == null)
            {
                Debug.LogError("BuildSegmentPrefabs: Missing Floor.mat or Wall.mat.");
                return false;
            }

            // Loads zone template prefabs
            GameObject stimulusZonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                Path.Combine(prefabsPath, "StimulusTriggerZone.prefab")
            );
            GameObject occupancyZonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                Path.Combine(prefabsPath, "OccupancyTriggerZone.prefab")
            );
            GameObject resetZonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                Path.Combine(prefabsPath, "ResetZone.prefab")
            );

            foreach (Segment segment in template.segments)
            {
                string canonicalSegmentName = CanonicalSegmentName(segment, template);
                string segmentPrefabPath = Path.Combine(prefabsPath, $"{canonicalSegmentName}.prefab");

                if (AssetDatabase.LoadAssetAtPath<GameObject>(segmentPrefabPath) != null)
                {
                    continue;
                }

                // Calculates total segment length in Unity units
                float totalLengthUnity = segment.cueSequence.Sum(cueName => cueMap[cueName].LengthUnity(cmPerUnit));
                float cueOffsetUnity = template.cueOffsetCm / cmPerUnit;

                // Creates segment root with cue offset; the root takes the canonical prefab name so the
                // in-prefab m_Name matches the filename, matching the cue-side convention.
                GameObject segmentGameObject = new GameObject(canonicalSegmentName);
                segmentGameObject.transform.localPosition = new Vector3(0, 0, -cueOffsetUnity);

                // Places cue instances along the Z axis
                float cumulativeZ = 0f;
                foreach (string cueName in segment.cueSequence)
                {
                    Cue cue = cueMap[cueName];
                    float cueLengthUnity = cue.LengthUnity(cmPerUnit);

                    string lengthLabel = FormatCueLengthLabel(cue.lengthCm);
                    string cuePrefabPath = Path.Combine(cuesPath, $"Cue_{cueName}_{lengthLabel}cm.prefab");
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

                // Creates Floor
                GameObject floor = new GameObject("Floor");
                floor.transform.SetParent(segmentGameObject.transform);
                floor.transform.localPosition = new Vector3(0, 0, totalLengthUnity / 2f);
                floor.transform.localScale = new Vector3(0.1f, 1, totalLengthUnity / 10f);
                floor.AddComponent<MeshFilter>().sharedMesh = planeMesh;
                floor.AddComponent<MeshRenderer>().sharedMaterial = floorMaterial;

                // Creates Walls group with LeftWall and RightWall
                GameObject walls = new GameObject("Walls");
                walls.transform.SetParent(segmentGameObject.transform);
                walls.transform.localPosition = Vector3.zero;

                GameObject leftWall = new GameObject("LeftWall");
                leftWall.transform.SetParent(walls.transform);
                leftWall.transform.localPosition = new Vector3(-0.5f, 0.5f, totalLengthUnity / 2f);
                leftWall.transform.localRotation = Quaternion.Euler(0, -90, 0);
                leftWall.transform.localScale = new Vector3(totalLengthUnity, 1, 1);
                leftWall.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                leftWall.AddComponent<MeshRenderer>().sharedMaterial = wallMaterial;

                GameObject rightWall = new GameObject("RightWall");
                rightWall.transform.SetParent(walls.transform);
                rightWall.transform.localPosition = new Vector3(0.5f, 0.5f, totalLengthUnity / 2f);
                rightWall.transform.localRotation = Quaternion.Euler(0, 90, 0);
                rightWall.transform.localScale = new Vector3(totalLengthUnity, 1, 1);
                rightWall.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                rightWall.AddComponent<MeshRenderer>().sharedMaterial = wallMaterial;

                // Places zones from trial structure
                TrialStructure trial = template.GetTrialStructureForSegment(segment.name);

                if (trial != null)
                {
                    float zoneStartUnity = trial.stimulusTriggerZoneStartCm / cmPerUnit;
                    float zoneEndUnity = trial.stimulusTriggerZoneEndCm / cmPerUnit;
                    float zoneCenterUnity = (zoneStartUnity + zoneEndUnity) / 2f;
                    float zoneSizeUnity = zoneEndUnity - zoneStartUnity;
                    float stimulusLocationUnity = trial.stimulusLocationCm / cmPerUnit;

                    if (
                        string.Equals(trial.triggerType, "lick", StringComparison.Ordinal)
                        && stimulusZonePrefab != null
                    )
                    {
                        PlaceLickZone(
                            parent: segmentGameObject,
                            zonePrefab: stimulusZonePrefab,
                            zoneCenterUnity: zoneCenterUnity,
                            zoneSizeUnity: zoneSizeUnity,
                            stimulusLocationUnity: stimulusLocationUnity,
                            showBoundary: trial.showStimulusCollisionBoundary
                        );
                    }
                    else if (
                        string.Equals(trial.triggerType, "occupancy", StringComparison.Ordinal)
                        && occupancyZonePrefab != null
                    )
                    {
                        PlaceOccupancyZone(
                            parent: segmentGameObject,
                            zonePrefab: occupancyZonePrefab,
                            zoneCenterUnity: zoneCenterUnity,
                            zoneSizeUnity: zoneSizeUnity,
                            stimulusLocationUnity: stimulusLocationUnity,
                            showBoundary: trial.showStimulusCollisionBoundary
                        );
                    }

                    // Places ResetZone at segment start
                    if (resetZonePrefab != null)
                    {
                        GameObject resetZone = PrefabUtility.InstantiatePrefab(resetZonePrefab) as GameObject;
                        resetZone.transform.SetParent(segmentGameObject.transform);
                        resetZone.transform.localPosition = new Vector3(0, 0.5f, 1);
                    }
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
        /// Instantiates and configures a StimulusTriggerZone (lick mode) within a segment.
        /// Positions the root collider to span the trigger zone and the GuidanceRegion at the stimulus location.
        /// </summary>
        /// <param name="parent">The parent segment GameObject.</param>
        /// <param name="zonePrefab">The StimulusTriggerZone prefab to instantiate.</param>
        /// <param name="zoneCenterUnity">The center position of the trigger zone in Unity units.</param>
        /// <param name="zoneSizeUnity">The size of the trigger zone in Unity units.</param>
        /// <param name="stimulusLocationUnity">The stimulus location in Unity units.</param>
        /// <param name="showBoundary">Determines whether the zone boundary is visible.</param>
        private static void PlaceLickZone(
            GameObject parent,
            GameObject zonePrefab,
            float zoneCenterUnity,
            float zoneSizeUnity,
            float stimulusLocationUnity,
            bool showBoundary
        )
        {
            GameObject zone = PrefabUtility.InstantiatePrefab(zonePrefab) as GameObject;
            zone.transform.SetParent(parent.transform);
            zone.transform.localPosition = new Vector3(0, 0.505f, zoneCenterUnity);

            // Configures root BoxCollider to span the trigger zone
            BoxCollider rootCollider = zone.GetComponent<BoxCollider>();
            if (rootCollider != null)
            {
                rootCollider.size = new Vector3(1, 1, zoneSizeUnity);
                rootCollider.center = Vector3.zero;
            }

            // Configures GuidanceRegion at the stimulus location
            GuidanceZone guidanceZone = zone.GetComponentInChildren<GuidanceZone>();
            if (guidanceZone != null)
            {
                BoxCollider guidanceCollider = guidanceZone.GetComponent<BoxCollider>();
                if (guidanceCollider != null)
                {
                    guidanceCollider.size = new Vector3(1, 1, 0.4f);
                    guidanceCollider.center = new Vector3(0, 0, stimulusLocationUnity - zoneCenterUnity);
                }
            }

            // Sets boundary visibility
            StimulusTriggerZone stimulusZone = zone.GetComponent<StimulusTriggerZone>();
            if (stimulusZone != null)
            {
                stimulusZone.showBoundary = showBoundary;
            }
        }

        /// <summary>
        /// Instantiates and configures an OccupancyTriggerZone within a segment.
        /// The root is positioned at the stimulus boundary (past the occupancy zone).
        /// The OccupancyRegion child covers the start-to-end range where the animal must wait.
        /// </summary>
        /// <param name="parent">The parent segment GameObject.</param>
        /// <param name="zonePrefab">The OccupancyTriggerZone prefab to instantiate.</param>
        /// <param name="zoneCenterUnity">The center position of the occupancy zone in Unity units.</param>
        /// <param name="zoneSizeUnity">The size of the occupancy zone in Unity units.</param>
        /// <param name="stimulusLocationUnity">The stimulus location in Unity units.</param>
        /// <param name="showBoundary">Determines whether the zone boundary is visible.</param>
        private static void PlaceOccupancyZone(
            GameObject parent,
            GameObject zonePrefab,
            float zoneCenterUnity,
            float zoneSizeUnity,
            float stimulusLocationUnity,
            bool showBoundary
        )
        {
            // Root position: stimulus boundary area, starting at stimulus_location and extending
            float rootZ = stimulusLocationUnity + zoneSizeUnity / 2f;

            GameObject zone = PrefabUtility.InstantiatePrefab(zonePrefab) as GameObject;
            zone.transform.SetParent(parent.transform);
            zone.transform.localPosition = new Vector3(0, 0.505f, rootZ);

            // Configures root BoxCollider (stimulus boundary trigger area)
            BoxCollider rootCollider = zone.GetComponent<BoxCollider>();
            if (rootCollider != null)
            {
                rootCollider.size = new Vector3(1, 1, zoneSizeUnity);
                rootCollider.center = Vector3.zero;
            }

            // Configures OccupancyRegion to cover the occupancy zone range
            float occupancyCenterOffset = zoneCenterUnity - rootZ;

            OccupancyZone occupancyZone = zone.GetComponentInChildren<OccupancyZone>();
            if (occupancyZone != null)
            {
                BoxCollider occupancyCollider = occupancyZone.GetComponent<BoxCollider>();
                if (occupancyCollider != null)
                {
                    occupancyCollider.size = new Vector3(1, 1, zoneSizeUnity);
                    occupancyCollider.center = new Vector3(0, 0, occupancyCenterOffset);
                }
            }

            // Configures OccupancyGuidanceRegion at the downstream end of the occupancy zone
            OccupancyGuidanceZone occupancyGuidanceZone = zone.GetComponentInChildren<OccupancyGuidanceZone>();
            if (occupancyGuidanceZone != null)
            {
                BoxCollider occupancyGuidanceCollider = occupancyGuidanceZone.GetComponent<BoxCollider>();
                if (occupancyGuidanceCollider != null)
                {
                    occupancyGuidanceCollider.size = new Vector3(1, 1, 0.4f);
                    occupancyGuidanceCollider.center = new Vector3(
                        0,
                        0,
                        occupancyCenterOffset + zoneSizeUnity / 2f - 0.2f
                    );
                }
            }

            // Sets boundary visibility
            StimulusTriggerZone stimulusZone = zone.GetComponent<StimulusTriggerZone>();
            if (stimulusZone != null)
            {
                stimulusZone.showBoundary = showBoundary;
            }
        }
    }
}
