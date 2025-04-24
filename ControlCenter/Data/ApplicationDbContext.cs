using Microsoft.EntityFrameworkCore;

namespace ControlCenter.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<DoorRelayConfig> DoorRelayConfigs { get; set; }
    public DbSet<SerialPortConfig> SerialPortConfigs { get; set; }
    public DbSet<DoorConfig> DoorConfigs { get; set; }
    public DbSet<LockerConfig> LockerConfigs { get; set; }
    public DbSet<ProjectorConfig> ProjectorConfigs { get; set; }
    public DbSet<AppConfig> AppConfigs { get; set; }
    public DbSet<RelayCommand> RelayCommands { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
}