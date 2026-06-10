/// <summary>
/// Provides the ConfigLoader class for loading and validating task templates from YAML files.
/// </summary>
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SL.Config
{
    /// <summary>
    /// Loads and validates task templates from YAML files.
    /// </summary>
    public static class ConfigLoader
    {
        /// <summary>The tolerance for validating that trial transition probabilities sum to 1.0.</summary>
        private const float ProbabilitySumTolerance = 0.001f;

        /// <summary>
        /// Matches trial names that are safe to embed in generated segment prefab filenames. Restricts trial names
        /// to ASCII letters, digits, and underscores so the ``TaskName_TrialName`` segment naming scheme cannot be
        /// corrupted by path separators, whitespace, or punctuation introduced in a template.
        /// </summary>
        private static readonly Regex TrialNamePattern = new Regex("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

        /// <summary>Loads a TaskTemplate from a YAML file and derives the template name from the filename.</summary>
        /// <param name="filePath">The absolute path to the YAML template file.</param>
        /// <returns>The parsed template with templateName populated.</returns>
        /// <exception cref="FileNotFoundException">
        /// The template file at <paramref name="filePath"/> does not exist.
        /// </exception>
        /// <exception cref="FormatException">
        /// The YAML file cannot be deserialized into a <see cref="TaskTemplate"/>.
        /// </exception>
        /// <exception cref="InvalidDataException">The parsed template fails validation.</exception>
        public static TaskTemplate LoadTemplate(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Template file not found: {filePath}", filePath);
            }

            IDeserializer deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            string yaml = File.ReadAllText(filePath);
            TaskTemplate template = deserializer.Deserialize<TaskTemplate>(yaml);

            ValidateTemplate(template, filePath);

            // Derives template name from filename (without extension)
            template.templateName = Path.GetFileNameWithoutExtension(filePath);

            return template;
        }

        /// <summary>Validates the loaded template for required fields and data integrity.</summary>
        /// <param name="template">The template to validate.</param>
        /// <param name="filePath">The absolute path to the template file, used for resolving asset paths.</param>
        /// <exception cref="FormatException">The template is null or could not be parsed.</exception>
        /// <exception cref="InvalidDataException">The template fails one of the validation checks.</exception>
        private static void ValidateTemplate(TaskTemplate template, string filePath)
        {
            if (template == null)
            {
                throw new FormatException("Failed to parse template file.");
            }

            if (template.cues == null || template.cues.Count == 0)
            {
                throw new InvalidDataException("No cues defined in template.");
            }

            if (template.vrEnvironment == null)
            {
                throw new InvalidDataException("No VR environment configuration defined.");
            }

            if (template.trialStructures == null || template.trialStructures.Count == 0)
            {
                throw new InvalidDataException("No trial structures defined in template.");
            }

            // Validates cue codes are unique and within uint8 range
            HashSet<int> seenCodes = new HashSet<int>();
            HashSet<string> seenNames = new HashSet<string>();

            foreach (Cue cue in template.cues)
            {
                if (cue.code < 0 || cue.code > 255)
                {
                    throw new InvalidDataException($"Cue '{cue.name}' has invalid code {cue.code}. Must be 0-255.");
                }

                if (!seenCodes.Add(cue.code))
                {
                    throw new InvalidDataException($"Duplicate cue code {cue.code} found.");
                }

                if (!seenNames.Add(cue.name))
                {
                    throw new InvalidDataException($"Duplicate cue name '{cue.name}' found.");
                }

                if (cue.lengthCm <= 0)
                {
                    throw new InvalidDataException(
                        $"Cue '{cue.name}' has invalid length {cue.lengthCm}. Must be positive."
                    );
                }

                if (string.IsNullOrEmpty(cue.texture))
                {
                    throw new InvalidDataException($"Cue '{cue.name}' is missing required 'texture' field.");
                }

                string texturesDirectory = Path.Combine(Path.GetDirectoryName(filePath), "..", "Textures");
                string texturePath = Path.GetFullPath(Path.Combine(texturesDirectory, cue.texture));
                if (!File.Exists(texturePath))
                {
                    throw new InvalidDataException(
                        $"Cue '{cue.name}' references texture '{cue.texture}' but no file found at {texturePath}."
                    );
                }
            }

            // Validates trial structures reference valid cues, valid trigger types, and well-formed transitions.
            foreach (KeyValuePair<string, TrialStructure> trialEntry in template.trialStructures)
            {
                string trialName = trialEntry.Key;
                TrialStructure trial = trialEntry.Value;

                // Trial names are concatenated into segment prefab filenames (``TaskName_TrialName.prefab``), so
                // operator-controlled punctuation, whitespace, or path separators would corrupt the generated
                // filesystem layout. Rejects them at load time before any asset path is computed downstream.
                if (!TrialNamePattern.IsMatch(trialName))
                {
                    throw new InvalidDataException(
                        $"Trial name '{trialName}' is invalid. Trial names must contain only ASCII letters, "
                            + "digits, and underscores (used in generated segment prefab filenames)."
                    );
                }

                if (trial.cueSequence == null || trial.cueSequence.Count == 0)
                {
                    throw new InvalidDataException($"Trial '{trialName}' has no cue sequence.");
                }

                foreach (string cueName in trial.cueSequence)
                {
                    if (!seenNames.Contains(cueName))
                    {
                        throw new InvalidDataException($"Trial '{trialName}' references unknown cue '{cueName}'.");
                    }
                }

                if (string.IsNullOrEmpty(trial.triggerType))
                {
                    throw new InvalidDataException($"Trial '{trialName}' is missing required 'trigger_type' field.");
                }

                if (
                    !string.Equals(trial.triggerType, "interaction", StringComparison.Ordinal)
                    && !string.Equals(trial.triggerType, "occupancy_disarm", StringComparison.Ordinal)
                )
                {
                    throw new InvalidDataException(
                        $"Trial '{trialName}' has invalid trigger_type '{trial.triggerType}'. "
                            + "Must be 'interaction' or 'occupancy_disarm'."
                    );
                }

                if (trial.occupancyDurationMs <= 0f)
                {
                    string message =
                        $"Trial '{trialName}' has invalid occupancy_duration_ms {trial.occupancyDurationMs}. "
                        + "Must be positive.";
                    throw new InvalidDataException(message);
                }
            }

            // Validates that no two trials share an identical cue sequence. Identical cue sequences are
            // indistinguishable to the experiment's cue-stream decomposer, which would silently merge them.
            Dictionary<string, string> seenSequences = new Dictionary<string, string>();
            foreach (KeyValuePair<string, TrialStructure> trialEntry in template.trialStructures)
            {
                string trialName = trialEntry.Key;
                string signature = string.Join(" ", trialEntry.Value.cueSequence);
                if (seenSequences.TryGetValue(signature, out string existingTrialName))
                {
                    string message =
                        $"Trials '{existingTrialName}' and '{trialName}' share an identical cue sequence. "
                        + "Each trial must have a unique cue sequence so the experiment can identify it; use "
                        + "distinct cue codes (textures may be shared) to multiplex visually identical cues.";
                    throw new InvalidDataException(message);
                }
                seenSequences[signature] = trialName;
            }

            // Validates transitions reference defined trial names and sum to 1.0 when provided.
            foreach (KeyValuePair<string, TrialStructure> trialEntry in template.trialStructures)
            {
                string trialName = trialEntry.Key;
                TrialStructure trial = trialEntry.Value;

                if (!trial.HasTransitions)
                {
                    continue;
                }

                float probabilitySum = 0f;
                foreach (KeyValuePair<string, float> transition in trial.transitions)
                {
                    if (!template.trialStructures.ContainsKey(transition.Key))
                    {
                        throw new InvalidDataException(
                            $"Trial '{trialName}' has a transition to unknown trial '{transition.Key}'."
                        );
                    }
                    probabilitySum += transition.Value;
                }

                if (Mathf.Abs(probabilitySum - 1.0f) > ProbabilitySumTolerance)
                {
                    throw new InvalidDataException(
                        $"Trial '{trialName}' transition probabilities sum to {probabilitySum}, must be 1.0."
                    );
                }
            }
        }
    }
}
