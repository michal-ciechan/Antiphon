# CARD-0490: dedicated linux-x64 phone-home runner image. Companion compose only.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj -c Release -r linux-x64 --self-contained false -o /out/runner \
 && dotnet publish src/Antiphon.PtyHost/Antiphon.PtyHost.csproj -c Release -r linux-x64 --self-contained false -o /out/ptyhost

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
# Phone-home stays off until compose/launch injects PhoneHome__Enabled and PhoneHome__ServerOrigin.
# Do not bake a ServerOrigin URL into the image.
# Do not COPY OAuth credential files or any GROK_HOME contents (CARD-0575).
USER root
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl ca-certificates bash \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /tmp/grok-install \
 && curl -fsSL https://x.ai/cli/install.sh | env HOME=/tmp/grok-install GROK_BIN_DIR=/usr/local/bin bash -s 1.0.34 \
 && install -m 0755 /tmp/grok-install/.grok/downloads/grok-linux-x86_64 /usr/local/bin/grok \
 && ln -sf grok /usr/local/bin/agent \
 && /usr/local/bin/grok --version \
 && /usr/local/bin/grok --version | grep -F '1.0.34' \
 && rm -rf /tmp/grok-install
COPY --from=build /out/runner/ ./
COPY --from=build /out/ptyhost/Antiphon.PtyHost /app/Antiphon.PtyHost
COPY --from=build /out/ptyhost/libporta_pty.so /app/libporta_pty.so
ENV PhoneHome__Enabled=false
USER $APP_UID
ENTRYPOINT ["dotnet", "Antiphon.SessionRunner.dll"]
