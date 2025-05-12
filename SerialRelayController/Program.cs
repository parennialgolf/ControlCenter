using Shared.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to use port 5021
builder.WebHost.UseUrls("http://localhost:5001");

var app = builder.Build();

app.UseHttpsRedirection();

app.MapPost("/lockers/{lockerNumber:int}/unlock", async (int lockerNumber) =>
    {
        var relay = SerialRelayController.GetRelay(lockerNumber);
        
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

await app.RunAsync();