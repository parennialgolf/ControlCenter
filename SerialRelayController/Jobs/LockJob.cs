using Quartz;

namespace SerialRelayController.Jobs;

public class LockJob(PortController ports) : IJob
{
    public static JobKey JobKey
        => new("lock", "static-jobs");

    public static TriggerKey TriggerKey
        => new("lock-trigger", "static-jobs");

    public const string Description = "Locks the bay after the given delay.";

    public static readonly IScheduleBuilder Schedule = SimpleScheduleBuilder
        .Create()
        .WithIntervalInSeconds(15)
        .RepeatForever();
    
    public async Task Execute(IJobExecutionContext context)
    {
        var lockerNumber = context.MergedJobDataMap.GetInt("LockerNumber");
        Console.WriteLine($"🔒 Locking locker {lockerNumber}");
        var result = await ports.Lock(lockerNumber, []);

        Console.WriteLine(result.Success
            ? $"✅ Successfully locked locker {lockerNumber}"
            : $"⚠️ Failed to lock locker {lockerNumber}: {result.Error}");
    }
}