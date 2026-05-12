/// <summary>
/// Provides the ConfigLoader class for loading and validating task templates from YAML files.
/// </summary>
using System;
using System.Collections.Generic;
using System.IO;
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
        /// <summary>Loads a TaskTemplate from a YAML file and derives the template name from the filename.</summary>
        /// <param name="filePath">The absolute path to the YAML template file.</param>
        /// <returns>The parsed template with templateName populated.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the template file does not exist at the given path.</exception>
        /// <exception cref="FormatException">Thrown when the YAML file cannot be parsed into a TaskTemplate.</exception>
        /// <exception cref="InvalidDataException">Thrown when the parsed template fails validation.</exception>
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
        /// <exception cref="FormatException">Thrown when the template is null or cannot be parsed.</exception>
        /// <exception cref="InvalidDataException">Thrown when the template fails any validation check.</exception>
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

            if (template.segments == null || template.segments.Count == 0)
            {
                throw new InvalidDataException("No segments defined in template.");
            }

            if (template.vrEnvironment == null)
            {
                throw new InvalidDataException("No VR environment configuration defined.");
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
                        $"Cue '{cue.name}' references texture '{cue.texture}' " + $"but no file found at {texturePath}."
                    );
                }
            }

            // Validates segment cue sequences reference valid cues
            HashSet<string> segmentNames = new HashSet<string>();
            foreach (Segment segment in template.segments)
            {
                segmentNames.Add(segment.name);

                if (segment.cueSequence == null || segment.cueSequence.Count == 0)
                {
                    throw new InvalidDataException($"Segment '{segment.name}' has no cue sequence.");
                }

                foreach (string cueName in segment.cueSequence)
                {
                    if (!seenNames.Contains(cueName))
                    {
                        throw new InvalidDataException($"Segment '{segment.name}' references unknown cue '{cueName}'.");
                    }
                }

                // Validates transition probabilities sum to 1.0 if provided
                if (segment.transitionProbabilities != null && segment.transitionProbabilities.Count > 0)
                {
                    float sum = 0f;
                    foreach (float probability in segment.transitionProbabilities)
                    {
                        sum += probability;
                    }

                    if (Mathf.Abs(sum - 1.0f) > 0.001f)
                    {
                        throw new InvalidDataException(
                            $"Segment '{segment.name}' transition probabilities sum to {sum}, must be 1.0."
                        );
                    }
                }
            }

            // Validates trial structures reference valid segments
            if (template.trialStructures != null)
            {
                foreach (KeyValuePair<string, TrialStructure> trialEntry in template.trialStructures)
                {
                    string trialName = trialEntry.Key;
                    TrialStructure trial = trialEntry.Value;

                    if (!segmentNames.Contains(trial.segmentName))
                    {
                        throw new InvalidDataException(
                            $"Trial '{trialName}' references unknown segment '{trial.segmentName}'."
                        );
                    }

                    if (string.IsNullOrEmpty(trial.triggerType))
                    {
                        throw new InvalidDataException(
                            $"Trial '{trialName}' is missing required 'trigger_type' field."
                        );
                    }

                    if (
                        !string.Equals(trial.triggerType, "lick", StringComparison.Ordinal)
                        && !string.Equals(trial.triggerType, "occupancy", StringComparison.Ordinal)
                    )
                    {
                        throw new InvalidDataException(
                            $"Trial '{trialName}' has invalid trigger_type '{trial.triggerType}'. "
                                + "Must be 'lick' or 'occupancy'."
                        );
                    }
                }
            }
        }
    }
}
