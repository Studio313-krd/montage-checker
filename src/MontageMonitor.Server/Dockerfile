FROM node:24-alpine AS web-build
WORKDIR /source/src/MontageMonitor.Web
COPY src/MontageMonitor.Web/package*.json ./
RUN npm ci
COPY src/MontageMonitor.Web/ ./
COPY src/MontageMonitor.Agent/Assets/AppIcon.Source.png ../MontageMonitor.Agent/Assets/AppIcon.Source.png
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS server-build
WORKDIR /source
COPY MontageMonitor.sln Directory.Build.props global.json ./
COPY src/MontageMonitor.Shared/MontageMonitor.Shared.csproj src/MontageMonitor.Shared/
COPY src/MontageMonitor.Server/MontageMonitor.Server.csproj src/MontageMonitor.Server/
RUN dotnet restore src/MontageMonitor.Server/MontageMonitor.Server.csproj
COPY src/MontageMonitor.Shared/ src/MontageMonitor.Shared/
COPY src/MontageMonitor.Server/ src/MontageMonitor.Server/
RUN dotnet publish src/MontageMonitor.Server/MontageMonitor.Server.csproj -c Release -o /app/publish --no-restore
COPY --from=web-build /source/src/MontageMonitor.Web/dist /app/publish/wwwroot

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
RUN apk add --no-cache krb5-libs tzdata \
    && addgroup -S montage && adduser -S montage -G montage \
    && mkdir -p /data/screenshots /data/keys \
    && chown -R montage:montage /app /data
USER montage
COPY --from=server-build --chown=montage:montage /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "MontageMonitor.Server.dll"]
