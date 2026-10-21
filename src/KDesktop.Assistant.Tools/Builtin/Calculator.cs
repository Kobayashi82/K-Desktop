using System.Globalization;
using System.Text.Json;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>«¿Cuánto es el 18 % de 240?»: evaluates arithmetic (never code).</summary>
public sealed class CalculateTool : ITool
{
    public string Name => "calculate";

    public string Description => "Evaluates an arithmetic expression exactly. Use it for any calculation instead of doing it yourself.";

    public string ParametersSchemaJson => ToolText.OneString("expression", "The expression, with + - * / ^, % for percent and parentheses, e.g. 18%*240 for 18 percent of 240");

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.calculate.intent", ToolText.Clean(arguments.GetString("expression")));

    public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var expression = arguments.GetString("expression") ?? string.Empty;
        if (!Arithmetic.TryEvaluate(expression, out var value, out var error)) return Task.FromResult(ToolResult.Fail($"Cannot calculate it: {error}."));
        var shown = value.ToString("0.########", Loc.Instance.Culture);
        return Task.FromResult(new ToolResult(value.ToString("0.########", CultureInfo.InvariantCulture))
        {
            Done = Loc.T("tool.calculate.done", ToolText.Clean(expression), shown),
        });
    }
}

/// <summary>
/// Arithmetic by recursive descent: + - * / ^, unary signs, parentheses and % as percent (what people mean: «18 %» is
/// 0.18, and «18%240», as small models write «18 % of 240», is 43.2; never the remainder).
/// </summary>
public static class Arithmetic
{
    public static bool TryEvaluate(string expression, out double value, out string error)
    {
        var parser = new Parser(expression.Replace(',', '.').Replace('×', '*').Replace('÷', '/').Replace('x', '*'));
        try
        {
            value = parser.ParseExpression();
            parser.SkipSpaces();
            if (!parser.AtEnd) throw new FormatException($"unexpected «{parser.Current}»");
            if (!double.IsFinite(value)) throw new FormatException("the result is not a finite number");
            error = string.Empty;
            return true;
        }
        catch (FormatException ex)
        {
            value = 0;
            error = ex.Message;
            return false;
        }
    }

    private sealed class Parser(string text)
    {
        private int _position;
        private int _depth;

        public bool AtEnd => _position >= text.Length;

        public char Current => text[_position];

        public void SkipSpaces()
        {
            while (!AtEnd && char.IsWhiteSpace(Current)) _position++;
        }

        public double ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipSpaces();
                if (Accept('+')) value += ParseTerm();
                else if (Accept('-')) value -= ParseTerm();
                else return value;
            }
        }

        private double ParseTerm()
        {
            var value = ParsePower();
            while (true)
            {
                SkipSpaces();
                if (Accept('*')) value *= ParsePower();
                else if (Accept('/')) value /= NotZero(ParsePower());
                else if (Accept('%'))
                {
                    value /= 100;
                    SkipSpaces();
                    // «18%240»: of what follows.
                    if (!AtEnd && (char.IsDigit(Current) || Current == '(')) value *= ParsePower();
                }
                else return value;
            }
        }

        private static double NotZero(double divisor) => divisor == 0 ? throw new FormatException("division by zero") : divisor;

        private double ParsePower()
        {
            var value = ParseUnary();
            SkipSpaces();
            return Accept('^') ? Math.Pow(value, ParsePower()) : value;
        }

        private double ParseUnary()
        {
            SkipSpaces();
            if (Accept('-')) return -ParseUnary();
            if (Accept('+')) return ParseUnary();
            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            SkipSpaces();
            if (Accept('('))
            {
                if (++_depth > 64) throw new FormatException("too many parentheses");
                var value = ParseExpression();
                SkipSpaces();
                if (!Accept(')')) throw new FormatException("a parenthesis is missing");
                _depth--;
                return value;
            }
            var start = _position;
            while (!AtEnd && (char.IsDigit(Current) || Current == '.')) _position++;
            if (start == _position) throw new FormatException(AtEnd ? "it is incomplete" : $"unexpected «{Current}»");
            return double.TryParse(text.AsSpan(start, _position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number
                : throw new FormatException("a number is wrong");
        }

        private bool Accept(char c)
        {
            if (AtEnd || Current != c) return false;
            _position++;
            return true;
        }
    }
}
