FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
COPY ["/../LN-history.Startup/LN-history.Startup.csproj", "LN-history.Startup/"]
#COPY ["/../LN-history.Cache/LN-history.Cache.csproj", "LN-history.Cache/"]
COPY ["/../LN-History.Model/LN-History.Model.csproj", "LN-History.Model/"]
COPY ["/../LN-history.Data/LN-history.Data.csproj", "LN-history.Data/"]
#COPY ["/../LightningGraph/LightningGraph.csproj", "LightningGraph/"]
COPY ["/../LN-history.Api/LN-history.Api.csproj", "LN-history.Api/"]
COPY ["/../LN-history.Core/LN-history.Core.csproj", "LN-history.Core/"]
#COPY ["/../Bitcoin.Core/Bitcoin.Core.csproj", "Bitcoin.Core/"]
COPY . .
RUN dotnet build "/../LN-history.Startup/LN-history.Startup.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "/../LN-history.Startup/LN-history.Startup.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LN-history.Startup.dll"]
