using System.Net;
using System.Text.Json;
using ControlCenter;
using ControlCenter.Models;
using ControlCenter.Services;
using Microsoft.Extensions.Options;

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

        var response = await httpClient.PostAsJsonAsync(
            new Uri($"http://{config.CurrentValue.Host}/{lockerNumber}/unlock"),
            new LockerUnlockRequest(10, config.CurrentValue.SerialPorts),
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

app.MapGet("/lockers/{lockerNumber}/status", async (
    string lockerNumber,
    HttpClient httpClient,
    IOptionsMonitor<LockersConfig> config,
    CancellationToken cancellationToken = default) =>
{
    var response = await httpClient.PostAsJsonAsync(
        new Uri($"http://{config.CurrentValue.Host}/{lockerNumber}/status"),
        config.CurrentValue.SerialPorts,
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
    if (projectorData == null) return Results.NotFound();

    var ipAddress = IPAddress.Parse(projectorData.IpAddress);
    var projector = ProjectorControlFactory.Create(
        ipAddress,
        projectorData.Protocol);

    var status = await projector.GetStatusAsync();

    if (status.Status == ProjectorStatusType.On)
        return Results.Ok(new ProjectorStatusResult(true, status.Status, status.Message));

    var result = await projector.OnAsync();

    return result.Success
        ? Results.Ok(new ProjectorStatusResult(true, result.Status, result.Message))
        : Results.BadRequest(new ProjectorStatusResult(false, result.Status, result.Message));
});

app.MapPost("projectors/{projectorId:int}/off", async (int projectorId, IOptionsMonitor<ProjectorsConfig> projectors) =>
{
    var projectorData = projectors.CurrentValue.Projectors.FirstOrDefault(p => p.Id == projectorId);
    if (projectorData == null) return Results.NotFound();

    var ipAddress = IPAddress.Parse(projectorData.IpAddress);
    var projector = ProjectorControlFactory.Create(
        ipAddress,
        projectorData.Protocol);

    var status = await projector.GetStatusAsync();

    if (status.Status == ProjectorStatusType.Off)
        return Results.Ok(new ProjectorStatusResult(true, status.Status, status.Message));

    var result = await projector.OffAsync();

    return result.Success
        ? Results.Ok(new ProjectorStatusResult(true, result.Status, result.Message))
        : Results.BadRequest(new ProjectorStatusResult(false, result.Status, result.Message));
});

app.MapGet("/projectors/{projectorId:int}/status",
    async (int projectorId,
        IOptionsMonitor<ProjectorsConfig> projectors) =>
    {
        var projector = projectors.CurrentValue.Projectors.FirstOrDefault(p => p.Id == projectorId);

        if (projector is null)
            return Results.Ok(new ProjectorStatusResult(
                false,
                ProjectorStatusType.Unknown,
                "No projector found with the given ID"));

        var controller = ProjectorControlFactory.Create(
            IPAddress.Parse(projector.IpAddress),
            projector.Protocol);

        var result = await controller.GetStatusAsync();

        return Results.Ok(new ProjectorStatusResult(true, result.Status, result.Message));
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

    return Results.Ok(new ProjectorStatusResultList(
        true,
        statusResults
            .Select(r => new ProjectorStatusResult(r.Success, r.Status, r.Message))
            .ToList()));
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

internal record LockerUnlockRequest(
    int Duration,
    List<string> SerialPorts);

internal record ProjectorStatusResult(
    bool Success,
    ProjectorStatusType Status,
    string? Message);

internal record ProjectorStatusResultList(
    bool Success,
    List<ProjectorStatusResult> Results);