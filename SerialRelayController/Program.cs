using Shared.Services;

var builder = WebApplication.CreateBuilder(args);

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

    return Results.Ok(new LockerStatusResponse(
        true,
        [.. statuses.Select(s => new LockerStatusResult(
            s.LockerNumber,
            s.IsOn
            ? LockerStatus.Unlocked
            : LockerStatus.Locked))]
    ));
});

await app.RunAsync();