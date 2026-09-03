FROM node:24-alpine AS web
WORKDIR /src/QuickProxy
COPY Packages/Aditify/package.json Packages/Aditify/yarn.lock Packages/Aditify/
COPY Packages/Aditify/Ui/package.json Packages/Aditify/Ui/package.json
COPY Packages/Aditify/Identity/package.json Packages/Aditify/Identity/package.json
COPY package.json yarn.lock ./
COPY src/QuickProxy.Admin/package.json src/QuickProxy.Admin/package.json
COPY documentation/package.json documentation/package.json
RUN corepack enable \
    && yarn --cwd Packages/Aditify install --frozen-lockfile \
    && yarn install --frozen-lockfile
COPY Packages/Aditify/Ui Packages/Aditify/Ui
COPY Packages/Aditify/Identity Packages/Aditify/Identity
COPY src/QuickProxy.Admin src/QuickProxy.Admin
COPY documentation documentation
RUN yarn build

FROM mcr.microsoft.com/dotnet/sdk:10.0.301 AS build
WORKDIR /src/QuickProxy
COPY . .
COPY --from=web /src/QuickProxy/src/QuickProxy/wwwroot/admin src/QuickProxy/wwwroot/admin
COPY --from=web /src/QuickProxy/src/QuickProxy/wwwroot/docs src/QuickProxy/wwwroot/docs
ARG APP_VERSION=0.9.0-local
ARG APP_ASSEMBLY_VERSION=0.9.0.0
RUN dotnet publish src/QuickProxy/QuickProxy.csproj \
        -c Release \
        -r linux-x64 \
        --self-contained false \
        -o /app \
        /p:PublishProfile= \
        /p:PublishSingleFile=false \
        /p:EnableCompressionInSingleFile=false \
        /p:UseAppHost=false \
        /p:Version=${APP_VERSION} \
        /p:AssemblyVersion=${APP_ASSEMBLY_VERSION} \
        /p:FileVersion=${APP_ASSEMBLY_VERSION} \
        /p:InformationalVersion=${APP_VERSION}

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG APP_VERSION=0.9.0
LABEL org.opencontainers.image.version=${APP_VERSION}
USER root
RUN mkdir -p /app/Data && chown -R app:app /app
WORKDIR /app
COPY --from=build --chown=app:app /app .
USER app
ENV ASPNETCORE_ENVIRONMENT=Production \
    Listen__HttpPort=80 \
    Listen__HttpsPort=443 \
    Listen__InternalPort=9000 \
    Listen__AdminAccess=any \
    Containers__Endpoint=unix:///var/run/docker.sock \
    Proxy__Storage__Provider=sqlite \
    Proxy__Storage__ConnectionString="Data Source=/app/Data/quickproxy.db" \
    DOTNET_EnableDiagnostics=0
VOLUME ["/app/Data"]
EXPOSE 80 443 9000
ENTRYPOINT ["dotnet", "QuickProxy.dll"]
