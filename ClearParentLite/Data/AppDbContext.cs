using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ClearParentLite.Data;
public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<TimelineEntry> Entries => Set<TimelineEntry>();
    public DbSet<EntryRevision> Revisions => Set<EntryRevision>();
    public DbSet<EntryUpdate> Updates => Set<EntryUpdate>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<ShareGrant> Shares => Set<ShareGrant>();
    public DbSet<ShareGrantEntry> ShareEntries => Set<ShareGrantEntry>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<TimelineEntry>().HasIndex(e => new { e.OwnerUserId, e.EventDate, e.Id }).IsDescending(false, true, false);
        b.Entity<TimelineEntry>().HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TimelineEntry>().HasQueryFilter(e => !e.IsDeleted);
        b.Entity<EntryRevision>().HasIndex(e => new { e.EntryId, e.RevisionNumber }).IsUnique();
        b.Entity<TimelineEntry>().HasMany(e => e.Revisions).WithOne().HasForeignKey(e => e.EntryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TimelineEntry>().HasMany(e => e.Updates).WithOne().HasForeignKey(e => e.EntryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TimelineEntry>().HasMany(e => e.Attachments).WithOne().HasForeignKey(e => e.EntryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Attachment>().HasIndex(e => e.StorageKey).IsUnique();
        b.Entity<ShareGrant>().HasIndex(e => e.TokenHash).IsUnique().HasFilter("[TokenHash] IS NOT NULL");
        b.Entity<ShareGrant>().HasIndex(e => new { e.OwnerUserId, e.InvitedEmailNormalized });
        b.Entity<ShareGrant>().HasMany(e => e.Entries).WithOne().HasForeignKey(e => e.ShareGrantId);
        b.Entity<ShareGrantEntry>().HasKey(e => new { e.ShareGrantId, e.EntryId });
        b.Entity<ShareGrantEntry>().HasOne<TimelineEntry>().WithMany().HasForeignKey(e => e.EntryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AuditEvent>().HasIndex(e => new { e.OwnerUserId, e.OccurredUtc });
    }
    void ValidateImmutability()
    {
        if (ChangeTracker.Entries<EntryRevision>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Entry revisions are immutable.");
        if (ChangeTracker.Entries<EntryUpdate>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Updates are append-only.");
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess) { ValidateImmutability(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) { ValidateImmutability(); return base.SaveChangesAsync(acceptAllChangesOnSuccess,cancellationToken); }
}
