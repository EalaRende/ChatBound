using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ChatBoundStore>();
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/v1/sessions", (CreateSessionRequest request, ChatBoundStore store) =>
{
    if (request.Role is not ("owner" or "pet"))
        return Results.BadRequest(new { error = "Role must be owner or pet." });

    return Results.Ok(store.CreateSession(request.Role));
});

app.MapPost("/api/v1/pairing/code", (HttpRequest request, ChatBoundStore store) =>
{
    if (!store.TryAuthenticate(request, out var session) || session.Role != "pet")
        return Results.Unauthorized();

    return Results.Ok(store.CreatePairingCode(session.ClientId));
});

app.MapPost("/api/v1/pairing/accept", (AcceptPairingRequest body, HttpRequest request, ChatBoundStore store) =>
{
    if (!store.TryAuthenticate(request, out var session) || session.Role != "owner")
        return Results.Unauthorized();

    return store.AcceptPairing(session.ClientId, body.Code)
        ? Results.Ok(new { status = "paired" })
        : Results.BadRequest(new { error = "Invalid or expired pairing code." });
});

app.MapGet("/api/v1/pairing/state", (HttpRequest request, ChatBoundStore store) =>
{
    if (!store.TryAuthenticate(request, out var session))
        return Results.Unauthorized();

    return Results.Ok(store.GetState(session.ClientId));
});

app.MapPut("/api/v1/pairing/permission", (PermissionRequest body, HttpRequest request, ChatBoundStore store) =>
{
    if (!store.TryAuthenticate(request, out var session) || session.Role != "pet")
        return Results.Unauthorized();

    return store.SetPermission(session.ClientId, body.AllowOwnerProfileChanges)
        ? Results.Ok(store.GetState(session.ClientId))
        : Results.NotFound();
});

app.MapPut("/api/v1/pairing/profile", (ProfileUpdate body, HttpRequest request, ChatBoundStore store) =>
{
    if (!store.TryAuthenticate(request, out var session) || session.Role != "owner")
        return Results.Unauthorized();

    return store.UpdateProfile(session.ClientId, body)
        ? Results.Ok(store.GetState(session.ClientId))
        : Results.StatusCode(StatusCodes.Status403Forbidden);
});

app.MapPost("/api/v1/pairing/revoke", (HttpRequest request, ChatBoundStore store) =>
{
    if (!store.TryAuthenticate(request, out var session) || session.Role != "pet")
        return Results.Unauthorized();

    return store.TryRevoke(session.ClientId)
        ? Results.Ok(new { status = "revoked" })
        : Results.StatusCode(StatusCodes.Status403Forbidden);
});

app.Run();

record CreateSessionRequest(string Role);
record AcceptPairingRequest(string Code);
record PermissionRequest(bool AllowOwnerProfileChanges);
record ProfileUpdate(bool Enabled, bool ActivationLocked, string[] Words, string[] Channels);
record SessionResponse(string ClientId, string Token, string Role);
record PairingState(bool Paired, bool Enabled, bool ActivationLocked, bool AllowOwnerProfileChanges, string[] Words, string[] Channels, string UnknownWordMode, string? OwnerClientId);

sealed class ChatBoundStore
{
    private static readonly TimeSpan PairingCodeLifetime = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, Session> sessions = new();
    private readonly ConcurrentDictionary<string, Pairing> pairingsByPet = new();
    private readonly ConcurrentDictionary<string, PairingCode> pairingCodes = new();
    private readonly string persistencePath;

    public ChatBoundStore(IHostEnvironment environment)
    {
        persistencePath = Path.Combine(environment.ContentRootPath, "chatbound-server.json");
        Load();
    }

    public SessionResponse CreateSession(string role)
    {
        var clientId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        sessions[clientId] = new Session(clientId, token, role);
        Persist();
        return new SessionResponse(clientId, token, role);
    }

    public object CreatePairingCode(string petClientId)
    {
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        pairingCodes[code] = new PairingCode(petClientId, DateTimeOffset.UtcNow.Add(PairingCodeLifetime));
        return new { code, expiresInSeconds = (int)PairingCodeLifetime.TotalSeconds };
    }

    public bool AcceptPairing(string ownerClientId, string rawCode)
    {
        var code = rawCode.Trim().ToUpperInvariant();
        if (!pairingCodes.TryRemove(code, out var pairingCode) || pairingCode.ExpiresAt <= DateTimeOffset.UtcNow)
            return false;

        pairingsByPet[pairingCode.PetClientId] = new Pairing(pairingCode.PetClientId, ownerClientId);
        Persist();
        return true;
    }

