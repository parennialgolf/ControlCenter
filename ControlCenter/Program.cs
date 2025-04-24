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


await app.RunAsync();