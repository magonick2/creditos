using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation; // Necesario para [ValidateNever]

namespace PlataformaCreditos.Models;

public enum EstadoSolicitud { Pendiente, Aprobado, Rechazado }

public class SolicitudCredito
{
    public int Id { get; set; }
    
    public int ClienteId { get; set; }
    
    // [ValidateNever] evita que el formulario exija este objeto al crear
    [ValidateNever]
    public Cliente Cliente { get; set; } = null!;
    
    public double MontoSolicitado { get; set; }
    
    public DateTime FechaSolicitud { get; set; } = DateTime.Now;
    
    public EstadoSolicitud Estado { get; set; } = EstadoSolicitud.Pendiente;
    
    public string? MotivoRechazo { get; set; }
}