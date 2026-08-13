using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class ApiService
{
    private readonly HttpClient _client;

    // ── Connection switch ────────────────────────────────────────────────
    // Set this to "L" for localhost, "G" for the global/hosted API.
    // This is the ONLY line you need to touch to change environment.
    private const string ConnectionType = "L"; // "L" = local, "G" = global

    private const string LocalUrl = "https://localhost:7022/";
    private const string GlobalUrl = "https://purplemoonapi.mythitsolutions.co.in/";

    public ApiService()
    {
        if (AppConfig.IsLocal)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    (message, cert, chain, errors) => true
            };

            _client = new HttpClient(handler)
            {
                BaseAddress = new Uri(AppConfig.BaseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
        }
        else
        {
            _client = new HttpClient
            {
                BaseAddress = new Uri(AppConfig.BaseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
        }
    }

    public async Task<string> GetAsync(string endpoint)
    {
        var response = await _client.GetAsync(endpoint);
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadAsStringAsync();
        return null;
    }

    public async Task<string> PostAsync(string endpoint, string json)
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _client.PostAsync(endpoint, content);
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadAsStringAsync();
        return null;
    }

    public async Task<string> PutAsync(string endpoint, string json)
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _client.PutAsync(endpoint, content);
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadAsStringAsync();
        return null;
    }

    public async Task<bool> DeleteAsync(string endpoint)
    {
        var response = await _client.DeleteAsync(endpoint);
        return response.IsSuccessStatusCode;
    }

    public async Task<string> LoginAsync(string password)
    {
        try
        {
            var loginData = new { password = password };
            var json = JsonSerializer.Serialize(loginData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _client.PostAsync("api/UserAuth/login-by-password", content);
            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync();
                return $"ERROR: {(int)response.StatusCode} {response.StatusCode} - {errorText}";
            }
            return await response.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
        {
            return "ERROR: Cannot connect to server. Is the API running?";
        }
        catch (HttpRequestException ex)
        {
            return $"HTTP Error: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Unexpected Error: {ex.Message}";
        }
    }
}