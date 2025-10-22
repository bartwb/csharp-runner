using System.Diagnostics;
using System.Text; // Nodig voor StringBuilder
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options => {
    options.AddDefaultPolicy(policy => {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();
app.UseCors();

app.MapGet("/health", () => {
    Console.WriteLine("Health check requested, returning OK.");
    return Results.Ok(new { status = "healthy" });
});

app.MapPost("/execute", async (HttpContext context) =>
{
    // *** NIEUW: Lijst om logs op te vangen ***
    var debugLog = new List<string>();
    // ***************************************

    string code;
    using (var reader = new StreamReader(context.Request.Body)) {
        var body = await reader.ReadToEndAsync();
        try {
            var jsonBody = JsonDocument.Parse(body);
            code = jsonBody.RootElement.GetProperty("code").GetString() ?? "";
            debugLog.Add($"Code received (length: {code.Length})."); // Log
        } catch (Exception e) {
            debugLog.Add($"JSON parse error: {e.Message}"); // Log
            // Stuur log mee in BadRequest
            return Results.BadRequest(new { stdout = "", stderr = $"JSON parse error: {e.Message}", debugLog });
        }
    }

    if (string.IsNullOrWhiteSpace(code)) {
         debugLog.Add("Code body is empty."); // Log
        return Results.BadRequest(new { stdout = "", stderr = "Code body is empty.", debugLog });
    }

    var scriptPath = "temp_script.csx";
    await File.WriteAllTextAsync(scriptPath, code);
    debugLog.Add($"Code saved to {scriptPath}."); // Log

    string stdout = "";
    string stderr = "";
    int exitCode = -1;

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

            debugLog.Add($"Starting process: {process.StartInfo.FileName} {process.StartInfo.Arguments}"); // Log
            
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // Maak een CancellationTokenSource met 30s timeout
            bool exitedCleanly = false;
            try
            {
                await process.WaitForExitAsync(cts.Token); // Geef de CancellationToken mee
                exitedCleanly = true; // Process is zelf gestopt
                 exitCode = process.ExitCode; 
                 debugLog.Add($"Process exited with code: {exitCode}."); // Log
            }
            catch (OperationCanceledException) // Deze exception treedt op bij timeout
            {
                var timeoutMsg = "Process timed out after 30 seconds.";
                stderr += $"\n{timeoutMsg}";
                debugLog.Add(timeoutMsg); // Log
                try { process.Kill(); } catch {} // Probeer proces te stoppen
            }
            stdout = await stdoutTask;
            stderr = await stderrTask;

            debugLog.Add($"Raw stdout captured (length {stdout.Length})."); // Log
            // debugLog.Add($"Raw stdout content:\n{stdout}"); // Optioneel: log de inhoud zelf
            debugLog.Add($"Raw stderr captured (length {stderr.Length})."); // Log
            // debugLog.Add($"Raw stderr content:\n{stderr}"); // Optioneel: log de inhoud zelf
        }
    }
    catch (Exception e)
    {
        var errorMsg = $"Process execution EXCEPTION: {e.ToString()}";
        stderr += $"\n{errorMsg}";
        debugLog.Add($"!!! EXCEPTION during process execution: {e.ToString()}"); // Log
    }
    finally
    {
        if (File.Exists(scriptPath))
        {
            try { File.Delete(scriptPath); } 
            catch (Exception ex) { 
                var deleteError = $"Failed to delete script file: {ex.Message}";
                stderr += $"\n{deleteError}"; // Voeg toe aan stderr
                debugLog.Add(deleteError); // Log
            }
        }
    }

    // 4. Stuur het resultaat terug, INCLUSIEF de debug logs
    if (!string.IsNullOrEmpty(stderr) || exitCode != 0)
    {
        debugLog.Add("Execution failed, returning BadRequest (400)."); // Log
        // *** Stuur debugLog mee ***
        return Results.BadRequest(new { stdout, stderr, exitCode, debugLog }); 
    }
    
    debugLog.Add("Execution successful, returning OK (200)."); // Log
    // *** Stuur debugLog mee ***
    return Results.Ok(new { stdout, stderr, exitCode, debugLog }); 
});

app.Run("http://*:6000"); // Zorg dat dit 6000 is