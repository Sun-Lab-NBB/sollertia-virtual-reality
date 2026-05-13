/// <summary>
/// Provides utility functions for measuring prefab dimensions.
/// </summary>
using UnityEngine;

namespace SL.Tasks
{
    /// <summary>
    /// Static utility class for prefab measurements and other helper functions.
    /// </summary>
    public static class Utility
    {
        /// <summary>Calculates the z-axis lengths of an array of segment prefabs.</summary>
        /// <param name="segmentPrefabs">The array of segment prefab GameObjects.</param>
        /// <returns>An array of lengths corresponding to each prefab's z-axis extent.</returns>
        public static float[] GetSegmentLengths(GameObject[] segmentPrefabs)
        {
            int segmentCount = segmentPrefabs.Length;
            float[] segmentLengths = new float[segmentCount];

            for (int i = 0; i < segmentCount; i++)
            {
                segmentLengths[i] = GetPrefabLength(segmentPrefabs[i]);
            }

            return segmentLengths;
        }

        /// <summary>Calculates the z-axis length of a prefab by combining all child renderer bounds.</summary>
        /// <param name="prefab">The prefab GameObject to measure.</param>
        /// <returns>The z-axis size of the combined bounds, or 0 if no renderers found.</returns>
        public static float GetPrefabLength(GameObject prefab)
        {
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
            {
                Debug.LogWarning($"Utility.GetPrefabLength: No renderers found on prefab '{prefab.name}'.");
                return 0f;
            }

            Bounds combinedBounds = renderers[0].bounds;

            for (int i = 1; i < renderers.Length; i++)
            {
                combinedBounds.Encapsulate(renderers[i].bounds);
            }

            return combinedBounds.size.z;
        }
    }
}
