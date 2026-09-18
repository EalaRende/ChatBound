using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;

namespace ChatBound;

public sealed class SettingsWindow : Window
{
    private readonly ChatBoundConfiguration configuration;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ServerSyncService server;
    private string dictionaryText = string.Empty;
    private string profile = string.Empty;
    private string pairingCodeInput = string.Empty;
    private string serverUrl = string.Empty;
    private int serverRoleIndex;

    public SettingsWindow(ChatBoundConfiguration configuration, IDalamudPluginInterface pluginInterface, ServerSyncService server)
        : base("ChatBound | Local Profile")
    {
        this.configuration = configuration;
        this.pluginInterface = pluginInterface;
        this.server = server;
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
        var isPet = configuration.ServerRole == "pet";
        ImGui.Text(isPet ? "Pet server controls" : "Relationship Owner");
        ImGui.TextWrapped(isPet
            ? "The Pet controls pairing and whether the Owner may change the incoming chat profile."
            : "The Owner can change only the Pet's incoming chat activation, dictionary, and selected channels.");

        if (!configuration.RemotePairingConfirmed)
        {
            var role = serverRoleIndex;
            if (ImGui.Combo("Initial role", ref role, "Pet\0Owner\0"))
            {
                serverRoleIndex = role;
                configuration.ServerRole = role == 0 ? "pet" : "owner";
                configuration.ServerClientId = string.Empty;
                configuration.ServerToken = string.Empty;
                configuration.RemotePairingConfirmed = false;
                Save();
            }
            ImGui.Text("Server URL");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##server-url", ref serverUrl, 256))
            {
                configuration.ServerUrl = serverUrl;
                Save();
            }
            if (ImGui.Button("Connect to ChatBound server"))
            {
                var session = server.Connect();
                Save();
                ImGui.SameLine();
                ImGui.Text(session is null ? "Connection failed." : "Connected.");
            }
        }

        if (isPet)
        {
            if (ImGui.Button("Create remote pairing code"))
            {
                var result = server.CreatePairingCode();
                if (result is not null)
                    configuration.OwnerPairingCode = result.Code;
                Save();
            }
            if (!string.IsNullOrEmpty(configuration.OwnerPairingCode))
                ImGui.Text($"Pairing code: {configuration.OwnerPairingCode}");
        }
        else if (!configuration.RemotePairingConfirmed)
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("Remote pairing code", ref pairingCodeInput, 32);
            if (ImGui.Button("Accept remote pairing") && server.AcceptPairing(pairingCodeInput))
            {
                configuration.RemotePairingConfirmed = true;
                Save();
            }
        }

        if (isPet && configuration.RemotePairingConfirmed)
        {
            var allowOwner = configuration.AllowOwnerProfileChanges;
            if (ImGui.Checkbox("Allow Owner to change incoming profile", ref allowOwner))
            {
                configuration.AllowOwnerProfileChanges = allowOwner;
                server.SetOwnerPermission(allowOwner);
                Save();
            }
            if (ImGui.Button("Apply remote incoming profile"))
            {
                var state = server.GetState();
                if (state is not null && state.AllowOwnerProfileChanges)
                {
                    ApplyRemoteState(state);
                    Save();
                }
            }
            if (configuration.ActivationLocked)
                ImGui.BeginDisabled();
            if (ImGui.Button("Revoke remote pairing"))
            {
                server.Revoke();
                configuration.RemotePairingConfirmed = false;
                configuration.AllowOwnerProfileChanges = false;
                configuration.ActivationLocked = false;
                configuration.Enabled = false;
                Save();
            }
            if (configuration.ActivationLocked)
            {
                ImGui.EndDisabled();
                ImGui.TextDisabled("Pairing cannot be revoked while puppy mode is locked.");
            }
        }
        else if (!isPet && configuration.RemotePairingConfirmed)
        {
            ImGui.Text("Paired. Use Profile & dictionary to manage the permitted controls.");
        }

