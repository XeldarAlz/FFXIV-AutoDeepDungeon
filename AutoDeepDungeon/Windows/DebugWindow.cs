using System;
using System.Collections.Generic;
using System.Numerics;
using AutoDeepDungeon.Helpers;
using AutoDeepDungeon.IPC;
using AutoDeepDungeon.Managers;
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

        ImGui.SameLine();
        var overlay = Plugin.Config.EnableDebugOverlay;
        if (ImGui.Checkbox("World overlay", ref overlay))
        {
            Plugin.Config.EnableDebugOverlay = overlay;
            Plugin.SaveConfig();
        }

        ImGui.Separator();

        if (ImGui.CollapsingHeader("Floor scan", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawFloorScan();
        }

        if (ImGui.CollapsingHeader("Executor", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawExecutor();
        }

        if (ImGui.CollapsingHeader("Planner / PathCost", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawPathCost();
        }

        if (ImGui.CollapsingHeader("IPC readiness"))
        {
            DrawIpcRow(Plugin.Vnav);
            DrawVnavExtras();
            DrawIpcRow(Plugin.RotationSolver);
            DrawIpcRow(Plugin.WrathCombo);
            DrawIpcRow(Plugin.BossMod);
            DrawIpcRow(Plugin.PalacePal);
            DrawPalacePalExtras();
            DrawSplatoonRow();
        }

        if (ImGui.CollapsingHeader("Config snapshot"))
        {
            DrawConfigSnapshot();
        }

        if (ImGui.CollapsingHeader("Humanizer"))
        {
            DrawHumanizer();
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

    private static void DrawFloorScan()
    {
        var f = Plugin.Floor.Current;
        if (!f.InDeepDungeon)
        {
            ImGui.TextDisabled("Not in a Deep Dungeon — scanner idle.");
            return;
        }
        ImGui.Text($"Kind: {f.Kind?.ToString() ?? "?"}  Floor: {f.Floor}  Territory: {f.TerritoryType}  PassageProgress: {f.PassageProgress}");
        ImGui.Text(
            $"Mobs: {f.Mobs.Count}   " +
            $"Traps: {f.Traps.Count} live / {f.PersistentTraps.Count} saved   " +
            $"Coffers: {f.Coffers.Count}   " +
            $"Hoards: {f.Hoards.Count} live / {f.PersistentHoards.Count} saved   " +
            $"Passage: {(f.Passage == null ? "no" : (f.Passage.Active ? "ACTIVE" : "inactive"))}");

        var aggroHere = AggroMap.AnyAggroCovers(f, f.SelfPosition);
        ImGui.Text($"Aggro on self: {(aggroHere ? "YES" : "no")}");
        ImGui.SameLine();
        if (aggroHere) ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "— a mob would pull if we're here");

        if (f.Mobs.Count > 0 && ImGui.TreeNode($"Mobs##mobs_{f.Mobs.Count}"))
        {
            // Copy + sort by distance so the closest mobs are at the top. Cheap with <50 mobs.
            var byDist = new System.Collections.Generic.List<Data.MobEntity>(f.Mobs);
            byDist.Sort((a, b) => Vector3.Distance(f.SelfPosition, a.Position)
                                   .CompareTo(Vector3.Distance(f.SelfPosition, b.Position)));

            if (ImGui.BeginTable("##mobs", 9, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit))
            {
                ImGui.TableSetupColumn("BaseId");
                ImGui.TableSetupColumn("NameId");
                ImGui.TableSetupColumn("Name");
                ImGui.TableSetupColumn("HP");
                ImGui.TableSetupColumn("Dist");
                ImGui.TableSetupColumn("Rad");
                ImGui.TableSetupColumn("Type");
                ImGui.TableSetupColumn("Combat");
                ImGui.TableSetupColumn("Mimic?");
                ImGui.TableHeadersRow();
                foreach (var m in byDist)
                {
                    var geom = AggroMap.Compute(m);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.Text(m.BaseId.ToString());
                    ImGui.TableNextColumn(); ImGui.Text(m.NameId.ToString());
                    ImGui.TableNextColumn(); ImGui.Text(string.IsNullOrEmpty(m.Name) ? "<anon>" : m.Name);
                    ImGui.TableNextColumn(); ImGui.Text($"{m.CurrentHp}/{m.MaxHp}");
                    ImGui.TableNextColumn(); ImGui.Text($"{Vector3.Distance(f.SelfPosition, m.Position):F1}y");
                    ImGui.TableNextColumn(); ImGui.Text($"{geom.Radius:F0}y");
                    ImGui.TableNextColumn(); ImGui.Text(geom.Omnidirectional ? "omni" : "cone");
                    ImGui.TableNextColumn(); ImGui.Text(m.InCombat ? "yes" : "");
                    ImGui.TableNextColumn(); if (m.IsMimicCandidate) ImGui.TextColored(new Vector4(1f, 0.6f, 0.2f, 1f), "MIMIC");
                }
                ImGui.EndTable();
            }
            ImGui.TreePop();
        }

        DrawEObjList("Traps",   f.Traps,   f.SelfPosition);
        DrawEObjList("Coffers", f.Coffers, f.SelfPosition);
        DrawEObjList("Hoards",  f.Hoards,  f.SelfPosition);
        if (f.Passage is { } p)
        {
            ImGui.Text($"Passage: DataId={p.DataId}  Dist={Vector3.Distance(f.SelfPosition, p.Position):F1}y  EventState={p.EventState}  Active={p.Active}");
        }
    }

    private static void DrawPathCost()
    {
        var planner = Plugin.Planner;
        var floor = Plugin.Floor.Current;

        if (!Plugin.Vnav.IsReady)
        {
            ImGui.TextDisabled("vnavmesh not ready — planner disabled.");
            return;
        }

        ImGui.TextDisabled(
            $"Weights: aggro×{Plugin.Config.PlannerAggroPenalty:F0}  " +
            $"coffer×{Plugin.Config.PlannerCofferReward:F0}  " +
            $"trap<{Plugin.Config.PlannerTrapAvoidRadius:F1}y  " +
            $"coffer≤{Plugin.Config.CofferDetourYalms}y  " +
            $"hyst×{Plugin.Config.PlannerHysteresisRatio:F2}");

        var running = planner.Enabled;
        var tickColor = running ? new Vector4(0.35f, 1.0f, 0.35f, 1f) : new Vector4(0.7f, 0.7f, 0.7f, 1f);
        ImGui.TextColored(tickColor, running ? "TICK ON (200ms)" : "tick off");
        ImGui.SameLine();
        ImGui.TextDisabled(
            $"rounds: {planner.RoundCount}   swaps: {planner.ReplanCount}   goal: {planner.ActiveGoal.Kind}   " +
            $"last round: {planner.LastCandidatesViable}/{planner.LastCandidatesConsidered} viable");
        if (planner.QueryInFlight)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(querying…)");
        }

        var noPassage = floor.Passage is null;
        if (noPassage) ImGui.BeginDisabled();
        if (ImGui.Button("Plan once → passage") && floor.Passage is { } p1)
        {
            planner.PlanOnce(PlanGoal.ToPassage(p1.Position));
        }
        ImGui.SameLine();
        if (!running)
        {
            if (ImGui.Button("Enable tick → passage") && floor.Passage is { } p2)
            {
                planner.Enable(PlanGoal.ToPassage(p2.Position));
            }
        }
        else
        {
            if (ImGui.Button("Disable tick"))
            {
                planner.Disable();
            }
        }
        if (noPassage) ImGui.EndDisabled();

        ImGui.SameLine();
        var autoDrive = planner.AutoDrive;
        if (ImGui.Checkbox("Auto-drive", ref autoDrive))
        {
            planner.AutoDrive = autoDrive;
        }

        if (noPassage)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(no passage in current floor scan)");
        }

        if (planner.LastError is { } err)
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), $"error: {err}");
        }

        var plan = planner.Current;
        if (plan.Waypoints.Count == 0)
        {
            ImGui.TextDisabled("no plan yet.");
            return;
        }

        var ago = (DateTime.UtcNow - plan.BuiltAt).TotalSeconds;
        var viaLabel = plan.IsDetour ? "detour via" : "direct";
        ImGui.Text($"Plan: {plan.Waypoints.Count} waypoints   built {ago:F0}s ago   [{viaLabel}]");
        var fatal = plan.Score.HasTrap;
        var totalText = fatal ? "FATAL (trap on path)" : $"{plan.Score.Total:F1}";
        var totalColor = fatal ? new Vector4(1f, 0.4f, 0.4f, 1f) : new Vector4(0.35f, 1.0f, 0.35f, 1f);
        ImGui.Text("Total:"); ImGui.SameLine(); ImGui.TextColored(totalColor, totalText);
        ImGui.Text(
            $"  length {plan.Score.Length:F1}y   " +
            $"cones {plan.Score.ConeCrossings} (+{plan.Score.ConePenalty:F0})   " +
            $"coffers {plan.Score.CoffersOnPath} (-{plan.Score.CofferReward:F0})   " +
            $"trap {(plan.Score.HasTrap ? "YES" : "no")}");
        if (ImGui.Button("Drive plan"))
        {
            // Match auto-drive: hand vnav the full scored waypoint list via
            // Path.MoveTo so trap avoidance is honored. A live-nav to Via
            // alone would let vnav re-route through traps since it has no
            // trap awareness.
            Plugin.Exec.StartWaypoints(new List<Vector3>(plan.Waypoints));
        }
        ImGui.SameLine();
        ImGui.TextDisabled(
            $"via {plan.Via.X:F1},{plan.Via.Y:F1},{plan.Via.Z:F1}   " +
            $"goal {plan.Goal.Point.X:F1},{plan.Goal.Point.Y:F1},{plan.Goal.Point.Z:F1}");
    }

    private static void DrawExecutor()
    {
        var exec = Plugin.Exec;
        var running = exec.IsRunning;
        var color = running ? new Vector4(0.35f, 1.0f, 0.35f, 1.0f) : new Vector4(0.7f, 0.7f, 0.7f, 1f);
        ImGui.TextColored(color, running ? "RUNNING" : "idle");
        ImGui.SameLine();
        ImGui.Text($"waypoints: {exec.WaypointCount}");
        ImGui.SameLine();
        ImGui.TextDisabled($"stuck events: {exec.StuckEventCount}   retries: {exec.RetriesUsed}");
        if (exec.LastStuckAt != DateTime.MinValue)
        {
            ImGui.SameLine();
            var ago = (DateTime.UtcNow - exec.LastStuckAt).TotalSeconds;
            ImGui.TextDisabled($"(last {ago:F0}s ago)");
        }

        var vnavReady = Plugin.Vnav.IsReady;
        if (!vnavReady)
        {
            ImGui.TextDisabled("vnavmesh IPC not ready — controls disabled.");
            return;
        }

        // Target of last user click — vnav can path to any Vector3, so the simplest
        // manual test is "walk to whatever the client currently has targeted".
        var target = Svc.Targets.Target;
        if (target != null)
        {
            var dist = Vector3.Distance(Svc.ClientState.LocalPlayer?.Position ?? Vector3.Zero, target.Position);
            ImGui.Text($"Target: {target.Name.TextValue}  ({dist:F1}y)");
            if (ImGui.Button("Walk to target"))
            {
                exec.Start(target.Position);
            }
        }
        else
        {
            ImGui.TextDisabled("No target selected — click a mob/NPC to enable 'Walk to target'.");
        }

        var passage = Plugin.Floor.Current.Passage;
        if (passage is not null)
        {
            ImGui.SameLine();
            if (ImGui.Button("Walk to passage"))
            {
                exec.Start(passage.Position);
            }
        }

        if (ImGui.Button("Stop")) exec.Stop();
    }

    private static void DrawEObjList(string label, System.Collections.Generic.IReadOnlyList<Data.EObjEntity> items, Vector3 self)
    {
        if (items.Count == 0) return;
        if (!ImGui.TreeNode($"{label} ({items.Count})##list_{label}")) return;
        if (ImGui.BeginTable($"##tbl_{label}", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("DataId");
            ImGui.TableSetupColumn("Position");
            ImGui.TableSetupColumn("Dist");
            ImGui.TableHeadersRow();
            foreach (var e in items)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.Text(e.DataId.ToString());
                ImGui.TableNextColumn(); ImGui.Text($"({e.Position.X:F1}, {e.Position.Y:F1}, {e.Position.Z:F1})");
                ImGui.TableNextColumn(); ImGui.Text($"{Vector3.Distance(self, e.Position):F1}y");
            }
            ImGui.EndTable();
        }
        ImGui.TreePop();
    }

    private static void DrawConfigSnapshot()
    {
        var c = Plugin.Config;
        if (ImGui.BeginTable("##adg_cfg_snap", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
        {
            Row("Target",          $"{c.TargetDungeon}  floors {c.FloorStart}-{c.FloorEnd}");
            Row("Stop",            c.StopCondition == Data.StopCondition.NRuns
                                     ? $"{c.StopCondition} ({c.StopAfterRuns})"
                                     : c.StopCondition.ToString());
            Row("Combat driver",   c.CombatDriver.ToString());
            Row("Save behavior",   c.SaveBehavior.ToString());
            Row("On death",        $"{c.OnDeath}  +{c.RequeueDelaySeconds}s");
            Row("Hoard",           c.Hoard.ToString());
            Row("Magicite",        c.Magicite.ToString());
            Row("Raising",         c.Raising.ToString());
            Row("Conservative",    c.ConservativeMode.ToString());
            Row("Coffer detour",   $"{c.CofferDetourYalms} yalms");
            Row("Kill policy",     c.KillForAetherpool.ToString());
            Row("Multi-pull",      c.MultiPullTolerance.ToString());
            Row("HP emergency",    $"{c.HpEmergencyThresholdPct}%");
            Row("Stuck timeout",   $"{c.StuckTimeoutSeconds}s");
            Row("Auto-stop",       c.AutoStopOnUnexpectedState.ToString());
            ImGui.EndTable();
        }

        static void Row(string k, string v)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.TextDisabled(k);
            ImGui.TableNextColumn(); ImGui.Text(v);
        }
    }

    private static void DrawHumanizer()
    {
        ImGui.TextWrapped(
            "Log-normal jitter used to space automated actions. Median ~650ms, clamped to [400, 1200]ms.");
        if (ImGui.Button("Roll 10 samples"))
        {
            var buf = new int[10];
            for (var i = 0; i < buf.Length; i++) buf[i] = Humanizer.NextDelayMs();
            Svc.Log.Information($"[Humanizer] samples: {string.Join(", ", buf)}");
        }
    }

    private static void DrawSplatoonRow()
    {
        var ready = Plugin.Overlay?.IsConnected ?? false;
        var color = ready ? new Vector4(0.35f, 1.0f, 0.35f, 1.0f) : new Vector4(1.0f, 0.4f, 0.4f, 1.0f);
        var badge = ready ? "READY" : "NOT READY";
        ImGui.TextColored(color, badge);
        ImGui.SameLine();
        ImGui.Text("Splatoon (overlay renderer)");
        if (!ready)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("— install Splatoon for world-space overlays");
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
