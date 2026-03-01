using System;
using System.IO;
using D3Energy.UI.Automation.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace D3Energy.UI.Automation.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<DbSequence>     Sequences { get; set; } = null!;
        public DbSet<DbSession>      Sessions  { get; set; } = null!;
        public DbSet<DbScheduledJob> Jobs      { get; set; } = null!;
        public DbSet<DbTestCase>     TestCases { get; set; } = null!;

        private static string DbPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "D3Energy.UI.Automation",
                "d3energy.ui.automation.db");

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
            options.UseSqlite($"Data Source={DbPath}");
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<DbTestCase>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Title).IsRequired().HasMaxLength(200);
                e.HasIndex(x => x.Title);
                e.HasOne(x => x.Sequence).WithMany()
                 .HasForeignKey(x => x.SequenceId).OnDelete(DeleteBehavior.SetNull);
            });

            model.Entity<DbSequence>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).IsRequired().HasMaxLength(200);
                e.HasMany(x => x.Sessions).WithOne(x => x.Sequence)
                 .HasForeignKey(x => x.SequenceId).OnDelete(DeleteBehavior.SetNull);
                e.HasMany(x => x.Jobs).WithOne(x => x.Sequence)
                 .HasForeignKey(x => x.SequenceId).OnDelete(DeleteBehavior.Cascade);
            });

            model.Entity<DbSession>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.ExternalId);
                e.HasOne(x => x.Job).WithMany(x => x.Sessions)
                 .HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.SetNull);
            });

            model.Entity<DbScheduledJob>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            });
        }

        public static AppDbContext Create()
        {
            var ctx = new AppDbContext();
            ctx.Database.EnsureCreated();
            return ctx;
        }
    }
}
