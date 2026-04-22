using System;
using System.Numerics;
using AutoDeepDungeon.Helpers;
using AutoDeepDungeon.IPC;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;

namespace AutoDeepDungeon.Windows;

public sealed class DebugWindow : Window
{
    public DebugWindow()
        : base("AutoDeepDungeon — Debug")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 200),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        DrawLifecycle();
        ImGui.Separator();

        if (ImGui.CollapsingHeader("IPC readiness", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawIpcRow(Plugin.Vnav);
            DrawVnavExtras();
            DrawIpcRow(Plugin.RotationSolver);
            DrawIpcRow(Plugin.WrathCombo);
            DrawIpcRow(Plugin.BossMod);
            DrawIpcRow(Plugin.PalacePal);
            DrawPalacePalExtras();
        }
    }

    private static void DrawLifecycle()
    {
        var stage = Plugin.Lifecycle.CurrentStage;
        var since = (DateTime.UtcNow - Plugin.Lifecycle.StageEnteredAt).TotalSeconds;
        ImGui.Text($"Stage: {stage}   ({since:F0}s)");
        ImGui.Text($"Master toggle: {(Plugin.Config.MasterEnabled ? "ON" : "OFF")}   ToS accepted: {Plugin.Config.ToSAccepted}");

        var inDd = DDStateHelper.IsInDeepDungeon();
        var floor = DDStateHelper.CurrentFloor();
        var kind = DDStateHelper.CurrentDDKind();
        ImGui.Text($"In DD: {inDd}   Floor: {floor}   Kind: {kind?.ToString() ?? "n/a"}");
        ImGui.TextDisabled($"Territory: {Svc.ClientState.TerritoryType}");

        if (ImGui.Button("Start")) Plugin.Lifecycle.Start();
        ImGui.SameLine();
        if (ImGui.Button("Stop")) Plugin.Lifecycle.Stop();
    }

    private static void DrawIpcRow(IIpcSubscriber sub)
    {
        var ready = sub.IsReady;
        var color = ready ? new Vector4(0.35f, 1.0f, 0.35f, 1.0f) : new Vector4(1.0f, 0.4f, 0.4f, 1.0f);
        var badge = ready ? "READY" : "NOT READY";
        ImGui.TextColored(color, badge);
        ImGui.SameLine();
        ImGui.Text(sub.Name);
        if (!ready && !string.IsNullOrEmpty(sub.LastError))
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"— {sub.LastError}");
        }
    }

    private static void DrawVnavExtras()
    {
        if (!Plugin.Vnav.IsReady) return;
        var prog = Plugin.Vnav.BuildProgress;
        if (prog >= 0f && prog < 1f)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"(mesh build {prog * 100f:F0}%)");
        }
    }

    private static void DrawPalacePalExtras()
    {
        var pp = Plugin.PalacePal;
        if (pp.DatabasePath != null)
        {
            ImGui.TextDisabled($"DB: {pp.DatabasePath}");
        }
        var territory = Svc.ClientState.TerritoryType;
        if (pp.IsReady && territory != 0)
        {
            var hits = pp.QueryByTerritory(territory);
            ImGui.Text($"Territory {territory}: {hits.Count} trap/hoard rows");
        }
    }
}
