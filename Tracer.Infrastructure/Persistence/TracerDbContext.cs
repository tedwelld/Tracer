using Microsoft.EntityFrameworkCore;
using Tracer.Core.Entities;
using Tracer.Core.Enums;

namespace Tracer.Infrastructure.Persistence;

public sealed class TracerDbContext(DbContextOptions<TracerDbContext> options) : DbContext(options)
{
    public DbSet<DiscoveredDevice> DiscoveredDevices => Set<DiscoveredDevice>();
    public DbSet<DeviceObservation> DeviceObservations => Set<DeviceObservation>();
    public DbSet<DeviceAlert> DeviceAlerts => Set<DeviceAlert>();
    public DbSet<ScanBatch> ScanBatches => Set<ScanBatch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DiscoveredDevice>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DeviceKey).HasMaxLength(128);
            entity.Property(x => x.DisplayName).HasMaxLength(256);
            entity.Property(x => x.HardwareAddress).HasMaxLength(64);
            entity.Property(x => x.NetworkName).HasMaxLength(256);
            entity.Property(x => x.SecurityType).HasMaxLength(128);
            entity.Property(x => x.Password).HasMaxLength(256);
            entity.Property(x => x.LastInterfaceName).HasMaxLength(256);
            entity.Property(x => x.FrequencyBand).HasMaxLength(64);
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
            entity.Property(x => x.Title).HasMaxLength(256);
            entity.Property(x => x.Message).HasMaxLength(1024);
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
            entity.HasIndex(x => x.CompletedUtc);
        });
    }
}
