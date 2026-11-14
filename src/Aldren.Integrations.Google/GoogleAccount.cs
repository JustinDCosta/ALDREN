using Aldren.Security.Secrets;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Drive.v3;
using Google.Apis.Json;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace Aldren.Integrations.Google;

/// <summary>
/// Your Google account (ARCHITECTURE §14.3): your own Desktop OAuth client, the installed-app loopback flow of Google's
/// official library (your browser opens Google's consent page), and tokens kept in Windows Credential Manager instead of the
/// library's plaintext file store. Least privilege: calendar events, Drive read-only, and Drive files ALDREN creates.
/// </summary>
public sealed class GoogleAccount(ISecretStore secrets)
{
    private const string ClientIdName = "google-client-id";
    private const string ClientSecretName = "google-client-secret";
    private const string User = "aldren";

    private static readonly string[] Scopes =
    [
        CalendarService.Scope.CalendarEvents,
        CalendarService.Scope.CalendarReadonly,
        DriveService.Scope.DriveReadonly,
        DriveService.Scope.DriveFile,
    ];

    private UserCredential? _credential;

    public bool HasClient => secrets.Get(ClientIdName) is not null && secrets.Get(ClientSecretName) is not null;

    public bool SignedIn => _credential is not null || new CredentialDataStore(secrets).HasToken(User);

    public void SetClient(string clientId, string clientSecret)
    {
        if (!clientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal) || clientSecret.Trim().Length < 10)
        {
            throw new InvalidOperationException("That doesn't look like a Desktop OAuth client ID and secret from Google Cloud.");
        }

        secrets.Set(ClientIdName, clientId.Trim());
        secrets.Set(ClientSecretName, clientSecret.Trim());
    }

    /// <summary>Opens Google's consent page in your browser the first time; afterwards the stored token is refreshed silently.</summary>
    public async Task<UserCredential> CredentialAsync(CancellationToken ct)
    {
        if (_credential is not null)
        {
            return _credential;
        }

        var id = secrets.Get(ClientIdName);
        var secret = secrets.Get(ClientSecretName);
        if (id is null || secret is null)
        {
            throw new InvalidOperationException("Google isn't set up: add your OAuth client in Settings > Google account (see docs/guides/google-oauth.md).");
        }

        _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            new ClientSecrets { ClientId = id, ClientSecret = secret }, Scopes, User, ct, new CredentialDataStore(secrets));
        return _credential;
    }

    public async Task<CalendarService> CalendarAsync(CancellationToken ct) =>
        new(new BaseClientService.Initializer { HttpClientInitializer = await CredentialAsync(ct), ApplicationName = "ALDREN" });

    public async Task<DriveService> DriveAsync(CancellationToken ct) =>
        new(new BaseClientService.Initializer { HttpClientInitializer = await CredentialAsync(ct), ApplicationName = "ALDREN" });

    /// <summary>Revokes the token at Google and wipes it here (ARCHITECTURE §14.3).</summary>
    public async Task DisconnectAsync(CancellationToken ct)
    {
        try
        {
            if (_credential is not null || SignedIn)
            {
                await (await CredentialAsync(ct)).RevokeTokenAsync(ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Already revoked or offline: the local wipe still happens.
        }

        _credential = null;
        await new CredentialDataStore(secrets).ClearAsync();
    }
}

/// <summary>Google's token store, in Credential Manager (one entry per key, under the 2,560-byte limit).</summary>
internal sealed class CredentialDataStore(ISecretStore secrets) : IDataStore
{
    private const string Prefix = "google-token-";

    public Task StoreAsync<T>(string key, T value)
    {
        secrets.Set(Prefix + Name<T>(key), NewtonsoftJsonSerializer.Instance.Serialize(value!));
        return Task.CompletedTask;
    }

    public Task DeleteAsync<T>(string key)
    {
        secrets.Delete(Prefix + Name<T>(key));
        return Task.CompletedTask;
    }

    public Task<T> GetAsync<T>(string key)
    {
        var json = secrets.Get(Prefix + Name<T>(key));
        return Task.FromResult(json is null ? default! : NewtonsoftJsonSerializer.Instance.Deserialize<T>(json));
    }

    public Task ClearAsync()
    {
        secrets.Delete(Prefix + Name<global::Google.Apis.Auth.OAuth2.Responses.TokenResponse>("aldren"));
        return Task.CompletedTask;
    }

    public bool HasToken(string user) => secrets.Get(Prefix + Name<global::Google.Apis.Auth.OAuth2.Responses.TokenResponse>(user)) is not null;

    private static string Name<T>(string key) => $"{typeof(T).Name}-{key}".ToLowerInvariant();
}