    public PairingState GetState(string clientId)
    {
        var pair = pairingsByPet.Values.FirstOrDefault(item => item.PetClientId == clientId || item.OwnerClientId == clientId);
        return pair is null
            ? new PairingState(false, false, false, false, [], [], "ReplaceWithDots", null)
            : new PairingState(true, pair.Enabled, pair.ActivationLocked, pair.AllowOwnerProfileChanges, pair.Words.ToArray(), pair.Channels.ToArray(), pair.UnknownWordMode, pair.OwnerClientId);
    }

    public bool SetPermission(string petClientId, bool allowOwnerProfileChanges)
    {
        if (!pairingsByPet.TryGetValue(petClientId, out var pair))
            return false;

        pair.AllowOwnerProfileChanges = allowOwnerProfileChanges;
        Persist();
        return true;
    }

    public bool UpdateProfile(string ownerClientId, ProfileUpdate update)
    {
        var pair = pairingsByPet.Values.FirstOrDefault(item => item.OwnerClientId == ownerClientId);
        if (pair is null || !pair.AllowOwnerProfileChanges)
            return false;

        pair.Enabled = update.Enabled;
        pair.ActivationLocked = update.ActivationLocked;
        pair.Words = update.Words.Where(word => !string.IsNullOrWhiteSpace(word)).Select(word => word.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        pair.Channels = update.Channels.Where(channel => !string.IsNullOrWhiteSpace(channel)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Persist();
        return true;
    }

    public bool TryRevoke(string clientId)
    {
        if (pairingsByPet.Values.Any(pair => pair.PetClientId == clientId && pair.ActivationLocked))
            return false;

        foreach (var item in pairingsByPet.Where(item => item.Value.PetClientId == clientId || item.Value.OwnerClientId == clientId).ToArray())
            pairingsByPet.TryRemove(item.Key, out _);
        Persist();
        return true;
    }

    public bool TryAuthenticate(HttpRequest request, out Session session)
    {
        session = null!;
        if (!request.Headers.TryGetValue("Authorization", out var header) || !header.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        var token = header.ToString()[7..];
        var candidate = sessions.Values.FirstOrDefault(item => CryptographicOperations.FixedTimeEquals(Convert.FromHexString(item.Token), Convert.FromHexString(token)));
        if (candidate is null)
            return false;

        session = candidate;
        return true;
    }

    public sealed record Session(string ClientId, string Token, string Role);
    private sealed record PairingCode(string PetClientId, DateTimeOffset ExpiresAt);
    private sealed record PersistedState(List<Session> Sessions, List<PersistedPairing> Pairings);
    private sealed record PersistedPairing(string PetClientId, string OwnerClientId, bool Enabled, bool ActivationLocked, bool AllowOwnerProfileChanges, string[] Words, string[] Channels, string UnknownWordMode);
    private sealed class Pairing
    {
        public Pairing(string petClientId, string ownerClientId) => (PetClientId, OwnerClientId) = (petClientId, ownerClientId);
        public string PetClientId { get; }
        public string OwnerClientId { get; }
        public bool Enabled { get; set; }
        public bool ActivationLocked { get; set; }
        public bool AllowOwnerProfileChanges { get; set; }
        public HashSet<string> Words { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Channels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string UnknownWordMode { get; set; } = "ReplaceWithDots";
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(persistencePath))
                return;

            var state = JsonSerializer.Deserialize<PersistedState>(File.ReadAllText(persistencePath));
            if (state is null)
                return;

            foreach (var session in state.Sessions)
                sessions[session.ClientId] = session;
            foreach (var item in state.Pairings)
            {
                var pair = new Pairing(item.PetClientId, item.OwnerClientId)
                {
                    Enabled = item.Enabled,
                    ActivationLocked = item.ActivationLocked,
                    AllowOwnerProfileChanges = item.AllowOwnerProfileChanges,
                    Words = item.Words.ToHashSet(StringComparer.OrdinalIgnoreCase),
                    Channels = item.Channels.ToHashSet(StringComparer.OrdinalIgnoreCase),
                    UnknownWordMode = item.UnknownWordMode
                };
                pairingsByPet[item.PetClientId] = pair;
            }
        }
        catch (IOException) { }
        catch (JsonException) { }
    }

    private void Persist()
    {
        var state = new PersistedState(
            sessions.Values.ToList(),
            pairingsByPet.Values.Select(pair => new PersistedPairing(
                pair.PetClientId,
                pair.OwnerClientId,
                pair.Enabled,
                pair.ActivationLocked,
                pair.AllowOwnerProfileChanges,
                pair.Words.ToArray(),
                pair.Channels.ToArray(),
                pair.UnknownWordMode)).ToList());
        var temporaryPath = persistencePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, persistencePath, true);
    }
}
