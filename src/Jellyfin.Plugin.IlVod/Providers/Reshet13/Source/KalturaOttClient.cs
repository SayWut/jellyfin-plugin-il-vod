using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.IlVod.Providers.Reshet13.Source;

/// <summary>
/// The only class that knows Kaltura OTT's envelope: the fields every body carries, the anonymous
/// session (<c>ks</c>) they carry after logging in, the <c>result</c> every answer is wrapped in,
/// Kaltura's error codes, and its list paging. Answers a call's <c>result</c> as a detached
/// <see cref="JsonElement"/>, so nothing needs disposing.
/// </summary>
/// <remarks>
/// The session lives in memory only and is never logged or put in a message (N3). It is reused
/// until Kaltura rejects it — a session error (missing, malformed or expired) logs in
/// again and retries the call once — so its stated expiry is not tracked: an expired session costs
/// one rejected call a week, and the same path covers a session Kaltura dropped early.
/// </remarks>
public sealed class KalturaOttClient
{
    private const string ApiVersion = "6.5.0";
    private const string ClientTag = "ReshetWeb";
    private const int PartnerId = 5031;

    private const string LoginService = "ottUser";
    private const string LoginAction = "anonymousLogin";

    private const string UnknownErrorCode = "(no code)";

    /// <summary>
    /// The errors a new session cures: <c>500004</c> no session, <c>500015</c> a malformed one,
    /// <c>500016</c> an expired one. Not every <c>5000xx</c> code is one — <c>500007</c> is
    /// "Asset not found" (seen live on 2026-10-16), which logging in again would not change.
    /// </summary>
    private static readonly HashSet<string> SessionErrorCodes = ["500004", "500015", "500016"];

