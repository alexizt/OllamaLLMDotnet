#:package UglyToad.PdfPig@1.7.0-custom-5
#:package System.Text.Json@10.0.1

using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UglyToad.PdfPig; // para PDF
//using DocumentFormat.OpenXml.Packaging; // para DOCX
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;



class Program
{
    static async Task Main(string[] args)
    {
        // 1. Extraer texto de PDF
        string pdfText = ExtractTextFromPdf("ejemplo.pdf");

        // 3. Concatenar
        string inputText = pdfText + "\n";

        // 4. Enviar a Ollama (local)
        using var client = new HttpClient();
        var requestBody = new
        {
            model = "llama2", // o el modelo que tengas instalado en Ollama
            prompt = $"Resume el siguiente documento:\n{inputText}"
        };


        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        var json = JsonSerializer.Serialize(requestBody, options);
        var response = await client.PostAsync(
            "http://localhost:11434/api/generate",
            new StringContent(json, Encoding.UTF8, "application/json")
        );

        string result = await response.Content.ReadAsStringAsync();
        Console.WriteLine("Respuesta del modelo:");
        Console.WriteLine(result);
    }

    static string ExtractTextFromPdf(string filePath)
    {
        using var pdf = PdfDocument.Open(filePath);
        string text = "";
        foreach (var page in pdf.GetPages())
        {
            text += page.Text + "\n";
        }
        return text;
    }
}