using System.Diagnostics;
using System.Text.Json; // Nodig voor JSON parsing
using System.Text;     // Nodig voor StringBuilder als je dat zou gebruiken
using System;         // Nodig voor Environment, Guid etc.
using System.IO;       // Nodig voor StreamReader, File etc.
using System.Threading; // Nodig voor CancellationTokenSource

var builder = WebApplication.CreateBuilder(args);

// --- CORS Configuratie ---
// Sta Cross-Origin (CORS) verzoeken toe zodat je React app kan verbinden
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(policy => {
        policy.AllowAnyOrigin() // Sta elke oorsprong toe (voor P.O.C. is dit oké)
              .AllowAnyHeader() // Sta elke header toe
              .AllowAnyMethod(); // Sta elke HTTP methode toe (GET, POST, etc.)
    });
});

var app = builder.Build();
app.UseCors(); // Activeer het CORS beleid

// --- Health Endpoint ---
// Een simpel endpoint dat Azure kan aanroepen om te zien of de container leeft
app.MapGet("/healthstatus", () => {
    Console.WriteLine("[Health Check] Health check requested, returning OK."); // Log voor debugging
    return Results.Ok(new { status = "healthy" }); // Stuur simpel "OK" terug
});

// --- Execute Endpoint ---
// Het hoofd-endpoint dat C# code ontvangt en uitvoert
app.MapPost("/execute", async (HttpContext context) =>
{
    // Lijst om interne debug logs op te vangen
    var debugLog = new List<string>();
    debugLog.Add("[Execute] Request received."); // Eerste log entry

    string code = "";       // Variabele voor de C# code
    string stdout = "";     // Variabele voor de Standaard Output
    string stderr = "";     // Variabele voor de Standaard Error Output
    int exitCode = -1;      // Exit code van het dotnet script proces
    // Maak een unieke bestandsnaam om conflicten te voorkomen
    var scriptPath = $"temp_script_{Guid.NewGuid()}.csx";

    try
    {
        // 1. Lees Code uit Request Body
        debugLog.Add("[Execute] Reading request body...");
        using (var reader = new StreamReader(context.Request.Body))
        {
            var body = await reader.ReadToEndAsync();
            debugLog.Add($"[Execute] Raw body received (length: {body.Length}).");
            try
            {
                // Parse de verwachte JSON: { "code": "..." }
                var jsonBody = JsonDocument.Parse(body);
                code = jsonBody.RootElement.GetProperty("code").GetString() ?? "";
                debugLog.Add($"[Execute] Code extracted (length: {code.Length}).");
            }
            catch (Exception e) // Vang fouten op bij het parsen van JSON
            {
                var errorMsg = $"JSON parse error: {e.Message}";
                stderr = errorMsg; // Zet de fout in stderr
                debugLog.Add($"[Execute] ERROR: {errorMsg}"); // Log de fout
                // Stuur 400 Bad Request terug met de logs
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

        // 2. Sla Code op in Tijdelijk Bestand
        try
        {
            await File.WriteAllTextAsync(scriptPath, code);
            debugLog.Add($"[Execute] Code saved to temporary file: {scriptPath}.");
        }
        catch (Exception e) // Vang fouten op bij het schrijven van het bestand
        {
             stderr = $"Failed to write script file: {e.Message}";
             debugLog.Add($"[Execute] ERROR: {stderr}");
             // Dit is een serverfout, stuur 500 terug
             return Results.Json(new { stdout = "", stderr, exitCode = -1, debugLog }, statusCode: 500);
        }


        // 3. Voer Script uit met dotnet-script
        try
        {
            using (var process = new Process())
            {
                // Gebruik het expliciete pad waar de tool geïnstalleerd is in de Dockerfile
                string dotnetScriptPath = "/root/.dotnet/tools/dotnet-script";
                // Controleer of het bestand bestaat
                if (!File.Exists(dotnetScriptPath))
                {
                    stderr = "Internal Server Error: dotnet-script tool not found.";
                    debugLog.Add($"[Execute] FATAL ERROR: dotnet-script tool not found at {dotnetScriptPath}");
                    return Results.Json(new { stdout = "", stderr, exitCode = -1, debugLog }, statusCode: 500);
                }
                debugLog.Add($"[Execute] Using dotnet-script path: {dotnetScriptPath}");

                // Configureer het proces om te starten
                process.StartInfo.FileName = dotnetScriptPath; // Het commando
                process.StartInfo.Arguments = $"\"{scriptPath}\""; // Het argument (het script-bestand)
                process.StartInfo.RedirectStandardOutput = true; // Vang stdout op
                process.StartInfo.RedirectStandardError = true;  // Vang stderr op
                process.StartInfo.UseShellExecute = false;      // Vereist voor redirect
                process.StartInfo.CreateNoWindow = true;       // Geen UI tonen
                process.StartInfo.WorkingDirectory = "/app";   // Werkmap expliciet instellen

                debugLog.Add($"[Execute] Starting process: {process.StartInfo.FileName} {process.StartInfo.Arguments}");

                var outputData = new List<string>(); // Lijst voor stdout regels
                var errorData = new List<string>();  // Lijst voor stderr regels

                // Event handlers om output/error direct op te vangen
                process.OutputDataReceived += (sender, args) => {
                    if (args.Data != null) {
                        outputData.Add(args.Data);
                        // Log elke stdout regel direct
                        debugLog.Add($"[dotnet-script stdout] {args.Data}");
                    }
                };
                process.ErrorDataReceived += (sender, args) => {
                    if (args.Data != null) {
                        errorData.Add(args.Data);
                        // Log elke stderr regel direct
                        debugLog.Add($"[dotnet-script stderr] {args.Data}");
                    }
                };

                // Start het proces
                process.Start();
                // Begin met het asynchroon lezen van de output streams
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Wacht op het proces om te eindigen, met een timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // 30 seconden timeout
                bool exitedCleanly = false;
                
                try
                {
                    // Wacht asynchroon tot het proces stopt OF de timeout optreedt
                    await process.WaitForExitAsync(cts.Token);
                    exitedCleanly = true; // Proces is zelf gestopt
                    exitCode = process.ExitCode; // Sla de exit code op
                    debugLog.Add($"[Execute] Process exited with code: {exitCode}.");
                }
                catch (OperationCanceledException) // Timeout is opgetreden
                {
                    var timeoutMsg = "Process timed out after 30 seconds.";
                    stderr = timeoutMsg; // Zet dit als de primaire foutmelding
                    debugLog.Add($"[Execute] ERROR: {timeoutMsg}");
                    try { process.Kill(true); } // Probeer het proces te stoppen
                    catch (InvalidOperationException) { /* Proces was al gestopt */ }
                    catch (Exception killEx) { debugLog.Add($"[Execute] Error trying to kill process: {killEx.Message}"); }
                }
                catch (Exception waitEx) // Vang andere fouten op tijdens het wachten
                {
                     var waitErrorMsg = $"Error waiting for process exit: {waitEx.Message}";
                     stderr = waitErrorMsg; // Zet dit als de primaire foutmelding
                     debugLog.Add($"[Execute] ERROR: {waitErrorMsg}");
                }

                // Combineer de opgevangen regels (NA het wachten of timeout)
                stdout = string.Join(Environment.NewLine, outputData);
                // Combineer stderr regels en voeg eventuele timeout/exception meldingen toe
                string processStderr = string.Join(Environment.NewLine, errorData);
                if (!string.IsNullOrEmpty(processStderr)) {
                     // Voeg proces stderr toe aan eventuele eerdere fouten (zoals timeout)
                     stderr = string.IsNullOrEmpty(stderr) ? processStderr : $"{stderr}\n{processStderr}";
                }

                debugLog.Add($"[Execute] Final captured stdout (length {stdout.Length}).");
                debugLog.Add($"[Execute] Final captured stderr (length {stderr.Length}).");
            }
        }
        catch (Exception e) // Vang fouten op bij het opzetten/starten van het proces
        {
            var processErrorMsg = $"Process execution setup/start EXCEPTION: {e.ToString()}";
            // Voeg toe aan stderr
            stderr = string.IsNullOrEmpty(stderr) ? processErrorMsg : $"{stderr}\n{processErrorMsg}";
            debugLog.Add($"[Execute] !!! EXCEPTION during process setup/start: {e.ToString()}");
        }
    }
    finally // Dit blok wordt ALTIJD uitgevoerd, ook na een return of exception
    {
        // Verwijder het tijdelijke script bestand
        if (File.Exists(scriptPath))
        {
            try
            {
                 File.Delete(scriptPath);
                 debugLog.Add($"[Execute] Temporary script file deleted: {scriptPath}.");
            }
            catch (Exception ex) // Vang fouten op bij het verwijderen
            {
                var deleteError = $"Failed to delete script file {scriptPath}: {ex.Message}";
                // Log dit alleen, overschrijf niet de primaire stderr
                debugLog.Add($"[Execute] WARNING: {deleteError}");
            }
        }
    }

    // 4. Stuur Resultaat Terug (inclusief debug logs)
    // Als er een fout was (stderr niet leeg OF exit code niet 0), stuur 400 Bad Request
    if (!string.IsNullOrEmpty(stderr) || exitCode != 0)
    {
        debugLog.Add("[Execute] Execution failed or produced stderr/non-zero exit code, returning BadRequest (400).");
        return Results.BadRequest(new { stdout, stderr, exitCode, debugLog });
    }

    // Anders was het succesvol, stuur 200 OK
    debugLog.Add("[Execute] Execution successful, returning OK (200).");
    return Results.Ok(new { stdout, stderr, exitCode, debugLog });
});

// --- Start Applicatie ---
// Luister op de poort die is opgegeven via de PORT environment variabele (standaard in containers)
// of val terug op 6000 als die niet is ingesteld.
// Zorg dat deze poort overeenkomt met EXPOSE in Dockerfile en Target Port in Azure.
var port = Environment.GetEnvironmentVariable("PORT") ?? "6000";
app.Run($"http://*:{port}"); // Luister op alle netwerkinterfaces (*)
Console.WriteLine($"API listening on port {port}..."); // Log dat de server start