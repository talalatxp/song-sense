using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SongSense.Core;

namespace SongSense.Infrastructure;

public sealed class ChatGptInsightProvider(ChatGptConnection connection, HttpClient http, ISettingsStore? settings = null, IRequestBudget? budget = null) : IInsightProvider
{
    public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken token)
        => GenerateAsync(track, lyrics, OutputLanguages.Default, token);
    public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, string targetLanguage, CancellationToken token)
    {
        var instructions = OpenAiInsightProvider.InstructionsFor(targetLanguage);
        if (lyrics.IsInstrumental || lyrics.TrackRevision != track.Revision || string.IsNullOrWhiteSpace(lyrics.Text) || lyrics.Text.Length > 20_000)
            throw new AiException(AiFailure.InvalidLyrics);
        return connection.RunIncludedRequestAsync(async (model, accessToken, current) =>
        {
            if (settings is not null && budget is not null)
            {
                var configuration = await settings.ReadSettingsAsync(current);
                var date = DateOnly.FromDateTime(DateTime.Now);
                if (configuration.DailyRequestLimit is < 1 or > 100) throw new AiException(AiFailure.InvalidSettings);
                if (await settings.ReadRequestCountAsync(date, current) >= configuration.DailyRequestLimit || !await budget.TryReserveRequestAsync(date, current))
                    throw new AiException(AiFailure.DailyLimit);
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(current);
            deadline.CancelAfter(TimeSpan.FromMinutes(3));
            var data = JsonSerializer.Serialize(new { lyrics_data = InsightValidation.Lines(lyrics.Text).Select(line => new { id = line.Id, text = line.Text }) });
            var payload = new { model, store = false, stream = true, instructions,
                input = new[] { new { role = "user", content = data } },
                text = new { format = new { type = "json_schema", name = OpenAiInsightProvider.PromptVersion, strict = true, schema = OpenAiInsightProvider.Schema } } };
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Content = JsonContent.Create(payload);
            try
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (!response.IsSuccessStatusCode) throw await ReadHttpErrorAsync(response, deadline.Token);
                await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
                var result = await ReadStreamAsync(stream, deadline.Token);
                try { return InsightValidation.Parse(track.Revision, lyrics.Text, result.Text, targetLanguage) with {
                    Model = model, PromptVersion = OpenAiInsightProvider.PromptVersion, Usage = result.Usage }; }
                catch (AiException error) { throw new AiException(error.Failure) { Usage = result.Usage, InferenceStage = InferenceStage.InsightValidation }; }
            }
            catch (OperationCanceledException) when (!current.IsCancellationRequested) { throw new AiException(AiFailure.Timeout); }
            catch (Exception error) when (error is HttpRequestException or IOException) { throw new AiException(AiFailure.Offline); }
            finally { request.Headers.Authorization = null; }
        }, token);
    }

    private static async Task<AiException> ReadHttpErrorAsync(HttpResponseMessage response, CancellationToken token)
    {
        var code = InferenceCode.None;
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(chunk, token)) > 0)
        {
            if (buffer.Length + count > 65_536) { code = InferenceCode.Other; break; }
            buffer.Write(chunk, 0, count);
        }
        if (code != InferenceCode.Other)
        {
            try { using var document = JsonDocument.Parse(buffer.ToArray()); code = ReadCode(document.RootElement); }
            catch (Exception error) when (error is JsonException or InvalidOperationException) { code = InferenceCode.Other; }
        }
        return new AiException(response.StatusCode switch {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AiFailure.Authentication,
                    HttpStatusCode.TooManyRequests => AiFailure.RateLimited,
                    HttpStatusCode.BadRequest or HttpStatusCode.NotFound => AiFailure.IncompatibleModel,
                    _ => AiFailure.HttpError }) { HttpStatus = (int)response.StatusCode, InferenceStage = InferenceStage.Http, InferenceCode = code };
    }

    // Consume terminal events immediately; an SSE connection need not close after completion.
    public static async Task<OpenAiResponsesClient.Result> ReadStreamAsync(Stream stream, CancellationToken token)
    {
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[4096]; var chars = new char[8192];
        var pending = new StringBuilder(); var data = new StringBuilder();
        var events = new CompletedItems();
        bool? jsonBody = null;
        int total = 0, count;
        while ((count = await stream.ReadAsync(bytes, token)) > 0)
        {
            total += count;
            if (total > 4 * 1024 * 1024) throw new AiException(AiFailure.InvalidResponse) { InferenceStage = InferenceStage.Stream };
            pending.Append(chars, 0, decoder.GetChars(bytes, 0, count, chars, 0));
            if (jsonBody is null)
            {
                var start = pending.ToString().TrimStart(' ', '\t', '\r', '\n', '\uFEFF');
                if (start.Length == 0) continue;
                jsonBody = start[0] == '{';
            }
            if (jsonBody == true) continue;
            int end;
            while ((end = IndexOfNewline(pending)) >= 0)
            {
                var line = pending.ToString(0, end).TrimEnd('\r'); pending.Remove(0, end + 1);
                if (line.Length == 0)
                {
                    var result = ReadEvent(data.ToString(), events); data.Clear();
                    if (result is not null) return result;
                }
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                { if (data.Length > 0) data.Append('\n'); data.Append(line[5..].TrimStart(' ')); }
            }
        }
        if (jsonBody == true) return ReadJsonBody(pending.ToString().TrimStart('\uFEFF'));
        throw new AiException(AiFailure.Incomplete) { InferenceStage = InferenceStage.Stream };
    }

    // Validate the body rather than trusting a missing or inaccurate media-type header.
    private static OpenAiResponsesClient.Result ReadJsonBody(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if ((root.TryGetProperty("error", out var errorBody) && errorBody.ValueKind != JsonValueKind.Null) || root.TryGetProperty("detail", out _))
            {
                var code = ReadCode(root);
                throw new AiException(FailureForCode(code, AiFailure.HttpError)) { InferenceStage = InferenceStage.Http, HttpStatus = 200, InferenceCode = code };
            }
            try { return OpenAiResponsesClient.ReadCompletedResponse(root); }
            catch (AiException error) { throw new AiException(error.Failure) { Usage = error.Usage, InferenceStage = InferenceStage.CompletedResponse, HttpStatus = 200, ResponseShape = error.ResponseShape }; }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
        { throw new AiException(AiFailure.InvalidResponse) { InferenceStage = InferenceStage.Http, HttpStatus = 200 }; }
    }

    private static AiFailure FailureForCode(InferenceCode code, AiFailure fallback) => code switch {
        InferenceCode.UsageLimitExceeded => AiFailure.RateLimited,
        InferenceCode.UserNotEligible or InferenceCode.ScopeNotAuthorized => AiFailure.Authentication,
        InferenceCode.UnsupportedCapability => AiFailure.IncompatibleModel,
        InferenceCode.UsageUnavailable or InferenceCode.RouteNotSupported => AiFailure.HttpError,
        _ => fallback };

    private static int IndexOfNewline(StringBuilder text)
    { for (int i = 0; i < text.Length; i++) if (text[i] == '\n') return i; return -1; }

    private static InferenceCode ReadCode(JsonElement root)
    {
        var error = root.TryGetProperty("error", out var nested) && nested.ValueKind == JsonValueKind.Object ? nested : root;
        if (!error.TryGetProperty("code", out var value) || value.ValueKind != JsonValueKind.String) return InferenceCode.None;
        return value.GetString() switch {
            "subscription_sharing_usage_limit_exceeded" => InferenceCode.UsageLimitExceeded,
            "subscription_sharing_user_not_eligible" => InferenceCode.UserNotEligible,
            "chatpass_v2_scope_not_authorized" or "invalid_authorization_context" => InferenceCode.ScopeNotAuthorized,
            "subscription_sharing_unsupported_capability" => InferenceCode.UnsupportedCapability,
            "subscription_sharing_route_not_supported" => InferenceCode.RouteNotSupported,
            "subscription_sharing_usage_unavailable" or "subscription_sharing_user_unavailable" => InferenceCode.UsageUnavailable,
            _ => InferenceCode.Other };
    }

    private static OpenAiResponsesClient.Result? ReadEvent(string json, CompletedItems events)
    {
        if (json.Length == 0 || json == "[DONE]") return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            string? type = root.GetProperty("type").GetString();
            if (type == "response.output_item.done")
            {
                events.Add(root.GetProperty("output_index").GetInt32(), root.GetProperty("item"));
                return null;
            }
            if (type is "response.output_text.done" or "response.content_part.done")
            {
                var part = type == "response.output_text.done"
                    ? JsonSerializer.SerializeToElement(new { type = "output_text", text = root.GetProperty("text").GetString() })
                    : root.GetProperty("part");
                events.AddPart(root.GetProperty("output_index").GetInt32(), root.GetProperty("content_index").GetInt32(), part);
                return null;
            }
            if (type is "response.failed" or "response.incomplete" or "error")
            {
                var response = root.TryGetProperty("response", out var value) ? value : root;
                var code = ReadCode(response);
                throw new AiException(FailureForCode(code, type == "response.incomplete" ? AiFailure.Incomplete : AiFailure.HttpError)) {
                    Usage = OpenAiResponsesClient.ReadUsage(response), InferenceStage = InferenceStage.Stream, InferenceCode = code };
            }
            if (type != "response.completed") return null;
            var terminalUsage = OpenAiResponsesClient.ReadUsage(root.GetProperty("response"));
            try
            {
                var response = root.GetProperty("response");
                try { return OpenAiResponsesClient.ReadCompletedResponse(response); }
                catch (AiException error) when (error.ResponseShape == ResponseShape.EmptyOutput && events.HasItems)
                {
                    // Some streams carry completed items separately and omit them in the terminal envelope.
                    var assembled = JsonSerializer.SerializeToElement(new { status = "completed", output = events.Items,
                        usage = response.TryGetProperty("usage", out var usage) ? usage : (JsonElement?)null });
                    return OpenAiResponsesClient.ReadCompletedResponse(assembled);
                }
            }
            catch (AiException error) { throw new AiException(error.Failure) { Usage = error.Usage ?? terminalUsage, InferenceStage = InferenceStage.CompletedResponse, ResponseShape = error.ResponseShape }; }
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new AiException(AiFailure.InvalidResponse) { InferenceStage = InferenceStage.Stream }; }
    }

    public static OpenAiResponsesClient.Result ParseStream(string stream)
    {
        var events = new CompletedItems();
        OpenAiResponsesClient.Result? completed = null;
        var data = new StringBuilder();
        try
        {
            using var reader = new StringReader(stream);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Length == 0) { Process(); continue; }
                if (line.StartsWith("data:", StringComparison.Ordinal))
                { if (data.Length > 0) data.Append('\n'); data.Append(line[5..].TrimStart(' ')); }
            }
            Process();
            return completed ?? throw new AiException(AiFailure.Incomplete);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new AiException(AiFailure.InvalidResponse); }

        void Process()
        {
            if (data.Length == 0) return;
            string json = data.ToString(); data.Clear();
            var result = ReadEvent(json, events);
            if (result is null) return;
            if (completed is not null) throw new AiException(AiFailure.InvalidResponse);
            completed = result;
        }
    }

    private sealed class CompletedItems
    {
        private readonly SortedDictionary<int, JsonElement> items = [];
        private readonly SortedDictionary<int, SortedDictionary<int, JsonElement>> parts = [];
        public bool HasItems => items.Count > 0 || parts.Count > 0;
        public IEnumerable<JsonElement> Items => items.Keys.Concat(parts.Keys).Distinct().Order().Select(BuildItem);
        private JsonElement BuildItem(int index)
        {
            if (items.TryGetValue(index, out var item)) return item;
            var content = parts[index];
            if (!content.Keys.SequenceEqual(Enumerable.Range(0, content.Count)))
                throw new AiException(AiFailure.Incomplete) { InferenceStage = InferenceStage.Stream };
            return JsonSerializer.SerializeToElement(new { type = "message", status = "completed", content = content.Values });
        }
        public void Add(int index, JsonElement item)
        {
            if (index < 0 || index > 1024 || items.ContainsKey(index))
                throw new AiException(AiFailure.InvalidResponse) { InferenceStage = InferenceStage.Stream };
            items.Add(index, item.Clone());
        }
        public void AddPart(int outputIndex, int contentIndex, JsonElement part)
        {
            var type = part.GetProperty("type").GetString();
            if (type == "reasoning_text") return;
            if (outputIndex is < 0 or > 1024 || contentIndex is < 0 or > 1024 || type is not ("output_text" or "refusal"))
                throw new AiException(AiFailure.InvalidResponse) { InferenceStage = InferenceStage.Stream };
            var canonical = type == "refusal" ? JsonSerializer.SerializeToElement(new { type }) :
                JsonSerializer.SerializeToElement(new { type, text = part.GetProperty("text").GetString() });
            if (!parts.TryGetValue(outputIndex, out var content)) parts.Add(outputIndex, content = []);
            if (content.TryGetValue(contentIndex, out var existing))
            {
                if (existing.GetRawText() != canonical.GetRawText())
                    throw new AiException(AiFailure.InvalidResponse) { InferenceStage = InferenceStage.Stream };
                return;
            }
            content.Add(contentIndex, canonical);
        }
    }
}
