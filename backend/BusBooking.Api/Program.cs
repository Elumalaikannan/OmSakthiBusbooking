using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BusBooking.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];
var jwtSecretKey = builder.Configuration["Jwt:SecretKey"]
    ?? throw new InvalidOperationException("Jwt:SecretKey must be configured as a Base64 value.");
byte[] jwtKey;
try
{
    jwtKey = Convert.FromBase64String(jwtSecretKey);
}
catch (FormatException exception)
{
    throw new InvalidOperationException("Jwt:SecretKey must be a valid Base64 value.", exception);
}
if (jwtKey.Length < 32)
    throw new InvalidOperationException("Jwt:SecretKey must decode to at least 32 bytes.");
if (string.IsNullOrWhiteSpace(jwtIssuer) || string.IsNullOrWhiteSpace(jwtAudience))
    throw new InvalidOperationException("Jwt:Issuer and Jwt:Audience must be configured.");
var jwtExpiryMinutes = builder.Configuration.GetValue<int?>("Jwt:ExpiryMinutes")
    ?? throw new InvalidOperationException("Jwt:ExpiryMinutes must be configured.");
if (jwtExpiryMinutes < 1)
    throw new InvalidOperationException("Jwt:ExpiryMinutes must be greater than zero.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["https://omsakthivignannager02.netlify.app"]).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(jwtKey),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    };
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});

var app = builder.Build();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await EnsureTripStatusColumnAsync(db);
    await EnsureBookingColumnsAsync(db);
    await EnsureActiveSeatUniqueIndexAsync(db);
    var seedEmail = app.Configuration["AdminSeed:Email"]?.Trim().ToLowerInvariant();
    var seedPassword = app.Configuration["AdminSeed:Password"];
    if (!string.IsNullOrWhiteSpace(seedEmail) && !string.IsNullOrWhiteSpace(seedPassword)
        && !await db.Users.AnyAsync(user => user.Email == seedEmail))
    {
        var admin = new AppUser { Name = "Administrator", Email = seedEmail, PasswordHash = "" , Role = "Admin" };
        admin.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>()
            .HashPassword(admin, seedPassword);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
    }
}

var auth = app.MapGroup("/api/auth");
auth.MapPost("/register", async (RegisterRequest request, AppDbContext db,
    IPasswordHasher<AppUser> hasher) =>
{
    var email = NormalizeEmail(request.Email);
    if (string.IsNullOrWhiteSpace(request.Name) || email is null
        || string.IsNullOrEmpty(request.Password) || request.Password.Length < 8)
        return Results.BadRequest(new { message = "Name, a valid email, and a password of at least 8 characters are required." });
    if (await db.Users.AnyAsync(user => user.Email == email))
        return Results.Conflict(new { message = "An account with that email already exists." });

    var user = new AppUser { Name = request.Name.Trim(), Email = email, PasswordHash = "" };
    user.PasswordHash = hasher.HashPassword(user, request.Password);
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return Results.Created("/api/auth/login", new { user.Id, user.Name, user.Email, user.Role });
});

auth.MapPost("/login", async (LoginRequest request, AppDbContext db,
    IPasswordHasher<AppUser> hasher, IConfiguration config) =>
{
    var email = NormalizeEmail(request.Email);
    var user = email is null ? null : await db.Users.SingleOrDefaultAsync(x => x.Email == email);
    if (user is null || string.IsNullOrEmpty(request.Password)
        || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password)
        == PasswordVerificationResult.Failed)
        return Results.Unauthorized();
    var token = CreateJwt(user, config);
    return Results.Ok(new AuthResponse(token, user.Id, user.Name, user.Email, user.Role));
});

