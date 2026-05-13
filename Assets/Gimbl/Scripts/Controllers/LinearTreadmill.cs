/// <summary>
/// Provides the LinearTreadmill class for handling physical treadmill input via MQTT.
///
/// Receives movement data from an external treadmill device and translates it
/// to actor position updates in the VR environment.
/// </summary>
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Gimbl
{
    /// <summary>
    /// Handles linear treadmill input from MQTT and updates actor position.
    /// </summary>
    public class LinearTreadmill : ControllerObject
    {
        /// <summary>The settings for this treadmill controller.</summary>
        public LinearTreadmillSettings settings;

        /// <summary>The accumulated movement since last frame.</summary>
        private float _moved;

        /// <summary>The cached actor position for updates.</summary>
        private Vector3 _position;

        /// <summary>The cached actor rotation for updates.</summary>
        private Quaternion _newRotation;

        /// <summary>The MQTT channel subscribed to incoming treadmill data; null for simulated treadmills.</summary>
        private MQTTChannel<TreadmillMessage> _dataChannel;

        /// <summary>Sets up the MQTT listener for this treadmill on start.</summary>
        private void Start()
        {
            if (this is not SimulatedLinearTreadmill && settings != null)
            {
                _dataChannel = new MQTTChannel<TreadmillMessage>($"{settings.deviceName}/Data");
                _dataChannel.receivedEvent.AddListener(OnMessage);
            }
        }

        /// <summary>Removes the MQTT listener so the treadmill stops receiving data after destruction.</summary>
        private void OnDestroy()
        {
            _dataChannel?.receivedEvent.RemoveListener(OnMessage);
        }

        /// <summary>Processes accumulated movement each frame.</summary>
        public virtual void Update()
        {
            ProcessMovement();
        }

        /// <summary>Applies accumulated movement to the actor's position.</summary>
        public void ProcessMovement()
        {
            lock (movement)
            {
                if (actor != null && (settings == null || settings.isActive))
                {
                    _moved = movement.Sum();

                    _position = actor.transform.position;
                    _newRotation = actor.transform.rotation;

                    _position.z += _moved;

                    if (actor.isActive)
                    {
                        actor.transform.position = _position;
                        actor.transform.rotation = _newRotation;
                    }
                }

                movement.Clear();
            }
        }

        /// <summary>Receives movement data from the treadmill via MQTT callback.</summary>
        /// <param name="message">The message containing the movement value.</param>
        public void OnMessage(TreadmillMessage message)
        {
            lock (movement)
            {
                movement.Add(message.movement);
            }
        }

        /// <summary>Creates or links the settings ScriptableObject for this controller.</summary>
        /// <param name="assetPath">The path to an existing settings asset, or empty to create new.</param>
        public override void LinkSettings(string assetPath = "")
        {
#if UNITY_EDITOR
            LinearTreadmillSettings asset;

            if (string.IsNullOrEmpty(assetPath))
            {
                asset = ScriptableObject.CreateInstance<LinearTreadmillSettings>();
                AssetDatabase.CreateAsset(asset, $"Assets/VRSettings/Controllers/{gameObject.name}.asset");
            }
            else
            {
                asset = (LinearTreadmillSettings)
                    AssetDatabase.LoadAssetAtPath(assetPath, typeof(LinearTreadmillSettings));
            }

            settings = asset;
#endif
        }

        /// <summary>Renders the editor GUI for this controller.</summary>
        public override void EditMenu()
        {
#if UNITY_EDITOR
            SerializedObject serializedObject = new SerializedObject(settings);

            if (this is SimulatedLinearTreadmill)
            {
                ControllerMenuTitle(isActive: settings.isActive, type: "Simulated Linear Treadmill");
                EditorGUILayout.LabelField("Device", EditorStyles.boldLabel);

                if (EditorApplication.isPlaying)
                {
                    GUI.enabled = false;
                }

                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("isActive"),
                    new GUIContent("Active"),
                    LayoutSettings.EditFieldOption
                );
                EditorGUI.indentLevel--;
                GUI.enabled = true;
            }
            else
            {
                ControllerMenuTitle(isActive: settings.isActive, type: "Linear Treadmill");
                EditorGUILayout.LabelField("Device", EditorStyles.boldLabel);

                if (EditorApplication.isPlaying)
                {
                    GUI.enabled = false;
                }

                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("isActive"),
                    new GUIContent("Active"),
                    LayoutSettings.EditFieldOption
                );
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("deviceName"),
                    new GUIContent("MQTT Name"),
                    LayoutSettings.EditFieldOption
                );
                EditorGUI.indentLevel--;
                GUI.enabled = true;
            }
#endif
        }

        /// <summary>Defines the MQTT message structure for treadmill movement data.</summary>
        public class TreadmillMessage
        {
            /// <summary>The movement value received from the treadmill device.</summary>
            public float movement;
        }
    }
}
