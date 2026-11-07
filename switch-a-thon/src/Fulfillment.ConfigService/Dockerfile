FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY NuGet.Config ./NuGet.Config
COPY src/Fulfillment.ConfigService/Fulfillment.ConfigService.csproj src/Fulfillment.ConfigService/
RUN dotnet restore src/Fulfillment.ConfigService/Fulfillment.ConfigService.csproj
COPY src/Fulfillment.ConfigService/ src/Fulfillment.ConfigService/
RUN dotnet publish src/Fulfillment.ConfigService/Fulfillment.ConfigService.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Fulfillment.ConfigService.dll"]
