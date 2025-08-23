using Shared.Services;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapPost("/lockers/{lockerNumber:int}/unlock", async (int lockerNumber) =>
    {
        try
        {
            var relay = SerialRelayController.GetRelay(lockerNumber);
            var result = await SerialRelayController.SendToSerialWithConfirmation(relay.SerialPort, relay.Channel);

            return result.Success
                ? Results.Ok(new
                {
                    lockerNumber,
                    status = "Unlocked",
                    response = result.StatusResponse
                })
                : Results.BadRequest(new
                {
                    lockerNumber,
                    status = "Failed",
                    error = result.Error,
                    response = result.StatusResponse
                });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new
            {
                lockerNumber,
                status = "Failed",
                error = ex.Message
            });
        }
    })
    .WithName("UnlockLocker")
    .WithDescription(
        "Unlocks a locker by sending ON, verifying status, then OFF. Returns 200 OK if the relay reports ON, otherwise 400.");

app.MapGet("/lockers/status", () =>
    {
        var statuses = SerialRelayController.GetAllStatuses();

        return Results.Ok(new LockerStatusResponse(
            true,
            statuses.Select(s => new LockerStatusResult(
                s.LockerNumber,
                s.IsOn ? LockerStatus.Unlocked : LockerStatus.Locked
            )).ToList()
        ));
    })
    .WithDescription("Returns the status of all lockers across all relay boards.");

await app.RunAsync();