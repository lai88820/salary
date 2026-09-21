using PayslipApi.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PayslipApi.Pdf;

public class PayslipPdfOptions
{
    public string CompanyName { get; set; } = "成大教育事業";
    public string ThankYouText { get; set; } = "謝謝老師，您辛苦了!";
    public string FontFamily { get; set; } = "Microsoft JhengHei";
    public bool IncludeInternalNotes { get; set; }
}

/// <summary>
/// 薪資單（一人一頁），版面：
///   公司名稱 / 115 年 08 月薪資
///   姓名、工號、到職日、部門
///   應領項目 | 金額 | 應扣項目 | 金額
///   帶班科目 | 人數
///   應領合計(A)、應扣合計(B)、實發金額(A)-(B)
/// </summary>
public class PayslipDocument : IDocument
{
    private readonly IReadOnlyList<StaffPayslip> _staff;
    private readonly IReadOnlyList<LecturerPayslip> _lecturers;
    private readonly PayslipPdfOptions _opt;

    private const string Grey = "#D9D9D9";
    private const float RowH = 26;

    public PayslipDocument(IReadOnlyList<StaffPayslip> staff, IReadOnlyList<LecturerPayslip> lecturers, PayslipPdfOptions opt)
    {
        _staff = staff;
        _lecturers = lecturers;
        _opt = opt;
    }

    public DocumentMetadata GetMetadata() => new() { Title = $"{_opt.CompanyName} 薪資" };

    public void Compose(IDocumentContainer container)
    {
        foreach (var s in _staff)
            container.Page(page => { SetupPage(page); page.Content().Element(c => ComposeSlip(c, FromStaff(s))); });

        foreach (var l in _lecturers)
            container.Page(page => { SetupPage(page); page.Content().Element(c => ComposeSlip(c, FromLecturer(l))); });
    }

    // ---------------- 資料轉成薪資單格式 ----------------

    private sealed class Slip
    {
        public string Period = "";
        public string? Name, EmpCode, HireDate, Dept;
        public int? Base, Overtime, OtherAdd, Labor, Health, Pension, OtherDeduct;
        public string? EarnNote, DeductNote;
        public int Gross, Deduct, Net;
        public Dictionary<string, decimal> Counts = new();
    }

    private static Slip FromStaff(StaffPayslip s)
    {
        var slip = new Slip
        {
            Period = s.Period,
            Name = s.SlipName ?? s.Name,
            EmpCode = s.EmpCode,
            HireDate = s.HireDate,
            Dept = s.SlipDept ?? s.Department,
            Base = s.PayBase,
            Overtime = s.PayOvertime,
            OtherAdd = s.PayOtherAdd,
            EarnNote = s.EarnNote,
            Labor = s.DedLaborIns,
            Health = s.DedHealthIns,
            Pension = s.DedPension,
            OtherDeduct = s.DedOther,
            DeductNote = s.DeductNote,
            Counts = s.ClassCounts,
        };
        slip.Gross = s.GrossTotal ?? (slip.Base ?? 0) + (slip.Overtime ?? 0) + (slip.OtherAdd ?? 0);
        slip.Deduct = s.DeductTotal ?? (slip.Labor ?? 0) + (slip.Health ?? 0) + (slip.Pension ?? 0) + (slip.OtherDeduct ?? 0);
        slip.Net = s.NetPay ?? slip.Gross - slip.Deduct;
        return slip;
    }

