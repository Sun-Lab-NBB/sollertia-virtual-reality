/// <summary>
/// Provides utilities for programmatically managing Unity tags and layers.
///
/// Adapted from https://answers.unity.com/questions/33597/is-it-possible-to-create-a-tag-programmatically.html
/// </summary>
#if UNITY_EDITOR
using System;
using UnityEditor;

namespace Gimbl
{
    /// <summary>
    /// Manages Unity tags and layers through the TagManager asset.
    /// </summary>
    public static class TagsAndLayers
    {
        /// <summary>The maximum number of tags allowed.</summary>
        private const int MaxTags = 10000;

        /// <summary>The maximum number of layers allowed.</summary>
        private const int MaxLayers = 31;

        /// <summary>Adds a new tag to the project if it does not already exist.</summary>
        /// <param name="tagName">The name of the tag to add.</param>
        /// <returns>True if the tag was added, false if it already exists.</returns>
        /// <exception cref="InvalidOperationException">
        /// The project already holds the maximum number of tags.
        /// </exception>
        public static bool AddTag(string tagName)
        {
            SerializedObject tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]
            );
            SerializedProperty tagsProp = tagManager.FindProperty("tags");
            if (tagsProp.arraySize >= MaxTags)
            {
                throw new InvalidOperationException(
                    $"No more tags can be added to the Tags property. You have {tagsProp.arraySize} tags."
                );
            }
            if (!PropertyExists(tagsProp, start: 0, end: tagsProp.arraySize, value: tagName))
            {
                int index = tagsProp.arraySize;
                tagsProp.InsertArrayElementAtIndex(index);
                SerializedProperty newTag = tagsProp.GetArrayElementAtIndex(index);
                newTag.stringValue = tagName;
                tagManager.ApplyModifiedProperties();
                return true;
            }
            return false;
        }

        /// <summary>Adds a new layer to the project if it does not already exist.</summary>
        /// <param name="layerName">The name of the layer to add.</param>
        /// <returns>True if the layer was added, false if it already exists.</returns>
        /// <exception cref="InvalidOperationException">
        /// All allowed layer slots are already filled.
        /// </exception>
        public static bool AddLayer(string layerName)
        {
            SerializedObject tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]
            );
            SerializedProperty layersProp = tagManager.FindProperty("layers");
            if (!PropertyExists(layersProp, start: 0, end: MaxLayers, value: layerName))
            {
                SerializedProperty layerSlot;
                for (int layerIndex = 8; layerIndex < MaxLayers; layerIndex++)
                {
                    layerSlot = layersProp.GetArrayElementAtIndex(layerIndex);
                    if (string.IsNullOrEmpty(layerSlot.stringValue))
                    {
                        layerSlot.stringValue = layerName;
                        tagManager.ApplyModifiedProperties();
                        return true;
                    }
                }
                throw new InvalidOperationException("All allowed layers have been filled.");
            }
            return false;
        }

        /// <summary>Checks if a value exists in a serialized array property.</summary>
        /// <param name="property">The serialized array property to search.</param>
        /// <param name="start">The starting index for the search.</param>
        /// <param name="end">The ending index for the search.</param>
        /// <param name="value">The value to search for.</param>
        /// <returns>True if the value exists in the property range.</returns>
        private static bool PropertyExists(SerializedProperty property, int start, int end, string value)
        {
            for (int elementIndex = start; elementIndex < end; elementIndex++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(elementIndex);
                if (element.stringValue.Equals(value, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
#endif