auth.MapPost("/forgot-password", async (ForgotPasswordRequest request, AppDbContext db,
    IHostEnvironment environment) =>
{
    if (!environment.IsDevelopment())
        return Results.Problem("Password recovery email delivery is not configured.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    var email = NormalizeEmail(request.Email);
    var user = email is null ? null : await db.Users.SingleOrDefaultAsync(x => x.Email == email);
    if (user is null)
        return Results.Ok(new { message = "If the account exists, password reset instructions will be sent." });

    await db.PasswordResetTokens
        .Where(x => x.UserId == user.Id && x.UsedAtUtc == null)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.UsedAtUtc, DateTime.UtcNow));
    var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    db.PasswordResetTokens.Add(new PasswordResetToken
    {
        UserId = user.Id,
        TokenHash = HashToken(rawToken),
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
    });
    await db.SaveChangesAsync();
    return environment.IsDevelopment()
        ? Results.Ok(new { message = "Password reset token created. It expires in 30 minutes.", resetToken = rawToken })
        : Results.Ok(new { message = "If the account exists, password reset instructions will be sent." });
});

auth.MapPost("/reset-password", async (ResetPasswordRequest request, AppDbContext db,
    IPasswordHasher<AppUser> hasher) =>
{
    if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrEmpty(request.NewPassword)
        || request.NewPassword.Length < 8)
        return Results.BadRequest(new { message = "A reset token and a password of at least 8 characters are required." });
    var tokenHash = HashToken(request.Token);
    var reset = await db.PasswordResetTokens
        .Include(x => x.User)
        .SingleOrDefaultAsync(x => x.TokenHash == tokenHash && x.UsedAtUtc == null
            && x.ExpiresAtUtc > DateTime.UtcNow);
    if (reset?.User is null)
        return Results.BadRequest(new { message = "This reset token is invalid or expired." });
    reset.User.PasswordHash = hasher.HashPassword(reset.User, request.NewPassword);
    reset.UsedAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "Password updated. You can now sign in." });
});

var buses = app.MapGroup("/api/buses").RequireAuthorization("AdminOnly");
buses.MapGet("/", async (AppDbContext db) =>
    Results.Ok(await db.Buses.OrderBy(x => x.Name).ToListAsync()));
buses.MapPost("/", async (BusRequest request, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.RegistrationNumber)
        || request.Capacity is < 1 or > 500)
        return Results.BadRequest(new { message = "Name, registration number, and capacity (1-500) are required." });
    var bus = new Bus
    {
        Name = request.Name.Trim(),
        RegistrationNumber = request.RegistrationNumber.Trim().ToUpperInvariant(),
        Capacity = request.Capacity
    };
    db.Buses.Add(bus);
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateException) { return Results.Conflict(new { message = "A bus with that registration number already exists." }); }
    return Results.Created($"/api/buses/{bus.Id}", bus);
});

app.MapGet("/api/users", async (AppDbContext db) =>
    Results.Ok(await db.Users.OrderBy(x => x.Name)
        .Select(x => new { x.Id, x.Name, x.Email, x.Role }).ToListAsync()))
    .RequireAuthorization("AdminOnly");

