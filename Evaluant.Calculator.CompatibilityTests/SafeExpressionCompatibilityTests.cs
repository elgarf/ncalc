using System;
using System.Collections;
using System.Collections.Generic;
using Xunit;

namespace NCalc.CompatibilityTests;

public class SafeExpressionCompatibilityTests
{
    public static IEnumerable<object[]> Cases()
    {
        // Core parsing/evaluation
        yield return Case("1+1");
        yield return Case("1+2*3");
        yield return Case("(1+2)*3");
        yield return Case("1<2?10:20");
        yield return Case("123456");
        yield return Case("#01/01/2001#");
        yield return Case("123.456");
        yield return Case("true");
        yield return Case("'true'");
        yield return Case("'azerty'");
        yield return Case("'\\u0048\\u0065\\u006C\\u006C\\u006F'");
        yield return Case("'to' + 'to'");
        yield return Case("'one' + 2");
        yield return Case("1 + '2'");
        yield return Case("1 == 1");
        yield return Case("1 != 1");
        yield return Case("1 <> 1");
        yield return Case("1 <= 2");
        yield return Case("1 >= 1");
        yield return Case("1 < 2");
        yield return Case("1 > 2");
        yield return Case("1.22e1");
        yield return Case("1e2");
        yield return Case("1e+2");
        yield return Case("1e-2");
        yield return Case(".1e-2");
        yield return Case("40000000000+1");
        yield return Case("(0=1500000)||(((0+2200000000)-1500000)<0)");
        yield return Case("#1/1/2009#==#1/1/2009#");
        yield return Case("Abs(-1)");
        yield return Case("aBs(-1)", EvaluateOptions.IgnoreCase);
        yield return Case("aBs(-1)", EvaluateOptions.None);
        yield return Case("Round(22.5, 0)");
        yield return Case("Round(22.5, 0)", EvaluateOptions.RoundAwayFromZero);
        yield return Case("!true");
        yield return Case("not false");
        yield return Case("7 % 2");
        yield return Case("1 & 1");
        yield return Case("1 | 1");
        yield return Case("1 ^ 1");
        yield return Case("~1");
        yield return Case("2 >> 1");
        yield return Case("2 << 1");
        yield return Case("true && false");
        yield return Case("true || false");
        yield return Case("if(true, 0, 1)");
        yield return Case("if(false, 0, 1)");
        yield return Case("2+2+2+2");
        yield return Case("2*2*2*2");
        yield return Case("2*2+2");
        yield return Case("2+2*2");
        yield return Case("1 + 2 + 3 * 4 / 2");
        yield return Case("18/2/2*3");
        yield return Case("0 <= -0.6");
        yield return Case("'\\'hello\\''");
        yield return Case("' \\' hel lo \\' '");
        yield return Case("'hel\\nlo'");
        yield return Case("in((2 + 2), [1], [2], 1 + 2, 4, 1 / 0)", EvaluateOptions.None,
            new Dictionary<string, object> { ["1"] = 2, ["2"] = 5 });
        yield return Case("in((2 + 2), [1], [2], 1 + 2, 3)", EvaluateOptions.None,
            new Dictionary<string, object> { ["1"] = 2, ["2"] = 5 });
        yield return Case("in('to' + 'to', 'titi', 'toto')");
        yield return Case("(1604326026000-1604325747000)/60000");

        // Parameters/events scenarios
        yield return Case("if([divider] <> 0, [divided] / [divider], 0)", EvaluateOptions.None,
            new Dictionary<string, object> { ["divider"] = 2, ["divided"] = 2 });
        yield return Case("if([divider] <> 0, [divided] / [divider], 0)", EvaluateOptions.None,
            new Dictionary<string, object> { ["divider"] = 0, ["divided"] = 2 });
        yield return Case("[x]+1", EvaluateOptions.None, new Dictionary<string, object> { ["x"] = 50 });
        yield return Case("[Pi Squared]+1", EvaluateOptions.None, new Dictionary<string, object> { ["Pi Squared"] = 5 });
        yield return Case("x * x", EvaluateOptions.IterateParameters, new Dictionary<string, object> { ["x"] = new[] { 0, 1, 2, 3, 4 } });
        yield return Case("SecretOperation(3, 6)", scenario: "SecretOperation");
        yield return Case("SecretOperation([e], 6) + f", EvaluateOptions.None, new Dictionary<string, object> { ["e"] = 3, ["f"] = 1 }, "SecretOperation");
        yield return Case("Round(Pow(Pi, 2) + Pow([Pi Squared], 2) + [X], 2)", EvaluateOptions.None,
            new Dictionary<string, object> { ["Pi Squared"] = new Expression("Pi * [Pi]"), ["X"] = 10 }, "PiParameter");
        yield return Case("Round(Pow([Pi], 2) + Pow([Pi], 2) + 10, 2)", scenario: "PiParameter");
        yield return Case("if(true, func1(x) + func2(func3(y)), 0)", scenario: "NestedFunctions");
        yield return Case("Round(1.99, 2)", scenario: "OverrideRound");
        yield return Case("SecretOperation(3, 6)", scenario: "NullFunctionResult");
        yield return Case("x", scenario: "NullParameterResult");
        yield return Case("[surface] * h", EvaluateOptions.None,
            new Dictionary<string, object>
            {
                ["surface"] = new Expression("[l] * [L]") { Parameters = { ["l"] = 1, ["L"] = 2 } },
                ["h"] = 3
            });
        yield return Case("([a] != 0) && ([b]/[a]>2)", EvaluateOptions.None, new Dictionary<string, object> { ["a"] = 0 });
        yield return Case("(a > b) + 10", EvaluateOptions.None, new Dictionary<string, object> { ["a"] = 1, ["b"] = 2 });
        yield return Case("x/2", EvaluateOptions.None, new Dictionary<string, object> { ["x"] = 2F });
        yield return Case("x/2", EvaluateOptions.None, new Dictionary<string, object> { ["x"] = 2D });
        yield return Case("x/2", EvaluateOptions.None, new Dictionary<string, object> { ["x"] = 2M });
        yield return Case("a / b * 100", EvaluateOptions.None, new Dictionary<string, object> { ["a"] = 20M, ["b"] = 20M });
        yield return Case("1.8 + Abs([var1])", EvaluateOptions.None, new Dictionary<string, object> { ["var1"] = 9.2 });
        yield return Case("1.8 - Abs([var1])", EvaluateOptions.None, new Dictionary<string, object> { ["var1"] = 0.8 });
        yield return Case("1.8 * Abs([var1])", EvaluateOptions.None, new Dictionary<string, object> { ["var1"] = 9.2 });
        yield return Case("1.8 / Abs([var1])", EvaluateOptions.None, new Dictionary<string, object> { ["var1"] = 0.5 });

        // Error cases
        yield return Case("4. + 2");
        yield return Case("(3 + 2");
        yield return Case("a + b * (");
        yield return Case("+ b ");
        yield return Case("\"0\"");
        yield return Case("Format(\"{0:(###) ###-####}\", \"9999999999\")");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SafeExpression_Should_Be_Compatible(
        string expressionText,
        EvaluateOptions options,
        Dictionary<string, object>? parameters,
        string? scenario)
    {
        var oldExpr = new Expression(expressionText, options);
        var newExpr = new SafeExpression(expressionText, options);

        if (parameters != null)
        {
            foreach (var kv in parameters)
            {
                oldExpr.Parameters[kv.Key] = kv.Value;
                newExpr.Parameters[kv.Key] = kv.Value;
            }
        }

        ApplyScenario(oldExpr, scenario);
        ApplyScenario(newExpr, scenario);

        var oldResult = EvaluateOld(oldExpr);
        var newResult = EvaluateNew(newExpr);

        Assert.Equal(oldResult.Success, newResult.Success);
        if (oldResult.Success)
        {
            Assert.True(AreEquivalent(oldResult.Result, newResult.Result),
                $"Expression: {expressionText}, old={Format(oldResult.Result)}, new={Format(newResult.Result)}");
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(newResult.Error));
        }
    }

