cmd-aiplayer_spawn-desc = Spawns an autonomous AI player with the given job.
cmd-aiplayer_spawn-help = Usage: aiplayer_spawn [job id] [persistent id]
cmd-aiplayer_spawn-arg-job = <job id>
cmd-aiplayer_spawn-invalid-job = Unknown job prototype: { $job }
cmd-aiplayer_spawn-failed = Failed to spawn the AI player (no station or spawn point available).
cmd-aiplayer_spawn-success = Spawned AI player { $entity }.

cmd-aiplayer_spawn_cognitive-desc = Spawns an AI player running the AI Players 2.0 Milestone 1 cognitive loop (Desire/Belief/Intent via the LLM), instead of the legacy Goal System alone.
cmd-aiplayer_spawn_cognitive-help = Usage: aiplayer_spawn_cognitive [job id]
cmd-aiplayer_spawn_cognitive-arg-job = <job id>
cmd-aiplayer_spawn_cognitive-invalid-job = Unknown job prototype: { $job }
cmd-aiplayer_spawn_cognitive-failed = Failed to spawn the AI player (no station or spawn point available).
cmd-aiplayer_spawn_cognitive-success = Spawned cognitive-mode AI player { $entity }. Note: also requires the ai_players.cognitive.enabled CVar to be true for it to actually request cognitive decisions.

cmd-aiplayer_debug-desc = Dumps an AI player's internal state (personality, needs, goal, HTN, memory/relationships).
cmd-aiplayer_debug-help = Usage: aiplayer_debug <EntityUid>
cmd-aiplayer_debug-not-ai-player = { $entity } is not an AI player.

cmd-aiplayer_say-desc = Makes an AI player say a line via the Talk action.
cmd-aiplayer_say-help = Usage: aiplayer_say <EntityUid> <text...>
cmd-aiplayer_say-failed = Talk action failed: { $reason }
cmd-aiplayer_say-success = Said.
