# 薪資明細列印 × 收費對帳系統

補習班（20 個分校、400 餘名員工）的內部作業工具，把兩項原本耗時的人工流程自動化。兩個模組共用同一套前後端，打包成一個執行檔交付給不懂技術的行政同事使用。

| 模組 | 原本 | 現在 |
|---|---|---|
| **薪資明細列印** | 從加密 Excel 逐人抄成薪資單，400 人 | 匯入後勾選、批次產生 PDF，一人一頁 |
| **收費對帳** | 3 分校 × 5 種收費項目、800+ 筆紙本與系統人工核對，約 2 天 | 上傳兩個 Excel，**數秒**完成 |

---

## 技術棧

| 層 | 技術 |
|---|---|
| 後端 | ASP.NET Core 8 Web API、Dapper、MySqlConnector |
| 前端 | Angular 18（standalone components、signals、新版控制流語法） |
| PDF | QuestPDF |
| Excel | ClosedXML（讀寫）、openpyxl（匯入工具） |
| 資料庫 | MySQL 8 |
| 部署 | `dotnet publish --self-contained`，目標機器免裝 .NET |

## 系統架構

```
                    ┌──────────────── payslip-web (Angular 18) ────────────────┐
                    │   /            薪資明細列印   查詢・勾選・批次列印          │
                    │   /preview     薪資單預覽     pdf.js 內嵌預覽              │
                    │   /reconcile   收費對帳       上傳・比對・匯出              │
                    └────────────────────────┬─────────────────────────────────┘
                                             │  /api/*
                    ┌────────────────────────┴─────────────────────────────────┐
                    │                PayslipApi (.NET 8)                       │
                    │                                                          │
                    │  Controllers/   PayslipsController    ReconcileController │
                    │  Data/          PayrollRepository (Dapper)               │
                    │  Pdf/           PayslipDocument (QuestPDF)               │
                    │  Reconcile/     ReconcileEngine · ExcelReader · Exporter │
                    └───────┬──────────────────────────────┬───────────────────┘
                            │                              │
                     ┌──────┴──────┐              ┌────────┴─────────┐
                     │  MySQL      │              │ 上傳的 Excel      │
                     │  payroll    │              │（不落地、僅記憶體）│
                     └──────▲──────┘              └──────────────────┘
                            │
                 tools/excel_to_sql.py
                 （每月加密 Excel → .sql 匯入檔）
```

前端 build 後放進後端的 `wwwroot`，由後端一起提供 —— 使用者只要開一個程式。

**前後端放在同一個 repo**，因為它們一起發版：`publish.bat` 會 build Angular、複製到 `wwwroot`、再 `dotnet publish`，最後產出單一個執行檔。API 契約改動也需要兩邊同步，分開會讓中間出現版本對不上的空窗。

---

## 模組一：薪資明細列印

每月的薪資是一份有密碼、含公式的 Excel。程式把它轉成 SQL 匯入 MySQL，再依勾選批次產生 PDF。

### 技術難點：從 Excel 公式反推應扣項目

主檔的「勞健保」是**一個合併後的數字**，但薪資單必須拆成「代扣勞保」與「代扣健保」兩列。

解法是**不看值、看公式**：

```python
# J 欄公式長得像 =-(1234+567)  →  第 1 項是勞保、第 2 項是健保
terms = split_terms(formula)          # 依括號深度拆出最外層的 +/- 項
amounts = [-sign * evaluate(t, val) for sign, t in terms]
```

只有單一項時無法從結構判斷是勞保還是健保，因此先**掃過全部有兩項以上的公式**，學出勞保基數與健保基數的集合，再回頭比對。`split_terms()` 需自行處理括號平衡，不能直接用 `split('+')`，否則 `=-(a+b)*c` 會拆錯。

其他處理：從自由格式的備註文字抽出各科目帶班人數且只取當月段落；離職人員以最後上班日取代工號；講師與一般員工資料結構完全不同，另一組資料表與版型。

## 模組二：收費對帳

系統匯出的「應收項目明細表」記錄**收了多少錢**；各班紙本訂餐表記錄**實際吃了幾餐／有沒有參加**。兩者差額就是該退的錢。

### 四條主規則

| 規則 | 判斷 | 輸出 |
|---|---|---|
| R1 名單 | 兩邊互相找不到人 | 漏登記／該退未退 |
| R2 日期 | 繳費日期不一致 | 抄錄錯誤 |
| R3 金額 | 紙本收費金額 ≠ 系統已繳 | 金額寫錯 |
| R4 退費 | `已收金額 − 實際份數 × 單價 − 已退費` | 應退金額 |

**R4 刻意用金額計、而非份數計。** 用份數會在三個地方出錯：點心「每週五天」收 650 而不是 20 × 33 = 660，用份數反推會少退；有沖銷或折扣的人已繳 ≠ 應繳，用應繳的份數算會多退。改用金額後這三種狀況都不需要特別處理。

