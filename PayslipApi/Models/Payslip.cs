namespace PayslipApi.Models;

/// <summary>payroll_detail：一般員工（在職 / 離職）</summary>
public class StaffPayslip
{
    public int Id { get; set; }
    public string Period { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Department { get; set; }
    public int? SeqNo { get; set; }
    public string? EmpCode { get; set; }
    public string? LastWorkDay { get; set; }
    public string? Name { get; set; }
    public string? EnglishName { get; set; }
    public int PayTransfer { get; set; }  // TINYINT 0/1
    public int PayCash { get; set; }
    public string? BranchPosition { get; set; }
    public string? HireInfo { get; set; }
    public decimal? Salary { get; set; }
    public string? SalaryStructure { get; set; }
    public decimal? AbsenceAdj { get; set; }
    public string? AbsenceNote { get; set; }
    public decimal? Insurance { get; set; }
    public string? InsuranceNote { get; set; }
    public decimal? OtherAdj1 { get; set; }
    public string? OtherAdj1Note { get; set; }
    public decimal? OtherAdj2 { get; set; }
    public string? OtherAdj2Note { get; set; }
    public decimal? Total { get; set; }
    public decimal? BonusMonthlyQuarterly { get; set; }
    public string? BonusNote { get; set; }
    public string? Bonus3MonthNote { get; set; }
    public string? Remarks { get; set; }

    // ===== 薪資單欄位 =====
    public string? SlipName { get; set; }
    public string? SlipDept { get; set; }
    public string? HireDate { get; set; }
    public int? PayBase { get; set; }
    public int? PayOvertime { get; set; }
    public int? PayOtherAdd { get; set; }
    public string? EarnNote { get; set; }
    public int? DedLaborIns { get; set; }
    public int? DedHealthIns { get; set; }
    public int? DedPension { get; set; }
    public int? DedOther { get; set; }
    public string? DeductNote { get; set; }
    public int? GrossTotal { get; set; }
    public int? DeductTotal { get; set; }
    public int? NetPay { get; set; }

    /// <summary>帶班科目人數：安親、資數、輔導資數、兒美、才藝、其他</summary>
    public Dictionary<string, decimal> ClassCounts { get; set; } = new();
}

public class ClassCountRow
{
    public int PayrollId { get; set; }
    public string Category { get; set; } = "";
    public decimal Headcount { get; set; }
}

/// <summary>lecturer_payroll：講師 / 外師</summary>
public class LecturerPayslip
{
    public int Id { get; set; }
    public string Period { get; set; } = "";
    public string? Subject { get; set; }
    public int SeqNo { get; set; }
    public string? EmpCode { get; set; }
    public string? Name { get; set; }
    public string? Alias { get; set; }
    public int PayTransfer { get; set; }  // TINYINT 0/1
    public int PayCash { get; set; }
    public decimal? Total { get; set; }
    public string? Remarks { get; set; }
    public List<LecturerClassLine> Lines { get; set; } = new();
}

public class LecturerClassLine
{
    public int SeqNo { get; set; }
    public int LineNo { get; set; }
    public string? Branch { get; set; }
    public string? Description { get; set; }
    public decimal? AbsenceAdj { get; set; }
    public decimal? Sessions { get; set; }
    public decimal? HourlyFee { get; set; }
    public string? OtherAdj { get; set; }

    /// <summary>堂數 × 鐘點費（與 Excel 合計算法一致）</summary>
    public decimal? Subtotal => Sessions.HasValue && HourlyFee.HasValue ? Sessions * HourlyFee : null;
}

/// <summary>列表用的精簡資料</summary>
public record PayslipListItem(
    string Type,          // staff | lecturer
    int Key,              // staff: id；lecturer: seq_no
    string Period,
    string? Group,        // 部門 / 科別
    string? Status,
    int? SeqNo,
    string? EmpCode,
    string? Name,
    string? Position,
    bool PayTransfer,
    bool PayCash,
    decimal? Total);

public class BatchPdfRequest
{
    public string Period { get; set; } = "";
    public List<int> StaffIds { get; set; } = new();
    public List<int> LecturerSeqNos { get; set; } = new();
    /// <summary>是否印出內部備註（薪資結構說明、其他備註）</summary>
    public bool IncludeInternalNotes { get; set; }
}
