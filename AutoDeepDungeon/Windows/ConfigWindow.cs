using AutoDeepDungeon.Configuration;
using AutoDeepDungeon.Data;
using Dalamud.Bindings.ImGui;
using ECommons.ImGuiMethods;
using System.Numerics;

namespace AutoDeepDungeon.Windows;

public static class ConfigWindow
{
    public static void Draw()
    {
        var c = Plugin.Config;

        DrawMasterToggle(c);
        ImGui.Spacing();

        if (ImGui.BeginTabBar("##adg_cfg_tabs"))
        {
            if (ImGui.BeginTabItem("General"))        { DrawGeneral(c);   ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Run goals"))      { DrawRunGoals(c);  ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Save file"))      { DrawSave(c);      ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Death"))          { DrawDeath(c);     ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Pomanders"))      { DrawPomanders(c); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Planner"))        { DrawPlanner(c);   ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Safety"))         { DrawSafety(c);    ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Diagnostics"))    { DrawDiag(c);      ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
    }

    private static void DrawMasterToggle(Config c)
    {
        var tosAccepted = c.ToSAccepted;
        if (!tosAccepted)
        {
            ImGui.TextColored(new Vector4(1.0f, 0.55f, 0.35f, 1.0f),
                "Master toggle disabled until you accept the risk modal.");
            if (ImGui.Button("Open risk acceptance"))
            {
                Plugin.OpenSafetyModal();
            }
            return;
        }

        var enabled = c.MasterEnabled;
        if (ImGui.Checkbox("Enable autopilot (session toggle; always off at plugin load)", ref enabled))
        {
            c.MasterEnabled = enabled;
        }
        ImGuiEx.HelpMarker(
            "Paranoid default: automation is off every time the plugin loads. " +
            "You must re-enable it each session.");
    }

    private static void DrawGeneral(Config c)
    {
        EnumCombo("Combat driver", ref c.CombatDriver);
        ImGuiEx.HelpMarker(
            "Auto-detect: uses whichever of RotationSolver or WrathCombo is installed. " +
            "If both are installed, prefers RotationSolver.");
    }

    private static void DrawRunGoals(Config c)
    {
        EnumCombo("Target dungeon", ref c.TargetDungeon);

        ImGui.SliderInt("Floor start", ref c.FloorStart, 1, 200);
        ImGui.SliderInt("Floor end",   ref c.FloorEnd,   1, 200);
        if (c.FloorEnd < c.FloorStart) c.FloorEnd = c.FloorStart;

        EnumCombo("Stop condition", ref c.StopCondition);
        if (c.StopCondition == StopCondition.NRuns)
        {
            ImGui.SliderInt("Stop after N runs", ref c.StopAfterRuns, 1, 100);
        }
    }

    private static void DrawSave(Config c)
    {
        EnumCombo("Save file behavior", ref c.SaveBehavior);
        ImGuiEx.HelpMarker(
            "New: discard existing save and start a fresh run. " +
            "Continue: resume the most recent save. " +
            "Interactive: pause and wait for you to choose.");
    }

    private static void DrawDeath(Config c)
    {
        EnumCombo("On death", ref c.OnDeath);
        ImGui.SliderInt("Requeue delay (seconds)", ref c.RequeueDelaySeconds, 0, 30);
    }

    private static void DrawPomanders(Config c)
    {
        EnumCombo("Accursed Hoard", ref c.Hoard);
        EnumCombo("Magicite",       ref c.Magicite);
        EnumCombo("Raising pomander", ref c.Raising);
        ImGui.Checkbox("Conservative mode (pop defensives one floor early)", ref c.ConservativeMode);
    }

    private static void DrawPlanner(Config c)
    {
        ImGui.SliderInt("Coffer detour tolerance (yalms)", ref c.CofferDetourYalms, 0, 50);
        ImGuiEx.HelpMarker("0 = speed-only (ignore coffers). 50 = collect everything.");

        EnumCombo("Kill for aetherpool", ref c.KillForAetherpool);
        EnumCombo("Multi-pull tolerance", ref c.MultiPullTolerance);
    }

    private static void DrawSafety(Config c)
    {
        ImGui.SliderInt("HP emergency threshold (%)", ref c.HpEmergencyThresholdPct, 10, 30);
        ImGui.SliderInt("Stuck timeout (seconds)",    ref c.StuckTimeoutSeconds,     15, 120);
        ImGui.Checkbox("Auto-stop on unexpected state", ref c.AutoStopOnUnexpectedState);
    }

    private static void DrawDiag(Config c)
    {
        ImGui.Checkbox("Write death log",    ref c.EnableDeathLog);
        ImGui.Checkbox("Write run log",      ref c.EnableRunLog);
        ImGui.Checkbox("Debug overlay (planned path, aggro cones, goal marker)", ref c.EnableDebugOverlay);
    }

    private static void EnumCombo<T>(string label, ref T value) where T : struct, System.Enum
    {
        var names = System.Enum.GetNames<T>();
        var values = System.Enum.GetValues<T>();
        var idx = System.Array.IndexOf(values, value);
        if (idx < 0) idx = 0;
        if (ImGui.Combo(label, ref idx, names, names.Length))
        {
            value = values[idx];
        }
    }
}
