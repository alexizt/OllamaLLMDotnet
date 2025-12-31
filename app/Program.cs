using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using UglyToad.PdfPig; // para PDF
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Linq;
using System.Diagnostics;

public class Program
{
    private static readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    

    static async Task<int> Main(string[] args)
    {
        // If the user asked for help or passed no arguments, show usage and exit BEFORE validating files
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine("Usage: dotnet run --project app -- <file> <model> [apiUrl] [options]");
            Console.WriteLine();
            Console.WriteLine("Positional args:");
            Console.WriteLine("  <file>    Path to the PDF to summarize (default: ejemplo.pdf)");
            Console.WriteLine("  <model>   Ollama model name to use (default: llama3.1)");
            Console.WriteLine("  [apiUrl]  Optional Ollama API URL (default: http://localhost:11434/api/generate)");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --structured, -s       Return a structured JSON with fields title, summary, key_points, language, word_count");
            Console.WriteLine("  --lang, -l <code>      Language code for JSON values (default: en)");
            Console.WriteLine("  --output, -o <path>    Write output to the specified file (JSON or plain text)");
            Console.WriteLine("  --help, -h             Show this help message and exit");
            return 0;
        }

        // Build positional arguments while consuming flag values (so values like the language code aren't treated as positional)
        var consumed = new bool[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--lang" || a == "-l" || a == "--output" || a == "-o")
            {
                consumed[i] = true;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    consumed[i + 1] = true;
                    i++;
                }
                continue;
            }

            if (a.StartsWith("--lang=") || a.StartsWith("-l=") || a.StartsWith("--output=") || a.StartsWith("-o="))
            {
                consumed[i] = true;
                continue;
            }

