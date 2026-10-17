# Builds stock Lingarr + Lingarr.Plugin.Ray (community plugin, API 1.0).
# Watchtower must stay disabled on the resulting image tag, or a Hub pull drops /app/plugins.
ARG LINGARR_BASE=lingarr/lingarr:1.4.0

FROM ${LINGARR_BASE} AS lingarr

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY --from=lingarr /app/Lingarr.Contracts.dll /references/Lingarr.Contracts.dll
COPY . .
RUN dotnet publish RayLingarrPlugin.csproj -c Release -o /publish \
    -p:LingarrContractsPath=/references/Lingarr.Contracts.dll

FROM ${LINGARR_BASE}
ENV PLUGINS_PATH=/app/plugins
RUN mkdir -p /app/plugins
COPY --from=build /publish/Lingarr.Plugin.Ray.dll /app/plugins/
COPY --from=lingarr /app/Lingarr.Contracts.dll /app/plugins/
