namespace Game.Api.Application;

public class RecoveryOptions
{
    public const string SectionName = "Recovery";

    public bool Enabled { get; set; } = true;
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan GracePeriod { get; set; } = TimeSpan.FromSeconds(30);
    public int WarnAfterAttempts { get; set; } = 5;
    public int BatchSize { get; set; } = 50;
}
