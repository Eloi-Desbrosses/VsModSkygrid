FROM mcr.microsoft.com/dotnet/sdk:10.0

ARG VS_VERSION=1.22.2

ENV VINTAGE_STORY=/opt/vintagestory
ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update \
 && apt-get install -y --no-install-recommends curl ca-certificates \
 && rm -rf /var/lib/apt/lists/*

RUN mkdir -p $VINTAGE_STORY \
 && curl -fSL "https://cdn.vintagestory.at/gamefiles/stable/vs_server_linux-x64_${VS_VERSION}.tar.gz" \
      -o /tmp/vs.tar.gz \
 && tar -xzf /tmp/vs.tar.gz -C $VINTAGE_STORY --strip-components=0 \
 && rm /tmp/vs.tar.gz \
 && ls $VINTAGE_STORY/VintagestoryAPI.dll

WORKDIR /src
