import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiService } from './api.service';
import { AppUser, AuthResponse, Booking, Bus, PassengerDetails, Seat, Trip } from './models';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.component.html'
})
export class AppComponent implements OnInit {
  user: AuthResponse | null = this.loadUser();
  trips: Trip[] = [];
  managedTrips: Trip[] = [];
  buses: Bus[] = [];
  bookings: Booking[] = [];
  users: AppUser[] = [];
  seatRows: (Seat | null)[][] = [];
  reportSeatRows: (Seat | null)[][] = [];
  reportBookings: Booking[] = [];
  reportBookingsBySeat: Record<number, Booking> = {};
  reportGeneratedAt = new Date();
  selectedSeats: Record<number, number[]> = {};
  passengerDetails: Record<number, Record<number, PassengerDetails>> = {};
  bookingPage = 1;
  readonly bookingPageSize = 10;
  reportBookingPage = 1;
  reportTripId: number | null = null;
  seatPickerTripId: number | null = null;
  loadingSeatsTripId: number | null = null;
  view: 'login' | 'register' | 'forgot' | 'reset' = 'login';
  message = '';
  error = '';
  resetToken = '';
  selectedUserId = 0;
  busy = false;
  loginForm = { email: '', password: '' };
  registerForm = { name: '', email: '', password: '' };
  forgotEmail = '';
  newPassword = '';
  busForm = { name: '', registrationNumber: '', capacity: 50 };
  tripForm = { busId: 0, origin: '', destination: '', departureUtc: '', arrivalUtc: '', price: 25 };

  constructor(private readonly api: ApiService) {}

  ngOnInit(): void {
    if (this.user) this.loadDashboard();
  }

  get isAdmin(): boolean { return this.user?.role === 'Admin'; }
  get activeTrip(): Trip | undefined {
    return this.trips.find(trip => trip.id === this.seatPickerTripId);
  }
  get reportTrip(): Trip | undefined {
    return this.managedTrips.find(trip => trip.id === this.reportTripId);
  }
  get displayedBookings(): Booking[] {
    const start = (this.bookingPage - 1) * this.bookingPageSize;
    return this.bookings.slice(start, start + this.bookingPageSize);
  }
  get bookingPageCount(): number {
    return Math.max(1, Math.ceil(this.bookings.length / this.bookingPageSize));
  }
  get bookingPageStart(): number {
    return this.bookings.length ? (this.bookingPage - 1) * this.bookingPageSize + 1 : 0;
  }
  get bookingPageEnd(): number {
    return Math.min(this.bookingPage * this.bookingPageSize, this.bookings.length);
  }
  changeBookingPage(page: number): void {
    if (Number.isInteger(page) && page >= 1 && page <= this.bookingPageCount) {
      this.bookingPage = page;
    }
  }
  get displayedReportBookings(): Booking[] {
    const start = (this.reportBookingPage - 1) * this.bookingPageSize;
    return this.reportBookings.slice(start, start + this.bookingPageSize);
  }
  get reportBookingPageCount(): number {
    return Math.max(1, Math.ceil(this.reportBookings.length / this.bookingPageSize));
  }
  get reportBookingPageStart(): number {
    return this.reportBookings.length ? (this.reportBookingPage - 1) * this.bookingPageSize + 1 : 0;
  }
  get reportBookingPageEnd(): number {
    return Math.min(this.reportBookingPage * this.bookingPageSize, this.reportBookings.length);
  }
  changeReportBookingPage(page: number): void {
    if (Number.isInteger(page) && page >= 1 && page <= this.reportBookingPageCount) {
      this.reportBookingPage = page;
    }
  }

  login(): void {
    this.run(this.api.login(this.loginForm), response => {
      this.user = response;
      localStorage.setItem('busbooking.token', response.token);
      localStorage.setItem('busbooking.user', JSON.stringify(response));
      this.message = `Welcome back, ${response.name}.`;
      this.loadDashboard();
    });
  }

  register(): void {
    this.run(this.api.register(this.registerForm), () => {
      this.view = 'login';
      this.loginForm.email = this.registerForm.email;
      this.message = 'Account created. Sign in to start booking.';
    });
  }

  forgot(): void {
    this.run(this.api.forgotPassword(this.forgotEmail), response => {
      this.message = response.message;
      if (response.resetToken) {
        this.resetToken = response.resetToken;
        this.view = 'reset';
      }
    });
  }

  reset(): void {
    this.run(this.api.resetPassword(this.resetToken, this.newPassword), response => {
      this.view = 'login';
      this.message = response.message;
      this.newPassword = '';
    });
  }

