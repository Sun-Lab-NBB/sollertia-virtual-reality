/// <summary>
/// Defines the ControllerTypes enumeration for identifying available controller implementations.
/// </summary>
namespace Gimbl
{
    /// <summary>Defines the available controller types in the system.</summary>
    public enum ControllerTypes
    {
        /// <summary>Represents a physical linear treadmill connected via MQTT.</summary>
        LinearTreadmill,

        /// <summary>Represents a keyboard-simulated linear treadmill for testing without hardware.</summary>
        SimulatedLinearTreadmill,
    }
}
