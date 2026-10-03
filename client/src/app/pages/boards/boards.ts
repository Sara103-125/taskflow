import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { errorMessage } from '../../core/errors';
import { BoardSummary } from '../../core/models';
import { ToastService } from '../../shared/toast.service';

/** "My boards": every board I'm a member of, plus a form to create a new one. */
@Component({
  selector: 'app-boards',
  imports: [FormsModule, RouterLink],
  templateUrl: './boards.html',
})
export class BoardsPage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  protected readonly boards = signal<BoardSummary[] | null>(null);
  protected readonly active = computed(() => this.boards()?.filter((b) => !b.archivedAt) ?? null);
  protected readonly archived = computed(() => this.boards()?.filter((b) => b.archivedAt) ?? []);
  protected newBoardName = '';

  ngOnInit(): void {
    this.load();
  }

  protected create(): void {
    const name = this.newBoardName.trim();
    if (!name) return;
    this.api.createBoard(name).subscribe({
      next: (board) => {
        this.newBoardName = '';
        this.boards.update((list) => [...(list ?? []), board].sort((a, b) => a.name.localeCompare(b.name)));
      },
      error: (err) => this.toast.error(errorMessage(err)),
    });
  }

  /** Soft delete: members stop seeing it, the owner can restore it below. */
  protected archive(board: BoardSummary, event: Event): void {
    event.preventDefault(); // the button sits inside the board's link
    event.stopPropagation();
    this.api.archiveBoard(board.id).subscribe({
      next: () => this.setArchived(board.id, new Date().toISOString()),
      error: (err) => this.toast.error(errorMessage(err)),
    });
  }

  protected restore(board: BoardSummary): void {
    this.api.restoreBoard(board.id).subscribe({
      next: () => this.setArchived(board.id, null),
      error: (err) => this.toast.error(errorMessage(err)),
    });
  }

  protected remove(board: BoardSummary): void {
    if (!confirm(`Permanently delete "${board.name}" and all its tasks? This can't be undone.`)) return;
    this.api.deleteBoard(board.id).subscribe({
      next: () => this.boards.update((list) => (list ?? []).filter((b) => b.id !== board.id)),
      error: (err) => this.toast.error(errorMessage(err)),
    });
  }

  private setArchived(id: number, archivedAt: string | null): void {
    this.boards.update((list) => (list ?? []).map((b) => (b.id === id ? { ...b, archivedAt } : b)));
  }

  private load(): void {
    this.api.getBoards().subscribe({
      next: (boards) => this.boards.set(boards),
      error: (err) => this.toast.error(errorMessage(err)),
    });
  }
}
