# NID102 flat world event and reset state fix

## ROOT CAUSE

`SendQuestWorldStateEvent` wrapped every world event in `world_state`, while the server decodes protocol fields at the root. RESET completion also used scalar semantic state `"reset"`.

## OLD NID102 SHAPE

`{ type: "QuestWorldStateEvent", world_state: { protocol_version, event_type, ... } }`

## NEW NID102 SHAPE

`{ type: "QuestWorldStateEvent", protocol_version, event_id, event_type, session_id, canonical_set_id, reset_request_id, semantic_state, details }`

## SERIALIZER FIX

`AuthoringProtocolClient.SendQuestWorldStateEvent` converts the event to a `JObject`, adds `type` at root, logs a compact pre-send diagnostic, then uses the unchanged NID102 sender.

## RESET SEMANTIC STATE

The previous value was `"reset"`. The new value is an object keyed by canonical object ID and derived after canonical physical application from real drawer, lock, door, painting, lamp, sphere, and active/inactive states.

## EXPECTED STATE SCHEMA

The result is a direct object mapping such as `lock_001: locked`, `table_drawer_001: closed`, and `key_001: inactive/active`, matching the server's object-to-state expected initial state shape.

## WORLD STATE SNAPSHOT

`RESET_COMPLETED_STATE_SNAPSHOT` records the correlated compact state object. `QUEST_WORLD_STATE_WIRE_PAYLOAD` records root fields and semantic-state keys immediately before send.

## OTHER WORLD EVENTS

All `QuestWorldStateEvent` messages use the same serializer and are now root-flat. `QuestConsequenceAck` and other NID102 families retain their existing serialization.

## RESET ORDER

Request → physical reset → canonical apply → SceneContext refresh → canonical applied → root-flat correlated completion.

## CORRELATION

Protocol v1, event ID, server session, canonical set, and exact server reset request ID remain root fields.

## TESTS

Static checks confirm no `world_state` wrapper remains and all protocol fields enter the root `JObject`. Unity compile/device verification remains required.

## DEVICE TEST

Set A C1: expect root-flat RESET_COMPLETED, server reset verification/barrier release, then `NextTaskGenerated(set_a:T1)` and activation.
