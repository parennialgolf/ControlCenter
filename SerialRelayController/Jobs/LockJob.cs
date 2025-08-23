using System.IO.Ports;
using Quartz;

namespace SerialRelayController.Jobs;

/// <summary>
/// Quartz job that sends the OFF command to a relay, marking a locker as locked again.
/// </summary>
public class LockJob(SerialRelayController controller) : IJob
{
    private const string Key = "LockerNumber";

    /// <summary>
    /// Build a Quartz JobDetail for a given locker number.
    /// </summary>
    public static IJobDetail BuildJob()
    {
        return JobBuilder.Create<LockJob>()
            .WithIdentity($"lock-job", "lockers")
            .Build();
    }

    /// <summary>
    /// Build a Quartz Trigger for the relock job with a given delay.
    /// </summary>
    public static ITrigger BuildTrigger(int lockerNumber, int delaySeconds = 10)
    {
        return TriggerBuilder.Create()
            .WithIdentity($"lock-trigger-{lockerNumber}", "lockers")
            .UsingJobData(Key, lockerNumber.ToString())
            .StartAt(DateBuilder.FutureDate(delaySeconds, IntervalUnit.Second))
            .Build();
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var lockerNumber = context.MergedJobDataMap.GetInt(Key);
        await controller.Lock(lockerNumber);
    }
}