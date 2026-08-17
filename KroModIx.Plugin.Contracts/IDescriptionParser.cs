using System.Collections.Generic;

namespace KroModIx.Plugin.Contracts;

/// <summary>Zentraler Baukasten fuer Mod-Beschreibungen. Nexus liefert
/// Descriptions als Mix aus HTML (Web-Editor) und BBCode (Vault-Editor,
/// den viele Mod-Autoren nutzen). Ohne zentralen Parser hat jedes Plugin
/// eine eigene Regex-Chain — mit dem Risiko dass ein BBCode-Tag fehlt,
/// im UI roher Muell erscheint, und der Fix in 4 Plugins nachgezogen
/// werden muss.
///
/// <para><b>Contracts v1.19.0+.</b> Bei aelteren Hosts default-implementiert
/// als <see cref="NullDescriptionParser.Instance"/> (Passthrough — der User
/// sieht raw HTML/BBCode, aber das Plugin crasht nicht).</para>
///
/// <para>Analog zum <see cref="IImageDecoder"/>-Muster (v1.18) —
/// Cross-Cutting-Concern-Baukasten, gemeinsame Bug-Fixes an einer Stelle.</para></summary>
public interface IDescriptionParser
{
    /// <summary>Wandelt HTML+BBCode-Mix in lesbaren Plain-Text um.
    /// Strippt Container-Tags ([center]/[b]/[color=..]/[size=..]/[font=..]/...),
    /// dropped [img …]URL[/img] (Inline-Bilder wuerden im scrollbaren
    /// TextBlock als roher Link-Text erscheinen), reduziert [url=..]Text[/url]
    /// auf Text. HTML: strippt Tags, dekodiert Entities, normalisiert
    /// Whitespace. Leerer Input → leerer Output.</summary>
    string ToPlainText(string html);

    /// <summary>Extrahiert alle Inline-Bild-URLs aus einem HTML+BBCode-Blob.
    /// Nuetzlich fuer Screenshot-Galerien wenn das Plugin Media-Preview
    /// zeigen will (v0.7+ Cyberpunk-Style — Detail-Dialog mit Bilder-Leiste
    /// unter der Beschreibung). BBCode <c>[img …]URL[/img]</c> und HTML
    /// <c>&lt;img src="URL"&gt;</c> werden beide erfasst.
    /// Rueckgabe in Vorkommens-Reihenfolge; leerer Input → leere Liste.</summary>
    IReadOnlyList<InlineImage> ExtractImages(string html);
}

/// <summary>Ein Inline-Bild aus einer Mod-Description. Nur URL ist Pflicht —
/// <see cref="Height"/> ist optional (Nexus-BBCode gibt oft nur die Height an,
/// Alt-Text bekommt der Editor nicht mit).
///
/// <para>Weitere Felder (Width, AltText, Caption) koennen als init-Property
/// nachtraeglich hinzugefuegt werden ohne binary-break (Contracts-Records-
/// Compat-Regel).</para></summary>
public sealed record InlineImage(string Url, int? Height = null);
