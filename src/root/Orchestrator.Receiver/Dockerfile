# Build context is `src/` (the .NET solution root) -- this project references sibling shared/
# and modules/ projects, so a narrower context can't resolve them.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish root/Orchestrator.Receiver/Orchestrator.Receiver.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:3000
EXPOSE 3000

USER $APP_UID
ENTRYPOINT ["dotnet", "Orchestrator.Receiver.dll"]
