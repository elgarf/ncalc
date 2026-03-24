using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using NCalc.Domain;

namespace NCalc
{
    public class SafeExpression
    {
        public EvaluateOptions Options { get; set; }

        protected string OriginalExpression;

        public SafeExpression(string expression) : this(expression, EvaluateOptions.None)
        {
        }

        public SafeExpression(string expression, EvaluateOptions options)
        {
            Options = options;
            OriginalExpression = expression ?? string.Empty;
            if (string.IsNullOrEmpty(expression))
            {
                Error = "Expression can't be empty";
            }
        }

        public SafeExpression(LogicalExpression expression) : this(expression, EvaluateOptions.None)
        {
        }

        public SafeExpression(LogicalExpression expression, EvaluateOptions options)
        {
            Options = options;
            ParsedExpression = expression;
            if (expression == null)
            {
                Error = "Expression can't be null";
            }
        }

        #region Cache management
        private static bool _cacheEnabled = true;
        private static Dictionary<string, WeakReference> _compiledExpressions = new Dictionary<string, WeakReference>();
        private static readonly ReaderWriterLock Rwl = new ReaderWriterLock();

        public static bool CacheEnabled
        {
            get { return _cacheEnabled; }
            set
            {
                _cacheEnabled = value;
                if (!CacheEnabled)
                {
                    _compiledExpressions = new Dictionary<string, WeakReference>();
                }
            }
        }

