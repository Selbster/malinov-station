using System.Text;
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

    public static string BuildDialogueSystemPrompt()
    {
        return "You are voicing one character's single line of dialogue in a short, casual exchange between " +
               "two NPCs aboard a space station in a sci-fi roleplaying game. Keep it brief (one sentence), " +
               "in character, consistent with their personality and how they feel about the other character. " +
               "Respond with ONLY a single JSON object, no other text, of the exact form: {\"line\": \"<what they say>\"}.";
    }

    public static string BuildDialogueUserPrompt(DialogueContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Speaker: {context.SpeakerName}. Notable personality traits: {context.SpeakerPersonalitySummary}.");
        sb.AppendLine(
            $"Talking to: {context.PartnerName} (trust {context.Trust:0.00}, respect {context.Respect:0.00}, " +
            $"friendship {context.Friendship:0.00}, fear {context.Fear:0.00}).");

        if (!string.IsNullOrWhiteSpace(context.RelevantMemory))
            sb.AppendLine($"Last thing {context.SpeakerName} remembers about {context.PartnerName}: {context.RelevantMemory}");

        if (!string.IsNullOrWhiteSpace(context.RumorToShare))
        {
            sb.AppendLine(
                $"{context.SpeakerName} wants to tell {context.PartnerName} something important they witnessed: " +
                $"\"{context.RumorToShare}\". Open with that, in character (e.g. \"Did you hear...\"), instead of small talk.");
        }
        else
        {
            sb.AppendLine(context.LinePartnerJustSaid is null
                ? $"{context.SpeakerName} is starting the conversation with a brief greeting or check-in."
                : $"{context.PartnerName} just said: \"{context.LinePartnerJustSaid}\". {context.SpeakerName} should give a short, natural reply.");
        }

        return sb.ToString();
    }

    /// <summary>AI Players 0.3/Navigation Controller: the LLM Cognitive Layer's system prompt.
    /// <paramref name="allowedGoals"/> is still the combined AIGoals/professional-goal whitelist (same list
    /// <see cref="Systems.LlmGatewaySystem.GetAllowedIntents"/> always computed) - it validates the "goal"
    /// parameter of the "PursueGoal" action, not "intention" (which is free-form and unvalidated). NOTE: the
    /// four available actions described below (PursueGoal, ContinueActivity, GoToKnownLocation,
    /// UseInteractable) must stay in sync with <see cref="ActionProposalResolver"/>'s switch - acceptable to
    /// hardcode at this size, revisit once the action catalog grows.</summary>
    public static string BuildCognitiveSystemPrompt(IReadOnlyCollection<string> allowedGoals)
    {
        return "You are the mind of a character aboard a space station in a sci-fi roleplaying game - not a script, " +
               "a person with their own desires, beliefs and feelings. You will be given your current needs, mood, " +
               "personality, what you're currently doing, what you currently want (your desires) and how strongly, " +
               "who/what you can see, what you know for certain, things you've heard but aren't sure are true, " +
               "places you know how to get to, and nearby things you could interact with. " +
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
               "\"action\": \"<one of: PursueGoal, ContinueActivity, GoToKnownLocation, UseInteractable>\", " +
               "\"parameters\": {\"goal\": \"<required only for PursueGoal - one of the allowed goals below>\", " +
               "\"location\": \"<required only for GoToKnownLocation - one of the places you know below>\", " +
               "\"target\": \"<required only for UseInteractable - one of the things you could interact with below>\"}}. " +
               "\"PursueGoal\" actively commits to one of your existing goals right now - use it when one of the " +
               "allowed goals below matches what you want to do. \"GoToKnownLocation\" walks you to a place you " +
               "know how to get to - use it ONLY for a place actually listed below, never one you merely guess " +
               "the name of. \"UseInteractable\" uses a nearby object like a switch - use it ONLY for something " +
               "actually listed below, never one you merely guess the name of. \"ContinueActivity\" means keep " +
               "doing what you're already doing, with no parameters. " +
               $"Allowed goals (for PursueGoal's \"goal\" parameter only): {string.Join(", ", allowedGoals)}. " +
               "Use exactly one of the allowed goals, spelled exactly as given.";
    }

    /// <summary>AI Players 2.0 Milestone 1: the LLM Cognitive Layer's user prompt, from a full <see cref="CognitiveState"/>.</summary>
    public static string BuildCognitiveUserPrompt(CognitiveState context)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Character: {context.Name}, job: {context.Job}.");
        sb.AppendLine($"Notable personality traits: {context.PersonalitySummary}.");
        sb.AppendLine(
            $"Needs (0 = fine, 1 = critical): Fatigue={context.Needs.Fatigue:0.00}, Stress={context.Needs.Stress:0.00}, " +
            $"Safety={context.Needs.Safety:0.00}, SocialNeed={context.Needs.SocialNeed:0.00}, " +
            $"Hungry={context.Needs.IsHungry}, Thirsty={context.Needs.IsThirsty}.");
        sb.AppendLine(
            $"Mood (0 = none, 1 = intense): Fear={context.Emotion.Fear:0.00}, Anger={context.Emotion.Anger:0.00}, " +
            $"Sadness={context.Emotion.Sadness:0.00}, Anxiety={context.Emotion.Anxiety:0.00}, " +
            $"Joy={context.Emotion.Joy:0.00}, Confidence={context.Emotion.Confidence:0.00}.");
        sb.AppendLine($"Currently doing: {context.CurrentActivity}.");

        if (context.CurrentDesires.Count == 0)
        {
            sb.AppendLine("You don't currently want anything in particular.");
        }
        else
        {
            sb.AppendLine("What you currently want, strongest first:");
            foreach (var desire in context.CurrentDesires)
                sb.AppendLine($"- {desire.Name} (strength {desire.Priority:0.00}) - {desire.Reason}");
        }

        if (context.VisibleWorld.Count == 0)
        {
            sb.AppendLine("You do not currently see anyone else nearby.");
        }
        else
        {
            sb.AppendLine("You can currently see:");
            foreach (var character in context.VisibleWorld)
            {
                sb.Append($"- {character.Name} (trust {character.Trust:0.00}, respect {character.Respect:0.00}, ");
                sb.Append($"friendship {character.Friendship:0.00}, fear {character.Fear:0.00}, anger {character.Anger:0.00}, loyalty {character.Loyalty:0.00})");
                if (!string.IsNullOrWhiteSpace(character.RelevantMemory))
                    sb.Append($" - last memory: {character.RelevantMemory}");
                sb.AppendLine();
            }
        }

        if (context.KnownFacts.Count == 0)
        {
            sb.AppendLine("You don't know anything else worth mentioning right now.");
        }
        else
        {
            sb.AppendLine("Things you know for certain:");
            foreach (var fact in context.KnownFacts)
                sb.AppendLine($"- {fact}");
        }

        if (context.Beliefs.Count > 0)
        {
            sb.AppendLine("Things you've heard but aren't sure are true:");
            foreach (var belief in context.Beliefs)
                sb.AppendLine($"- {belief.Content} (confidence {belief.Confidence:0.00}, from {belief.Source})");
        }

        if (context.KnownLocations.Count == 0)
        {
            sb.AppendLine("You don't know how to get to anywhere in particular right now.");
        }
        else
        {
            sb.AppendLine("Places you know how to get to (for GoToKnownLocation's \"location\" parameter):");
            foreach (var location in context.KnownLocations)
                sb.AppendLine($"- {location}");
        }

        if (context.NearbyInteractables.Count == 0)
        {
            sb.AppendLine("There is nothing nearby you could interact with right now.");
        }
        else
        {
            sb.AppendLine("Nearby things you could interact with (for UseInteractable's \"target\" parameter):");
            foreach (var interactable in context.NearbyInteractables)
                sb.AppendLine($"- {interactable}");
        }

        return sb.ToString();
    }
}
