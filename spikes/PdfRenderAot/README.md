# Spike A — PDFium em Native AOT na Lambda

**Pergunta:** uma Lambda .NET 10 compilada em Native AOT consegue renderizar PDF em PNG com PDFium?

**Resposta: sim** (13/10/2026), em linux-x64 e linux-arm64 (Graviton).

## Como foi testado

- Build Native AOT dentro de `amazonlinux:2023` (mesma glibc 2.34 da Lambda), com clang e .NET 10 SDK
- Execução na imagem `public.ecr.aws/lambda/provided:al2023`, a base do runtime das Lambdas com runtime próprio
- Biblioteca `PDFtoImage` 5.4.0 (PDFium + SkiaSharp); nenhum aviso de AOT ou trimming no publish
- Documento: holerite próprio, guardado fora do repositório (regra 5)

## Resultados

| Medida | Valor |
| --- | --- |
| Executável nativo | 1,8 MB |
| Bibliotecas nativas | `libpdfium.so` 7,6 MB + `libSkiaSharp.so` 11,7 MB |
| Pacote total da Lambda (sem `.dbg`) | ~21 MB (limite da Lambda: 250 MB descompactado) |
| Renderização de 1 página a 150 DPI | ~165 ms, PNG de ~215 KB |

### arm64 (Graviton)

- Build com `--platform linux/arm64`, emulado pelo QEMU do Docker Desktop: ~11 min (x64: ~2 min)
- Executável e bibliotecas nativas gerados para AArch64 (cabeçalho ELF `0xB7`); pacote de tamanho equivalente
- PNG idêntico byte a byte ao gerado em x64
- Tempo medido (~800 ms) não é representativo: rodou emulado; medir na Lambda real

## Conclusões para o worker Ingest

- Ingest pode ser Native AOT como os demais workers; a exceção prevista na spec não é necessária
- O build das Lambdas AOT precisa rodar em Amazon Linux 2023 (container), nunca direto no Windows
- O `.dbg` (símbolos) não deve ir no pacote da Lambda
- arm64 (Graviton, ~20% mais barato) é viável; no Windows x64 o build é lento por emulação. No CI, avaliar runner ARM do GitHub Actions (build nativo; disponibilidade e custo dependem do plano e da visibilidade do repositório) antes de recorrer à emulação

## Como reproduzir

```powershell
docker build -t parsen-spike-pdf spikes/PdfRenderAot
docker build --platform linux/arm64 -t parsen-spike-pdf:arm64 spikes/PdfRenderAot   # Graviton
docker run --rm -v <pasta-com-pdf>:/in:ro -v <pasta-de-saida>:/out parsen-spike-pdf /in/<arquivo>.pdf /out
```

Use pastas fora do repositório: o PNG gerado contém os dados do documento.
