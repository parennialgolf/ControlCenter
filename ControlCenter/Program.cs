using System.Net;
using ControlCenter.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to use port 5021
builder.WebHost.UseUrls("http://localhost:5022");

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

        // Send command (just mocked here)
        var result = await SerialRelayController.SendToSerialWithConfirmation(relay.SerialPort, relay.Channel);

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

app.MapPost("projectors/{projectorId:int}", async (int projectorId, ProjectorControlRequest request) =>
{
    var ipAddress = IPAddress.Parse(request.IpAddress);
    var projector = ProjectorControlFactory.Create(
        ipAddress,
        request.Protocol);

    var result = request.Status switch
    {
        ProjectorStatusType.On => await projector.OnAsync(),
        ProjectorStatusType.Off => await projector.OffAsync(),
        ProjectorStatusType.Failure => throw new NotImplementedException("Failure status not implemented"),
        _ => throw new ArgumentOutOfRangeException(nameof(request.Status))
    };

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