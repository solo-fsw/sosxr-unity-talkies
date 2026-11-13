# Changelog

All notable changes to this project will be documented in this file.
The changelog format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/)

## [0.4.0] - 2026-11-13

### Added

- Editor assembly `SOSXR.Talkies.Editor` with drawers for `[Button]`, `[ShowIf]`, `[EnableIf]`, `[DisableEditing]` and `[Suffix]`.
- `TalkiesButtonEditor`, registered for the package's own components. Derive it to get buttons on your own component.
- `ISerialConnect.Write(byte[], int, int)` and `ISerialConnect.Read(byte[], int, int)`. Reads do not block.
- `ByteSize.TryReadByte(out byte)` and `ByteSize.LastReceivedByte`.
- `MQTTClient.ClearSubscriptions()`, `SubscribedTopics`, `PendingPublicationCount` and `TopicMatches(filter, topic)`.
- Pure C# helpers `PinProtocol`, `SerialLineBuffer` and `PinDeadlineTracker`.
- EditMode tests in `Tests/Editor/` (`SOSXR.Talkies.Tests`) for pin protocol, line buffering, pin deadlines, `ByteSize`, MQTT and the Inspector attributes.

### Fixed

- `DesktopSerialConnector` P/Invoke matches the shipped `SerialPlugin.bundle` exports. Missing or wrong-arch plugin logs one error instead of throwing.
- `AndroidSerialConnector` reads return data through the Java `SerialBridge.readBytes`. Bytes beyond the request are kept for the next call.
- `PinController` resolves its connector lazily, parses responses with `PinProtocol`, skips malformed lines and no longer piles up handlers while disconnected.
- `SafetyPin` drives LOW only pins set through the controller. Negative pins are ignored.
- `PinTimer` and `PinTimerWithEvents` resolve their controller lazily and restart correctly after re-enable.
- `MQTTClient` incoming messages are queued thread-safely and dispatched on the main thread.
- `MQTTClient` lost-connection flag no longer fires `OnDisconnected` every frame.
- `MQTTClient` reconnect detaches old handlers and re-subscribes all topics.
- `MQTTClient` subscriptions survive disconnects and focus loss. Only `ClearSubscriptions()` drops them.
- `MQTTClient` TLS 1.2 encryption works. Empty topic means `#`.
- `MQTTUI.OnDisable` unsubscribes correctly.
- `MQTTFirstResponder` subscribes to `#` for an empty topic.

### Changed

- Dependency `com.unity.textmeshpro` replaced by `com.unity.ugui` 2.0.0.
- Samples are in visible `Samples/` (was `Samples~/`). `package.json` paths updated.
- `ByteSize` goes through `ISerialConnect` and no longer P/Invokes `SerialPlugin` directly.
- `ByteSize.SendCommand(byte)` and `SendCommand(byte, byte)` return `bool` (was `void`). Source compatible, binary breaking.
- `[Button]` attributes placed before XML doc comments are moved below them so the docs attach.

## [0.2.0] - In Development

### Added

-

### Fixed

-

### Changed

- Renamed the sosxr-unity-mqtt package to sosxr-unity-talkies, to encompass other connection-types in the same package.

### Removed

-

## [0.1.0] - 2025-10-20

### Forked from: [gpvigano](https://github.com/gpvigano/M2MqttUnity)

- Main purpose: turn into a package instead of a Unity Project

### Added

-

### Fixed

-

### Changed

-

### Removed

-