    private static Slip FromLecturer(LecturerPayslip l)
    {
        var total = (int)Math.Round(l.Total ?? 0, 0, MidpointRounding.AwayFromZero);
        var sessions = l.Lines.Where(x => x.Sessions.HasValue).Sum(x => x.Sessions!.Value);
        return new Slip
        {
            Period = l.Period,
            Name = l.Name,
            EmpCode = l.EmpCode,
            Dept = l.Lines.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Branch))?.Branch?.Replace("彙整", "").Trim() ?? l.Subject,
            Base = total,
            EarnNote = sessions > 0 ? $"鐘點 {Math.Round(sessions, 1):0.#} 堂" : null,
            Labor = 0, Health = 0, Pension = 0, OtherDeduct = 0,
            Gross = total, Deduct = 0, Net = total,
        };
    }

    // ---------------- 版面 ----------------

    private void SetupPage(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.MarginVertical(1.8f, Unit.Centimetre);
        page.MarginHorizontal(2.2f, Unit.Centimetre);
        page.DefaultTextStyle(x => x.FontFamily(_opt.FontFamily, "Noto Sans TC", "Microsoft JhengHei", "PMingLiU").FontSize(11));
    }

    private void ComposeSlip(IContainer container, Slip s)
    {
        container.Column(col =>
        {
            // 標題
            col.Item().AlignCenter().Text(_opt.CompanyName).FontSize(16).Bold();
            col.Item().PaddingTop(10).AlignCenter().Text(PeriodTitle(s.Period)).FontSize(13);

            // 基本資料
            col.Item().PaddingTop(18).PaddingBottom(6).PaddingHorizontal(4).Row(r =>
            {
                r.RelativeItem(3).Text($"姓名：{s.Name}");
                r.RelativeItem(3).Text($"工號：{s.EmpCode}");
                r.RelativeItem(4).Text($"到職日：{s.HireDate}");
                r.RelativeItem(4).Text($"部門：{s.Dept}");
            });

            // 應領 / 應扣
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });

                HeadCell(t, "應領項目"); HeadCell(t, "金額", right: true);
                HeadCell(t, "應扣項目"); HeadCell(t, "金額", right: true);

                Label(t, "本　　薪"); Amount(t, s.Base, blankZero: true);
                Label(t, "代扣勞保"); Amount(t, s.Labor);

                Label(t, "平均加班"); Amount(t, s.Overtime, blankZero: true);
                Label(t, "代扣健保"); Amount(t, s.Health);

                Label(t, "其他加項"); Amount(t, s.OtherAdd, blankZero: true);
                Label(t, "自提勞退"); Amount(t, s.Pension);

                Label(t, "備註說明"); Note(t, s.EarnNote);
                Label(t, "其他減項"); Amount(t, s.OtherDeduct);

                Label(t, ""); Note(t, null);
                Label(t, "備註說明"); Note(t, s.DeductNote);
            });

            // 帶班科目
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); // 左半：科目|人數|科目|人數
                    c.RelativeColumn(2); c.RelativeColumn(2);                                     // 右半
                });

                t.Cell().ColumnSpan(3).Background(Grey).Height(RowH).PaddingHorizontal(4).AlignMiddle().Text("帶班科目").Bold();
                t.Cell().Background(Grey).Height(RowH).PaddingHorizontal(4).AlignMiddle().AlignRight().Text("人　數").Bold();
                t.Cell().Background(Grey).Height(RowH).PaddingHorizontal(4).AlignMiddle().Text("帶班科目").Bold();
                t.Cell().Background(Grey).Height(RowH).PaddingHorizontal(4).AlignMiddle().AlignRight().Text("人　數").Bold();

                var left = new[] { ("安親", "安　親"), ("資數", "資　數"), ("輔導資數", "輔導資數") };
                var right = new[] { ("兒美", "兒　美"), ("才藝", "才　藝"), ("其他", "其　他") };
                for (var i = 0; i < 3; i++)
                {
                    ClassLabel(t, left[i].Item2); ClassCount(t, s.Counts, left[i].Item1);
                    ClassLabel(t, right[i].Item2); ClassCount(t, s.Counts, right[i].Item1);
                    t.Cell().Height(RowH); t.Cell().Height(RowH);
                }
            });

            // 合計
            col.Item().PaddingTop(4).Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });

                Label(t, "應領合計(A)", bold: true); Amount(t, s.Gross, bold: true);
                Label(t, "應扣合計(B)", bold: true); Amount(t, s.Deduct, bold: true);

                Label(t, "實發金額(A)-(B)", bold: true); Amount(t, s.Net, bold: true);
                t.Cell().ColumnSpan(2).Height(RowH).PaddingHorizontal(4).AlignMiddle().Text(_opt.ThankYouText).Bold();
            });
        });
    }

    private static void HeadCell(TableDescriptor t, string text, bool right = false)
    {
        var c = t.Cell().Background(Grey).Height(RowH).PaddingHorizontal(4).AlignMiddle();
        (right ? c.AlignRight() : c).Text(text).Bold();
    }

    private static void Label(TableDescriptor t, string text, bool bold = false)
    {
        var tx = t.Cell().MinHeight(RowH).PaddingHorizontal(4).AlignMiddle().Text(text);
        if (bold) tx.Bold();
    }

    private static void Amount(TableDescriptor t, int? v, bool blankZero = false, bool bold = false)
    {
        var text = v is null || (blankZero && v == 0) ? "" : v.Value.ToString("#,0");
        var tx = t.Cell().MinHeight(RowH).PaddingHorizontal(4).AlignMiddle().AlignRight().Text(text);
        if (bold) tx.Bold();
    }

    private static void Note(TableDescriptor t, string? text) =>
        t.Cell().MinHeight(RowH).PaddingHorizontal(4).PaddingVertical(2).AlignMiddle().AlignRight()
         .Text(text ?? "").FontSize(8);

    private static void ClassLabel(TableDescriptor t, string text) =>
        t.Cell().Background(Grey).Height(RowH).PaddingHorizontal(6).AlignMiddle().Text(text);

    private static void ClassCount(TableDescriptor t, Dictionary<string, decimal> counts, string key) =>
        t.Cell().Height(RowH).PaddingHorizontal(4).AlignMiddle().AlignRight()
         .Text(counts.TryGetValue(key, out var n) ? n.ToString("0.#") : "");

    /// <summary>"115-08" → "115 年 08 月薪資"</summary>
    private static string PeriodTitle(string p)
    {
        var parts = p.Split('-');
        return parts.Length == 2 ? $"{parts[0]} 年 {parts[1]} 月薪資" : $"{p} 薪資";
    }
}
