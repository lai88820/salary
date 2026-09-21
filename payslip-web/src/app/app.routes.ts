import { Routes } from '@angular/router';
import { PayslipListComponent } from './payslip/payslip-list.component';

export const routes: Routes = [
  { path: '', component: PayslipListComponent, title: '薪資明細列印' },
  // 例：/preview?period=115-08&staff=14,15&lecturer=3（pdf.js 較大，所以延遲載入）
  {
    path: 'preview',
    loadComponent: () => import('./payslip/payslip-preview.component').then((m) => m.PayslipPreviewComponent),
    title: '薪資單預覽',
  },
  {
    path: 'reconcile',
    loadComponent: () => import('./reconcile/reconcile.component').then((m) => m.ReconcileComponent),
    title: '收費對帳',
  },
  { path: '**', redirectTo: '' },
];
