cmd-aiplayer_spawn-desc = Создаёт автономного AI-игрока с указанной профессией.
cmd-aiplayer_spawn-help = Использование: aiplayer_spawn [id профессии] [persistent id]
cmd-aiplayer_spawn-arg-job = <id профессии>
cmd-aiplayer_spawn-invalid-job = Неизвестный прототип профессии: { $job }
cmd-aiplayer_spawn-failed = Не удалось создать AI-игрока (нет станции или точки спавна).
cmd-aiplayer_spawn-success = Создан AI-игрок { $entity }.

cmd-aiplayer_spawn_cognitive-desc = Создаёт AI-игрока, работающего через когнитивный цикл AI Players 2.0 Milestone 1 (Desire/Belief/Intent через LLM), вместо одной лишь устаревшей Goal System.
cmd-aiplayer_spawn_cognitive-help = Использование: aiplayer_spawn_cognitive [id профессии]
cmd-aiplayer_spawn_cognitive-arg-job = <id профессии>
cmd-aiplayer_spawn_cognitive-invalid-job = Неизвестный прототип профессии: { $job }
cmd-aiplayer_spawn_cognitive-failed = Не удалось создать AI-игрока (нет станции или точки спавна).
cmd-aiplayer_spawn_cognitive-success = Создан когнитивный AI-игрок { $entity }. Примечание: для реальных когнитивных решений также нужен включённый CVar ai_players.cognitive.enabled.

cmd-aiplayer_debug-desc = Выводит внутреннее состояние AI-игрока (личность, потребности, цель, HTN, память/отношения).
cmd-aiplayer_debug-help = Использование: aiplayer_debug <EntityUid>
cmd-aiplayer_debug-not-ai-player = { $entity } не является AI-игроком.

cmd-aiplayer_say-desc = Заставляет AI-игрока произнести реплику через действие Talk.
cmd-aiplayer_say-help = Использование: aiplayer_say <EntityUid> <текст...>
cmd-aiplayer_say-failed = Действие Talk не удалось: { $reason }
cmd-aiplayer_say-success = Сказано.
