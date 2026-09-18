using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dalamud.Game.Text;

namespace ChatBound;

public sealed class ServerSyncService : IDisposable
{
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly ChatBoundConfiguration configuration;

    public ServerSyncService(ChatBoundConfiguration configuration)
    {
        this.configuration = configuration;
    }

    public bool IsConfigured => Uri.TryCreate(configuration.ServerUrl, UriKind.Absolute, out _);

    public ServerConnection? Connect()
    {
        if (!IsConfigured)
            return null;

        return GetState() is not null
            ? new ServerConnection(configuration.ServerRole)
            : null;
    }

    public PairingState? GetState()
        => Get<PairingState>("api/v1/pairing/state");

    public PairingState? SetOwnerPermission(bool allowed)
        => Put<PairingState>("api/v1/pairing/permission", new { allowOwnerProfileChanges = allowed });

    public PairingState? UpdateProfile(ChatBoundConfiguration source)
        => Put<PairingState>("api/v1/pairing/profile", new
        {
            enabled = source.Enabled,
            activationLocked = source.ActivationLocked,
            unknownWordMode = source.UnknownWords.ToString(),
            words = source.Profiles.TryGetValue(source.ActiveProfile, out var words) ? words.ToArray() : [],
            channels = source.Channels.Select(channel => channel.ToString()).ToArray()
        });

    public PairingState? UpdateDictionary(ChatBoundConfiguration source)
        => Put<PairingState>("api/v1/pairing/dictionary", new
        {
            words = source.Profiles.TryGetValue(source.ActiveProfile, out var words) ? words.ToArray() : []
        });

    public void Dispose() => httpClient.Dispose();

    private T? Get<T>(string path)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Get, path);
            using var response = httpClient.Send(request);
            return response.IsSuccessStatusCode ? response.Content.ReadFromJsonAsync<T>().GetAwaiter().GetResult() : default;
        }
        catch (HttpRequestException) { return default; }
        catch (TaskCanceledException) { return default; }
    }

    private T? Put<T>(string path, object body)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Put, path);
            request.Content = JsonContent.Create(body);
            using var response = httpClient.Send(request);
            return response.IsSuccessStatusCode ? response.Content.ReadFromJsonAsync<T>().GetAwaiter().GetResult() : default;
        }
        catch (HttpRequestException) { return default; }
        catch (TaskCanceledException) { return default; }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(new Uri(configuration.ServerUrl.TrimEnd('/') + "/"), path));
        if (!string.IsNullOrWhiteSpace(configuration.ServerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ServerToken);
        return request;
    }
}

public sealed record ServerConnection(string Role);
public sealed record PairingState(bool Paired, bool Enabled, bool ActivationLocked, bool AllowOwnerProfileChanges, string[] Words, string[] Channels, string UnknownWordMode);
