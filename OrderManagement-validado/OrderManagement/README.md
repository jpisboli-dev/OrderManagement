# Order Management API

API em **.NET 10**, com Clean Architecture, CQRS/MediatR, Entity Framework Core, SQL Server, JWT, SignalR e Hangfire. Inclui migração inicial e testes xUnit com SQLite em memória.

## Executar com Docker

Pré-requisitos: Docker Desktop com containers Linux (SQL Server requer ambiente x64). O banco usa a edição Developer, destinada a desenvolvimento e testes.

1. Copie `.env.example` para `.env`.
2. Preencha `DB_PASSWORD` com uma senha forte compatível com SQL Server, `JWT_KEY` com um segredo aleatório de pelo menos 32 bytes e `ADMIN_PASSWORD` com 12 a 128 caracteres. Defina `ADMIN_EMAIL`.
3. Na pasta da solução, execute:

```sh
docker compose up --build -d
docker compose logs -f api
```

A API fica em `http://localhost:8080`. Verifique `GET /health`. A primeira inicialização aplica as migrações, cria as tabelas do Hangfire e cadastra o administrador. O banco tem volume persistente e não publica porta para o host. Para parar mantendo os dados: `docker compose down`.

O bootstrap só cria o administrador se não existir nenhum. Alterar `ADMIN_PASSWORD` no `.env` depois disso não altera a senha armazenada.

## Experimentar o fluxo completo

No PowerShell 7, com os containers em execução:

```powershell
./scripts/demo.ps1 -AdminEmail 'admin@example.com'
```

O script solicita a senha do administrador, cria um cliente e um produto, gera um pedido, avança pelos cinco status e aguarda um relatório em background. A confirmação de e-mail é **simulada nos logs** da API.

## Endpoints

Todas as rotas `/api`, exceto cadastro e login, exigem `Authorization: Bearer <token>`.

| Método | Rota | Permissão / finalidade |
| --- | --- | --- |
| POST | `/api/auth/register` | Público; cadastra cliente com nome, e-mail e senha |
| POST | `/api/auth/login` | Público; retorna JWT válido por uma hora |
| GET | `/api/products?page=1` | Autenticado; lista produtos |
| POST | `/api/products` | Admin; cadastra produto |
| GET | `/api/customers?page=1` | Admin; lista clientes, sem hashes de senha |
| POST | `/api/orders` | Autenticado; pedido do próprio usuário |
| GET | `/api/orders?page=1` | Cliente vê seus pedidos; Admin vê todos |
| GET | `/api/orders/{id}` | Dono ou Admin |
| PATCH | `/api/orders/{id}/status` | Admin; avança exatamente um status |
| POST | `/api/reports/sales` | Admin; retorna 202 e ID do relatório |
| GET | `/api/reports/sales/{id}` | Admin; consulta resultado e `completedAt` |
| GET | `/openapi/v1.json` | Admin; especificação OpenAPI |
| GET | `/health` | Público; verificação de processo ativo |
| SignalR | `/hubs/orders` | Autenticado; eventos do próprio cliente ou todos para Admin |

Listagens retornam até 50 registros por página. Erros de negócio usam Problem Details: 400 para entradas/transições inválidas, 401 sem autenticação, 403 sem papel permitido, 404 para recurso inexistente ou pedido de outro cliente, 409 para conflito.

Exemplos de corpos JSON:

```json
{"name":"Ana","email":"ana@example.com","password":"uma-senha-longa-e-unica"}
```

```json
{"name":"Teclado","price":199.90}
```

```json
{"items":[{"productId":"GUID-DO-PRODUTO","quantity":2}]}
```

```json
{"status":"Pago"}
```

Sequência: `Criado → Pago → EmPreparo → Enviado → Entregue`. O cliente não informa preços, total, dono do pedido ou papel de usuário. O pedido copia nome e preço do produto no momento da criação. A marcação de pagamento é uma operação administrativa; não há integração com gateway de pagamento.

## SignalR

Exemplo para um cliente JavaScript que tenha `@microsoft/signalr` instalado, hospedado na mesma origem da API:

```javascript
import * as signalR from '@microsoft/signalr';
const connection = new signalR.HubConnectionBuilder()
  .withUrl('/hubs/orders', { accessTokenFactory: () => accessToken })
  .withAutomaticReconnect()
  .build();
const received = new Set();
connection.on('OrderStatusChanged', event => {
  if (received.has(event.eventId)) return;
  received.add(event.eventId);
  console.log(event.orderId, event.status, event.version);
});
connection.onreconnected(() => refreshOrders());
await connection.start();
```

