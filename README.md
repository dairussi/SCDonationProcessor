# SCDonationProcessor

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![C#](https://img.shields.io/badge/C%23-12.0-239120?logo=csharp)
![RabbitMQ](https://img.shields.io/badge/RabbitMQ-3-FF6600?logo=rabbitmq)
![Worker Service](https://img.shields.io/badge/tipo-Worker%20Service-informational)
![Kubernetes](https://img.shields.io/badge/Kubernetes-EKS%20%7C%20Local-326CE5?logo=kubernetes)
![License](https://img.shields.io/badge/license-academic--project-lightgrey)

> Worker de processamento de doações do ecossistema **SolidarityConnection (Conexão Solidária)**.

Este repositório contém o serviço responsável por processar de forma assíncrona as doações recebidas pela API principal, decidir o resultado do processamento e devolver esse resultado via mensageria.

## Sumário

- [Sobre o serviço](#sobre-o-serviço)
- [Papel no ecossistema](#papel-no-ecossistema)
- [Stack técnica](#stack-técnica)
- [Estrutura do repositório](#estrutura-do-repositório)
- [Regra de processamento](#regra-de-processamento)
- [Confiabilidade da fila](#confiabilidade-da-fila)
- [Como subir o projeto](#como-subir-o-projeto)
- [Como subir na AWS](#como-subir-na-aws)
- [Secrets e variáveis de ambiente](#secrets-e-variáveis-de-ambiente)
- [Observabilidade](#observabilidade)
- [Repositórios relacionados](#repositórios-relacionados)

## Sobre o serviço

O `SCDonationProcessor` é um **Worker Service** (.NET Generic Host, sem interface HTTP de API) que fica escutando uma fila do RabbitMQ. Ele não tem banco de dados próprio nem expõe endpoints de negócio — sua única responsabilidade é consumir o evento de uma doação recebida, decidir (hoje, de forma simulada) se ela foi aprovada, ficou pendente ou foi rejeitada, e publicar o resultado de volta em outra fila para que a API principal atualize o estado da doação.

Este é um dos três repositórios do projeto SolidarityConnection — para o contexto completo da plataforma (regras de negócio, autenticação, dashboard de transparência), veja o repositório principal [SolidarityConnection](https://github.com/dairussi/SolidarityConnection).

## Papel no ecossistema

```
[SolidarityConnection API]
        │
        ▼ publica DonationReceivedEvent
   fila: donation-received
        │
        ▼
[SCDonationProcessor Worker]   ◄── você está aqui
        │
        ▼ publica DonationProcessedEvent
   fila: donation-processed
        │
        ▼
[SolidarityConnection API]  (consome e atualiza SQL Server + MongoDB)
```

1. A API publica um `DonationReceivedEvent` na fila `donation-received` assim que uma doação é registrada (endpoint `POST /api/donation`).
2. Este Worker consome essa mensagem, decide o resultado do processamento (`DonationProcessingService`) e monta um `DonationProcessedEvent`.
3. O Worker publica esse evento na fila `donation-processed`.
4. A própria API consome essa fila e atualiza o status da doação, o saldo da campanha e o dashboard de transparência.

O Worker não persiste nada em banco de dados — ele é *stateless* em relação aos dados de negócio; todo o estado vive na API principal.

## Stack técnica

| Camada | Tecnologia |
|---|---|
| Linguagem / Runtime | C# / .NET 8 (Worker Service — `Microsoft.NET.Sdk.Worker`) |
| Mensageria | RabbitMQ (RabbitMQ.Client 6.8.1), consumo manual com ack/nack |
| Observabilidade | Prometheus (`prometheus-net`, servidor de métricas dedicado na porta `9091`), OpenTelemetry (traces exportados para o Jaeger) |
| Orquestração | Kubernetes (EKS em produção, Docker Desktop em ambiente local) |
| CI/CD | GitHub Actions |

**Arquitetura de software**: mesma linha do repositório principal — camadas Domain / Application / Infrastructure / EventProcessor (host), com portas e adaptadores (`IEventPublisher`, `IDonationProcessingService`).

> Este repositório não possui suíte de testes automatizados no momento.

## Estrutura do repositório

```
SCDonationProcessor/
├── SCDonationProcessor.Domain/           # Enum de status da doação, evento de saída (DonationProcessedEvent)
├── SCDonationProcessor.Application/      # Evento de entrada (DonationReceivedEvent), regra de processamento
├── SCDonationProcessor.Infrastructure/   # Consumer RabbitMQ, publisher RabbitMQ, HostedService, opções de configuração
├── SCDonationProcessor.EventProcessor/   # Host executável (Program.cs, Dockerfile, appsettings)
└── .github/workflows/main.yml            # Pipeline de CI/CD (build, push da imagem, deploy no EKS)
```

## Regra de processamento

Hoje a decisão de aprovação é **simulada** (não há integração real com um meio de pagamento) — implementada em `DonationProcessingService.ProcessDonationAsync`, através de um sorteio numérico:

| Resultado | Probabilidade | Status resultante |
|---|---|---|
| Aprovada | 70% | `Paid` |
| Pendente | 20% | `Pending` |
| Rejeitada | 10% | `Rejected` |

Doações que caem em `Pending` acabam sendo reprocessadas depois pelo job `PendingDonationReprocessingJob`, que roda periodicamente na API principal.

> Se a intenção futura for plugar um provedor de pagamento real no lugar do sorteio, o ponto de extensão é exatamente essa classe (`IDonationProcessingService`).

## Confiabilidade da fila

O consumo da fila `donation-received` é feito com **ack manual** (`autoAck: false`) e `prefetchCount: 1` (processa uma mensagem por vez antes de buscar a próxima):

- **Sucesso** → `BasicAck` (mensagem removida da fila);
- **Erro inesperado durante o processamento** → `BasicNack` com `requeue: true` (a mensagem volta para o fim da fila e será tentada novamente);
- **Mensagem que não pôde ser desserializada** (payload corrompido/incompatível) → `BasicReject` com `requeue: false` (descartada, para não travar a fila indefinidamente com uma mensagem "envenenada").

Cada mensagem processada também incrementa um contador Prometheus (`worker_messages_processed_total`), com rótulo `status` (`success`, `error` ou `deserialization_failed`), útil para monitorar a taxa de erro/reprocessamento no Grafana.

O contexto de trace (OpenTelemetry `traceparent`) é propagado através dos headers da mensagem RabbitMQ, então é possível acompanhar, no Jaeger, o trace completo desde a chamada `POST /api/donation` na API até o processamento aqui no Worker e a volta do evento processado.

## Como subir o projeto

Assim como a API, este Worker **não sobe isolado via `docker run`/`docker-compose`** — ele faz parte do mesmo ambiente orquestrado pelo repositório [SolidarityConnectionDeployFile](https://github.com/dairussi/SolidarityConnectionDeployFile), que builda a imagem deste repositório e da API juntas, dentro de um cluster Kubernetes local (Docker Desktop).

### Pré-requisitos

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) com **Kubernetes habilitado**
- `kubectl`
- Git Bash (Windows) ou um shell bash (Linux/macOS)
- .NET 8 SDK (opcional, apenas para compilar localmente fora de containers)

### Passo a passo

1. Clone os três repositórios lado a lado (veja o passo a passo completo no README do [SolidarityConnection](https://github.com/dairussi/SolidarityConnection#como-subir-o-projeto)):
   ```bash
   git clone https://github.com/dairussi/SolidarityConnection.git
   git clone https://github.com/dairussi/SCDonationProcessor.git
   git clone https://github.com/dairussi/SolidarityConnectionDeployFile.git
   ```

2. Configure `SolidarityConnectionDeployFile/k8s-local/.env.local`, apontando `WORKER_REPO_PATH` para a raiz deste repositório (onde está o `.sln`).

3. A partir de `SolidarityConnectionDeployFile/k8s-local`, suba tudo:
   ```bash
   kubectl config use-context docker-desktop
   ./subir_local.sh
   ```
   O script builda a imagem `sc-donation-processor:local` a partir do `SCDonationProcessor.EventProcessor/Dockerfile`, sobe o RabbitMQ (dependência deste Worker) e por fim aplica o Deployment do Worker.

4. **Acompanhar o Worker rodando:**
   ```bash
   kubectl get pods -n solidarity-connection-namespace -l app=sc-donation-processor
   kubectl logs -f <nome-do-pod> -n solidarity-connection-namespace
   ```
   Você deve ver o log `Worker escutando a fila donation-received.` e, a cada doação criada pela API, os logs de recebimento e de publicação do evento processado.

5. **Métricas do Worker** ficam expostas em `/metrics` na porta `9091` (internamente ao cluster, via o Service `sc-donation-processor-metrics`), e já são coletadas automaticamente pelo Prometheus.

6. Para derrubar o ambiente:
   ```bash
   ./limpar_local.sh
   ```

### Rodando localmente sem Kubernetes (debug pontual)

Caso queira apenas compilar/rodar o Worker isoladamente (ex: para ler o código no Visual Studio), ele precisa de um RabbitMQ acessível na string de conexão configurada em `appsettings.Development.json` (por padrão, `localhost:5672`). Sem um RabbitMQ real acessível, o Worker não conseguirá iniciar a conexão.

## Como subir na AWS

O deploy em produção é feito pelo pipeline de CI/CD deste repositório (`.github/workflows/main.yml`), assumindo que a infraestrutura compartilhada (cluster EKS, RabbitMQ, namespace) já foi provisionada pelo repositório [SolidarityConnectionDeployFile](https://github.com/dairussi/SolidarityConnectionDeployFile) — veja a seção *"Como subir na AWS"* do README do [SolidarityConnection](https://github.com/dairussi/SolidarityConnection#como-subir-na-aws-eks--api-gateway) para os passos de provisionamento do RDS/EKS (o Worker não usa o RDS, mas depende do mesmo cluster e do mesmo RabbitMQ).

Um `push` na branch `main` (ou disparo manual via `workflow_dispatch`) executa:

1. **Build & Push**: builda o `.sln`, faz login no Docker Hub e builda/publica a imagem `sc-donation-processor-image` (tags `latest` e pelo SHA do commit) a partir de `SCDonationProcessor.EventProcessor/Dockerfile`;
2. **Deploy**: configura credenciais AWS, atualiza o kubeconfig do cluster `solidarity-connection-eks`, aplica `namespace.yaml`, `configmap.yaml`, `service.yaml` e `deployment.yaml`, cria/atualiza o Secret `sc-donation-processor-secret` (credenciais do RabbitMQ), atualiza a imagem do Deployment para a tag do commit e aguarda o rollout finalizar.

> Diferente da API, o Worker não tem Job de migration (não possui banco de dados próprio) nem passa por scan de vulnerabilidade (Trivy) no pipeline atual.

Para verificar o Worker rodando no EKS:
```bash
kubectl get pods -n solidarity-connection-namespace -l app=sc-donation-processor
kubectl logs <nome-do-pod> -n solidarity-connection-namespace --tail=200
```

## Secrets e variáveis de ambiente

> ⚠️ Todos os valores estão mascarados. Nunca faça commit de senhas reais.

### Configuração da aplicação (`appsettings.json` / ConfigMap)

| Chave | Descrição |
|---|---|
| `RabbitMQ:Host` / `Port` / `Username` / `Password` | Conexão com o RabbitMQ |
| `RabbitMQ:DonationReceivedQueue` | Fila consumida pelo Worker (`donation-received`) |
| `RabbitMQ:DonationProcessedQueue` | Fila em que o Worker publica o resultado (`donation-processed`) |
| `Jaeger:OtlpEndpoint` | Endpoint OTLP para exportação de traces |

### Secrets configurados no GitHub Actions

| Secret | Descrição |
|---|---|
| `DOCKER_USERNAME` / `DOCKER_PASSWORD` | Credenciais do Docker Hub para push da imagem |
| `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` / `AWS_SESSION_TOKEN` | Credenciais temporárias da AWS usadas para deploy no EKS |
| `RABBITMQ_USERNAME` / `RABBITMQ_PASSWORD` | Credenciais usadas para popular o Secret `sc-donation-processor-secret` no cluster |

### Credenciais de ambiente local (`.env.local`, ver [SolidarityConnectionDeployFile](https://github.com/dairussi/SolidarityConnectionDeployFile))

| Variável | Observação |
|---|---|
| `RABBITMQ_USERNAME` / `RABBITMQ_PASSWORD` | `********` — mesmas credenciais usadas pela API para se conectar ao mesmo RabbitMQ |

## Observabilidade

| Ferramenta | Como este serviço se integra |
|---|---|
| Prometheus | Métricas expostas em `/metrics:9091`, incluindo `worker_messages_processed_total{status=...}` e métricas de runtime .NET (GC, memória, threads) |
| Grafana | Consome as métricas acima (mesmo Prometheus compartilhado com a API) |
| Jaeger | Traces do processamento de cada doação, correlacionados com o trace iniciado na API via propagação de contexto nos headers da mensagem RabbitMQ |

## Repositórios relacionados

- 🧩 **API + Frontend (regras de negócio)**: [SolidarityConnection](https://github.com/dairussi/SolidarityConnection)
- ☁️ **Infraestrutura (Kubernetes local + AWS/EKS)**: [SolidarityConnectionDeployFile](https://github.com/dairussi/SolidarityConnectionDeployFile)
