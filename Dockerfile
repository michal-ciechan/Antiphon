# CARD-0590 server image. SDK 10 publish, ASP.NET 9 runtime, linux-x64.
# The last stage is the runtime image. Tests and the SDK do not ship.
ARG SOURCE_REVISION=unknown

FROM node:22-bookworm AS client-build
WORKDIR /src/client
COPY client/package.json client/package-lock.json ./
RUN npm ci
COPY client/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server-build
ARG SOURCE_REVISION=unknown
WORKDIR /src
COPY global.json Directory.Build.props Directory.Build.targets ./
COPY src/ src/
COPY server/ server/
COPY docker/stack/init-state.sh /stack/init-state.sh
RUN dotnet publish server/Antiphon.Server.csproj -c Release -r linux-x64 --self-contained false -p:SourceRevisionId=$SOURCE_REVISION -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
ARG SOURCE_REVISION=unknown
LABEL org.opencontainers.image.revision="${SOURCE_REVISION}"
WORKDIR /app
USER root
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl ca-certificates git \
 && rm -rf /var/lib/apt/lists/* \
 && curl --version \
 && git --version
COPY --from=server-build /publish ./
COPY --from=server-build /stack/init-state.sh /stack/init-state.sh
COPY --from=client-build /src/client/dist ./wwwroot
RUN chmod 0755 /stack/init-state.sh \
 && test -f /app/Antiphon.Server.dll \
 && test -f /app/wwwroot/index.html
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK CMD curl -fsS http://127.0.0.1:8080/health || exit 1
USER 1654:1654
ENTRYPOINT ["dotnet", "Antiphon.Server.dll"]
