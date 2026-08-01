# Rejected Snippets (PVS)

| Zone | What's found | Why not take it as a standard | Signal |
|---|---|---|---|
| `net.maxupdaterange` | "visibility radius CVar" | Does not exist; renamed to `net.pvs_range` (default 25, the *side* of the view square) | Missing CVar |
| `net.pvs_entity_budget` | "new entity budget CVar" | Does not exist; current name is `net.pvs_budget` (default 50) | Missing CVar |
| `net.pvs_entity_enter_budget` | "re-enter budget CVar" | Does not exist; current name is `net.pvs_enter_budget` (default 200, not 80) | Missing CVar |
| 5-tier LoD table | "LodCounts applied by distance (very far/far/average/close)" | Runtime uses only 2 tiers: whole `Contents` or `LodCounts[0]`. `LodCounts[1..3]` are computed but never consumed | Dead code path |
| `ViewBounds = NetPvsPriorityRange × PvsScale` | "the priority range is the view size" | `size = max(net.pvs_range, net.pvs_priority_range) × PvsScale`, radius `= size/2`; the priority range is meant for PvsPriority entities directly parented to a grid/map | Oversimplification |
| `ExpandPvsEvent` as a separate pipeline stage | "raised after overrides" | It is raised *inside* `AddAllOverrides`, and its `VisMask` silently replaces the session mask for all overrides that tick | Wrong ordering / hidden side effect |
| `net.pvs false` = "send everything every tick" | tuning tip | First state is a full dump (`enumerateAll`, `fromTick = 0`); afterwards only modified entities are sent (`GetAllEntityStates`) | Inaccurate semantics |
| `ForceSend` for containers | "it will make the whole container visible" | `ForceSend` never adds children — contents stay invisible; use `GlobalOverride`/`SessionOverride` | Missing children |
