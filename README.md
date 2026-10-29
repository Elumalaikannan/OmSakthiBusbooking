# Wayfarer Bus Booking

A complete bus ticket booking starter with an Angular standalone frontend, ASP.NET Core 10 minimal API, SQLite persistence, JWT authentication, and Admin/User authorization.

## Requirements

- .NET 10 SDK
- Node.js 20.19+ and npm

## Run locally

1. Start the API:

   ```powershell
   cd backend\BusBooking.Api
   dotnet restore
   dotnet run --urls http://localhost:5000
   ```

   The API creates `busbooking.db` on first run and seeds the development administrator when it does not already exist:

   - Email: `admin@busbooking.local`
   - Password: `ChangeMe123!`

2. In another terminal, start Angular:

   ```powershell
   cd frontend
   npm install
   npm start
   ```

3. Visit `http://localhost:4200`. Create a user account or sign in with the development administrator.

The Angular app reads its API URL from `frontend\src\assets\config.json` at startup. Update the deployed `assets/config.json` `baseUrl` value to change the backend URL without rebuilding the frontend; the config is fetched without browser caching. The default local value is `http://localhost:5000/api`. The API allows the local Angular origin `http://localhost:4200`.

## Features

- Register and sign in; passwords are stored as salted ASP.NET Core password hashes.
- JWTs expire after eight hours. API routes use role claims for administrator authorization.
- Forgot password and reset password with single-use, hashed, 30-minute reset tokens. In Development, the forgot-password endpoint returns the token so the flow can be exercised locally. In production, connect a real email delivery provider; the endpoint intentionally does not disclose whether an email address exists.
- Admin: add buses, publish trips, view all bookings, and book tickets for a selected account or themselves.
- Admin: inspect any trip's seat map and passenger manifest from the admin dashboard. Hover over or focus a booked seat to see passenger details; use **Print trip report** for a trip summary and all passenger and booking-account details.
- Admin trip lifecycle: cancel a scheduled trip first; this transaction marks the trip Cancelled and marks its confirmed bookings Cancelled. Cancelled trips with booking history are retained as an archive and cannot be hard-deleted. A cancelled trip with no booking history may then be deleted.
- User: browse trips, enter each selected passenger's name, age, gender, mobile number, and optional Adhiparasakthi Worship Mandram / Circle Name, then atomically book 1-6 seats per trip, view their own bookings, and cancel a booking.
- Booking seat uniqueness is enforced by a SQLite unique index.

## API overview

| Method | Route | Access | Purpose |
|---|---|---|---|
| POST | `/api/auth/register` | Public | Create a User account |
| POST | `/api/auth/login` | Public | Get a JWT |
| POST | `/api/auth/forgot-password` | Public | Request a reset token |
| POST | `/api/auth/reset-password` | Public | Consume a reset token |
| GET | `/api/trips` | Public | Browse trips; optional `origin`, `destination`, `date` query parameters |
| GET | `/api/trips/manage` | Admin | List scheduled and cancelled trips with booking counts |
| GET | `/api/trips/{id}/seats` | Public | List each seat as `Available` or `Booked` |
| POST | `/api/trips/{id}/cancel` | Admin | Cancel a scheduled trip and its confirmed bookings |
| DELETE | `/api/trips/{id}` | Admin | Delete a cancelled trip only when it has no booking history |
| GET/POST | `/api/buses` | Admin | List/add buses |
| POST | `/api/trips` | Admin | Publish a trip |
| GET | `/api/users` | Admin | List accounts for assigning a booking |
| GET/POST | `/api/bookings` | Authenticated | List permitted bookings / book one selected seat with passenger details (`tripId`, `seatNumber`, `passengerName`, `passengerAge`, `passengerGender`, `passengerMobileNumber`, optional `adhiparasakthiCircleName`; Admin may also send `userId`) |
| POST | `/api/bookings/group` | Authenticated | Atomically book 1-6 selected seats with passenger details for each seat (`tripId`, `seatNumbers`, `passengers`; Admin may also send `userId`) |
| GET/DELETE | `/api/bookings/{id}` | Owner or Admin | View/cancel a booking |
| GET | `/api/health` | Public | Health check |

Send `Authorization: Bearer <JWT>` for authenticated routes. Only Admins can add fleet data, publish trips, see all bookings, and assign a booking to another account. Users can only see and cancel their own bookings. The seat picker uses the public availability endpoint. Group booking rechecks all 1-6 selected seats and reserves them in one transaction, or returns a conflict without creating partial bookings if any selected seat was taken.

## SQLite schema

The API creates these tables on first start via EF Core `EnsureCreated`. On startup it also adds the `Trips.Status` and booking passenger-detail columns (including the Adhiparasakthi Circle Name) to existing SQLite databases if they are missing and updates the seat uniqueness index so cancelled bookings release their seats:

- `Users`: id, name, unique normalized email, password hash, role, created timestamp.
- `Buses`: id, name, unique registration number, capacity.
- `Trips`: id, bus foreign key, origin, destination, UTC departure/arrival, decimal fare, lifecycle status (`Scheduled` or `Cancelled`).
- `Bookings`: id, user and trip foreign keys, seat number, passenger name, age, gender, mobile number, optional Adhiparasakthi Worship Mandram / Circle Name, booking timestamp, status. Each booking retains its trip and bus through the foreign-key relationship; ticket responses include passenger details, route and timing plus bus name, registration, and capacity. A filtered unique `(TripId, SeatNumber)` index prevents duplicate active seat assignments while allowing a cancelled seat to be booked again.
- `PasswordResetTokens`: user foreign key, unique SHA-256 token hash, expiry and used timestamp.

Foreign keys protect active references. Cancelled trips with booking history remain stored as an archive. Cancellation atomically updates the trip and its confirmed bookings in SQLite. Notification delivery, refunds, and driver assignment integrations are not configured in this starter and must be connected before claiming those external actions have completed.

## Configuration and production notes

Shared JWT settings are in `backend\BusBooking.Api\appsettings.json`: issuer `ASS_BBB`, audience `APS_Vignan_Nagar_Bengaluru`, and a 30-minute token lifetime. Keep the Base64-encoded signing secret out of source control. For local development, create a 32-byte secret and save it with .NET user-secrets:

   ```powershell
   cd backend\BusBooking.Api
   $bytes = New-Object byte[] 32
   [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
   dotnet user-secrets set "Jwt:SecretKey" ([Convert]::ToBase64String($bytes))
   ```

Production startup requires a strong `Jwt__SecretKey` environment/secret-manager value, Base64-encoded and decoding to at least 32 bytes; it does not seed the local administrator. To seed a production administrator intentionally, set `AdminSeed__Email` and `AdminSeed__Password` securely. Set `Cors__AllowedOrigins__0` (and further indexed entries as needed) to the deployed frontend origin, with no trailing slash (for example, `https://omsakthivignannager02.netlify.app`). Serve over HTTPS, and integrate an email provider before enabling password recovery in production; until configured, that endpoint returns HTTP 503 outside Development rather than claiming to have sent an email.

`EnsureCreated` is suitable for this starter's fresh local database. Use EF Core migrations when evolving an existing deployed schema.
