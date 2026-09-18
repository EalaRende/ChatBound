using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace ChatBound;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "ChatBound";

    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    private readonly ChatBoundConfiguration configuration;
    private readonly ChatFilterService filter;
    private readonly ServerSyncService server;
    private readonly WindowSystem windows = new("ChatBound");
    private readonly SettingsWindow settings;

    public Plugin()
    {
        configuration = ChatBoundConfiguration.Load(PluginInterface);
        filter = new ChatFilterService(configuration, ObjectTable);
        server = new ServerSyncService(configuration);
        settings = new SettingsWindow(configuration, PluginInterface, server);
        windows.AddWindow(settings);

        CommandManager.AddHandler("/chatbound", new Dalamud.Game.Command.CommandInfo(OnCommand)
        {
            HelpMessage = "Open ChatBound settings, or use 'on'/'off' to toggle Puppy Mode"
        });
        ChatGui.ChatMessage += OnChatMessage;
        PluginInterface.UiBuilder.Draw += OnUiDraw;
        PluginInterface.UiBuilder.OpenMainUi += OpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
    }

    private void OpenMainUi() => settings.IsOpen = true;

    private void OnCommand(string command, string arguments)
    {
        var option = arguments.Trim().ToLowerInvariant();
        if (option.Length == 0)
        {
            OpenMainUi();
            return;
        }

        if (option is not ("on" or "off"))
        {
            ChatGui.Print("ChatBound usage: /chatbound on, /chatbound off, or /chatbound.");
            return;
        }

        if (configuration.ServerRole != "owner")
        {
            ChatGui.Print("ChatBound Puppy Mode can only be controlled by the Owner.");
            return;
        }

        if (!configuration.ServerConnected)
        {
            ChatGui.Print("ChatBound is not connected. The Owner must connect before changing Puppy Mode.");
            return;
        }

        configuration.Enabled = option == "on";
        configuration.Save(PluginInterface);
        var published = server.UpdateProfile(configuration) is not null;
        ChatGui.Print(published
            ? $"ChatBound Puppy Mode {(configuration.Enabled ? "enabled" : "disabled")} for the Pet."
            : "ChatBound could not update the Pet. The change will be retried automatically.");
    }

    private void OpenConfig() => settings.IsOpen = true;

    private void OnUiDraw()
    {
        settings.SynchronizeServer();
        windows.Draw();
    }

    private void OnChatMessage(IHandleableChatMessage message) => filter.TryFilter(message);

    public void Dispose()
    {
        ChatGui.ChatMessage -= OnChatMessage;
        PluginInterface.UiBuilder.Draw -= OnUiDraw;
        PluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfig;
        windows.RemoveAllWindows();
        CommandManager.RemoveHandler("/chatbound");
        server.Dispose();
    }
}
