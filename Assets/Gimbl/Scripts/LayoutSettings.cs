/// <summary>
/// Provides GUI layout configuration for Gimbl editor windows.
/// </summary>
#if UNITY_EDITOR
using UnityEngine;

namespace Gimbl
{
    /// <summary>
    /// Defines shared GUI layout options and styles for editor windows.
    /// </summary>
    /// <remarks>
    /// Widths are sized to fit the longest expected line — illustratively the Camera Mapping row, whose label
    /// reads <c>"Monitor N (x, y)"</c> followed by a camera-name Popup (its entries are "None" or a scene camera
    /// name, not a literal "Camera View" string). The ~170 px monitor label, an unmeasured ~95 px dropdown estimate,
    /// and ~15 px of MainBox padding together add up to roughly the 280 px <see cref="MainBoxStyle"/> width.
    /// </remarks>
    public static class LayoutSettings
    {
        /// <summary>The layout option for standard edit fields (label + widget combined).</summary>
        public static readonly GUILayoutOption EditFieldOption = GUILayout.Width(220);

        /// <summary>The style for section header labels.</summary>
        public static readonly GUIStyle SectionLabel = new GUIStyle()
        {
            alignment = TextAnchor.MiddleLeft,
            normal = UnityEditor.EditorStyles.label.normal,
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            richText = true,
        };

        /// <summary>The style for main content boxes in editor windows.</summary>
        public static readonly GUIStyle MainBoxStyle = new GUIStyle("HelpBox")
        {
            margin = new RectOffset(10, 10, 10, 5),
            padding = new RectOffset(10, 5, 5, 15),
            fixedWidth = 280,
        };
    }
}
#endif
