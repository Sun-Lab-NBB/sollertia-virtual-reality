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
    public static class LayoutSettings
    {
        /// <summary>The layout option for edit field width.</summary>
        public static readonly GUILayoutOption EditWidth = GUILayout.Width(330);

        /// <summary>The layout option for standard edit fields.</summary>
        public static readonly GUILayoutOption EditFieldOption = GUILayout.Width(300);

        /// <summary>The layout option for tab text width.</summary>
        public static readonly GUILayoutOption TabTextOption = GUILayout.Width(50);

        /// <summary>The layout option for button width.</summary>
        public static readonly GUILayoutOption ButtonOption = GUILayout.Width(100);

        /// <summary>The layout option for link object fields.</summary>
        public static readonly GUILayoutOption LinkObjectLayout = GUILayout.Width(147);

        /// <summary>The layout option for link label fields.</summary>
        public static readonly GUILayoutOption LinkFieldLayout = GUILayout.Width(150);

        /// <summary>The style for link field labels with rich text support.</summary>
        public static readonly GUIStyle LinkFieldStyle = new GUIStyle()
        {
            alignment = TextAnchor.MiddleLeft,
            normal = UnityEditor.EditorStyles.label.normal,
            fontStyle = FontStyle.Normal,
            richText = true,
            fixedWidth = 10,
        };

        /// <summary>The style for section header labels.</summary>
        public static readonly GUIStyle SectionLabel = new GUIStyle()
        {
            alignment = TextAnchor.MiddleLeft,
            normal = UnityEditor.EditorStyles.label.normal,
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            richText = true,
        };

        /// <summary>The style for controller header labels.</summary>
        public static readonly GUIStyle ControllerLabel = new GUIStyle()
        {
            alignment = TextAnchor.MiddleLeft,
            normal = UnityEditor.EditorStyles.label.normal,
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            richText = true,
        };

        /// <summary>
        /// Defines the style for nested content boxes in editor windows.
        /// </summary>
        public class SubBox
        {
            /// <summary>The GUI style for sub boxes.</summary>
            public readonly GUIStyle Style;

            /// <summary>Creates a new sub box style based on HelpBox.</summary>
            public SubBox()
            {
                Style = new GUIStyle("HelpBox");
                Style.margin = new RectOffset(15, 15, 10, 5);
                Style.padding = new RectOffset(10, 5, 5, 15);
            }
        }

        /// <summary>The sub-box style instance for nested content.</summary>
        public static readonly SubBox SubBoxStyle = new SubBox();

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
                Style.fixedWidth = 350;
            }
        }

        /// <summary>The main box style instance for primary content.</summary>
        public static readonly MainBox MainBoxStyle = new MainBox();
    }
}
#endif
