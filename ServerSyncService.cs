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

    public SessionResponse? Connect()
    {
        if (!IsConfigured)
            return null;

        if (!string.IsNullOrWhiteSpace(configuration.ServerToken))
            return GetState() is not null
                ? new SessionResponse(configuration.ServerClientId, configuration.ServerToken, configuration.ServerRole)
                : null;

        var session = Post<SessionResponse>("api/v1/sessions", new { role = configuration.ServerRole });
        if (session is null)
            return null;

        configuration.ServerClientId = session.ClientId;
        configuration.ServerToken = session.Token;
        return session;
    }

    public PairingCodeResponse? CreatePairingCode()
        => Post<PairingCodeResponse>("api/v1/pairing/code", null);

    public bool AcceptPairing(string code)
        => Post<object>("api/v1/pairing/accept", new { code }) is not null;

    public PairingState? GetState()
        => Get<PairingState>("api/v1/pairing/state");

    public PairingState? SetOwnerPermission(bool allowed)
        => Put<PairingState>("api/v1/pairing/permission", new { allowOwnerProfileChanges = allowed });

    public PairingState? UpdateProfile(ChatBoundConfiguration source)
        => Put<PairingState>("api/v1/pairing/profile", new
        {
            enabled = source.Enabled,
            activationLocked = source.ActivationLocked,
            words = source.Profiles.TryGetValue(source.ActiveProfile, out var words) ? words.ToArray() : [],
            channels = source.Channels.Select(channel => channel.ToString()).ToArray()
        });

    public bool Revoke()
        => Post<object>("api/v1/pairing/revoke", null) is not null;

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

    private T? Post<T>(string path, object? body)
    {
        try
        {
            using var request = CreateRequest(HttpMethod.Post, path);
            if (body is not null)
                request.Content = JsonContent.Create(body);
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

public sealed record SessionResponse(string ClientId, string Token, string Role);
public sealed record PairingCodeResponse(string Code, int ExpiresInSeconds);
public sealed record PairingState(bool Paired, bool Enabled, bool ActivationLocked, bool AllowOwnerProfileChanges, string[] Words, string[] Channels, string UnknownWordMode, string? OwnerClientId);
