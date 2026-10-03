import { Component, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Label } from '../../core/models';
import { BoardStore } from './board.store';

export const LABEL_COLORS = ['#2563eb', '#7c3aed', '#db2777', '#dc2626', '#ea580c', '#ca8a04', '#16a34a', '#0891b2', '#64748b'];

/** Create, rename, recolour and delete the board's labels. Any member can do this. */
@Component({
  selector: 'app-labels-dialog',
  imports: [FormsModule],
  template: `
    <div class="backdrop" (click)="closed.emit()">
      <div class="card dialog" role="dialog" aria-modal="true" (click)="$event.stopPropagation()">
        <h2>Labels</h2>

        <ul class="label-list">
          @for (label of store.labels(); track label.id) {
            <li>
              <span class="label-chip selected" [style.--label]="label.color">{{ label.name }}</span>
              <span class="spacer"></span>
              <div class="swatches">
                @for (color of colors; track color) {
                  <button
                    class="swatch"
                    [class.active]="color === label.color"
                    [style.background]="color"
                    [title]="'Use this colour'"
                    (click)="store.updateLabel(label.id, label.name, color)"
                  ></button>
                }
              </div>
              <button class="icon-btn" title="Rename" (click)="rename(label)">&#9998;</button>
              <button class="icon-btn" title="Delete label" (click)="remove(label)">&times;</button>
            </li>
          } @empty {
            <li class="muted small">No labels yet.</li>
          }
        </ul>

        <form class="inline-form" (ngSubmit)="add()">
          <input name="name" [(ngModel)]="name" placeholder="New label, e.g. Bug" maxlength="30" />
          <div class="swatches">
            @for (c of colors; track c) {
              <button type="button" class="swatch" [class.active]="c === color()" [style.background]="c" (click)="color.set(c)"></button>
            }
          </div>
          <button class="btn btn-primary" type="submit" [disabled]="!name.trim()">Add</button>
        </form>
        @if (error()) {
          <p class="error">{{ error() }}</p>
        }

        <div class="dialog-actions">
          <span class="spacer"></span>
          <button class="btn" (click)="closed.emit()">Close</button>
        </div>
      </div>
    </div>
  `,
})
export class LabelsDialog {
  protected readonly store = inject(BoardStore);
  readonly closed = output<void>();

  protected readonly colors = LABEL_COLORS;
  protected readonly color = signal(LABEL_COLORS[0]);
  protected readonly error = signal<string | null>(null);
  protected name = '';

  protected async add(): Promise<void> {
    this.error.set(await this.store.addLabel(this.name.trim(), this.color()));
    if (!this.error()) this.name = '';
  }

  protected rename(label: Label): void {
    const name = prompt('Label name', label.name)?.trim();
    if (name && name !== label.name) this.store.updateLabel(label.id, name, label.color);
  }

  protected remove(label: Label): void {
    if (confirm(`Delete the label "${label.name}"? It will be removed from all tasks.`)) this.store.deleteLabel(label.id);
  }
}
