using Microsoft.EntityFrameworkCore;

namespace PressureMonitor.Models;

public class ApplicationDbContext : DbContext
{
    
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) {}
    
    public DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // We will need to add specific configurations later
        
        
        base.OnModelCreating(modelBuilder); 
    }
}