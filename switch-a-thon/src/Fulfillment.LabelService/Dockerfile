FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY NuGet.Config ./NuGet.Config
COPY src/Fulfillment.LabelService/Fulfillment.LabelService.csproj src/Fulfillment.LabelService/
RUN dotnet restore src/Fulfillment.LabelService/Fulfillment.LabelService.csproj
COPY src/Fulfillment.LabelService/ src/Fulfillment.LabelService/
RUN dotnet publish src/Fulfillment.LabelService/Fulfillment.LabelService.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Fulfillment.LabelService.dll"]
