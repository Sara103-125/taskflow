import { Component, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../core/auth.service';
import { BoardStore } from './board.store';

/** Shows board members. The owner can invite (by email) and remove; members can leave. */
@Component({
  selector: 'app-members-dialog',
  imports: [FormsModule],
  template: `
    <div class="backdrop" (click)="closed.emit()">
      <div class="card dialog" role="dialog" aria-modal="true" (click)="$event.stopPropagation()">
        <h2>Members</h2>

        <ul class="member-list">
          @for (member of store.members(); track member.userId) {
            <li>
              <div>
                <strong>{{ member.displayName }}</strong>
                <span class="muted small"> {{ member.email }}</span>
              </div>
              <span class="badge">{{ member.role }}</span>
              @if (canRemove(member.userId, member.role)) {
                <button class="btn btn-ghost btn-small danger" (click)="store.removeMember(member.userId)">Remove</button>
              }
            </li>
          }
        </ul>

        @if (store.board()?.isDemo) {
          <p class="muted small">Members can't be changed on the demo board.</p>
        } @else if (store.isOwner()) {
          <form class="inline-form" (ngSubmit)="invite()">
            <input name="email" type="email" [(ngModel)]="email" placeholder="teammate@example.com" required />
            <button class="btn btn-primary" type="submit" [disabled]="!email.trim()">Invite</button>
          </form>
          <p class="muted small">They need a TaskFlow account first.</p>
          @if (error()) {
            <p class="error">{{ error() }}</p>
          }
        } @else {
          <button class="btn btn-ghost danger" (click)="leave()">Leave this board</button>
        }

        <div class="dialog-actions">
          <span class="spacer"></span>
          <button class="btn" (click)="closed.emit()">Close</button>
        </div>
      </div>
    </div>
  `,
})
export class MembersDialog {
  protected readonly store = inject(BoardStore);
  private readonly auth = inject(AuthService);
  readonly closed = output<void>();

  protected email = '';
  protected readonly error = signal<string | null>(null);

  protected canRemove(userId: string, role: string): boolean {
    return this.store.isOwner() && !this.store.board()?.isDemo && role !== 'Owner' && userId !== this.auth.user()?.id;
  }

  protected async invite(): Promise<void> {
    this.error.set(await this.store.invite(this.email.trim()));
    if (!this.error()) this.email = '';
  }

  protected leave(): void {
    if (confirm('Leave this board? You will need to be invited again to come back.')) this.store.leaveBoard();
  }
}
