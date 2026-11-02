using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Saphir.Shared.Contracts;

namespace Saphir.Services;

/// <summary>Erreur renvoyée par le serveur SAPHIR, avec un message lisible par l'agent.</summary>
public sealed class ApiException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>Appels à l'API du serveur SAPHIR avec le jeton de la session Discord.</summary>
public sealed class ApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http;

    public ApiClient(string token) : this(token, null)
    {
    }

    /// <summary>Avec un <paramref name="handler"/> fourni : réponses simulées (outil de captures d'écran).</summary>
    public ApiClient(string token, HttpMessageHandler? handler)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = ClientSettings.Current.HttpBaseUri;
        _http.Timeout = TimeSpan.FromSeconds(20);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
    {
        using var response = await SendRawAsync(() => new HttpRequestMessage(HttpMethod.Get, path), ct);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
               ?? throw new ApiException("Réponse du serveur SAPHIR invalide.");
    }

    public async Task<T> PostAsync<T>(string path, object body, CancellationToken ct = default)
    {
        using var response = await SendRawAsync(() => new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, body.GetType(), options: JsonOptions) }, ct);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
               ?? throw new ApiException("Réponse du serveur SAPHIR invalide.");
    }

    public async Task SendAsync(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        using var response = await SendRawAsync(() =>
        {
            var request = new HttpRequestMessage(method, path);
            if (body is not null)
                request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
            return request;
        }, ct);
    }

    /// <summary>Paramètre d'URL encodé.</summary>
    public static string Q(string value) => Uri.EscapeDataString(value);

    private async Task<HttpResponseMessage> SendRawAsync(Func<HttpRequestMessage> build, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            using var request = build();
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException)
        {
            throw new ApiException("Serveur SAPHIR injoignable. Vérifie la connexion.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ApiException("Le serveur SAPHIR met trop de temps à répondre.");
        }

        if (response.IsSuccessStatusCode)
            return response;

        var status = response.StatusCode;
        string? message = null;
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                        message = error.GetString();
                    else if (document.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                        message = detail.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Corps non JSON : message générique ci-dessous.
        }
        finally
        {
            response.Dispose();
        }

        message ??= status switch
        {
            HttpStatusCode.Unauthorized => "Session expirée : reconnecte-toi à SAPHIR.",
            HttpStatusCode.Forbidden => "Accès non autorisé pour ton rôle Discord.",
            HttpStatusCode.NotFound => "Aucun résultat.",
            _ => $"Le serveur SAPHIR a répondu une erreur ({(int)status})."
        };
        throw new ApiException(message, status);
    }
}

/// <summary>Session ouverte sur le poste : utilisée par les applications pour appeler le serveur.</summary>
public static class SaphirSession
{
    public static ApiClient? Api { get; private set; }
    public static SaphirUserDto? User { get; private set; }

    public static bool IsAdministrator => User?.Levels.Contains(AccessLevels.Administration) == true;

    public static void Open(string token, SaphirUserDto user)
    {
        Api = new ApiClient(token);
        User = user;
    }

    public static void Update(SaphirUserDto user) => User = user;

    /// <summary>Session avec un client d'API fourni (outil de captures d'écran, données d'exemple).</summary>
    public static void Open(ApiClient api, SaphirUserDto user)
    {
        Api = api;
        User = user;
    }

    public static void Close()
    {
        Api = null;
        User = null;
        PendingCallId = null;
    }

    /// <summary>Messages temps réel du serveur, relayés aux applications ouvertes (PEGASE, PTS…).</summary>
    public static event Action<RealtimeMessage>? Realtime;

    public static void Publish(RealtimeMessage message) => Realtime?.Invoke(message);

    /// <summary>Appel 17 décroché à ouvrir dans PEGASE (consommé par la première fenêtre PEGASE qui le lit).</summary>
    public static long? PendingCallId { get; set; }

    public static long? TakePendingCall()
    {
        var id = PendingCallId;
        PendingCallId = null;
        return id;
    }
}
