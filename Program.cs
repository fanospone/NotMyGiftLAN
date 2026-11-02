using NotMyGiftLAN.Hubs;
using NotMyGiftLAN.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5080");
builder.Services.AddSignalR();
builder.Services.AddSingleton<GameStore>();
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHub<GameHub>("/gamehub");
app.Run();
