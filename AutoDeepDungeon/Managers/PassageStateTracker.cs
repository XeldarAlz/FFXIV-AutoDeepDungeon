using System;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using ECommons.DalamudServices;
using ECommons.Hooks;

namespace AutoDeepDungeon.Managers;

/// <summary>
/// Detects when a Cairn of Passage (HoH: Beacon, Orthos: Pylon) activates.
///
/// Two signals run in parallel — whichever fires first wins:
///
/// 1. Chat-message listener. The game announces 'The Cairn of Passage is activated!'
///    (etc) to the party channel on activation. User-visible text, locale-dependent,
///    but matches what PalacePal / the visual overlay do.
///
/// 2. ECommons' MapEffect packet hook. The plan pointed at a (data1=4, data2=8) packet
///    signature; we haven't observed it in practice yet, but we keep the hook armed
///    with logging so we can pin the real packet values next time.
///
/// Both signals cleared on territory change.
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
            Svc.Log.Warning($"[PassageStateTracker] MapEffect hook failed: {ex.Message}. Will rely on chat listener.");
        }

        Svc.Chat.ChatMessage += OnChatMessage;
        Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    public void Dispose()
    {
        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
        Svc.Chat.ChatMessage -= OnChatMessage;
        try { MapEffect.Dispose(); } catch { /* best effort */ }
    }

    private void OnTerritoryChanged(ushort _)
    {
        PassageActivated = false;
    }

    private void OnMapEffect(long a1, uint a2, ushort a3, ushort a4)
    {
        // Plan points at (a3=4, a4=8) for passage activation; we haven't observed this
        // path firing in practice (chat listener below is the primary signal). Kept
        // armed quietly in case a future ECommons sig update starts catching it.
        if (a3 == 4 && a4 == 8)
        {
            PassageActivated = true;
        }
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (PassageActivated) return;
        if (!Helpers.DDStateHelper.IsInDeepDungeon()) return;

        // Localised to English for now. Per the plan the same passage EObj concept is
        // 'Cairn' in PotD, 'Beacon' in HoH, 'Pylon' in Orthos. Matching any of those
        // plus 'activated' keeps us language-specific for English clients; a
        // LogMessage-id based check will replace this when we verify the row IDs.
        var text = message.TextValue;
        if (string.IsNullOrEmpty(text)) return;
        if (!(text.Contains("Cairn", StringComparison.OrdinalIgnoreCase) ||
              text.Contains("Beacon", StringComparison.OrdinalIgnoreCase) ||
              text.Contains("Pylon", StringComparison.OrdinalIgnoreCase)))
            return;
        if (!text.Contains("activated", StringComparison.OrdinalIgnoreCase)) return;

        PassageActivated = true;
        Svc.Log.Info($"[PassageStateTracker] Activated via chat: '{text}'");
    }
}
