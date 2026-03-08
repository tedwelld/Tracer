using Microsoft.EntityFrameworkCore;
using Tracer.Core.Entities;
using Tracer.Core.Enums;

namespace Tracer.Infrastructure.Persistence;

public sealed class TracerDbContext(DbContextOptions<TracerDbContext> options) : DbContext(options)
{
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();
    public DbSet<LoginAuditLog> LoginAuditLogs => Set<LoginAuditLog>();
    public DbSet<RuntimeSettings> RuntimeSettings => Set<RuntimeSettings>();
    public DbSet<ScanEventLog> ScanEventLogs => Set<ScanEventLog>();
    public DbSet<DiscoveredDevice> DiscoveredDevices => Set<DiscoveredDevice>();
    public DbSet<DeviceObservation> DeviceObservations => Set<DeviceObservation>();
    public DbSet<DeviceAlert> DeviceAlerts => Set<DeviceAlert>();
    public DbSet<ScanBatch> ScanBatches => Set<ScanBatch>();
    public DbSet<OuiVendor> OuiVendors => Set<OuiVendor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminUser>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserName).HasMaxLength(128);
            entity.Property(x => x.NormalizedUserName).HasMaxLength(128);
            entity.Property(x => x.PasswordHash).HasMaxLength(512);
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => x.NormalizedUserName).IsUnique();
        });

        modelBuilder.Entity<AdminAuditLog>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserName).HasMaxLength(128);
            entity.Property(x => x.Action).HasMaxLength(128);
            entity.Property(x => x.EntityType).HasMaxLength(128);
            entity.Property(x => x.EntityId).HasMaxLength(128);
            entity.Property(x => x.IpAddress).HasMaxLength(64);
            entity.Property(x => x.UserAgent).HasMaxLength(512);
            entity.Property(x => x.Details).HasMaxLength(2048);
            entity.HasIndex(x => x.CreatedUtc);
            entity.HasOne(x => x.AdminUser)
                .WithMany()
                .HasForeignKey(x => x.AdminUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<LoginAuditLog>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserName).HasMaxLength(128);
            entity.Property(x => x.FailureReason).HasMaxLength(256);
            entity.Property(x => x.IpAddress).HasMaxLength(64);
            entity.Property(x => x.UserAgent).HasMaxLength(512);
            entity.HasIndex(x => x.CreatedUtc);
            entity.HasOne(x => x.AdminUser)
                .WithMany()
                .HasForeignKey(x => x.AdminUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RuntimeSettings>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<ScanEventLog>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RadioKind).HasMaxLength(32);
            entity.Property(x => x.Level).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.EventType).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.ScannerNode).HasMaxLength(128);
            entity.Property(x => x.Message).HasMaxLength(512);
            entity.Property(x => x.Details).HasMaxLength(2048);
            entity.HasIndex(x => x.CreatedUtc);
            entity.HasOne(x => x.ScanBatch)
                .WithMany(x => x.EventLogs)
                .HasForeignKey(x => x.ScanBatchId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Device)
                .WithMany()
                .HasForeignKey(x => x.DeviceId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DiscoveredDevice>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DeviceKey).HasMaxLength(128);
            entity.Property(x => x.DisplayName).HasMaxLength(256);
            entity.Property(x => x.HardwareAddress).HasMaxLength(64);
            entity.Property(x => x.NetworkName).HasMaxLength(256);
            entity.Property(x => x.SecurityType).HasMaxLength(128);
            entity.Property(x => x.Password).HasMaxLength(256);
            entity.Property(x => x.VendorPrefix).HasMaxLength(16);
            entity.Property(x => x.VendorName).HasMaxLength(128);
            entity.Property(x => x.DeviceType).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Reputation).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.RiskReasons).HasMaxLength(1024);
            entity.Property(x => x.LastRecommendation).HasMaxLength(512);
            entity.Property(x => x.ConnectionState).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.EstimatedDistanceMeters).HasPrecision(8, 2);
            entity.Property(x => x.LastInterfaceName).HasMaxLength(256);
            entity.Property(x => x.FrequencyBand).HasMaxLength(64);
            entity.Property(x => x.MovementTrend).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.RadioKind).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => x.DeviceKey).IsUnique();
            entity.HasIndex(x => x.LastSeenUtc);
        });

        modelBuilder.Entity<DeviceObservation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DisplayName).HasMaxLength(256);
            entity.Property(x => x.HardwareAddress).HasMaxLength(64);
            entity.Property(x => x.NetworkName).HasMaxLength(256);
            entity.Property(x => x.SecurityType).HasMaxLength(128);
            entity.Property(x => x.InterfaceName).HasMaxLength(256);
            entity.Property(x => x.FrequencyBand).HasMaxLength(64);
            entity.Property(x => x.RawPayload).HasMaxLength(1024);
            entity.Property(x => x.DeviceType).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Reputation).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.EstimatedDistanceMeters).HasPrecision(8, 2);
            entity.Property(x => x.MovementTrend).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.ConnectionState).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => x.ObservedUtc);
            entity.HasOne(x => x.Device)
                .WithMany(x => x.Observations)
                .HasForeignKey(x => x.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.ScanBatch)
                .WithMany(x => x.Observations)
                .HasForeignKey(x => x.ScanBatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeviceAlert>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AlertType).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.Severity).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.NotificationStatus).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Title).HasMaxLength(256);
            entity.Property(x => x.Message).HasMaxLength(1024);
            entity.Property(x => x.LastNotificationError).HasMaxLength(1024);
            entity.HasIndex(x => new { x.Status, x.CreatedUtc });
            entity.HasOne(x => x.Device)
                .WithMany(x => x.Alerts)
                .HasForeignKey(x => x.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScanBatch>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ScannerNode).HasMaxLength(128);
            entity.Property(x => x.AdapterStatusSummary).HasMaxLength(512);
            entity.HasIndex(x => x.CompletedUtc);
        });

        modelBuilder.Entity<OuiVendor>(entity =>
        {
            entity.HasKey(x => x.Prefix);
            entity.Property(x => x.Prefix).HasMaxLength(16);
            entity.Property(x => x.VendorName).HasMaxLength(256);
            entity.Property(x => x.Country).HasMaxLength(128);
            entity.HasIndex(x => x.UpdatedAt);
        });
    }
}
