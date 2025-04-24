using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shared.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to use port 5021
builder.WebHost.UseUrls("http://localhost:5021");

// Configure JSON serialization options
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new IPAddressJsonConverter());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();

app.MapPost("/doors/{doorNumber:int}/unlock", async (int doorNumber) =>
{
    var result = await IpRelayController.TriggerDoorAsync(doorNumber);

    return result.Success
        ? Results.Ok(result)
        : Results.BadRequest(result);
});

app.MapGet("/doors/status", async () =>
{
    var result = await IpRelayController.GetRelayStatusAsync();

    return result.Success
        ? Results.Ok(new
        {
            result.Success,
            result.RawResponse,
            Doors = result.Doors!.Select(d => new
            {
                d.DoorNumber,
                Status = d.IsOpen ? "UNLOCKED" : "LOCKED"
            })
        })
        : Results.BadRequest(new
        {
            result.Success,
            result.Error
        });
});


app.MapPost("/lockers/{lockerNumber:int}/unlock", async (int lockerNumber) =>
    {
        var relay = SerialRelayController.GetRelay(lockerNumber);
        
        // curl -X POST http://10.1.10.150:5000/unlock \
        // -H "Content-Type: application/json" \
        // -d '{"locker_number": 10}'
        
        // var result = await HttpClient
        
        // var result = await SerialRelayController.SendToSerialWithConfirmation(relay.SerialPort, relay.Channel);

        return result.Success
            ? Results.Ok(result)
            : Results.BadRequest(result);
    })
    .WithName("UnlockLocker");

app.MapGet("/lockers/status", () =>
{
    var statuses = SerialRelayController.GetAllStatuses();

    return Results.Ok(new
    {
        success = true,
        lockers = statuses.Select(s => new
        {
            s.LockerNumber,
            status = s.IsOn ? "UNLOCKED" : "LOCKED"
        })
    });
});

app.MapPost("projectors/{projectorId:int}/on", async (int projectorId) =>
{
    var projectorData = ProjectorRegistry.Projectors.FirstOrDefault(p => p.Id == projectorId);
    if (projectorData == null)
    {
        return Results.NotFound();
    }

    var ipAddress = IPAddress.Parse(projectorData.Ip);
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

app.MapPost("projectors/{projectorId:int}/off", async (int projectorId) =>
{
    var projectorData = ProjectorRegistry.Projectors.FirstOrDefault(p => p.Id == projectorId);
    if (projectorData == null)
    {
        return Results.NotFound();
    }

    var ipAddress = IPAddress.Parse(projectorData.Ip);
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

app.MapGet("/projectors/status", async () =>
{
    var tasks = ProjectorRegistry.Projectors
        .Select(async projector =>
        {
            var ip = IPAddress.Parse(projector.Ip);
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


await app.RunAsync();


// public enum ProjectorStatus
public record ProjectorControlRequest(
    string IpAddress,
    ProjectorProtocolType Protocol,
    ProjectorStatusType Status);

public static class ProjectorRegistry
{
    public static readonly List<RegisteredProjector> Projectors =
    [
        new(1, "Bay 1", "10.1.10.122", ProjectorProtocolType.Rs232),
        new(2, "Bay 2", "10.1.10.138", ProjectorProtocolType.Rs232),
        new(3, "Bay 3", "10.1.10.98", ProjectorProtocolType.Rs232),
        new(4, "Bay 4", "10.1.10.60", ProjectorProtocolType.Rs232),
        new(5, "Bay 5", "10.1.10.88", ProjectorProtocolType.Rs232)
    ];
}

public record RegisteredProjector(
    int Id,
    string Name,
    string Ip,
    ProjectorProtocolType Protocol);

// IPAddress JSON converter
public class IPAddressJsonConverter : JsonConverter<IPAddress>
{
    public override IPAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var ipString = reader.GetString();
        return IPAddress.Parse(ipString!);
    }

    public override void Write(Utf8JsonWriter writer, IPAddress value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}