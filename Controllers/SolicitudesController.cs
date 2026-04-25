using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlataformaCreditos.Data;
using PlataformaCreditos.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace PlataformaCreditos.Controllers;

[Authorize]
public class SolicitudesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IDistributedCache _cache;

    public SolicitudesController(ApplicationDbContext context, IDistributedCache cache)
    {
        _context = context;
        _cache = cache;
    }

    // GET: Solicitudes
    public async Task<IActionResult> Index(string? estado, double? montoMin, double? montoMax, DateTime? fechaInicio, DateTime? fechaFin)
    {
        ModelState.Clear();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        string cacheKey = $"Solicitudes_{userId}";
        List<SolicitudCredito>? solicitudes;
        
        var cachedData = await _cache.GetStringAsync(cacheKey);
        
        if (cachedData != null)
        {
            solicitudes = JsonSerializer.Deserialize<List<SolicitudCredito>>(cachedData);
        }
        else
        {
            var query = _context.Solicitudes
                .Include(s => s.Cliente)
                .Where(s => s.Cliente.UsuarioId == userId);

            if (montoMin < 0 || montoMax < 0) ModelState.AddModelError("", "Los montos no pueden ser negativos.");
            if (montoMin.HasValue && montoMax.HasValue && montoMax < montoMin) ModelState.AddModelError("", "Rango de montos inválido.");
            if (fechaInicio.HasValue && fechaFin.HasValue && fechaInicio > fechaFin) ModelState.AddModelError("", "Rango de fechas inválido.");

            if (!ModelState.IsValid) return View(new List<SolicitudCredito>());

            if (!string.IsNullOrEmpty(estado) && Enum.TryParse(typeof(EstadoSolicitud), estado, out var estadoEnum))
                query = query.Where(s => s.Estado == (EstadoSolicitud)estadoEnum);

            if (montoMin.HasValue) query = query.Where(s => s.MontoSolicitado >= montoMin.Value);
            if (montoMax.HasValue) query = query.Where(s => s.MontoSolicitado <= montoMax.Value);
            if (fechaInicio.HasValue) query = query.Where(s => s.FechaSolicitud >= fechaInicio.Value);
            if (fechaFin.HasValue) query = query.Where(s => s.FechaSolicitud <= fechaFin.Value);

            solicitudes = await query.ToListAsync();

            var cacheOptions = new DistributedCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromSeconds(60));
            
            await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(solicitudes), cacheOptions);
        }

        return View(solicitudes);
    }

    public IActionResult Create() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SolicitudCredito solicitud)
{
    ModelState.Remove("Cliente");
    
    // Agregamos '?? string.Empty' para que userId nunca sea nulo
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    
    var cliente = await _context.Clientes.FirstOrDefaultAsync(c => c.UsuarioId == userId);

    if (cliente == null)
    {
        cliente = new Cliente 
        { 
            UsuarioId = userId, 
            IngresosMensuales = 10000, 
            Activo = true 
        };
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();
        
        // Usamos '!' al final para decirle al compilador: "Confía en mí, ya no es nulo"
        cliente = (await _context.Clientes.FirstOrDefaultAsync(c => c.UsuarioId == userId))!;
    }
        // ----------------------------------------------

        if (!cliente.Activo)
            ModelState.AddModelError("", "No puede solicitar crédito porque su cuenta está inactiva.");

        var tienePendiente = await _context.Solicitudes
            .AnyAsync(s => s.ClienteId == cliente.Id && s.Estado == EstadoSolicitud.Pendiente);
        
        if (tienePendiente)
            ModelState.AddModelError("", "Ya tiene una solicitud pendiente de revisión.");

        if (solicitud.MontoSolicitado > (cliente.IngresosMensuales * 10))
            ModelState.AddModelError("MontoSolicitado", $"El monto no puede superar 10 veces sus ingresos mensuales.");

        if (ModelState.IsValid)
        {
            solicitud.ClienteId = cliente.Id;
            solicitud.Estado = EstadoSolicitud.Pendiente;
            solicitud.FechaSolicitud = DateTime.Now;

            _context.Add(solicitud);
            await _context.SaveChangesAsync();

            await _cache.RemoveAsync($"Solicitudes_{userId}");

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

        HttpContext.Session.SetString("UltimaId", solicitud.Id.ToString());
        HttpContext.Session.SetString("UltimaMonto", solicitud.MontoSolicitado.ToString());

        return View(solicitud);
    }

    // --- MÉTODOS DE LA PREGUNTA 5 (PANEL ANALISTA) ---

    [Authorize(Roles = "Analista")]
    public async Task<IActionResult> Analista()
    {
        var solicitudes = await _context.Solicitudes
            .Include(s => s.Cliente)
            .Where(s => s.Estado == EstadoSolicitud.Pendiente)
            .ToListAsync();

        return View(solicitudes);
    }

    [Authorize(Roles = "Analista")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Procesar(int id, EstadoSolicitud nuevoEstado, string? motivo)
    {
        var solicitud = await _context.Solicitudes
            .Include(s => s.Cliente)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (solicitud == null) return NotFound();

        if (solicitud.Estado != EstadoSolicitud.Pendiente)
        {
            TempData["Error"] = "La solicitud ya ha sido procesada.";
            return RedirectToAction(nameof(Analista));
        }

        if (nuevoEstado == EstadoSolicitud.Rechazado && string.IsNullOrWhiteSpace(motivo))
        {
            TempData["Error"] = "El motivo es obligatorio para rechazar la solicitud.";
            return RedirectToAction(nameof(Analista));
        }

        if (nuevoEstado == EstadoSolicitud.Aprobado)
        {
            if (solicitud.MontoSolicitado > (solicitud.Cliente.IngresosMensuales * 5))
            {
                TempData["Error"] = "No se puede aprobar: El monto solicitado excede 5 veces los ingresos mensuales del cliente.";
                return RedirectToAction(nameof(Analista));
            }
        }

        solicitud.Estado = nuevoEstado;
        solicitud.MotivoRechazo = nuevoEstado == EstadoSolicitud.Rechazado ? motivo : null;

        await _context.SaveChangesAsync();

        await _cache.RemoveAsync($"Solicitudes_{solicitud.Cliente.UsuarioId}");

        TempData["Success"] = "Solicitud procesada con éxito.";
        return RedirectToAction(nameof(Analista));
    }
}