using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ControlCenter;
using ControlCenter.Models;
using ControlCenter.Services;
using Microsoft.Extensions.Options;
using Shared;
using Shared.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient<ControlByWebRelayController>(c => c.Timeout = TimeSpan.FromSeconds(30));

builder.Configuration.AddJsonFile("config.json", optional: false, reloadOnChange: true);
builder.Services.Configure<DoorsConfig>(builder.Configuration.GetSection("Doors"));
builder.Services.Configure<LockersConfig>(builder.Configuration.GetSection("Lockers"));
builder.Services.Configure<ProjectorsConfig>(builder.Configuration.GetSection("Projectors"));

builder.Services.AddTransient<ControlByWebRelayController>();

// Configure JSON serialization options
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new IpAddressJsonConverter());
    options.SerializerOptions.Converters.Add(new ProjectorStatusJsonConverter());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.MapPost("/doors/{doorNumber:int}/pulse", async (int doorNumber, ControlByWebRelayController controller) =>
{
    var result = await controller.PulseAsync(doorNumber);

    return result.Success
        ? Results.Ok(result)
        : Results.BadRequest(result);
});

app.MapPost("/doors/{doorNumber:int}/unlock", async (int doorNumber, ControlByWebRelayController controller) =>
{
    var result = await controller.OpenAsync(doorNumber);

    return result.Success
        ? Results.Ok(result)
        : Results.BadRequest(result);
});

app.MapPost("/doors/{doorNumber:int}/lock", async (int doorNumber, ControlByWebRelayController controller) =>
{
    var result = await controller.CloseAsync(doorNumber);

    return result.Success
        ? Results.Ok(result)
        : Results.BadRequest(result);
});

app.MapGet("/doors/status", async (ControlByWebRelayController controller) =>
{
    var result = await controller.StatusAsync();

    return result.Success
        ? Results.Ok(result)
        : Results.BadRequest(result);
});


app.MapPost("/lockers/{lockerNumber:int}/unlock", async (
        int lockerNumber,
        HttpClient httpClient,
        ILogger<Program> logger,
        IOptionsMonitor<LockersConfig> config,
        CancellationToken cancellationToken = default) =>
    {
        logger.LogInformation(
            "Received unlock request for locker {LockerNumber}, forwarding to {Host}",
            lockerNumber,
            config.CurrentValue.Host);

        var request = new LockerUnlockRequest(10, config.CurrentValue.SerialPorts);

        var response = await httpClient.PostAsJsonAsync(
            new Uri($"http://{config.CurrentValue.Host}/{lockerNumber}/unlock"),
            request,
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        logger.LogInformation(
            "Response from locker {LockerNumber} unlock request: {ResponseBody}",
            lockerNumber,
            body);

        var result = JsonSerializer.Deserialize<LockerStatusResult>(body);

        return response.IsSuccessStatusCode
            ? Results.Ok(result)
            : Results.BadRequest(result);
    })
    .WithName("UnlockLocker");

app.MapGet("/lockers/status", async (
    int lockerNumber,
    HttpClient httpClient,
    IOptionsMonitor<LockersConfig> config,
    CancellationToken cancellationToken = default) =>
{
    var json = JsonSerializer.Serialize(config.CurrentValue.SerialPorts);
    var content = new StringContent(json, Encoding.UTF8, "application/json");

    var response = await httpClient.PostAsync(
        new Uri($"http://{config.CurrentValue.Host}/{lockerNumber}/unlock"),
        content,
        cancellationToken);

    var body = await response.Content.ReadAsStringAsync(cancellationToken);

    var result = JsonSerializer.Deserialize<LockerStatusResponse>(body);

    return response.IsSuccessStatusCode
        ? Results.Ok(result)
        : Results.BadRequest(result);
});


app.MapPost("projectors/{projectorId:int}/on", async (
    int projectorId,
    IOptionsMonitor<ProjectorsConfig> projectors,
    CancellationToken cancellationToken = default) =>
{
    var projectorData = projectors.CurrentValue.Projectors.FirstOrDefault(p => p.Id == projectorId);
    if (projectorData == null)
    {
        return Results.NotFound();
    }

    var ipAddress = IPAddress.Parse(projectorData.IpAddress);
    var projector = ProjectorControlFactory.Create(
        ipAddress,
        projectorData.Protocol);

    var status = await projector.GetStatusAsync();

    if (status.Status == ProjectorStatusType.On)
    {
        return Results.Ok(status);
    }

    var result = await projector.OnAsync();

    return result.Success
        ? Results.Ok(result)
        : Results.BadRequest(result);
});

app.MapPost("projectors/{projectorId:int}/off", async (int projectorId, IOptionsMonitor<ProjectorsConfig> projectors) =>
{
    var projectorData = projectors.CurrentValue.Projectors.FirstOrDefault(p => p.Id == projectorId);
    if (projectorData == null)
    {
        return Results.NotFound();
    }

    var ipAddress = IPAddress.Parse(projectorData.IpAddress);
    var projector = ProjectorControlFactory.Create(
        ipAddress,
        projectorData.Protocol);

    var status = await projector.GetStatusAsync();

    if (status.Status == ProjectorStatusType.Off)
    {
        return Results.Ok(status);
    }

    var result = await projector.OffAsync();

    return result.Success
        ? Results.Ok(result)
        : Results.BadRequest(result);
});

app.MapGet("/projectors/status", async (IOptionsMonitor<ProjectorsConfig> projectors) =>
{
    var tasks = projectors.CurrentValue.Projectors
        .Select(async projector =>
        {
            var ip = IPAddress.Parse(projector.IpAddress);
            var control = ProjectorControlFactory.Create(ip, projector.Protocol);

            var result = await control.GetStatusAsync();

            return result;
        });

    var statusResults = await Task.WhenAll(tasks);

    return Results.Ok(new
    {
        Success = true,
        Projectors = statusResults
    });
});

var configPath = Path.GetFullPath("config.json");

app.MapGet("/config", async (CancellationToken cancellationToken = default) =>
{
    if (!File.Exists(configPath))
        return Results.NotFound("config.json not found.");

    var json = await File.ReadAllTextAsync(configPath, cancellationToken);
    var config = JsonSerializer.Deserialize<RootConfig>(json, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = false
    });

    return Results.Ok(config);
});

app.MapPost("/config", async (RootConfig updatedConfig, CancellationToken cancellationToken = default) =>
{
    var json = JsonSerializer.Serialize(updatedConfig, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
    });

    await File.WriteAllTextAsync(configPath, json, cancellationToken);

    return Results.Ok(new { success = true });
});


await app.RunAsync();

public record LockerUnlockRequest(
    int Duration,
    List<string> SerialPorts);