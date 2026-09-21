using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace PayslipApi.Reconcile;

/// <summary>收費項目設定檔。新增項目時改這裡就好。</summary>
public sealed record FeeProfile(string Name, string ItemKeyword, ReconcileOptions Options)
{
    public static readonly FeeProfile[] All =
    {
        new("午餐",     "午餐",   new ReconcileOptions { UnitPrice = 95 }),
        new("點心",     "點心",   new ReconcileOptions { UnitPrice = 33, MonthlyCap = 650 }),
        new("晚餐",     "晚餐",   new ReconcileOptions { UnitPrice = 95 }),
        new("電影",     "電影院", new ReconcileOptions { UnitPrice = 490,  FixedPrice = true }),
        new("大型活動", "大型活動", new ReconcileOptions { UnitPrice = 1580, FixedPrice = true }),
    };

    public static FeeProfile Get(string name) =>
        All.FirstOrDefault(p => p.Name == name)
        ?? throw new ArgumentException($"未知的收費項目：{name}");
}

/// <summary>
/// 讀取兩種來源檔。欄位以「標題文字」對應，不寫死欄號，
/// 所以系統匯出格式微調（多一欄少一欄）不會壞掉。
/// </summary>
public static class ExcelReader
{
    // ---------- 來源 A：應收項目明細表 ----------
    private static readonly string[] BillingRequired = { "學生", "收費項目", "已繳" };

    public static List<BillingRecord> ReadBilling(Stream stream, string month, string itemKeyword)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();
        var (headerRow, map) = FindHeader(ws, BillingRequired);

