/// <summary>
/// Provides the shared MenuSettings class used by Gimbl editor windows to track creation and selection
/// state for a generic Unity Object type.
/// </summary>
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Gimbl
{
    /// <summary>
    /// Tracks creation and selection state for an editor menu over a generic Unity Object type.
    /// </summary>
    /// <typeparam name="T">The Unity Object type this menu manages.</typeparam>
    public class MenuSettings<T>
        where T : UnityEngine.Object
    {
        /// <summary>The display name of the object type.</summary>
        public string typeName;

        /// <summary>The array of foldout visibility states.</summary>
        public bool[] show = { false, false, false, false, false };

        /// <summary>The name for creating new objects.</summary>
        public string name = "";

        /// <summary>The entity ID of the selected object, persisted across editor reloads.</summary>
        public EntityId selectedEntityId;

        /// <summary>The rectangle position for the editing window.</summary>
        public Rect editRect = new Rect();

        /// <summary>The backing field for the selected object.</summary>
        private T _selectedObject;

        /// <summary>The currently selected object.</summary>
        public T SelectedObject
        {
            get { return _selectedObject; }
            set
            {
                if (!UnityEngine.Object.ReferenceEquals(value, _selectedObject))
                {
                    _selectedObject = value;
                    if (value != null)
                    {
                        selectedEntityId = value.GetEntityId();
                    }
                    else
                    {
                        selectedEntityId = EntityId.None;
                    }
                }
            }
        }
    }
}
#endif
