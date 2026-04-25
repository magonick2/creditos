namespace PlataformaCreditos.Models;

public class Cliente
{
    public int Id { get; set; }
    
    // Al usar null! le decimos al compilador que EF Core se encargará de llenarlo
    public string UsuarioId { get; set; } = null!;
    
    public double IngresosMensuales { get; set; }
    
    public bool Activo { get; set; } = true;
}