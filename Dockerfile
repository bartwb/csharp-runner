# Fase 1: De C# API bouwen
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# Kopieer en bouw het project
COPY *.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Fase 2: De uiteindelijke image maken
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

WORKDIR /app
COPY --from=build /app/publish .

# Installeer de 'dotnet-script' tool
# Dit is de tool die de .csx-bestanden uitvoert
RUN dotnet tool install -g dotnet-script

# Zorg dat de 'dotnet-script' tool in het PATH staat
ENV PATH "$PATH:/root/.dotnet/tools"

# De poort die onze API gebruikt (zie Program.cs)
EXPOSE 8080

# Het commando om de API te starten
ENTRYPOINT ["dotnet", "webapi.dll"]