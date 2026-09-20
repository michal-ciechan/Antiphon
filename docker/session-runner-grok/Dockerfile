# CARD-0490: dedicated linux-x64 phone-home runner image. Companion compose only.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj -c Release -r linux-x64 --self-contained false -o /out/runner \
 && dotnet publish src/Antiphon.PtyHost/Antiphon.PtyHost.csproj -c Release -r linux-x64 --self-contained false -o /out/ptyhost

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /out/runner/ ./
COPY --from=build /out/ptyhost/Antiphon.PtyHost /app/Antiphon.PtyHost
COPY --from=build /out/ptyhost/libporta_pty.so /app/libporta_pty.so
ENV PhoneHome__Enabled=false
ENTRYPOINT ["dotnet", "Antiphon.SessionRunner.dll"]
