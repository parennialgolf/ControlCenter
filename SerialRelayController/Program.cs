using System.Text.Json.Serialization;
using Quartz;
using Quartz.AspNetCore;
using SerialRelayController;
using SerialRelayController.Jobs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<PortController>();
builder.Services.AddSingleton<LockerStateCache>();
builder.Services.AddTransient<SerialPorts>();
builder.Services.AddHostedService<ThreadPoolMonitor>();
builder.Services.AddHostedService<ResourceMonitor>();

builder.Services.AddQuartz(q =>
{
    q.UseInMemoryStore();
    // q
    //     .AddJob<LockJob>(j => j
    //         .StoreDurably()
    //         .WithIdentity(LockJob.JobKey)
    //         .WithDescription(LockJob.Description)
    //         .Build())
    //     .AddTrigger(t => t
    //         .ForJob(LockJob.JobKey)
    //         .WithIdentity(LockJob.TriggerKey)
    //         .WithSchedule(LockJob.Schedule)
    //         .StartNow());
});

builder.Services.AddQuartzServer(options =>
{
    options.WaitForJobsToComplete = true;
});

var app = builder.Build();

app.MapPost("{lockerNumber:int}/unlock", async (
        int lockerNumber,
        LockerUnlockRequest request,
        PortController relay) =>
    {
        try
        {
            Console.WriteLine(
                $"Locker number: {lockerNumber}, Duration: {request.Duration}, Ports: {string.Join(", ", request.SerialPorts)}");

            var result = await relay.Unlock(lockerNumber, request);

            Console.WriteLine(result.Success
                ? $"Successfully opened lockerNumber: {lockerNumber}"
                : $"Error opening lockerNumber: {lockerNumber}, {result.Error}");

            return result.Success
                ? Results.Ok(new LockerStatusResult(lockerNumber, LockerStatus.Unlocked))
                : Results.BadRequest(new LockerStatusResult(lockerNumber, LockerStatus.FailedToUnlock, result.Error));
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

app.MapPost("{lockerNumber:int}/status", (
        int lockerNumber,
        List<string> serialPorts,
        SerialPorts ports) =>
    {
        try
        {
            var statuses = ports.GetAllStatuses(serialPorts);

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

app.MapPost("status", (List<string> serialPorts, SerialPorts ports) =>
    {
        var statuses = ports.GetAllStatuses(serialPorts);

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

public record LockerUnlockRequest(
    int Duration,
    List<string> SerialPorts);

public record LockerStatusResponse(
    bool Success,
    List<LockerStatusResult> Data
);

public record LockerStatusResult(
    int LockerNumber,
    LockerStatus Status,
    string? Error = null);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LockerStatus
{
    Locked,
    Unlocked,
    FailedToUnlock,
    Unknown
}