var trips = app.MapGroup("/api/trips");
trips.MapGet("/manage", async (AppDbContext db) =>
{
    var results = await db.Trips.Include(x => x.Bus)
        .OrderByDescending(x => x.DepartureUtc)
        .Select(x => new TripResponse(
            x.Id, x.BusId, x.Bus!.Name, x.Bus.RegistrationNumber, x.Bus.Capacity, x.Origin, x.Destination,
            x.DepartureUtc, x.ArrivalUtc, x.Price,
            x.Bus.Capacity - x.Bookings.Count(b => b.Status == "Confirmed"),
            x.Status, x.Bookings.Count))
        .ToListAsync();
    return Results.Ok(results);
}).RequireAuthorization("AdminOnly");
trips.MapGet("/{id:int}/seats", async (int id, AppDbContext db) =>
{
    var trip = await db.Trips.Include(x => x.Bus).SingleOrDefaultAsync(x => x.Id == id);
    if (trip?.Bus is null) return Results.NotFound(new { message = "Trip not found." });
    if (trip.Status != "Scheduled")
        return Results.Conflict(new { message = "Seat selection is unavailable for a cancelled trip." });
    var bookedSeats = await db.Bookings
        .Where(x => x.TripId == id && x.Status == "Confirmed")
        .Select(x => x.SeatNumber)
        .ToListAsync();
    return Results.Ok(Enumerable.Range(1, trip.Bus.Capacity)
        .Select(seatNumber => new SeatResponse(
            seatNumber, bookedSeats.Contains(seatNumber) ? "Booked" : "Available")));
});
trips.MapGet("/", async (AppDbContext db, string? origin, string? destination, DateTime? date) =>
{
    var now = DateTime.UtcNow;
    var query = db.Trips.Include(x => x.Bus)
        .Where(x => x.DepartureUtc >= now && x.Status == "Scheduled").AsQueryable();
    if (!string.IsNullOrWhiteSpace(origin))
        query = query.Where(x => x.Origin.ToLower().Contains(origin.Trim().ToLower()));
    if (!string.IsNullOrWhiteSpace(destination))
        query = query.Where(x => x.Destination.ToLower().Contains(destination.Trim().ToLower()));
    if (date.HasValue)
    {
        var day = date.Value.Date;
        query = query.Where(x => x.DepartureUtc >= day && x.DepartureUtc < day.AddDays(1));
    }
    var results = await query.OrderBy(x => x.DepartureUtc).Select(x => new TripResponse(
        x.Id, x.BusId, x.Bus!.Name, x.Bus.RegistrationNumber, x.Bus.Capacity, x.Origin, x.Destination,
        x.DepartureUtc, x.ArrivalUtc, x.Price,
        x.Bus.Capacity - x.Bookings.Count(b => b.Status == "Confirmed"),
        x.Status, x.Bookings.Count)).ToListAsync();
    return Results.Ok(results);
});
trips.MapPost("/", async (TripRequest request, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Origin) || string.IsNullOrWhiteSpace(request.Destination)
        || request.Origin.Trim().Equals(request.Destination.Trim(), StringComparison.OrdinalIgnoreCase)
        || request.DepartureUtc <= DateTime.UtcNow || request.ArrivalUtc <= request.DepartureUtc
        || request.Price <= 0)
        return Results.BadRequest(new { message = "Provide distinct route cities, a future departure, a later arrival, and a positive price." });
    var bus = await db.Buses.FindAsync(request.BusId);
    if (bus is null) return Results.NotFound(new { message = "Bus not found." });
    var trip = new Trip
    {
        BusId = bus.Id, Origin = request.Origin.Trim(), Destination = request.Destination.Trim(),
        DepartureUtc = ToUtc(request.DepartureUtc), ArrivalUtc = ToUtc(request.ArrivalUtc), Price = request.Price
    };
    db.Trips.Add(trip);
    await db.SaveChangesAsync();
    return Results.Created($"/api/trips/{trip.Id}", new TripResponse(
        trip.Id, bus.Id, bus.Name, bus.RegistrationNumber, bus.Capacity, trip.Origin, trip.Destination,
        trip.DepartureUtc, trip.ArrivalUtc, trip.Price, bus.Capacity, trip.Status, 0));
}).RequireAuthorization("AdminOnly");
trips.MapPost("/{id:int}/cancel", async (int id, AppDbContext db) =>
{
    var trip = await db.Trips.SingleOrDefaultAsync(x => x.Id == id);
    if (trip is null) return Results.NotFound(new { message = "Trip not found." });
    if (trip.Status == "Cancelled")
        return Results.Conflict(new { message = "Trip is already cancelled." });
    if (trip.DepartureUtc <= DateTime.UtcNow)
        return Results.Conflict(new { message = "Only upcoming trips can be cancelled." });

    await using var transaction = await db.Database.BeginTransactionAsync();
    trip.Status = "Cancelled";
    await db.Bookings
        .Where(x => x.TripId == id && x.Status == "Confirmed")
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, "Cancelled"));
    await db.SaveChangesAsync();
    await transaction.CommitAsync();
    return Results.Ok(new { message = "Trip cancelled; confirmed bookings are marked cancelled.", tripId = id });
}).RequireAuthorization("AdminOnly");
trips.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
{
    var trip = await db.Trips.SingleOrDefaultAsync(x => x.Id == id);
    if (trip is null) return Results.NotFound(new { message = "Trip not found." });
    if (trip.Status != "Cancelled")
        return Results.Conflict(new { message = "Cancel the trip before deleting it." });
    if (await db.Bookings.AnyAsync(x => x.TripId == id))
        return Results.Conflict(new { message = "This cancelled trip has booking history and is archived instead of being deleted." });
    db.Trips.Remove(trip);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization("AdminOnly");

var bookings = app.MapGroup("/api/bookings").RequireAuthorization();
bookings.MapGet("/", async (ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = GetUserId(principal);
    var isAdmin = principal.IsInRole("Admin");
    var query = db.Bookings.Include(x => x.User).Include(x => x.Trip).ThenInclude(x => x!.Bus).AsQueryable();
    if (!isAdmin) query = query.Where(x => x.UserId == userId);
    var results = await query.OrderByDescending(x => x.BookedAtUtc).ToListAsync();
    return Results.Ok(results.Select(ToBookingResponse));
});
bookings.MapPost("/", async (CreateBookingRequest request, ClaimsPrincipal principal,
    AppDbContext db) =>
{
    if (request.SeatNumber < 1)
        return Results.BadRequest(new { message = "Choose a valid seat before booking." });
    if (!IsValidPassengerDetails(request.PassengerName, request.PassengerAge,
            request.PassengerGender, request.PassengerMobileNumber))
        return Results.BadRequest(new { message = "Enter a valid passenger name, age, gender, and mobile number." });
    if (!IsValidCircleName(request.AdhiparasakthiCircleName))
        return Results.BadRequest(new { message = "Adhiparasakthi Worship Mandram / Circle Name must be 150 characters or fewer." });
    var trip = await db.Trips.Include(x => x.Bus).SingleOrDefaultAsync(x => x.Id == request.TripId);
    if (trip?.Bus is null) return Results.NotFound(new { message = "Trip not found." });
    if (trip.Status != "Scheduled")
        return Results.Conflict(new { message = "Bookings are not available for a cancelled trip." });
    if (trip.DepartureUtc <= DateTime.UtcNow)
        return Results.BadRequest(new { message = "This trip has already departed." });
    if (request.SeatNumber > trip.Bus.Capacity)
        return Results.BadRequest(new { message = "The selected seat does not exist on this bus." });
    var userId = GetUserId(principal);
    if (principal.IsInRole("Admin") && request.UserId.HasValue)
        userId = request.UserId.Value;
    if (!await db.Users.AnyAsync(x => x.Id == userId))
        return Results.NotFound(new { message = "Booking user not found." });

    await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
    var seatAlreadyBooked = await db.Bookings.AnyAsync(x =>
        x.TripId == trip.Id && x.SeatNumber == request.SeatNumber && x.Status == "Confirmed");
    if (seatAlreadyBooked) return Results.Conflict(new { message = "That seat has already been booked. Please choose another seat." });
    var booking = new Booking
    {
        UserId = userId,
        TripId = trip.Id,
        SeatNumber = request.SeatNumber,
        PassengerName = request.PassengerName!.Trim(),
        PassengerAge = request.PassengerAge!.Value,
        PassengerGender = NormalizeGender(request.PassengerGender!),
        PassengerMobileNumber = request.PassengerMobileNumber!.Trim(),
        AdhiparasakthiCircleName = request.AdhiparasakthiCircleName?.Trim() ?? string.Empty
    };
    db.Bookings.Add(booking);
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateException) { return Results.Conflict(new { message = "The seat was just booked. Please try again." }); }
    await transaction.CommitAsync();
    await db.Entry(booking).Reference(x => x.User).LoadAsync();
    booking.Trip = trip;
    return Results.Created($"/api/bookings/{booking.Id}", ToBookingResponse(booking));
});
bookings.MapPost("/group", async (CreateGroupBookingRequest request, ClaimsPrincipal principal,
    AppDbContext db) =>
{
    if (request.SeatNumbers is null || request.SeatNumbers.Length is < 1 or > 6
        || request.SeatNumbers.Any(seat => seat < 1)
        || request.SeatNumbers.Distinct().Count() != request.SeatNumbers.Length
        || request.Passengers is null || request.Passengers.Length != request.SeatNumbers.Length
        || request.Passengers.Select(x => x.SeatNumber).Distinct().Count() != request.Passengers.Length
        || !request.Passengers.Select(x => x.SeatNumber).ToHashSet().SetEquals(request.SeatNumbers)
        || request.Passengers.Any(passenger => !IsValidPassengerDetails(
            passenger.Name, passenger.Age, passenger.Gender, passenger.MobileNumber)))
        return Results.BadRequest(new { message = "Select between 1 and 6 different valid seats." });
    if (request.Passengers.Any(passenger => !IsValidCircleName(passenger.AdhiparasakthiCircleName)))
        return Results.BadRequest(new { message = "Adhiparasakthi Worship Mandram / Circle Name must be 150 characters or fewer." });

    var trip = await db.Trips.Include(x => x.Bus).SingleOrDefaultAsync(x => x.Id == request.TripId);
    if (trip?.Bus is null) return Results.NotFound(new { message = "Trip not found." });
    if (trip.Status != "Scheduled")
        return Results.Conflict(new { message = "Bookings are not available for a cancelled trip." });
    if (trip.DepartureUtc <= DateTime.UtcNow)
        return Results.BadRequest(new { message = "This trip has already departed." });
    if (request.SeatNumbers.Any(seat => seat > trip.Bus.Capacity))
        return Results.BadRequest(new { message = "One or more selected seats do not exist on this bus." });

    var userId = GetUserId(principal);
    if (principal.IsInRole("Admin") && request.UserId.HasValue)
        userId = request.UserId.Value;
    if (!await db.Users.AnyAsync(x => x.Id == userId))
        return Results.NotFound(new { message = "Booking user not found." });

    await using var transaction = await db.Database.BeginTransactionAsync(
        System.Data.IsolationLevel.Serializable);
    var bookedSeats = await db.Bookings
        .Where(x => x.TripId == trip.Id && x.Status == "Confirmed"
            && request.SeatNumbers.Contains(x.SeatNumber))
        .Select(x => x.SeatNumber)
        .ToListAsync();
    if (bookedSeats.Count > 0)
        return Results.Conflict(new
        {
            message = $"Seat(s) {string.Join(", ", bookedSeats)} were just booked. Refresh availability and choose again.",
            bookedSeats
        });

    var passengersBySeat = request.Passengers.ToDictionary(passenger => passenger.SeatNumber);
    var createdBookings = request.SeatNumbers.Select(seatNumber =>
    {
        var passenger = passengersBySeat[seatNumber];
        return new Booking
        {
            UserId = userId,
            TripId = trip.Id,
            SeatNumber = seatNumber,
            PassengerName = passenger.Name!.Trim(),
            PassengerAge = passenger.Age!.Value,
            PassengerGender = NormalizeGender(passenger.Gender!),
            PassengerMobileNumber = passenger.MobileNumber!.Trim(),
            AdhiparasakthiCircleName = passenger.AdhiparasakthiCircleName?.Trim() ?? string.Empty
        };
    }).ToList();
    db.Bookings.AddRange(createdBookings);
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateException)
    {
        return Results.Conflict(new { message = "One or more selected seats were just booked. No seats were reserved; refresh availability and choose again." });
    }

    await transaction.CommitAsync();
    foreach (var booking in createdBookings)
    {
        booking.User = await db.Users.SingleAsync(x => x.Id == userId);
        booking.Trip = trip;
    }
    return Results.Created("/api/bookings", createdBookings.Select(ToBookingResponse));
});
bookings.MapGet("/{id:int}", async (int id, ClaimsPrincipal principal, AppDbContext db) =>
{
    var item = await db.Bookings.Include(x => x.User).Include(x => x.Trip).ThenInclude(x => x!.Bus)
        .SingleOrDefaultAsync(x => x.Id == id);
    if (item is null) return Results.NotFound();
    if (!principal.IsInRole("Admin") && item.UserId != GetUserId(principal))
        return Results.Forbid();
    return Results.Ok(ToBookingResponse(item));
});
bookings.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
{
    var item = await db.Bookings.SingleOrDefaultAsync(x => x.Id == id);
    if (item is null) return Results.NotFound();
    if (item.Status != "Confirmed") return Results.Conflict(new { message = "Booking is already cancelled." });
    item.Status = "Cancelled";
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.Run();

static string? NormalizeEmail(string? email)
{
    var normalized = email?.Trim().ToLowerInvariant();
    return normalized is not null && System.Net.Mail.MailAddress.TryCreate(normalized, out _) ? normalized : null;
}

static int GetUserId(ClaimsPrincipal principal) =>
    int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Authenticated token is missing its user identifier."));

static string HashToken(string token) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

static string CreateJwt(AppUser user, IConfiguration config)
{
    var claims = new[]
    {
        new System.Security.Claims.Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new System.Security.Claims.Claim(ClaimTypes.Name, user.Name),
        new System.Security.Claims.Claim(ClaimTypes.Email, user.Email),
        new System.Security.Claims.Claim(ClaimTypes.Role, user.Role)
    };
    var key = new SymmetricSecurityKey(Convert.FromBase64String(config["Jwt:SecretKey"]!));
    var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(config["Jwt:Issuer"], config["Jwt:Audience"], claims,
        expires: DateTime.UtcNow.AddMinutes(config.GetValue<int>("Jwt:ExpiryMinutes")),
        signingCredentials: credentials);
    return new JwtSecurityTokenHandler().WriteToken(token);
}

static DateTime ToUtc(DateTime value) => value.Kind switch
{
    DateTimeKind.Utc => value,
    DateTimeKind.Local => value.ToUniversalTime(),
    _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
};

static bool IsValidPassengerDetails(string? name, int? age, string? gender, string? mobileNumber)
{
    if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100
        || age is < 1 or > 120 || string.IsNullOrWhiteSpace(gender)
        || string.IsNullOrWhiteSpace(mobileNumber) || mobileNumber.Trim().Length > 20)
        return false;
    if (!Regex.IsMatch(name.Trim(), @"^[\p{L}\p{M}][\p{L}\p{M} .'\u2019-]*$")
        || !Regex.IsMatch(mobileNumber.Trim(), @"^[0-9() -]+$"))
        return false;
    if (gender is not ("Male" or "Female" or "Other")
        && !new[] { "Male", "Female", "Other" }.Contains(gender, StringComparer.OrdinalIgnoreCase))
        return false;
    var digits = new string(mobileNumber.Where(char.IsDigit).ToArray());
    return digits.Length == 10 && digits[0] is >= '6' and <= '9';
}

