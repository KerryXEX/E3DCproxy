FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim-amd64 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 5033

ENV E3DC-IP "192.168.99.99"
ENV E3DC-Port 5033
ENV E3DC-User "xxx@yyy.zz"
ENV E3DC-Password "++++++++"
ENV RSCP-Password "********"

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["E3DCproxy/E3DCproxy.csproj", "E3DCproxy/"]
RUN dotnet restore "E3DCproxy/E3DCproxy.csproj"
COPY . .
WORKDIR "/src/E3DCproxy"
RUN dotnet build "E3DCproxy.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "E3DCproxy.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "E3DCproxy.dll"]
