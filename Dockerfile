# Fase 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY *.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish
# NO dotnet tool install

# Fase 2: Final
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# NO tool copy
# NO path update for tools
EXPOSE 6000
ENTRYPOINT ["dotnet", "webapi.dll"]