import { Component, ElementRef, inject, OnInit, signal, ViewChild } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { firstValueFrom } from 'rxjs';
import * as pdfjsLib from 'pdfjs-dist/legacy/build/pdf.mjs';
import { PayslipService } from './payslip.service';

// pdf.js 的 worker 由 angular.json 複製到 /pdfjs/
pdfjsLib.GlobalWorkerOptions.workerSrc = '/pdfjs/pdf.worker.min.mjs';

/**
 * 新分頁顯示薪資單：/preview?period=115-08&staff=1,2&lecturer=3[&print=1]
 * 用 pdf.js 把 PDF 畫在頁面上，不經過瀏覽器的 PDF 檢視器，
 * 所以不會因為瀏覽器設定「一律下載 PDF」或 IDM 等下載工具而變成下載。
 */
@Component({
  selector: 'app-payslip-preview',
  standalone: true,
  template: `
    <div class="bar">
      <strong>{{ caption() }}</strong>
      @if (pageCount()) { <span class="muted">共 {{ pageCount() }} 頁</span> }
      <span class="spacer"></span>
      <button class="btn" [disabled]="!ready()" (click)="download()">下載 PDF</button>
      <button class="btn primary" [disabled]="!ready()" (click)="print()">列印</button>
      <button class="btn" (click)="close()">關閉分頁</button>
    </div>

    @if (loading()) {
      <div class="msg">薪資單產生中，請稍候…</div>
    }
    @if (error()) {
      <div class="msg error">{{ error() }}</div>
    }
    <div class="pages" #pages></div>
  `,
  styles: [`
    :host { display: flex; flex-direction: column; height: 100vh; font-family: 'Microsoft JhengHei', 'Noto Sans TC', sans-serif; background: #e9ebee; }
    .bar { display: flex; align-items: center; gap: 10px; padding: 8px 16px; background: #fff; border-bottom: 1px solid #dde; position: sticky; top: 0; z-index: 1; }
    .spacer { flex: 1; }
    .muted { color: #777; font-size: 13px; }
    .btn { height: 32px; padding: 0 14px; border: 1px solid #99a; background: #fff; border-radius: 4px; cursor: pointer; font: inherit; }
    .btn.primary { background: #1f5fbf; border-color: #1f5fbf; color: #fff; }
    .btn:disabled { opacity: .5; cursor: default; }
    .pages { flex: 1; overflow: auto; padding: 16px; display: flex; flex-direction: column; align-items: center; gap: 16px; }
    .msg { padding: 40px; text-align: center; color: #555; }
    .msg.error { color: #a12622; }
  `],
})
export class PayslipPreviewComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private api = inject(PayslipService);
  private titleSvc = inject(Title);

  @ViewChild('pages', { static: true }) pagesEl!: ElementRef<HTMLDivElement>;

  loading = signal(true);
  ready = signal(false);
  error = signal('');
  caption = signal('薪資單預覽');
  pageCount = signal(0);

  private pdfBlob?: Blob;
  private canvases: HTMLCanvasElement[] = [];
  private fileName = '薪資單.pdf';

  async ngOnInit(): Promise<void> {
    const q = this.route.snapshot.queryParamMap;
    const period = q.get('period') ?? '';
    const staffIds = this.ids(q.get('staff'));
    const lecturerSeqNos = this.ids(q.get('lecturer'));
    const total = staffIds.length + lecturerSeqNos.length;

    if (!period || total === 0) {
      this.fail('網址缺少薪資月份或人員。');
      return;
    }

    const name = q.get('name');
    this.caption.set(total === 1 && name ? `${period} 薪資單－${name}` : `${period} 薪資單（共 ${total} 人）`);
    this.titleSvc.setTitle(this.caption());
    this.fileName = `${this.caption().replace(/[\\/:*?"<>|（）]/g, '_')}.pdf`;

    try {
      this.pdfBlob = await firstValueFrom(
        this.api.batchPdf({ period, staffIds, lecturerSeqNos, includeInternalNotes: false }),
      );
    } catch (e) {
      console.error(e);
      this.fail('產生 PDF 失敗，請確認後端 API 與資料庫連線。');
      return;
    }

    try {
      await this.render(await this.pdfBlob.arrayBuffer());
    } catch (e) {
      console.error(e);
      this.fail('PDF 顯示失敗，可以改用「下載 PDF」。');
      this.ready.set(true);
      return;
    }

    this.loading.set(false);
    this.ready.set(true);
    if (q.get('print') === '1') setTimeout(() => this.print(), 300);
  }

  /** 每一頁畫成一張 canvas */
  private async render(data: ArrayBuffer): Promise<void> {
    const pdf = await pdfjsLib.getDocument({ data }).promise;
    this.pageCount.set(pdf.numPages);
    const ratio = Math.max(window.devicePixelRatio || 1, 2); // 高解析度，列印才清楚

    for (let n = 1; n <= pdf.numPages; n++) {
      const page = await pdf.getPage(n);
      const cssViewport = page.getViewport({ scale: 1.3 });
      const viewport = page.getViewport({ scale: 1.3 * ratio });

      const canvas = document.createElement('canvas');
      canvas.width = Math.floor(viewport.width);
      canvas.height = Math.floor(viewport.height);
      canvas.style.width = `${Math.floor(cssViewport.width)}px`;
      canvas.style.maxWidth = '100%';
      canvas.style.background = '#fff';
      canvas.style.boxShadow = '0 1px 4px rgba(0,0,0,.25)';

      await page.render({ canvasContext: canvas.getContext('2d')!, viewport }).promise;
      this.pagesEl.nativeElement.appendChild(canvas);
      this.canvases.push(canvas);
    }
  }

  /** 把畫好的頁面放進隱藏 iframe 列印（不經過 PDF 檢視器） */
  print(): void {
    if (!this.canvases.length) return;
    const imgs = this.canvases.map((c) => `<img src="${c.toDataURL('image/png')}">`).join('');
    const iframe = document.createElement('iframe');
    iframe.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;';
    document.body.appendChild(iframe);
    const doc = iframe.contentDocument!;
    doc.open();
    doc.write(`<!doctype html><html><head><title>${this.caption()}</title><style>
      @page { size: A4; margin: 0; }
      html, body { margin: 0; padding: 0; }
      img { display: block; width: 210mm; height: 297mm; page-break-after: always; }
      img:last-child { page-break-after: auto; }
    </style></head><body>${imgs}</body></html>`);
    doc.close();

    const imgsEls = Array.from(doc.images);
    Promise.all(imgsEls.map((im) => (im.complete ? Promise.resolve() : new Promise((r) => (im.onload = r))))).then(() => {
      iframe.contentWindow!.focus();
      iframe.contentWindow!.print();
      setTimeout(() => iframe.remove(), 1000);
    });
  }

  download(): void {
    if (!this.pdfBlob) return;
    const url = URL.createObjectURL(this.pdfBlob);
    const a = document.createElement('a');
    a.href = url;
    a.download = this.fileName;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 10_000);
  }

  close(): void {
    window.close();
  }

  private fail(msg: string): void {
    this.loading.set(false);
    this.error.set(msg);
  }

  private ids(v: string | null): number[] {
    return (v ?? '').split(',').map((x) => Number(x)).filter((x) => Number.isInteger(x) && x > 0);
  }
}
