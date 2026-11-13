# Zerar o banco (somente desenvolvimento)

> ⚠️ **Destrutivo e sem volta.** Apaga todos os dados do Postgres do `docker compose` local, mantendo apenas o usuário **Admin C2L Tech**, o escritório dele e a tabela `__EFMigrationsHistory`.

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

- O `.sh`/`.ps1` só rodam com `ASPNETCORE_ENVIRONMENT=Development` no `.env` e com o serviço `db` do compose no ar, e pede para digitar `ZERAR`.
- O `.sql` exige `-v confirmar=ZERAR`, recusa conexões que não sejam locais (socket ou 127.0.0.1, como no `docker compose exec db psql`) e exige exatamente um usuário ativo com o nome informado.
- Tudo roda numa única transação: qualquer erro desfaz a operação inteira. O `TRUNCATE` é feito sem `CASCADE`, então uma FK nova apontando para `usuarios`/`escritorios` faz o script falhar em vez de apagar o que não deve.

O que mais acontece: as sessões são encerradas (refresh tokens apagados), a numeração de solicitações e tarefas volta a 1 e o bloqueio de login do admin é liberado. O seed de demonstração (`DB_SEED_DEMO`) **não** roda de novo, porque o escritório do admin continua existindo.

## Arquivos órfãos no storage

Documentos, anexos, certidões, XMLs de NF-e, imagens de notícias e logos das empresas ficam no storage, não no banco, e sobram depois de zerar. Os certificados A1 ficam no banco e já são apagados. Para limpar:

- **API no docker compose** (volume `storage`): `scripts/db/zerar-banco.sh --com-arquivos`, ou
  `docker compose run --rm --no-deps -T --entrypoint sh api -c 'find /app/storage -mindepth 1 -delete'`
- **API rodando com `dotnet run`**: apague a pasta `storage/` dentro do diretório de onde a API foi iniciada (ex.: `src/Contix.Api/storage`).

O logo do escritório é uma URL externa e não fica no storage.
