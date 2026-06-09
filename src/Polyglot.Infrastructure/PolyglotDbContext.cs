using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Polyglot.Domain;

namespace Polyglot.Infrastructure
{
    public class PolyglotDbContext(DbContextOptions<PolyglotDbContext> options) : DbContext(options)
    {
        public DbSet<User> Users => Set<User>();
        public DbSet<Chat> Chats => Set<Chat>();
        public DbSet<Message> Messages => Set<Message>();
        public DbSet<Attachment> Attachments => Set<Attachment>();
        public DbSet<ModelListEntry> ModelListEntries => Set<ModelListEntry>();
        public DbSet<AdminSettings> AdminSettings => Set<AdminSettings>();
        public DbSet<Model> Models => Set<Model>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(PolyglotDbContext).Assembly);
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            ApplySaveChangesGuards();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            ApplySaveChangesGuards();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void ApplySaveChangesGuards()
        {
            foreach (var entry in ChangeTracker.Entries<BaseEntity>())
                if (entry.State == EntityState.Modified)
                    entry.Entity.UpdatedAt = DateTime.UtcNow;

            if (ChangeTracker.Entries<AdminSettings>().Any(e => e.State == EntityState.Deleted))
                throw new InvalidOperationException("AdminSettings cannot be deleted");
        }
    }

    public class PolyglotDbContextFactory : IDesignTimeDbContextFactory<PolyglotDbContext>
    {
        public PolyglotDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<PolyglotDbContext>();
            optionsBuilder.UseNpgsql();
            return new PolyglotDbContext(optionsBuilder.Options);
        }
    }
}
