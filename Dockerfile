# The node (API, explorer, sync worker) as one container. Build context: the repo root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/MiniBtc.Api/MiniBtc.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# The aspnet image ships a non-root user for exactly this.
USER app
ENTRYPOINT ["dotnet", "MiniBtc.Api.dll"]
