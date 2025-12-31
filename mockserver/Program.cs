using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Mock Ollama running");

app.MapPost("/api/generate", async (HttpRequest req) =>
{
    using var doc = await JsonDocument.ParseAsync(req.Body);
    string model = doc.RootElement.GetProperty("model").GetString() ?? "unknown";
    string prompt = doc.RootElement.GetProperty("prompt").GetString() ?? "";

    // Very small 'fake' summarization: take first 80 chars
    var snippet = prompt.Length <= 80 ? prompt : prompt.Substring(0, 80) + "...";
    var responseObj = new { text = $"Resumen simulado (modelo={model}): {snippet}" };
    return Results.Json(responseObj);
});

app.Run();