  logout(): void {
    localStorage.removeItem('busbooking.token');
    localStorage.removeItem('busbooking.user');
    this.user = null;
    this.view = 'login';
    this.trips = [];
    this.bookings = [];
    this.message = 'You have been signed out.';
  }

  book(trip: Trip): void {
    const seatNumbers = this.selectedSeats[trip.id] ?? [];
    if (seatNumbers.length < 1 || seatNumbers.length > 6) {
      this.error = 'Select between 1 and 6 seats before confirming your booking.';
      return;
    }
    if (!this.hasCompletePassengerDetails(trip.id)) {
      this.error = 'Enter a valid name, age, gender, and mobile number for every selected seat.';
      return;
    }
    const userId = this.isAdmin && this.selectedUserId ? this.selectedUserId : undefined;
    const passengers = seatNumbers.map(seatNumber => ({
      seatNumber,
      details: this.passengerDetails[trip.id][seatNumber]
    }));
    this.run(this.api.bookSeats(trip.id, passengers, userId), bookings => {
      const reservedSeats = bookings.map(booking => booking.seatNumber).sort((a, b) => a - b);
      this.message = `Seat${reservedSeats.length > 1 ? 's' : ''} ${reservedSeats.join(', ')} reserved on ${trip.origin} → ${trip.destination}.`;
      this.selectedSeats[trip.id] = [];
      this.passengerDetails[trip.id] = {};
      this.seatPickerTripId = null;
      this.seatRows = [];
      this.loadDashboard();
    });
  }

  toggleSeatPicker(trip: Trip): void {
    if (this.seatPickerTripId === trip.id) {
      this.seatPickerTripId = null;
      return;
    }
    this.seatPickerTripId = trip.id;
    this.selectedSeats[trip.id] = [];
    this.passengerDetails[trip.id] = {};
    this.error = '';
    this.seatRows = [];
    this.loadingSeatsTripId = trip.id;
    this.api.seats(trip.id).subscribe({
      next: seats => {
        this.seatRows = this.createSeatRows(trip.capacity, seats);
        this.loadingSeatsTripId = null;
      },
      error: error => {
        this.loadingSeatsTripId = null;
        this.handleError(error);
      }
    });
  }

  backToTrips(): void {
    this.seatPickerTripId = null;
    this.error = '';
    this.seatRows = [];
  }

  selectReportTrip(tripId: number | null): void {
    this.reportTripId = tripId;
    this.reportBookingPage = 1;
    this.updateAdminReport();
  }

  reportBookingForSeat(seatNumber: number): Booking | undefined {
    return this.reportBookingsBySeat[seatNumber];
  }

  reportSeatAriaLabel(seatNumber: number): string {
    const booking = this.reportBookingForSeat(seatNumber);
    return booking
      ? `Seat ${seatNumber}, booked by ${booking.passengerName}, age ${booking.passengerAge}, mobile ${booking.passengerMobileNumber}`
      : `Seat ${seatNumber}, available`;
  }

  printTripReport(): void {
    if (this.isAdmin && this.reportTrip) {
      this.reportGeneratedAt = new Date();
      window.print();
    }
  }

  selectSeat(tripId: number, seat: Seat): void {
    if (seat.status === 'Booked') return;
    const selected = this.selectedSeats[tripId] ?? [];
    if (selected.includes(seat.seatNumber)) {
      this.selectedSeats[tripId] = selected.filter(seatNumber => seatNumber !== seat.seatNumber);
      delete this.passengerDetails[tripId]?.[seat.seatNumber];
      return;
    }
    if (selected.length >= 6) {
      this.error = 'You can select up to 6 seats per trip.';
      return;
    }
    this.error = '';
    this.selectedSeats[tripId] = [...selected, seat.seatNumber].sort((a, b) => a - b);
    this.passengerDetails[tripId] ??= {};
    this.passengerDetails[tripId][seat.seatNumber] = {
      name: '',
      age: null,
      gender: '',
      mobileNumber: '',
      adhiparasakthiCircleName: ''
    };
  }

  selectedSeatNumbers(tripId: number): number[] {
    return this.selectedSeats[tripId] ?? [];
  }

  passengerForSeat(tripId: number, seatNumber: number): PassengerDetails {
    return this.passengerDetails[tripId]?.[seatNumber]
      ?? { name: '', age: null, gender: '', mobileNumber: '', adhiparasakthiCircleName: '' };
  }

