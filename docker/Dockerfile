# skafinity — build the C#→WebAssembly engine, serve the static web toy.
#
# Stage 1 compiles the SAME MusicGen.cs / VibeCodec.cs the s&box library ships (shared
# source via wasm/Skafinity.Wasm.csproj's relative <Compile Include>s) to wasm, producing
# the _framework runtime bundle. Stage 2 is a plain nginx serving web/ plus that bundle —
# no .NET at runtime, so the image is small and the served docroot is exactly what
# `make serve` would host locally.

# ── Build stage: publish the wasm runtime bundle ────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

# Emscripten drives its bitcode->wasm step through python, which the SDK image does not
# ship: without this the AOT publish dies late with "unable to find python in $PATH". Some
# of the toolchain spells it `python3` and some plain `python`, so provide both.
RUN apt-get update \
 && apt-get install -y --no-install-recommends python3 \
 && ln -sf /usr/bin/python3 /usr/bin/python \
 && rm -rf /var/lib/apt/lists/*

# wasm-tools (Emscripten + AOT) is the slow, source-independent layer — install it before
# copying any code so it caches across rebuilds.
RUN dotnet workload install wasm-tools

# Restore against just the project file so a source-only change doesn't re-restore. The
# csproj pulls the engine in from ../sbox-library, so the build context is the repo root.
COPY wasm/Skafinity.Wasm.csproj wasm/runtimeconfig.template.json wasm/
RUN dotnet restore wasm/Skafinity.Wasm.csproj

COPY wasm/ wasm/
COPY sbox-library/ sbox-library/
RUN dotnet publish wasm/Skafinity.Wasm.csproj -c Release -o /publish

# ── Runtime stage: nginx serving the static bundle ──────────────────────────
FROM nginx:1.27-alpine

# wasm MIME type ships in this nginx's mime.types; conf just sets the docroot + caching.
COPY docker/nginx.conf /etc/nginx/conf.d/default.conf

# Static page + glue (index.html, engine.js, app.js, queue.js, worker.js, style.css). web/_framework
# is excluded via .dockerignore so only the freshly-built bundle below lands in the image.
COPY web/ /usr/share/nginx/html/
COPY --from=builder /publish/wwwroot/_framework /usr/share/nginx/html/_framework

# Overwrite web/config.json with the canonical house-mix config, exactly as `make stage`
# does — otherwise the image would ship whatever committed/local copy the context happened
# to hold, which can lag the canonical one.
COPY sbox-library/Skafinity/skafinity.config.json /usr/share/nginx/html/config.json

EXPOSE 80
