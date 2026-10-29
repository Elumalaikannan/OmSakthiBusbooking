
# Build the application
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/BusBooking.Api.csproj backend/
RUN dotnet restore backend/BusBooking.Api.csproj

COPY . .
WORKDIR /src/backend
RUN dotnet publish BusBooking.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Run the application
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=10000

EXPOSE 10000

ENTRYPOINT ["dotnet", "BusBooking.Api.dll"]
