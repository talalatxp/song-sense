using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SongSense.Core;

namespace SongSense.Infrastructure;

public sealed class OpenAiResponsesClient(HttpClient http)
{
    public async Task<string> SendAsync(string key, object payload, CancellationToken token, int maximumBytes = 1_048_576)
    {
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
            return texts[0];
        }
        catch (HttpRequestException) { throw new AiException(AiFailure.Offline); }
        catch (IOException) { throw new AiException(AiFailure.Offline); }
        catch (JsonException) { throw new AiException(AiFailure.InvalidResponse); }
        catch (InvalidOperationException) { throw new AiException(AiFailure.InvalidResponse); }
        catch (KeyNotFoundException) { throw new AiException(AiFailure.InvalidResponse); }
        finally { request.Headers.Authorization = null; }
    }
}
