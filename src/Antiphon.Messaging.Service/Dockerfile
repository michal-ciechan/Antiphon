# Build context = the repo's src/ folder (the three messaging projects are siblings).
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Antiphon.Messaging/ ./Antiphon.Messaging/
COPY Antiphon.Messaging.Telegram/ ./Antiphon.Messaging.Telegram/
COPY Antiphon.Messaging.Service/ ./Antiphon.Messaging.Service/
RUN dotnet publish Antiphon.Messaging.Service/Antiphon.Messaging.Service.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Antiphon.Messaging.Service.dll"]
