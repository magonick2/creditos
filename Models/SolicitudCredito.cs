using System.ComponentModel.DataAnnotations;

namespace PlataformaCreditos.Models;

public enum EstadoSolicitud { Pendiente, Aprobado, Rechazado }

public class SolicitudCredito
{
    public int Id { get; set; }
    
    public int ClienteId { get; set; }
    
    // Propiedad de navegación: se inicializa con null! para quitar la línea amarilla
    public Cliente Cliente { get; set; } = null!;
    
    public double MontoSolicitado { get; set; }
    
    public DateTime FechaSolicitud { get; set; } = DateTime.Now;
    
    public EstadoSolicitud Estado { get; set; } = EstadoSolicitud.Pendiente;
    
    // Esta sí puede ser nula, por eso usamos el signo ?
    public string? MotivoRechazo { get; set; }
}