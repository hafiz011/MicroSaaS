FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Microservice.Session.csproj", "."]
RUN dotnet restore "./Microservice.Session.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "./Microservice.Session.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Microservice.Session.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# Copy GeoLite2 DB
# COPY ./GeoIP/GeoLite2-City.mmdb ./GeoLite2-City.mmdb
# Copy GeoLite2 DB into container
COPY ./GeoIP/GeoLite2-City.mmdb /app/GeoLite2-City.mmdb

ENTRYPOINT ["dotnet", "Microservice.Session.dll"]
