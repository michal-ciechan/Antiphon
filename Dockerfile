# Stage 1: Build React client
FROM node:22-alpine AS client-build
WORKDIR /app/client
COPY client/package*.json ./
RUN npm ci
COPY client/ ./
RUN npm run build

# Stage 2: Build .NET server
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS server-build
WORKDIR /app/server
COPY server/*.csproj ./
RUN dotnet restore
COPY server/ ./
RUN dotnet publish -c Release -o /publish

# Stage 3: Final image
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=server-build /publish ./
COPY --from=client-build /app/client/dist ./wwwroot
EXPOSE 5000
ENTRYPOINT ["dotnet", "Antiphon.Server.dll"]
