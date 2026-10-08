using DesktopAssistant.Automation;
using DesktopAssistant.Configuration;
using DesktopAssistant.AI;

namespace DesktopAssistant.Infrastructure;

public sealed class AppServices
{
    public UserSettings Settings { get; } = SettingsStore.Load();
    public ApplicationLauncher Launcher { get; } = new();
    public DesktopUiAutomation UiAutomation { get; } = new();
    public ActionSafety Safety { get; } = new();
    public IAiProvider AiProvider { get; }
    public AgentToolRegistry AgentTools { get; }
    public GeminiAgentLoop Agent { get; }
    public AppServices()
    {
        AiProvider = new GeminiAiProvider(() => Settings);
        AgentTools = DesktopAgentTools.Create(Launcher, UiAutomation, (GeminiAiProvider)AiProvider);
        Agent = new GeminiAgentLoop((IAgentModel)AiProvider, AgentTools);
    }
}
