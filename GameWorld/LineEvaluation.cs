using Microsoft.Xna.Framework;
using STM.Data;
using STM.GameWorld;
using STM.UI;
using STMG.Engine;
using System.Runtime.CompilerServices;
using Utilities;

namespace AITweaks.GameWorld;


// Line Evaluation Tooltip - UITweaks will access
public static class LineEvaluation
{
    // Data extensions
    internal class ExtraData
    {
        internal string Header = "?"; // companyId-lineId-hubName
        internal readonly List<List<string>> Text = []; // text lines
    }
    private static readonly ConditionalWeakTable<Line, ExtraData> _extras = [];
    internal static ExtraData Extra(this Line line) => _extras.GetOrCreateValue(line);

    // 2025-10-26 Race condition happens here if the line is split into 2+ hubs!
    internal static void NewEvaluation(this Line line, string header)
    {
        string _header = line.Extra().Header;
        if (!_header.Contains(header))
            line.Extra().Header = _header == "?" ? header : _header + "|" + header;
        List<string> newEval = [$"[{DateTime.Now:HH:mm:ss}]  {header}"];
        List<List<string>> _text = line.Extra().Text;
        lock (_text)
        {
            _text.Insert(0, newEval);
            if (_text.Count == 4) _text.RemoveAt(3); // keep only last 3 evals
        }
    }

    internal static void AddEvaluationText(this Line line, string text)
    {
        line.Extra().Text.First().Add(text);
    }

    public static int GetNumEvaluations(Line line)
    {
        return line.Extra().Text.Count;
    }

    public static TooltipPreset GetEvaluationTooltip(Line line, GameEngine engine)
    {
        if (line.Extra().Text.Count == 0)
            return TooltipPreset.Get("No evaluations", engine, can_lock: false);
        TooltipPreset tooltip = TooltipPreset.Get(line.Extra().Header, engine, can_lock: true);
        List<List<string>> _text = line.Extra().Text;
        lock (_text)
        {
            for (int i = 0; i < _text.Count; i++)
            {
                if (i > 0) tooltip.AddSeparator();
                tooltip.AddBoldLabel(_text[i][0]);
                for (int j = 1; j < _text[i].Count; j++)
                    tooltip.AddDescription(_text[i][j]);
            }
        }
        tooltip.GetPrivateField<STMG.UI.Control.ControlCollection>("main_control").Size_local = new Vector2(650, MainData.Tooltip_header);
        return tooltip;
    }
}
