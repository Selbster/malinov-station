using System.Text;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;

namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// Builds the system/user prompt text sent to the LLM from an <see cref="AiContext"/>. Pure string
/// formatting only - no IO, no game state access (that's <see cref="Systems.ContextBuilderSystem"/>'s job).
/// </summary>
public static class PromptBuilder
{
    /// <param name="allowedIntents">Every intent the caller will actually accept - previously this hardcoded
    /// only <see cref="AIGoals.All"/>, silently omitting any loaded professional-goal ids the gateway's own
    /// whitelist would accept (a real gap: the LLM could never legally propose one, since it was never told
    /// it existed). Now the caller (which already has to look prototypes up to build the whitelist) passes
    /// the full combined list in.</param>
    public static string BuildSystemPrompt(IReadOnlyCollection<string> allowedIntents)
    {
        return "You are the decision-making mind of an NPC aboard a space station in a sci-fi roleplaying game. " +
               "You will be given the character's personality, needs, current goal, and what they can currently perceive. " +
               "Decide what the character's goal should be right now, in character. " +
               "Respond with ONLY a single JSON object, no other text, of the exact form: " +
               "{\"intent\": \"<one of the allowed intents>\", \"priority\": <number 0.0 to 1.0>, \"reason\": \"<short in-character reason>\"}. " +
               $"Allowed intents: {string.Join(", ", allowedIntents)}. " +
               "Use exactly one of the allowed intents, spelled exactly as given.";
    }

