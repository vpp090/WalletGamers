FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/Wallet.Api/Wallet.Api.csproj src/Wallet.Api/
RUN dotnet restore src/Wallet.Api/Wallet.Api.csproj
COPY src/Wallet.Api/ src/Wallet.Api/
RUN dotnet publish src/Wallet.Api/Wallet.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Wallet.Api.dll"]
