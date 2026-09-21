import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  FINDING_BY_INDEX,
  FINDING_LABEL,
  FeeProfile,
  Finding,
  FindingType,
  ReconcileResponse,
  ReconcileService,
} from './reconcile.service';

type Tab = 'findings' | 'rows';

@Component({
  selector: 'app-reconcile',
  standalone: true,
  imports: [FormsModule, DecimalPipe],
  templateUrl: './reconcile.component.html',
  styleUrl: './reconcile.component.scss',
})
export class ReconcileComponent implements OnInit {
  private api = inject(ReconcileService);

  // 條件
  profiles = signal<FeeProfile[]>([]);
  sheets = signal<string[]>([]);
  profile = '午餐';
  rosterSheet = '';
  month = defaultMonth();

  billingFile: File | null = null;
  rosterFile: File | null = null;

  // 狀態
  busy = signal(false);
  error = signal('');
  result = signal<ReconcileResponse | null>(null);
  tab = signal<Tab>('findings');
  filterType = signal<FindingType | ''>('');

  /**
   * 注意：billingFile / rosterFile / rosterSheet / month 是一般屬性不是 signal，
   * 用 computed() 包住的話追蹤不到變化，會永遠停在第一次算出的 false（按鈕按不下去）。
   * 這裡用一般方法，每次變更偵測都重算。
   */
  canRun(): boolean {
    return !!this.billingFile && !!this.rosterFile && !!this.rosterSheet && !!this.month;
  }

  findings = computed<Finding[]>(() => {
    const r = this.result();
    if (!r) return [];
    const t = this.filterType();
    return t ? r.findings.filter((f) => f.type === t) : r.findings;
  });

  /**
   * 異常型態 → 件數。
   * 一定要從 findings 算，不能用 summary.byType：
   * 那邊的 key 永遠是字串，findings 的 type 卻可能是數字（後端 enum 沒轉字串時），
   * 兩個來源不一致就會出現「chip 顯示 190 筆、點下去卻說沒有異常」。
   */
  typeCounts = computed(() => {
    const r = this.result();
    if (!r) return [] as { type: FindingType; label: string; count: number }[];
    const m = new Map<FindingType, number>();
    for (const f of r.findings) m.set(f.type, (m.get(f.type) ?? 0) + 1);
    return [...m.entries()]
      .map(([type, count]) => ({ type, label: this.labelOf(type), count }))
      .sort((a, b) => b.count - a.count);
  });

  /** type 可能是名稱或數字，兩種都要能翻成中文 */
  labelOf(t: FindingType | number): string {
    const key = typeof t === 'number' ? FINDING_BY_INDEX[t] : t;
    return FINDING_LABEL[key] ?? String(t);
  }

  ngOnInit(): void {
    this.api.profiles().subscribe({
      next: (p) => this.profiles.set(p),
      error: (e) => this.fail('讀取收費項目失敗', e),
    });
  }

  onBillingPick(e: Event): void {
    this.billingFile = (e.target as HTMLInputElement).files?.[0] ?? null;
    this.result.set(null);
  }

  onRosterPick(e: Event): void {
    this.rosterFile = (e.target as HTMLInputElement).files?.[0] ?? null;
    this.sheets.set([]);
    this.rosterSheet = '';
    this.result.set(null);
    if (!this.rosterFile) return;

    this.busy.set(true);
    this.api.sheets(this.rosterFile).subscribe({
      next: (s) => {
        this.sheets.set(s);
        // 依收費項目猜一個預設分頁，例如「信義-午餐」
        this.rosterSheet = s.find((x) => x.includes(this.profile)) ?? s[0] ?? '';
        this.busy.set(false);
      },
      error: (e) => this.fail('讀取訂餐表分頁失敗', e),
    });
  }

  onProfileChange(): void {
    const hit = this.sheets().find((x) => x.includes(this.profile));
    if (hit) this.rosterSheet = hit;
    this.result.set(null);
  }

  run(): void {
    if (!this.canRun()) return;
    this.busy.set(true);
    this.error.set('');
    this.filterType.set('');
    this.api
      .run({
        billing: this.billingFile!,
        roster: this.rosterFile!,
        profile: this.profile,
        rosterSheet: this.rosterSheet,
        month: this.month,
      })
      .subscribe({
        next: (r) => {
          this.result.set(r);
          this.tab.set('findings');
          this.busy.set(false);
        },
        error: (e) => this.fail('比對失敗', e),
      });
  }

  exportXlsx(): void {
    const r = this.result();
    if (r) window.open(this.api.exportUrl(r.id), '_blank');
  }

  toggleFilter(t: FindingType): void {
    this.filterType.set(this.filterType() === t ? '' : t);
  }

  /** severity 同樣可能是名稱或數字（0=Info 1=Warning 2=Critical） */
  private sevName(s: Finding['severity'] | number): string {
    return typeof s === 'number' ? (['Info', 'Warning', 'Critical'][s] ?? 'Info') : s;
  }

  sevClass(s: Finding['severity'] | number): string {
    const n = this.sevName(s);
    return n === 'Critical' ? 'sev bad' : n === 'Warning' ? 'sev warn' : 'sev';
  }

  sevText(s: Finding['severity'] | number): string {
    const n = this.sevName(s);
    return n === 'Critical' ? '嚴重' : n === 'Warning' ? '注意' : '提醒';
  }

  private fail(msg: string, e: unknown): void {
    const detail = typeof (e as { error?: unknown })?.error === 'string' ? (e as { error: string }).error : '';
    this.error.set(detail ? `${msg}：${detail}` : msg);
    this.busy.set(false);
  }
}

function defaultMonth(): string {
  const d = new Date();
  d.setMonth(d.getMonth() - 1); // 通常對帳的是上個月
  return `${d.getFullYear()}年${String(d.getMonth() + 1).padStart(2, '0')}月`;
}
