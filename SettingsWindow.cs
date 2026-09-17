using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Game.Text;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;

namespace ChatBound;

public sealed class SettingsWindow : Window
{
    private readonly ChatBoundConfiguration configuration;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ITargetManager targetManager;
    private string dictionaryText = string.Empty;
    private string controller = string.Empty;
    private string profile = string.Empty;
    private string pairingCodeInput = string.Empty;

    public SettingsWindow(ChatBoundConfiguration configuration, IDalamudPluginInterface pluginInterface, ITargetManager targetManager)
        : base("ChatBound | Local Profile")
    {
        this.configuration = configuration;
        this.pluginInterface = pluginInterface;
        this.targetManager = targetManager;
        Size = new System.Numerics.Vector2(560, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
        LoadState();
    }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("##chatbound-tabs"))
        {
            if (ImGui.BeginTabItem("Owner"))
            {
                DrawOwnerTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Profile & dictionary"))
            {
                DrawProfileTab();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    private void DrawOwnerTab()
    {
        ImGui.Text("Local owner consent");
        ImGui.TextWrapped("Select a player in FFXIV, add them as the owner, then exchange the displayed code manually. This does not send data or grant remote control.");
        ImGui.Separator();

        var target = targetManager.Target;
        ImGui.Text("Current target");
        ImGui.SameLine();
        ImGui.Text(target?.Name.TextValue ?? "None");
        if (target is IPlayerCharacter && ImGui.Button("Add target as owner"))
        {
            configuration.ControllerName = target.Name.TextValue;
            configuration.OwnerObjectId = target.GameObjectId;
            configuration.GeneratePairingCode();
            pairingCodeInput = string.Empty;
            Save();
        }
        if (target is not IPlayerCharacter)
            ImGui.TextDisabled("Target a player character first.");

        ImGui.Spacing();
        ImGui.Text($"Owner: {(string.IsNullOrWhiteSpace(configuration.ControllerName) ? "Not set" : configuration.ControllerName)}");
        if (!string.IsNullOrEmpty(configuration.OwnerPairingCode))
        {
            ImGui.Text($"Pairing code: {configuration.OwnerPairingCode}");
            ImGui.TextDisabled("Exchange this code out of band, for example in an agreed in-game message.");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("Confirmation code", ref pairingCodeInput, 16);
            if (ImGui.Button("Confirm consent") && string.Equals(pairingCodeInput.Trim(), configuration.OwnerPairingCode, StringComparison.OrdinalIgnoreCase))
            {
                configuration.OwnerConsentConfirmed = true;
                Save();
            }
        }

        ImGui.Text(configuration.OwnerConsentConfirmed ? "Consent confirmed locally." : "Consent not confirmed.");
        var enabled = configuration.Enabled;
        if (!configuration.OwnerConsentConfirmed)
            ImGui.BeginDisabled();
        if (ImGui.Checkbox("Activate profile for this pet", ref enabled))
        {
            configuration.Enabled = enabled;
            Save();
        }
        if (!configuration.OwnerConsentConfirmed)
            ImGui.EndDisabled();
        if (ImGui.Button("Emergency disable"))
        {
            configuration.Enabled = false;
            Save();
        }
    }

    private void DrawProfileTab()
    {
        ImGui.Text("Active profile");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##profile", ref profile, 64))
        {
            if (!configuration.Profiles.ContainsKey(profile))
                configuration.Profiles[profile] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            configuration.LoadDictionary(pluginInterface, profile);
            LoadDictionary();
            Save();
        }

        ImGui.Text("Designated controller label");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##controller", ref controller, 128))
        {
            configuration.ControllerName = controller;
            Save();
        }

        ImGui.Text("Unknown words");
        var mode = (int)configuration.UnknownWords;
        if (ImGui.Combo("##unknown", ref mode, "Replace with dots\0Remove\0Hide entire message\0"))
        {
            configuration.UnknownWords = (UnknownWordMode)mode;
            Save();
        }

        ImGui.Spacing();
        ImGui.Text("Filtered channels");
        DrawChannel(XivChatType.Say, "Say");
        ImGui.SameLine();
        DrawChannel(XivChatType.TellIncoming, "Tell");
        ImGui.SameLine();
        DrawChannel(XivChatType.Party, "Party");
        ImGui.SameLine();
        DrawChannel(XivChatType.Alliance, "Alliance");

        ImGui.Spacing();
        ImGui.Text("Understandable dictionary");
        ImGui.TextDisabled("One word or phrase per line");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextMultiline("##dictionary", ref dictionaryText, 4096, new System.Numerics.Vector2(-1, 180)))
        {
            configuration.Profiles[configuration.ActiveProfile] = dictionaryText
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            configuration.SaveDictionary(pluginInterface, configuration.ActiveProfile);
            Save();
        }
    }

    private void DrawChannel(XivChatType channel, string label)
    {
        var selected = configuration.Channels.Contains(channel);
        if (ImGui.Checkbox(label, ref selected))
        {
            if (selected) configuration.Channels.Add(channel);
            else configuration.Channels.Remove(channel);
            Save();
        }
    }

    private void LoadState()
    {
        profile = configuration.ActiveProfile;
        controller = configuration.ControllerName;
        LoadDictionary();
    }

    private void LoadDictionary()
    {
        dictionaryText = configuration.Profiles.TryGetValue(configuration.ActiveProfile, out var words)
            ? string.Join(Environment.NewLine, words.Order(StringComparer.OrdinalIgnoreCase))
            : string.Empty;
    }

    private void Save() => configuration.Save(pluginInterface);
}
