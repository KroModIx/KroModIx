using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KroModIx.Plugin.Contracts;

/// <summary>Eine Aktion eines Plugins, die zu keinem einzelnen Spiel gehört.
/// Der Host hängt sie in der Sidebar neben „Spiel hinzufügen" ein.
///
/// <para><b>Warum es das braucht:</b> bis v1.35 konnte ein Plugin UI nur über
/// <see cref="IGameTabContribution"/> beisteuern, und dessen beide Methoden
/// bekommen ein <see cref="DetectedGame"/>. Alles, was ein Plugin anbietet,
/// hing damit an einer vorhandenen Kachel. Für „neues Spiel aus einem Archiv
/// anlegen" (Ren'Py Assist v0.26) war das eine Sackgasse: man brauchte ein
/// Spiel, um ein Spiel anzulegen. Der Knopf landete notgedrungen in der
/// Übersicht irgendeines anderen Spiels — dort sucht ihn niemand.</para>
///
/// <para><b>Abgrenzung:</b> Hier gehört hin, was <em>keinen</em> Spiel-Kontext
/// hat. Alles Spielbezogene bleibt ein Tab — eine globale Aktion, die intern
/// doch wieder ein Spiel auswählen lässt, ist am falschen Platz.</para>
///
/// <para>Contracts v1.36+.</para></summary>
public interface IGlobalAction
{
    /// <summary>Stabile Kennung, plugin-weit eindeutig. Geht in Logs und
    /// erlaubt dem Host, eine Aktion wiederzuerkennen.</summary>
    string Id { get; }

    /// <summary>Beschriftung im Menü, schon übersetzt. Das Plugin kennt seine
    /// Sprache, der Host kennt den Text nicht.</summary>
    string Label { get; }

    /// <summary>Ein oder zwei Emoji als Icon. Leer ist erlaubt.</summary>
    string Icon { get; }

    /// <summary>Optionaler Erklärtext für den Tooltip.</summary>
    string? Description => null;

    /// <summary>Sortierung innerhalb der Aktionen eines Plugins; zwischen
    /// Plugins sortiert der Host nach Plugin-Name.</summary>
    int Order => 0;

    /// <summary>Soll die Aktion im Moment anklickbar sein? Wird unmittelbar
    /// vor dem Öffnen des Menüs gefragt, nicht gecacht — ein Plugin darf hier
    /// also den aktuellen Zustand prüfen.</summary>
    bool IsEnabled => true;

    /// <summary>Führt die Aktion aus. Läuft auf dem UI-Thread, das Plugin darf
    /// also Dialoge öffnen. Ausnahmen fängt der Host und meldet sie — er lässt
    /// sich davon nicht aus dem Tritt bringen.</summary>
    Task InvokeAsync(CancellationToken ct = default);
}