`accessToken` é o JWT obtido no login e `refreshOrders()` deve buscar a lista atual pela API. O servidor define os destinatários pela identidade autenticada, sem permitir inscrição arbitrária em pedidos. Eventos podem ser repetidos por tentativas de entrega; use `eventId` para deduplicar. SignalR não armazena eventos para clientes desconectados: recarregue o estado após reconectar. Tokens expirados encerram a conexão; faça novo login. Não há CORS liberado por padrão.

## Organização

```text
src/
  Domain/           Entidades, dinheiro, quantidades e transições de status
  Application/      Commands, Queries, handlers MediatR, DTOs e interfaces
  Infrastructure/   EF Core, repositories, Unit of Work, migrações e jobs
  API/              Endpoints, autenticação JWT, SignalR e composição
tests/Tests/        Testes unitários e integração HTTP/SQLite em memória
```

Domain não referencia outras camadas. Application depende de Domain. Infrastructure implementa as interfaces de Application. API resolve as dependências.

Cada command grava com uma única chamada à Unit of Work. Pedido e mensagem de outbox são persistidos na mesma transação EF. Um agendador verifica a outbox a cada dois segundos e envia o processamento ao Hangfire. Hangfire guarda os jobs no SQL Server, aplica exclusão distribuída e tenta novamente em caso de falha. O envio SignalR acontece no processamento; normalmente haverá alguns segundos de atraso.

O relatório inclui os pedidos criados até a solicitação que estejam em `Pago` ou em etapa posterior no momento do processamento. `revenue` soma seus totais e `paidOrders` conta esses pedidos. Não é um fechamento contábil histórico. O resultado fica persistido no banco, sem depender do sistema de arquivos do container.

## Compilar e testar

Requer SDK .NET 10; os testes não precisam de Docker nem de SQL Server:

```sh
dotnet restore
dotnet build --no-restore
dotnet test --no-build
```

Os testes usam a API real em TestServer, JWT real e SQLite relacional em memória. Nos testes dos handlers de background, o envio de notificação é capturado e os jobs são executados diretamente, sem servidor Hangfire. Isso não substitui a validação de transporte e armazenamento no ambiente Docker.

Para executar fora do Docker, disponibilize um SQL Server e configure `ConnectionStrings__Database`, `Jwt__Key`, `Bootstrap__AdminEmail` e `Bootstrap__AdminPassword` como variáveis de ambiente. Depois:

```sh
dotnet run --project src/API --urls http://localhost:8080
```

Para novas migrações, instale `dotnet-ef` 10.0.9 e execute:

```sh
dotnet ef migrations add NomeDaMigracao --project src/Infrastructure --startup-project src/Infrastructure
```

## Limites e decisões

- O escopo é uma API: não inclui interface web, estoque, cancelamentos, recuperação de senha ou refresh tokens.
- O e-mail é uma simulação. Ao conectar um provedor real, use a chave do evento para evitar duplicação.
- O Compose é uma configuração local com uma instância. Em produção, configure HTTPS, usuário SQL com privilégios mínimos, gestão de segredos, backups e migrações em uma etapa de implantação. Para múltiplas APIs, SignalR precisa de backplane; o agendador de outbox também deve ser centralizado para reduzir jobs duplicados.
- A outbox entrega ao menos uma vez. Falha permanente no primeiro evento impede o avanço do lote; monitore os logs e jobs com falha. Não foi exposto um dashboard Hangfire público.
- Cadastro e login têm limite de 20 chamadas por minuto por IP, em memória por instância. Senhas são armazenadas com PasswordHasher/PBKDF2. JWT não tem revogação imediata.
- Containers validados localmente com SQL Server, fluxo de pedidos, relatório Hangfire e eventos SignalR por WebSocket. Consulte `VALIDATION.md` para os resultados e limites dos testes.

## Referências

- [Autenticação e autorização no SignalR](https://learn.microsoft.com/aspnet/core/signalr/authn-and-authz)
- [MediatR 12.5.0](https://www.nuget.org/packages/MediatR/12.5.0)
- [Hangfire.AspNetCore](https://www.nuget.org/packages/Hangfire.AspNetCore/1.8.25)
