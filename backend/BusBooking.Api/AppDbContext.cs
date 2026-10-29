using Microsoft.EntityFrameworkCore;

namespace BusBooking.Api;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Bus> Buses => Set<Bus>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<Bus>().HasIndex(x => x.RegistrationNumber).IsUnique();
        modelBuilder.Entity<Bus>().Property(x => x.RegistrationNumber).HasMaxLength(32);
        modelBuilder.Entity<Trip>().Property(x => x.Price).HasPrecision(10, 2);
        modelBuilder.Entity<Trip>()
            .HasOne(x => x.Bus)
            .WithMany(x => x.Trips)
            .HasForeignKey(x => x.BusId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Booking>().HasIndex(x => new { x.TripId, x.SeatNumber })
            .IsUnique()
            .HasFilter("\"Status\" = 'Confirmed'");
        modelBuilder.Entity<Booking>()
            .HasOne(x => x.User)
            .WithMany(x => x.Bookings)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Booking>()
            .HasOne(x => x.Trip)
            .WithMany(x => x.Bookings)
            .HasForeignKey(x => x.TripId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PasswordResetToken>().HasIndex(x => x.TokenHash).IsUnique();
    }
}
