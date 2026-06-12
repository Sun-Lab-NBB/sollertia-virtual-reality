/// <summary>
/// Provides the MQTTChannel classes for type-safe MQTT messaging.
///
/// Includes the base MQTTChannel for simple trigger messages and the generic
/// MQTTChannel&lt;TMessage&gt; for JSON-serialized typed messages.
/// </summary>
using System;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

namespace Gimbl
{
    /// <summary>
    /// Handles simple trigger-based MQTT messaging without payload data.
    /// </summary>
    public class MQTTChannel
    {
        /// <summary>The MQTT topic string for this channel.</summary>
        public readonly string topic;

        /// <summary>The reference to the MQTTClient managing the broker connection.</summary>
        public readonly MQTTClient client;

        /// <summary>The Unity event invoked when a message is received on this channel.</summary>
        public readonly UnityEvent receivedEvent = new UnityEvent();

        /// <summary>Creates a new MQTT channel for the specified topic.</summary>
        /// <param name="topicString">The MQTT topic to subscribe to or publish on.</param>
        /// <param name="isListener">Determines whether to subscribe to receive messages on this topic.</param>
        /// <param name="qosLevel">The Quality of Service level for the subscription.</param>
        /// <exception cref="InvalidOperationException">No <see cref="MQTTClient"/> singleton is available.</exception>
        public MQTTChannel(string topicString, bool isListener = true, byte qosLevel = 2)
        {
            topic = topicString;
            client = MQTTClient.Instance;
            if (client == null)
            {
                throw new InvalidOperationException("MQTTChannel: MQTTClient.Instance not available.");
            }

            if (isListener)
            {
                client.Subscribe(this, topic, qosLevel);
            }
        }

        /// <summary>Handles received messages by invoking the receivedEvent.</summary>
        /// <param name="messageString">The received message string (ignored for trigger channels).</param>
        public virtual void ReceivedMessage(string messageString)
        {
            receivedEvent.Invoke();
        }

        /// <summary>Publishes a trigger message (null payload) to this channel's topic.</summary>
        public void Send()
        {
            client.Publish(topic, null);
        }
    }

    /// <summary>
    /// Handles typed MQTT messaging with JSON serialization for the payload.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message payload to serialize and deserialize.</typeparam>
    /// <remarks>
    /// The typed <see cref="receivedEvent"/> shadows the base <see cref="MQTTChannel.receivedEvent"/> via
    /// the <c>new</c> modifier because <see cref="UnityEngine.Events.UnityEvent"/> and
    /// <see cref="UnityEngine.Events.UnityEvent{T0}"/> are unrelated types with no shared parameterized
    /// contract. A virtual property cannot express both signatures, so the payload type would be lost
    /// under a clean override. Callers that need the deserialized payload must reference the channel as
    /// <see cref="MQTTChannel{TMessage}"/>; a base <see cref="MQTTChannel"/> reference exposes only the
    /// parameterless trigger event and will silently miss the typed callback.
    /// </remarks>
    public class MQTTChannel<TMessage> : MQTTChannel
    {
        /// <summary>The typed Unity event invoked when a message is received on this channel.</summary>
        public new readonly ChannelEvent receivedEvent = new ChannelEvent();

        /// <summary>Creates a new typed MQTT channel for the specified topic.</summary>
        /// <param name="topicString">The MQTT topic to subscribe to or publish on.</param>
        /// <param name="isListener">Determines whether to subscribe to receive messages on this topic.</param>
        /// <param name="qosLevel">The Quality of Service level for the subscription.</param>
        public MQTTChannel(string topicString, bool isListener = true, byte qosLevel = 2)
            : base(topicString, isListener, qosLevel) { }

        /// <summary>Handles received messages by deserializing JSON and invoking the typed receivedEvent.</summary>
        /// <param name="messageString">The received JSON message string.</param>
        public override void ReceivedMessage(string messageString)
        {
            try
            {
                TMessage message = JsonUtility.FromJson<TMessage>(messageString);
                receivedEvent.Invoke(message);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"MQTTChannel<{typeof(TMessage).Name}>: Failed to deserialize message: {exception.Message}",
                    exception
                );
            }
        }

        /// <summary>Publishes a typed message as JSON to this channel's topic.</summary>
        /// <param name="message">The message object to serialize and publish.</param>
        public void Send(TMessage message)
        {
            client.Publish(topic, Encoding.UTF8.GetBytes(JsonUtility.ToJson(message)));
        }

        /// <summary>The typed Unity event class for this channel.</summary>
        public class ChannelEvent : UnityEvent<TMessage> { }
    }
}