static bool IsValidCircleName(string? circleName) =>
    circleName is null || circleName.Trim().Length <= 150;

static string NormalizeGender(string gender) => gender.ToLowerInvariant() switch
{
    "male" => "Male",
    "female" => "Female",
    _ => "Other"
};

static BookingResponse ToBookingResponse(Booking booking) => new(
    booking.Id, booking.UserId, booking.User!.Name, booking.User.Email, booking.TripId,
    booking.Trip!.BusId, booking.Trip.Origin, booking.Trip.Destination, booking.Trip.DepartureUtc,
    booking.Trip.ArrivalUtc, booking.Trip.Bus!.Name, booking.Trip.Bus.RegistrationNumber,
    booking.Trip.Bus.Capacity, booking.SeatNumber, booking.PassengerName, booking.PassengerAge,
    booking.PassengerGender, booking.PassengerMobileNumber, booking.AdhiparasakthiCircleName,
    booking.Trip.Price, booking.Status, booking.BookedAtUtc);

static async Task EnsureBookingColumnsAsync(AppDbContext db)
{
    var columns = new (string Name, string Sql)[]
    {
        ("PassengerName", "ALTER TABLE \"Bookings\" ADD COLUMN \"PassengerName\" TEXT NOT NULL DEFAULT ''"),
        ("PassengerAge", "ALTER TABLE \"Bookings\" ADD COLUMN \"PassengerAge\" INTEGER NOT NULL DEFAULT 0"),
        ("PassengerGender", "ALTER TABLE \"Bookings\" ADD COLUMN \"PassengerGender\" TEXT NOT NULL DEFAULT ''"),
        ("PassengerMobileNumber", "ALTER TABLE \"Bookings\" ADD COLUMN \"PassengerMobileNumber\" TEXT NOT NULL DEFAULT ''"),
        ("AdhiparasakthiCircleName", "ALTER TABLE \"Bookings\" ADD COLUMN \"AdhiparasakthiCircleName\" TEXT NOT NULL DEFAULT ''")
    };
    await db.Database.OpenConnectionAsync();
    try
    {
        var existingColumns = new HashSet<string>(StringComparer.Ordinal);
        await using (var check = db.Database.GetDbConnection().CreateCommand())
        {
            check.CommandText = "PRAGMA table_info('Bookings')";
            await using var reader = await check.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                existingColumns.Add(reader.GetString(1));
        }

        foreach (var column in columns.Where(column => !existingColumns.Contains(column.Name)))
            await db.Database.ExecuteSqlRawAsync(column.Sql);
    }
    finally
    {
        await db.Database.CloseConnectionAsync();
    }
}

