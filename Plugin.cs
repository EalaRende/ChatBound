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

        CommandManager.AddHandler("/chatbound", new Dalamud.Game.Command.CommandInfo((_, _) => OpenMainUi())
        {
            HelpMessage = "Open ChatBound settings"
        });
        ChatGui.ChatMessage += OnChatMessage;
        PluginInterface.UiBuilder.Draw += OnUiDraw;
        PluginInterface.UiBuilder.OpenMainUi += OpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfig;
    }

    private void OpenMainUi() => settings.IsOpen = true;

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
