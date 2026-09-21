# 薪資明細列印（.NET 8 + Angular 18）

```
PayslipApi/      C# Web API：查詢資料 + QuestPDF 產生薪資單 PDF
payslip-web/     Angular 18 前端：查詢、勾選、預覽 / 下載 / 列印
payroll_11508.sql  115年08月資料（MySQL）
```

## 1. 資料庫（MySQL 8 / MariaDB）

```sql
CREATE DATABASE payroll CHARACTER SET utf8mb4;
```
```bash
mysql -u root -p --default-character-set=utf8mb4 payroll < payroll_11508.sql
```

## 2. 後端 PayslipApi

1. 修改 `appsettings.json` 的 `ConnectionStrings:Payroll`（帳號、密碼、資料庫名稱）。
2. 執行：
   ```bash
   cd PayslipApi
   dotnet restore
   dotnet run
   ```
3. 開啟 http://localhost:5080/swagger 可測試 API。

### API
| 方法 | 路徑 | 說明 |
|---|---|---|
| GET | `/api/payslips/periods` | 薪資月份清單 |
| GET | `/api/payslips/groups?period=115-08` | 部門 / 分校清單 |
| GET | `/api/payslips?period=115-08&type=staff&status=在職&group=&keyword=` | 查詢列表（type：staff / lecturer / 空白=全部） |
| GET | `/api/payslips/staff/{id}/pdf?period=115-08` | 單一員工薪資單 |
| GET | `/api/payslips/lecturer/{seqNo}/pdf?period=115-08` | 單一講師薪資單 |
| POST | `/api/payslips/pdf` | 批次，一份 PDF 一人一頁 |

批次範例：
```json
{ "period": "115-08", "staffIds": [1, 2, 3], "lecturerSeqNos": [1, 3], "includeInternalNotes": false }
```

`includeInternalNotes=true` 會多印「薪資結構說明、感恩獎金計算說明、鐘點費率、其他備註」，
這些是內部計算用的文字，發給員工的版本建議保持 `false`。

### 字型
PDF 預設使用 Windows 內建的「微軟正黑體」。
若部署到 Linux / Docker 主機，請把 `NotoSansTC-Regular.ttf`（Google Fonts 免費下載）放到 `PayslipApi/Fonts/`，
並把 `appsettings.json` 的 `Payslip:FontFamily` 改成 `Noto Sans TC`，否則中文會變成方塊。

### QuestPDF 授權
程式使用 Community 授權，年營收低於 100 萬美元的公司可免費商用；超過需購買授權。

## 3. 前端 payslip-web

```bash
cd payslip-web
npm install
npm start
```
開啟 http://localhost:4200 。開發模式透過 `proxy.conf.json` 把 `/api` 轉到 http://localhost:5080 。

正式部署：`npm run build` 後把 `dist/payslip-web/browser` 放到 IIS / Nginx，並把 `/api` 反向代理到後端
（或放進後端的 `wwwroot`）。

## 薪資單內容

- **一般員工（在職 / 離職）**：姓名、工號（離職者為最後上班日）、部門、分校職務、到職日、發放方式；
  薪資、未到（加減薪）、勞健保、其他加減項、實發（結算）金額；感恩獎金；出勤 / 投保 / 加減項說明；簽收欄。
- **講師 / 外師**：姓名、工號、科別、發放方式；每一行課程的分校、課程、堂數、鐘點費、小計（堂數 × 鐘點費）與實發金額；簽收欄。
- 金額四捨五入到元。「實發金額」直接取自薪資計算表的合計，不由程式重新加總
  （原表的合計含按日折算、加班等公式，並非各項目直接相加）。

## 之後每月新增資料
把新的月份 Excel 轉成相同結構的 SQL，`period` 欄位填新月份（例如 `115-09`）匯入即可，
前端的月份下拉選單會自動出現。注意 `payroll_11508.sql` 開頭有 `DROP TABLE`，
第二個月起請只匯入 `INSERT` 的部分，或把建表語法拿掉。
