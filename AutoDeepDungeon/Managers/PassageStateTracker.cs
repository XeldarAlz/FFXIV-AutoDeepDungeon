using System;
using ECommons.DalamudServices;
using ECommons.Hooks;

namespace AutoDeepDungeon.Managers;

/// <summary>
/// Detects when a Cairn of Passage (or HoH/Orthos equivalent) activates by hooking the
/// server-sent <c>MapEffect</c> packet stream. The plan's activation signature is
/// <c>data1 == 4 &amp;&amp; data2 == 8</c> — verified against PalacePal's behaviour (it
/// highlights the passage the moment this packet arrives).
///
/// Neither <c>InstanceContentDeepDungeon.PassageProgress</c> nor the EObj's
/// <c>EventState</c> byte flip at the right moment: the former is a kill counter that
/// increments on the first mob, and the latter stays zero on DD passages in practice.
/// The packet hook is the only signal that lines up with the in-game visual.
/// </summary>
public sealed class PassageStateTracker : IDisposable
{
    public bool PassageActivated { get; private set; }

    public PassageStateTracker()
    {
        try
        {
            MapEffect.Init(OnMapEffect);
            Svc.Log.Info("[PassageStateTracker] MapEffect hook installed.");
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"[PassageStateTracker] MapEffect hook failed: {ex.Message}. Passage state will be permanently inactive.");
        }

        Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    public void Dispose()
    {
        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
        try { MapEffect.Dispose(); } catch { /* best effort */ }
    }

    private void OnTerritoryChanged(ushort newTerritory)
    {
        PassageActivated = false;
    }

    private void OnMapEffect(long a1, uint a2, ushort a3, ushort a4)
    {
        // Debug: log every packet inside a DD so we can identify the passage-activation
        // signature. The plan's (a3=4, a4=8) rule didn't match reality — need to observe
        // what actually fires when the cairn lights up.
        if (Helpers.DDStateHelper.IsInDeepDungeon())
        {
            Svc.Log.Info($"[MapEffect] a1=0x{a1:X} a2={a2} a3={a3} a4={a4}");
        }

        if (a3 == 4 && a4 == 8)
        {
            PassageActivated = true;
        }
    }
}
