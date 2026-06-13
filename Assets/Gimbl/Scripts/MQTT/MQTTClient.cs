/// <summary>
/// Provides the MQTTClient class for managing connectivity with the MQTT broker.
///
/// Handles connection establishment, topic subscription, and message routing for
/// bidirectional communication between Unity and external systems like sollertia-experiment.
/// </summary>
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using UnityEngine;

namespace Gimbl
{
    /// <summary>
    /// Manages the MQTT broker connection and routes messages to subscribed channels.
    /// </summary>
    /// <remarks>
    /// This MonoBehaviour should be attached to a GameObject named "MQTT Client" in the scene.
    /// Connection settings (IP and port) are loaded from Unity EditorPrefs.
    /// Access via the static Instance property instead of GameObject.Find().
    /// </remarks>
    public class MQTTClient : MonoBehaviour
    {
        /// <summary>The IP address of the MQTT broker.</summary>
        /// <remarks>
        /// The initializer matches the loopback fallback applied by <see cref="Awake"/> and by
        /// <see cref="Gimbl.MainWindow.EnsureMqttDefaults"/> so a freshly-instantiated client (via
        /// <c>AddComponent</c> at editor time, before either hook has run) reports the same value
        /// the eventual fallback would assign.
        /// </remarks>
        [HideInInspector]
        public string ipAddress = "127.0.0.1";

        /// <summary>The port number of the MQTT broker.</summary>
        /// <remarks>
        /// The initializer matches the standard MQTT port fallback applied by <see cref="Awake"/>
        /// and by <see cref="Gimbl.MainWindow.EnsureMqttDefaults"/>.
        /// </remarks>
        [HideInInspector]
        public int port = 1883;

        /// <summary>The underlying MQTTnet client instance.</summary>
        public IMqttClient client;

        /// <summary>The singleton instance of the MQTTClient.</summary>
        public static MQTTClient Instance { get; private set; }

        /// <summary>The list of all subscribed channels for message routing.</summary>
        private List<Channel> _channelList = new List<Channel>();

        /// <summary>The topics already warned about no-broker loopback delivery, to avoid repeating it.</summary>
        private readonly HashSet<string> _loopbackWarnedTopics = new HashSet<string>();

        /// <summary>The channel for broadcasting session start events.</summary>
        private MQTTChannel _startChannel;

        /// <summary>The channel for broadcasting session stop events.</summary>
        private MQTTChannel _stopChannel;

        /// <summary>The stored handler for received MQTT application messages.</summary>
        private Func<MqttApplicationMessageReceivedEventArgs, Task> _messageReceivedHandler;

        /// <summary>Registers this instance as the singleton and loads connection settings on awake.</summary>
        /// <remarks>
        /// Connection settings are loaded in Awake (rather than Start) because peer scripts such as
        /// <see cref="MQTTConnectorObject"/> trigger <see cref="Connect"/> from their OnEnable, which
        /// Unity executes after every Awake but before every Start. Reading ipAddress/port in Start
        /// would leave them empty when the connect call runs and crash MqttClientOptionsBuilder.
        /// </remarks>
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("MQTTClient: Multiple instances found, using existing instance");
                return;
            }
            Instance = this;

#if UNITY_EDITOR
            ipAddress = UnityEditor.EditorPrefs.GetString("SollertiaVR_MQTT_IP");
            port = UnityEditor.EditorPrefs.GetInt("SollertiaVR_MQTT_Port");
#endif

