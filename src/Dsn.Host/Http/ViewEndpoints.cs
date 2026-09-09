using System.Security.Cryptography;
using System.Text;
using Dsn.Contracts;
using Dsn.View;

namespace Dsn.Host;

internal static class ViewEndpoints
{
    internal static void MapViewEndpoints(this WebApplication web, Settings settings, IRecordReader store,
        ViewService view, ViewDefinitions definitions)
    {
        web.Use(async (context, next) =>
        {
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
            catch (IOException)
            { if (!context.Response.HasStarted) { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { error = "Storage unavailable" }); } }
        });
        string User(HttpContext c) => (string)c.Items["user"]!;
        string[] Allowed(HttpContext c) => settings.Users.Count == 0 ? store.Fields().Keys.Concat(["echo", "hex", "admin"]).Distinct().ToArray() : settings.Users[User(c)].Workspaces;
        bool Authorized(HttpContext c, string[] w) => w.All(Allowed(c).Contains);
        string[] Targets(HttpContext c) => c.Request.Query.TryGetValue("workspaces", out var w) ? w.ToString().Split(',') : Allowed(c);
        long After(HttpContext c) => c.Request.Query.TryGetValue("afterId", out var a) ? long.Parse(a.ToString()) : 0;
        int Limit(HttpContext c) => c.Request.Query.TryGetValue("limit", out var l) ? int.Parse(l.ToString()) : 100;
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
