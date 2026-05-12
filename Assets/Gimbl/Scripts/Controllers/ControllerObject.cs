/// <summary>
/// Provides the ControllerObject base class for input handling.
///
/// Defines the abstract controller interface and the ValueBuffer class for
/// accumulating input between frames.
/// </summary>
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Gimbl
{
    /// <summary>
    /// Abstract base class for all input controllers.
    /// </summary>
    public abstract class ControllerObject : MonoBehaviour
    {
        /// <summary>The actor receiving input from this controller.</summary>
        public ActorObject actor;

        /// <summary>The buffer for accumulating movement input between frames.</summary>
        public ValueBuffer movement = new ValueBuffer(size: 100, circular: false);

        /// <summary>Renders the custom editor GUI for this controller type.</summary>
        public abstract void EditMenu();

        /// <summary>Creates or links the settings ScriptableObject for this controller.</summary>
        /// <param name="assetPath">The path to an existing settings asset, or empty to create new.</param>
        public abstract void LinkSettings(string assetPath = "");

#if UNITY_EDITOR
        /// <summary>Initializes a new controller and creates its settings asset.</summary>
        public void InitiateController()
        {
            gameObject.transform.SetParent(GameObject.Find("Controllers").transform);
            LinkSettings();
            Undo.RegisterCreatedObjectUndo(gameObject, "Create Controller");
        }

        /// <summary>Saves the controller settings to a user-specified file.</summary>
        public void SaveController()
        {
            // Gets controller type and file extension.
            string sourceType = AssetDatabase
                .GetMainAssetTypeAtPath($"Assets/VRSettings/Controllers/{name}.asset")
                .ToString();
            string[] typeParts = sourceType.Split('.');
            if (typeParts.Length < 2)
            {
                Debug.LogError($"Controller.SaveController: Invalid asset type format '{sourceType}'");
                return;
            }
            string extension = typeParts[1];

            string outputFile = EditorUtility.SaveFilePanel("Save Controller settings as..", "", "", extension);
            if (string.IsNullOrEmpty(outputFile))
            {
                return;
            }

            AssetDatabase.SaveAssets();
            string sourcePath = Path.Combine(Application.dataPath, $"VRSettings/Controllers/{name}.asset");
            FileUtil.ReplaceFile(sourcePath, outputFile);
        }

        /// <summary>Loads controller settings from a user-specified file.</summary>
        public void LoadController()
        {
            // Gets controller type and file extension.
            string sourceFile = $"Assets/VRSettings/Controllers/{gameObject.name}.asset";
            string sourceType = AssetDatabase.GetMainAssetTypeAtPath(sourceFile).ToString();
            string[] typeParts = sourceType.Split('.');
            if (typeParts.Length < 2)
            {
                Debug.LogError($"Controller.LoadController: Invalid asset type format '{sourceType}'");
                return;
            }
            string extension = typeParts[1];

            string inputFile = EditorUtility.OpenFilePanel("Import Setup", Application.dataPath, extension);
            if (string.IsNullOrEmpty(inputFile))
            {
                return;
            }

            // Removes current settings file and copies new one.
            string settingsFileAssetPath = $"Assets/VRSettings/Controllers/{gameObject.name}.asset";
            AssetDatabase.DeleteAsset(settingsFileAssetPath);

            string newLocation = Path.Combine(Application.dataPath, $"VRSettings/Controllers/{gameObject.name}.asset");
            FileUtil.CopyFileOrDirectory(inputFile, newLocation);
            AssetDatabase.ImportAsset(settingsFileAssetPath);

            if (gameObject.TryGetComponent<ControllerObject>(out ControllerObject controllerObject))
            {
                controllerObject.LinkSettings(settingsFileAssetPath);
            }
        }

        /// <summary>Deletes this controller after user confirmation.</summary>
        public void DeleteController()
        {
            bool accept = EditorUtility.DisplayDialog(
                $"Remove Controller {gameObject.name}?",
                $"Are you sure you want to delete Controller {gameObject.name}?",
                "Delete",
                "Cancel"
            );

            if (accept)
            {
                Undo.DestroyObjectImmediate(gameObject);
            }
        }

        /// <summary>Renders the controller menu title with status color.</summary>
        /// <param name="isActive">Determines whether the controller is active.</param>
        /// <param name="type">The display name of the controller type.</param>
        public void ControllerMenuTitle(bool isActive, string type)
        {
            EditorGUILayout.BeginHorizontal();

            if (isActive && actor != null)
            {
                EditorGUILayout.LabelField($"<color=#66CC00>{name}</color> - {type}", LayoutSettings.ControllerLabel);
            }
            else
            {
                EditorGUILayout.LabelField($"<color=#EE0000>{name}</color> - {type}", LayoutSettings.ControllerLabel);
            }

            EditorGUILayout.EndHorizontal();
        }
#endif

        /// <summary>
        /// Buffers values for accumulating input between frames.
        /// </summary>
        public class ValueBuffer
        {
            /// <summary>The maximum size of the buffer.</summary>
            private readonly int _bufferSize;

            /// <summary>Determines whether the buffer wraps around when full.</summary>
            private readonly bool _isCircular;

            /// <summary>The array storing buffered values.</summary>
            private readonly float[] _values;

            /// <summary>The current write position in the buffer.</summary>
            private int _counter;

            /// <summary>Creates a new value buffer with the specified size and mode.</summary>
            /// <param name="size">The maximum number of values to buffer.</param>
            /// <param name="circular">If true, the buffer wraps around when full.</param>
            public ValueBuffer(int size, bool circular)
            {
                _bufferSize = size;
                _values = new float[_bufferSize];
                _counter = 0;
                _isCircular = circular;
            }

            /// <summary>Adds a value to the buffer.</summary>
            /// <param name="value">The value to add.</param>
            public void Add(float value)
            {
                _values[_counter] = value;
                _counter++;

                if (_counter == _bufferSize)
                {
                    _counter = _isCircular ? 0 : _bufferSize - 1;
                }
            }

            /// <summary>Returns the sum of all buffered values.</summary>
            /// <returns>The sum of values in the buffer.</returns>
            public float Sum()
            {
                float result = 0;
                int limit = _isCircular ? _bufferSize : _counter;

                for (int i = 0; i < limit; i++)
                {
                    result += _values[i];
                }

                return result;
            }

            /// <summary>Clears all values from the buffer.</summary>
            public void Clear()
            {
                int limit = _isCircular ? _bufferSize : _counter;

                for (int i = 0; i < limit; i++)
                {
                    _values[i] = 0;
                }

                _counter = 0;
            }
        }
    }
}