  hasCompletePassengerDetails(tripId: number): boolean {
    return this.selectedSeatNumbers(tripId).every(seatNumber => {
      const passenger = this.passengerDetails[tripId]?.[seatNumber];
      const mobileDigits = passenger?.mobileNumber.replace(/\D/g, '') ?? '';
      return !!passenger?.name.trim() && passenger.name.trim().length <= 100
        && passenger.age !== null && Number.isInteger(passenger.age) && passenger.age >= 1 && passenger.age <= 120
        && ['Male', 'Female', 'Other'].includes(passenger.gender)
        && /^[0-9+() -]{7,20}$/.test(passenger.mobileNumber)
        && mobileDigits.length >= 7 && mobileDigits.length <= 15;
    });
  }

  isSeatSelected(tripId: number, seatNumber: number): boolean {
    return (this.selectedSeats[tripId] ?? []).includes(seatNumber);
  }

  selectedSeatLabel(tripId: number): string {
    const selected = this.selectedSeats[tripId] ?? [];
    return selected.length ? `Seat${selected.length > 1 ? 's' : ''} ${selected.join(', ')}` : 'Choose 1–6 seats';
  }

  selectedSeatCount(tripId: number): number {
    return this.selectedSeats[tripId]?.length ?? 0;
  }

  cancel(booking: Booking): void {
    if (!this.isAdmin) {
      this.message = '';
      this.error = 'Only administrators can cancel bookings.';
      return;
    }
    this.run(this.api.cancelBooking(booking.id), () => {
      this.message = `Booking #${booking.id} cancelled.`;
      this.loadDashboard();
    });
  }

  addBus(): void {
    this.run(this.api.addBus(this.busForm), () => {
      this.message = 'Bus added to your fleet.';
      this.busForm = { name: '', registrationNumber: '', capacity: 50 };
      this.loadBuses();
    });
  }

  addTrip(): void {
    const departure = this.toUtcIso(this.tripForm.departureUtc);
    const arrival = this.toUtcIso(this.tripForm.arrivalUtc);
    if (!departure || !arrival) {
      this.error = 'Select both a departure and arrival date and time.';
      return;
    }
    if (new Date(departure) <= new Date() || new Date(arrival) <= new Date(departure)) {
      this.error = 'Departure must be in the future and arrival must be after departure.';
      return;
    }

    this.run(this.api.addTrip({
      ...this.tripForm,
      departureUtc: departure,
      arrivalUtc: arrival
    }), () => {
      this.message = 'Trip published and ready for bookings.';
      this.loadTrips();
      this.loadManagedTrips();
    });
  }

  updateTripDateTime(field: 'departureUtc' | 'arrivalUtc', event: Event): void {
    if (event.target instanceof HTMLInputElement) {
      this.tripForm[field] = event.target.value;
    }
  }

  cancelTrip(trip: Trip): void {
    if (!this.isAdmin || trip.status !== 'Scheduled'
      || !window.confirm(`Cancel ${trip.origin} → ${trip.destination}? Confirmed bookings will also be marked cancelled.`)) return;
    this.run(this.api.cancelTrip(trip.id), response => {
      this.message = response.message;
      this.loadDashboard();
    });
  }

  deleteTrip(trip: Trip): void {
    if (!this.isAdmin || trip.status !== 'Cancelled' || trip.bookingCount > 0) return;
    if (!window.confirm(`Permanently delete the cancelled trip ${trip.origin} → ${trip.destination}?`)) return;
    this.run(this.api.deleteTrip(trip.id), () => {
      this.message = `Cancelled trip ${trip.origin} → ${trip.destination} deleted.`;
      this.loadDashboard();
    });
  }

  private loadDashboard(): void {
    this.loadTrips();
    this.api.bookings().subscribe({
      next: rows => {
        this.bookings = rows;
        this.bookingPage = Math.min(this.bookingPage, this.bookingPageCount);
        if (this.isAdmin) this.updateAdminReport();
      },
      error: error => this.handleError(error)
    });
    if (this.isAdmin) {
      this.loadBuses();
      this.loadManagedTrips();
      this.api.users().subscribe({
        next: rows => this.users = rows,
        error: error => this.handleError(error)
      });
    }
  }

  private loadManagedTrips(): void {
    this.api.managedTrips().subscribe({
      next: rows => {
        this.managedTrips = rows;
        if (!rows.some(trip => trip.id === this.reportTripId)) {
          this.reportTripId = rows[0]?.id ?? null;
        }
        this.updateAdminReport();
      },
      error: error => this.handleError(error)
    });
  }

