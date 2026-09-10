using System.Security.Cryptography;
using System.Text;
using Dsn.Contracts;
using Dsn.View;
using Dsn.Runtime;

namespace Dsn.Host;

internal static class ViewEndpoints
{
    internal static void MapViewEndpoints(this WebApplication web, Settings settings, IRecordReader store,
        ViewService view, ViewDefinitions definitions, IRawArchive? archive, DsnRuntime runtime, PipelineRouter pipeline)
    {
        web.Use(async (context, next) =>
        {
            // Only the empty UI shell is public; every data request still requires authentication.
            if (context.Request.Path == "/" || context.Request.Path == "/demo") { await next(context); return; }
            string? user = null;
            if (settings.Users.Count == 0) user = "local";
            else
            {
                var authorization = context.Request.Headers.Authorization.ToString();
                if (authorization.StartsWith("Bearer ", StringComparison.Ordinal))
                {
                    var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(authorization[7..]));
                    foreach (var p in settings.Users)
                        if (CryptographicOperations.FixedTimeEquals(supplied, SHA256.HashData(Encoding.UTF8.GetBytes(p.Value.Token)))) user = p.Key;
                }
            }
            if (user is null) { context.Response.StatusCode = 401; return; }
            context.Items["user"] = user;
            try { await next(context); }
            catch (Exception e) when (e is ArgumentException or System.Text.Json.JsonException or BadHttpRequestException or FormatException or OverflowException)
            { if (!context.Response.HasStarted) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "Invalid View request" }); } }
            catch (Exception e) when (e is IOException or System.Data.Common.DbException)
            { if (!context.Response.HasStarted) { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { error = "Storage unavailable" }); } }
        });
        string User(HttpContext c) => (string)c.Items["user"]!;
        string[] Allowed(HttpContext c) => settings.Users.Count == 0 ? store.Fields().Keys.Concat(runtime.Workspaces).Concat(["admin"]).Distinct().ToArray() : settings.Users[User(c)].Workspaces;
        bool Authorized(HttpContext c, string[] w) => w.All(Allowed(c).Contains);
        string[] Targets(HttpContext c) => c.Request.Query.TryGetValue("workspaces", out var w) ? w.ToString().Split(',') : Allowed(c);
        long After(HttpContext c) => c.Request.Query.TryGetValue("afterId", out var a) ? long.Parse(a.ToString()) : 0;
        int Limit(HttpContext c) => c.Request.Query.TryGetValue("limit", out var l) ? int.Parse(l.ToString()) : 100;
        web.MapGet("/", () =>
        {
            using var resource = typeof(ViewEndpoints).Assembly.GetManifestResourceStream("Dsn.Host.Presenter.html")!;
            using var reader = new StreamReader(resource);
            return Results.Content(reader.ReadToEnd(), "text/html; charset=utf-8");
        });
        web.MapGet("/demo", () =>
        {
            using var resource = typeof(ViewEndpoints).Assembly.GetManifestResourceStream("Dsn.Host.Demo.html")!;
            using var reader = new StreamReader(resource);
            return Results.Content(reader.ReadToEnd(), "text/html; charset=utf-8");
        });
        web.MapGet("/pipelines", (HttpContext c) => Results.Ok(pipeline.Revisions.Where(p => Allowed(c).Contains(p.Key)).ToDictionary(p => p.Key,
            p => new { revision = p.Value, filters = settings.Pipelines.TryGetValue(p.Key, out var spec) ? spec.Filters.Select(f => f.Type).ToArray() : [],
                sinks = settings.Pipelines.TryGetValue(p.Key, out var config) ? config.Sinks : ["sqlite"] })));
        web.MapGet("/workspaces", (HttpContext c) => Results.Ok(runtime.Workspaces.Where(Allowed(c).Contains)));
        bool RawAllowed(HttpContext c, RawMessage r) => settings.Users.Count == 0 || r.Envelope.Workspace.All(Allowed(c).Contains);
        web.MapGet("/raw", (HttpContext c) =>
        {
            if (archive is null) return Results.StatusCode(409);
            var after = After(c); var limit = Limit(c);
            if (after < 0 || limit is < 1 or > 100) throw new ArgumentException("Invalid raw page");
            // Scan a bounded page. nextAfterId also advances over invisible records.
            var batch = archive.Read(after, limit);
            return Results.Ok(new { items = batch.Where(r => RawAllowed(c, r)), nextAfterId = batch.LastOrDefault()?.Id ?? after });
        });
        web.MapPost("/raw/{id:long}/replay", (HttpContext c, long id, ReplayRequest request) =>
        {
            if (settings.Users.Count != 0 && !settings.Users[User(c)].CanReplay) return Results.StatusCode(403);
            if (archive is null) return Results.StatusCode(409);
            if (id < 1 || request.Workspaces is not { Length: > 0 and <= 32 } || request.Workspaces.Any(w => !ContractNames.Workspace(w)))
                throw new ArgumentException("Invalid replay request");
            if (!Authorized(c, request.Workspaces)) return Results.StatusCode(403);
            if (request.Workspaces.Any(w => !runtime.Workspaces.Contains(w))) throw new ArgumentException("Unknown Workspace");
            var raw = archive.Read(id - 1, 1).SingleOrDefault();
            if (raw is null || raw.Id != id || !RawAllowed(c, raw)) return Results.NotFound();
            var replayId = Guid.NewGuid().ToString("N");
            var message = new InboundMessage(raw.Envelope with { Workspace = request.Workspaces }, raw.Payload)
                { OriginalMessageId = raw.MessageId, ReplayId = replayId };
            return runtime.TrySubmit(message) ? Results.Json(new { replayId, messageId = raw.MessageId, status = "queued" }, statusCode: 202) : Results.StatusCode(503);
        });
        web.MapGet("/health", () => Results.Ok(new { status = "ready" }));
        web.MapGet("/fields", (HttpContext c) => Results.Ok(store.Fields().Where(p => Allowed(c).Contains(p.Key)).ToDictionary()));
        web.MapGet("/view", (HttpContext c) =>
        {
            var w = Targets(c);
            if (!Authorized(c, w)) return Results.StatusCode(403);
            var f = c.Request.Query.TryGetValue("fields", out var fields) ? fields.ToString().Split(',') : new[] { "id", "workspace" };
            return Results.Ok(view.Read(new(w, f), After(c), Limit(c)));
        });
        web.MapGet("/export", (HttpContext c) =>
        {
            var w = Targets(c);
            return !Authorized(c, w) ? Results.StatusCode(403) : Results.Text(store.Export(new(w, After(c), Limit(c))), "application/x-ndjson");
        });
        web.MapGet("/views", (HttpContext c) => Results.Ok(definitions.List(User(c))));
        web.MapPut("/views/{name}", (HttpContext c, string name, ViewDefinition definition) =>
        {
            if (definition.Workspaces is null) throw new ArgumentException("Missing workspaces");
            if (!Authorized(c, definition.Workspaces)) return Results.StatusCode(403);
            view.Validate(definition); definitions.Put(User(c), name, definition); return Results.NoContent();
        });
        web.MapGet("/views/{name}", (HttpContext c, string name) =>
        {
            if (!definitions.List(User(c)).TryGetValue(name, out var definition)) return Results.NotFound();
            return !Authorized(c, definition.Workspaces) ? Results.StatusCode(403) : Results.Ok(view.Read(definition, After(c), Limit(c)));
        });
    }
}

public sealed record ReplayRequest(string[] Workspaces);
