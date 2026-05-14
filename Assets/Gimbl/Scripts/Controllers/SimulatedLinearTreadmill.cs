/// <summary>
/// Provides the SimulatedLinearTreadmill class for keyboard-based treadmill simulation.
///
/// Extends the LinearTreadmill controller to accept keyboard input instead of MQTT
/// messages, enabling testing without physical hardware.
/// </summary>
using UnityEngine;
using UnityEngine.InputSystem;

namespace Gimbl
{
    /// <summary>
    /// Simulates linear treadmill input using keyboard controls.
    /// </summary>
    public class SimulatedLinearTreadmill : LinearTreadmill
    {
        /// <summary>The movement speed multiplier for input scaling.</summary>
        private const float MovementSpeedMultiplier = 8.0f;

        /// <summary>The Unity Input System action map for keyboard/mouse simulation.</summary>
        private SimulatedInput _input;

        /// <summary>The MQTT channel for sending simulated lick trigger events.</summary>
        private MQTTChannel _lickTrigger;

        /// <summary>Initializes the Input System for keyboard/mouse simulation on start.</summary>
        private void Start()
        {
            _input = new SimulatedInput();
            _input.Enable();
            _lickTrigger = new MQTTChannel("LickPort/");
        }

        /// <summary>Processes simulated input and movement each frame.</summary>
        public override void Update()
        {
            GetSimulatedInput();
            if (
                actor != null
                && (
                    _input.Player.Jump.WasPressedThisFrame()
                    || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                )
            )
            {
                _lickTrigger?.Send();
            }
            ProcessMovement();
        }

        /// <summary>Cleans up the Input System resources when destroyed.</summary>
        private void OnDestroy()
        {
            if (_input != null)
            {
                _input.Disable();
                _input.Dispose();
            }
        }

        /// <summary>Reads keyboard input and converts it to treadmill movement values.</summary>
        public void GetSimulatedInput()
        {
            float moveControl =
                _input.Player.Movement.ReadValue<Vector2>().y * Time.deltaTime * MovementSpeedMultiplier;
            movement.Add(moveControl);
        }
    }
}
