import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type PayslipType = 'staff' | 'lecturer';

export interface PayslipListItem {
  type: PayslipType;
  key: number; // staff: id；lecturer: seqNo
  period: string;
  group: string | null;
  status: string | null;
  seqNo: number | null;
  empCode: string | null;
  name: string | null;
  position: string | null;
  payTransfer: boolean;
  payCash: boolean;
  total: number | null;
}

export interface SearchQuery {
  period: string;
  type?: '' | PayslipType;
  status?: string;
  group?: string;
  keyword?: string;
}

export interface BatchPdfRequest {
  period: string;
  staffIds: number[];
  lecturerSeqNos: number[];
  includeInternalNotes: boolean;
}

@Injectable({ providedIn: 'root' })
export class PayslipService {
  private http = inject(HttpClient);
  private base = '/api/payslips';

  periods(): Observable<string[]> {
    return this.http.get<string[]>(`${this.base}/periods`);
  }

  groups(period: string): Observable<string[]> {
    return this.http.get<string[]>(`${this.base}/groups`, { params: { period } });
  }

  search(q: SearchQuery): Observable<PayslipListItem[]> {
    let params = new HttpParams().set('period', q.period);
    for (const k of ['type', 'status', 'group', 'keyword'] as const) {
      if (q[k]) params = params.set(k, q[k]!);
    }
    return this.http.get<PayslipListItem[]>(this.base, { params });
  }

  singlePdf(item: PayslipListItem, includeInternalNotes: boolean): Observable<Blob> {
    return this.http.get(`${this.base}/${item.type}/${item.key}/pdf`, {
      params: { period: item.period, includeInternalNotes },
      responseType: 'blob',
    });
  }

  batchPdf(req: BatchPdfRequest): Observable<Blob> {
    return this.http.post(`${this.base}/pdf`, req, { responseType: 'blob' });
  }
}