    [Fact]
    public void SafeExpression_Should_Be_Compatible_On_Randomized_Arithmetic()
    {
        var random = new Random(12345);
        for (int i = 0; i < 200; i++)
        {
            var expressionText = BuildRandomExpression(random, 3);

            var oldExpr = new Expression(expressionText);
            var newExpr = new SafeExpression(expressionText);

            var oldResult = EvaluateOld(oldExpr);
            var newResult = EvaluateNew(newExpr);

            Assert.Equal(oldResult.Success, newResult.Success);
            if (oldResult.Success)
            {
                Assert.True(AreEquivalent(oldResult.Result, newResult.Result),
                    $"Expression: {expressionText}, old={Format(oldResult.Result)}, new={Format(newResult.Result)}");
            }
        }
    }

    private static object[] Case(
        string expressionText,
        EvaluateOptions options = EvaluateOptions.None,
        Dictionary<string, object>? parameters = null,
        string? scenario = null)
    {
        return new object[] { expressionText, options, parameters, scenario };
    }

    private static string BuildRandomExpression(Random random, int depth)
    {
        if (depth <= 0)
        {
            var v = random.Next(-9, 10);
            return v.ToString();
        }

        var left = BuildRandomExpression(random, depth - 1);
        var right = BuildRandomExpression(random, depth - 1);
        var ops = new[] { "+", "-", "*", "/", "%", "==", "!=", "<", "<=", ">", ">=" };
        var op = ops[random.Next(ops.Length)];
        return "(" + left + " " + op + " " + right + ")";
    }

