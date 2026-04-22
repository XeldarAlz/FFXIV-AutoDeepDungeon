using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Numerics;

namespace AutoDeepDungeon.Windows;

public sealed class SafetyModal : Window
{
    private const string Title = "AutoDeepDungeon — Acceptance of risk";

    public SafetyModal()
        : base(Title, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize)
    {
        RespectCloseHotkey = false;
        AllowPinning = false;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 0),
            MaximumSize = new Vector2(640, float.MaxValue),
        };
    }

    public override void Draw()
    {
        ImGui.TextColored(new Vector4(1.0f, 0.35f, 0.35f, 1.0f), "Read before enabling automation.");
        ImGui.Separator();

        ImGui.TextWrapped(
            "AutoDeepDungeon automates FFXIV gameplay inside Deep Dungeons. " +
            "Automation violates the FFXIV Terms of Service. Using this plugin " +
            "can result in account suspension, permanent ban, or loss of progress.");
        ImGui.Spacing();
        ImGui.TextWrapped(
            "The plugin makes a best effort to play safely but it cannot guarantee " +
            "it will not get your character killed, stuck, or into a state from " +
            "which it cannot recover.");
        ImGui.Spacing();
        ImGui.TextWrapped(
            "You are solely responsible for using this tool. There is no warranty, " +
            "no support line, and no recovery help from Square Enix if something goes wrong.");
        ImGui.Spacing();
        ImGui.Separator();

        if (ImGui.Button("I understand and accept the risk"))
        {
            Plugin.Config.ToSAccepted = true;
            Plugin.SaveConfig();
            IsOpen = false;
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            IsOpen = false;
        }
    }
}
