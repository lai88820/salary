import { Component, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    @if (showNav()) {
      <nav class="topnav">
        <a routerLink="/" routerLinkActive="on" [routerLinkActiveOptions]="{ exact: true }">薪資明細列印</a>
        <a routerLink="/reconcile" routerLinkActive="on">收費對帳</a>
      </nav>
    }
    <router-outlet />
  `,
  styles: `
    .topnav {
      display: flex;
      gap: 4px;
      padding: 0 16px;
      border-bottom: 1px solid #dde;
      background: #fff;
      font-family: 'Microsoft JhengHei', 'Noto Sans TC', sans-serif;
    }
    .topnav a {
      padding: 10px 16px;
      color: #555;
      text-decoration: none;
      font-size: 14px;
      border-bottom: 2px solid transparent;
    }
    .topnav a:hover { color: #1f5fbf; }
    .topnav a.on { color: #1f5fbf; border-bottom-color: #1f5fbf; font-weight: 600; }
  `,
})
export class AppComponent {
  private router = inject(Router);
  // 列印預覽頁不顯示導覽列
  showNav = signal(!this.router.url.startsWith('/preview'));

  constructor() {
    this.router.events
      .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
      .subscribe((e) => this.showNav.set(!e.urlAfterRedirects.startsWith('/preview')));
  }
}
