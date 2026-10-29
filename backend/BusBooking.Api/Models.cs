namespace BusBooking.Api;

public sealed class AppUser
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public string Role { get; set; } = "User";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<Booking> Bookings { get; set; } = [];
}

public sealed class Bus
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string RegistrationNumber { get; set; }
    public int Capacity { get; set; }
    public ICollection<Trip> Trips { get; set; } = [];
}

public sealed class Trip
{
    public int Id { get; set; }
    public int BusId { get; set; }
    public Bus? Bus { get; set; }
    public required string Origin { get; set; }
    public required string Destination { get; set; }
    public DateTime DepartureUtc { get; set; }
    public DateTime ArrivalUtc { get; set; }
    public decimal Price { get; set; }
    public string Status { get; set; } = "Scheduled";
    public ICollection<Booking> Bookings { get; set; } = [];
}

public sealed class Booking
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    public int TripId { get; set; }
    public Trip? Trip { get; set; }
    public int SeatNumber { get; set; }
    public required string PassengerName { get; set; }
    public int PassengerAge { get; set; }
    public required string PassengerGender { get; set; }
    public required string PassengerMobileNumber { get; set; }
    public string AdhiparasakthiCircleName { get; set; } = string.Empty;
    public DateTime BookedAtUtc { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Confirmed";
}

public sealed class PasswordResetToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    public required string TokenHash { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
}
