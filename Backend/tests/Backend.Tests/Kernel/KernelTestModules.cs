using Backend.Kernel;
using Backend.Kernel.Domain;
using Mediator;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

// Two small test modules (A and B) with their own DbContext, to test the kernel at its seam:
// mediator, pipeline, transaction behavior and domain event interceptor.

namespace Backend.Tests.Kernel.ModuleA
{
    public sealed record Rejected(string Reason);

    public sealed class Note : AggregateRoot
    {
        public string Text { get; private set; } = "";

        public static Note Create(string text)
        {
            var note = new Note { Text = text };
            note.Raise(new NoteCreated(note.Id, text));
            return note;
        }
    }

    public sealed class AuditEntry
    {
        public Guid Id { get; set; } = Guid.CreateVersion7();

        public string Text { get; set; } = "";
    }

    public sealed record NoteCreated(Guid NoteId, string Text) : DomainEvent;

    public sealed class ModuleADbContext(DbContextOptions<ModuleADbContext> options) : DbContext(options)
    {
        public DbSet<Note> Notes => Set<Note>();

        public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    }

    public sealed record CreateNote(string Text) : ICommand<Outcome<Guid, Rejected>>;

    public sealed class CreateNoteValidator : AbstractValidator<CreateNote>
    {
        public CreateNoteValidator() => RuleFor(c => c.Text).NotEmpty();
    }

    public sealed class CreateNoteHandler(ModuleADbContext db, TestProbe probe)
        : ICommandHandler<CreateNote, Outcome<Guid, Rejected>>
    {
        public async ValueTask<Outcome<Guid, Rejected>> Handle(CreateNote request, CancellationToken cancellationToken)
        {
            probe.Calls.Add(nameof(CreateNoteHandler));

            var note = Note.Create(request.Text);
            db.Notes.Add(note);
            await db.SaveChangesAsync(cancellationToken);

            return request.Text switch
            {
                _ when request.Text.StartsWith("reject") => new Rejected("rejected after saving"),
                "throw" => throw new InvalidOperationException("boom"),
                _ => note.Id,
            };
        }
    }

    /// <summary>
    /// Handles NoteCreated inside the transaction of the command (AD-6): writes an audit row and saves itself.
    /// Depending on the text it adds another note (a cascade through its own save), or sends commands.
    /// </summary>
    public sealed class NoteCreatedHandler(ModuleADbContext db, IMediator mediator) : INotificationHandler<NoteCreated>
    {
        public async ValueTask Handle(NoteCreated notification, CancellationToken cancellationToken)
        {
            db.AuditEntries.Add(new AuditEntry { Text = notification.Text });

            if (notification.Text is not ("audit" or "nested"))
            {
                db.Notes.Add(Note.Create("audit"));
            }

            await db.SaveChangesAsync(cancellationToken);

            if (notification.Text.Contains("[item]"))
            {
                await mediator.Send(new ModuleB.AddItem("from event"), cancellationToken);
            }

            if (notification.Text.Contains("[command]"))
            {
                await mediator.Send(new CreateNote("nested"), cancellationToken);
            }
        }
    }

    /// <summary>Command of module A that calls a command of module B inside the same transaction (AD-14).</summary>
    public sealed record CreateNoteAndItem(string ItemText, bool IgnoreItemFailure = false) : ICommand<Outcome<Guid, Rejected>>;

    public sealed class CreateNoteAndItemHandler(ModuleADbContext db, IMediator mediator)
        : ICommandHandler<CreateNoteAndItem, Outcome<Guid, Rejected>>
    {
        public async ValueTask<Outcome<Guid, Rejected>> Handle(CreateNoteAndItem request, CancellationToken cancellationToken)
        {
            var note = Note.Create("with item");
            db.Notes.Add(note);
            await db.SaveChangesAsync(cancellationToken);

            var item = await mediator.Send(new ModuleB.AddItem(request.ItemText), cancellationToken);
            if (!item.IsSuccess && !request.IgnoreItemFailure)
            {
                return new Rejected("item failed");
            }

            return item.IsSuccess && request.ItemText == "fail outer" ? new Rejected("outer failed") : note.Id;
        }
    }

    /// <summary>Calls another command of the same module: must be refused (AD-14).</summary>
    public sealed record CreateNoteTwice : ICommand<Outcome<Guid, Rejected>>;

    public sealed class CreateNoteTwiceHandler(IMediator mediator) : ICommandHandler<CreateNoteTwice, Outcome<Guid, Rejected>>
    {
        public async ValueTask<Outcome<Guid, Rejected>> Handle(CreateNoteTwice request, CancellationToken cancellationToken)
            => await mediator.Send(new CreateNote("inner"), cancellationToken);
    }

    public sealed record CountNotes : IQuery<int>;

    public sealed class CountNotesHandler(ModuleADbContext db) : IQueryHandler<CountNotes, int>
    {
        public async ValueTask<int> Handle(CountNotes request, CancellationToken cancellationToken)
            => await db.Notes.AsNoTracking().CountAsync(cancellationToken);
    }
}

namespace Backend.Tests.Kernel.ModuleB
{
    public sealed class Item
    {
        public Guid Id { get; set; } = Guid.CreateVersion7();

        public string Text { get; set; } = "";
    }

    public sealed class ModuleBDbContext(DbContextOptions<ModuleBDbContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();
    }

    public sealed record AddItem(string Text) : ICommand<Outcome<Guid, ModuleA.Rejected>>;

    public sealed class AddItemHandler(ModuleBDbContext db) : ICommandHandler<AddItem, Outcome<Guid, ModuleA.Rejected>>
    {
        public async ValueTask<Outcome<Guid, ModuleA.Rejected>> Handle(AddItem request, CancellationToken cancellationToken)
        {
            var item = new Item { Text = request.Text };
            db.Items.Add(item);
            await db.SaveChangesAsync(cancellationToken);

            return request.Text == "reject" ? new ModuleA.Rejected("item rejected") : item.Id;
        }
    }
}

namespace Backend.Tests.Kernel
{
    public sealed class TestProbe
    {
        public List<string> Calls { get; } = [];
    }
}
