using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlataformaCreditos.Data;
using PlataformaCreditos.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.Extensions.Caching.Distributed; // Nuevo
using System.Text.Json; // Nuevo

namespace PlataformaCreditos.Controllers;

[Authorize]
public class SolicitudesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IDistributedCache _cache; // Inyectado para Pregunta 4

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
        
        // --- LÓGICA DE CACHÉ (PREGUNTA 4) ---
        string cacheKey = $"Solicitudes_{userId}";
        List<SolicitudCredito>? solicitudes;
        
        // Intentar obtener de Redis
        var cachedData = await _cache.GetStringAsync(cacheKey);
        
        if (cachedData != null)
        {
            solicitudes = JsonSerializer.Deserialize<List<SolicitudCredito>>(cachedData);
        }
        else
        {
            // Si no hay caché, hacemos la consulta a la DB
            var query = _context.Solicitudes
                .Include(s => s.Cliente)
                .Where(s => s.Cliente.UsuarioId == userId);

            // Validaciones de montos y fechas
            if (montoMin < 0 || montoMax < 0) ModelState.AddModelError("", "Los montos no pueden ser negativos.");
            if (montoMin.HasValue && montoMax.HasValue && montoMax < montoMin) ModelState.AddModelError("", "Rango de montos inválido.");
            if (fechaInicio.HasValue && fechaFin.HasValue && fechaInicio > fechaFin) ModelState.AddModelError("", "Rango de fechas inválido.");

            if (!ModelState.IsValid) return View(new List<SolicitudCredito>());

            // Filtros
            if (!string.IsNullOrEmpty(estado) && Enum.TryParse(typeof(EstadoSolicitud), estado, out var estadoEnum))
                query = query.Where(s => s.Estado == (EstadoSolicitud)estadoEnum);

            if (montoMin.HasValue) query = query.Where(s => s.MontoSolicitado >= montoMin.Value);
            if (montoMax.HasValue) query = query.Where(s => s.MontoSolicitado <= montoMax.Value);
            if (fechaInicio.HasValue) query = query.Where(s => s.FechaSolicitud >= fechaInicio.Value);
            if (fechaFin.HasValue) query = query.Where(s => s.FechaSolicitud <= fechaFin.Value);

            solicitudes = await query.ToListAsync();

            // Guardar en Redis por 60 segundos
            var cacheOptions = new DistributedCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromSeconds(60));
            
            await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(solicitudes), cacheOptions);
        }

        return View(solicitudes);
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
        ModelState.Remove("Cliente");
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var cliente = await _context.Clientes.FirstOrDefaultAsync(c => c.UsuarioId == userId);

        if (cliente == null)
        {
            ModelState.AddModelError("", "Perfil de cliente no encontrado.");
            return View(solicitud);
        }

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

            // --- INVALIDAR CACHÉ (PREGUNTA 4) ---
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

        // --- SESIÓN REDIS-BACKED (PREGUNTA 4) ---
        // Guardamos los datos para el Layout
        HttpContext.Session.SetString("UltimaId", solicitud.Id.ToString());
        HttpContext.Session.SetString("UltimaMonto", solicitud.MontoSolicitado.ToString());

        return View(solicitud);
    }
    [Authorize(Roles = "Analista")]
[HttpPost]
public async Task<IActionResult> ProcesarSolicitud(int id, EstadoSolicitud nuevoEstado, string? motivo)
{
    var solicitud = await _context.Solicitudes.Include(s => s.Cliente).FirstOrDefaultAsync(x => x.Id == id);
    if (solicitud == null) return NotFound();

    solicitud.Estado = nuevoEstado;
    solicitud.MotivoRechazo = motivo;

    await _context.SaveChangesAsync();

    // INVALIDAR CACHÉ DEL CLIENTE (No del analista)
    await _cache.RemoveAsync($"Solicitudes_{solicitud.Cliente.UsuarioId}");

    return RedirectToAction("IndexAnalista");
}
}