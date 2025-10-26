using System.Diagnostics;
using System.Text.Json; 
using System.Text;    
using System;
using System.IO;      
using System.Threading; 

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options => {
    options.AddDefaultPolicy(policy => {
        policy.AllowAnyOrigin() // Sta elke oorsprong toe (voor P.O.C. is dit oké)
              .AllowAnyHeader() // Sta elke header toe
              .AllowAnyMethod(); // Sta elke HTTP methode toe (GET, POST, etc.)
    });
});

var app = builder.Build();
app.UseCors(); 

app.MapGet("/healthstatus", () => {
    Console.WriteLine("[Health Check] Health check requested, returning OK."); // Log voor debugging
    return Results.Ok(new { status = "healthy" }); // Stuur simpel "OK" terug
});

app.MapPost("/runner", async (HttpContext context) =>
{
    // Lijst om interne debug logs op te vangen
    var debugLog = new List<string>();
    debugLog.Add("[Execute] Request received."); 

    string code = "";       
    string stdout = "";   
    string stderr = "";  
    int exitCode = -1; // Exit code van het dotnet script proces
    
    // Maak altijd een unieke bestandsnaam om problemen te voorkomen
    var scriptPath = Path.Combine("/tmp", $"temp_script_{Guid.NewGuid()}.csx");

    try
    {
        // Lees Code uit Request Body
        debugLog.Add("[Execute] Reading request body...");
        using (var reader = new StreamReader(context.Request.Body))
        {
            var body = await reader.ReadToEndAsync();
            debugLog.Add($"[Execute] Raw body received (length: {body.Length}).");
            try
            {
                // Haal de code uit de JSON
                var jsonBody = JsonDocument.Parse(body);
                code = jsonBody.RootElement.GetProperty("code").GetString() ?? "";
                debugLog.Add($"[Execute] Code extracted (length: {code.Length}).");
            }
            catch (Exception e) 
            {
                var errorMsg = $"JSON parse error: {e.Message}";
                stderr = errorMsg; 
                debugLog.Add($"[Execute] ERROR: {errorMsg}"); // Log de fout        
                return Results.BadRequest(new { stdout = "", stderr, exitCode = -1, debugLog });
            }
        }

        // Controleer of er code is ontvangen
        if (string.IsNullOrWhiteSpace(code))
        {
            stderr = "Code body is empty.";
            debugLog.Add("[Execute] ERROR: Code body is empty.");
            return Results.BadRequest(new { stdout = "", stderr, exitCode = -1, debugLog });
        }

        // Sla Code op in Tijdelijk Bestand
        try
        {
            await File.WriteAllTextAsync(scriptPath, code);
            debugLog.Add($"[Execute] Code saved to temporary file: {scriptPath}.");
        }
        catch (Exception e)
        {
             stderr = $"Failed to write script file: {e.Message}";
             debugLog.Add($"[Execute] ERROR: {stderr}");
             return Results.Json(new { stdout = "", stderr, exitCode = -1, debugLog }, statusCode: 500);
        }


        // Voer Script uit met dotnet-script
        try
        {
            using (var process = new Process())
            {
                
                string dotnetScriptPath = "/root/.dotnet/tools/dotnet-script";
                if (!File.Exists(dotnetScriptPath))
                {
                    stderr = "Internal Server Error: dotnet-script tool not found.";
                    debugLog.Add($"[Execute] FATAL ERROR: dotnet-script tool not found at {dotnetScriptPath}");
                    return Results.Json(new { stdout = "", stderr, exitCode = -1, debugLog }, statusCode: 500);
                }
                debugLog.Add($"[Execute] Using dotnet-script path: {dotnetScriptPath}");

                process.StartInfo.FileName = dotnetScriptPath;
                process.StartInfo.Arguments = $"\"{scriptPath}\"";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError  = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow  = true;

                // Werkdirectory naar /tmp - Nodig om het in ACA te kunnen laten werken
                process.StartInfo.WorkingDirectory = "/tmp";

                // Expliciete env vars voor NuGet/dotnet - Nodig om het in ACA te kunnen laten werken
                process.StartInfo.Environment["NUGET_PACKAGES"]                 = "/root/.nuget/packages";
                process.StartInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
                process.StartInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"]    = "1";
                process.StartInfo.Environment["HOME"]                           = "/root";

                debugLog.Add($"[Execute] Starting process: {process.StartInfo.FileName} {process.StartInfo.Arguments}");

                var outputData = new List<string>(); 
                var errorData = new List<string>(); 

                process.OutputDataReceived += (sender, args) => {
                    if (args.Data != null) {
                        outputData.Add(args.Data);
                        debugLog.Add($"[dotnet-script stdout] {args.Data}");
                    }
                };
                process.ErrorDataReceived += (sender, args) => {
                    if (args.Data != null) {
                        errorData.Add(args.Data);
                        debugLog.Add($"[dotnet-script stderr] {args.Data}");
                    }
                };

                // Start het uitvoeren van de code met dotnet-script
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Wacht op het proces om te eindigen met een timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); 
                bool exitedCleanly = false;
                
                try
                {
                    // Wacht asynchroon tot het proces stopt of er timeout komt
                    await process.WaitForExitAsync(cts.Token);
                    exitedCleanly = true; // Proces is zelf gestopt
                    exitCode = process.ExitCode; 
                    debugLog.Add($"[Execute] Process exited with code: {exitCode}.");
                }
                catch (OperationCanceledException) // Timeout is opgetreden
                {
                    var timeoutMsg = "Process timed out after 30 seconds.";
                    stderr = timeoutMsg; 
                    debugLog.Add($"[Execute] ERROR: {timeoutMsg}");
                    try { process.Kill(true); } // Probeer het proces te stoppen
                    catch (InvalidOperationException) // Proces was al gestopt
                    catch (Exception killEx) { debugLog.Add($"[Execute] Error trying to kill process: {killEx.Message}"); }
                }
                catch (Exception waitEx) // Vang andere fouten op tijdens het wachten
                {
                     var waitErrorMsg = $"Error waiting for process exit: {waitEx.Message}";
                     stderr = waitErrorMsg;
                     debugLog.Add($"[Execute] ERROR: {waitErrorMsg}");
                }

                // Combineer de opntvangen regels
                stdout = string.Join(Environment.NewLine, outputData);
                // Combineer stderr regels en voeg timeout/exception meldingen toe
                string processStderr = string.Join(Environment.NewLine, errorData);
                if (!string.IsNullOrEmpty(processStderr)) {
                     // Voeg proces stderr toe aan eventuele eerdere fouten
                     stderr = string.IsNullOrEmpty(stderr) ? processStderr : $"{stderr}\n{processStderr}";
                }

                debugLog.Add($"[Execute] Final captured stdout (length {stdout.Length}).");
                debugLog.Add($"[Execute] Final captured stderr (length {stderr.Length}).");
            }
        }
        catch (Exception e) // Vang fouten op bij het opzetten/starten van het proces
        {
            var processErrorMsg = $"Process execution setup/start EXCEPTION: {e.ToString()}";
            stderr = string.IsNullOrEmpty(stderr) ? processErrorMsg : $"{stderr}\n{processErrorMsg}";
            debugLog.Add($"[Execute] !!! EXCEPTION during process setup/start: {e.ToString()}");
        }
    }
    finally 
    {
        // Verwijder het tijdelijke script bestand
        if (File.Exists(scriptPath))
        {
            try
            {
                 File.Delete(scriptPath);
                 debugLog.Add($"[Execute] Temporary script file deleted: {scriptPath}.");
            }
            catch (Exception ex) 
            {
                var deleteError = $"Failed to delete script file {scriptPath}: {ex.Message}";
                debugLog.Add($"[Execute] WARNING: {deleteError}");
            }
        }
    }

    // 4. Stuur resultaat terug met debug logs
    if (!string.IsNullOrEmpty(stderr) || exitCode != 0)
    {
        debugLog.Add("[Execute] Execution failed or produced stderr/non-zero exit code, returning BadRequest (400).");
        return Results.BadRequest(new { stdout, stderr, exitCode, debugLog });
    }

    debugLog.Add("[Execute] Execution successful, returning OK (200).");
    return Results.Ok(new { stdout, stderr, exitCode, debugLog });
});

// Start app op poort uit .env of 6000 - 6000 is nodig om de app te kunnen bereiken in ACA
var port = Environment.GetEnvironmentVariable("PORT") ?? "6000";
app.Run($"http://*:{port}"); 
Console.WriteLine($"API listening on port {port}..."); 