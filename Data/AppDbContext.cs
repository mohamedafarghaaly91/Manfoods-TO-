using Microsoft.EntityFrameworkCore;
using MvcApp.Models;

namespace MvcApp.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<ActiveEmployee> ActiveEmployees { get; set; }
    public DbSet<Resignation> Resignations { get; set; }
    public DbSet<StoreReference> StoreReferences { get; set; }
    public DbSet<UploadLog> UploadLogs { get; set; }
    public DbSet<ExitInterview> ExitInterviews { get; set; }
    public DbSet<PasswordResetOtp> PasswordResetOtps { get; set; }
    public DbSet<AppSetting> AppSettings { get; set; }
    public DbSet<AiUsageDaily> AiUsageDaily { get; set; }
    public DbSet<StoreActionPlan> StoreActionPlans { get; set; }
    public DbSet<ActionPlanRecommendation> ActionPlanRecommendations { get; set; }
    public DbSet<ActionPlanNote> ActionPlanNotes { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();
        modelBuilder.Entity<AiUsageDaily>().HasKey(a => new { a.UserId, a.UsageDate });

        // Only one Active plan per store. This config only takes effect for a
        // fresh EnsureCreated() database (e.g. local/test) — the real schema
        // change for the existing production database is scripts/migrate.sql,
        // since this app doesn't use EF Migrations.
        modelBuilder.Entity<StoreActionPlan>()
            .HasIndex(p => p.StoreName)
            .IsUnique()
            .HasFilter("status = 'Active'")
            .HasDatabaseName("ux_store_action_plans_active_store");
    }
}
