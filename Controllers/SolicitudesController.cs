using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlataformaCreditos.Data;
using PlataformaCreditos.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace PlataformaCreditos.Controllers;

[Authorize]
public class SolicitudesController : Controller
{
    private readonly ApplicationDbContext _context;

    public SolicitudesController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: Solicitudes
    public async Task<IActionResult> Index(string? estado, double? montoMin, double? montoMax, DateTime? fechaInicio, DateTime? fechaFin)
    {
        ModelState.Clear();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var query = _context.Solicitudes
            .Include(s => s.Cliente)
            .Where(s => s.Cliente.UsuarioId == userId);

        if (montoMin < 0 || montoMax < 0)
        {
            ModelState.AddModelError("", "Los montos no pueden ser valores negativos.");
        }

        if (montoMin.HasValue && montoMax.HasValue && montoMax < montoMin)
        {
            ModelState.AddModelError("", "El monto máximo no puede ser menor al mínimo.");
        }

        if (fechaInicio.HasValue && fechaFin.HasValue && fechaInicio > fechaFin)
        {
            ModelState.AddModelError("", "La fecha de inicio no puede ser posterior a la fecha de fin.");
        }

        if (!ModelState.IsValid)
        {
            return View(new List<SolicitudCredito>());
        }

        if (!string.IsNullOrEmpty(estado) && Enum.TryParse(typeof(EstadoSolicitud), estado, out var estadoEnum))
        {
            query = query.Where(s => s.Estado == (EstadoSolicitud)estadoEnum);
        }

        if (montoMin.HasValue) query = query.Where(s => s.MontoSolicitado >= montoMin.Value);
        if (montoMax.HasValue) query = query.Where(s => s.MontoSolicitado <= montoMax.Value);
        if (fechaInicio.HasValue) query = query.Where(s => s.FechaSolicitud >= fechaInicio.Value);
        if (fechaFin.HasValue) query = query.Where(s => s.FechaSolicitud <= fechaFin.Value);

        return View(await query.ToListAsync());
    }

    // GET: Solicitudes/Create
    public IActionResult Create()
    {
        return View();
    }

    // POST: Solicitudes/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SolicitudCredito solicitud)
    {
        // --- PARCHE DE VALIDACIÓN ---
        // Eliminamos "Cliente" del ModelState porque no viene del formulario
        ModelState.Remove("Cliente");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var cliente = await _context.Clientes.FirstOrDefaultAsync(c => c.UsuarioId == userId);

        if (cliente == null)
        {
            ModelState.AddModelError("", "Perfil de cliente no encontrado.");
            return View(solicitud);
        }

        if (!cliente.Activo)
        {
            ModelState.AddModelError("", "No puede solicitar crédito porque su cuenta está inactiva.");
        }

        var tienePendiente = await _context.Solicitudes
            .AnyAsync(s => s.ClienteId == cliente.Id && s.Estado == EstadoSolicitud.Pendiente);
        
        if (tienePendiente)
        {
            ModelState.AddModelError("", "Ya tiene una solicitud pendiente de revisión.");
        }

        if (solicitud.MontoSolicitado > (cliente.IngresosMensuales * 10))
        {
            ModelState.AddModelError("MontoSolicitado", $"El monto no puede superar 10 veces sus ingresos mensuales (${cliente.IngresosMensuales * 10}).");
        }

        if (ModelState.IsValid)
        {
            solicitud.ClienteId = cliente.Id;
            solicitud.Estado = EstadoSolicitud.Pendiente;
            solicitud.FechaSolicitud = DateTime.Now;

            _context.Add(solicitud);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        return View(solicitud);
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();

        var solicitud = await _context.Solicitudes
            .Include(s => s.Cliente)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (solicitud == null) return NotFound();

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (solicitud.Cliente.UsuarioId != userId) return Forbid();

        return View(solicitud);
    }
}