    private static EvalResult EvaluateOld(Expression expression)
    {
        try
        {
            return EvalResult.Ok(expression.Evaluate());
        }
        catch (Exception ex)
        {
            return EvalResult.Fail(ex.Message);
        }
    }

    private static EvalResult EvaluateNew(SafeExpression expression)
    {
        if (expression.TryEvaluate(out var result, out var error))
        {
            return EvalResult.Ok(result);
        }

        return EvalResult.Fail(error);
    }

    private static void ApplyScenario(Expression expr, string? scenario)
    {
        if (string.IsNullOrEmpty(scenario)) return;

        switch (scenario)
        {
            case "SecretOperation":
                expr.EvaluateFunction += SecretOperation;
                break;
            case "PiParameter":
                expr.EvaluateParameter += PiParameter;
                break;
            case "NestedFunctions":
                expr.EvaluateFunction += NestedFunctions;
                expr.EvaluateParameter += XYZParameters;
                break;
            case "OverrideRound":
                expr.EvaluateFunction += OverrideRound;
                break;
            case "NullFunctionResult":
                expr.EvaluateFunction += NullFunctionResult;
                break;
            case "NullParameterResult":
                expr.EvaluateParameter += NullParameterResult;
                break;
        }
    }

    private static void ApplyScenario(SafeExpression expr, string? scenario)
    {
        if (string.IsNullOrEmpty(scenario)) return;

        switch (scenario)
        {
            case "SecretOperation":
                expr.EvaluateFunction += SecretOperation;
                break;
            case "PiParameter":
                expr.EvaluateParameter += PiParameter;
                break;
            case "NestedFunctions":
                expr.EvaluateFunction += NestedFunctions;
                expr.EvaluateParameter += XYZParameters;
                break;
            case "OverrideRound":
                expr.EvaluateFunction += OverrideRound;
                break;
            case "NullFunctionResult":
                expr.EvaluateFunction += NullFunctionResult;
                break;
            case "NullParameterResult":
                expr.EvaluateParameter += NullParameterResult;
                break;
        }
    }

