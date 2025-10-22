# Fase 1: De C# API bouwen EN de tool installeren EN de cache vullen
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# Kopieer en bouw het project
COPY *.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Installeer de 'dotnet-script' tool HIER (in de SDK stage)
RUN dotnet tool install -g dotnet-script

# !! NIEUWE STAP: Voer een dummy dotnet-script uit om de cache te vullen !!
# Maak een leeg script bestand
RUN touch /src/dummy.csx
# Voer het uit. Dit triggert de 'dotnet restore' tijdens de build.
# We negeren eventuele fouten van het lege script zelf met '|| true'.
RUN /root/.dotnet/tools/dotnet-script /src/dummy.csx || true
# Verwijder het dummy bestand weer
RUN rm /src/dummy.csx


# Fase 2: De uiteindelijke image maken
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

WORKDIR /app
COPY --from=build /app/publish .

# Kopieer de geïnstalleerde tool VAN DE build stage
COPY --from=build /root/.dotnet/tools /root/.dotnet/tools
# !! NIEUW: Kopieer de gevulde NuGet cache VAN DE build stage !!
COPY --from=build /root/.nuget /root/.nuget
# !! NIEUW: Kopieer de gevulde dotnet-script cache VAN DE build stage !!
COPY --from=build /root/.cache/dotnet-script /root/.cache/dotnet-script


# Zorg dat de 'dotnet-script' tool in het PATH staat
ENV PATH="$PATH:/root/.dotnet/tools"

# De poort die onze API gebruikt (zie Program.cs)
EXPOSE 6000

# Het commando om de API te starten
ENTRYPOINT ["dotnet", "webapi.dll"]