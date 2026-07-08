using ConcurrencyLab.Api.Demos;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<DemoRegistry>();
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

// --- API -------------------------------------------------------------------

var api = app.MapGroup("/api");

// Catalog: metadata + code for every demo.
api.MapGet("/demos", (DemoRegistry registry) => Results.Ok(registry.List()));

// Category names in display order (for UI grouping).
api.MapGet("/categories", (DemoRegistry registry) => Results.Ok(registry.Categories()));

// A single demo's info.
api.MapGet("/demos/{id}", (string id, DemoRegistry registry) =>
    registry.Find(id) is { } demo ? Results.Ok(demo.Info) : Results.NotFound());

// Run a demo: executes the antipattern and the pattern live and returns real metrics.
api.MapPost("/demos/{id}/run", async (
    string id, RunRequest? request, DemoRegistry registry, CancellationToken ct) =>
{
    var demo = registry.Find(id);
    if (demo is null) return Results.NotFound();
    if (!demo.Info.SupportsRun)
        return Results.BadRequest(new { message = "This demo is illustrative only." });

    var args = new RunArgs(request?.Parameters);
    var which = (request?.Variant ?? "both").ToLowerInvariant();

    // Cap any single run so a bad parameter set can't tie up the server.
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(30));
    var token = timeout.Token;

    // Run sequentially so the two variants don't contend for cores and skew timings.
    RunVariant? anti = null, pattern = null;
    try
    {
        if (which is "both" or "antipattern")
            anti = await demo.RunAntipatternAsync(args, token);
        if (which is "both" or "pattern")
            pattern = await demo.RunPatternAsync(args, token);
    }
    catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
    {
        return Results.Json(new { message = "The run exceeded the 30s time budget." }, statusCode: 408);
    }

    var verdict = demo.BuildVerdict(anti, pattern);
    return Results.Ok(new RunResponse(id, anti, pattern, verdict));
});

app.MapGet("/", () => Results.Redirect("/api/demos"));

app.Run();

/// <summary>Body of POST /api/demos/{id}/run.</summary>
public sealed record RunRequest(string? Variant, Dictionary<string, int>? Parameters);
