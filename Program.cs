using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PlataformaCreditos.Data;
using PlataformaCreditos.Models;

var builder = WebApplication.CreateBuilder(args);

// Configurar la conexión a SQLite
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// --- CONFIGURACIÓN DE REDIS Y SESIÓN (PREGUNTA 4) ---
// 1. Configurar Caché Distribuido con Redis
var redisConfig = builder.Configuration["Redis__ConnectionString"] 
                  ?? builder.Configuration.GetConnectionString("RedisConnection") 
                  ?? "localhost:6379";

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConfig;
    options.InstanceName = "PlataformaCreditos_";
});

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Necesario para acceder a la sesión desde el Layout (_Layout.cshtml)
builder.Services.AddHttpContextAccessor();
// ---------------------------------------------------

// Configurar Identity con Roles
builder.Services.AddDefaultIdentity<IdentityUser>(options => {
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 4;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
})
.AddRoles<IdentityRole>() 
.AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddControllersWithViews();

var app = builder.Build();

// --- INICIO SEED DATA ---
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<ApplicationDbContext>();
    var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

    context.Database.Migrate();

    if (!await roleManager.RoleExistsAsync("Analista"))
    {
        await roleManager.CreateAsync(new IdentityRole("Analista"));
    }

    var analistaEmail = "analista@test.com";
    if (await userManager.FindByEmailAsync(analistaEmail) == null)
    {
        var user = new IdentityUser { UserName = analistaEmail, Email = analistaEmail, EmailConfirmed = true };
        await userManager.CreateAsync(user, "Admin123!");
        await userManager.AddToRoleAsync(user, "Analista");
    }

    if (!context.Clientes.Any())
    {
        var cliente1 = new Cliente { UsuarioId = Guid.NewGuid().ToString(), IngresosMensuales = 5000, Activo = true };
        var cliente2 = new Cliente { UsuarioId = Guid.NewGuid().ToString(), IngresosMensuales = 3000, Activo = true };
        context.Clientes.AddRange(cliente1, cliente2);
        await context.SaveChangesAsync();

        context.Solicitudes.AddRange(
            new SolicitudCredito { ClienteId = cliente1.Id, MontoSolicitado = 10000, Estado = EstadoSolicitud.Pendiente, FechaSolicitud = DateTime.Now },
            new SolicitudCredito { ClienteId = cliente2.Id, MontoSolicitado = 5000, Estado = EstadoSolicitud.Aprobado, FechaSolicitud = DateTime.Now.AddDays(-2) }
        );
        await context.SaveChangesAsync();
    }

    var todosLosUsuarios = userManager.Users.ToList();
    foreach (var u in todosLosUsuarios)
    {
        if (!context.Clientes.Any(c => c.UsuarioId == u.Id))
        {
            context.Clientes.Add(new Cliente 
            { 
                UsuarioId = u.Id, 
                IngresosMensuales = 10000, 
                Activo = true 
            });
        }
    }
    await context.SaveChangesAsync();
}
// --- FIN SEED DATA ---

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// --- ACTIVAR SESIÓN (IMPORTANTE: Después de UseRouting y antes de UseAuthorization) ---
app.UseSession();
// -------------------------------------------------------------------------------------

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

app.Run();