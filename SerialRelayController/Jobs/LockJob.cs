using System.IO.Ports;
using Quartz;

namespace SerialRelayController.Jobs;

/// <summary>
/// Quartz job that sends the OFF command to a relay, marking a locker as locked again.
/// </summary>
public class LockJob(LockerStateCache cache) : IJob
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
            .UsingJobData(Key, lockerNumber)
            .StartAt(DateBuilder.FutureDate(delaySeconds, IntervalUnit.Second))
            .Build();
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var lockerNumber = context.MergedJobDataMap.GetInt(Key);

        try
        {
            var relay = SerialRelayController.GetRelay(lockerNumber);
            var command = SerialRelayCommands.GetCommand(relay.Channel);
            if (command == null)
            {
                Console.WriteLine($"⚠️ No OFF command defined for locker {lockerNumber}");
                return;
            }

            var offCmd = command.Off.Replace("\\r", "\r").Replace("\\n", "\n");

            using var serialPort = new SerialPort(relay.SerialPort, 9600, Parity.None, 8, StopBits.One);
            serialPort.WriteTimeout = 2000;
            serialPort.NewLine = "\r\n";
            serialPort.Open();
            serialPort.Write(offCmd);

            cache.MarkLocked(lockerNumber);

            Console.WriteLine($"✅ Relocked locker {lockerNumber}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Failed to relock locker {lockerNumber}: {ex.Message}");
        }

        await Task.CompletedTask;
    }
}