import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export interface FeeProfile {
  name: string;
  unitPrice: number;
  fixedPrice: boolean;
  monthlyCap: number | null;
}

export interface ReconcileRow {
  studentName: string;
  studentId: string | null;
  className: string;
  billedUnits: number | null;
  paidUnits: number | null;
  actualUnits: number | null;
  paid: number;
  alreadyRefunded: number;
  pendingRefund: number;
  unpaid: number;
  status: string;
}

export type FindingType =
  | 'MissingInRoster'
  | 'MissingInBilling'
  | 'DateMismatch'
  | 'AmountMismatch'
  | 'PendingRefund'
  | 'Unpaid'
  | 'DuplicateInRoster'
  | 'PendingSettlement'
  | 'RefundedButStillListed'
  | 'CancelledButNotRefunded';

export interface Finding {
  type: FindingType;
  severity: 'Info' | 'Warning' | 'Critical';
  studentName: string;
  studentId: string | null;
  className: string;
  amount: number;
  paperValue: string | null;
  systemValue: string | null;
  message: string;
}

export interface ReconcileSummary {
  profile: string;
  month: string;
  sheet: string;
  billingCount: number;
  rosterCount: number;
  personCount: number;
  matchedCount: number;
  matchRate: number;
  amountMismatchRate: number;
  totalPaid: number;
  totalPendingRefund: number;
  totalUnpaid: number;
  findingCount: number;
  byType: Record<string, number>;
}

export interface ReconcileResponse {
  id: string;
  /** 兩份檔可能不是同一分校/月份時的警告，正常時為 null */
  warning: string | null;
  summary: ReconcileSummary;
  rows: ReconcileRow[];
  findings: Finding[];
}

export interface RunRequest {
  billing: File;
  roster: File;
  profile: string;
  rosterSheet: string;
  month: string;
}

@Injectable({ providedIn: 'root' })
export class ReconcileService {
  private http = inject(HttpClient);
  private base = '/api/reconcile';

  profiles(): Observable<FeeProfile[]> {
    return this.http.get<FeeProfile[]>(`${this.base}/profiles`);
  }

  sheets(roster: File): Observable<string[]> {
    const fd = new FormData();
    fd.append('roster', roster);
    return this.http.post<string[]>(`${this.base}/sheets`, fd);
  }

  run(req: RunRequest): Observable<ReconcileResponse> {
    const fd = new FormData();
    fd.append('billing', req.billing);
    fd.append('roster', req.roster);
    fd.append('profile', req.profile);
    fd.append('rosterSheet', req.rosterSheet);
    fd.append('month', req.month);
    return this.http.post<ReconcileResponse>(this.base, fd);
  }

  exportUrl(id: string): string {
    return `${this.base}/${id}/export`;
  }
}

/**
 * 後端 enum 若沒加 JsonStringEnumConverter，type 會是數字。
 * 這張表讓畫面在沒重啟後端時也還能顯示中文（順序同 C# 的 FindingType）。
 */
export const FINDING_BY_INDEX: FindingType[] = [
  'MissingInRoster',
  'MissingInBilling',
  'DateMismatch',
  'AmountMismatch',
  'PendingRefund',
  'Unpaid',
  'DuplicateInRoster',
  'PendingSettlement',
  'RefundedButStillListed',
  'CancelledButNotRefunded',
];

/** 給畫面用的中文標籤 */
export const FINDING_LABEL: Record<FindingType, string> = {
  MissingInRoster: '系統有繳費、名單沒有',
  MissingInBilling: '名單有、系統查無繳費',
  DateMismatch: '繳費日期不符',
  AmountMismatch: '金額不符',
  PendingRefund: '應退未退',
  Unpaid: '欠費',
  DuplicateInRoster: '名單重複登記',
  PendingSettlement: '待結單',
  RefundedButStillListed: '已退費仍在名單',
  CancelledButNotRefunded: '紙本劃掉未退款',
};
