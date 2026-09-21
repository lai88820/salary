using Microsoft.AspNetCore.Mvc;
using PayslipApi.Data;
using PayslipApi.Models;
using PayslipApi.Pdf;
using QuestPDF.Fluent;

namespace PayslipApi.Controllers;

[ApiController]
[Route("api/payslips")]
public class PayslipsController : ControllerBase
{
    private readonly PayrollRepository _repo;
    private readonly IConfiguration _config;

    public PayslipsController(PayrollRepository repo, IConfiguration config)
    {
        _repo = repo;
        _config = config;
    }

    /// <summary>可選的薪資月份，例如 115-08</summary>
    [HttpGet("periods")]
    public async Task<IEnumerable<string>> Periods() => await _repo.GetPeriodsAsync();

    /// <summary>部門 / 分校清單</summary>
    [HttpGet("groups")]
    public async Task<IEnumerable<string>> Groups([FromQuery] string period) => await _repo.GetGroupsAsync(period);

    /// <summary>查詢列表。type = staff | lecturer | 空白(全部)</summary>
    [HttpGet]
    public async Task<IEnumerable<PayslipListItem>> Search(
        [FromQuery] string period, [FromQuery] string? type, [FromQuery] string? status,
        [FromQuery] string? group, [FromQuery] string? keyword)
        => await _repo.SearchAsync(period, type, status, group, keyword);

    /// <summary>單一員工 PDF</summary>
    [HttpGet("staff/{id:int}/pdf")]
    public Task<IActionResult> StaffPdf(int id, [FromQuery] string period, [FromQuery] bool includeInternalNotes = false)
        => BuildPdf(new BatchPdfRequest { Period = period, StaffIds = { id }, IncludeInternalNotes = includeInternalNotes });

    /// <summary>單一講師 PDF</summary>
    [HttpGet("lecturer/{seqNo:int}/pdf")]
    public Task<IActionResult> LecturerPdf(int seqNo, [FromQuery] string period, [FromQuery] bool includeInternalNotes = false)
        => BuildPdf(new BatchPdfRequest { Period = period, LecturerSeqNos = { seqNo }, IncludeInternalNotes = includeInternalNotes });

    /// <summary>批次：一份 PDF，一人一頁</summary>
    [HttpPost("pdf")]
    public Task<IActionResult> BatchPdf([FromBody] BatchPdfRequest req) => BuildPdf(req);

    private async Task<IActionResult> BuildPdf(BatchPdfRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Period)) return BadRequest("請指定 period");

        var staff = await _repo.GetStaffAsync(req.Period, req.StaffIds);
        var lecturers = await _repo.GetLecturersAsync(req.Period, req.LecturerSeqNos);
        if (staff.Count == 0 && lecturers.Count == 0) return NotFound("查無資料");

        var opt = new PayslipPdfOptions
        {
            CompanyName = _config["Payslip:CompanyName"] ?? "成大教育事業",
            ThankYouText = _config["Payslip:ThankYouText"] ?? "謝謝老師，您辛苦了!",
            FontFamily = _config["Payslip:FontFamily"] ?? "Microsoft JhengHei",
            IncludeInternalNotes = req.IncludeInternalNotes,
        };

        var bytes = new PayslipDocument(staff, lecturers, opt).GeneratePdf();

        var total = staff.Count + lecturers.Count;
        var fileName = total == 1
            ? $"薪資單_{req.Period}_{(staff.FirstOrDefault()?.SlipName ?? staff.FirstOrDefault()?.Name) ?? lecturers[0].Name}.pdf"
            : $"薪資單_{req.Period}_{total}人.pdf";

        // inline：瀏覽器可直接預覽 / 列印
        Response.Headers.ContentDisposition = $"inline; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
        return File(bytes, "application/pdf");
    }
}
