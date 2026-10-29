export interface AuthResponse {
  token: string;
  userId: number;
  name: string;
  email: string;
  role: 'Admin' | 'User';
}

export interface Bus {
  id: number;
  name: string;
  registrationNumber: string;
  capacity: number;
}

export interface Trip {
  id: number;
  busId: number;
  busName: string;
  busRegistrationNumber: string;
  capacity: number;
  origin: string;
  destination: string;
  departureUtc: string;
  arrivalUtc: string;
  price: number;
  availableSeats: number;
  status: 'Scheduled' | 'Cancelled';
  bookingCount: number;
}

export interface Seat {
  seatNumber: number;
  status: 'Available' | 'Booked';
}

export interface Booking {
  id: number;
  userId: number;
  userName: string;
  userEmail: string;
  tripId: number;
  busId: number;
  origin: string;
  destination: string;
  departureUtc: string;
  arrivalUtc: string;
  busName: string;
  busRegistrationNumber: string;
  busCapacity: number;
  seatNumber: number;
  passengerName: string;
  passengerAge: number;
  passengerGender: string;
  passengerMobileNumber: string;
  adhiparasakthiCircleName: string;
  price: number;
  status: string;
  bookedAtUtc: string;
}

export interface PassengerDetails {
  name: string;
  age: number | null;
  gender: string;
  mobileNumber: string;
  adhiparasakthiCircleName: string;
}

export interface AppUser {
  id: number;
  name: string;
  email: string;
  role: string;
}
