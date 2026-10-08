using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SongSense.Core;

namespace SongSense.Infrastructure;

public sealed class OpenAiResponsesClient(HttpClient http)
{
    public async Task<string> SendAsync(string key, object payload, CancellationToken token, int maximumBytes = 1_048_576)
        => (await SendWithUsageAsync(key, payload, token, maximumBytes)).Text;

    public sealed record Result(string Text, AiUsage? Usage);
    public static AiUsage? ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object ||
            !usage.TryGetProperty("input_tokens", out var input) || input.ValueKind != JsonValueKind.Number || !input.TryGetInt64(out long i) ||
            !usage.TryGetProperty("output_tokens", out var output) || output.ValueKind != JsonValueKind.Number || !output.TryGetInt64(out long o) ||
            !usage.TryGetProperty("total_tokens", out var total) || total.ValueKind != JsonValueKind.Number || !total.TryGetInt64(out long t) ||
            i < 0 || o < 0 || t < 0 || i > long.MaxValue - o || i + o != t) return null;
        return new AiUsage(i, o, t);
    }

    public async Task<Result> SendWithUsageAsync(string key, object payload, CancellationToken token, int maximumBytes = 1_048_576)
    {
        AiUsage? usage = null;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = JsonContent.Create(payload);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
                throw new AiException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AiFailure.Authentication,
                    HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity => AiFailure.IncompatibleModel,
                    HttpStatusCode.TooManyRequests => AiFailure.RateLimited,
                    _ => AiFailure.HttpError
                });
            if (response.Content.Headers.ContentLength > maximumBytes) throw new AiException(AiFailure.InvalidResponse);
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream();
            var chunk = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(chunk, token)) != 0)
            {
                if (buffer.Length + read > maximumBytes) throw new AiException(AiFailure.InvalidResponse);
                buffer.Write(chunk, 0, read);
            }
            using var document = JsonDocument.Parse(buffer.ToArray());
            var root = document.RootElement;
            usage = ReadUsage(root);
            var status = root.GetProperty("status").GetString();
            if (status == "incomplete") throw new AiException(AiFailure.Incomplete);
            if (status != "completed") throw new AiException(AiFailure.InvalidResponse);
            var texts = new List<string>();
            foreach (var item in root.GetProperty("output").EnumerateArray())
            {
                var type = item.GetProperty("type").GetString();
                if (type == "reasoning") continue;
                if (type != "message") throw new AiException(AiFailure.InvalidResponse);
                if (item.GetProperty("status").GetString() != "completed") throw new AiException(AiFailure.Incomplete);
                foreach (var part in item.GetProperty("content").EnumerateArray())
                {
                    var partType = part.GetProperty("type").GetString();
                    if (partType == "refusal") throw new AiException(AiFailure.Refused);
                    if (partType != "output_text") throw new AiException(AiFailure.InvalidResponse);
                    texts.Add(part.GetProperty("text").GetString() ?? "");
                }
            }
            if (texts.Count != 1) throw new AiException(AiFailure.InvalidResponse);
            return new Result(texts[0], usage);
        }
        catch (AiException error) { throw new AiException(error.Failure) { Usage = usage }; }
        catch (HttpRequestException) { throw new AiException(AiFailure.Offline); }
        catch (IOException) { throw new AiException(AiFailure.Offline); }
        catch (JsonException) { throw new AiException(AiFailure.InvalidResponse) { Usage = usage }; }
        catch (InvalidOperationException) { throw new AiException(AiFailure.InvalidResponse) { Usage = usage }; }
        catch (KeyNotFoundException) { throw new AiException(AiFailure.InvalidResponse) { Usage = usage }; }
        finally { request.Headers.Authorization = null; }
    }
}
