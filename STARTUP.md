# Como iniciar o Forge (modo dev atual — não containerizado)

Este arquivo descreve o modo **real** em que o Forge está rodando hoje: Postgres/Temporal/Temporal UI em containers Docker, e Forge.Api/Forge.Worker/frontend como processos soltos no host (`dotnet run`/`vite`), gerenciados por `scripts/restart-forge-dev.sh`.

Existe também um `docker-compose.yml` na raiz que containeriza TUDO (incluindo Api/Worker/frontend) — é um modo alternativo documentado em `docs/015-Deployment.md` §6, mas **não é o que está rodando hoje** e não foi testado ao vivo nesta sessão. Não misture os dois modos sem primeiro derrubar um deles (portas 5080/5173/5432/7233/8233 colidem).

## 1. Containers Docker (Postgres, Temporal, Temporal UI)

Esses containers foram criados a partir de um compose file antigo (`docker/local/docker-compose.yml`) que já não existe mais no repo — mas os containers já criados continuam funcionando independente disso. Docker não precisa do arquivo de origem para continuar rodando um container já criado.

**Verificar se já estão rodando:**
```bash
docker ps --filter "name=forge-postgres" --filter "name=forge-temporal"
```

**Se estiverem parados (mas ainda existem), só reiniciar:**
```bash
docker start forge-postgres forge-temporal forge-temporal-ui
```

**Se não existirem mais (removidos)**: não há mais um compose file dedicado só a esses 3 serviços. A opção mais simples é usar o `docker-compose.yml` da raiz, mas subir só esses 3 serviços dele:
```bash
docker compose up -d postgres temporal temporal-ui
```
(isso NÃO builda/inicia forge-api/forge-worker/frontend, já que você está listando os serviços explicitamente)

Portas esperadas: Postgres `5432`, Temporal `7233`, Temporal UI `8233` (`http://localhost:8233`).

## 2. Forge.Api + Forge.Worker + frontend (processos bare)

Esses são os que mais provavelmente caem quando a sessão do Claude Code é reiniciada (foram iniciados como processos filhos da sessão).

**Verificar se estão rodando:**
```bash
ps aux | grep -E "Forge.Worker/bin|dotnet run --urls|vite --port" | grep -v grep
```

**Reiniciar Api + frontend juntos** (script já existente, usado pelo próprio Deploy agent):
```bash
bash /home/felippe/repos/Forge/scripts/restart-forge-dev.sh
```

**O Worker precisa ser iniciado separadamente** (o script acima deliberadamente nunca toca nele — ver comentário no próprio script e `docs/015-Deployment.md` §2):
```bash
cd /home/felippe/repos/Forge/backend/src/Forge.Worker && dotnet run
```

**Confirmar que subiu:**
```bash
curl -s http://localhost:5080/health   # deve retornar {"status":"healthy"}
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5173   # deve retornar 200
```

## 3. Login

Usuário: `felippe.domingos@actiz.com.br`
Senha atual: `Foco2023!` (resetada em 2026-08-07 via `POST /users/{id}/reset-password`)

## 4. Ordem recomendada de subida completa (do zero)

```bash
docker start forge-postgres forge-temporal forge-temporal-ui   # ou docker compose up -d postgres temporal temporal-ui
sleep 5
cd /home/felippe/repos/Forge/backend/src/Forge.Worker && dotnet run &
bash /home/felippe/repos/Forge/scripts/restart-forge-dev.sh
```

Depois disso: `http://localhost:5173` (frontend), `http://localhost:8233` (Temporal UI).
