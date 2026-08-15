cmd-aiplayer_spawn-desc = Spawns an autonomous AI player with the given job.
cmd-aiplayer_spawn-help = Usage: aiplayer_spawn [job id] [persistent id]
cmd-aiplayer_spawn-arg-job = <job id>
cmd-aiplayer_spawn-invalid-job = Unknown job prototype: { $job }
cmd-aiplayer_spawn-failed = Failed to spawn the AI player (no station or spawn point available).
cmd-aiplayer_spawn-success = Spawned AI player { $entity }.

cmd-aiplayer_debug-desc = Dumps an AI player's internal state (personality, needs, goal, HTN, memory/relationships).
cmd-aiplayer_debug-help = Usage: aiplayer_debug <EntityUid>
cmd-aiplayer_debug-not-ai-player = { $entity } is not an AI player.

cmd-aiplayer_say-desc = Makes an AI player say a line via the Talk action.
cmd-aiplayer_say-help = Usage: aiplayer_say <EntityUid> <text...>
cmd-aiplayer_say-failed = Talk action failed: { $reason }
cmd-aiplayer_say-success = Said.
