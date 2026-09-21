using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace PayslipApi.Reconcile;

/// <summary>把比對結果輸出成 xlsx（總覽 / 逐人明細 / 異常清單）。</summary>
public static class ReportExporter
{
    private const string Money = "#,##0;(#,##0);-";
    private static readonly XLColor Head = XLColor.FromHtml("#2F5597");
    private static readonly XLColor Warn = XLColor.FromHtml("#FFF2CC");
    private static readonly XLColor Bad = XLColor.FromHtml("#FCE4E4");
    private static readonly XLColor Tot = XLColor.FromHtml("#D9E2F3");

    public static byte[] Build(ReconcileResult r, string profile, string month, string sheet)
    {
        using var wb = new XLWorkbook();

        // ---------- 總覽 ----------
        var ws = wb.AddWorksheet("總覽");
        ws.Cell(1, 1).Value = $"{month} {profile} 比對結果";
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        ws.Cell(2, 1).Value = $"訂餐表分頁：{sheet}　產生時間：{DateTime.Now:yyyy/MM/dd HH:mm}";
        ws.Cell(2, 1).Style.Font.SetItalic().Font.SetFontSize(9);

        var stats = new (string, object)[]
        {
            ("比對人數",     r.PersonCount),
            ("已收金額",     r.TotalPaid),
            ("應退金額",     r.TotalPendingRefund),
            ("欠費金額",     r.TotalUnpaid),
            ("異常筆數",     r.Findings.Count),
        };
        int row = 4;
        foreach (var (k, v) in stats)
        {
            ws.Cell(row, 1).Value = k;
            ws.Cell(row, 2).Value = XLCellValue.FromObject(v);
            if (v is decimal) ws.Cell(row, 2).Style.NumberFormat.Format = Money;
            ws.Cell(row, 1).Style.Font.SetBold();
            row++;
        }
        row++;
        ws.Cell(row++, 1).Value = "異常型態統計";
        ws.Cell(row - 1, 1).Style.Font.SetBold();
        foreach (var g in r.Findings.GroupBy(f => f.Type).OrderByDescending(g => g.Count()))
        {
            ws.Cell(row, 1).Value = TypeName(g.Key);
            ws.Cell(row, 2).Value = g.Count();
            row++;
        }
        ws.Column(1).Width = 28;
        ws.Column(2).Width = 16;

        // ---------- 逐人明細 ----------
        ws = wb.AddWorksheet("逐人明細");
        WriteHeader(ws, new[] { "班級", "學號", "學生", "訂購份數", "已收份數", "實際份數", "已收金額", "已退費", "應退金額", "欠費", "狀態" },
                        new[] { 12, 12, 14, 10, 10, 10, 12, 10, 12, 10, 12 });
        row = 2;
        foreach (var x in r.Rows)
        {
            ws.Cell(row, 1).Value = x.ClassName;
            ws.Cell(row, 2).Value = x.StudentId ?? "";
            ws.Cell(row, 3).Value = x.StudentName;
            ws.Cell(row, 4).Value = x.BilledUnits.HasValue ? x.BilledUnits.Value : Blank();
            ws.Cell(row, 5).Value = x.PaidUnits.HasValue ? x.PaidUnits.Value : Blank();
            ws.Cell(row, 6).Value = x.ActualUnits.HasValue ? x.ActualUnits.Value : Blank();
            ws.Cell(row, 7).Value = x.Paid;
            ws.Cell(row, 8).Value = x.AlreadyRefunded;
            ws.Cell(row, 9).Value = x.PendingRefund;
            ws.Cell(row, 10).Value = x.Unpaid;
            ws.Cell(row, 11).Value = x.Status;
            ws.Range(row, 7, row, 10).Style.NumberFormat.Format = Money;
            if (x.Unpaid > 0) ws.Range(row, 1, row, 11).Style.Fill.BackgroundColor = Bad;
            else if (x.PendingRefund > 0) ws.Range(row, 1, row, 11).Style.Fill.BackgroundColor = Warn;
            row++;
        }
        if (row > 2)
        {
            ws.Cell(row, 3).Value = "合計";
            ws.Cell(row, 7).FormulaA1 = $"SUM(G2:G{row - 1})";
            ws.Cell(row, 8).FormulaA1 = $"SUM(H2:H{row - 1})";
            ws.Cell(row, 9).FormulaA1 = $"SUM(I2:I{row - 1})";
            ws.Cell(row, 10).FormulaA1 = $"SUM(J2:J{row - 1})";
            ws.Range(row, 1, row, 11).Style.Fill.BackgroundColor = Tot;
            ws.Range(row, 1, row, 11).Style.Font.SetBold();
            ws.Range(row, 7, row, 10).Style.NumberFormat.Format = Money;
        }
        ws.SheetView.FreezeRows(1);

        // ---------- 異常清單 ----------
        ws = wb.AddWorksheet("異常清單");
        WriteHeader(ws, new[] { "嚴重度", "型態", "班級", "學生", "金額", "紙本", "系統", "說明" },
                        new[] { 10, 18, 12, 14, 12, 18, 22, 40 });
        row = 2;
        foreach (var f in r.Findings.OrderByDescending(f => f.Severity).ThenByDescending(f => f.Amount))
        {
            ws.Cell(row, 1).Value = f.Severity switch
            {
                Severity.Critical => "嚴重",
                Severity.Warning => "注意",
                _ => "提醒",
            };
            ws.Cell(row, 2).Value = TypeName(f.Type);
            ws.Cell(row, 3).Value = f.ClassName;
            ws.Cell(row, 4).Value = f.StudentName;
            ws.Cell(row, 5).Value = f.Amount;
            ws.Cell(row, 6).Value = f.PaperValue ?? "";
            ws.Cell(row, 7).Value = f.SystemValue ?? "";
            ws.Cell(row, 8).Value = f.Message;
            ws.Cell(row, 5).Style.NumberFormat.Format = Money;
            if (f.Severity == Severity.Critical) ws.Range(row, 1, row, 8).Style.Fill.BackgroundColor = Bad;
            else if (f.Severity == Severity.Warning) ws.Range(row, 1, row, 8).Style.Fill.BackgroundColor = Warn;
            row++;
        }
        ws.SheetView.FreezeRows(1);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static XLCellValue Blank() => XLCellValue.FromObject(null);

    private static void WriteHeader(IXLWorksheet ws, string[] headers, int[] widths)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(1, i + 1);
            c.Value = headers[i];
            c.Style.Fill.BackgroundColor = Head;
            c.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Column(i + 1).Width = widths[i];
        }
    }

    private static string TypeName(FindingType t) => t switch
    {
        FindingType.MissingInRoster => "系統有繳費、名單沒有",
        FindingType.MissingInBilling => "名單有、系統查無繳費",
        FindingType.DateMismatch => "繳費日期不符",
        FindingType.AmountMismatch => "金額不符",
        FindingType.PendingRefund => "應退未退",
        FindingType.Unpaid => "欠費",
        FindingType.DuplicateInRoster => "名單重複登記",
        FindingType.PendingSettlement => "待結單",
        FindingType.RefundedButStillListed => "已退費仍在名單",
        FindingType.CancelledButNotRefunded => "紙本劃掉未退款",
        _ => t.ToString(),
    };
}
