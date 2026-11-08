using System.Globalization;

namespace Aldren.Core.Tools;

/// <summary>
/// Arithmetic only: numbers, + - * / % ^, parentheses, sqrt, abs, round and pi. No variables and nothing executed, so a model
/// or a page can't turn it into anything else.
/// </summary>
public sealed class Calculator
{
    private readonly string _text;
    private int _pos;

    private Calculator(string text) => _text = text;

    public static double Evaluate(string expression)
    {
        var text = expression.Replace("×", "*").Replace("÷", "/").Replace("x", "*").Replace(",", "");
        var calc = new Calculator(text);
        var value = calc.Expression();
        calc.SkipSpaces();
        if (calc._pos != calc._text.Length)
        {
            throw new FormatException($"I can't read \"{expression}\" as a calculation.");
        }

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArithmeticException("That has no finite answer.");
        }

        return value;
    }

    private double Expression()
    {
        var value = Term();
        while (true)
        {
            if (Eat('+')) { value += Term(); }
            else if (Eat('-')) { value -= Term(); }
            else { return value; }
        }
    }

    private double Term()
    {
        var value = Power();
        while (true)
        {
            if (Eat('*')) { value *= Power(); }
            else if (Eat('/')) { value /= Power(); }
            else if (Eat('%')) { value %= Power(); }
            else { return value; }
        }
    }

    private double Power()
    {
        var value = Unary();
        return Eat('^') ? Math.Pow(value, Power()) : value;
    }

    private double Unary()
    {
        if (Eat('-')) { return -Unary(); }
        if (Eat('+')) { return Unary(); }
        return Primary();
    }

    private double Primary()
    {
        SkipSpaces();
        if (Eat('('))
        {
            var value = Expression();
            Expect(')');
            return value;
        }

        if (_pos < _text.Length && char.IsLetter(_text[_pos]))
        {
            var start = _pos;
            while (_pos < _text.Length && char.IsLetter(_text[_pos])) { _pos++; }
            var name = _text[start.._pos].ToLowerInvariant();
            if (name == "pi") { return Math.PI; }
            Expect('(');
            var arg = Expression();
            Expect(')');
            return name switch
            {
                "sqrt" => Math.Sqrt(arg),
                "abs" => Math.Abs(arg),
                "round" => Math.Round(arg),
                _ => throw new FormatException($"I don't know the function \"{name}\"."),
            };
        }

        var begin = _pos;
        while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.')) { _pos++; }
        if (begin == _pos)
        {
            throw new FormatException("A number is missing.");
        }

        return double.Parse(_text[begin.._pos], CultureInfo.InvariantCulture);
    }

    private void SkipSpaces()
    {
        while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) { _pos++; }
    }

    private bool Eat(char c)
    {
        SkipSpaces();
        if (_pos < _text.Length && _text[_pos] == c)
        {
            _pos++;
            return true;
        }

        return false;
    }

    private void Expect(char c)
    {
        if (!Eat(c))
        {
            throw new FormatException($"Expected '{c}'.");
        }
    }
}
