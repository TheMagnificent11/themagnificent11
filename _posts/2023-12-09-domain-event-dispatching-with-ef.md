# Domain event dispatching using the outbox pattern with Entity Framework

Domain event dispatching is a concept that related to [domain-driven design](https://martinfowler.com/bliki/DomainDrivenDesign.html).

Having said that, event dispatching is central to any [event-driven architecture](https://learn.microsoft.com/en-us/azure/architecture/guide/architecture-styles/event-driven), which follows the [publisher-subscriber pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/publisher-subscriber).

Now, I've not actually read Eric Evans' seminal book on domain-driven design, [Domain-driven Design: Tacking Complexity in the Heart of Software](https://www.amazon.com.au/Domain-Driven-Design-Tackling-Complexity-Software/dp/0321125215), so I'm unsure whether Evans suggests whether domain events should be published as part of the transaction that creates them, or have the event persisted with the change in application state and then later published using an [outbox pattern](https://codeopinion.com/outbox-pattern-reliably-save-state-publish-events).

In this blog post, I'm going to explore how an application using [Entity Framework](https://learn.microsoft.com/en-us/aspnet/entity-framework) as an ORM can use an outbox pattern to publish domain events that are persisted with the application data.

The packages used will be Entity Framewok and I will leverage `INotification` in [MediatR](https://github.com/jbogard/MediatR) to assist with the publisher-subscriber implementation.

All of these code samples are taken from my [Lewee](https://github.com/TheMagnificent11/lewee) project.

## Domain Event

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

## Storing Domain Events

### Entity Framework Entity

Below is entity class used to store the details about a domain event after it related aggregate root has been persisted.

Things to note, we are storing the domain event as JSON (`DomainEventJson`) and then storing the assembly name (`DomainEventAssemblyName`) and class name (`DomainEventClassName`) to allow us to deserialize the JSON back to the doamin event in the `ToDomainEvent` method.

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

### How the aggregate root stores the domain event

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
        public const string TableInUse = "Table is already being used";
        public const string CannotOrderIfTableNotInUse = "Cannot order items if table is not in use";
    }
}
```

### How the DbContext stores the domain event

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

Finally, here's abstract `DbContext` implementation that exposes the `DomainEventReference` `DbSet` and adds the `DomainEventSaveChangesInterceptor`.

```cs
using Microsoft.EntityFrameworkCore;

public abstract class ApplicationDbContext<TContext> : DbContext, IApplicationDbContext
    where TContext : DbContext, IApplicationDbContext
{
    private readonly IAuthenticatedUserService authenticatedUserService;

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

        optionsBuilder.AddInterceptors(new DomainEventSaveChangesInterceptor<TContext>(this.authenticatedUserService));
    }

    protected abstract void ConfigureDatabaseModel(ModelBuilder modelBuilder);
}
```

## Dispatching Domain Events