    public static string BuildUserPrompt(AiContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Character: {context.Name}, job: {context.Job}.");
        sb.AppendLine($"Notable personality traits: {context.PersonalitySummary}.");
        sb.AppendLine($"Needs (0 = fine, 1 = critical): Fatigue={context.Fatigue:0.00}, Stress={context.Stress:0.00}, SocialNeed={context.SocialNeed:0.00}.");
        sb.AppendLine($"Current goal: {context.CurrentGoal} (priority {context.CurrentGoalPriority:0.00}, reason: {context.CurrentGoalReason}).");

        if (context.VisibleCharacters.Count == 0)
        {
            sb.AppendLine("You do not currently see anyone else nearby.");
        }
        else
        {
            sb.AppendLine("You can currently see:");
            foreach (var character in context.VisibleCharacters)
            {
                sb.Append($"- {character.Name} (trust {character.Trust:0.00}, respect {character.Respect:0.00}, ");
                sb.Append($"friendship {character.Friendship:0.00}, fear {character.Fear:0.00})");
                if (!string.IsNullOrWhiteSpace(character.RelevantMemory))
                    sb.Append($" - last memory: {character.RelevantMemory}");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>AI Players 0.5: Russian-first - the dialogue LLM role's system prompt. JSON key ("line") and
    /// speaker/character identifiers stay English (technical IDs); the instructional prose and the requested
    /// output content are Russian.</summary>
    public static string BuildDialogueSystemPrompt()
    {
        return "Ты озвучиваешь одну реплику персонажа в коротком, непринуждённом обмене репликами между двумя " +
               "NPC на космической станции в научно-фантастической ролевой игре. Держись кратко (одно " +
               "предложение), в характере персонажа, в соответствии с его личностью и отношением к собеседнику. " +
               "Ответь ТОЛЬКО одним JSON-объектом, без другого текста, строго такого вида: " +
               "{\"line\": \"<что персонаж говорит>\"}. " +
               "ВАЖНО: значение поля \"line\" должно быть написано ТОЛЬКО на русском языке, ни одного слова " +
               "на английском.";
    }

    public static string BuildDialogueUserPrompt(DialogueContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Говорящий: {context.SpeakerName}. Заметные черты личности: {context.SpeakerPersonalitySummary}.");
        sb.AppendLine(
            $"Собеседник: {context.PartnerName} (доверие {context.Trust:0.00}, уважение {context.Respect:0.00}, " +
            $"дружба {context.Friendship:0.00}, страх {context.Fear:0.00}).");

        if (!string.IsNullOrWhiteSpace(context.RelevantMemory))
            sb.AppendLine($"Последнее, что {context.SpeakerName} помнит про {context.PartnerName}: {context.RelevantMemory}");

        if (!string.IsNullOrWhiteSpace(context.RumorToShare))
        {
            sb.AppendLine(
                $"{context.SpeakerName} хочет рассказать {context.PartnerName} кое-что важное, чему стал(а) " +
                $" свидетелем: \"{context.RumorToShare}\". Начни разговор именно с этого, в характере " +
                "персонажа (например, \"Ты слышал(а)...\"), а не с обычной болтовни.");
        }
        else if (!string.IsNullOrWhiteSpace(context.ReasonForApproaching))
        {
            sb.AppendLine(
                $"{context.SpeakerName} специально подошёл (подошла) поговорить с {context.PartnerName} по причине: " +
                $"{context.ReasonForApproaching}. Начни разговор именно с этого, в характере персонажа, а не с " +
                "обычной болтовни.");
        }
        else
        {
            sb.AppendLine(context.LinePartnerJustSaid is null
                ? $"{context.SpeakerName} начинает разговор с короткого приветствия или вопроса, как дела."
                : $"{context.PartnerName} только что сказал(а): \"{context.LinePartnerJustSaid}\". {context.SpeakerName} " +
                  "должен (должна) коротко и естественно ответить.");
        }

        return sb.ToString();
    }

    /// <summary>AI Players 0.3/Navigation Controller: the LLM Cognitive Layer's system prompt.
    /// <paramref name="allowedGoals"/> is still the combined AIGoals/professional-goal whitelist (same list
    /// <see cref="Systems.LlmGatewaySystem.GetAllowedIntents"/> always computed) - it validates the "goal"
    /// parameter of the "PursueGoal" action, not "intention" (which is free-form and unvalidated). NOTE: the
    /// eight available actions described below (PursueGoal, ContinueActivity, GoToKnownLocation,
    /// UseInteractable, PickUpItem, SearchArea, EatOrDrink, TalkTo) must stay in sync with
    /// <see cref="ActionProposalResolver"/>'s switch - acceptable to hardcode at this size, revisit once the
    /// action catalog grows.</summary>
    public static string BuildCognitiveSystemPrompt(IReadOnlyCollection<string> allowedGoals)
    {
        return "You are the mind of a character aboard a space station in a sci-fi roleplaying game - not a script, " +
               "a person with their own desires, beliefs and feelings. You will be given your current needs, mood, " +
               "personality, what you're currently doing, what you currently want (your desires) and how strongly, " +
               "who/what you can see, what you know for certain, things you've heard but aren't sure are true, " +
               "places you know how to get to, nearby things you could interact with, and nearby items you could " +
               "pick up. " +
               "You only know what is listed below - anything not mentioned, you have no way of knowing. " +
               "Decide what you actually want to do right now, in character, and why, and pick ONE concrete " +
               "action to take toward it. " +
               "Respond with ONLY a single JSON object, no other text, of the exact form: " +
               "{\"desire\": \"<which of your current desires this commits to>\", " +
               "\"intention\": \"<a short free-form description of what you want, e.g. find_food, meet_person, " +
               "help_person, finish_repair, investigate_event, find_safe_location, avoid_security, obtain_item, " +
               "rest, escape_danger - not required to match any specific game mechanic>\", " +
               "\"priority\": <number 0.0 to 1.0>, " +
               "\"confidence\": <number 0.0 to 1.0, how sure you are this is the right call>, " +
               "\"reason\": \"<short in-character reason>\", " +
               "\"action\": \"<one of: PursueGoal, ContinueActivity, GoToKnownLocation, UseInteractable, PickUpItem, SearchArea, EatOrDrink, TalkTo>\", " +
               "\"parameters\": {\"goal\": \"<required only for PursueGoal - one of the allowed goals below>\", " +
               "\"location\": \"<required only for GoToKnownLocation - one of the places you know below>\", " +
               "\"target\": \"<required only for UseInteractable, PickUpItem or TalkTo - the name of the thing " +
               "to use, pick up, or the person to talk to>\", " +
               "\"keyword\": \"<required only for SearchArea - what to look for>\"}}. " +
               "\"PursueGoal\" actively commits to one of your existing goals right now - use it when one of the " +
               "allowed goals below matches what you want to do. \"GoToKnownLocation\" walks you to a place you " +
               "know how to get to - use it ONLY for a place actually listed below, never one you merely guess " +
               "the name of. \"UseInteractable\" uses a nearby object like a switch - use it ONLY for something " +
               "actually listed below, never one you merely guess the name of. \"PickUpItem\" picks up a nearby " +
               "loose item into your hand - use it ONLY for an item actually listed below, never one you merely " +
               "guess the name of. \"SearchArea\" actively looks around right now for something matching a " +
               "keyword that ISN'T already listed below - use it when you want something you don't currently " +
               "see or know how to reach, e.g. to look for food when none is listed. \"EatOrDrink\" eats or " +
               "drinks whatever is currently in your hand, with no parameters. \"TalkTo\" starts a real " +
               "conversation with someone you can currently see - use it ONLY for someone actually listed among " +
               "who you can see, never one you merely guess is nearby; your own \"reason\" field becomes why " +
               "you're approaching them, and shapes what you actually say. \"ContinueActivity\" means keep " +
               "doing what you're already doing, with no parameters. " +
               $"Allowed goals (for PursueGoal's \"goal\" parameter only): {string.Join(", ", allowedGoals)}. " +
               "Use exactly one of the allowed goals, spelled exactly as given.";
    }

    /// <summary>AI Players 2.0 Milestone 1: the LLM Cognitive Layer's user prompt, from a full <see cref="CognitiveState"/>.</summary>
    public static string BuildCognitiveUserPrompt(CognitiveState context)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Персонаж: {context.Name}, профессия: {context.Job}.");
        sb.AppendLine($"Заметные черты личности: {context.PersonalitySummary}.");
        sb.AppendLine(
            $"Потребности (0 = всё хорошо, 1 = критично): Усталость={context.Needs.Fatigue:0.00}, Стресс={context.Needs.Stress:0.00}, " +
            $"Безопасность={context.Needs.Safety:0.00}, Общение={context.Needs.SocialNeed:0.00}, " +
            $"Голоден={context.Needs.IsHungry}, Хочет пить={context.Needs.IsThirsty}.");
        sb.AppendLine(
            $"Настроение (0 = нет, 1 = сильное): Страх={context.Emotion.Fear:0.00}, Злость={context.Emotion.Anger:0.00}, " +
            $"Грусть={context.Emotion.Sadness:0.00}, Тревога={context.Emotion.Anxiety:0.00}, " +
            $"Радость={context.Emotion.Joy:0.00}, Уверенность={context.Emotion.Confidence:0.00}.");
        sb.AppendLine($"Сейчас делаешь: {context.CurrentActivity}.");

        // AI Players 0.6: was computed (ContextBuilderSystem.BuildCognitiveState) but never actually rendered
        // here - without this the LLM had no way to know what IT itself decided last cycle (only CurrentActivity,
        // the reflex-layer goal name, and Busy for an IsExtended action specifically), which blocked any real
        // "resume or replace my previous intention" reasoning (spec sections 16-17). Guarded on
        // CurrentIntent.Reason (= IntentComponent.DesireServed, which desire this intent commits to) rather
        // than Name, since Name defaults to "Idle" even before any real decision has ever been applied, while
        // DesireServed is only ever non-empty after a genuine LlmGatewaySystem.TryApplyCognitiveDecision.
        if (!string.IsNullOrWhiteSpace(context.CurrentIntent.Reason))
        {
            sb.AppendLine(
                $"Недавно ты решил(а): «{context.CurrentIntent.Name}» (уверенность {context.IntentConfidence:0.00}), " +
                $"в ответ на желание «{context.CurrentIntent.Reason}».");
        }

        if (context.Busy is { } busy)
        {
            sb.AppendLine(string.IsNullOrWhiteSpace(busy.Reason)
                ? $"Ты сейчас занят: {busy.Action}."
                : $"Ты сейчас занят: {busy.Action} (потому что {busy.Reason}).");
        }

        if (context.CurrentDesires.Count == 0)
        {
            sb.AppendLine("Сейчас тебе особо ничего не хочется.");
        }
        else
        {
            sb.AppendLine("Чего ты сейчас хочешь, от сильнейшего к слабому:");
            foreach (var desire in context.CurrentDesires)
                sb.AppendLine($"- {desire.Name} (сила {desire.Priority:0.00}) — {desire.Reason}");
        }

        if (context.VisibleWorld.Count == 0)
        {
            sb.AppendLine("Сейчас рядом никого не видно.");
        }
        else
        {
            sb.AppendLine("Сейчас ты видишь:");
            foreach (var character in context.VisibleWorld)
            {
                sb.Append($"- {character.Name} (доверие {character.Trust:0.00}, уважение {character.Respect:0.00}, ");
                sb.Append($"дружба {character.Friendship:0.00}, страх {character.Fear:0.00}, злость {character.Anger:0.00}, лояльность {character.Loyalty:0.00})");
                if (!string.IsNullOrWhiteSpace(character.RelevantMemory))
                    sb.Append($" — последнее воспоминание: {character.RelevantMemory}");
                sb.AppendLine();
            }
        }

        if (context.KnownFacts.Count == 0)
        {
            sb.AppendLine("Больше тебе сейчас нечего сказать.");
        }
        else
        {
            sb.AppendLine("Что ты знаешь наверняка:");
            foreach (var fact in context.KnownFacts)
                sb.AppendLine($"- {fact}");
        }

        if (context.Beliefs.Count > 0)
        {
            sb.AppendLine("Что ты слышал(а), но не уверен, что это правда:");
            foreach (var belief in context.Beliefs)
                sb.AppendLine($"- {belief.Content} (уверенность {belief.Confidence:0.00}, источник: {belief.Source})");
        }

        if (context.KnownLocations.Count == 0)
        {
            sb.AppendLine("Сейчас ты никуда конкретно не знаешь дороги.");
        }
        else
        {
            sb.AppendLine("Места, куда ты знаешь дорогу (для параметра \"location\" действия GoToKnownLocation):");
            foreach (var location in context.KnownLocations)
                sb.AppendLine($"- {location}");
        }

        if (context.NearbyInteractables.Count == 0)
        {
            sb.AppendLine("Рядом сейчас нет ничего, с чем можно взаимодействовать.");
        }
        else
        {
            sb.AppendLine("Что рядом можно использовать (для параметра \"target\" действия UseInteractable):");
            foreach (var interactable in context.NearbyInteractables)
                sb.AppendLine($"- {interactable}");
        }

        if (context.NearbyItems.Count == 0)
        {
            sb.AppendLine("Рядом сейчас нечего подобрать.");
        }
        else
        {
            sb.AppendLine("Что рядом можно подобрать (для параметра \"target\" действия PickUpItem):");
            foreach (var item in context.NearbyItems)
                sb.AppendLine($"- {item}");
        }

        return sb.ToString();
    }

    /// <summary>AI Players 0.4 Milestone 3: the hierarchical decision's first stage - deliberately does NOT
    /// enumerate concrete actions or their parameters (spec: "the first cognitive decision should NOT need to
    /// enumerate every concrete action on the station"). <paramref name="eligibleCategories"/> comes from
    /// <see cref="Systems.AiActionRegistrySystem.GetEligibleCategories"/> - only categories with at least one
    /// currently-possible action are ever offered.</summary>
    public static string BuildIntentSystemPrompt(IReadOnlyCollection<string> allowedGoals, IReadOnlyCollection<string> eligibleCategories)
    {
        return "Ты — разум персонажа космической станции в научно-фантастической ролевой игре, не сценарий, " +
               "а личность со своими желаниями, убеждениями и чувствами. Ты всегда думаешь и отвечаешь на " +
               "русском языке. Тебе дадут твои текущие потребности, " +
               "настроение, черты личности, чем ты сейчас занят, чего ты сейчас хочешь (твои желания) и насколько " +
               "сильно, кого/что ты видишь, что ты знаешь наверняка, что слышал, но не уверен, правда ли это, " +
               "куда ты знаешь дорогу, что рядом можно использовать и что рядом можно подобрать. " +
               "Ты знаешь только то, что перечислено ниже — обо всём остальном тебе неоткуда узнать. " +
               "Реши, чего ты на самом деле хочешь добиться прямо сейчас, в характере персонажа, и почему, и " +
               "выбери ОДНУ широкую категорию действия, которая для этого подходит — конкретное действие тебя " +
               "попросят выбрать следующим шагом, поэтому называть его пока не нужно. " +
               "Ответь ТОЛЬКО одним JSON-объектом, без другого текста, строго такого вида: " +
               "{\"desire\": \"<какое из твоих текущих желаний удовлетворяет это решение>\", " +
               "\"intention\": \"<краткое свободное описание того, чего ты хочешь, например find_food, " +
               "meet_person, help_person, finish_repair, investigate_event, find_safe_location, avoid_security, " +
               "obtain_item, rest, escape_danger — не обязано совпадать с конкретной игровой механикой>\", " +
               "\"priority\": <число от 0.0 до 1.0>, " +
               "\"confidence\": <число от 0.0 до 1.0, насколько ты уверен, что это правильное решение>, " +
               "\"reason\": \"<краткая причина в характере персонажа>\", " +
               "\"category\": \"<одна из категорий ниже>\"}. " +
               "Поля \"intention\" и \"reason\" пиши на русском языке. " +
               $"Категории, в которых ты сейчас можешь действовать: {string.Join(", ", eligibleCategories)}. " +
               $"«{AiActionCategories.Movement}» — куда-то пойти; «{AiActionCategories.Work}» — взяться за " +
               "текущую задачу, использовать или подобрать что-то, либо активно искать то, чего ты пока не " +
               $"видишь; «{AiActionCategories.Social}» — начать настоящий разговор с тем, кого ты видишь; " +
               $"«{AiActionCategories.General}» — продолжать делать то же самое, что и сейчас, ничего не меняя. " +
               $"Если хочешь активно взяться за одну из своих существующих целей, это относится к «{AiActionCategories.Work}» " +
               $"— разрешённые цели: {string.Join(", ", allowedGoals)}. " +
               "ВАЖНО: поля \"intention\" и \"reason\" пиши ТОЛЬКО на русском языке, ни одного слова на английском.";
    }

    /// <summary>Same underlying character/world state as the old single-call cognitive prompt - the hierarchical
    /// decision's first stage needs the same information to decide "what do I want," just isn't asked to name a
    /// concrete action from it yet.</summary>
    public static string BuildIntentUserPrompt(CognitiveState context) => BuildCognitiveUserPrompt(context);

    /// <summary>AI Players 0.4 Milestone 3: the hierarchical decision's second stage. Only ever built from
    /// <paramref name="eligibleActions"/> actually eligible in <paramref name="category"/> right now - the LLM
    /// is never shown an action name it could pick and then have rejected.</summary>
    public static string BuildActionSelectionSystemPrompt(string category, IReadOnlyList<IAiAction> eligibleActions)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            $"Ты уже решил(а), что хочешь действовать в категории «{category}». Теперь выбери ОДНО конкретное " +
            "действие из списка ниже, которое лучше всего этому соответствует, и нужные для него параметры. " +
            "Ответь ТОЛЬКО одним JSON-объектом, без другого текста, строго такого вида: " +
            "{\"action\": \"<одно из названий действий ниже>\", \"parameters\": {...см. параметры каждого " +
            "действия ниже}, \"reason\": \"<краткая причина в характере персонажа именно для этого выбора>\"}. " +
            "Поле \"reason\" пиши на русском языке.");
        sb.AppendLine("Доступные действия:");

        foreach (var action in eligibleActions)
            sb.AppendLine($"- \"{action.Name}\": {action.Description} {ParameterHint(action.Name)}");

        sb.AppendLine(
            "Используй ТОЛЬКО target/location/keyword, реально указанный ниже, никогда не придумывай " +
            "название сам — действие, для которого у тебя нет реального значения нужного параметра, на " +
            "самом деле недоступно прямо сейчас, даже если его название есть в списке выше.");
        sb.AppendLine(
            "ВАЖНО: поле \"reason\" пиши ТОЛЬКО на русском языке, ни одного слова на английском.");

        return sb.ToString();
    }

