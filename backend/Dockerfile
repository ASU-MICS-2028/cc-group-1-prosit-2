# Stage 1: build and publish the API. It runs on the builder's own CPU ($BUILDPLATFORM) even when the image
# is for another (arm64 for the Graviton server): a .NET publish without a runtime id is plain managed files,
# so nothing is compiled for the target CPU and the build needs no slow emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY APIs/ APIs/
COPY Services/ Services/
# The cache mount keeps downloaded packages between builds (a retry after a network drop resumes).
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore APIs/healthconnect-api
# The OpenAPI document is generated on a normal build and committed in docs/openapi; skip it here.
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish APIs/healthconnect-api -c Release -o /out --no-restore -p:OpenApiGenerateDocuments=false

# Stage 2: run it. Alpine on purpose: it is small, has few vulnerabilities for the deploy's Trivy gate to block,
# and has the `wget` the server's compose health check uses. (Globalization is invariant, so it needs no ICU.)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
# Take Alpine's security fixes released since the base image was built, then drop to the unprivileged user.
USER root
RUN apk upgrade --no-cache
WORKDIR /app
COPY --from=build /out .
# Non-root user (the base image provides one; APP_UID is its id).
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
ENTRYPOINT ["dotnet", "HealthConnect.Api.dll"]
