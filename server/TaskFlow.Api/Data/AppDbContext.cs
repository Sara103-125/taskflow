using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TaskFlow.Api.Entities;

namespace TaskFlow.Api.Data;

/// <summary>
/// IdentityDbContext adds the AspNetUsers / AspNetRoles tables for us.
/// </summary>
public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Board> Boards => Set<Board>();
    public DbSet<BoardMember> BoardMembers => Set<BoardMember>();
    public DbSet<Column> Columns => Set<Column>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    /// <summary>
    /// We always store UTC. SQL Server's datetime2 doesn't remember that, so mark values read
    /// back as UTC; then JSON sends "2026-10-04T09:00:00Z" and the browser shows local time.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    private class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter()
            : base(toDb => toDb, fromDb => DateTime.SpecifyKind(fromDb, DateTimeKind.Utc)) { }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(e =>
        {
            e.Property(u => u.DisplayName).HasMaxLength(50).IsRequired();
        });

        builder.Entity<Board>(e =>
        {
            e.Property(b => b.Name).HasMaxLength(100).IsRequired();
        });

        builder.Entity<BoardMember>(e =>
        {
            e.HasKey(m => new { m.BoardId, m.UserId });
            e.HasOne(m => m.Board).WithMany(b => b.Members)
                .HasForeignKey(m => m.BoardId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.User).WithMany(u => u.Memberships)
                .HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Column>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(50).IsRequired();
            e.HasOne(c => c.Board).WithMany(b => b.Columns)
                .HasForeignKey(c => c.BoardId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TaskItem>(e =>
        {
            e.Property(t => t.Title).HasMaxLength(200).IsRequired();
            e.Property(t => t.Description).HasMaxLength(4000);
            e.HasOne(t => t.Column).WithMany(c => c.Tasks)
                .HasForeignKey(t => t.ColumnId).OnDelete(DeleteBehavior.Cascade);
            // Deleting a board cascades Board -> Column -> Task. A second cascade from Users
            // would give SQL Server "multiple cascade paths", so the assignee link is NoAction
            // (the service clears assignees itself when a member leaves a board).
            e.HasOne(t => t.Assignee).WithMany()
                .HasForeignKey(t => t.AssigneeId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(t => new { t.ColumnId, t.Position });
        });

        builder.Entity<ActivityLog>(e =>
        {
            e.Property(a => a.Message).HasMaxLength(500).IsRequired();
            e.HasOne(a => a.Board).WithMany(b => b.Activity)
                .HasForeignKey(a => a.BoardId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.User).WithMany()
                .HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(a => new { a.BoardId, a.CreatedAt });
        });
    }
}
