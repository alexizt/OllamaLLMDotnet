@echo off
rem dotnet run --project app --help
dotnet test tests/LLM.Tests/LLM.Tests.csproj -v minimal
dotnet run --project app -- ejemplo2.pdf llama3.1 --structured --lang es