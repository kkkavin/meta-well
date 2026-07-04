# Runtime debug panels

Drag these prefabs into a sample scene to inspect live SDK state while the scene runs.

| Prefab | Shows |
|---|---|
| `Emotion State Debug Panel.prefab` | backend emotion label, emotion scale, resolved face channel, controller weight |
| `Dynamic Context Debug Panel.prefab` | local state, events, attention object, advanced updates, backend acknowledgement |

Both prefabs build their own screen-space overlay canvas at runtime. In single-character sample scenes, keep **Auto Resolve** enabled. In multi-character scenes, assign the target `ConvaiCharacter` and related controller or manager explicitly.
