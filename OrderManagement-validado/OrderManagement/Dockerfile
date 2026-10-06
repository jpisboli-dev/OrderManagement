FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props NuGet.Config ./
COPY src/ ./src/
RUN dotnet restore src/API/API.csproj --configfile NuGet.Config
RUN dotnet publish src/API/API.csproj -c Release --no-restore -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "API.dll"]
