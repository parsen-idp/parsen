# ---------- Etapa 1: build Native AOT no mesmo Linux da Lambda (Amazon Linux 2023) ----------
# Compilar aqui garante que o binário use a mesma glibc do runtime da Lambda.
FROM public.ecr.aws/amazonlinux/amazonlinux:2023 AS build

# clang e zlib-devel: exigidos pelo Native AOT para gerar e linkar o executável
RUN dnf install -y clang zlib-devel tar gzip findutils libicu && dnf clean all

# .NET 10 SDK via script oficial (o Amazon Linux não traz o SDK 10 pronto)
RUN curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet
ENV PATH="${PATH}:/usr/share/dotnet" \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

WORKDIR /src
COPY . .

# TARGETARCH é preenchido pelo Docker conforme --platform (amd64 ou arm64)
ARG TARGETARCH
RUN case "$TARGETARCH" in \
      arm64) RID=linux-arm64 ;; \
      *)     RID=linux-x64 ;; \
    esac \
    && echo "Publicando para $RID" \
    && dotnet publish -c Release -r "$RID" -o /out

# ---------- Etapa 2: imagem base do runtime da Lambda (provided.al2023) ----------
# É a imagem mínima usada por Lambdas com runtime próprio, como as de Native AOT.
FROM public.ecr.aws/lambda/provided:al2023
COPY --from=build /out/ /var/task/
ENTRYPOINT ["/var/task/PdfRenderAot"]