static async Task EnsureTripStatusColumnAsync(AppDbContext db)
{
    await db.Database.OpenConnectionAsync();
    try
    {
        await using var check = db.Database.GetDbConnection().CreateCommand();
        check.CommandText = "PRAGMA table_info('Trips')";
        await using var reader = await check.ExecuteReaderAsync();
        var hasStatus = false;
        while (await reader.ReadAsync())
        {
            if (reader.GetString(1) == "Status")
            {
                hasStatus = true;
                break;
            }
        }

        await reader.DisposeAsync();
        if (!hasStatus)
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Trips\" ADD COLUMN \"Status\" TEXT NOT NULL DEFAULT 'Scheduled'");
    }
    finally
    {
        await db.Database.CloseConnectionAsync();
    }
}

static async Task EnsureActiveSeatUniqueIndexAsync(AppDbContext db)
{
    var indexSql = await db.Database.SqlQueryRaw<string?>(
        "SELECT sql AS Value FROM sqlite_master WHERE type = 'index' AND name = 'IX_Bookings_TripId_SeatNumber'")
        .SingleOrDefaultAsync();
    if (indexSql?.Contains("WHERE", StringComparison.OrdinalIgnoreCase) == true)
        return;

    await db.Database.ExecuteSqlRawAsync(
        "DROP INDEX IF EXISTS \"IX_Bookings_TripId_SeatNumber\"");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE UNIQUE INDEX \"IX_Bookings_TripId_SeatNumber\" ON \"Bookings\" (\"TripId\", \"SeatNumber\") WHERE \"Status\" = 'Confirmed'");
}