另外**一律重算退費金額，不採信紙本的合計欄** —— 驗證月份的 11 個班級中有 6 個班的合計是錯的（表格跨頁時只加到部分列）。

### 三層防呆

配錯檔案會產出「看起來正常、實際完全錯誤」的金額，因此比對後做三重檢查：

| 層 | 條件 | 抓什麼 |
|---|---|---|
| 1 | 對得上的人 < 50% | 不同分校、不同月份 |
| 2 | 對得上的人裡，金額不符 > 50% | **分頁或收費項目選錯** |
| 3 | 分頁名稱指向其他收費項目 | 分頁叫「信義-午餐」卻選了電影 |

第 2 層是實測後補的：選錯收費項目時，因為是同一批學生，**姓名 93% 都對得上**，第 1 層完全攔不住，但金額已經全錯。

> 十個實務例外的完整清單、驗證結果對照表與各檔案職責，見 **[PayslipApi/README.md](PayslipApi/README.md)**。

---

## 關於資料

**這個 repo 不包含任何實際薪資或學生資料。**

薪資明細牽涉 400 餘名員工的個人薪資，收費對帳牽涉學生姓名與繳費紀錄，兩者都是公司內部資料，不隨程式碼發佈（見 `.gitignore`）。repo 內提供的是：

- `PayslipApi/schema.sql` —— 只有建表語法，沒有資料
- `PayslipApi/appsettings.example.json` —— 設定範本，實際連線字串不進版控

## 執行方式

### 1. 資料庫

```bash
mysql -u root -p -e "CREATE DATABASE payroll CHARACTER SET utf8mb4;"
mysql -u root -p --default-character-set=utf8mb4 payroll < PayslipApi/schema.sql
```

資料需自行匯入：把薪資 Excel 拖到 `PayslipApi/tools/轉換SQL.bat` 上，會產生對應月份的 `.sql`，再用 MySQL Workbench 整份執行。第二個月起請只匯入 `INSERT` 的部分（產生的檔案開頭有 `DROP TABLE`）。

### 2. 後端

```bash
cd PayslipApi
cp appsettings.example.json appsettings.json   # 填入自己的連線字串
dotnet restore
dotnet run                                     # http://localhost:5080
```

http://localhost:5080/swagger 可測試 API。

### 3. 前端

```bash
cd payslip-web
npm install
npm start                                      # http://localhost:4200
```

開發模式透過 `proxy.conf.json` 把 `/api` 轉到 http://localhost:5080。

### 打包給非技術使用者

```bash
cd PayslipApi
publish.bat
```

依序 build Angular → 複製到 `wwwroot` → `dotnet publish --self-contained`。產出的 `PayslipApp` 資料夾整個複製到目標電腦，雙擊 `start-payslip.bat` 即可使用，**免安裝 .NET**。

---

## API

| 方法 | 路徑 | 說明 |
|---|---|---|
| GET | `/api/payslips/periods` | 薪資月份清單 |
| GET | `/api/payslips/groups?period=115-08` | 部門／分校清單 |
| GET | `/api/payslips?period=&type=&status=&group=&keyword=` | 查詢列表（type：staff／lecturer／空白=全部） |
| GET | `/api/payslips/staff/{id}/pdf?period=115-08` | 單一員工薪資單 |
| GET | `/api/payslips/lecturer/{seqNo}/pdf?period=115-08` | 單一講師薪資單 |
| POST | `/api/payslips/pdf` | 批次，一份 PDF 一人一頁 |
| GET | `/api/reconcile/profiles` | 可選的收費項目 |
| POST | `/api/reconcile/sheets` | 上傳名單後列出分頁 |
| POST | `/api/reconcile` | 執行比對 |
| GET | `/api/reconcile/{id}/export` | 匯出比對結果 xlsx |

批次列印範例：

```json
{ "period": "115-08", "staffIds": [1, 2, 3], "lecturerSeqNos": [1, 3], "includeInternalNotes": false }
```

`includeInternalNotes=true` 會多印薪資結構說明、感恩獎金計算說明、鐘點費率等內部計算文字，發給員工的版本建議保持 `false`。

---

## 部署注意事項

- **字型**：PDF 預設使用 Windows 內建的微軟正黑體。部署到 Linux／Docker 時，把 `NotoSansTC-Regular.ttf` 放到 `PayslipApi/Fonts/`，並把設定的 `Payslip:FontFamily` 改成 `Noto Sans TC`，否則中文會變方塊。
- **QuestPDF 授權**：使用 Community 授權，年營收低於 100 萬美元可免費商用，超過需購買。

## 已知限制

- 訂餐表若無學號欄位，同名學生無法區分
- 紙本「整列劃掉表示退出」的標記無法從 Excel 讀取，需改以備註或欄位表示
- 比對結果暫存於記憶體（30 分鐘），未持久化
- 尚未加入身分驗證（目前為單機內部使用）
