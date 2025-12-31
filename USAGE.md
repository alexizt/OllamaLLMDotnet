LLM PDF Summarizer — Usage

Usage

  dotnet run --project app -- <file> <model> [apiUrl] [options]

Positional arguments

  <file>    Path to the PDF to summarize (default: ejemplo.pdf)
  <model>   Ollama model name to use (default: llama2)
  [apiUrl]  Optional Ollama API URL (default: http://localhost:11434/api/generate)

Options

  --structured, -s       Return a structured JSON with fields: title, summary, key_points, language, word_count
  --lang, -l <code>      Language code for JSON values (default: en)
  --output, -o <path>    Write output to the specified file (JSON or plain text)
  --help, -h             Show this help message and exit

Examples

  # Structured JSON in Spanish, save to file
  dotnet run --project app -- documento.pdf llama3.1 --structured --lang es --output resumen.json

  # Simple summary (English default)
  dotnet run --project app -- documento.pdf llama3.1

Notes

  - The `--lang` flag controls the language of the JSON values (title/summary/key_points), not the JSON property names.
  - If the model does not return a valid JSON when using `--structured`, the program will retry by asking the model to return only the required JSON structure.