using Dalamud.Configuration;
using Dalamud.Plugin;
using Dalamud.Game.Text;
using System.Text;
using System.Security.Cryptography;

namespace ChatBound;

[Serializable]
public sealed class ChatBoundConfiguration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; }
    public string ActiveProfile { get; set; } = "Puppy Basics";
    public string ControllerName { get; set; } = string.Empty;
    public ulong OwnerObjectId { get; set; }
    public string OwnerPairingCode { get; set; } = string.Empty;
    public bool OwnerConsentConfirmed { get; set; }
    public string ServerUrl { get; set; } = "[removed]";
    public string ServerRole { get; set; } = "pet";
    public string ServerClientId { get; set; } = string.Empty;
    public string ServerToken { get; set; } = string.Empty;
    public bool RemotePairingConfirmed { get; set; }
    public bool AllowOwnerProfileChanges { get; set; }
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
        if (!File.Exists(path))
        {
            var bundledPath = Path.Combine(AppContext.BaseDirectory, "Dictionaries", $"{profileName}.txt");
            if (File.Exists(bundledPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.Copy(bundledPath, path, overwrite: false);
            }
        }

        if (!File.Exists(path))
            return;

        Profiles[profileName] = ReadWords(path);
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

    public void GeneratePairingCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        OwnerPairingCode = string.Concat(bytes.ToArray().Select(value => alphabet[value % alphabet.Length]));
        OwnerConsentConfirmed = false;
    }

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
