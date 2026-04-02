namespace Maui.CodePush;

public class CodePushOptions
{
    public string? ServerUrl { get; set; }

    public string? AppId { get; set; }

    public string? AppToken { get; set; }

    public string? ReleaseVersion { get; set; }

    public string Channel { get; set; } = "production";

    public bool CheckOnStartup { get; set; } = true;

    public UpdatePolicy UpdatePolicy { get; set; } = UpdatePolicy.OnNextRestart;

    internal AssemblyRegister Register { get; } = new();

    public void AddModule(string assemblyName)
    {
        Register.AddAssembly(assemblyName);
    }
}

public enum UpdatePolicy
{
    /// <summary>
    /// Download now, apply on next cold start.
    /// </summary>
    OnNextRestart,

    /// <summary>
    /// Download and force restart.
    /// </summary>
    Immediate,

    /// <summary>
    /// Download then prompt user to restart.
    /// </summary>
    Prompt
}