        if (isPet && !configuration.ActivationLocked && ImGui.Button("Emergency disable"))
        {
            configuration.Enabled = false;
            Save();
        }
    }

    private void DrawProfileTab()
    {
        var isOwner = configuration.ServerRole == "owner";
        if (!isOwner)
        {
            ImGui.Text("Active profile");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##profile", profile))
            {
                foreach (var profileName in configuration.Profiles.Keys.Order(StringComparer.OrdinalIgnoreCase))
                {
                    var selected = string.Equals(profileName, configuration.ActiveProfile, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(profileName, selected))
                    {
                        profile = profileName;
                        configuration.LoadDictionary(pluginInterface, profileName);
                        LoadDictionary();
                        Save();
                    }
                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            ImGui.Text("Unknown words");
            var mode = (int)configuration.UnknownWords;
            if (ImGui.Combo("##unknown", ref mode, "Replace with dots\0Remove\0Hide entire message\0"))
            {
                configuration.UnknownWords = (UnknownWordMode)mode;
                Save();
            }

            var enabled = configuration.Enabled;
            if (configuration.ActivationLocked)
                ImGui.BeginDisabled();
            if (ImGui.Checkbox("Activate puppy incoming chat filtering", ref enabled))
            {
                configuration.Enabled = enabled;
                Save();
            }
            if (configuration.ActivationLocked)
            {
                ImGui.EndDisabled();
                ImGui.TextDisabled("Activation is locked by the Owner.");
            }
        }
        else
            ImGui.Text("Owner controls for the paired Pet");

        ImGui.Spacing();
        ImGui.Text("Incoming chat channels");
        ImGui.TextDisabled("Only messages received by this client are filtered.");
        DrawChannelRow(
            (XivChatType.TellIncoming, "Tell"),
            (XivChatType.Say, "Say"),
            (XivChatType.Party, "Party"),
            (XivChatType.Alliance, "Alliance"));
        DrawChannelRow(
            (XivChatType.Yell, "Yell"),
            (XivChatType.Shout, "Shout"),
            (XivChatType.FreeCompany, "Free Company"),
            (XivChatType.Echo, "Echo"));
        ImGui.Text("Linkshells");
        DrawChannelRow(
            (XivChatType.Ls1, "LS1"),
            (XivChatType.Ls2, "LS2"),
            (XivChatType.Ls3, "LS3"),
            (XivChatType.Ls4, "LS4"));
        DrawChannelRow(
            (XivChatType.Ls5, "LS5"),
            (XivChatType.Ls6, "LS6"),
            (XivChatType.Ls7, "LS7"),
            (XivChatType.Ls8, "LS8"));
        ImGui.Text("Cross-world linkshells");
        DrawChannelRow(
            (XivChatType.CrossLinkShell1, "CWL1"),
            (XivChatType.CrossLinkShell2, "CWL2"),
            (XivChatType.CrossLinkShell3, "CWL3"),
            (XivChatType.CrossLinkShell4, "CWL4"));
        DrawChannelRow(
            (XivChatType.CrossLinkShell5, "CWL5"),
            (XivChatType.CrossLinkShell6, "CWL6"),
            (XivChatType.CrossLinkShell7, "CWL7"),
            (XivChatType.CrossLinkShell8, "CWL8"));

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

        if (isOwner)
        {
            var enabled = configuration.Enabled;
            var locked = configuration.ActivationLocked;
            if (ImGui.Checkbox("Activate Pet incoming chat filtering", ref enabled))
            {
                configuration.Enabled = enabled;
                Save();
            }
            if (ImGui.Checkbox("Lock puppy mode activation", ref locked))
            {
                configuration.ActivationLocked = locked;
                Save();
            }
            ImGui.TextDisabled("When locked, the Pet cannot change the activation state.");
            if (configuration.RemotePairingConfirmed && ImGui.Button("Publish permitted controls to Pet"))
            {
                var state = server.UpdateProfile(configuration);
                ImGui.SameLine();
                ImGui.Text(state is null ? "Not permitted or not paired." : "Published.");
            }
        }
    }

    private void DrawChannelRow(params (XivChatType Channel, string Label)[] channels)
    {
        for (var index = 0; index < channels.Length; index++)
        {
            var channel = channels[index];
            if (index > 0)
                ImGui.SameLine();
            var selected = configuration.Channels.Contains(channel.Channel);
            if (ImGui.Checkbox(channel.Label, ref selected))
            {
                if (selected) configuration.Channels.Add(channel.Channel);
                else configuration.Channels.Remove(channel.Channel);
                Save();
            }
        }
    }

    private void LoadState()
    {
        profile = configuration.ActiveProfile;
        serverUrl = configuration.ServerUrl;
        serverRoleIndex = string.Equals(configuration.ServerRole, "owner", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        LoadDictionary();
    }

    private void ApplyRemoteState(PairingState state)
    {
        configuration.Enabled = state.Enabled;
        configuration.ActivationLocked = state.ActivationLocked;
        configuration.UnknownWords = Enum.TryParse<UnknownWordMode>(state.UnknownWordMode, out var mode) ? mode : UnknownWordMode.ReplaceWithDots;
        configuration.Channels = state.Channels
            .Select(channel => Enum.TryParse<XivChatType>(channel, out var parsed) ? parsed : (XivChatType?)null)
            .Where(channel => channel.HasValue)
            .Select(channel => channel!.Value)
            .ToHashSet();
        configuration.Profiles[configuration.ActiveProfile] = state.Words.ToHashSet(StringComparer.OrdinalIgnoreCase);
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
