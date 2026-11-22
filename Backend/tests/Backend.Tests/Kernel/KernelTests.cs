using System.Data.Common;
using System.Diagnostics;
using Backend.Kernel;
using Backend.Kernel.Behaviors;
using Mediator;
using Backend.Tests.Kernel.ModuleA;
using Backend.Tests.Kernel.ModuleB;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Kernel;

/// <summary>
/// Tests the kernel at its seam: a command or query goes in through the mediator, and the observable
/// result is the outcome and what ended up in the database (one shared connection, as in AD-14).
/// </summary>
public sealed class KernelTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private ServiceProvider _services = null!;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddSingleton<TestProbe>();
        services.AddSingleton<DbConnection>(_connection); // an instance, so the container does not dispose it per scope
        services.AddMediator(options =>
        {
            options.ServiceLifetime = ServiceLifetime.Scoped;
            options.PipelineBehaviors = [typeof(ValidationBehavior<,>), typeof(TransactionBehavior<,>)];
            options.Telemetry.EnableTracing = true;
            options.Telemetry.EnableMetrics = true;
        });
        services.AddKernel(typeof(KernelTests).Assembly);
        services.AddModule<ModuleADbContext>("Backend.Tests.Kernel.ModuleA", (options, connection) => options.UseSqlite(connection));
        services.AddModule<ModuleBDbContext>("Backend.Tests.Kernel.ModuleB", (options, connection) => options.UseSqlite(connection));
        _services = services.BuildServiceProvider(validateScopes: true);

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ModuleADbContext>().GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        await scope.ServiceProvider.GetRequiredService<ModuleBDbContext>().GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<TResult> InNewScope<TResult>(Func<IServiceProvider, Task<TResult>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private Task<TResponse> Send<TResponse>(ICommand<TResponse> command) =>
        InNewScope(async services => await services.GetRequiredService<IMediator>().Send(command));

    private Task<TResponse> Send<TResponse>(IQuery<TResponse> query) =>
        InNewScope(async services => await services.GetRequiredService<IMediator>().Send(query));

    private Task<int> Notes() => Send(new CountNotes()).ContinueWith(t => t.Result);

    private Task<int> Items() =>
        InNewScope(services => services.GetRequiredService<ModuleBDbContext>().Items.AsNoTracking().CountAsync());

    private Task<int> AuditEntries() =>
        InNewScope(services => services.GetRequiredService<ModuleADbContext>().AuditEntries.AsNoTracking().CountAsync());

    [Fact]
    public async Task A_successful_command_returns_its_outcome_and_commits()
    {
        var outcome = await Send(new CreateNote("hello"));

        Assert.True(outcome.IsSuccess);
        Assert.Equal(2, await Notes()); // the note and the audit note added by the domain event handler
    }

    [Fact]
    public async Task Domain_events_are_dispatched_in_a_loop_and_saved_in_the_same_save()
    {
        await Send(new CreateNote("hello"));

        // NoteCreated (hello) -> audit row + audit note -> NoteCreated (audit) -> audit row
        Assert.Equal(2, await AuditEntries());
        Assert.Equal(2, await Notes());
    }

    [Fact]
    public async Task An_event_handler_that_saves_itself_runs_in_the_transaction_of_the_command()
    {
        var outcome = await Send(new CreateNote("reject"));

        Assert.False(outcome.IsSuccess);
        Assert.Equal(0, await AuditEntries()); // written and saved by the handler, rolled back with the command
        Assert.Equal(0, await Notes());
    }

    [Fact]
    public async Task An_event_handler_can_send_a_command_of_another_module_in_the_same_transaction()
    {
        var outcome = await Send(new CreateNote("ok [item]"));

        Assert.True(outcome.IsSuccess);
        Assert.Equal(1, await Items());
    }

    [Fact]
    public async Task A_failure_after_the_event_chain_rolls_back_the_commands_sent_from_event_handlers()
    {
        var outcome = await Send(new CreateNote("reject [item]"));

        Assert.False(outcome.IsSuccess);
        Assert.Equal(0, await Items());
        Assert.Equal(0, await Notes());
        Assert.Equal(0, await AuditEntries());
    }

    [Fact]
    public async Task An_event_handler_can_send_a_command_of_the_same_module()
    {
        var outcome = await Send(new CreateNote("ok [command]"));

        Assert.True(outcome.IsSuccess);
        Assert.True(await Notes() >= 3); // the note, its audit note and the nested note
    }

    [Fact]
    public async Task An_invalid_command_returns_ValidationFailed_and_never_reaches_the_handler()
    {
        var outcome = await Send(new CreateNote(""));

        var failed = Assert.IsType<ValidationFailed>(outcome.Value);
        Assert.Contains(nameof(CreateNote.Text), failed.Errors!.Keys);
        Assert.Empty(_services.GetRequiredService<TestProbe>().Calls);
        Assert.Equal(0, await Notes());
    }

    [Fact]
    public async Task A_failed_outcome_rolls_the_transaction_back()
    {
        var outcome = await Send(new CreateNote("reject"));

        Assert.False(outcome.IsSuccess);
        Assert.IsType<Rejected>(outcome.Value);
        Assert.Equal(0, await Notes());
        Assert.Equal(0, await AuditEntries());
    }

    [Fact]
    public async Task An_exception_rolls_the_transaction_back_and_is_not_swallowed()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Send(new CreateNote("throw")));

        Assert.Equal(0, await Notes());
    }

    [Fact]
    public async Task A_query_runs_without_a_transaction_behavior()
    {
        await Send(new CreateNote("hello"));

        var count = await Send(new CountNotes());

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task A_command_of_another_module_joins_the_transaction_and_commits_with_it()
    {
        var outcome = await Send(new CreateNoteAndItem("item"));

        Assert.True(outcome.IsSuccess);
        Assert.Equal(1, await Items());
        Assert.True(await Notes() >= 1);
    }

    [Fact]
    public async Task A_failure_of_the_outer_command_rolls_back_the_joined_command_too()
    {
        var outcome = await Send(new CreateNoteAndItem("fail outer"));

        Assert.False(outcome.IsSuccess);
        Assert.Equal(0, await Items());
        Assert.Equal(0, await Notes());
    }

    [Fact]
    public async Task A_failed_joined_command_rolls_back_to_its_savepoint_and_the_outer_command_can_still_commit()
    {
        var outcome = await Send(new CreateNoteAndItem("reject", IgnoreItemFailure: true));

        Assert.True(outcome.IsSuccess);
        Assert.Equal(0, await Items());
        Assert.True(await Notes() >= 1);
    }

    [Fact]
    public async Task A_command_cannot_enter_a_DbContext_that_is_already_running_a_command()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Send(new CreateNoteTwice()));

        Assert.Equal(0, await Notes());
    }

    [Fact]
    public async Task Synchronous_saving_is_refused()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ModuleADbContext>();
        db.Notes.Add(Note.Create("sync"));

        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task Every_command_and_query_is_a_span()
    {
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == Mediator.Mediator.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await Send(new CreateNote("hello"));
        await Send(new CountNotes());

        Assert.Contains(spans, s => s.OperationName == "send CreateNote" && (string?)s.GetTagItem("messaging.mediator.message.kind") == "command");
        Assert.Contains(spans, s => s.OperationName == "send CountNotes" && (string?)s.GetTagItem("messaging.mediator.message.kind") == "query");
    }
}
