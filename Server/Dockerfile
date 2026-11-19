# The Stratum dedicated server. Build from the repository root (the server compiles the game's Core code):
#   docker build -f Server/Dockerfile -t stratum-server .
#   docker run -p 25600:25600 -v stratum-world:/data stratum-server
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Assets/Scripts/Core/ Assets/Scripts/Core/
COPY Server/ Server/
RUN dotnet publish Server/Stratum.Server.csproj -c Release -o /out --nologo

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /out .
ENV STRATUM_DATA=/data \
    STRATUM_PORT=25600 \
    DOTNET_gcServer=0 \
    DOTNET_GCHeapHardLimitPercent=70
# The world lives in /data: mount a volume there (docker -v, or a Railway volume; Railway refuses VOLUME lines).
EXPOSE 25600
# The world is saved on SIGTERM (docker stop, a Railway redeploy), so give it a moment.
STOPSIGNAL SIGTERM
ENTRYPOINT ["dotnet", "StratumServer.dll"]
