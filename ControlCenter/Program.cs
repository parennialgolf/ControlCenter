using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Shared;
using Shared.Models;
using Shared.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to use port 5021
builder.WebHost.UseUrls($"http://localhost:{builder.Configuration.GetValue<int>("CONTROL_CENTER_PORT")}");

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
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// app.UseHttpsRedirection();

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
        IOptionsMonitor<LockersConfig> lockerConfig,
        IConfiguration config,
        ILogger<Program> logger) =>
    {
        if (lockerConfig.CurrentValue.UseLegacy)
        {
            // curl -X POST http://10.1.10.150:5000/unlock \
            // -H "Content-Type: application/json" \
            // -d '{"locker_number": 10}'

            logger.LogInformation("Unlocking locker {LockerNumber} using legacy API", lockerNumber);

            var response = await httpClient.PostAsync(
                $"http://{config.GetValue<string>("SERIAL_RELAY_CONTROLLER_HOST")}:{config.GetValue<int>("SERIAL_RELAY_CONTROLLER_PORT")}/unlock",
                new StringContent(
                    JsonSerializer.Serialize(new { locker_number = lockerNumber }),
                    Encoding.UTF8,
                    "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                return Results.BadRequest(new { success = false, error = await response.Content.ReadAsStringAsync() });
            }

            var result =
                JsonSerializer.Deserialize<LockerPassthroughResult>(await response.Content.ReadAsStringAsync());

            return Results.Ok(result);
        }
        else
        {
            var forwardUrl =
                $"http://{config.GetValue<string>("SERIAL_RELAY_CONTROLLER_HOST")}:{config.GetValue<int>("SERIAL_RELAY_CONTROLLER_PORT")}/lockers/{lockerNumber}/unlock";

            var response = await httpClient.PostAsync(forwardUrl, null);
            var body = await response.Content.ReadAsStringAsync();

            var result = JsonSerializer.Deserialize<SerialCommandResult>(body);

            return response.IsSuccessStatusCode
                ? Results.Ok(result)
                : Results.BadRequest(result);
        }
    })
    .WithName("UnlockLocker");

app.MapGet("/lockers/status", async (
    int lockerNumber,
    HttpClient httpClient,
    IConfiguration config) =>
{
    if (config.GetValue<bool>("USE_LEGACY_LOCKER_API"))
    {
        return Results.StatusCode(StatusCodes.Status501NotImplemented);
    }

    var forwardUrl =
        $"http://{config.GetValue<string>("SERIAL_RELAY_CONTROLLER_HOST")}:{config.GetValue<int>("SERIAL_RELAY_CONTROLLER_PORT")}/lockers/status";

    var response = await httpClient.GetAsync(forwardUrl);

    if (!response.IsSuccessStatusCode)
    {
        return Results.BadRequest(new { success = false, error = await response.Content.ReadAsStringAsync() });
    }

    var body = await response.Content.ReadAsStringAsync();

    var result = JsonSerializer.Deserialize<LockerStatusResponse>(body);

    return Results.Ok(result);
});


app.MapPost("projectors/{projectorId:int}/on", async (int projectorId, IOptionsMonitor<ProjectorsConfig> projectors) =>
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

app.MapGet("/config", async () =>
{
    if (!File.Exists(configPath))
        return Results.NotFound("config.json not found.");

    var json = await File.ReadAllTextAsync(configPath);
    var config = JsonSerializer.Deserialize<RootConfig>(json, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = false
    });

    return Results.Ok(config);
});

app.MapPost("/config", async (RootConfig updatedConfig) =>
{
    var json = JsonSerializer.Serialize(updatedConfig, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
    });

    await File.WriteAllTextAsync(configPath, json);

    return Results.Ok(new { success = true });
});



await app.RunAsync();

public record LockerPassthroughResult
{
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
};