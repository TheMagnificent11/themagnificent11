## 11-Dec-23 Domain event dispatching using the outbox pattern with Entity Framework

### What is domain event dispatching?

Domain event dispatching is a concept that related to [domain-driven design](https://martinfowler.com/bliki/DomainDrivenDesign.html), or DDD as it's also known.

Having said that, event dispatching is central to any [event-driven architecture](https://learn.microsoft.com/en-us/azure/architecture/guide/architecture-styles/event-driven), which follows the [publisher-subscriber pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/publisher-subscriber).

Now, I've not actually read Eric Evans' seminal book on domain-driven design, [Domain-driven Design: Tacking Complexity in the Heart of Software](https://www.amazon.com.au/Domain-Driven-Design-Tackling-Complexity-Software/dp/0321125215), so I'm unsure whether Evans suggests whether domain events should be published as part of the transaction that creates them, or have the event persisted with the change in application state and then later published using an [outbox pattern](https://codeopinion.com/outbox-pattern-reliably-save-state-publish-events).

I prefer the outbox pattern for domain event dispatching because I don't think you want a scenario where data is persisted and an event is raised that has multiple subscribers, but one of the subscribers fails to execute, causing the whole transation to be rolled back.

You then get an inconsistent scenario where certain event handlers have been handled, but the data that relates to those actions does not exist.  Why send a confirmation email for an account that wasn't successfully regsitered.

Or, conversely, the transaction is not rolled back and one subscriber has failed to execute (including retries with back-off).

The outbox pattern, keeps a record of whether the event has been dispatched or "sent", behaving a bit like a service bus.

In this blog post, I'm going to explore how an application using [Entity Framework](https://learn.microsoft.com/en-us/aspnet/entity-framework) as an ORM can use an outbox pattern to publish domain events that are persisted with the application data.

The packages used will be Entity Framework and I will leverage `INotification` in [MediatR](https://github.com/jbogard/MediatR) to assist with the publisher-subscriber implementation.

All of these code samples are taken from my [Lewee](https://github.com/TheMagnificent11/lewee) project.

### Domain Event

Here's rough representation of a domain event.

I've included a `CorrelationId` property that I believe should be set to allow you to correlate the logs from the original transaction that persisted the [aggregate root](https://martinfowler.com/bliki/DDD_Aggregate.html) and all the subsequent events that get handled as part of the dispatching.

```cs
using MediatR;

public interface IDomainEvent : INotification
{
    Guid CorrelationId { get; }
}
```

And here's a sample implementation of an menu item being added to the table's order at a restaurant.

```cs
public class MenuItemAddedToOrderDomainEvent : IDomainEvent
{
    public MenuItemAddedToOrderDomainEvent(
        Guid correlationId,
        Guid tableId,
        int tableNumber,
        Guid orderId,
        Guid menuItemId,
        decimal price)
    {
        this.CorrelationId = correlationId;
        this.TableId = tableId;
        this.TableNumber = tableNumber;
        this.OrderId = orderId;
        this.MenuItemId = menuItemId;
        this.Price = price;
    }

    public Guid CorrelationId { get; }
    public Guid TableId { get; }
    public int TableNumber { get; }
    public Guid OrderId { get; }
    public Guid MenuItemId { get; }
    public decimal Price { get; }
}
```

### Storing Domain Events

Below is entity class used to store the details about a domain event after it related aggregate root has been persisted.

Things to note, we are storing the domain event as JSON (`DomainEventJson`) and also storing the assembly name (`DomainEventAssemblyName`) and class name (`DomainEventClassName`) to allow us to deserialize the JSON back to the doamin event in the `ToDomainEvent` method.

```cs
using System.Reflection;
using System.Text.Json;

public class DomainEventReference
{
    public DomainEventReference(DomainEvent domainEvent)
    {
        this.Id = Guid.NewGuid();
        this.DomainEventAssemblyName = assemblyName;
        this.DomainEventClassName = fullClassName;
        this.DomainEventJson = JsonSerializer.Serialize(domainEvent, domainEventType);
        this.PersistedAt = DateTime.UtcNow;
        this.Dispatched = false;
    }

    // EF constructor
    private DomainEventReference()
    {
        this.DomainEventAssemblyName = string.Empty;
        this.DomainEventClassName = string.Empty;
        this.DomainEventJson = "{}";
        this.PersistedAt = DateTime.UtcNow;
        this.Dispatched = false;
    }

    public Guid Id { get; protected set; }
    public string DomainEventAssemblyName { get; protected set; }
    public string DomainEventClassName { get; protected set; }
    public string DomainEventJson { get; protected set; }
    public bool Dispatched { get; protected set; }
    public DateTime PersistedAt { get; protected set; }
    public DateTime? DispatchedAt { get; protected set; }

    public DomainEvent? ToDomainEvent()
    {
        if (string.IsNullOrWhiteSpace(this.DomainEventJson))
        {
            return null;
        }

        var assembly = Assembly.Load(this.DomainEventAssemblyName);
        var targetType = assembly.GetType(this.DomainEventClassName);

        if (targetType == null)
        {
            return null;
        }

        var objDomainEvent = Deserialize(this.DomainEventJson, targetType);
        if (objDomainEvent is not DomainEvent domainEvent)
        {
            return null;
        }

        domainEvent.UserId = this.UserId;

        return domainEvent;

        static object? Deserialize(string json, Type type)
        {
            try
            {
                return JsonSerializer.Deserialize(json, type);
            }
            catch
            {
                return null;
            }
        }
    }

    public void Dispatch()
    {
        this.Dispatched = true;
        this.DispatchedAt = DateTime.UtcNow;
    }
}
```

And here's how we've configured the underlying database table.

```cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class DomainEventReferenceConfiguration : IEntityTypeConfiguration<DomainEventReference>
{
    public void Configure(EntityTypeBuilder<DomainEventReference> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DomainEventAssemblyName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.DomainEventClassName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.DomainEventJson)
            .HasMaxLength(8000)
            .IsRequired();

        builder.Property(x => x.Dispatched)
            .IsRequired();

        builder.Property(x => x.PersistedAt)
            .IsRequired();

        builder.Property(x => x.DispatchedAt)
            .IsRequired(false);

        builder.Property(x => x.UserId)
            .HasMaxLength(50);

        builder.HasIndex(
            nameof(DomainEventReference.Dispatched),
            nameof(DomainEventReference.PersistedAt));
    }
}
```

The aggregate root needs a collection of domain events on it.

```cs
public class DomainEventsCollection
{
    private readonly List<DomainEvent> domainEvents = new();
    private readonly object syncLock = new();

    public void Raise<T>(T domainEvent)
        where T : DomainEvent
    {
        lock (this.syncLock)
        {
            this.domainEvents.Add(domainEvent);
        }
    }

    public DomainEvent[] GetAndClear()
    {
        lock (this.syncLock)
        {
            var events = this.domainEvents.ToArray();
            this.domainEvents.Clear();

            return events;
        }
    }
}
```

```cs
public abstract class AggregateRoot : Entity
{
    protected AggregateRoot()
        : base(Guid.NewGuid())
    {
    }

    protected AggregateRoot(Guid id)
        : base(id)
    {
    }

    public DomainEventsCollection DomainEvents { get; } = new DomainEventsCollection();
}
```

And here's a sample implementation of an aggregate root "raising" a domain event.

In this example, a menu item is being added to the order of a table at a restaurant.

```cs
public class Table : AggregateRoot
{
    private readonly List<Order> orders = new();

    public Table(Guid id, int tableNumber)
        : base(id)
    {
        this.TableNumber = tableNumber;
    }

    public Order? CurrentOrder => this.orders
        .Where(x => x.OrderStatusId != OrderStatus.Paid)
        .Where(x => !x.IsDeleted)
        .OrderByDescending(x => x.CreatedAtUtc)
        .FirstOrDefault();

    public int TableNumber { get; protected set; }
    public bool IsInUse { get; protected set; }
    public IReadOnlyCollection<Order> Orders => this.orders;

    public void OrderMenuItem(MenuItem menuItem, Guid correlationId)
    {
        if (!this.IsInUse || this.CurrentOrder == null)
        {
            throw new DomainException(ErrorMessages.CannotOrderIfTableNotInUse);
        }

        this.CurrentOrder.AddItem(menuItem);

        this.DomainEvents.Raise(new MenuItemAddedToOrderDomainEvent(
            correlationId,
            this.Id,
            this.TableNumber,
            this.CurrentOrder.Id,
            menuItem.Id,
            menuItem.Price));
    }

    public static class ErrorMessages
    {
        public const string CannotOrderIfTableNotInUse = "Cannot order items if table is not in use";
    }
}
```

We are going to use a [SaveChangesInterceptor](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.diagnostics.savechangesinterceptor) to find the domain events added to our aggregate roots and add them as `DomainEventReference` instances before "save changes" executes.

To do that, we need to be able easily identify the `DbSet` for our `DomainEventReference` entities.

So, we've created an `IApplicationDbContext` interface that we can add to our Entity Framework `DbContext`.

```cs
public interface IApplicationDbContext
{
    DbSet<DomainEventReference>? DomainEventReferences { get; }
}
```

And here's the interceptor that find the domain events on the aggregate roots and stores them in the `DbContext`.

```cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

public class DomainEventSaveChangesInterceptor<TContext> : SaveChangesInterceptor
    where TContext : DbContext, IApplicationDbContext
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        this.StoreDomainEvents(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        this.StoreDomainEvents(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void StoreDomainEvents(DbContext? context)
    {
        if (context == null || context is not TContext)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            this.StoreDomainEventsForEntry((TContext)context, entry);
        }
    }

    private void StoreDomainEventsForEntry(TContext context, EntityEntry entry)
    {
        if (entry.Entity is not AggregateRoot aggregateRootEntity)
        {
            return;
        }

        var events = aggregateRootEntity.DomainEvents.GetAndClear();

        foreach (var domainEvent in events)
        {
            if (domainEvent == null)
            {
                continue;
            }

            var reference = new DomainEventReference(domainEvent);

            context.DomainEventReferences?.Add(reference);
        }
    }
}
```

Finally, here's an abstract `DbContext` implementation that exposes the `DomainEventReference` `DbSet` and adds the `DomainEventSaveChangesInterceptor`.

```cs
using Microsoft.EntityFrameworkCore;

public abstract class ApplicationDbContext<TContext> : DbContext, IApplicationDbContext
    where TContext : DbContext, IApplicationDbContext
{
    protected ApplicationDbContext(DbContextOptions<TContext> options)
        : base(options)
    {
    }

    public DbSet<DomainEventReference>? DomainEventReferences { get; internal set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new DomainEventReferenceConfiguration());

        this.ConfigureDatabaseModel(modelBuilder);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        optionsBuilder.AddInterceptors(new DomainEventSaveChangesInterceptor<TContext>());
    }

    protected abstract void ConfigureDatabaseModel(ModelBuilder modelBuilder);
}
```

### Dispatching Domain Events

The code below reads from the database table for the `DomainEventReference` entity and dispatches them in batches of 50.

For event row, it deserializes the JSON to the original `IDomainEvent` and because `IDomainEvent` implements `INotification` from `MediatR`, you can use the `Publish` to achieve the publisher-subscriber pattern; there can be multiple notification handlers for each event.  Conversely, if there are not notification handlers, `MediatR` will handle this for us and return successfully.

The domain event does not get marked as "dispatched" in the database unless all dispatching succeeds.

There is a downside here because you could have three notification handlers for an event, two could succeed and one could fail, causing the event does not get marked as dispatched.

Any process that tries to re-dispatch will attempt all three handlers again, so you could get duplication of certain handlers.

However, I think this is still better than the other scenario I outlined earlier.

```cs
using MediatR;
using Microsoft.EntityFrameworkCore;
using Serilog;

public class DomainEventDispatcher<TContext>
    where TContext : DbContext, IApplicationDbContext
{
    private const int BatchSize = 50;

    private readonly IDbContextFactory<TContext> dbContextFactory;
    private readonly IMediator mediator;
    private readonly ILogger logger;

    public DomainEventDispatcher(
        IDbContextFactory<TContext> dbContextFactory,
        IMediator mediator,
        ILogger logger)
    {
        this.dbContextFactory = dbContextFactory;
        this.mediator = mediator;
        this.logger = logger.ForContext<DomainEventDispatcher<TContext>>();
    }

    public async Task DispatchEvents(CancellationToken cancellationToken)
    {
        var eventsToDispatch = await this.ThereAreEventsToDispatch(cancellationToken);

        while (eventsToDispatch && !cancellationToken.IsCancellationRequested)
        {
            await this.DispatchBatch(cancellationToken);

            eventsToDispatch = await this.ThereAreEventsToDispatch(cancellationToken);
        }
    }

    private async Task<bool> ThereAreEventsToDispatch(CancellationToken token)
    {
        using (var scope = this.dbContextFactory.CreateDbContext())
        {
            var dbSet = scope.Set<DomainEventReference>();

            if (dbSet == null)
            {
                return false;
            }

            return await dbSet
                .Where(x => !x.Dispatched)
                .OrderBy(x => x.PersistedAt)
                .AnyAsync(token);
        }
    }

    private async Task DispatchBatch(CancellationToken token)
    {
        using (var scope = this.dbContextFactory.CreateDbContext())
        {
            var dbSet = scope.Set<DomainEventReference>();
            if (dbSet == null)
            {
                return;
            }

            var events = await dbSet
                .Where(x => !x.Dispatched)
                .OrderBy(x => x.PersistedAt)
                .Take(BatchSize)
                .ToArrayAsync(token);

            var domainEvents = new List<DomainEvent>();

            foreach (var domainEventReference in events)
            {
                domainEventReference.Dispatch();

                var domainEvent = domainEventReference.ToDomainEvent();

                if (domainEvent == null)
                {
                    this.logger.Warning(
                        "Could not deserialize DomainEventReference {Id}",
                        domainEventReference.Id);
                }
                else
                {
                    domainEvents.Add(domainEvent);
                }
            }

            if (domainEvents.Any())
            {
                foreach (var domainEvent in domainEvents)
                {
                    await this.mediator.Publish(domainEvent, token);
                }
            }

            await scope.SaveChangesAsync(token);
        }
    }
}
```

So the code above does the dispatching, but what triggers the dispatching?

As far as I know, there is nothing similar to the `SaveChangesInterceptor` that executes after a successful save changes.

So, we've decided to use a background service.

In the code below, we are execting our `DomainEventDispatcher` event 2.5 seconds.

So, any events that failed to dispatch will be retried after 2.5 seconds.

`DomainEventReference` does not currently have a "retry count" or a "failed" property and that is definitely an improvement that could be added; fail if retried 10 times.

Under this solution, we will keep retrying and failed events will be attempted before new events, potentially causing a performance issue if failed events build up.

```cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Lewee.Infrastructure.Data;

public class DomainEventDispatcherService<TContext> : BackgroundService
    where TContext : DbContext, IApplicationDbContext
{
    public DomainEventDispatcherService(DomainEventDispatcher<TContext> domainEventDispatcher)
    {
        this.DomainEventDispatcher = domainEventDispatcher;
    }

    public DomainEventDispatcher<TContext> DomainEventDispatcher { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await this.DomainEventDispatcher.DispatchEvents(stoppingToken);

            await Task.Delay(2500, stoppingToken);
        }
    }
}
```

There are definitely better ways to achieve this.

You could override save changes on your `DbContext` to trigger your domain event dispatcher and later you use some sort of retry policy if any events fails to dispatch.

That would be more efficient, but more complicated and harder to implement.

### Dependency Injection Configuration

```cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public static class ApplicationDbContextServiceCollectionExtensions
{
    public static IServiceCollection ConfigureDatabase<T>(
        this IServiceCollection services,
        string connectionString)
        where T : ApplicationDbContext<T>
    {
        services.AddDbContextFactory<T>(options => options.UseSqlServer(connectionString));
        services.AddScoped<T>();

        services.AddSingleton<DomainEventDispatcher<T>>();
        services.AddHostedService<DomainEventDispatcherService<T>>();

        return services;
    }
}
```

#### This is a lot of boilerplate

This seems like a lot of code to achieve what I'd expect to be relatively straight-forward.

Given this is fairly common pattern and that DDD is used by a lot in software development, you'd expect that there are frameworks that do this for you.

That's what I've tried to achieve with [Lewee](https://github.com/TheMagnificent11/lewee).

However, there's definitely a better way.

In a future blog post, I'd like to explore how to achieve something similar using [Wolverine](https://wolverine.netlify.app) and [Marten](https://martendb.io), instead of `MediatR` and Entity Framework.

<!-- [11 December 2023]({{ site.baseurl }}{% post_url 2023-12-11-outbox-domain-event-dispatching-with-ef %}): Domain event dispatching using the outbox pattern with Entity Framework -->
