using Dalamud.Configuration;
using Dalamud.Plugin;
using Dalamud.Game.Text;
using System.Text;

namespace ChatBound;

[Serializable]
public sealed class ChatBoundConfiguration : IPluginConfiguration
{
    public const string ProductionServerUrl = "https://chatbound.app";
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; }
    public string ActiveProfile { get; set; } = "Puppy Basics";
    public string ServerUrl => ProductionServerUrl;
    public string ServerRole { get; set; } = "pet";
    public string ServerToken { get; set; } = string.Empty;
    public bool ServerConnected { get; set; }
    public bool AutoConnect { get; set; } = true;
    public bool AllowOwnerProfileChanges { get; set; }
    public bool ActivationLocked { get; set; }
    public UnknownWordMode UnknownWords { get; set; } = UnknownWordMode.ReplaceWithDots;
    public HashSet<XivChatType> Channels { get; set; } = new()
    {
        XivChatType.Say,
        XivChatType.TellIncoming,
        XivChatType.Party,
        XivChatType.Alliance
    };
    public Dictionary<string, HashSet<string>> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Puppy Basics"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "yes", "no", "good", "wait", "come", "stay", "sit", "down", "food", "water", "pet", "owner"
        }
    };

    public static ChatBoundConfiguration Load(IDalamudPluginInterface pluginInterface)
    {
        var configuration = pluginInterface.GetPluginConfig() as ChatBoundConfiguration ?? new ChatBoundConfiguration();
        configuration.LoadDictionary(pluginInterface, configuration.ActiveProfile);
        return configuration;
    }

    public void LoadDictionary(IDalamudPluginInterface pluginInterface, string profileName)
    {
        var path = GetDictionaryPath(pluginInterface, profileName);
        var bundledPath = Path.Combine(AppContext.BaseDirectory, "Dictionaries", $"{profileName}.txt");
        var bundledWords = File.Exists(bundledPath) ? ReadWords(bundledPath) : [];
        var savedWords = File.Exists(path) ? ReadWords(path) : [];
        if (bundledWords.Count == 0 && savedWords.Count == 0)
            return;

        Profiles[profileName] = savedWords
            .Concat(bundledWords)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        SaveDictionary(pluginInterface, profileName);
        ActiveProfile = profileName;
    }

    public void SaveDictionary(IDalamudPluginInterface pluginInterface, string profileName)
    {
        var path = GetDictionaryPath(pluginInterface, profileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var words = Profiles.TryGetValue(profileName, out var profileWords)
            ? profileWords.Order(StringComparer.OrdinalIgnoreCase)
            : Enumerable.Empty<string>();
        File.WriteAllLines(path, words, Encoding.UTF8);
    }

    public void Save(IDalamudPluginInterface pluginInterface) => pluginInterface.SavePluginConfig(this);

    private string GetDictionaryPath(IDalamudPluginInterface pluginInterface, string profileName)
        => Path.Combine(pluginInterface.ConfigDirectory.FullName, "Dictionaries", $"{profileName}.txt");

    private static HashSet<string> ReadWords(string path)
        => File.ReadLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

public enum UnknownWordMode
{
    ReplaceWithDots,
    Remove,
    HideMessage
}
