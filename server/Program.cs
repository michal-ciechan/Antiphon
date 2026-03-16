var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

// SPA fallback for production (serves React build from wwwroot)
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();
