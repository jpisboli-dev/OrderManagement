# Validação realizada

Data: 24/09/2026. Ambiente: Windows, SDK .NET 10.0.401.

- **Compilação Release:** concluída, com avisos tratados como erros.
- **xUnit:** 13 testes aprovados; 0 falhas; 0 ignorados.
- **Publicação Release:** `dotnet publish` concluído.
- **Migração SQL Server:** migração inicial aplicada na inicialização do container; cadastro e leitura de dados confirmados pela API. Script SQL idempotente também gerado com sucesso.
- **OpenAPI:** documento gerado via HTTP autenticado no teste de integração.
- **SignalR:** envio real por WebSocket validado com os containers em execução. Dono do pedido e administrador receberam o mesmo evento; outro cliente não recebeu. Negociação anônima rejeitada com 401; acesso ao pedido de outro cliente rejeitado com 404.
- **Docker Compose:** imagem da API construída, SQL Server saudável e API iniciada, conforme execução local do script pelo usuário e resultado salvo em `docker-validation.txt`.
- **Hangfire com SQL Server:** relatório de vendas processado pelo worker real; pedido de teste percorreu Criado, Pago, EmPreparo, Enviado e Entregue, com total de 399,80.
- **API em execução:** `GET http://127.0.0.1:8080/health` retornou 200; teste adicional pela API confirmou a entrega de eventos do worker via SignalR.

Os testes cobrem transições obrigatórias, preços e quantidades inválidos, pedidos vazios e duplicados, cadastro/login, JWT adulterado, papéis, isolamento por cliente, paginação inválida, concorrência otimista, outbox, processamento de relatório e não reprocessamento de eventos concluídos.

Os testes xUnit usam TestServer e banco SQLite em memória, com restrições relacionais. Neles, o processador de jobs é executado diretamente e as notificações são capturadas por uma implementação de teste. Além desses testes, o ambiente Docker foi exercitado com SQL Server, worker Hangfire e conexões WebSocket reais.

O usuário executou `scripts/start-and-test.ps1` porque a sessão do assistente não tinha acesso à conexão administrativa do Docker. O assistente conferiu o resultado salvo e executou diretamente o teste HTTP/WebSocket contra a API local. Evidências: `docker-validation.txt` e `signalr-validation.json`. Foram criados clientes e pedidos de teste no banco local; não houve envio real de e-mail. Reinicialização com recuperação de jobs e falhas de infraestrutura não foram exercitadas.

A restauração foi concluída com auditoria padrão do NuGet habilitada. O ambiente exigiu uma ponte local para o acesso TLS ao NuGet; isso foi restrito às ferramentas temporárias e não faz parte do projeto entregue. A configuração entregue utiliza diretamente `https://api.nuget.org/v3/index.json`.