        var list = new List<BillingRecord>();
        foreach (var row in ws.RowsUsed().Where(r => r.RowNumber() > headerRow))
        {
            string item = Str(row, map, "收費項目");
            if (string.IsNullOrWhiteSpace(item)) continue;
            if (!item.Contains(month) || !item.Contains(itemKeyword)) continue;

            list.Add(new BillingRecord
            {
                StudentId   = Str(row, map, "學號", "學生編號"),
                StudentName = Str(row, map, "學生", "學生姓名"),
                ClassName   = Str(row, map, "班級"),
                Category    = Str(row, map, "收費類別"),
                ItemName    = item,
                PaidDate    = Date(row, map, "繳費日期"),
                Due         = Num(row, map, "應繳"),
                Paid        = Num(row, map, "已繳"),
                Unpaid      = Num(row, map, "未繳"),
                Refunded    = Num(row, map, "已退費"),
                Discount    = Num(row, map, "折扣"),
                WriteOff    = Num(row, map, "沖銷金額"),
                BillStatus  = Str(row, map, "帳單狀態"),
                RefundReason= Str(row, map, "退費事由"),
                Note        = Str(row, map, "備註"),
            });
        }
        return list;
    }

    // ---------- 來源 B：訂餐表／活動名單 ----------
    // 兩種格式都要吃：
    //   (a) 規格格式：一列標題（學號/學生姓名/班級/實際份數/收費金額/繳費日期/…）
    //   (b) 各班原始訂餐表：兩列標題（「合計」「繳費狀況」是跨欄合併，子標題在下一列），
    //       份數欄叫「餐數」，中間夾 1~31 日的打勾格，最後有「每日餐份合計」等統計列。
    private static readonly string[][] RosterRequiredSets =
    {
        new[] { "學生姓名", "實際份數" },
        new[] { "學生姓名", "餐數" },
        new[] { "姓名",     "餐數" },
    };

    /// <summary>不是學生、要跳過的列（原始訂餐表底部的統計列）。</summary>
    private static readonly string[] SummaryLabels =
        { "合計", "總計", "素食餐份", "每日餐份", "餐份合計" };

    public static List<string> ListSheets(Stream stream)
    {
        using var wb = new XLWorkbook(stream);
        return wb.Worksheets.Select(w => w.Name).ToList();
    }

    public static List<RosterRecord> ReadRoster(Stream stream, string sheetName)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheet(sheetName);
        var (headerRow, map) = FindRosterHeader(ws);

        var list = new List<RosterRecord>();
        foreach (var row in ws.RowsUsed().Where(r => r.RowNumber() > headerRow))
        {
            string name = Str(row, map, "學生姓名", "姓名", "學生");
            if (string.IsNullOrWhiteSpace(name)) continue;
            // 統計列不是學生，跳過（「每日餐份合計」等）
            if (SummaryLabels.Any(s => name.Contains(s))) continue;

            list.Add(new RosterRecord
            {
                StudentId     = Str(row, map, "學號", "學生編號"),
                StudentName   = name,
                // 原始訂餐表沒有班級欄，班別寫在表頭，退而用分頁名稱
                ClassName     = Col(map, "班級") is not null ? Str(row, map, "班級") : sheetName,
                ActualUnits   = Int(row, map, "實際份數", "餐數"),
                ChargedAmount = NumOrNull(row, map, "收費金額"),
                ChargedDate   = Date(row, map, "繳費日期", "日期"),
                RefundAmount  = NumOrNull(row, map, "退費金額"),
                ReceiptNo     = Str(row, map, "收據編號"),
                IsCancelled   = Str(row, map, "是否退出", "退出").Trim().ToUpperInvariant() is "Y" or "是" or "TRUE",
                Note          = Str(row, map, "備註"),
            });
        }
        return list;
    }

    // ---------- helpers ----------

    /// <summary>往下找 15 列，第一列同時包含所有必要標題的就是標題列。</summary>
    private static (int, Dictionary<string, int>) FindHeader(IXLWorksheet ws, string[] required)
    {
        foreach (var row in ws.RowsUsed().Take(15))
        {
            var map = LabelsOf(row);
            if (required.All(r => map.Keys.Any(k => k.Contains(r))))
                return (row.RowNumber(), map);
        }
        throw new InvalidDataException(
            $"找不到標題列，需要包含：{string.Join("、", required)}");
    }

    /// <summary>
    /// 訂餐表的標題列可能橫跨兩列（上列是合併的大標題，下列才是子標題），
    /// 所以除了單列，也把「相鄰兩列」合起來當成一組標題試。
    /// 回傳的列號是兩列中較下面那列，資料從它的下一列開始。
    /// </summary>
    private static (int, Dictionary<string, int>) FindRosterHeader(IXLWorksheet ws)
    {
        var rows = ws.RowsUsed().Take(15).ToList();

        // 先試單列
        foreach (var required in RosterRequiredSets)
            foreach (var row in rows)
            {
                var map = LabelsOf(row);
                if (required.All(r => map.Keys.Any(k => k.Contains(r))))
                    return (row.RowNumber(), map);
            }

        // 再試相鄰兩列合併
        foreach (var required in RosterRequiredSets)
            for (int i = 0; i + 1 < rows.Count; i++)
            {
                if (rows[i + 1].RowNumber() != rows[i].RowNumber() + 1) continue;
                var map = LabelsOf(rows[i]);
                foreach (var (k, v) in LabelsOf(rows[i + 1]))
                    map.TryAdd(k, v);
                if (required.All(r => map.Keys.Any(x => x.Contains(r))))
                    return (rows[i + 1].RowNumber(), map);
            }

        throw new InvalidDataException(
            "找不到訂餐表的標題列。需要有「學生姓名」，以及「實際份數」或「餐數」其中一欄。");
    }

    private static Dictionary<string, int> LabelsOf(IXLRow row)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var cell in row.CellsUsed())
        {
            var t = cell.GetString().Trim();
            if (t.Length > 0 && !map.ContainsKey(t)) map[t] = cell.Address.ColumnNumber;
        }
        return map;
    }

    private static int? Col(Dictionary<string, int> map, params string[] names)
    {
        foreach (var n in names)
        {
            if (map.TryGetValue(n, out var c)) return c;
            var hit = map.Keys.FirstOrDefault(k => k.Contains(n));
            if (hit != null) return map[hit];
        }
        return null;
    }

    private static string Str(IXLRow row, Dictionary<string, int> map, params string[] names)
        => Col(map, names) is { } c ? row.Cell(c).GetString().Trim() : "";

    private static decimal Num(IXLRow row, Dictionary<string, int> map, params string[] names)
        => NumOrNull(row, map, names) ?? 0m;

    private static decimal? NumOrNull(IXLRow row, Dictionary<string, int> map, params string[] names)
    {
        if (Col(map, names) is not { } c) return null;
        var cell = row.Cell(c);
        if (cell.IsEmpty()) return null;
        if (cell.TryGetValue<decimal>(out var d)) return d;
        var s = cell.GetString().Replace(",", "").Replace("$", "").Trim();
        return decimal.TryParse(s, out var p) ? p : null;
    }

    private static int? Int(IXLRow row, Dictionary<string, int> map, params string[] names)
        => NumOrNull(row, map, names) is { } d ? (int)Math.Round(d) : null;

    private static DateOnly? Date(IXLRow row, Dictionary<string, int> map, params string[] names)
    {
        if (Col(map, names) is not { } c) return null;
        var cell = row.Cell(c);
        if (cell.IsEmpty()) return null;
        if (cell.TryGetValue<DateTime>(out var dt)) return DateOnly.FromDateTime(dt);

        // 容錯：純文字的 "2026/08/12"、"8/12"
        var s = cell.GetString().Trim();
        if (string.IsNullOrEmpty(s)) return null;
        if (DateTime.TryParse(s, out var p)) return DateOnly.FromDateTime(p);
        var parts = s.Split('/');
        if (parts.Length == 2 && int.TryParse(parts[0], out var m) && int.TryParse(parts[1], out var d2))
            return new DateOnly(DateTime.Today.Year, m, d2);
        return null;
    }
}