    private static void SecretOperation(string name, FunctionArgs args)
    {
        if (!string.Equals(name, "SecretOperation", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var a = Convert.ToInt32(args.Parameters[0].Evaluate());
        var b = Convert.ToInt32(args.Parameters[1].Evaluate());
        args.Result = a + b;
    }

    private static void PiParameter(string name, ParameterArgs args)
    {
        if (string.Equals(name, "Pi", StringComparison.OrdinalIgnoreCase))
        {
            args.Result = 3.14;
        }
    }

    private static void NestedFunctions(string name, FunctionArgs args)
    {
        switch (name)
        {
            case "func1": args.Result = 1d; break;
            case "func2": args.Result = 2d * Convert.ToDouble(args.Parameters[0].Evaluate()); break;
            case "func3": args.Result = 3d * Convert.ToDouble(args.Parameters[0].Evaluate()); break;
        }
    }

    private static void XYZParameters(string name, ParameterArgs args)
    {
        switch (name)
        {
            case "x": args.Result = 1; break;
            case "y": args.Result = 2; break;
            case "z": args.Result = 3; break;
        }
    }

    private static void OverrideRound(string name, FunctionArgs args)
    {
        if (string.Equals(name, "Round", StringComparison.OrdinalIgnoreCase))
        {
            args.Result = 3;
        }
    }

    private static void NullFunctionResult(string name, FunctionArgs args)
    {
        if (string.Equals(name, "SecretOperation", StringComparison.OrdinalIgnoreCase))
        {
            args.Result = null;
        }
    }

    private static void NullParameterResult(string name, ParameterArgs args)
    {
        if (string.Equals(name, "x", StringComparison.OrdinalIgnoreCase))
        {
            args.Result = null;
        }
    }

    private static bool AreEquivalent(object? left, object? right)
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        if (IsNumeric(left) && IsNumeric(right))
        {
            try
            {
                var l = Convert.ToDecimal(left);
                var r = Convert.ToDecimal(right);
                return decimal.Abs(l - r) < 0.000001m;
            }
            catch (OverflowException)
            {
                var l = Convert.ToDouble(left);
                var r = Convert.ToDouble(right);
                if (double.IsNaN(l) && double.IsNaN(r)) return true;
                if (double.IsInfinity(l) || double.IsInfinity(r)) return l.Equals(r);
                return Math.Abs(l - r) < 0.000001d;
            }
        }

        if (left is DateTime ldt && right is DateTime rdt)
        {
            return ldt == rdt;
        }

        if (left is string ls && right is string rs)
        {
            return ls == rs;
        }

        if (left is IEnumerable leftEnum && right is IEnumerable rightEnum && left is not string && right is not string)
        {
            var l = ToList(leftEnum);
            var r = ToList(rightEnum);
            if (l.Count != r.Count)
            {
                return false;
            }

            for (int i = 0; i < l.Count; i++)
            {
                if (!AreEquivalent(l[i], r[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return Equals(left, right);
    }

    private static List<object?> ToList(IEnumerable value)
    {
        var list = new List<object?>();
        foreach (var item in value)
        {
            list.Add(item);
        }

        return list;
    }

    private static bool IsNumeric(object value)
    {
        var code = Type.GetTypeCode(value.GetType());
        return code == TypeCode.Byte
               || code == TypeCode.SByte
               || code == TypeCode.UInt16
               || code == TypeCode.UInt32
               || code == TypeCode.UInt64
               || code == TypeCode.Int16
               || code == TypeCode.Int32
               || code == TypeCode.Int64
               || code == TypeCode.Decimal
               || code == TypeCode.Double
               || code == TypeCode.Single;
    }

    private static string Format(object? value) => value == null ? "null" : value.ToString() ?? "null";

    private sealed class EvalResult
    {
        public bool Success { get; private init; }
        public object? Result { get; private init; }
        public string? Error { get; private init; }

        public static EvalResult Ok(object? value) => new EvalResult { Success = true, Result = value };
        public static EvalResult Fail(string? error) => new EvalResult { Success = false, Error = error };
    }
}
