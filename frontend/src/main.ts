import { bootstrapApplication } from '@angular/platform-browser';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { AppComponent } from './app/app.component';
import { API_BASE_URL } from './app/api.service';
import { authInterceptor } from './app/auth.interceptor';

interface RuntimeConfig {
  baseUrl: string;
}

function isRuntimeConfig(value: unknown): value is RuntimeConfig {
  return typeof value === 'object'
    && value !== null
    && 'baseUrl' in value
    && typeof value.baseUrl === 'string'
    && value.baseUrl.trim().length > 0;
}

async function bootstrap(): Promise<void> {
  const response = await fetch(new URL('assets/config.json', document.baseURI), { cache: 'no-store' });
  if (!response.ok) {
    throw new Error(`Failed to load runtime configuration (${response.status}).`);
  }
  const config: unknown = await response.json();
  if (!isRuntimeConfig(config)) {
    throw new Error('Runtime configuration must contain a non-empty baseUrl.');
  }

  await bootstrapApplication(AppComponent, {
    providers: [
      provideAnimationsAsync(),
      provideHttpClient(withInterceptors([authInterceptor])),
      { provide: API_BASE_URL, useValue: config.baseUrl.trim() }
    ]
  });
}

bootstrap().catch(error => console.error('Application startup failed.', error));
