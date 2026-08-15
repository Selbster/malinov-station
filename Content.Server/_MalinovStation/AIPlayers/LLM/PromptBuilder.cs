using System.Text;
using Content.Server._MalinovStation.AIPlayers.Components;

namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// Builds the system/user prompt text sent to the LLM from an <see cref="AiContext"/>. Pure string
/// formatting only - no IO, no game state access (that's <see cref="Systems.ContextBuilderSystem"/>'s job).
/// </summary>
public static class PromptBuilder
{
    public static string BuildSystemPrompt()
    {
        return "You are the decision-making mind of an NPC aboard a space station in a sci-fi roleplaying game. " +
               "You will be given the character's personality, needs, current goal, and what they can currently perceive. " +
               "Decide what the character's goal should be right now, in character. " +
               "Respond with ONLY a single JSON object, no other text, of the exact form: " +
               "{\"intent\": \"<one of the allowed intents>\", \"priority\": <number 0.0 to 1.0>, \"reason\": \"<short in-character reason>\"}. " +
               $"Allowed intents: {string.Join(", ", AIGoals.All)}. " +
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
}
