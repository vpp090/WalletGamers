FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/Casino.Cli/Casino.Cli.csproj src/Casino.Cli/
RUN dotnet restore src/Casino.Cli/Casino.Cli.csproj
COPY src/Casino.Cli/ src/Casino.Cli/
RUN dotnet publish src/Casino.Cli/Casino.Cli.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "Casino.Cli.dll"]
