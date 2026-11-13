# Zerar o banco (somente desenvolvimento)

> ⚠️ **Destrutivo e sem volta.** Apaga todos os dados do Postgres do `docker compose` local, mantendo apenas o usuário **Admin C2L Tech**, o escritório dele e o histórico de migrations do EF (`__ef_migrations_history`).

```bash
docker compose up -d db
scripts/db/zerar-banco.sh                 # pede para digitar ZERAR
scripts/db/zerar-banco.sh --com-arquivos  # também apaga os arquivos do storage
```

Para manter outro usuário: `ADMIN_NOME="Nome do Usuário" scripts/db/zerar-banco.sh`.

No Windows (PowerShell):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\db\zerar-banco.ps1               # pede para digitar ZERAR
powershell -ExecutionPolicy Bypass -File scripts\db\zerar-banco.ps1 -ComArquivos  # também apaga o storage
```

(`-ExecutionPolicy Bypass` vale só para essa execução; o Windows bloqueia `.ps1` por padrão.)

## Proteções

- O `.sh`/`.ps1` só rodam com `ASPNETCORE_ENVIRONMENT=Development` no `.env` e com o container `db` do compose no ar (achado pelos labels do projeto `contix`, sem depender do `.env` estar completo), e pedem para digitar `ZERAR`.
- O `.sql` exige `-v confirmar=ZERAR`, recusa conexões que não sejam locais (socket ou 127.0.0.1, como no `docker compose exec db psql`) e exige exatamente um usuário ativo com o nome informado.
- Tudo roda numa única transação: qualquer erro desfaz a operação inteira. O `TRUNCATE` é feito sem `CASCADE`, então uma FK nova apontando para `usuarios`/`escritorios` faz o script falhar em vez de apagar o que não deve.

O que mais acontece: as sessões são encerradas (refresh tokens apagados), a numeração de solicitações e tarefas volta a 1 e o bloqueio de login do admin é liberado. O seed de demonstração (`DB_SEED_DEMO`) **não** roda de novo, porque o escritório do admin continua existindo.

## Arquivos órfãos no storage

Documentos, anexos, certidões, XMLs de NF-e, imagens de notícias e logos das empresas ficam no storage, não no banco, e sobram depois de zerar. Os certificados A1 ficam no banco e já são apagados. Para limpar:

Rode com `--com-arquivos` (`.sh`) ou `-ComArquivos` (`.ps1`). Isso limpa:

- as pastas `storage/` e `src/Contix.Api/storage/`, usadas quando a API roda com `dotnet run`;
- o volume `contix_storage`, usado quando a API roda no docker compose (se existir).

O logo do escritório é uma URL externa e não fica no storage.

## Inicializar / reparar o histórico de migrations

Se a API não sobe porque o EF tenta criar tabelas que já existem (por exemplo, `relation "escritorios" already exists`), o histórico de migrations foi apagado. A primeira versão do `zerar-banco.sql` fazia isso. Rode:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\db\inicializar-banco.ps1   # Windows
```
```bash
scripts/db/inicializar-banco.sh                                              # Linux/macOS/Git Bash
```

O script não apaga nem altera dados. Ele olha quais tabelas e colunas existem, marca como aplicadas as migrations correspondentes e deixa as que faltam para a API aplicar ao subir. Com o banco vazio, ele não faz nada e a API cria tudo sozinha. Ao criar uma migration nova, acrescente-a na lista do `inicializar-banco.sql`.
