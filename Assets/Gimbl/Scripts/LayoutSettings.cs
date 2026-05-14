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
    /// Widths are sized to fit the longest expected line — the Camera Mapping row of
    /// <c>"Monitor N (x, y) [Camera View ▾]"</c>. The 170 px monitor label, ~95 px dropdown content,
    /// and 15 px of MainBox padding together land just under the 280 px <see cref="MainBox"/> width.
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

        /// <summary>
        /// Defines the style for main content boxes in editor windows.
        /// </summary>
        public class MainBox
        {
            /// <summary>The GUI style for main boxes.</summary>
            public readonly GUIStyle Style;

            /// <summary>Creates a new main box style based on HelpBox.</summary>
            public MainBox()
            {
                Style = new GUIStyle("HelpBox");
                Style.margin = new RectOffset(10, 10, 10, 5);
                Style.padding = new RectOffset(10, 5, 5, 15);
                Style.fixedWidth = 280;
            }
        }

        /// <summary>The main box style instance for primary content.</summary>
        public static readonly MainBox MainBoxStyle = new MainBox();
    }
}
#endif
