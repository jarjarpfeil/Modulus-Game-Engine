using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ModulusEngine.MCPServer.Bridge;

/// <summary>
/// HTTP bridge to the Modulus Engine API (localhost:9876).
/// All engine communication goes through this class.
/// </summary>
public class EngineBridge
{
    private readonly HttpClient _http;

    public EngineBridge(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Check if the engine is reachable.
    /// </summary>
    public async Task<bool> IsEngineRunningAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("/api/v1/status", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// GET request to the engine API. Returns JSON string.
    /// </summary>
    public async Task<string> GetAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync(path, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return body;
        }
        catch (HttpRequestException ex)
        {
            return JsonSerializer.Serialize(new { error = $"Engine not reachable: {ex.Message}" });
        }
        catch (TaskCanceledException)
        {
            return JsonSerializer.Serialize(new { error = "Request timed out" });
        }
    }

    /// <summary>
    /// POST request to the engine API with JSON body. Returns JSON string.
    /// </summary>
    public async Task<string> PostAsync(string path, object? body = null, CancellationToken ct = default)
    {
        try
        {
            HttpResponseMessage response;
            if (body != null)
            {
                var json = JsonSerializer.Serialize(body);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                response = await _http.PostAsync(path, content, ct);
            }
            else
            {
                response = await _http.PostAsync(path, null, ct);
            }
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            return responseBody;
        }
        catch (HttpRequestException ex)
        {
            return JsonSerializer.Serialize(new { error = $"Engine not reachable: {ex.Message}" });
        }
        catch (TaskCanceledException)
        {
            return JsonSerializer.Serialize(new { error = "Request timed out" });
        }
    }

    /// <summary>
    /// DELETE request to the engine API. Returns JSON string.
    /// </summary>
    public async Task<string> DeleteAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.DeleteAsync(path, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return body;
        }
        catch (HttpRequestException ex)
        {
            return JsonSerializer.Serialize(new { error = $"Engine not reachable: {ex.Message}" });
        }
        catch (TaskCanceledException)
        {
            return JsonSerializer.Serialize(new { error = "Request timed out" });
        }
    }
}
