import { Inject, Injectable, InjectionToken } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AppUser, AuthResponse, Booking, Bus, PassengerDetails, Seat, Trip } from './models';

export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL');

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly baseUrl: string;

  constructor(
    private readonly http: HttpClient,
    @Inject(API_BASE_URL) baseUrl: string
  ) {
    this.baseUrl = baseUrl.replace(/\/+$/, '');
  }

  register(data: { name: string; email: string; password: string }): Observable<object> {
    return this.http.post(`${this.baseUrl}/auth/register`, data);
  }
  login(data: { email: string; password: string }): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.baseUrl}/auth/login`, data);
  }
  forgotPassword(email: string): Observable<{ message: string; resetToken?: string }> {
    return this.http.post<{ message: string; resetToken?: string }>(
      `${this.baseUrl}/auth/forgot-password`, { email });
  }
  resetPassword(token: string, newPassword: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(
      `${this.baseUrl}/auth/reset-password`, { token, newPassword });
  }
  trips(): Observable<Trip[]> {
    return this.http.get<Trip[]>(`${this.baseUrl}/trips`);
  }
  managedTrips(): Observable<Trip[]> {
    return this.http.get<Trip[]>(`${this.baseUrl}/trips/manage`);
  }
  seats(tripId: number): Observable<Seat[]> {
    return this.http.get<Seat[]>(`${this.baseUrl}/trips/${tripId}/seats`);
  }
  buses(): Observable<Bus[]> {
    return this.http.get<Bus[]>(`${this.baseUrl}/buses`);
  }
  addBus(data: { name: string; registrationNumber: string; capacity: number }): Observable<Bus> {
    return this.http.post<Bus>(`${this.baseUrl}/buses`, data);
  }
  addTrip(data: { busId: number; origin: string; destination: string;
    departureUtc: string; arrivalUtc: string; price: number }): Observable<Trip> {
    return this.http.post<Trip>(`${this.baseUrl}/trips`, data);
  }
  deleteTrip(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/trips/${id}`);
  }
  cancelTrip(id: number): Observable<{ message: string; tripId: number }> {
    return this.http.post<{ message: string; tripId: number }>(`${this.baseUrl}/trips/${id}/cancel`, {});
  }
  users(): Observable<AppUser[]> {
    return this.http.get<AppUser[]>(`${this.baseUrl}/users`);
  }
  bookings(): Observable<Booking[]> {
    return this.http.get<Booking[]>(`${this.baseUrl}/bookings`);
  }
  book(tripId: number, seatNumber: number, passenger: PassengerDetails, userId?: number): Observable<Booking> {
    return this.http.post<Booking>(`${this.baseUrl}/bookings`, {
      tripId, seatNumber, userId, passengerName: passenger.name, passengerAge: passenger.age,
      passengerGender: passenger.gender, passengerMobileNumber: passenger.mobileNumber,
      adhiparasakthiCircleName: passenger.adhiparasakthiCircleName
    });
  }
  bookSeats(tripId: number, passengers: { seatNumber: number; details: PassengerDetails }[],
    userId?: number): Observable<Booking[]> {
    return this.http.post<Booking[]>(`${this.baseUrl}/bookings/group`, {
      tripId,
      seatNumbers: passengers.map(passenger => passenger.seatNumber),
      passengers: passengers.map(passenger => ({
        seatNumber: passenger.seatNumber,
        name: passenger.details.name,
        age: passenger.details.age,
        gender: passenger.details.gender,
        mobileNumber: passenger.details.mobileNumber,
        adhiparasakthiCircleName: passenger.details.adhiparasakthiCircleName
      })),
      userId
    });
  }
  cancelBooking(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/bookings/${id}`);
  }
}
