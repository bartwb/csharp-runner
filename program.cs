using System.Diagnostics;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Sta Cross-Origin (CORS) verzoeken toe
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(policy => {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();
app.UseCors();

// Ons enige API-endpoint
app.MapPost("/execute", async (HttpContext context) =>
{
    // 1. Lees de C# code uit de body
    string code;
    using (var reader = new StreamReader(context.Request.Body))
    {
        var body = await reader.ReadToEndAsync();
        // Verwacht simpele JSON: { "code": "..." }
        try
        {
            var jsonBody = JsonDocument.Parse(body);
            code = jsonBody.RootElement.GetProperty("code").GetString() ?? "";
        }
        catch (Exception e)
        {
            return Results.BadRequest(new { stdout = "", stderr = $"JSON parse error: {e.Message}" });
        }
    }

    if (string.IsNullOrWhiteSpace(code))
    {
        return Results.BadRequest(new { stdout = "", stderr = "Code body is empty." });
    }

    // 2. Sla de code op in een tijdelijk script-bestand
    var scriptPath = "temp_script.csx";
    await File.WriteAllTextAsync(scriptPath, code);

    // 3. Voer het script uit met 'dotnet script' en vang de output op
    string stdout = "";
    string stderr = "";

    try
    {
        using (var process = new Process())
        {
            process.StartInfo.FileName = "dotnet";
            process.StartInfo.Arguments = $"script \"{scriptPath}\"";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();

            // Lees de output streams
            stdout = await process.StandardOutput.ReadToEndAsync();
            stderr = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();
        }
    }
    catch (Exception e)
    {
        stderr += $"\nProcess execution error: {e.Message}";
    }
    finally
    {
        if (File.Exists(scriptPath))
        {
            File.Delete(scriptPath);
        }
    }

    // 4. Stuur het resultaat terug
    if (!string.IsNullOrEmpty(stderr))
    {
        // Stuur een 400 (Bad Request) als de C# code een fout had
        return Results.BadRequest(new { stdout, stderr });
    }
    
    return Results.Ok(new { stdout, stderr });
});

// Zorg dat de API op de juiste poort draait (8080 is standaard voor containers)
app.Run("http://*:8080");