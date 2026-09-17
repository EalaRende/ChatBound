using System.Text.RegularExpressions;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace ChatBound;

public sealed class ChatFilterService
{
    private static readonly Regex WordPattern = new(@"[\p{L}\p{N}_']+", RegexOptions.Compiled);
    private readonly ChatBoundConfiguration configuration;

    public ChatFilterService(ChatBoundConfiguration configuration) => this.configuration = configuration;

    public bool TryFilter(IHandleableChatMessage message)
    {
        var channel = message.LogKind;
        if (!configuration.Enabled || !configuration.Channels.Contains(channel))
            return false;

        if (!configuration.Profiles.TryGetValue(configuration.ActiveProfile, out var words))
            return false;

        var text = message.Message.TextValue;
        var allowedCharacters = new bool[text.Length];
        var allowedPattern = new Regex(
            $@"(?<![\p{{L}}\p{{N}}_'])(?:{string.Join("|", words.OrderByDescending(word => word.Length).Select(Regex.Escape))})(?![\p{{L}}\p{{N}}_'])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (Match match in allowedPattern.Matches(text))
            for (var index = match.Index; index < match.Index + match.Length; index++)
                allowedCharacters[index] = true;

        if (configuration.UnknownWords == UnknownWordMode.HideMessage &&
            WordPattern.IsMatch(text) && WordPattern.Matches(text).Cast<Match>().All(match => !allowedCharacters[match.Index]))
        {
            message.PreventOriginal();
            return true;
        }

        var changed = false;
        var result = WordPattern.Replace(text, match =>
        {
            if (allowedCharacters[match.Index])
                return match.Value;

            changed = true;
            return configuration.UnknownWords == UnknownWordMode.Remove ? string.Empty : new string('.', match.Value.Length);
        });

        if (changed)
            message.Message = new SeString(new TextPayload(result));
        return changed;
    }
}