public sealed record RegisterRequest(string Name, string Email, string Password);
public sealed record LoginRequest(string Email, string Password);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string NewPassword);
public sealed record AuthResponse(string Token, int UserId, string Name, string Email, string Role);
public sealed record BusRequest(string Name, string RegistrationNumber, int Capacity);
public sealed record TripRequest(int BusId, string Origin, string Destination, DateTime DepartureUtc, DateTime ArrivalUtc, decimal Price);
public sealed record CreateBookingRequest(int TripId, int SeatNumber, int? UserId,
    string? PassengerName, int? PassengerAge, string? PassengerGender, string? PassengerMobileNumber,
    string? AdhiparasakthiCircleName);
public sealed record PassengerDetailsRequest(int SeatNumber, string? Name, int? Age,
    string? Gender, string? MobileNumber, string? AdhiparasakthiCircleName);
public sealed record CreateGroupBookingRequest(int TripId, int[]? SeatNumbers,
    PassengerDetailsRequest[]? Passengers, int? UserId);
public sealed record SeatResponse(int SeatNumber, string Status);
public sealed record TripResponse(int Id, int BusId, string BusName, string BusRegistrationNumber, int Capacity, string Origin,
    string Destination, DateTime DepartureUtc, DateTime ArrivalUtc, decimal Price, int AvailableSeats,
    string Status, int BookingCount);
public sealed record BookingResponse(int Id, int UserId, string UserName, string UserEmail, int TripId,
    int BusId, string Origin, string Destination, DateTime DepartureUtc, DateTime ArrivalUtc,
    string BusName, string BusRegistrationNumber, int BusCapacity, int SeatNumber,
    string PassengerName, int PassengerAge, string PassengerGender, string PassengerMobileNumber,
    string AdhiparasakthiCircleName, decimal Price, string Status, DateTime BookedAtUtc);
