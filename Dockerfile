# Fase 1: De C# API bouwen EN de tool installeren
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# Kopieer en bouw het project
COPY *.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Installeer de 'dotnet-script' tool HIER (in de SDK stage)
RUN dotnet tool install -g dotnet-script

# Fase 2: De uiteindelijke image maken
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

WORKDIR /app
COPY --from=build /app/publish .

# Kopieer de geïnstalleerde tool van de build stage
COPY --from=build /root/.dotnet/tools /root/.dotnet/tools

# Zorg dat de 'dotnet-script' tool in het PATH staat
# FIX for the warning: Use key=value format
ENV PATH="$PATH:/root/.dotnet/tools"

# De poort die onze API gebruikt (zie Program.cs)
EXPOSE 6000

# Het commando om de API te starten
ENTRYPOINT ["dotnet", "webapi.dll"]