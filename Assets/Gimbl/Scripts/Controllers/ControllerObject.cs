/// <summary>
/// Provides the ControllerObject base class for input handling.
///
/// Defines the abstract controller interface and the ValueBuffer class for
/// accumulating input between frames.
/// </summary>
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Gimbl
{
    /// <summary>
    /// Represents the abstract base class for all input controllers.
    /// </summary>
    public abstract class ControllerObject : MonoBehaviour
    {
        /// <summary>The actor receiving input from this controller.</summary>
        public ActorObject actor;

        /// <summary>The buffer for accumulating movement input between frames.</summary>
        public ValueBuffer movement = new ValueBuffer(size: 100);

#if UNITY_EDITOR
        /// <summary>Parents this controller under the scene's Controllers root and registers it for undo.</summary>
        public void InitiateController()
        {
            gameObject.transform.SetParent(GameObject.Find("Controllers").transform);
            Undo.RegisterCreatedObjectUndo(gameObject, "Create Controller");
        }
#endif

        /// <summary>
        /// Buffers values for accumulating input between frames.
        /// </summary>
        /// <remarks>
        /// Add overflow clamps the write index at the final slot rather than wrapping. Callers are expected
        /// to drain the buffer (via <see cref="Sum"/> + <see cref="Clear"/>) each frame, so overflow should
        /// never occur under normal operation and the clamp exists only as a safety against unbounded writes.
        /// </remarks>
        public class ValueBuffer
        {
            /// <summary>The maximum size of the buffer.</summary>
            private readonly int _bufferSize;

            /// <summary>The array storing buffered values.</summary>
            private readonly float[] _values;

            /// <summary>The current write position in the buffer.</summary>
            private int _counter;

            /// <summary>Creates a new value buffer with the specified size.</summary>
            /// <param name="size">The maximum number of values to buffer.</param>
            public ValueBuffer(int size)
            {
                _bufferSize = size;
                _values = new float[_bufferSize];
                _counter = 0;
            }

            /// <summary>Adds a value to the buffer.</summary>
            /// <param name="value">The value to add.</param>
            public void Add(float value)
            {
                _values[_counter] = value;
                _counter++;

                if (_counter == _bufferSize)
                {
                    _counter = _bufferSize - 1;
                }
            }

            /// <summary>Returns the sum of all buffered values.</summary>
            /// <returns>The sum of values in the buffer.</returns>
            public float Sum()
            {
                float result = 0;

                for (int i = 0; i < _counter; i++)
                {
                    result += _values[i];
                }

                return result;
            }

            /// <summary>Clears all values from the buffer.</summary>
            public void Clear()
            {
                for (int i = 0; i < _counter; i++)
                {
                    _values[i] = 0;
                }

                _counter = 0;
            }
        }
    }
}
