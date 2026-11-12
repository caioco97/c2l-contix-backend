# Contix API

Backend da plataforma **Contix**, que conecta escritórios de contabilidade aos seus clientes (empresas): gestão de clientes, pastas e documentos, com portal do cliente.

Stack: **.NET 10**, ASP.NET Core Web API, EF Core 10 + PostgreSQL, Clean Architecture, FluentValidation, JWT com refresh token rotativo, Serilog, Swagger/OpenAPI, Docker.

## Subindo o ambiente

### Com Docker (API + PostgreSQL)

```bash
cp .env.example .env        # ajuste JWT_SIGNING_KEY
docker compose up -d --build
```

- API: http://localhost:5080
- Swagger: http://localhost:5080/swagger
- Health check: http://localhost:5080/health
- Mailpit (caixa de entrada dos e-mails de desenvolvimento): http://localhost:8025

Com `DB_SEED_DEMO=true` são criados um escritório, uma empresa ("Empresa Demonstração Ltda") com pastas padrão e um documento com vencimento no mês atual, além dos usuários abaixo (senha `Contix@123`):

| Perfil | E-mail |
| --- | --- |
| Administrador do escritório | `admin@contix.dev` |
| Colaborador do escritório | `colaborador@contix.dev` |
| Usuário do cliente | `cliente@contix.dev` |

### Rodando a API localmente

Pré-requisitos: .NET SDK 10 e um PostgreSQL acessível (pode ser só o do compose: `docker compose up -d db`).

```bash
dotnet run --project src/Contix.Api     # http://localhost:5080/swagger
```

Em `Development` a API aplica as migrations e o seed automaticamente (`appsettings.Development.json`).

### Testes

```bash
dotnet test
```

