using Quartz;
using SerialRelayController;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTransient<SerialRelayController.SerialRelayController>();
builder.Services.AddSingleton<LockerStateCache>();

// Quartz with SQLite
builder.Services.AddQuartz(q =>
{
    // Persistent job store in SQLite
    q.UsePersistentStore(store =>
    {
        store.UseProperties = true;
#pragma warning disable CS0618
        store.UseJsonSerializer(); // System.Text.Json, safe for primitives
#pragma warning restore CS0618

        store.UseSQLite(sqlite => { sqlite.ConnectionString = "Data Source=/home/user/quartz.db;"; });
    });
});

builder.Services.AddQuartzHostedService(opt => { opt.WaitForJobsToComplete = true; });

var app = builder.Build();

app.MapPost("/lockers/{lockerNumber:int}/unlock", async (
        int lockerNumber,
        SerialRelayController.SerialRelayController relay) =>
    {
        try
        {
            var result = await relay.Unlock(lockerNumber);

            Console.WriteLine(result.Success
                ? $"Successfully opened lockerNumber: {lockerNumber}"
                : $"Error opening lockerNumber: {lockerNumber}, {result.Error}");

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

app.MapGet("/lockers/{lockerNumber:int}/status",
        (int lockerNumber, SerialRelayController.SerialRelayController relay) =>
        {
            try
            {
                var statuses = relay.GetAllStatuses();

                var lockerStatus = statuses.FirstOrDefault(s => s.LockerNumber == lockerNumber);

                if (lockerStatus == null)
                {
                    return Results.NotFound(new
                    {
                        lockerNumber,
                        status = "Unknown",
                        error = $"No status available for locker {lockerNumber}"
                    });
                }

                return Results.Ok(new
                {
                    lockerNumber,
                    status = lockerStatus.IsOn ? "Unlocked" : "Locked"
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
    .WithName("LockerStatus");

app.MapGet("/lockers/status", (SerialRelayController.SerialRelayController relay) =>
    {
        var statuses = relay.GetAllStatuses();

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