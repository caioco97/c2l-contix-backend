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

Os testes de integração usam [Testcontainers](https://dotnet.testcontainers.org/) e precisam do Docker em execução.

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

## Próximos passos (backlog)

Recuperação de senha e convites por e-mail, departamentos e responsáveis, auditoria de documentos, envio de documentos por e-mail, notícias, CND, solicitações, NF-e, tarefas e notificações.