Os testes de integração usam [Testcontainers](https://dotnet.testcontainers.org/) e precisam do Docker em execução. Sem Docker, aponte para um PostgreSQL descartável:

```bash
CONTIX_TEST_CONNECTION="Host=localhost;Database=contix_tests;Username=contix;Password=contix" dotnet test
```

### Migrations

```bash
dotnet tool restore
dotnet ef migrations add <Nome> -p src/Contix.Infrastructure -s src/Contix.Infrastructure -o Persistence/Migrations
dotnet ef database update -p src/Contix.Infrastructure -s src/Contix.Infrastructure
```

## Arquitetura

```
src/
  Contix.Domain          Entidades, enums, regras de domínio (sem dependências)
  Contix.Application     Casos de uso (services), DTOs, validações, interfaces de repositório/infra
  Contix.Infrastructure  EF Core/PostgreSQL, repositórios, Unit of Work, JWT, hash de senha, storage
  Contix.Api             Controllers, autenticação/autorização, Swagger, middlewares, Serilog
tests/
  Contix.UnitTests         Domínio, validações, AuthService, isolamento de tenant (EF InMemory)
  Contix.IntegrationTests  API completa contra PostgreSQL real (WebApplicationFactory + Testcontainers)
```

Dependências apontam para dentro: `Api → Application → Domain` e `Infrastructure → Application/Domain`.

### Decisões principais

- **Multi-tenant por escritório.** Toda entidade de negócio herda `TenantEntity` (`TenantId`, soft delete). Um filtro global do EF restringe as consultas ao `tenant_id` do token; sem usuário autenticado nenhuma linha é retornada. O `AuditInterceptor` preenche `TenantId`, `CreatedAt`/`UpdatedAt` e converte `DELETE` em soft delete.
- **Perfis:** `Administrador`, `Colaborador` (escritório) e `Cliente` (portal). Usuários do cliente só acessam empresas às quais estão vinculados; acesso a outra empresa retorna **404** para não revelar sua existência.
- **Autenticação:** access token JWT de 15 min + refresh token de 7 dias, opaco, armazenado apenas como hash SHA-256. Cada refresh rotaciona o token; reutilizar um token já rotacionado revoga a cadeia inteira. Bloqueio de 15 min após 5 senhas erradas e rate limit nos endpoints de autenticação.
- **Erros** sempre em `application/problem+json` (ProblemDetails), com `errors` por campo em validações e `traceId` (= header `X-Correlation-Id`). Stack trace só em `Development`.
- **Documentos:** armazenados via `IFileStorage` (disco local por padrão; S3/Azure Blob podem ser adicionados implementando a interface). Tamanho máximo e extensões permitidas configuráveis na seção `Documentos`.
- **JSON:** camelCase, enums como string, datas ISO 8601 (`DateOnly` como `yyyy-MM-dd`).

## Endpoints (v1)

Todos sob `/api/v1`. Detalhes de payloads no Swagger.

| Método | Rota | Quem | Descrição |
| --- | --- | --- | --- |
| POST | `/auth/login` | público | Login com e-mail e senha |
| POST | `/auth/refresh` | público | Rotaciona o refresh token |
| POST | `/auth/logout` | público | Revoga o refresh token |
| POST | `/auth/registrar` | público | Onboarding: cria escritório + administrador |
| GET | `/auth/me` | autenticado | Usuário atual e escritório (nome, logo) |
| GET / PUT | `/escritorio` | todos / admin | Dados do escritório |
| GET / POST | `/usuarios` | escritório | Lista / cria usuários (colaborador só cria usuários do cliente) |
| GET / PUT | `/usuarios/{id}` | escritório | Detalhe / atualiza perfil, situação e empresas vinculadas |
| GET | `/empresas` | autenticado | Busca paginada (cliente vê só as vinculadas) |
| POST | `/empresas` | escritório | Cadastra empresa e cria as pastas padrão |
| GET / PUT / DELETE | `/empresas/{id}` | — / escritório / admin | Detalhe / atualiza / exclusão lógica |
| GET / POST | `/empresas/{id}/pastas` | autenticado / escritório | Árvore de pastas com não lidos / cria subpasta |
| PUT / DELETE | `/empresas/{id}/pastas/{pastaId}` | escritório | Renomeia / exclui pasta vazia |
| GET | `/empresas/{id}/documentos` | autenticado | Lista com filtros `pastaId`, `busca`, `status` (`Todos`, `NaoLidos`, `Lidos`), `orderBy`, `desc`, `page`, `pageSize` |
| POST | `/empresas/{id}/documentos` | autenticado | Upload multipart (`pastaId`, `arquivos[]`, `comentario`, `competencia`, `dataVencimento`) |
| GET | `/empresas/{id}/documentos/{docId}` | autenticado | Detalhe |
| GET | `/empresas/{id}/documentos/{docId}/download` | autenticado | Baixa e marca como lido |
| POST | `/empresas/{id}/documentos/download` | autenticado | Baixa vários em `.zip` (`{ documentoIds: [] }`) |
| POST | `/empresas/{id}/documentos/{docId}/lido` | autenticado | Marca como lido |
| DELETE | `/empresas/{id}/documentos/{docId}` | autenticado | Exclusão lógica (cliente só os que enviou) |
| GET | `/empresas/{id}/documentos/vencimentos?inicio&fim` | autenticado | Calendário de vencimentos (padrão: mês atual) |
| GET | `/solicitacoes/tipos` | autenticado | Catálogo de tipos de solicitação (departamento, prazo padrão, se exige anexo) |
| GET | `/solicitacoes` | escritório | Fila de atendimento de todas as empresas. Filtros `empresaId`, `tipo`, `status`, `emAndamento`, `departamento`, `responsavelId`, `semResponsavel`, `atrasadas`, `busca`; `orderBy` = `numero`, `prazo`, `status`, `atualizado`, `createdAt` |
| GET / POST | `/empresas/{id}/solicitacoes` | autenticado | Solicitações da empresa / abre uma (multipart: `tipo`, `assunto`, `departamento`, `descricao`, `dados` (JSON), `anexos[]`) |
| GET | `/empresas/{id}/solicitacoes/{solId}` | autenticado | Detalhe com histórico, anexos e `transicoesPermitidas` para o usuário |
| POST | `/empresas/{id}/solicitacoes/{solId}/comentarios` | autenticado | Comentário com anexos (multipart: `mensagem`, `anexos[]`) |
| POST | `/empresas/{id}/solicitacoes/{solId}/status` | autenticado | Muda o status (`{ status, mensagem }`); cliente só cancela |
| PUT | `/empresas/{id}/solicitacoes/{solId}/atribuicao` | escritório | Responsável e prazo (`{ responsavelId, prazo }`) |
| GET | `/empresas/{id}/solicitacoes/{solId}/anexos/{anexoId}/download` | autenticado | Baixa um anexo |

### Solicitações de serviço

- **Tipos:** Geral, Cadastro de Empregado/Contribuinte/Estagiário, Aviso e Cálculo de Férias, Aviso e Cálculo de Rescisão, Afastamento e Lançamento de Rubricas. Os campos específicos de cada tipo trafegam em `dados` (JSON) e são validados no servidor; erros voltam como `dados.<campo>` (ex.: `dados.cpf`, `dados.lancamentos[0].valor`).
- **Fluxo:** `Aberta → EmAnalise → AguardandoCliente → Concluida / Cancelada`. O escritório pode reabrir uma finalizada. Pedir algo ao cliente (`AguardandoCliente`) exige mensagem; quando o cliente responde, a solicitação volta sozinha para `EmAnalise`. O cliente só pode cancelar enquanto está `Aberta` ou `AguardandoCliente`.
- **Histórico:** abertura, comentários, mudanças de status e atribuições ficam registrados com autor e data; anexos ficam ligados ao item do histórico em que foram enviados.
- **Número:** sequencial por escritório (`#1`, `#2`...), reservado de forma atômica.
- **Prazo (SLA):** definido pelo prazo padrão do tipo na abertura e ajustável pelo escritório; `atrasada` indica prazo vencido sem conclusão.

### Notícias e comunicados

| Método | Rota | Perfil | Descrição |
| --- | --- | --- | --- |
| GET | `/noticias` | escritório | Lista com situação (`Agendada`, `Publicada`, `Expirada`) e alcance (`empresasLeram` / `empresasDestinatarias`) |
| POST / PUT | `/noticias[/{id}]` | escritório | Multipart: `titulo`, `resumo`, `conteudo`, `publicarEm`, `expiraEm`, `publico`, `empresaIds`, `regimes`, `imagem`, `anexos`; na edição, `anexosRemovidos` e `removerImagem` |
| DELETE | `/noticias/{id}` | escritório | Exclusão lógica |
| GET | `/noticias/{id}/leituras` | escritório | Empresas destinatárias e se o cliente já leu |
| GET | `/empresas/{id}/noticias?status=NaoLidos\|Lidos` | autenticado | Notícias publicadas para a empresa |
| GET / POST | `/empresas/{id}/noticias/{noticiaId}` · `/lida` | autenticado | Detalhe · marca como lida (idempotente) |
| GET | `/empresas/{id}/noticias/{noticiaId}/imagem` · `/anexos/{anexoId}/download` | autenticado | Imagem e anexos |

- **Público:** `Todos` (inclui empresas cadastradas depois), `EmpresasSelecionadas` ou `RegimeTributario`. Notícias agendadas, expiradas ou de outro público respondem 404 no portal.
- **Leitura:** registrada por usuário e empresa. O alcance conta só leituras de usuários do cliente; a pré-visualização pelo escritório não infla o número.
- **Conteúdo:** texto puro com parágrafos (sem HTML, para não abrir brecha de XSS). Sem `resumo`, o portal mostra o início do conteúdo.

### Certidões (CND)

| Método | Rota | Perfil | Descrição |
| --- | --- | --- | --- |
| GET | `/empresas/{id}/certidoes?somenteAtuais=true&tipo=` | autenticado | Histórico ou só a certidão atual de cada tipo |
| POST / PUT / DELETE | `/empresas/{id}/certidoes[/{certidaoId}]` | escritório | Multipart: `tipo`, `situacao`, `numero`, `emissao`, `validade`, `observacao`, `arquivo` (`removerArquivo` na edição) |
| GET | `/empresas/{id}/certidoes/{certidaoId}/download` | autenticado | Arquivo da certidão |
| GET | `/certidoes/painel?dias=30` | escritório | Certidões atuais vencidas, a vencer e positivas de todas as empresas, com totais de 15/30 dias |

- **Tipos:** `FederalPgfn`, `Estadual`, `Municipal`, `Fgts`, `Trabalhista`. **Situação:** `Negativa`, `PositivaComEfeitoNegativa`, `Positiva`.
- **Atual:** a de validade mais recente de cada tipo; renovar uma certidão vencida a tira do painel. `statusValidade` é `Vigente`, `AVencer` (até 30 dias) ou `Vencida`; `regular` = não positiva e dentro da validade.

## Configuração

Configurações em `appsettings.{Ambiente}.json`, sobrescritas por variáveis de ambiente (`Secao__Chave`).

| Chave | Descrição |
| --- | --- |
| `ConnectionStrings__Default` | Connection string do PostgreSQL |
| `Jwt__SigningKey` | Chave HMAC do JWT (mín. 32 caracteres). **Obrigatória**; nunca versione a de produção |
| `Jwt__AccessTokenMinutos` / `Jwt__RefreshTokenDias` | Validade dos tokens |
| `Cors__AllowedOrigins__0` | Origem do frontend |
| `Database__MigrateOnStartup` / `Database__SeedDemoData` | Migrations e seed na inicialização |
| `FileStorage__LocalPath` | Pasta dos arquivos enviados |
| `Documentos__TamanhoMaximoMb` / `Documentos__ExtensoesPermitidas__N` | Regras de upload |
| `Swagger__Enabled` | Expõe o Swagger (ligado em Development) |

Para produção: `ASPNETCORE_ENVIRONMENT=Production`, chave JWT via secret, migrations aplicadas no pipeline de deploy (ou `Database__MigrateOnStartup=true` em instância única), volume persistente ou storage em nuvem para documentos e HTTPS terminado no proxy/ingress.

### Recuperação de senha

- `POST /api/v1/auth/esqueci-senha` sempre responde **202**, exista ou não a conta (não revela e-mails cadastrados).
- O link `{Notificacoes:UrlFrontend}/redefinir-senha?token=...` leva um token aleatório de 256 bits, armazenado apenas como hash SHA-256, de **uso único** e com validade de `Notificacoes:ValidadeLinkRedefinicaoMinutos` (60 min). Um novo pedido invalida os links anteriores.
- `POST /api/v1/auth/redefinir-senha` troca a senha, libera um eventual bloqueio por tentativas e **revoga todas as sessões** (refresh tokens) do usuário. Token inválido/expirado/usado retorna 422.

### Notificações e e-mails

- **In-app:** `GET /api/v1/notificacoes`, `GET /notificacoes/contador` (sino), `POST /notificacoes/{id}/lida`, `POST /notificacoes/lidas`.
- **Eventos:** novo documento (escritório publica → usuários do cliente; cliente envia → equipe do escritório), envio explícito de documentos ao cliente (`POST /empresas/{id}/documentos/enviar`), vencimento próximo, nova solicitação e atualização de solicitação (status, mensagens e atribuição). Quem executou a ação não é notificado.
- **Lembrete de vencimento:** job a cada `Notificacoes:LembreteVencimento:IntervaloMinutos` avisa os usuários do cliente sobre documentos que vencem em até `DiasLembreteVencimento` dias (configurável por escritório em `PUT /escritorio`, 0 desativa). Cada documento é lembrado uma única vez.
- **Preferências:** `GET/PUT /api/v1/notificacoes/preferencias` liga/desliga o e-mail por tipo; a notificação in-app é sempre gerada.
- **Envio assíncrono (outbox):** e-mails são gravados em `emails_pendentes` na mesma transação da ação e enviados em segundo plano (`FOR UPDATE SKIP LOCKED`, seguro com várias instâncias), com até 5 tentativas e backoff exponencial. Templates HTML + texto com nome/logo do escritório.
- **Provider:** `Email:Provider = Smtp` (MailKit; `Email:Smtp:*`) ou `Log` (apenas registra no log; padrão sem SMTP e nos testes). Em desenvolvimento o docker compose sobe o **Mailpit** (SMTP na porta 1025).

## Próximos passos (backlog)

Convites por e-mail, departamentos e responsáveis, auditoria de documentos, editor rico nas notícias, emissão automática de CND, NF-e e tarefas. Em solicitações: ativar/desativar tipos por empresa, atribuição automática por responsável do cliente, importação de rubricas por planilha e relatórios.
