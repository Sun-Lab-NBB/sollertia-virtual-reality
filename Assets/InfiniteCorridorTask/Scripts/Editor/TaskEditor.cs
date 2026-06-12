/// <summary>
/// Provides the TaskEditor class that replaces the default Inspector for Task components.
///
/// Renders a notice directing users to manage Task settings via the Window > Task Parameters window
/// instead of the Inspector.
/// </summary>
using UnityEditor;

namespace SL.Tasks
{
    /// <summary>
    /// Overrides the default Task inspector with a notice pointing to the Window > Task Parameters window.
    /// </summary>
    [CustomEditor(typeof(Task))]
    public class TaskEditor : Editor
    {
        /// <summary>Renders an informational message directing users to the Task Parameters window.</summary>
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Task settings are managed via Window > Task Parameters (Task section).",
                MessageType.Info
            );
        }
    }
}
