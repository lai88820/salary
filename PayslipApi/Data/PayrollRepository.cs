using Dapper;
using MySqlConnector;
using PayslipApi.Models;

namespace PayslipApi.Data;

public class PayrollRepository
{
    private readonly string _connStr;

    public PayrollRepository(IConfiguration config)
    {
        _connStr = config.GetConnectionString("Payroll")
                   ?? throw new InvalidOperationException("缺少 ConnectionStrings:Payroll 設定");
        DefaultTypeMap.MatchNamesWithUnderscores = true; // pay_transfer -> PayTransfer
    }

    private MySqlConnection Open() => new(_connStr);

    public async Task<IEnumerable<string>> GetPeriodsAsync()
    {
        await using var db = Open();
        return await db.QueryAsync<string>(
            "SELECT period FROM payroll_detail UNION SELECT period FROM lecturer_payroll ORDER BY period DESC");
    }

    public async Task<IEnumerable<string>> GetGroupsAsync(string period)
    {
        await using var db = Open();
        return await db.QueryAsync<string>(@"
            SELECT department FROM payroll_detail WHERE period=@period AND department IS NOT NULL
            GROUP BY department ORDER BY MIN(id)", new { period });
    }

    public async Task<IEnumerable<PayslipListItem>> SearchAsync(
        string period, string? type, string? status, string? group, string? keyword)
    {
        await using var db = Open();
        var kw = string.IsNullOrWhiteSpace(keyword) ? null : $"%{keyword.Trim()}%";
        var result = new List<PayslipListItem>();

        if (type is null or "" or "staff")
        {
            var staff = await db.QueryAsync<StaffPayslip>(@"
                SELECT * FROM payroll_detail
                WHERE period=@period
                  AND (@status IS NULL OR status=@status)
                  AND (@grp IS NULL OR department=@grp)
                  AND (@kw IS NULL OR name LIKE @kw OR english_name LIKE @kw OR emp_code LIKE @kw OR branch_position LIKE @kw)
                ORDER BY id", new { period, status = Empty(status), grp = Empty(group), kw });
            result.AddRange(staff.Select(s => new PayslipListItem("staff", s.Id, s.Period, s.Department, s.Status,
                s.SeqNo, s.EmpCode ?? s.LastWorkDay, s.SlipName ?? s.Name, s.BranchPosition, s.PayTransfer == 1, s.PayCash == 1, (decimal?)s.NetPay ?? s.Total)));
        }

        // 講師不分在職/離職、不屬於部門篩選
        if ((type is null or "" or "lecturer") && Empty(status) is null && Empty(group) is null)
        {
            var lec = await db.QueryAsync<LecturerPayslip>(@"
                SELECT * FROM lecturer_payroll
                WHERE period=@period AND name IS NOT NULL
                  AND (@kw IS NULL OR name LIKE @kw OR alias LIKE @kw OR emp_code LIKE @kw OR subject LIKE @kw)
                ORDER BY seq_no", new { period, kw });
            result.AddRange(lec.Select(l => new PayslipListItem("lecturer", l.SeqNo, l.Period, l.Subject, "講師",
                l.SeqNo, l.EmpCode, l.Name, l.Alias, l.PayTransfer == 1, l.PayCash == 1, l.Total)));
        }
        return result;
    }

    public async Task<List<StaffPayslip>> GetStaffAsync(string period, IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new();
        await using var db = Open();
        var rows = await db.QueryAsync<StaffPayslip>(
            "SELECT * FROM payroll_detail WHERE period=@period AND id IN @list ORDER BY id", new { period, list });
        var result = rows.ToList();
        var counts = await db.QueryAsync<ClassCountRow>(
            "SELECT payroll_id, category, headcount FROM payroll_class_count WHERE payroll_id IN @list", new { list });
        var byId = counts.ToLookup(c => c.PayrollId);
        foreach (var s in result)
            s.ClassCounts = byId[s.Id].ToDictionary(c => c.Category, c => c.Headcount);
        return result;
    }

    public async Task<List<LecturerPayslip>> GetLecturersAsync(string period, IEnumerable<int> seqNos)
    {
        var list = seqNos.Distinct().ToList();
        if (list.Count == 0) return new();
        await using var db = Open();
        var heads = (await db.QueryAsync<LecturerPayslip>(
            "SELECT * FROM lecturer_payroll WHERE period=@period AND seq_no IN @list ORDER BY seq_no",
            new { period, list })).ToList();
        var lines = await db.QueryAsync<LecturerClassLine>(
            "SELECT * FROM lecturer_class_line WHERE period=@period AND seq_no IN @list ORDER BY seq_no, line_no",
            new { period, list });
        var bySeq = lines.ToLookup(l => l.SeqNo);
        foreach (var h in heads) h.Lines = bySeq[h.SeqNo].ToList();
        return heads;
    }

    private static string? Empty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
