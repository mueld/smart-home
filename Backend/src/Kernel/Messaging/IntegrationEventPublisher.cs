//using Newtonsoft.Json;

//namespace Backend.Kernel.Messaging;

//internal class IntegrationEventPublisher(IEnumerable<IOutboxDbContext> dbContexts) : ModuleDbContextResolver<IOutboxDbContext>(dbContexts), IIntegrationEventPublisher
//{
//    private static readonly JsonSerializerSettings JsonSerializerSettings = new()
//    {
//        TypeNameHandling = TypeNameHandling.All,
//    };
//    public async Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken)
//    {
//        var dbContext= GetDbContext(integrationEvent);

//        var outboxMessage = OutboxMessage.Create(
//            integrationEvent.GetType().Name,
//            JsonConvert.SerializeObject(integrationEvent, JsonSerializerSettings)
//        );

//        await dbContext.OutboxMessages.AddAsync(outboxMessage, cancellationToken);
//        await dbContext.SaveChangesAsync(cancellationToken);
//    }
//}