    private static readonly JsonWriterOptions BodyWriterOptions = new()
    {
        // kSql's quotes stay quotes, as Kaltura's own app sends them.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly KalturaQuery LoginQuery = new(LoginService, LoginAction, "an anonymous session", new JsonObject());

    private readonly IReshet13Http _http;
    private readonly ILogger<KalturaOttClient> _logger;

    /// <summary>One login at a time: callers who find no session, or a rejected one, wait for the
    /// login already under way instead of each starting their own.</summary>
    private readonly SemaphoreSlim _loginLock = new(1, 1);

    private volatile string? _session;

    public KalturaOttClient(IReshet13Http http, ILogger<KalturaOttClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>Asks <paramref name="query"/> under the current session and answers its <c>result</c>.</summary>
    /// <exception cref="Reshet13UnavailableException">Kaltura answered an error, or a session error
    /// again after logging in anew; or the request itself failed.</exception>
    /// <exception cref="FormatException">The answer is not Kaltura's envelope.</exception>
    public async Task<JsonElement> CallAsync(KalturaQuery query, CancellationToken cancellationToken)
    {
        string session = _session ?? await LogInAsync(rejectedSession: null, cancellationToken).ConfigureAwait(false);
        JsonElement result = await PostAsync(query, session, cancellationToken).ConfigureAwait(false);

        if (ErrorCodeOf(result) is string code && IsSessionError(code))
        {
            _logger.LogInformation("reshet13: Kaltura rejected the session ({ErrorCode}); logging in again", code);
            session = await LogInAsync(session, cancellationToken).ConfigureAwait(false);
            result = await PostAsync(query, session, cancellationToken).ConfigureAwait(false);
        }

        return SuccessOrThrow(result, query);
    }

    /// <summary>
    /// Every asset a list query matches, once each, asking page after page (1-based) until
    /// Kaltura's <c>totalCount</c> is reached — or a page brings no asset not already seen, which is
    /// how Kaltura answers a page past the end. Kaltura's orders tie (episodes share an
    /// <c>EpisodeNumber</c> across seasons), so an asset may come back on two pages: it is kept once.
    /// </summary>
    /// <exception cref="FormatException">The walk ended short of <c>totalCount</c>, or took more
    /// than one page with no <c>totalCount</c> to check against. A short list is never an answer:
    /// Jellyfin would delete whatever it lacks.</exception>
    public async Task<IReadOnlyList<JsonElement>> ListAllAsync(Func<int, KalturaQuery> queryForPage, CancellationToken cancellationToken)
    {
        List<JsonElement> assets = [];
        HashSet<string> seenAssetIds = [];
        int pagesWithNewAssets = 0;
        for (int pageIndex = 1; ; pageIndex++)
        {
            KalturaQuery query = queryForPage(pageIndex);
            JsonElement page = await CallAsync(query, cancellationToken).ConfigureAwait(false);
            int added = AddUnseen(assets, seenAssetIds, KalturaJson.Items(page, "objects"));
            pagesWithNewAssets += added > 0 ? 1 : 0;

            long? totalCount = TotalCountOf(page);
            if (added == 0 || assets.Count >= totalCount)
            {
                return CompleteOrThrow(assets, totalCount, pagesWithNewAssets, query);
            }
        }
    }

    /// <summary>
    /// Logs in unless another caller already replaced <paramref name="rejectedSession"/> while this
    /// one waited for the lock — then that newer session is the answer, and no second login is made.
    /// </summary>
    private async Task<string> LogInAsync(string? rejectedSession, CancellationToken cancellationToken)
    {
        await _loginLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session is string current && current != rejectedSession)
            {
                return current;
            }

            JsonElement login = SuccessOrThrow(await PostAsync(LoginQuery, null, cancellationToken).ConfigureAwait(false), LoginQuery);
            _session = KalturaJson.String(login, "ks") is { Length: > 0 } session
                ? session
                : throw new Reshet13UnavailableException($"{LoginQuery.Name} answered no session");
            return _session;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    private async Task<JsonElement> PostAsync(KalturaQuery query, string? session, CancellationToken cancellationToken)
    {
        string url = Reshet13Urls.Service(query.Service, query.Action);
        string answer = await _http.PostJsonAsync(url, BodyOf(query, session), cancellationToken).ConfigureAwait(false);
        return ResultOf(answer, query);
    }

    /// <summary>The fields every call carries, then the session, then the query's own fields —
    /// always in this order, so the same question is always the same bytes.</summary>
    private static string BodyOf(KalturaQuery query, string? session)
    {
        using MemoryStream body = new();
        using (Utf8JsonWriter writer = new(body, BodyWriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("apiVersion", ApiVersion);
            writer.WriteString("clientTag", ClientTag);
            writer.WriteNumber("partnerId", PartnerId);
            if (session is not null)
            {
                writer.WriteString("ks", session);
            }

            WriteFields(writer, query.Fields);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(body.ToArray());
    }

    private static void WriteFields(Utf8JsonWriter writer, JsonObject fields)
    {
        foreach ((string name, JsonNode? value) in fields)
        {
            writer.WritePropertyName(name);
            if (value is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                value.WriteTo(writer);
            }
        }
    }

    /// <exception cref="FormatException">The answer is not JSON, or carries no <c>result</c>
    /// object. The message never quotes the answer itself.</exception>
    private static JsonElement ResultOf(string answer, KalturaQuery query)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(answer);
            return KalturaJson.Object(document.RootElement, "result")?.Clone()
                ?? throw new FormatException($"{query.Name} for {query.Subject} answered no result");
        }
        catch (JsonException)
        {
            throw new FormatException($"{query.Name} for {query.Subject} answered something that is not JSON");
        }
    }

    /// <summary>The <c>result.error.code</c> of a failed call, which Kaltura sends as text;
    /// <c>null</c> when the call succeeded.</summary>
    private static string? ErrorCodeOf(JsonElement result) =>
        KalturaJson.Object(result, "error") is JsonElement error
            ? CodeTextOf(KalturaJson.Property(error, "code"))
            : null;

    private static string CodeTextOf(JsonElement? code) => code switch
    {
        { ValueKind: JsonValueKind.String } text => text.GetString()!,
        { ValueKind: JsonValueKind.Number } number => number.GetRawText(),
        _ => UnknownErrorCode,
    };

    private static bool IsSessionError(string code) => SessionErrorCodes.Contains(code);

    private static JsonElement SuccessOrThrow(JsonElement result, KalturaQuery query) =>
        ErrorCodeOf(result) is string code
            ? throw new Reshet13UnavailableException($"{query.Name} for {query.Subject} failed with Kaltura error {code}")
            : result;

    /// <returns>How many of <paramref name="pageAssets"/> were new. An asset with no id cannot be
    /// told apart from another, so it is always kept — the parser skips it anyway.</returns>
    private static int AddUnseen(List<JsonElement> assets, HashSet<string> seenAssetIds, IEnumerable<JsonElement> pageAssets)
    {
        int countBefore = assets.Count;
        foreach (JsonElement asset in pageAssets)
        {
            if (KalturaJson.Property(asset, "id") is not JsonElement id || seenAssetIds.Add(id.GetRawText()))
            {
                assets.Add(asset);
            }
        }

        return assets.Count - countBefore;
    }

    private static IReadOnlyList<JsonElement> CompleteOrThrow(
        List<JsonElement> assets, long? totalCount, int pagesWithNewAssets, KalturaQuery lastQuery)
    {
        if (totalCount is long total && assets.Count < total)
        {
            throw new FormatException($"{lastQuery.Name} for {lastQuery.Subject} ended the list at {assets.Count} of {total} assets");
        }

        return totalCount is null && pagesWithNewAssets > 1
            ? throw new FormatException($"{lastQuery.Name} for {lastQuery.Subject} spread the list over pages with no totalCount")
            : assets;
    }

    private static long? TotalCountOf(JsonElement page) => KalturaJson.WholeNumber(KalturaJson.Property(page, "totalCount"));
}
