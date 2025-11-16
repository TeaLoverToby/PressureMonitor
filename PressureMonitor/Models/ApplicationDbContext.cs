using Microsoft.EntityFrameworkCore;

namespace PressureMonitor.Models;

public class ApplicationDbContext : DbContext
{
    
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) {}
    
    public DbSet<User> Users { get; set; }
    public DbSet<Clinician> Clinicians { get; set; }
    public DbSet<Patient> Patients { get; set; }
    public DbSet<Admin> Admins { get; set; }

    // This method allows us to configure the relationships between the entities (e.g foreign keys, cardinality and that sort of thing)
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // User -> Clinician relationship (one-to-one)
        modelBuilder.Entity<User>()
            .HasOne(u => u.Clinician)
            .WithOne(c => c.User)
            .HasForeignKey<Clinician>(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        
        // User -> Patient (one-to-one)
        modelBuilder.Entity<User>()
            .HasOne(u => u.Patient)
            .WithOne(p => p.User)
            .HasForeignKey<Patient>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        
        // User -> Admin (one-to-one)
        modelBuilder.Entity<User>()
            .HasOne(u => u.Admin)
            .WithOne(a => a.User)
            .HasForeignKey<Admin>(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        
        base.OnModelCreating(modelBuilder); 
    }
}