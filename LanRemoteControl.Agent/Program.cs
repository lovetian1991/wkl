using System.Diagnostics;
using LanRemoteControl.Agent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

const string ServiceName = "wkl";

if (args.Length > 0)
{
    string command = args[0].ToLowerInvariant();

    if (command == "--install")
    {
        InstallService();
        return;
    }

    if (command == "--uninstall")
    {
        UninstallService();
        return;
    }

    Console.WriteLine($"Unknown argument: {args[0]}");
    Console.WriteLine("Usage:");
    Console.WriteLine("  --install    Install as Windows service");
    Console.WriteLine("  --uninstall  Uninstall Windows service");
    return;
}

// Run as Windows service host
var builder = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = ServiceName;
    })
    .ConfigureServices(services =>
    {
        services.AddSingleton<CrashRecoveryLogger>();
        services.AddHostedService<AgentService>();
    });

var host = builder.Build();
await host.RunAsync();

// ─── Service install / uninstall helpers ────────────────────────────

static void InstallService()
{
    string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName!;

    // Create the service
    int exitCode = RunScExe($"create {ServiceName} binPath= \"{exePath}\" start= auto");
    if (exitCode != 0)
    {
        Console.WriteLine($"Failed to install service '{ServiceName}'. sc.exe exit code: {exitCode}");
        return;
    }

    Console.WriteLine($"Service '{ServiceName}' installed successfully.");

    // Configure recovery: restart after 5 seconds for first 3 failures, reset counter after 24h
    int recoveryExitCode = RunScExe($"failure {ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/5000");
    if (recoveryExitCode != 0)
    {
        Console.WriteLine($"Warning: Failed to configure recovery policy. sc.exe exit code: {recoveryExitCode}");
    }
    else
    {
        Console.WriteLine("Recovery policy configured: restart after 5s for first 3 failures.");
    }
}

static void UninstallService()
{
    int exitCode = RunScExe($"delete {ServiceName}");
    if (exitCode != 0)
    {
        Console.WriteLine($"Failed to uninstall service '{ServiceName}'. sc.exe exit code: {exitCode}");
        return;
    }

    Console.WriteLine($"Service '{ServiceName}' uninstalled successfully.");
}

static int RunScExe(string arguments)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            Console.WriteLine("Failed to start sc.exe.");
            return -1;
        }

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!string.IsNullOrWhiteSpace(output))
            Console.WriteLine(output.TrimEnd());
        if (!string.IsNullOrWhiteSpace(error))
            Console.Error.WriteLine(error.TrimEnd());

        return process.ExitCode;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error running sc.exe: {ex.Message}");
        return -1;
    }
}
