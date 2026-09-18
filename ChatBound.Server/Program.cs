using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ChatBoundStore>();
var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/v1/pairing/state", (HttpRequest request, ChatBoundStore store) =>
{
    return store.TryAuthenticate(request, out var role)
        ? Results.Ok(store.GetState(role))
        : Results.Unauthorized();
});

app.MapPut("/api/v1/pairing/permission", (PermissionRequest body, HttpRequest request, ChatBoundStore store) =>
{
    if (!store.TryAuthenticate(request, out var role) || role != "pet")
        return Results.Unauthorized();

    return store.SetPermission(body.AllowOwnerProfileChanges)
        ? Results.Ok(store.GetState(role))
        : Results.NotFound();
});

app.MapPut("/api/v1/pairing/profile", (ProfileUpdate body, HttpRequest request, ChatBoundStore store) =>
{
    if (!store.TryAuthenticate(request, out var role) || role != "owner")
        return Results.Unauthorized();

    return store.UpdateProfile(body)
        ? Results.Ok(store.GetState(role))
        : Results.StatusCode(StatusCodes.Status403Forbidden);
});

app.Run();

record PermissionRequest(bool AllowOwnerProfileChanges);
record ProfileUpdate(bool Enabled, bool ActivationLocked, string UnknownWordMode, string[] Words, string[] Channels);
record PairingState(bool Paired, bool Enabled, bool ActivationLocked, bool AllowOwnerProfileChanges, string[] Words, string[] Channels, string UnknownWordMode);

sealed class ChatBoundStore
{
    private readonly string ownerToken;
    private readonly string petToken;
    private readonly string persistencePath;
    private readonly Pairing pairing = new();

    public ChatBoundStore(IHostEnvironment environment)
    {
        persistencePath = Path.Combine(environment.ContentRootPath, "chatbound-server.json");
        var localTokens = LoadLocalTokens(FindLocalTokenPath(environment.ContentRootPath));
        ownerToken = GetToken("CHATBOUND_OWNER_TOKEN", localTokens.OwnerToken);
        petToken = GetToken("CHATBOUND_PET_TOKEN", localTokens.PetToken);
        Load();
        Console.WriteLine("ChatBound fixed token pair loaded: owner and pet.");
    }

    private static string GetToken(string variable, string? localToken)
    {
        var environmentToken = Environment.GetEnvironmentVariable(variable);
        var token = string.IsNullOrWhiteSpace(environmentToken) ? localToken : environmentToken;
        return string.IsNullOrWhiteSpace(token)
            ? throw new InvalidOperationException($"Missing {variable}. Set it as an environment variable or in local-secrets/chatbound.tokens.json.")
            : token;
    }

    private static LocalTokens LoadLocalTokens(string path)
    {
        if (!File.Exists(path))
            return new LocalTokens(null, null);

        try
        {
            return JsonSerializer.Deserialize<LocalTokens>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new LocalTokens(null, null);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException($"Unable to read local token file '{path}'.", exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Local token file '{path}' is not valid JSON.", exception);
        }
    }

    private static string FindLocalTokenPath(string contentRoot)
    {
        var candidates = new[]
        {
            Path.Combine(contentRoot, "local-secrets", "chatbound.tokens.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "ChatBound.Server", "local-secrets", "chatbound.tokens.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "local-secrets", "chatbound.tokens.json")
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    public PairingState GetState(string role)
        => new(true, pairing.Enabled, pairing.ActivationLocked, pairing.AllowOwnerProfileChanges,
            pairing.Words.ToArray(), pairing.Channels.ToArray(), pairing.UnknownWordMode);

    public bool SetPermission(bool allowOwnerProfileChanges)
    {
        pairing.AllowOwnerProfileChanges = allowOwnerProfileChanges;
        Persist();
        return true;
    }

    public bool UpdateProfile(ProfileUpdate update)
    {
        if (!pairing.AllowOwnerProfileChanges)
            return false;

        pairing.Enabled = update.Enabled;
        pairing.ActivationLocked = update.ActivationLocked;
        pairing.UnknownWordMode = string.IsNullOrWhiteSpace(update.UnknownWordMode)
            ? "ReplaceWithDots"
            : update.UnknownWordMode;
        pairing.Words = update.Words
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .Select(word => word.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        pairing.Channels = update.Channels
            .Where(channel => !string.IsNullOrWhiteSpace(channel))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Persist();
        return true;
    }

    public bool TryAuthenticate(HttpRequest request, out string role)
    {
        role = string.Empty;
        if (!request.Headers.TryGetValue("Authorization", out var header) ||
            !header.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        var token = header.ToString()[7..];
        if (FixedEquals(ownerToken, token))
            role = "owner";
        else if (FixedEquals(petToken, token))
            role = "pet";
        else
            return false;

        return true;
    }

    private static bool FixedEquals(string expected, string actual)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));

    private sealed record PersistedPairing(bool Enabled, bool ActivationLocked, bool AllowOwnerProfileChanges, string[] Words, string[] Channels, string UnknownWordMode);
    private sealed record LocalTokens(string? OwnerToken, string? PetToken);

    private sealed class Pairing
    {
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

            var state = JsonSerializer.Deserialize<PersistedPairing>(File.ReadAllText(persistencePath));
            if (state is null)
                return;

            pairing.Enabled = state.Enabled;
            pairing.ActivationLocked = state.ActivationLocked;
            pairing.AllowOwnerProfileChanges = state.AllowOwnerProfileChanges;
            pairing.Words = (state.Words ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            pairing.Channels = (state.Channels ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            pairing.UnknownWordMode = string.IsNullOrWhiteSpace(state.UnknownWordMode)
                ? "ReplaceWithDots"
                : state.UnknownWordMode;
        }
        catch (IOException) { }
        catch (JsonException) { }
    }

    private void Persist()
    {
        var state = new PersistedPairing(
            pairing.Enabled,
            pairing.ActivationLocked,
            pairing.AllowOwnerProfileChanges,
            pairing.Words.ToArray(),
            pairing.Channels.ToArray(),
            pairing.UnknownWordMode);
        var temporaryPath = persistencePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, persistencePath, true);
    }
}