  private updateAdminReport(): void {
    const trip = this.reportTrip;
    if (!this.isAdmin || !trip) {
      this.reportBookings = [];
      this.reportBookingPage = 1;
      this.reportBookingsBySeat = {};
      this.reportSeatRows = [];
      return;
    }

    this.reportBookings = this.bookings
      .filter(booking => booking.tripId === trip.id)
      .sort((a, b) => a.seatNumber - b.seatNumber
        || new Date(b.bookedAtUtc).getTime() - new Date(a.bookedAtUtc).getTime());
    this.reportBookingPage = Math.min(this.reportBookingPage, this.reportBookingPageCount);
    this.reportBookingsBySeat = {};
    for (const booking of [...this.reportBookings].sort((a, b) =>
      Number(b.status === 'Confirmed') - Number(a.status === 'Confirmed'))) {
      this.reportBookingsBySeat[booking.seatNumber] ??= booking;
    }
    this.reportSeatRows = this.createSeatRows(trip.capacity,
      Object.keys(this.reportBookingsBySeat).map(seatNumber => ({
        seatNumber: Number(seatNumber),
        status: 'Booked'
      })));
  }

  private loadTrips(): void {
    this.api.trips().subscribe({
      next: rows => {
        this.trips = rows;
        if (this.seatPickerTripId !== null) {
          this.api.seats(this.seatPickerTripId).subscribe({
            next: seats => {
              const trip = this.activeTrip;
              if (trip) this.seatRows = this.createSeatRows(trip.capacity, seats);
            },
            error: error => this.handleError(error)
          });
        }
      },
      error: error => this.handleError(error)
    });
  }

  private createSeatRows(capacity: number, seats: Seat[]): (Seat | null)[][] {
    const seatsByNumber = new Map(seats.map(seat => [seat.seatNumber, seat]));
    const layout: (number | null)[][] = [
      [50, null, null, 3, 2, 1],
      [5, 4, null, 8, 9, 10],
      [6, 7, null, 13, 12, 11],
      [15, 14, null, 18, 19, 20],
      [16, 17, null, 23, 22, 21],
      [25, 24, null, 28, 29, 30],
      [26, 27, null, 33, 32, 31],
      [35, 34, null, 38, 39, 40],
      [36, 37, null, 43, 42, 41],
      [44, 45, 46, 47, 48, 49]
    ];
    const rows = layout.map(row => row.map(seatNumber =>
      seatNumber !== null && seatNumber <= capacity ? seatsByNumber.get(seatNumber) ?? null : null));

    for (let seatNumber = 51; seatNumber <= capacity; seatNumber++) {
      const offset = seatNumber - 51;
      const rowIndex = layout.length + Math.floor(offset / 5);
      const column = offset % 5 < 2 ? offset % 5 : offset % 5 + 1;
      if (!rows[rowIndex]) rows[rowIndex] = Array<Seat | null>(6).fill(null);
      rows[rowIndex][column] = seatsByNumber.get(seatNumber) ?? null;
    }

    return rows.filter(row => row.some(seat => seat !== null));
  }

  private toUtcIso(localDateTime: string): string | null {
    if (!localDateTime) return null;
    const value = new Date(localDateTime);
    return Number.isNaN(value.getTime()) ? null : value.toISOString();
  }

  private loadBuses(): void {
    this.api.buses().subscribe({
      next: rows => {
        this.buses = rows;
        if (!this.tripForm.busId && rows.length) this.tripForm.busId = rows[0].id;
      },
      error: error => this.handleError(error)
    });
  }

  private run<T>(request: import('rxjs').Observable<T>, onSuccess: (value: T) => void): void {
    this.error = '';
    this.message = '';
    this.busy = true;
    request.subscribe({
      next: value => { this.busy = false; onSuccess(value); },
      error: error => { this.busy = false; this.handleError(error); }
    });
  }

  private handleError(error: unknown): void {
    const response = error as HttpErrorResponse;
    this.error = response.error?.message
      ?? (response.status === 401 ? 'Email or password is incorrect.'
        : response.status === 403 ? 'You are not allowed to perform this action.'
          : 'Something went wrong. Please try again.');
  }

  private loadUser(): AuthResponse | null {
    const raw = localStorage.getItem('busbooking.user');
    if (!raw || !localStorage.getItem('busbooking.token')) return null;
    try { return JSON.parse(raw) as AuthResponse; }
    catch { localStorage.removeItem('busbooking.user'); return null; }
  }
}
