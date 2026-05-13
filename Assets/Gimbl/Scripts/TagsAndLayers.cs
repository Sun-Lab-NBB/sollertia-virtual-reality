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
        /// <returns>True if the tag was added, false if it already exists or limit reached.</returns>
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
        /// <returns>True if the layer was added, false if it already exists or no slots available.</returns>
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

        /// <summary>Removes a layer from the project.</summary>
        /// <param name="layerName">The name of the layer to remove.</param>
        /// <returns>True if the layer was removed, false if it does not exist.</returns>
        public static bool RemoveLayer(string layerName)
        {
            SerializedObject tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]
            );

            SerializedProperty layersProp = tagManager.FindProperty("layers");

            if (PropertyExists(layersProp, start: 0, end: layersProp.arraySize, value: layerName))
            {
                SerializedProperty layerSlot;

                for (int layerIndex = 0, arraySize = layersProp.arraySize; layerIndex < arraySize; layerIndex++)
                {
                    layerSlot = layersProp.GetArrayElementAtIndex(layerIndex);

                    if (layerSlot.stringValue.Equals(layerName, StringComparison.Ordinal))
                    {
                        layerSlot.stringValue = "";
                        tagManager.ApplyModifiedProperties();
                        return true;
                    }
                }
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
