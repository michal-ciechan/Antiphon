# CARD-0590 server image. SDK 10 publish, ASP.NET 9 runtime, linux-x64.
# The last stage is the runtime image. Tests and the SDK do not ship.
ARG SOURCE_REVISION=unknown
# CARD-1260: the server runs LandingGit and the guarded removals, so it needs Git at or above the
# floor in docker/git-minimum-version.sh. Debian bookworm ships 2.39.5 and bookworm-backports carries
# no git, so this image builds the same pinned release as docker/session-runner-grok/Dockerfile,
# verified by its kernel.org SHA-256 before it is unpacked. Raise both images together.
ARG GIT_VERSION=2.47.3
ARG GIT_SHA256=c073471530e92b716641ea2b381fcd0ece53eea9a76a9c5415f93f89e870dd5f

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
COPY scripts/lib/*.cs scripts/lib/
COPY docker/stack/init-state.sh /stack/init-state.sh
RUN dotnet publish server/Antiphon.Server.csproj -c Release -r linux-x64 --self-contained false -p:SourceRevisionId=$SOURCE_REVISION -o /publish

# Built on the runtime's own base so the binary links against the same bookworm libraries.
# sysconfdir=/etc keeps /etc/gitconfig the system config, where Debian's git read it.
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS git-build
ARG GIT_VERSION
ARG GIT_SHA256
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl make gcc libc6-dev \
      libcurl4-gnutls-dev libexpat1-dev zlib1g-dev \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /tmp/git-src \
 && curl -fsSL -o /tmp/git-src/git.tar.gz "https://mirrors.edge.kernel.org/pub/software/scm/git/git-${GIT_VERSION}.tar.gz" \
 && echo "${GIT_SHA256}  /tmp/git-src/git.tar.gz" | sha256sum -c - \
 && tar -xzf /tmp/git-src/git.tar.gz -C /tmp/git-src \
 && make -C "/tmp/git-src/git-${GIT_VERSION}" -j"$(nproc)" prefix=/usr/local sysconfdir=/etc \
      NO_GETTEXT=1 NO_TCLTK=1 NO_PERL=1 NO_PYTHON=1 NO_OPENSSL=1 INSTALL_SYMLINKS=1 \
      DESTDIR=/opt/git-root all install \
 && /opt/git-root/usr/local/bin/git --version | grep -F "git version ${GIT_VERSION}" \
 && test -x /opt/git-root/usr/local/libexec/git-core/git-remote-https \
 && rm -rf /tmp/git-src

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
ARG SOURCE_REVISION=unknown
ARG GIT_VERSION
LABEL org.opencontainers.image.revision="${SOURCE_REVISION}"
WORKDIR /app
USER root
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl ca-certificates libcurl3-gnutls libexpat1 zlib1g \
 && rm -rf /var/lib/apt/lists/* \
 && curl --version
# CARD-1260: the pinned Git (see git-build) replaces Debian's 2.39.5; no apt git is installed.
COPY --from=git-build /opt/git-root/usr/local/ /usr/local/
COPY docker/git-minimum-version.sh /usr/local/share/antiphon/git-minimum-version.sh
RUN test "$(command -v git)" = /usr/local/bin/git \
 && git --version | grep -F "git version ${GIT_VERSION}" \
 && sh /usr/local/share/antiphon/git-minimum-version.sh \
 && test -x /usr/local/libexec/git-core/git-remote-https
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
