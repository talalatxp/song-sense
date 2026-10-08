using System.Text.Json;
using SongSense.Core;

namespace SongSense.Infrastructure;

public sealed class OpenAiConnectionProbe(HttpClient http)
{
    private readonly OpenAiResponsesClient client = new(http);
    public async Task<AiUsage?> SendAsync(string model, string key, CancellationToken cancellationToken)
    {
        AiUsage? usage = null;
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
            var response = await client.SendWithUsageAsync(key, payload, cancellationToken, 131_072);
            usage = response.Usage;
            using var result = JsonDocument.Parse(response.Text);
            if (result.RootElement.ValueKind != JsonValueKind.Object || result.RootElement.EnumerateObject().Count() != 1 ||
                result.RootElement.GetProperty("ok").ValueKind != JsonValueKind.True)
                throw new AiException(AiFailure.InvalidResponse);
            return usage;
        }
        catch (AiException error) { throw new AiException(error.Failure is AiFailure.Refused or AiFailure.Incomplete ? AiFailure.InvalidResponse : error.Failure) { Usage = error.Usage ?? usage }; }
        catch (JsonException) { throw new AiException(AiFailure.InvalidResponse) { Usage = usage }; }
        catch (InvalidOperationException) { throw new AiException(AiFailure.InvalidResponse) { Usage = usage }; }
        catch (KeyNotFoundException) { throw new AiException(AiFailure.InvalidResponse) { Usage = usage }; }
    }
}
