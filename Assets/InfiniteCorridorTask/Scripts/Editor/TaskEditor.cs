/// <summary>
/// Provides the TaskEditor class that replaces the default Inspector for Task components.
///
/// Renders a notice directing users to manage Task settings via the Gimbl Settings window
/// instead of the Inspector.
/// </summary>
using UnityEditor;

namespace SL.Tasks
{
    /// <summary>
    /// Overrides the default Task inspector with a notice pointing to the Gimbl Settings window.
    /// </summary>
    [CustomEditor(typeof(Task))]
    public class TaskEditor : Editor
    {
        /// <summary>Renders an informational message directing users to the Settings window.</summary>
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Task settings are managed via Window > Gimbl > Settings (Task section).",
                MessageType.Info
            );
        }
    }
}