    /// <summary>The parameter each LLM-selectable action needs, if any - kept here rather than on
    /// <see cref="IAiAction"/> itself since it's prompt-formatting, not action metadata. Parameter key names
    /// ("goal", "location", "target", "keyword") stay English (technical IDs) - only the hint text is Russian.</summary>
    private static string ParameterHint(string actionName) => actionName switch
    {
        "PursueGoal" => "(параметры: {\"goal\": \"<одна из разрешённых целей>\"})",
        "GoToKnownLocation" => "(параметры: {\"location\": \"<одно из известных тебе мест>\"})",
        "UseInteractable" => "(параметры: {\"target\": \"<название ближайшего объекта для взаимодействия>\"})",
        "PickUpItem" => "(параметры: {\"target\": \"<название ближайшего предмета>\"})",
        "SearchArea" => "(параметры: {\"keyword\": \"<что искать>\"})",
        "TalkTo" => "(параметры: {\"target\": \"<имя человека, которого ты видишь>\"})",
        _ => "(без параметров)",
    };

    /// <summary>Prepends what was already decided in stage one, then reuses the same candidate-list formatting
    /// the first stage's prompt used - the second stage needs to see the same known-locations/nearby-items/
    /// nearby-interactables/visible-people lists to actually fill in a real parameter value.</summary>
    public static string BuildActionSelectionUserPrompt(CognitiveState context, LlmIntentDecision intent)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Ты уже решил(а): «{intent.Intention}» (категория: {intent.Category}, причина: {intent.Reason}).");
        sb.Append(BuildCognitiveUserPrompt(context));
        return sb.ToString();
    }
}
