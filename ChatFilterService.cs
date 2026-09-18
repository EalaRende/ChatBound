using System.Text.RegularExpressions;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;

namespace ChatBound;

public sealed class ChatFilterService
{
    private static readonly Regex WordPattern = new(@"[\p{L}\p{N}_']+", RegexOptions.Compiled);
    private static readonly Regex ActionPattern = new(@"\*[^\r\n*]*\*", RegexOptions.Compiled);
    private readonly ChatBoundConfiguration configuration;
    private readonly IObjectTable objectTable;

    public ChatFilterService(ChatBoundConfiguration configuration, IObjectTable objectTable)
    {
        this.configuration = configuration;
        this.objectTable = objectTable;
    }

    public bool TryFilter(IHandleableChatMessage message)
    {
        var channel = message.LogKind;
        var localPlayerName = objectTable.LocalPlayer?.Name.TextValue;
        var senderText = message.Sender.TextValue;
        var originalSenderText = message.OriginalSender.ToString();
        var isLocalPlayerMessage = message.SourceKind == XivChatRelationKind.LocalPlayer ||
            (!string.IsNullOrWhiteSpace(localPlayerName) &&
             ((IsSenderMatch(senderText, localPlayerName)) ||
              IsSenderMatch(originalSenderText, localPlayerName)));
        if (isLocalPlayerMessage ||
            configuration.ServerRole != "pet" ||
            !configuration.Enabled ||
            channel == XivChatType.TellOutgoing ||
            !configuration.Channels.Contains(channel))
            return false;

        if (!configuration.Profiles.TryGetValue(configuration.ActiveProfile, out var words))
            return false;

        var text = message.Message.TextValue;
        var allowedCharacters = new bool[text.Length];
        foreach (Match action in ActionPattern.Matches(text))
            for (var index = action.Index; index < action.Index + action.Length; index++)
                allowedCharacters[index] = true;

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

    private static bool IsSenderMatch(string senderText, string localPlayerName)
    {
        return !string.IsNullOrWhiteSpace(senderText) &&
            (senderText.Contains(localPlayerName, StringComparison.OrdinalIgnoreCase) ||
             localPlayerName.Contains(senderText, StringComparison.OrdinalIgnoreCase));
    }
}