            // Falls back to localhost defaults so a fresh project always attempts a connection. The
            // Task Parameters window applies the same fallback when its UI is opened; mirroring it here
            // ensures users who have not yet visited that window still get a working broker setup when
            // mosquitto (or another local broker) is running on standard ports.
            if (string.IsNullOrEmpty(ipAddress))
            {
                ipAddress = "127.0.0.1";
            }
            if (port == 0)
            {
                port = 1883;
            }
        }

        /// <summary>Creates the session start/stop publish channels and broadcasts session start.</summary>
        private void Start()
        {
            _startChannel = new MQTTChannel(MQTTTopics.SessionStart, isListener: false);
            _stopChannel = new MQTTChannel(MQTTTopics.SessionStop, isListener: false);
            // Discards the returned Task because the broadcast is genuinely fire-and-forget; any failure
            // is logged inside StartSessionAsync and there is no caller to observe completion.
            _ = StartSessionAsync();
        }

        /// <summary>Sends session stop message and cleans up subscriptions on application quit.</summary>
        private void OnApplicationQuit()
        {
            _stopChannel.Send();

            if (_channelList.Count > 0 && IsConnected())
            {
                MqttClientUnsubscribeOptionsBuilder unsubscribeOptions = new MqttClientUnsubscribeOptionsBuilder();
                foreach (string topic in _channelList.Select(channel => channel.topic))
                {
                    unsubscribeOptions.WithTopicFilter(topic);
                }
                client.UnsubscribeAsync(unsubscribeOptions.Build()).GetAwaiter().GetResult();
            }

            _channelList = new List<Channel>();

            if (client != null && _messageReceivedHandler != null)
            {
                client.ApplicationMessageReceivedAsync -= _messageReceivedHandler;
            }

            Disconnect();

            client?.Dispose();
            client = null;

            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>Unsubscribes the message handler and disposes the client on destroy.</summary>
        /// <remarks>
        /// Duplicates the cleanup performed by <see cref="OnApplicationQuit"/> because the two lifecycle
        /// callbacks do not always both fire. A scene transition that destroys this component without
        /// quitting the application reaches only <c>OnDestroy</c>; a process exit that bypasses scene
        /// teardown reaches only <c>OnApplicationQuit</c>. The duplicated handler unhook and disposal
        /// ensure the underlying <see cref="IMqttClient"/> is released in either path.
        /// </remarks>
        private void OnDestroy()
        {
            if (client != null && _messageReceivedHandler != null)
            {
                client.ApplicationMessageReceivedAsync -= _messageReceivedHandler;
            }

            client?.Dispose();
            client = null;

            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>Establishes a connection to the MQTT broker.</summary>
        /// <param name="verbose">Determines whether to log successful connection to the console.</param>
        public void Connect(bool verbose)
        {
            MqttFactory factory = new MqttFactory();
            client = factory.CreateMqttClient();

            // Routes received messages to the appropriate subscribed channels.
            _messageReceivedHandler = e =>
            {
                string payload = Encoding.UTF8.GetString(
                    e.ApplicationMessage.PayloadSegment.Array ?? Array.Empty<byte>(),
                    e.ApplicationMessage.PayloadSegment.Offset,
                    e.ApplicationMessage.PayloadSegment.Count
                );

                lock (_channelList)
                {
                    foreach (Channel channel in _channelList)
                    {
                        if (string.Equals(e.ApplicationMessage.Topic, channel.topic, StringComparison.Ordinal))
                        {
                            channel.mqttChannel.ReceivedMessage(payload);
                        }
                    }
                }

                return Task.CompletedTask;
            };
            client.ApplicationMessageReceivedAsync += _messageReceivedHandler;

            MqttClientOptions options = new MqttClientOptionsBuilder()
                .WithTcpServer(ipAddress, port)
                .WithClientId(Guid.NewGuid().ToString())
                .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500)
                .Build();

            // Runs connection in a task with timeout to avoid blocking.
            Task connectionTask = Task.Run(() => client.ConnectAsync(options));
            TimeSpan timeout = TimeSpan.FromMilliseconds(1000);
            if (!connectionTask.Wait(timeout))
            {
                Debug.LogError($"Could not connect to MQTT broker at {ipAddress}:{port}");
            }
            else if (verbose)
            {
                Debug.Log($"Successfully connected to MQTT Broker at: {ipAddress}:{port}");
            }
        }

        /// <summary>Disconnects from the MQTT broker if currently connected.</summary>
        public void Disconnect()
        {
            if (IsConnected())
            {
                client.DisconnectAsync().GetAwaiter().GetResult();
            }
        }

        /// <summary>Checks whether the client is currently connected to the broker.</summary>
        /// <returns>True if connected, false otherwise.</returns>
        public bool IsConnected()
        {
            try
            {
                return client != null && client.IsConnected;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"MQTTClient.IsConnected check failed: {exception.Message}");
                return false;
            }
        }

        /// <summary>Subscribes a channel to receive messages on the specified topic.</summary>
        /// <remarks>
        /// The channel is added to <see cref="_channelList"/> unconditionally so that <see cref="Publish"/>'s
        /// no-broker fallback can route messages locally during keyboard-only test runs. The broker-side
        /// <c>SubscribeAsync</c> only fires when the client is currently connected; a channel created while
        /// the broker is offline will receive in-process loopback messages but will <b>not</b> auto-subscribe
        /// once the broker comes online — callers that need broker-delivered messages after a late connect
        /// must re-create the channel or trigger a new subscribe pass.
        /// </remarks>
        /// <param name="channel">The MQTTChannel to receive messages.</param>
        /// <param name="topic">The MQTT topic to subscribe to.</param>
        /// <param name="qosLevel">The Quality of Service level for the subscription.</param>
        public void Subscribe(MQTTChannel channel, string topic, byte qosLevel)
        {
            lock (_channelList)
            {
                _channelList.Add(new Channel() { topic = topic, mqttChannel = channel });
            }

            if (!IsConnected())
            {
                return;
            }

            MqttQualityOfServiceLevel qualityOfServiceLevel = (MqttQualityOfServiceLevel)qosLevel;
            client
                .SubscribeAsync(
                    new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(f => f.WithTopic(topic).WithQualityOfServiceLevel(qualityOfServiceLevel))
                        .Build()
                )
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        string message = $"MQTT subscribe failed for '{topic}': {t.Exception?.InnerException?.Message}";
                        Debug.LogError(message);
                    }
                });
        }

        /// <summary>Publishes a message to the specified topic.</summary>
        /// <param name="topic">The MQTT topic to publish to.</param>
        /// <param name="payload">The message payload as a byte array, or null for trigger messages.</param>
        public void Publish(string topic, byte[] payload)
        {
            // When the broker is unreachable (typical for keyboard-only test runs without mosquitto),
            // routes the message directly to in-process subscribers on the matching topic. Production
            // setups with a real broker reach the IsConnected() branch below and use MQTT as normal.
            if (!IsConnected())
            {
                if (_loopbackWarnedTopics.Add(topic))
                {
                    string warning =
                        $"MQTTClient: broker unreachable, so '{topic}' is delivered to in-process "
                        + "subscribers only and will not reach sollertia-experiment. This is expected for "
                        + "keyboard-only testing, but a topic that works only this way has no wired "
                        + "experiment-side counterpart.";
                    Debug.LogWarning(warning);
                }

                string payloadString = payload == null ? string.Empty : Encoding.UTF8.GetString(payload);
                lock (_channelList)
                {
                    foreach (Channel channel in _channelList)
                    {
                        if (string.Equals(channel.topic, topic, StringComparison.Ordinal))
                        {
                            channel.mqttChannel.ReceivedMessage(payloadString);
                        }
                    }
                }
                return;
            }

            MqttApplicationMessage message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload ?? Array.Empty<byte>())
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                .Build();

            client
                .PublishAsync(message)
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        Debug.LogError($"MQTT publish failed on '{topic}': {t.Exception?.InnerException?.Message}");
                    }
                });
        }

        /// <summary>Sends the session start message after a brief delay.</summary>
        /// <remarks>
        /// Returns <see cref="Task"/> rather than <c>void</c> so exceptions thrown after the awaited delay
        /// surface through normal Task-error propagation if a future caller ever decides to observe them.
        /// The body still catches and logs internally because the only current caller fires-and-forgets.
        /// </remarks>
        /// <returns>A task that completes once the session-start message has been published.</returns>
        private async Task StartSessionAsync()
        {
            try
            {
                await Task.Delay(1000);
                _startChannel.Send();
            }
            catch (Exception exception)
            {
                Debug.LogError($"MQTTClient.StartSessionAsync failed: {exception.Message}");
            }
        }

        /// <summary>Maps a topic string to its corresponding channel handler.</summary>
        private class Channel
        {
            /// <summary>The MQTT topic this channel is subscribed to.</summary>
            public string topic;

            /// <summary>The MQTTChannel instance that handles messages for this topic.</summary>
            public MQTTChannel mqttChannel;
        }
    }
}
