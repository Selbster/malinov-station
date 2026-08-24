---
name: ss14-netcode
description: Architecture guide for SS14 networking - Lidgren transport, NetManager abstraction, typed messages, game state deltas, network events, EntityUid/NetEntity conversion, and component replication basics. Use it when writing or debugging any code that crosses the network boundary (RaiseNetworkEvent, AutoNetworkedField, Dirty/DirtyField, NetEntity conversions), before reaching for specialized ss14-pvs, ss14-prediction, or ss14-events skills.
---

# Claude Bridge

Canonical source skill file:
../../../.agents/skills/ss14-netcode/SKILL.md.

Use that file as the entrypoint and load resources from the same source skill directory.
