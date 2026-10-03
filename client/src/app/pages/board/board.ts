import { CdkDrag, CdkDragDrop, CdkDragHandle, CdkDropList } from '@angular/cdk/drag-drop';
import { Component, OnDestroy, OnInit, computed, effect, inject, input, numberAttribute, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Column, Priority, Task } from '../../core/models';
import { TimeAgoPipe, dueLabel, initials, isOverdue } from '../../shared/format';
import { ArchiveDialog } from './archive-dialog';
import { BoardStore } from './board.store';
import { LabelsDialog } from './labels-dialog';
import { MembersDialog } from './members-dialog';
import { TaskDialog } from './task-dialog';

@Component({
  selector: 'app-board',
  imports: [
    CdkDropList,
    CdkDrag,
    CdkDragHandle,
    FormsModule,
    RouterLink,
    TaskDialog,
    MembersDialog,
    LabelsDialog,
    ArchiveDialog,
    TimeAgoPipe,
  ],
  providers: [BoardStore],
  templateUrl: './board.html',
})
export class BoardPage implements OnInit, OnDestroy {
  protected readonly store = inject(BoardStore);

  /** From the route "boards/:id" (withComponentInputBinding). */
  readonly id = input.required({ transform: numberAttribute });
  /** Optional "?task=5" (from a notification or My tasks): open that task's dialog. */
  readonly task = input<number | undefined, unknown>(undefined, {
    transform: (v: unknown) => (v === undefined || v === null || v === '' ? undefined : numberAttribute(v)),
  });

  protected readonly initials = initials;
  protected readonly isOverdue = isOverdue;
  protected readonly dueLabel = dueLabel;

  /** Every task list's id, so a card can be dropped into any column. */
  protected readonly taskListIds = computed(() => this.store.sortedColumns().map((c) => `column-${c.id}`));

  protected readonly dialog = signal<{ columnId: number; taskId: number | null } | null>(null);
  protected readonly showMembers = signal(false);
  protected readonly showLabels = signal(false);
  protected readonly showArchive = signal(false);
  protected readonly priorities: Priority[] = ['Low', 'Medium', 'High'];
  protected readonly showActivity = signal(true);
  protected readonly editingBoardName = signal(false);
  protected readonly editingColumnId = signal<number | null>(null);
  protected newColumnName = '';

  constructor() {
    // Once the board has loaded, open the task from "?task=" (again whenever it changes).
    let opened: number | undefined;
    effect(() => {
      const taskId = this.task();
      const task = this.store.tasks().find((t) => t.id === taskId);
      if (taskId !== opened && task) {
        opened = taskId;
        this.dialog.set({ columnId: task.columnId, taskId: task.id });
      }
    });
  }

  ngOnInit(): void {
    void this.store.open(this.id());
  }

  ngOnDestroy(): void {
    void this.store.close();
  }

  // ---------- Drag and drop ----------

  protected dropTask(event: CdkDragDrop<number, number, Task>): void {
    const sameSpot = event.previousContainer === event.container && event.previousIndex === event.currentIndex;
    if (!sameSpot) this.store.moveTask(event.item.data.id, event.container.data, event.currentIndex);
  }

  protected dropColumn(event: CdkDragDrop<Column[]>): void {
    if (event.previousIndex !== event.currentIndex) this.store.moveColumn(event.previousIndex, event.currentIndex);
  }

  // ---------- Inline renames ----------

  protected renameBoard(name: string): void {
    this.editingBoardName.set(false);
    const trimmed = name.trim();
    if (trimmed && trimmed !== this.store.board()?.name) this.store.renameBoard(trimmed);
  }

  protected renameColumn(column: Column, name: string): void {
    this.editingColumnId.set(null);
    const trimmed = name.trim();
    if (trimmed && trimmed !== column.name) this.store.renameColumn(column.id, trimmed);
  }

  protected addColumn(): void {
    const name = this.newColumnName.trim();
    if (!name) return;
    this.store.addColumn(name);
    this.newColumnName = '';
  }

  protected deleteColumn(column: Column): void {
    const count = this.store.tasksByColumn()[column.id]?.length ?? 0;
    const warning = count ? ` Its ${count} task${count === 1 ? '' : 's'} will be deleted too.` : '';
    if (confirm(`Delete column "${column.name}"?${warning}`)) this.store.deleteColumn(column.id);
  }
}
