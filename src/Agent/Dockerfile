# syntax=docker/dockerfile:1
# ─── WHY multi-stage? ────────────────────────────────────────────────────────
# Stage 1 uses the full .NET SDK (~800 MB) to compile and publish the app.
# Stage 2 uses only the .NET runtime (~200 MB) and copies in the compiled output.
# The SDK never ships in the final image — smaller pull, smaller attack surface.
# ─────────────────────────────────────────────────────────────────────────────

# ─── Stage 1: build ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files BEFORE source code.
# Docker caches each instruction as a layer. If only .cs files change, the
# restore layer is reused on rebuild (NuGet packages are already downloaded).
# This is the single most important Dockerfile performance pattern for .NET.
COPY global.json .
COPY src/Agent/Agent.csproj          src/Agent/
COPY src/Rekordbox/Rekordbox.csproj  src/Rekordbox/
COPY src/Rekordbox.Graph/Rekordbox.Graph.csproj  src/Rekordbox.Graph/

RUN dotnet restore src/Agent/Agent.csproj

# Now copy the full source and publish a Release build.
# --no-restore: skip a second restore since we already have the packages.
# -o /app/publish: write the self-contained output to a known path.
COPY src/ src/
RUN dotnet publish src/Agent/Agent.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish

# ─── Stage 2: runtime ────────────────────────────────────────────────────────
# Start fresh from the runtime-only image. Nothing from the SDK stage leaks in
# except what we explicitly COPY --from=build.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# Pull the published output from the build stage.
COPY --from=build /app/publish .

# ENTRYPOINT sets the executable; CMD would set default arguments.
# Keeping them separate lets callers override the arguments without replacing
# the executable — e.g. `docker run dj-buddy-agent /data/rekordbox.xml`.
ENTRYPOINT ["dotnet", "Agent.dll"]

# ─── Usage ───────────────────────────────────────────────────────────────────
# Build (run from repo root so the build context includes all project refs):
#   docker build -f src/Agent/Dockerfile -t dj-buddy-agent .
#
# Run (mount your rekordbox.xml into the container as a read-only volume):
#   docker run --rm -it \
#     -v "$HOME/Music/rekordbox/rekordbox.xml:/data/rekordbox.xml:ro" \
#     -e GITHUB_TOKEN="$GITHUB_TOKEN" \
#     dj-buddy-agent /data/rekordbox.xml
#
# -i (--interactive): keeps stdin open so the app can read your keystrokes.
# -t (--tty): allocates a pseudo-terminal so colors and Spectre tables render.
# Both are needed for any interactive CLI tool running inside a container.
# The -v flag mounts a host path into the container. :ro makes it read-only.
# -e passes environment variables — the Copilot SDK needs GITHUB_TOKEN.
# --rm deletes the container after it exits (good for one-shot CLI tools).
