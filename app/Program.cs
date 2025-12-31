using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using UglyToad.PdfPig; // para PDF
//using DocumentFormat.OpenXml.Packaging; // para DOCX
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;



class Program
{
    private static readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    

    static async Task<int> Main(string[] args)
    {
        string filePath = args.Length > 0 ? args[0] : "ejemplo.pdf";
        string model = args.Length > 1 ? args[1] : "llama2";
        string apiUrl = args.Length > 2 ? args[2] : "http://localhost:11434/api/generate";

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

        foreach (var chunk in chunks)
        {
            var prompt = $"Resume el siguiente fragmento:\n{chunk}";
            var responseText = await SendPromptAsync(apiUrl, model, prompt);
            if (responseText == null)
            {
                Console.Error.WriteLine("API request failed for a chunk.");
                return 4;
            }
            summaries.Add(responseText.Trim());
        }

        var finalPrompt = $"Combina y resume las siguientes notas en un solo resumen claro y conciso:\n{string.Join("\n\n", summaries)}";
        var finalSummary = await SendPromptAsync(apiUrl, model, finalPrompt);
        if (finalSummary == null)
        {
            Console.Error.WriteLine("Final API request failed.");
            return 5;
        }

        Console.WriteLine("Resumen final:");
        Console.WriteLine(finalSummary.Trim());
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
        try
        {
            response = await httpClient.PostAsync(apiUrl, content);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"HTTP request failed: {ex.Message}");
            return null;
        }

        var responseText = await response.Content.ReadAsStringAsync();
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

    static List<string> SplitIntoChunks(string text, int maxChars)
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
}