        private static void CleanCache()
        {
            var keysToRemove = new List<string>();
            try
            {
                Rwl.AcquireWriterLock(Timeout.Infinite);
                foreach (var de in _compiledExpressions)
                {
                    if (!de.Value.IsAlive)
                    {
                        keysToRemove.Add(de.Key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    _compiledExpressions.Remove(key);
                }
            }
            finally
            {
                Rwl.ReleaseWriterLock();
            }
        }
        #endregion

        public static LogicalExpression Compile(string expression, bool nocache)
        {
            TryCompile(expression, nocache, out var logicalExpression, out _);
            return logicalExpression;
        }

        public static bool TryCompile(string expression, bool nocache, out LogicalExpression logicalExpression, out string error)
        {
            logicalExpression = null;
            error = null;

            if (string.IsNullOrEmpty(expression))
            {
                error = "Expression can't be empty";
                return false;
            }

            if (_cacheEnabled && !nocache)
            {
                try
                {
                    Rwl.AcquireReaderLock(Timeout.Infinite);
                    if (_compiledExpressions.TryGetValue(expression, out var wr))
                    {
                        logicalExpression = wr.Target as LogicalExpression;
                        if (wr.IsAlive && logicalExpression != null)
                        {
                            return true;
                        }
                    }
                }
                finally
                {
                    Rwl.ReleaseReaderLock();
                }
            }

            try
            {
                var parser = new InternalParser(expression);
                logicalExpression = parser.ParseExpression();
                error = parser.Error;

                if (!string.IsNullOrEmpty(error) || logicalExpression == null)
                {
                    logicalExpression = null;
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                logicalExpression = null;
                return false;
            }

            if (_cacheEnabled && !nocache && logicalExpression != null)
            {
                try
                {
                    Rwl.AcquireWriterLock(Timeout.Infinite);
                    _compiledExpressions[expression] = new WeakReference(logicalExpression);
                }
                finally
                {
                    Rwl.ReleaseWriterLock();
                }

                CleanCache();
            }

            return true;
        }

        public bool HasErrors()
        {
            if (!string.IsNullOrEmpty(Error))
            {
                return true;
            }

            if (ParsedExpression != null)
            {
                return false;
            }

            var noCache = (Options & EvaluateOptions.NoCache) == EvaluateOptions.NoCache;
            if (!TryCompile(OriginalExpression, noCache, out var compiled, out var error))
            {
                Error = error;
                return true;
            }

            ParsedExpression = compiled;
            Error = null;
            return false;
        }

        public string Error { get; private set; }

        public LogicalExpression ParsedExpression { get; private set; }

        protected Dictionary<string, IEnumerator> ParameterEnumerators;
        protected Dictionary<string, object> ParametersBackup;

        public object Evaluate()
        {
            TryEvaluate(out var result, out var error);
            Error = error;
            return result;
        }

        public bool TryEvaluate(out object result, out string error)
        {
            result = null;
            error = null;

            try
            {
                if (HasErrors())
                {
                    error = Error;
                    return false;
                }

                var visitor = new EvaluationVisitor(Options);
                visitor.EvaluateFunction += EvaluateFunction;
                visitor.EvaluateParameter += EvaluateParameter;
                visitor.Parameters = Parameters;

                if ((Options & EvaluateOptions.IterateParameters) == EvaluateOptions.IterateParameters)
                {
                    int size = -1;
                    ParametersBackup = new Dictionary<string, object>();
                    foreach (string key in Parameters.Keys)
                    {
                        ParametersBackup[key] = Parameters[key];
                    }

                    ParameterEnumerators = new Dictionary<string, IEnumerator>();

                    foreach (object parameter in Parameters.Values)
                    {
                        if (parameter is IEnumerable enumerable)
                        {
                            int localSize = 0;
                            foreach (var _ in enumerable)
                            {
                                localSize++;
                            }

                            if (size == -1)
                            {
                                size = localSize;
                            }
                            else if (localSize != size)
                            {
                                error = "When IterateParameters option is used, IEnumerable parameters must have the same number of items";
                                return false;
                            }
                        }
                    }

                    foreach (string key in Parameters.Keys)
                    {
                        if (Parameters[key] is IEnumerable enumerable)
                        {
                            ParameterEnumerators[key] = enumerable.GetEnumerator();
                        }
                    }

                    var results = new List<object>();
                    for (int i = 0; i < size; i++)
                    {
                        foreach (string key in ParameterEnumerators.Keys)
                        {
                            var enumerator = ParameterEnumerators[key];
                            enumerator.MoveNext();
                            Parameters[key] = enumerator.Current;
                        }

                        ParsedExpression.Accept(visitor);
                        results.Add(visitor.Result);
                    }

                    result = results;
                    return true;
                }

                ParsedExpression.Accept(visitor);
                result = visitor.Result;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Error = error;
                return false;
            }
        }

        public event EvaluateFunctionHandler EvaluateFunction;
        public event EvaluateParameterHandler EvaluateParameter;

        private Dictionary<string, object> _parameters;

        public Dictionary<string, object> Parameters
        {
            get { return _parameters ?? (_parameters = new Dictionary<string, object>()); }
            set { _parameters = value; }
        }

        private enum TokenKind
        {
            End, Identifier, Integer, Float, String, DateTime, True, False,
            Plus, Minus, Star, Slash, Percent, Bang, Tilde, Amp, Pipe, Caret,
            DoubleAmp, DoublePipe, Eq, DoubleEq, NotEq, AngleNotEq, Lt, Lte, Gt, Gte,
            ShiftLeft, ShiftRight, Question, Colon, Comma, LParen, RParen, And, Or, Not
        }

        private struct Token
        {
            public TokenKind Kind;
            public string Text;
            public int Position;
        }

        private sealed class InternalParser
        {
            private readonly string _input;
            private readonly List<Token> _tokens = new List<Token>();
            private int _index;

            public string Error { get; private set; }

            public InternalParser(string input)
            {
                _input = input ?? string.Empty;
                Tokenize();
            }

            public LogicalExpression ParseExpression()
            {
                if (!string.IsNullOrEmpty(Error)) return null;

                var expr = ParseTernary();
                if (!string.IsNullOrEmpty(Error)) return null;

                if (Current.Kind != TokenKind.End)
                {
                    SetError("Unexpected token '" + Current.Text + "'", Current.Position);
                    return null;
                }

                return expr;
            }

            private Token Current
            {
                get
                {
                    if (_index < _tokens.Count) return _tokens[_index];
                    return new Token { Kind = TokenKind.End, Text = string.Empty, Position = _input.Length };
                }
            }

            private Token Next()
            {
                var current = Current;
                if (_index < _tokens.Count) _index++;
                return current;
            }

            private bool Match(TokenKind kind)
            {
                if (Current.Kind == kind)
                {
                    Next();
                    return true;
                }

                return false;
            }

            private LogicalExpression ParseTernary()
            {
                var left = ParseLogicalOr();
                if (!string.IsNullOrEmpty(Error)) return null;

                if (Match(TokenKind.Question))
                {
                    var middle = ParseTernary();
                    if (!Match(TokenKind.Colon))
                    {
                        SetError("Expected ':'", Current.Position);
                        return null;
                    }

                    var right = ParseTernary();
                    return new TernaryExpression(left, middle, right);
                }

                return left;
            }

            private LogicalExpression ParseLogicalOr()
            {
                var left = ParseLogicalAnd();
                while (Current.Kind == TokenKind.DoublePipe || Current.Kind == TokenKind.Or)
                {
                    Next();
                    var right = ParseLogicalAnd();
                    left = new BinaryExpression(BinaryExpressionType.Or, left, right);
                }

                return left;
            }

            private LogicalExpression ParseLogicalAnd()
            {
                var left = ParseBitwiseOr();
                while (Current.Kind == TokenKind.DoubleAmp || Current.Kind == TokenKind.And)
                {
                    Next();
                    var right = ParseBitwiseOr();
                    left = new BinaryExpression(BinaryExpressionType.And, left, right);
                }

                return left;
            }

            private LogicalExpression ParseBitwiseOr()
            {
                var left = ParseBitwiseXor();
                while (Current.Kind == TokenKind.Pipe)
                {
                    Next();
                    var right = ParseBitwiseXor();
                    left = new BinaryExpression(BinaryExpressionType.BitwiseOr, left, right);
                }

                return left;
            }

            private LogicalExpression ParseBitwiseXor()
            {
                var left = ParseBitwiseAnd();
                while (Current.Kind == TokenKind.Caret)
                {
                    Next();
                    var right = ParseBitwiseAnd();
                    left = new BinaryExpression(BinaryExpressionType.BitwiseXOr, left, right);
                }

                return left;
            }

            private LogicalExpression ParseBitwiseAnd()
            {
                var left = ParseEquality();
                while (Current.Kind == TokenKind.Amp)
                {
                    Next();
                    var right = ParseEquality();
                    left = new BinaryExpression(BinaryExpressionType.BitwiseAnd, left, right);
                }

                return left;
            }

            private LogicalExpression ParseEquality()
            {
                var left = ParseRelational();
                while (Current.Kind == TokenKind.DoubleEq || Current.Kind == TokenKind.Eq || Current.Kind == TokenKind.NotEq || Current.Kind == TokenKind.AngleNotEq)
                {
                    var op = Next().Kind;
                    var right = ParseRelational();
                    var type = (op == TokenKind.NotEq || op == TokenKind.AngleNotEq) ? BinaryExpressionType.NotEqual : BinaryExpressionType.Equal;
                    left = new BinaryExpression(type, left, right);
                }

                return left;
            }

            private LogicalExpression ParseRelational()
            {
                var left = ParseShift();
                while (Current.Kind == TokenKind.Lt || Current.Kind == TokenKind.Lte || Current.Kind == TokenKind.Gt || Current.Kind == TokenKind.Gte)
                {
                    var op = Next().Kind;
                    var right = ParseShift();
                    BinaryExpressionType type;
                    switch (op)
                    {
                        case TokenKind.Lt: type = BinaryExpressionType.Lesser; break;
                        case TokenKind.Lte: type = BinaryExpressionType.LesserOrEqual; break;
                        case TokenKind.Gt: type = BinaryExpressionType.Greater; break;
                        default: type = BinaryExpressionType.GreaterOrEqual; break;
                    }

                    left = new BinaryExpression(type, left, right);
                }

                return left;
            }

            private LogicalExpression ParseShift()
            {
                var left = ParseAdditive();
                while (Current.Kind == TokenKind.ShiftLeft || Current.Kind == TokenKind.ShiftRight)
                {
                    var op = Next().Kind;
                    var right = ParseAdditive();
                    left = new BinaryExpression(op == TokenKind.ShiftLeft ? BinaryExpressionType.LeftShift : BinaryExpressionType.RightShift, left, right);
                }

                return left;
            }

            private LogicalExpression ParseAdditive()
            {
                var left = ParseMultiplicative();
                while (Current.Kind == TokenKind.Plus || Current.Kind == TokenKind.Minus)
                {
                    var op = Next().Kind;
                    var right = ParseMultiplicative();
                    left = new BinaryExpression(op == TokenKind.Plus ? BinaryExpressionType.Plus : BinaryExpressionType.Minus, left, right);
                }

                return left;
            }

            private LogicalExpression ParseMultiplicative()
            {
                var left = ParseUnary();
                while (Current.Kind == TokenKind.Star || Current.Kind == TokenKind.Slash || Current.Kind == TokenKind.Percent)
                {
                    var op = Next().Kind;
                    var right = ParseUnary();
                    BinaryExpressionType type;
                    switch (op)
                    {
                        case TokenKind.Star: type = BinaryExpressionType.Times; break;
                        case TokenKind.Slash: type = BinaryExpressionType.Div; break;
                        default: type = BinaryExpressionType.Modulo; break;
                    }

                    left = new BinaryExpression(type, left, right);
                }

                return left;
            }

            private LogicalExpression ParseUnary()
            {
                if (Current.Kind == TokenKind.Bang || Current.Kind == TokenKind.Not)
                {
                    Next();
                    return new UnaryExpression(UnaryExpressionType.Not, ParseUnary());
                }

                if (Current.Kind == TokenKind.Tilde)
                {
                    Next();
                    return new UnaryExpression(UnaryExpressionType.BitwiseNot, ParseUnary());
                }

                if (Current.Kind == TokenKind.Minus)
                {
                    Next();
                    return new UnaryExpression(UnaryExpressionType.Negate, ParseUnary());
                }

                return ParsePrimary();
            }

            private LogicalExpression ParsePrimary()
            {
                if (Match(TokenKind.LParen))
                {
                    var expr = ParseTernary();
                    if (!Match(TokenKind.RParen))
                    {
                        SetError("Expected ')'", Current.Position);
                        return null;
                    }

                    return expr;
                }

                if (Current.Kind == TokenKind.Integer)
                {
                    var t = Next();
                    if (long.TryParse(t.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lv))
                    {
                        if (lv >= int.MinValue && lv <= int.MaxValue) return new ValueExpression((int)lv);
                        return new ValueExpression(lv);
                    }

                    SetError("Invalid integer literal", t.Position);
                    return null;
                }

                if (Current.Kind == TokenKind.Float)
                {
                    var t = Next();
                    if (double.TryParse(t.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv))
                    {
                        return new ValueExpression(dv);
                    }

                    SetError("Invalid float literal", t.Position);
                    return null;
                }

                if (Current.Kind == TokenKind.String)
                {
                    return new ValueExpression(Next().Text);
                }

                if (Current.Kind == TokenKind.DateTime)
                {
                    var t = Next();
                    if (DateTime.TryParse(t.Text, out var dt))
                    {
                        return new ValueExpression(dt);
                    }

                    SetError("Invalid datetime literal", t.Position);
                    return null;
                }

                if (Current.Kind == TokenKind.True)
                {
                    Next();
                    return new ValueExpression(true);
                }

                if (Current.Kind == TokenKind.False)
                {
                    Next();
                    return new ValueExpression(false);
                }

                if (Current.Kind == TokenKind.Identifier)
                {
                    var name = Next().Text;
                    if (Match(TokenKind.LParen))
                    {
                        var args = new List<LogicalExpression>();
                        if (!Match(TokenKind.RParen))
                        {
                            while (true)
                            {
                                args.Add(ParseTernary());
                                if (Match(TokenKind.RParen)) break;

                                if (!Match(TokenKind.Comma))
                                {
                                    SetError("Expected ',' or ')'", Current.Position);
                                    return null;
                                }
                            }
                        }

                        return new Function(new Identifier(name), args.ToArray());
                    }

                    return new Identifier(name);
                }

                SetError("Unexpected token '" + Current.Text + "'", Current.Position);
                return null;
            }

            private void Tokenize()
            {
                int i = 0;
                while (i < _input.Length)
                {
                    var c = _input[i];
                    if (char.IsWhiteSpace(c))
                    {
                        i++;
                        continue;
                    }

                    if (c == '[')
                    {
                        int start = i++;
                        int nameStart = i;
                        while (i < _input.Length && _input[i] != ']') i++;
                        if (i >= _input.Length)
                        {
                            SetError("Unterminated identifier", start);
                            return;
                        }

                        var name = _input.Substring(nameStart, i - nameStart);
                        i++;
                        _tokens.Add(NewToken(TokenKind.Identifier, name, start));
                        continue;
                    }

                    if (char.IsLetter(c) || c == '_')
                    {
                        int start = i++;
                        while (i < _input.Length && (char.IsLetterOrDigit(_input[i]) || _input[i] == '_')) i++;
                        var text = _input.Substring(start, i - start);
                        switch (text)
                        {
                            case "true": _tokens.Add(NewToken(TokenKind.True, text, start)); break;
                            case "false": _tokens.Add(NewToken(TokenKind.False, text, start)); break;
                            case "and": _tokens.Add(NewToken(TokenKind.And, text, start)); break;
                            case "or": _tokens.Add(NewToken(TokenKind.Or, text, start)); break;
                            case "not": _tokens.Add(NewToken(TokenKind.Not, text, start)); break;
                            default: _tokens.Add(NewToken(TokenKind.Identifier, text, start)); break;
                        }

                        continue;
                    }

                    if (char.IsDigit(c) || (c == '.' && i + 1 < _input.Length && char.IsDigit(_input[i + 1])))
                    {
                        int start = i;
                        bool hasDot = c == '.';
                        bool hasExp = false;
                        bool hasDigit = false;
                        bool hasExpDigit = true;

                        if (c == '.')
                        {
                            i++;
                        }

                        while (i < _input.Length)
                        {
                            var ch = _input[i];
                            if (char.IsDigit(ch))
                            {
                                hasDigit = true;
                                if (hasExp) hasExpDigit = true;
                                i++;
                                continue;
                            }

                            if (ch == '.' && !hasDot && !hasExp)
                            {
                                hasDot = true;
                                i++;
                                continue;
                            }

                            if ((ch == 'e' || ch == 'E') && !hasExp && hasDigit)
                            {
                                hasExp = true;
                                hasExpDigit = false;
                                i++;
                                if (i < _input.Length && (_input[i] == '+' || _input[i] == '-')) i++;
                                continue;
                            }

                            break;
                        }

                        var text = _input.Substring(start, i - start);
                        if (!hasDigit || !hasExpDigit || text.EndsWith(".", StringComparison.Ordinal))
                        {
                            SetError("Invalid numeric literal", start);
                            return;
                        }

                        _tokens.Add(NewToken(hasDot || hasExp ? TokenKind.Float : TokenKind.Integer, text, start));
                        continue;
                    }

                    if (c == '\'')
                    {
                        int start = i++;
                        var chars = new List<char>();
                        bool closed = false;
                        while (i < _input.Length)
                        {
                            var ch = _input[i++];
                            if (ch == '\\' && i < _input.Length)
                            {
                                var esc = _input[i++];
                                switch (esc)
                                {
                                    case 'n': chars.Add('\n'); break;
                                    case 'r': chars.Add('\r'); break;
                                    case 't': chars.Add('\t'); break;
                                    case '\\': chars.Add('\\'); break;
                                    case '\'': chars.Add('\''); break;
                                    case 'u':
                                        if (i + 3 >= _input.Length)
                                        {
                                            SetError("Invalid unicode escape", start);
                                            return;
                                        }

                                        var hex = _input.Substring(i, 4);
                                        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                                        {
                                            SetError("Invalid unicode escape", start);
                                            return;
                                        }

                                        chars.Add((char)code);
                                        i += 4;
                                        break;
                                    default: chars.Add(esc); break;
                                }

                                continue;
                            }

                            if (ch == '\'')
                            {
                                closed = true;
                                break;
                            }

                            chars.Add(ch);
                        }

                        if (!closed)
                        {
                            SetError("Unterminated string literal", start);
                            return;
                        }

                        _tokens.Add(NewToken(TokenKind.String, new string(chars.ToArray()), start));
                        continue;
                    }

                    if (c == '#')
                    {
                        int start = i++;
                        int contentStart = i;
                        while (i < _input.Length && _input[i] != '#') i++;
                        if (i >= _input.Length)
                        {
                            SetError("Unterminated datetime literal", start);
                            return;
                        }

                        var value = _input.Substring(contentStart, i - contentStart);
                        i++;
                        _tokens.Add(NewToken(TokenKind.DateTime, value, start));
                        continue;
                    }

                    if (TryReadOperator(ref i)) continue;

                    SetError("Unexpected character '" + c + "'", i);
                    return;
                }

                _tokens.Add(NewToken(TokenKind.End, string.Empty, _input.Length));
            }

            private bool TryReadOperator(ref int i)
            {
                int start = i;
                if (i + 1 < _input.Length)
                {
                    var two = _input.Substring(i, 2);
                    switch (two)
                    {
                        case "&&": _tokens.Add(NewToken(TokenKind.DoubleAmp, two, start)); i += 2; return true;
                        case "||": _tokens.Add(NewToken(TokenKind.DoublePipe, two, start)); i += 2; return true;
                        case "==": _tokens.Add(NewToken(TokenKind.DoubleEq, two, start)); i += 2; return true;
                        case "!=": _tokens.Add(NewToken(TokenKind.NotEq, two, start)); i += 2; return true;
                        case "<=": _tokens.Add(NewToken(TokenKind.Lte, two, start)); i += 2; return true;
                        case ">=": _tokens.Add(NewToken(TokenKind.Gte, two, start)); i += 2; return true;
                        case "<>": _tokens.Add(NewToken(TokenKind.AngleNotEq, two, start)); i += 2; return true;
                        case "<<": _tokens.Add(NewToken(TokenKind.ShiftLeft, two, start)); i += 2; return true;
                        case ">>": _tokens.Add(NewToken(TokenKind.ShiftRight, two, start)); i += 2; return true;
                    }
                }

                var ch = _input[i++];
                switch (ch)
                {
                    case '+': _tokens.Add(NewToken(TokenKind.Plus, "+", start)); return true;
                    case '-': _tokens.Add(NewToken(TokenKind.Minus, "-", start)); return true;
                    case '*': _tokens.Add(NewToken(TokenKind.Star, "*", start)); return true;
                    case '/': _tokens.Add(NewToken(TokenKind.Slash, "/", start)); return true;
                    case '%': _tokens.Add(NewToken(TokenKind.Percent, "%", start)); return true;
                    case '!': _tokens.Add(NewToken(TokenKind.Bang, "!", start)); return true;
                    case '~': _tokens.Add(NewToken(TokenKind.Tilde, "~", start)); return true;
                    case '&': _tokens.Add(NewToken(TokenKind.Amp, "&", start)); return true;
                    case '|': _tokens.Add(NewToken(TokenKind.Pipe, "|", start)); return true;
                    case '^': _tokens.Add(NewToken(TokenKind.Caret, "^", start)); return true;
                    case '=': _tokens.Add(NewToken(TokenKind.Eq, "=", start)); return true;
                    case '<': _tokens.Add(NewToken(TokenKind.Lt, "<", start)); return true;
                    case '>': _tokens.Add(NewToken(TokenKind.Gt, ">", start)); return true;
                    case '?': _tokens.Add(NewToken(TokenKind.Question, "?", start)); return true;
                    case ':': _tokens.Add(NewToken(TokenKind.Colon, ":", start)); return true;
                    case ',': _tokens.Add(NewToken(TokenKind.Comma, ",", start)); return true;
                    case '(': _tokens.Add(NewToken(TokenKind.LParen, "(", start)); return true;
                    case ')': _tokens.Add(NewToken(TokenKind.RParen, ")", start)); return true;
                    default:
                        i = start;
                        return false;
                }
            }

            private Token NewToken(TokenKind kind, string text, int position)
            {
                return new Token { Kind = kind, Text = text, Position = position };
            }

            private void SetError(string message, int position)
            {
                if (string.IsNullOrEmpty(Error))
                {
                    Error = message + " at position " + position;
                }
            }
        }
    }
}
