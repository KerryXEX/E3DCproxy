using System.Text.Json;
using System.Text.Json.Serialization;
using E3dcproxy.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<E3DC>();
builder.Services.Configure<JsonOptions>(opt =>
{
    opt.SerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure WebHost   
builder.WebHost.ConfigureKestrel(opt =>
{
    opt.ListenAnyIP(5033);
    //opt.ListenAnyIP(9100, listOpt => { listOpt.UseHttps("./zepassess.pfx", "Stop.4pik"); });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();


//### Add routes for E3DC calls                                
app.MapGet("/", async (E3DC myE3DC, HttpRequest request) =>
{
    if (!myE3DC.isAuthorized(request)) return "Unauthorized";
    return JsonSerializer.Serialize(await myE3DC.GetInfo());
});

app.MapGet("/canConnect", async (E3DC myE3DC, HttpRequest request) =>
{
    if (!myE3DC.isAuthorized(request)) return "Unauthorized";
    return JsonSerializer.Serialize(await myE3DC.Connect());
});

app.MapGet("/getStates", async (E3DC myE3DC, HttpRequest request) =>
{
    if (!myE3DC.isAuthorized(request)) return "Unauthorized";
    return JsonSerializer.Serialize(await myE3DC.GetStates());
});

app.MapGet("/getHistSumStates", async (E3DC myE3DC, HttpRequest request) =>
{
    if (!myE3DC.isAuthorized(request)) return "Unauthorized";
    return JsonSerializer.Serialize(await myE3DC.GetHistSumStates());
});

app.MapGet("/getHistory", async (E3DC myE3DC, HttpRequest request, int? startDate, int? days, int? interval) =>
{
    if (!myE3DC.isAuthorized(request)) return "Unauthorized";

    DateTime start = startDate.HasValue
        ? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(startDate.Value)
        : DateTime.Today;
    int myDays = days ?? 1;
    int myInterval = interval ?? 900;
    return JsonSerializer.Serialize(await myE3DC.GetHistory(start, myDays, myInterval));
});

app.MapGet("/getBatteries", async (E3DC myE3DC, HttpRequest request) =>
{
    if (!myE3DC.isAuthorized(request)) return "Unauthorized";
    return JsonSerializer.Serialize(await myE3DC.GetBatteries());
});


app.Run();