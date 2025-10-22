// Program.cs - Minimal Version
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Hello World!"); // Simplest possible endpoint at root

Console.WriteLine("Minimal API Starting on port 6000..."); // Add a startup log

app.Run("http://*:6000");