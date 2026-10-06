using System.Text.Json;
using SongSense.Core;

namespace SongSense.Infrastructure;

public sealed class OpenAiConnectionProbe(HttpClient http)
{
    private readonly OpenAiResponsesClient client = new(http);
    public async Task SendAsync(string model, string key, CancellationToken cancellationToken)
    {
        var payload = new
        {
            model,
            store = false,
            input = "Connection test. Return only an object with ok equal to true.",
            max_output_tokens = 256,
            tools = Array.Empty<object>(),
            text = new { format = new { type = "json_schema", name = "songsense_connection", strict = true,
                schema = new { type = "object", properties = new { ok = new { type = "boolean", @enum = new[] { true } } },
                    required = new[] { "ok" }, additionalProperties = false } } }
        };
        try
        {
            var json = await client.SendAsync(key, payload, cancellationToken, 131_072);
            using var result = JsonDocument.Parse(json);
            if (result.RootElement.ValueKind != JsonValueKind.Object || result.RootElement.EnumerateObject().Count() != 1 ||
                result.RootElement.GetProperty("ok").ValueKind != JsonValueKind.True)
                throw new AiException(AiFailure.InvalidResponse);
        }
        catch (AiException error) when (error.Failure is AiFailure.Refused or AiFailure.Incomplete) { throw new AiException(AiFailure.InvalidResponse); }
        catch (JsonException) { throw new AiException(AiFailure.InvalidResponse); }
        catch (InvalidOperationException) { throw new AiException(AiFailure.InvalidResponse); }
        catch (KeyNotFoundException) { throw new AiException(AiFailure.InvalidResponse); }
    }
}
