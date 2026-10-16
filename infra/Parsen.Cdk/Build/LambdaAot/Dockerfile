# Imagem de build das Lambdas Native AOT, usada pelo bundling do CDK.
# Amazon Linux 2023: mesma glibc do runtime provided.al2023 da Lambda (spike A).
FROM public.ecr.aws/amazonlinux/amazonlinux:2023

# clang e zlib-devel: exigidos pelo Native AOT; demais: ferramentas do instalador e do SDK
RUN dnf install -y clang zlib-devel tar gzip findutils libicu && dnf clean all

RUN curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
    && rm /tmp/dotnet-install.sh

ENV PATH="${PATH}:/usr/share/dotnet" \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_HOME=/tmp \
    NUGET_PACKAGES=/tmp/nuget
