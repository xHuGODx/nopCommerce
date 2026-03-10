# Notas

## 1. Ler Antes de Tocar — Análise de Arquitetura

### Organização das camadas e regras de dependência

- O nopecommerce é um monolinth. As dependências estão na maior parte organizadas de forma descendente, ou seja:
  `Nop.Core -> Nop.Data -> Nop.Services -> Nop.Web.Framework -> Nop.Web`.
- `Nop.Core` contém interfaces base e contratos de domínio/eventos.
- `Nop.Data` adiciona os repositórios e as migrações da DB este depende só de `Nop.Core`.
- `Nop.Services` contem a business logic e a implementação de `IEventPublisher`, este depende de `Nop.Data`.
- `Nop.Web.Framework` é a camada de UI. Aqui também são registados os contextos e os serviços (em NopStartup.cs)

### Como o nopCommerce trata eventos internamente

- `IEventPublisher` é uma interface de apenas um método no Core `PublishAsync<TEvent>(TEvent)`.
- A implementação está em `Nop.Services`. Basicamente o EventPublisher procura todos os consumidores de um tipo de evento no contexto e depois faz um for loop em que envia o evento e aguarda que o consumer responda
- Não existe um ordem especifica para consumidores. Esta depende do resultado do scan do contexto.

### Onde é fácil acrescentar observabilidade

- `INopStartup`.
- `IEventPublisher`.
- Classe abstrata dos repositórios.

### Onde é difícil acrescentar observabilidade

...

### O que seria preciso mudar estruturalmente e se vale a pena

...

## 2. Escolher um Fluxo de Utilizador e Instrumentá-lo de Ponta a Ponta

### Fluxo escolhido

Escolho o cliente faz uma encomenda.

Parece-me ser o fluxo mais importante de qualquer loja.

### Plano de tracing distribuído

- Deixar o ASP.NET Core criar o span de servidor para `POST /checkout/confirm`.
- Criar o span principal de negócio em `OrderProcessingService.PlaceOrderAsync`, porque é aí que os diferentes variantes de checkout convergem.
- Acrescentar spans filhos para as fases principais:
  `checkout.prepare_details`,
  `checkout.payment.process`,
  `checkout.order.save`,
  `checkout.order_items.move`,
  `checkout.inventory.adjust`,
  `checkout.event.publish`,
  `checkout.payment.postprocess`.
- Para a base de dados, instrumentar o limite partilhado do `EntityRepository<TEntity>` para apanhar escritas de encomendas, moradas, items,
  produtos e histórico de stock sem tocar em todos os serviços.
- Se a instrumentação ADO.NET/OpenTelemetry funcionar com o provider usado, pode ser adicionada também; se não, o repositório continua a ser
  o fallback mais fiável.
- Convém evitar spans por item quando o carrinho tem muitos produtos; é preferível um span agregado para ajuste de inventário com
  `cart.items.count`.

### Métricas customizadas que fazem sentido

- `checkout_stage_duration_seconds`
  como histograma, etiquetado por `stage`, `payment_method` e `result`.
- Justificação:
  esta métrica permite perceber onde o checkout está a degradar antes de haver erros visíveis ao utilizador.
  Um `p95` mais alto em pagamento aponta para problemas do provider; um `p95` mais alto em `order.save` aponta para contenção na base de dados;
  um `p95` mais alto em `inventory.adjust` aponta para gargalos nas escritas de stock.
- `checkout_business_failures_total`
  como contador, etiquetado por `stage`, `failure_category` e `payment_method`.
- Justificação:
  este fluxo pode falhar sem devolver HTTP `5xx`, por isso é preciso medir falhas de negócio e não apenas falhas de transporte.
- Métrica opcional:
  `inventory_low_stock_crossings_total`.
- Justificação:
  ajuda a perceber quando o volume de checkout ou a lógica de stock está a empurrar produtos para estados de baixo stock antes de surgirem
  incidentes de oversell.

### Exclusão de dados sensíveis

- Incluir:
  `store.id`, `cart.items.count`, `checkout.variant`, `payment.method.system_name`, `result`, `failure.stage`, `is_recurring` e uma versão agregada de `order.total.range`.
- Não devem ser incluídos em traces, logs ou métricas: emails, nomes, números de telefone, moradas, campos de cartão, CVV, endereço IP ou respostas cruas do provider de pagamento.


### Abordagem inicial recomendada

...
