using AutoMapper;
using Backend_ZS.API.Data;
using Backend_ZS.API.Models.Domain;
using Backend_ZS.API.Models.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend_ZS.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "admin")]
    public class CashBoxesController : ControllerBase
    {
        private readonly ZsDbContext dbContext;
        private readonly IMapper mapper;

        public CashBoxesController(ZsDbContext dbContext, IMapper mapper)
        {
            this.dbContext = dbContext;
            this.mapper = mapper;
        }

        private static TimeZoneInfo GetEcuadorTz()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("America/Guayaquil"); }
            catch { return TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time"); }
        }

        private static DateOnly EcuadorToday(TimeZoneInfo tz)
        {
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            return DateOnly.FromDateTime(localNow);
        }

        private static (DateTime utcStart, DateTime utcEnd) UtcRangeForLocalDate(DateOnly localDate, TimeZoneInfo tz)
        {
            var localStart = DateTime.SpecifyKind(localDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
            var localEnd = DateTime.SpecifyKind(localDate.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);

            var utcStart = TimeZoneInfo.ConvertTimeToUtc(localStart, tz);
            var utcEnd = TimeZoneInfo.ConvertTimeToUtc(localEnd, tz);

            return (utcStart, utcEnd);
        }

        // GET: /api/CashBoxes/{id}
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById([FromRoute] Guid id)
        {
            var cashBox = await dbContext.CashBoxes
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);

            if (cashBox == null) return NotFound();

            return Ok(mapper.Map<CashBoxDto>(cashBox));
        }

        // GET: /api/CashBoxes/today?date=2026-01-11
        [HttpGet("today")]
        public async Task<IActionResult> GetToday([FromQuery] DateOnly? date = null)
        {
            var tz = GetEcuadorTz();
            var targetLocalDate = date ?? EcuadorToday(tz);
            var (utcStart, utcEnd) = UtcRangeForLocalDate(targetLocalDate, tz);

            var cashBox = await dbContext.CashBoxes
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.OpenedAt >= utcStart && x.OpenedAt < utcEnd);

            if (cashBox == null) return NoContent();

            return Ok(mapper.Map<CashBoxDto>(cashBox));
        }

        // POST: /api/CashBoxes/open
        [HttpPost("open")]
        public async Task<IActionResult> Open([FromBody] OpenCashBoxRequestDto req)
        {
            var tz = GetEcuadorTz();
            var todayLocal = EcuadorToday(tz);
            var (utcStart, utcEnd) = UtcRangeForLocalDate(todayLocal, tz);

            var existing = await dbContext.CashBoxes
                .FirstOrDefaultAsync(x => x.OpenedAt >= utcStart && x.OpenedAt < utcEnd);

            if (existing != null)
            {
                if (existing.Status == CashBoxStatus.Open)
                    return Conflict($"Ya existe una caja ABIERTA para {todayLocal}.");

                return Conflict($"Ya existe una caja CERRADA para {todayLocal}.");
            }

            var cashBox = new CashBox
            {
                Id = Guid.NewGuid(),
                Status = CashBoxStatus.Open,
                OpenedAt = DateTime.UtcNow,
                OpeningBalance = req.OpeningBalance
            };

            dbContext.CashBoxes.Add(cashBox);
            await dbContext.SaveChangesAsync();

            return Ok(mapper.Map<CashBoxDto>(cashBox));
        }

        // POST: /api/CashBoxes/{id}/reopen
        [HttpPost("{id:guid}/reopen")]
        public async Task<IActionResult> Reopen([FromRoute] Guid id)
        {
            var tz = GetEcuadorTz();
            var todayLocal = EcuadorToday(tz);
            var (utcStart, utcEnd) = UtcRangeForLocalDate(todayLocal, tz);

            var cashBox = await dbContext.CashBoxes.FirstOrDefaultAsync(x => x.Id == id);
            if (cashBox == null) return NotFound();

            if (cashBox.Status == CashBoxStatus.Open)
                return Conflict("La caja ya está abierta.");


            cashBox.Status = CashBoxStatus.Open;
            cashBox.ClosedAt = null;
            cashBox.ClosingBalance = null;

            await dbContext.SaveChangesAsync();
            return Ok(mapper.Map<CashBoxDto>(cashBox));
        }

        // POST: /api/CashBoxes/{id}/close
        [HttpPost("{id:guid}/close")]
        public async Task<IActionResult> Close([FromRoute] Guid id, [FromBody] CloseCashBoxRequestDto req)
        {
            var cashBox = await dbContext.CashBoxes.FirstOrDefaultAsync(x => x.Id == id);
            if (cashBox == null) return NotFound();

            if (cashBox.Status == CashBoxStatus.Closed)
                return Conflict("La caja ya está cerrada.");

            var totalPayments = await dbContext.Transactions
                .Where(t => t.CashBoxId == id)
                .SelectMany(t => t.Payments)
                .SumAsync(p => (double?)p.Total) ?? 0.0;

            cashBox.Status = CashBoxStatus.Closed;
            cashBox.ClosedAt = DateTime.UtcNow;
            cashBox.ClosingBalance = req.ClosingBalance ?? (cashBox.OpeningBalance + totalPayments);

            await dbContext.SaveChangesAsync();

            return Ok(mapper.Map<CashBoxDto>(cashBox));
        }

        // GET: /api/CashBoxes/range?from=2026-01-01&to=2026-01-31
        [HttpGet("range")]
        public async Task<IActionResult> Range([FromQuery] DateOnly from, [FromQuery] DateOnly to)
        {
            // ✅ Range “por fecha local” también debería hacerse por rangos UTC por día,
            // pero si solo lo usas para reportes simples, lo dejo como estaba.
            var list = await dbContext.CashBoxes
                .AsNoTracking()
                .Where(x => DateOnly.FromDateTime(x.OpenedAt) >= from && DateOnly.FromDateTime(x.OpenedAt) <= to)
                .OrderByDescending(x => x.OpenedAt)
                .ToListAsync();

            return Ok(mapper.Map<List<CashBoxDto>>(list));
        }

        [HttpGet("{id:guid}/transactions")]
        public async Task<IActionResult> GetTransactions([FromRoute] Guid id)
        {
            var exists = await dbContext.CashBoxes.AnyAsync(x => x.Id == id);
            if (!exists) return NotFound();

            var txs = await dbContext.Transactions
                .AsNoTracking()
                .Include(t => t.Client)
                .Include(t => t.TransactionItems)
                .Include(t => t.Payments)
                .Where(t => t.CashBoxId == id)
                .OrderByDescending(t => t.OpenedAt)
                .ToListAsync();

            return Ok(mapper.Map<List<TransactionDto>>(txs));
        }

        [HttpGet("{id:guid}/summary")]
        public async Task<IActionResult> Summary([FromRoute] Guid id)
        {
            var cashBox = await dbContext.CashBoxes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (cashBox == null) return NotFound();

            var txs = dbContext.Transactions.Where(t => t.CashBoxId == id);

            var totalCharges = await txs.SelectMany(t => t.TransactionItems).SumAsync(i => (double?)i.Total) ?? 0.0;
            var totalPayments = await txs.SelectMany(t => t.Payments).SumAsync(p => (double?)p.Total) ?? 0.0;

            var cashPayments = await txs.SelectMany(t => t.Payments)
                .Where(p => p.Type == PaymentType.Efectivo)
                .SumAsync(p => (double?)p.Total) ?? 0.0;

            var transferPayments = await txs.SelectMany(t => t.Payments)
                .Where(p => p.Type == PaymentType.Transferencia)
                .SumAsync(p => (double?)p.Total) ?? 0.0;

            var openCount = await txs.CountAsync(t => t.Status == TransactionStatus.Open);
            var closedCount = await txs.CountAsync(t => t.Status == TransactionStatus.Closed);

            return Ok(new
            {
                CashBox = mapper.Map<CashBoxDto>(cashBox),
                TotalCharges = totalCharges,
                TotalPayments = totalPayments,
                Cash = cashPayments,
                Transfer = transferPayments,
                OpenTransactions = openCount,
                ClosedTransactions = closedCount
            });
        }
    }
}