            if (a.StartsWith("-"))
            {
                // other flags without args (e.g., --structured, --help)
                consumed[i] = true;
                continue;
            }
        }

        var posArgs = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (!consumed[i]) posArgs.Add(args[i]);
        }

        string filePath = posArgs.Count > 0 ? posArgs[0] : "ejemplo.pdf";
        string model = posArgs.Count > 1 ? posArgs[1] : "llama3.1";
        string apiUrl = posArgs.Count > 2 ? posArgs[2] : "http://localhost:11434/api/generate";

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"File not found: {filePath}");
            return 1;
        }

        string pdfText;
        try
        {
            pdfText = ExtractTextFromPdf(filePath);
            if (string.IsNullOrWhiteSpace(pdfText))
            {
                Console.Error.WriteLine("No text extracted from PDF.");
                return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to extract PDF text: {ex.Message}");
            return 3;
        }

        var chunks = SplitIntoChunks(pdfText, 3000);
        var summaries = new List<string>();

        // Measure total processing time (including chunk requests and final summary)
        var sw = Stopwatch.StartNew();

        foreach (var chunk in chunks)
        {
            var prompt = $"Summarize the following passage:\n{chunk}";
            var responseText = await SendPromptAsync(apiUrl, model, prompt);
            if (responseText == null)
            {
                Console.Error.WriteLine("API request failed for a chunk.");
                return 4;
            }
            summaries.Add(responseText.Trim());
        }

        bool structured = args.Contains("--structured") || args.Contains("-s");

        // Language for the JSON *values* (not property names). Default: English (en)
        string lang = "en";
        string? outputFile = null;
        bool helpRequested = args.Contains("--help") || args.Contains("-h");

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--lang" || args[i] == "-l")
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    lang = args[i + 1].ToLowerInvariant();
            }
            else if (args[i].StartsWith("--lang="))
            {
                lang = args[i].Substring("--lang=".Length).ToLowerInvariant();
            }
            else if (args[i].StartsWith("-l="))
            {
                lang = args[i].Substring("-l=".Length).ToLowerInvariant();
            }
            else if (args[i] == "--output" || args[i] == "-o")
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    outputFile = args[i + 1];
            }
            else if (args[i].StartsWith("--output="))
            {
                outputFile = args[i].Substring("--output=".Length);
            }
            else if (args[i].StartsWith("-o="))
            {
                outputFile = args[i].Substring("-o=".Length);
            }
        }

        if (helpRequested)
        {
            Console.WriteLine("Usage: dotnet run --project app -- <file> <model> [apiUrl] [options]");
            Console.WriteLine();
            Console.WriteLine("Positional args:");
            Console.WriteLine("  <file>    Path to the PDF to summarize (default: ejemplo.pdf)");
            Console.WriteLine("  <model>   Ollama model name to use (default: llama3.1)");
            Console.WriteLine("  [apiUrl]  Optional Ollama API URL (default: http://localhost:11434/api/generate)");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --structured, -s       Return a structured JSON with fields title, summary, key_points, language, word_count");
            Console.WriteLine("  --lang, -l <code>      Language code for JSON values (default: en)");
            Console.WriteLine("  --output, -o <path>    Write output to the specified file (JSON or plain text)");
            Console.WriteLine("  --help, -h             Show this help message and exit");
            return 0;
        }

        var finalPrompt = $"Combine and summarize the following notes into a single clear and concise summary. The language for the summary content should be '{lang}'.\n{string.Join("\n\n", summaries)}";
        if (structured)
        {
            finalPrompt += $"\nRespond only with a JSON with the following structure: {{\"title\":\"\", \"summary\":\"\", \"key_points\": [\"\"], \"language\": \"{lang}\", \"word_count\": 0}}. The JSON values (title/summary/key_points) must be in '{lang}'. Do not include additional text.";
        }

        var finalSummary = await SendPromptAsync(apiUrl, model, finalPrompt);
        if (finalSummary == null)
        {
            Console.Error.WriteLine("Final API request failed.");
            return 5;
        }

        if (structured)
        {
            var json = ExtractJsonFromText(finalSummary);
            var structuredObj = ParseStructuredSummary(json);
            if (structuredObj == null)
            {
                var conversionPrompt = $"Extract and return only valid JSON with the following structure: {{\"title\":\"\",\"summary\":\"\",\"key_points\":[\"\"],\"language\":\"{lang}\",\"word_count\":0}} based on the following text. Respond ONLY with the JSON (no further explanation):\n\n" + finalSummary;
                var conversion = await SendPromptAsync(apiUrl, model, conversionPrompt);
                if (!string.IsNullOrEmpty(conversion))
                {
                    var extracted = ExtractJsonFromText(conversion);
                    structuredObj = ParseStructuredSummary(extracted);
                }
            }

            if (structuredObj != null)
            {
                // If the model didn't set the language field, assume the requested language
                if (string.IsNullOrWhiteSpace(structuredObj.Language))
                    structuredObj.Language = lang;
                else if (!string.Equals(structuredObj.Language, lang, StringComparison.OrdinalIgnoreCase))
                    Console.Error.WriteLine($"Warning: model returned Language='{structuredObj.Language}', expected '{lang}'.");

                Console.WriteLine("Structured summary:");
                var opts = new JsonSerializerOptions { WriteIndented = true };
                var serialized = JsonSerializer.Serialize(structuredObj, opts);
                Console.WriteLine(serialized);

                if (!string.IsNullOrEmpty(outputFile))
                {
                    try
                    {
                        File.WriteAllText(outputFile, serialized, Encoding.UTF8);
                        Console.WriteLine($"Saved structured JSON to: {outputFile}");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Failed to write output file '{outputFile}': {ex.Message}");
                    }
                }
            }
            else
            {
                Console.Error.WriteLine("Failed to parse structured output after retry; printing raw response:");
                Console.WriteLine(finalSummary.Trim());
            }
        }
        else
        {
            Console.WriteLine("Final summary:");
            Console.WriteLine(finalSummary.Trim());

            if (!string.IsNullOrEmpty(outputFile))
            {
                try
                {
                    File.WriteAllText(outputFile, finalSummary.Trim(), Encoding.UTF8);
                    Console.WriteLine($"Saved summary to: {outputFile}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to write output file '{outputFile}': {ex.Message}");
                }
            }
        }

        sw.Stop();
        Console.WriteLine($"Total elapsed time: {sw.Elapsed.TotalSeconds:F2} s");

        return 0;
    }

    static async Task<string?> SendPromptAsync(string apiUrl, string model, string prompt)
    {
        var requestBody = new { model, prompt };
        string json;
        using (var ms = new MemoryStream())
        {
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                writer.WriteString("model", model);
                writer.WriteString("prompt", prompt);
                // Request a non-streamed (single) response when supported by the server
                writer.WriteBoolean("stream", false);
                writer.WriteEndObject();
                writer.Flush();
            }
            json = Encoding.UTF8.GetString(ms.ToArray());
        }

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        HttpResponseMessage response;

        // Start a simple console spinner to show progress while waiting for the response
        using var spinnerCts = new CancellationTokenSource();
        var spinnerTask = Task.Run(async () =>
        {
            var frames = new[] { '|', '/', '-', '\\' };
            int idx = 0;
            try
            {
                while (!spinnerCts.Token.IsCancellationRequested)
                {
                    Console.Write($"\rWaiting for model... {frames[idx++ % frames.Length]}");
                    await Task.Delay(150, spinnerCts.Token);
                }
            }
            catch (TaskCanceledException) { }
            finally
            {
                // Clear the spinner line
                try
                {
                    Console.Write('\r');
                    Console.Write(new string(' ', Math.Max(0, Console.WindowWidth - 1)));
                    Console.Write('\r');
                }
                catch
                {
                    // ignore when console size not available
                }
            }
        });

        try
        {
            response = await httpClient.PostAsync(apiUrl, content);
        }
        catch (Exception ex)
        {
            spinnerCts.Cancel();
            await spinnerTask;
            Console.Error.WriteLine($"HTTP request failed: {ex.Message}");
            return null;
        }

        string responseText;
        try
        {
            responseText = await response.Content.ReadAsStringAsync();
        }
        finally
        {
            // stop spinner as soon as content is read
            spinnerCts.Cancel();
            await spinnerTask;
        }

        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"API responded with {response.StatusCode}: {responseText}");
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseText);

            // If root is an array (e.g., multiple JSON fragments), concat their 'response' fields
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("response", out var r) && r.ValueKind == JsonValueKind.String)
                        sb.Append(r.GetString());
                    else
                        sb.Append(el.ToString());
                }
                return sb.ToString();
            }

            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                // Prefer the 'response' field from Ollama-like responses
                if (doc.RootElement.TryGetProperty("response", out var resp))
                {
                    if (resp.ValueKind == JsonValueKind.String) return resp.GetString();
                    return resp.ToString();
                }

                if (doc.RootElement.TryGetProperty("text", out var txt))
                    return txt.GetString();

                // Some Ollama-like responses may have an "output" array or other shape
                if (doc.RootElement.TryGetProperty("output", out var output))
                {
                    if (output.ValueKind == JsonValueKind.Array && output.GetArrayLength() > 0)
                    {
                        var first = output[0];
                        if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("content", out var contentProp))
                            return contentProp.GetString();
                    }
                    return output.ToString();
                }
            }
        }
        catch (JsonException)
        {
            // ignore and fall back to raw
        }

        return responseText;
    }

    static string ExtractTextFromPdf(string filePath)
    {
        using var pdf = PdfDocument.Open(filePath);
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            if (!string.IsNullOrWhiteSpace(page.Text))
                sb.AppendLine(page.Text.Trim());
        }
        return sb.ToString();
    }

    public static List<string> SplitIntoChunks(string text, int maxChars)
    {
        var chunks = new List<string>();
        int pos = 0;
        while (pos < text.Length)
        {
            int len = Math.Min(maxChars, text.Length - pos);
            var segment = text.Substring(pos, len);
            int breakAt = segment.LastIndexOf('\n');
            if (breakAt <= 0) breakAt = segment.LastIndexOf(' ');
            if (breakAt <= 0) breakAt = len;
            var chunk = segment.Substring(0, breakAt).Trim();
            if (!string.IsNullOrEmpty(chunk)) chunks.Add(chunk);
            pos += breakAt;
            while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++;
        }
        return chunks;
    }

    public static string ExtractJsonFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
            return text.Substring(start, end - start + 1);

        start = text.IndexOf('[');
        end = text.LastIndexOf(']');
        if (start >= 0 && end > start)
            return text.Substring(start, end - start + 1);

        return text;
    }

    public static StructuredSummary? ParseStructuredSummary(string json)
    {
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<StructuredSummary>(json, opts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public class StructuredSummary
    {
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public List<string>? Key_Points { get; set; }
        public string? Language { get; set; }
        public int? Word_Count { get; set; }
    }
}