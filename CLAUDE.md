# CLAUDE.md — Parsen

Este arquivo orienta o Claude Code neste repositório. Leia-o inteiro antes de qualquer tarefa.

## O que é a Parsen

Plataforma de IA para extração e estruturação de dados de documentos brasileiros ("Documentos em dados").
Nichos iniciais: fintechs (comprovação de renda via API) e imobiliário (análise de dossiê no backoffice).
A especificação completa está em `docs/spec.md`. Em caso de dúvida, a spec manda; se a spec estiver ambígua, pergunte antes de implementar.

## Arquitetura (resumo)

- Totalmente serverless e assíncrona. Desenvolvimento e produção em `sa-east-1`. O Textract não existe em `sa-east-1`: é chamado em `us-east-1` (parâmetro `TextractRegion`) com os bytes da página. Regiões e IDs de modelo do Bedrock são parâmetros, nunca fixos no código.
- O cliente cria o dossiê na API, recebe URL pré-assinada e envia os arquivos direto ao S3.
- No `submit`, a API publica `DossierSubmitted` no EventBridge, que inicia uma execução do Step Functions por dossiê: ingestão (PDF → PNG) → split → classificação → OCR (Textract) → extração (Claude Haiku 4.5 via Bedrock) → verificação (regras C# + sinais do Claude Sonnet 5) → resultado (DynamoDB + webhook).
- Modo "renda expressa" para fintech: síncrono, até 6 arquivos e 20 páginas (holerite e extrato) inline, só Haiku, sem Step Functions, resposta em segundos; antifraude completa roda depois e avisa por webhook.
- O backoffice Angular é só mais um cliente da API pública. Não existem endpoints exclusivos do backoffice.
- **A Parsen verifica fatos; a política é do cliente.** Ela devolve dados, verificações (`checks`), fatos (`facts`) e sinais (`signals`); nunca aprova nem reprova. Limites de política ficam em `TenantPolicy` com valores padrão; configuração pelo cliente só na fase 2.

## Stack

- .NET 10 (LTS), C#, ASP.NET Core Minimal API em Lambda
- API Gateway REST com usage plan por tenant e WAF; Cognito (Pre Token Generation injeta `tenantId`)
- Workers do pipeline: Lambdas .NET com Native AOT, uma por etapa; build sempre em container Amazon Linux 2023 (glibc do runtime), nunca direto no Windows
- AWS SDK: BedrockRuntime (Converse), Textract, DynamoDBv2 (cliente de baixo nível nos workers AOT), S3, EventBridge, StepFunctions
- Powertools for AWS Lambda (.NET): logging e idempotência (`Idempotency-Key`)
- Infra: AWS CDK em C# (`infra/Parsen.Cdk`)
- Front: Angular + PDF.js (`web/backoffice`)
- Testes: xUnit; DynamoDB Local/Testcontainers para integração
- CI/CD: GitHub Actions com OIDC para a AWS (sem chaves de acesso fixas)

## Estrutura do repositório

```
src/
  Parsen.Api/              # Minimal API pública
  Parsen.Domain/           # Entidades, verificações, cálculo de renda, TenantPolicy (sem dependência de AWS)
  Parsen.Application/      # Casos de uso e portas (interfaces)
  Parsen.Infrastructure/   # Implementações: DynamoDB, S3, Bedrock, Textract, webhooks
  Parsen.Workers/          # Uma Lambda por etapa: Ingest, Split, Classify, Ocr, Extract, Validate, Notify
  Parsen.Schemas/          # JSON Schemas e prompts versionados por tipo de documento
web/backoffice/            # Angular
infra/Parsen.Cdk/          # Stacks: Network, Data, Api, Pipeline, Web
tests/
  Parsen.Domain.Tests/
  Parsen.Api.Tests/
  Parsen.Infrastructure.Tests/  # Integração com DynamoDB Local (Testcontainers; exige Docker)
  Parsen.Eval/             # Avaliação de precisão por campo contra gabarito (fixtures/ só sintéticos)
docs/
  spec.md                  # Especificação técnica do MVP
```

## Comandos

```bash
dotnet build                          # build da solução
dotnet test                           # todos os testes
dotnet test tests/Parsen.Domain.Tests # só regras de domínio
dotnet run --project tests/Parsen.Eval # relatório de precisão da extração
cd infra/Parsen.Cdk && cdk synth      # gerar CloudFormation
cd infra/Parsen.Cdk && cdk diff       # ver mudanças antes do deploy
cd web/backoffice && npm start        # backoffice local
```

## Regras inegociáveis

1. **`Parsen.Domain` não referencia nenhum pacote AWS.** Regras e cálculos são testáveis sem rede.
2. **Multi-tenant:** o `tenantId` vem sempre do token (claim), nunca do corpo ou da query. Toda PK no DynamoDB começa com `TENANT#{tenantId}` e o repositório exige o `TenantId` em toda operação; todo objeto no S3 fica sob o prefixo do tenant.
3. **Sem dados pessoais em logs nem em URLs:** mascarar CPF, CNPJ, nomes, valores de renda e endereços. Logar IDs, não conteúdo. CPF e afins vão no corpo, nunca em rota ou query.
4. **Sem segredos no código ou em variáveis de ambiente em texto puro:** usar Secrets Manager.
5. **Sem documentos reais no repositório.** Fixtures usam apenas documentos sintéticos; o dataset anonimizado do piloto fica em bucket S3 privado, fora do repositório.
6. **Nem o modelo de IA nem a Parsen decidem aprovação.** O modelo extrai e sinaliza; verificações e fatos nascem de regras determinísticas em C# (seção 10 da spec); a decisão é do cliente. Não implemente status de aprovação automática nem limites de política fora de `TenantPolicy`.
7. **Infraestrutura só via CDK.** Nada de criar recurso pelo console para o produto.
8. **Toda mudança em prompt ou schema** incrementa a versão (`holerite.v2.json`) e roda `Parsen.Eval` antes do merge.

## Convenções

- Identificadores de código (classes, métodos, variáveis, rotas da API) em inglês.
- Nomes de campos extraídos de documentos brasileiros em português, `snake_case` (ex.: `salario_liquido`, `renda_comprovada`), por serem termos do domínio.
- JSON da API: chaves estruturais e status em inglês (`status`, `checks`, `confidence`); campos extraídos e códigos de regra em português.
- Códigos de regra em maiúsculas, em português (ex.: `CPF_VALIDO`, `SOMA_HOLERITE`).
- IDs e chaves de ordenação temporais com ULID; erros em Problem Details (RFC 9457); paginação por cursor.
- Valores monetários como `decimal` em C#; nunca `double`.
- Datas em UTC no armazenamento; conversão para `America/Sao_Paulo` só na apresentação.
- Validação de CPF/CNPJ e cálculos de renda ficam em `Parsen.Domain`, com testes.
- Endpoints e webhooks idempotentes; webhooks assinados com HMAC-SHA256.

## Como trabalhar

- Uma fase do roadmap por vez; tarefas pequenas, um assunto por commit.
- Para verificações e cálculos: escreva os testes primeiro, depois a implementação.
- Rode `dotnet build` e `dotnet test` antes de dar uma tarefa como concluída.
- **Custo do Claude é real** (os créditos AWS não cobrem o Marketplace): testes automatizados e CI nunca chamam o Bedrock, usam fixtures JSON; só o `Parsen.Eval` e testes manuais chamam o modelo, e só com confirmação. Toda chamada define `maxTokens`; retries limitados a 3; prompt caching sempre; só Haiku até a fase 3.
- Comandos AWS sempre com `--profile parsen` (o perfil `default` da máquina é de outra conta).
- Antes de qualquer `cdk deploy`, mostre o `cdk diff` e peça confirmação.
- Nunca apague recursos, tabelas ou buckets sem confirmação explícita.
- Se algo na spec parecer errado ou faltar informação, pergunte; não invente regra de negócio.

## Status

Fase atual: 1 — Fundação (em andamento). Fase 0 concluída (pendente só trocar o e-mail do root para o domínio).
Atualize esta linha ao avançar de fase.
