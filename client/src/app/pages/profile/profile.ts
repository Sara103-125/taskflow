import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../core/auth.service';
import { errorMessage } from '../../core/errors';
import { ToastService } from '../../shared/toast.service';

const DEMO_EMAIL = 'demo@taskflow.example';

/** Change display name and password. Both are blocked for the shared demo account (server-side too). */
@Component({
  selector: 'app-profile',
  imports: [FormsModule],
  template: `
    <section class="page narrow">
      <h1>Profile</h1>
      @if (isDemo()) {
        <div class="banner">You're using the shared demo account, so the profile and password can't be changed.</div>
      }

      <div class="card">
        <h2>Your details</h2>
        <form (ngSubmit)="saveName()">
          <label>
            Email
            <input [value]="auth.user()?.email" disabled />
          </label>
          <label>
            Display name
            <input name="displayName" [(ngModel)]="displayName" required maxlength="50" [disabled]="isDemo()" />
          </label>
          @if (nameError()) {
            <p class="error">{{ nameError() }}</p>
          }
          <button class="btn btn-primary" type="submit" [disabled]="isDemo() || !displayName.trim() || displayName.trim() === auth.user()?.displayName">
            Save name
          </button>
        </form>
      </div>

      <div class="card">
        <h2>Change password</h2>
        <form (ngSubmit)="changePassword()" #pwForm="ngForm">
          <label>
            Current password
            <input name="current" type="password" [(ngModel)]="currentPassword" required autocomplete="current-password" [disabled]="isDemo()" />
          </label>
          <label>
            New password
            <input name="new" type="password" [(ngModel)]="newPassword" required minlength="8" autocomplete="new-password" [disabled]="isDemo()" />
          </label>
          <p class="muted small">At least 8 characters, with an uppercase letter, a lowercase letter and a digit.</p>
          @if (passwordError()) {
            <p class="error">{{ passwordError() }}</p>
          }
          <button class="btn btn-primary" type="submit" [disabled]="isDemo() || pwForm.invalid || busy()">Change password</button>
        </form>
      </div>
    </section>
  `,
})
export class ProfilePage {
  protected readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  protected readonly isDemo = computed(() => this.auth.user()?.email === DEMO_EMAIL);
  protected readonly nameError = signal<string | null>(null);
  protected readonly passwordError = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected displayName = this.auth.user()?.displayName ?? '';
  protected currentPassword = '';
  protected newPassword = '';

  protected saveName(): void {
    this.nameError.set(null);
    this.auth.updateProfile(this.displayName.trim()).subscribe({
      next: () => this.toast.info('Name updated.'),
      error: (err) => this.nameError.set(errorMessage(err)),
    });
  }

  protected changePassword(): void {
    this.busy.set(true);
    this.passwordError.set(null);
    this.auth.changePassword(this.currentPassword, this.newPassword).subscribe({
      next: () => {
        this.busy.set(false);
        this.currentPassword = this.newPassword = '';
        this.toast.info('Password changed.');
      },
      error: (err) => {
        this.busy.set(false);
        this.passwordError.set(errorMessage(err));
      },
    });
  }
}
