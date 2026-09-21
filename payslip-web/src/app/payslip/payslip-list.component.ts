import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { PayslipListItem, PayslipService, PayslipType } from './payslip.service';

type PdfAction = 'preview' | 'print' | 'download';

@Component({
  selector: 'app-payslip-list',
  standalone: true,
  imports: [FormsModule, DecimalPipe],
  templateUrl: './payslip-list.component.html',
  styleUrl: './payslip-list.component.scss',
})
export class PayslipListComponent implements OnInit {
  private api = inject(PayslipService);

  // 查詢條件
  periods = signal<string[]>([]);
  groups = signal<string[]>([]);
  period = '';
  type: '' | PayslipType = '';
  status = '';
  group = '';
  keyword = '';
  includeInternalNotes = false;

  // 狀態
  items = signal<PayslipListItem[]>([]);
  selected = signal<Set<string>>(new Set());
  loading = signal(false);
  busy = signal(false);
  error = signal('');

  selectedCount = computed(() => this.selected().size);
  allChecked = computed(() => this.items().length > 0 && this.items().every((i) => this.selected().has(this.keyOf(i))));
  totalAmount = computed(() => this.items().reduce((s, i) => s + (i.total ?? 0), 0));

  ngOnInit(): void {
    this.api.periods().subscribe({
      next: (p) => {
        this.periods.set(p);
        if (p.length) {
          this.period = p[0];
          this.onPeriodChange();
        }
      },
      error: (e) => this.fail('讀取薪資月份失敗', e),
    });
  }

  onPeriodChange(): void {
    this.api.groups(this.period).subscribe((g) => this.groups.set(g));
    this.search();
  }

  onTypeChange(): void {
    // 部門與在職/離職只適用一般員工
    if (this.type === 'lecturer') {
      this.status = '';
      this.group = '';
    }
    this.search();
  }

  search(): void {
    if (!this.period) return;
    this.loading.set(true);
    this.error.set('');
    this.api
      .search({ period: this.period, type: this.type, status: this.status, group: this.group, keyword: this.keyword })
      .subscribe({
        next: (rows) => {
          this.items.set(rows);
          this.selected.set(new Set());
          this.loading.set(false);
        },
        error: (e) => this.fail('查詢失敗', e),
      });
  }

  keyOf(i: PayslipListItem): string {
    return `${i.type}:${i.key}`;
  }

  toggle(i: PayslipListItem): void {
    const s = new Set(this.selected());
    const k = this.keyOf(i);
    s.has(k) ? s.delete(k) : s.add(k);
    this.selected.set(s);
  }

  toggleAll(): void {
    this.selected.set(this.allChecked() ? new Set() : new Set(this.items().map((i) => this.keyOf(i))));
  }

  payMethod(i: PayslipListItem): string {
    return [i.payTransfer ? '電匯' : '', i.payCash ? '領現' : ''].filter(Boolean).join(' / ');
  }

  /** 在新分頁開啟預覽頁 */
  private openPreview(period: string, staff: number[], lecturer: number[], name?: string | null, autoPrint = false): void {
    const params = new URLSearchParams({ period });
    if (staff.length) params.set('staff', staff.join(','));
    if (lecturer.length) params.set('lecturer', lecturer.join(','));
    if (name) params.set('name', name);
    if (autoPrint) params.set('print', '1');
    window.open(`/preview?${params.toString()}`, '_blank');
  }

  /** 單人 */
  single(i: PayslipListItem, action: PdfAction): void {
    if (action !== 'download') {
      this.openPreview(i.period, i.type === 'staff' ? [i.key] : [], i.type === 'lecturer' ? [i.key] : [], i.name, action === 'print');
      return;
    }
    this.handlePdf(this.api.singlePdf(i, this.includeInternalNotes), action, `薪資單_${i.period}_${i.name ?? i.key}.pdf`);
  }

  /** 勾選的人合併成一份 PDF（一人一頁） */
  batch(action: PdfAction): void {
    const chosen = this.items().filter((i) => this.selected().has(this.keyOf(i)));
    if (!chosen.length) return;
    if (action !== 'download') {
      this.openPreview(
        this.period,
        chosen.filter((i) => i.type === 'staff').map((i) => i.key),
        chosen.filter((i) => i.type === 'lecturer').map((i) => i.key),
        null,
        action === 'print',
      );
      return;
    }
    const req = {
      period: this.period,
      staffIds: chosen.filter((i) => i.type === 'staff').map((i) => i.key),
      lecturerSeqNos: chosen.filter((i) => i.type === 'lecturer').map((i) => i.key),
      includeInternalNotes: this.includeInternalNotes,
    };
    this.handlePdf(this.api.batchPdf(req), action, `薪資單_${this.period}_${chosen.length}人.pdf`);
  }

  private handlePdf(source: Observable<Blob>, action: PdfAction, fileName: string): void {
    // 預覽要先同步開視窗，避免被瀏覽器當成彈出視窗擋掉
    const win = action === 'preview' ? window.open('', '_blank') : null;
    this.busy.set(true);
    source.subscribe({
      next: (blob) => {
        this.busy.set(false);
        const url = URL.createObjectURL(new Blob([blob], { type: 'application/pdf' }));
        if (action === 'preview') {
          if (win) win.location.href = url;
          else window.open(url, '_blank');
        } else if (action === 'download') {
          const a = document.createElement('a');
          a.href = url;
          a.download = fileName;
          a.click();
        } else {
          this.printBlobUrl(url);
          return; // 列印完成後才釋放
        }
        setTimeout(() => URL.revokeObjectURL(url), 60_000);
      },
      error: (e) => {
        win?.close();
        this.fail('產生 PDF 失敗', e);
      },
    });
  }

  /** 用隱藏 iframe 載入 PDF 並直接叫出列印對話框 */
  private printBlobUrl(url: string): void {
    const iframe = document.createElement('iframe');
    iframe.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;';
    iframe.src = url;
    iframe.onload = () => {
      setTimeout(() => {
        iframe.contentWindow?.focus();
        iframe.contentWindow?.print();
      }, 300);
      setTimeout(() => {
        iframe.remove();
        URL.revokeObjectURL(url);
      }, 60_000);
    };
    document.body.appendChild(iframe);
  }

  private fail(msg: string, e: unknown): void {
    console.error(msg, e);
    this.loading.set(false);
    this.busy.set(false);
    this.error.set(`${msg}，請確認後端 API 與資料庫連線。`);
  }
}
