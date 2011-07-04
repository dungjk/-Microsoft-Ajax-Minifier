using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.Ajax.Utilities;
using Microsoft.Ajax.Utilities.JavaScript.Nodes;

namespace Microsoft.Ajax.Utilities.JavaScript.Visitors
{
    public class OutputVisitor : TreeVisitor
    {
        private enum BlockMode
        {
            NoBraces = 0,
            Normal,
        }

        private OutputSettings m_settings;

        private TextWriter m_outputStream;
        private char m_lastCharacter;
        private bool m_lastCountOdd;
        private bool m_onNewLine;
        private bool m_forceNewLine;
        private bool m_startOfExpressionStatement;
        private bool m_prependSemicolon;
        private BlockMode m_blockMode;

        private int m_indentLevel;

        // this is a regular expression that we'll use to minimize numeric values
        // that don't employ the e-notation
        private static Regex s_decimalFormat = new Regex(
            @"^\s*\+?(?<neg>\-)?0*(?<mag>(?<sig>\d*[1-9])(?<zer>0*))?(\.(?<man>\d*[1-9])?0*)?(?<exp>E\+?(?<eng>\-?)0*(?<pow>[1-9]\d*))?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static void Output(AstNode node, TextWriter stream, bool prependSemicolon, OutputSettings settings)
        {
            if (node != null)
            {
                // create the visitor, passing in our stream, and apply it to the node
                var visitor = new OutputVisitor(stream, prependSemicolon, settings);
                node.Accept(visitor);
            }
        }

        public static string Output(AstNode node, OutputSettings settings)
        {
            var sb = new StringBuilder();
            if (node != null)
            {
                using (var writer = new StringWriter(sb, CultureInfo.InvariantCulture))
                {
                    // don't prepend semicolon, use default settings
                    var visitor = new OutputVisitor(writer, false, settings);
                    node.Accept(visitor);
                }
            }
            return sb.ToString();
        }

        public static string Output(AstNode node)
        {
            return Output(node, new OutputSettings());
        }

        private OutputVisitor(TextWriter stream, bool prependSemicolon, OutputSettings settings)
        {
            // use what was passed to us
            m_outputStream = stream;
            m_prependSemicolon = prependSemicolon;

            // if we weren't passed any settings, use the default
            m_settings = settings ?? new OutputSettings();

            // we start off on a newline
            m_onNewLine = true;
        }

        #region IVisitor Members

        public override void Visit(ArrayLiteral node)
        {
            if (node != null)
            {
                // wrap the elements (if any) in square-brackets
                Output('[');

                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;
                
                // if we have elements, output them
                if (node.Elements != null)
                {
                    node.Elements.Accept(this);
                }
                Output(']');
            }
        }

        public override void Visit(AspNetBlockNode node)
        {
            if (node != null)
            {
                // just output the block text
                Output(node.BlockText);

                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;
            }
        }

        public override void Visit(AstNodeList node)
        {
            // if the node contains nothing, output nothing
            if (node != null && node.Count > 0)
            {
                // if the nodes are switch cases (if the first one is, all are; if the first isn't, none are)
                // then we DON'T want to separate them with anything. Otherwise we'll be separating with a comma
                var isSwitchCase = node[0] is SwitchCase;
                var needsSeparator = true;
                for (var ndx = 0; ndx < node.Count; ++ndx )
                {
                    if (ndx > 0)
                    {
                        if (isSwitchCase)
                        {
                            // if we need a separator, add it now
                            if (needsSeparator)
                            {
                                Output(';');
                            }

                            // and switch-cases are separated by newlines
                            NewLine();
                        }
                        else
                        {
                            // not a list of switch-cases.
                            // separate with a comma and optional space
                            Output(',');
                            if (m_settings.OperatorSpaces)
                            {
                                Output(' ');
                            }
                        }
                    }

                    // now add the child
                    AcceptNodeWithParens(node[ndx], GetPrecedence(node[ndx]) == OperatorPrecedence.Comma);

                    if (isSwitchCase)
                    {
                        // and see if we might need a separator if there's another item coming up
                        needsSeparator = node[ndx].RequiresSeparator;
                    }
                }
            }
        }

        public override void Visit(BinaryOperator node)
        {
            if (node != null)
            {
                // get our operator precedence
                OperatorPrecedence ourPrecedence = node.OperatorPrecedence;

                // if we have a left-hand side, output it maybe with parens
                if (node.Operand1 != null)
                {
                    AcceptNodeWithParens(node.Operand1, GetPrecedence(node.Operand1) < ourPrecedence);
                }

                // output the operator, maybe with spacing
                if (m_settings.OperatorSpaces) Output(' ');
                Output(JSScanner.GetOperatorString(node.OperatorToken));
                if (m_settings.OperatorSpaces) Output(' ');

                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;
                
                if (node.Operand2 != null)
                {
                    // we will need parentheses around the right hand side if it is a LOWER precendence
                    // than our current operator -- and it can be a little more complicated than a simple comparison.
                    bool rightNeedsParens;

                    // see if the right hand side is another binary expression
                    BinaryOperator binOp = node.Operand2 as BinaryOperator;
                    if (binOp == null)
                    {
                        // it's not -- so just base the decision on the precedence value
                        rightNeedsParens = GetPrecedence(node.Operand2) < ourPrecedence;
                    }
                    else
                    {
                        // they are BOTH binary expressions. This is where it gets complicated.
                        // because most binary tokens (except assignment) are evaluated from left to right,
                        // if we have a binary expression with the same precedence on the RIGHT, then that means the
                        // developer must've put parentheses around it. For some operators, those parentheses 
                        // may not be needed (associative operators like multiply and logical AND or logical OR).
                        // Non-associative operators (divide) will need those parens, so we will want to say they
                        // are a higher relative precedence because of those parentheses.
                        // The plus operator is a special case. It is the same physical token, but it can be two
                        // operations depending on the runtime data: numeric addition or string concatenation.
                        // Because of that ambiguity, let's also calculate the precedence for it as if it were
                        // non-associate as well.
                        // commas never need the parens -- they always evaluate left to right and always return the
                        // right value, so any parens will always be unneccessary.
                        var rightPrecedence = GetPrecedence(node.Operand2);
                        if (ourPrecedence == rightPrecedence
                            && ourPrecedence != OperatorPrecedence.Assignment
                            && ourPrecedence != OperatorPrecedence.Comma)
                        {
                            if (node.OperatorToken == binOp.OperatorToken)
                            {
                                // the tokens are the same and we're not assignment or comma operators.
                                // so for a few associative operators, we're going to say the relative precedence
                                // is the same so unneeded parens are removed. But for all others, we'll say the
                                // right-hand side is a higher precedence so we maintain the sematic structure
                                // of the expression
                                switch (node.OperatorToken)
                                {
                                    case JSToken.Multiply:
                                    case JSToken.BitwiseAnd:
                                    case JSToken.BitwiseXor:
                                    case JSToken.BitwiseOr:
                                    case JSToken.LogicalAnd:
                                    case JSToken.LogicalOr:
                                        // these are the same regardless
                                        rightNeedsParens = false;
                                        break;

                                    // TODO: the plus operator: if we can prove that it is a numeric operator
                                    // or a string operator on BOTH sides, then it can be associative, too. But
                                    // if one side is a string and the other numeric, or if we can't tell at 
                                    // compile-time, then we need to preserve the structural precedence.
                                    default:
                                        // all other operators are structurally a lower precedence when they
                                        // are on the right, so they need to be evaluated first
                                        rightNeedsParens = true;
                                        break;
                                }
                            }
                            else
                            {
                                // they have the same precedence, but the tokens are different.
                                // and the developer had purposely put parens around the right-hand side
                                // to get them on the right (otherwise with the same precedence they
                                // would've ended up on the left. Keep the parens; must've been done for
                                // a purpose.
                                rightNeedsParens = true;
                            }
                        }
                        else
                        {
                            // different precedence -- just base the decision on the relative precedence values
                            rightNeedsParens = rightPrecedence < ourPrecedence;
                        }
                    }

                    // output it
                    AcceptNodeWithParens(node.Operand2, rightNeedsParens);
                }
            }
        }

        public override void Visit(Block node)
        {
            if (node != null && node.Count > 0)
            {
                if (node.Count == 1)
                {
                    // make sure block mode is normal
                    m_blockMode = BlockMode.Normal;
                    m_startOfExpressionStatement = true;

                    node[0].Accept(this);
                }
                else
                {
                    // if the block mode is normal, we want to wrap the statements in braces
                    // and indent them; reset the flag so we don't affect child blocks
                    var braceAndIndent = m_blockMode == BlockMode.Normal;
                    m_blockMode = BlockMode.Normal;

                    if (braceAndIndent)
                    {
                        Output('{');
                        Indent();
                    }
                    NewLine();

                    var mightNeedSemicolon = false;
                    for (var ndx = 0; ndx < node.Count; ++ndx)
                    {
                        // if we need a semicolon, output it now
                        if (mightNeedSemicolon && m_lastCharacter != ';')
                        {
                            Output(';');
                        }
                        NewLine();

                        // we're starting a new statement, so set the flag that will tell any
                        // expression statements we come across that they're at the start
                        m_startOfExpressionStatement = true;

                        // output the statement
                        node[ndx].Accept(this);

                        // see if we MIGHT need a semicolon IF there's a next statement
                        if (m_lastCharacter == ';')
                        {
                            mightNeedSemicolon = false;
                        }
                        else
                        {
                            mightNeedSemicolon = node[ndx].RequiresSeparator;
                        }
                    }

                    if (braceAndIndent)
                    {
                        Unindent();
                        NewLine();
                        Output('}');
                    }
                }
            }
        }

        public override void Visit(BreakStatement node)
        {
            if (node != null)
            {
                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;

                Output("break");
                if (!string.IsNullOrEmpty(node.Label))
                {
                    // if we have an alternate label, use it; otherwise just output
                    // the regular label
                    Output(string.IsNullOrEmpty(node.AlternateLabel)
                        ? node.Label
                        : node.AlternateLabel);
                }
            }
        }

        public override void Visit(CallNode node)
        {
            if (node != null)
            {
                if (node.IsConstructor)
                {
                    Output("new");

                    // no longer the start of an expression statement
                    m_startOfExpressionStatement = false;
                }

                // list of items that DON'T need parens.
                // lookup, call or member don't need parens. All other items
                // are lower-precedence and therefore need to be wrapped in
                // parentheses to keep the right order.
                // function objects take care of their own parentheses.
                CallNode funcCall = node.Function as CallNode;
                bool encloseInParens = !(
                        (node.Function is Lookup)
                    || (node.Function is Member)
                    || (funcCall != null)
                    || (node.Function is ThisLiteral)
                    || (node.Function is FunctionObject));

                // because if the new-operator associates to the right and the ()-operator associates
                // to the left, we need to be careful that we don't change the precedence order when the 
                // function of a new operator is itself a call. In that case, the call will have it's own
                // parameters (and therefore parentheses) that will need to be associated with the call
                // and NOT the new -- the call will need to be surrounded with parens to keep that association.
                if (node.IsConstructor && funcCall != null && !funcCall.InBrackets)
                {
                    encloseInParens = true;
                }

                // if the root is a constructor with no arguments, we'll need to wrap it in parens so the 
                // member-dot comes out with the right precedence.
                // (don't bother checking if we already are already going to use parens)
                if (!encloseInParens && funcCall != null && funcCall.IsConstructor
                    && (funcCall.Arguments == null || funcCall.Arguments.Count == 0))
                {
                    encloseInParens = true;
                }

                // output the function, optionally wrapping in parens
                if (encloseInParens)
                {
                    Output('(');

                    // no longer the start of an expression statement
                    m_startOfExpressionStatement = false;
                }

                node.Function.Accept(this);

                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;
                if (encloseInParens)
                {
                    Output(')');
                }

                // if this isn't a constructor, or if it is and there are parameters,
                // then we want to output the parameters. But if this is a constructor with
                // no parameters, we can skip the whole empty-argument-parens thing altogether.
                if (!node.IsConstructor || (node.Arguments != null && node.Arguments.Count > 0))
                {
                    Output(node.InBrackets ? '[' : '(');
                    if (node.Arguments != null)
                    {
                        node.Arguments.Accept(this);
                    }
                    Output(node.InBrackets ? ']' : ')');
                }
            }
        }

        public override void Visit(ConditionalCompilationComment node)
        {
            if (node != null)
            {
                if (node.Statements != null && node.Statements.Count > 0)
                {
                    Output("/*");

                    // a first conditional compilation statement will take care of the opening @-sign,
                    // so if the first statement is NOT a conditional compilation statement, then we
                    // need to take care of it outselves
                    if (!(node.Statements[0] is ConditionalCompilationStatement))
                    {
                        Output("@ ");
                    }

                    // we don't want braces around these statements
                    m_blockMode = BlockMode.NoBraces;
                    node.Statements.Accept(this);
                    Output("@*/");
                }
            }
        }

        public override void Visit(ConditionalCompilationElse node)
        {
            if (node != null)
            {
                Output("@else");
            }
        }

        public override void Visit(ConditionalCompilationElseIf node)
        {
            if (node != null)
            {
                Output("@elif(");
                if (node.Condition != null)
                {
                    node.Condition.Accept(this);
                }
                Output(')');
            }
        }

        public override void Visit(ConditionalCompilationEnd node)
        {
            if (node != null)
            {
                Output("@end");
            }
        }

        public override void Visit(ConditionalCompilationIf node)
        {
            if (node != null)
            {
                Output("@if(");
                if (node.Condition != null)
                {
                    node.Condition.Accept(this);
                }
                Output(')');
            }
        }

        public override void Visit(ConditionalCompilationOn node)
        {
            if (node != null)
            {
                Output("@cc_on");
            }
        }

        public override void Visit(ConditionalCompilationSet node)
        {
            if (node != null)
            {
                Output("@set@");
                Output(node.VariableName);
                Output('=');

                // if the value is an operator of any kind, we need to wrap it in parentheses
                // so it gets properly parsed
                if (node.Value is BinaryOperator || node.Value is UnaryOperator)
                {
                    Output('(');
                    node.Value.Accept(this);
                    Output(')');
                }
                else if (node.Value != null)
                {
                    node.Value.Accept(this);
                }
            }
        }

        public override void Visit(Conditional node)
        {
            if (node != null)
            {
                // get our precedence
                var ourPrecedence = node.OperatorPrecedence;

                // output the condition, with parens if it's less than our precedence
                if (node.Condition != null)
                {
                    AcceptNodeWithParens(node.Condition, GetPrecedence(node.Condition) < ourPrecedence);
                }

                // we are no longer at the start of an expression statement (if we were before)
                m_startOfExpressionStatement = false;

                if (m_settings.OperatorSpaces)
                {
                    Output(" ? ");
                }
                else
                {
                    Output('?');
                }

                if (node.TrueExpression != null)
                {
                    // the true portion of the conditional is parsed as an assignment expression,
                    // so use the assignment precedence for the comparison to determine whether we
                    // need to wrap it in parens
                    AcceptNodeWithParens(node.TrueExpression, GetPrecedence(node.TrueExpression) < OperatorPrecedence.Assignment);
                }

                if (m_settings.OperatorSpaces)
                {
                    Output(" : ");
                }
                else
                {
                    Output(':');
                }

                if (node.FalseExpression != null)
                {
                    // the true portion of the conditional is parsed as an assignment expression,
                    // so use the assignment precedence for the comparison to determine whether we
                    // need to wrap it in parens
                    AcceptNodeWithParens(node.FalseExpression, GetPrecedence(node.FalseExpression) < OperatorPrecedence.Assignment);
                }
            }
        }

        public override void Visit(ConstantWrapper node)
        {
            if (node != null)
            {
                switch (node.PrimitiveType)
                {
                    case PrimitiveType.Null:
                        // null-literal is easy
                        Output("null");
                        break;

                    case PrimitiveType.Boolean:
                        // boolean-literals are almost as easy as null-literals
                        Output(node.ToBoolean() ? "true" : "false");
                        break;

                    case PrimitiveType.Number:
                        if (Object.ReferenceEquals(node.Context, null) || !m_settings.LeaveLiteralsUnchanged)
                        {
                            // format the numeric-literal
                            Output(NumericToString(node));
                        }
                        else
                        {
                            // we want the literals unchanged, output the source (assuming we have it)
                            Output(node.Context.Code);
                        }
                        break;

                    case PrimitiveType.String:
                        if (Object.ReferenceEquals(node.Context, null) || !m_settings.LeaveLiteralsUnchanged)
                        {
                            // try to get an enclosing lexical environment so we can determine if
                            // we are strict or not. If we can't, assume we are.
                            var lex = node.EnclosingLexicalEnvironment;
                            var isStrict = lex == null ? true : lex.UseStrict;

                            // escape the string and enclose with the appropriate delimiters
                            var str = ConstantWrapper.EscapeString(
                                node.ToString(),
                                node.IsParameterToRegExp,
                                false,
                                isStrict);

                            // if we wnt to make sure all strings are safe for inline, do it now
                            if (m_settings.InlineSafeStrings)
                            {
                                // if there are ANY closing script tags...
                                if (str.IndexOf("</script>", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    // replace all of them with an escaped version so a text-compare won't match
                                    str = str.Replace("</script>", @"<\/script>");
                                }

                                // if there are ANY closing CDATA strings...
                                if (str.IndexOf("]]>", StringComparison.Ordinal) >= 0)
                                {
                                    // replace all of them with an escaped version so a text-compare won't match
                                    str = str.Replace("]]>", @"]\]>");
                                }
                            }

                            Output(str);
                        }
                        else
                        {
                            // output as-is
                            Output(node.Context.Code);
                        }
                        break;

                    case PrimitiveType.Other:
                        // something weird -- shouldn't happen, but just output the context code if there is any
                        if (node.Value != Missing.Value && !Object.ReferenceEquals(node.Context, null))
                        {
                            Output(node.Context.Code);
                        }
                        break;
                }
            }

            // no longer the start of an expression statement
            m_startOfExpressionStatement = false;
        }

        public override void Visit(ConstantWrapperPP node)
        {
            if (node != null)
            {
                if (node.ForceComments)
                {
                    Output("/*");
                }
                Output('@');
                Output(node.VarName);
                if (node.ForceComments)
                {
                    Output("@*/");
                }
            }
        }

        public override void Visit(ContinueStatement node)
        {
            if (node != null)
            {
                Output("continue");
                if (!string.IsNullOrEmpty(node.Label))
                {
                    // if we have an alternate label, use it; otherwise just output
                    // the regular label
                    Output(string.IsNullOrEmpty(node.AlternateLabel)
                        ? node.Label
                        : node.AlternateLabel);
                }

                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;
            }
        }

        public override void Visit(DebuggerStatement node)
        {
            if (node != null)
            {
                Output("debugger");

                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;
            }
        }

        public override void Visit(DeleteNode node)
        {
            if (node != null)
            {
                // we only need parens if the operand is a binary op or a conditional op
                Output(JSScanner.GetOperatorString(JSToken.Delete));

                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;
                
                AcceptNodeWithParens(node.Operand, node.Operand is BinaryOperator || node.Operand is Conditional);
            }
        }

        public override void Visit(DoWhileStatement node)
        {
            if (node != null)
            {
                Output("do");

                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;

                if (node.Body != null && node.Body.Count > 0)
                {
                    if (node.Body.Count == 1)
                    {
                        Indent();
                        NewLine();
                        node.Body.Accept(this);
                        Unindent();

                        // we only output a single statement, with no curly-braces.
                        // if the one statement needs a semicolon, add it now
                        if (node.Body[0].RequiresSeparator)
                        {
                            Output(';');
                        }

                        // because we only output a single line, start the while on 
                        // a new line
                        NewLine();
                    }
                    else
                    {
                        NewLine();
                        node.Body.Accept(this);

                        // if we want operator separators, add a space
                        // between the closing curly-brace and the while
                        // (but not a new-line)
                        if (m_settings.OperatorSpaces)
                        {
                            Output(' ');
                        }
                    }
                }
                else
                {
                    // no body -- just output a semicolon
                    Output(';');
                }

                Output("while(");
                if (node.Condition != null)
                {
                    node.Condition.Accept(this);
                }
                Output(')');
            }
        }

        public override void Visit(ForEachStatement node)
        {
            if (node != null)
            {
                Output("for(");

                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;
                
                if (node.Variable != null)
                {
                    node.Variable.Accept(this);
                }
                Output("in");
                if (node.Collection != null)
                {
                    node.Collection.Accept(this);
                }

                Output(')');

                if (node.Body == null || node.Body.Count == 0)
                {
                    Output(';');
                }
                else if (node.Body.Count == 1)
                {
                    Indent();
                    NewLine();
                    node.Body.Accept(this);
                    Unindent();
                }
                else
                {
                    NewLine();
                    node.Body.Accept(this);
                }
            }
        }

        public override void Visit(ForStatement node)
        {
            if (node != null)
            {
                Output("for(");

                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;

                // initializer
                if (node.Initializer != null)
                {
                    node.Initializer.Accept(this);
                }

                Output(";");
                if (m_settings.OperatorSpaces)
                {
                    Output(' ');
                }
                
                // condition
                if (node.Condition != null)
                {
                    node.Condition.Accept(this);
                }

                Output(';');
                if (m_settings.OperatorSpaces)
                {
                    Output(' ');
                }
                
                // incrementer
                if (node.Incrementer != null)
                {
                    node.Incrementer.Accept(this);
                }

                Output(')');
                if (node.Body == null || node.Body.Count == 0)
                {
                    Output(';');
                }
                else if (node.Body.Count == 1)
                {
                    Indent();
                    NewLine();
                    node.Body.Accept(this);
                    Unindent();
                }
                else
                {
                    NewLine();
                    node.Body.Accept(this);
                }
            }
        }

        public override void Visit(FunctionObject node)
        {
            if (node != null)
            {
                // if this is a function expression at the start of an expression statement,
                // we're going to need to wrap this baby in parentheses
                var needsParens = node.FunctionType == FunctionType.Expression && m_startOfExpressionStatement;
                if (needsParens)
                {
                    Output('(');
                }
                m_startOfExpressionStatement = false;

                // getter and setter start differently that declarations and expressions
                Output(node.FunctionType == FunctionType.Getter
                    ? "get"
                    : node.FunctionType == FunctionType.Setter
                    ? "set"
                    : "function");

                // if there is a name, we need to output it now
                if (!string.IsNullOrEmpty(node.Name))
                {
                    // if there is a binding, use it; otherwise just use the name we were given
                    if (node.Binding != null)
                    {
                        // if the binding is not referenced at all, then don't output it
                        Output(node.Binding.ToString());
                    }
                    else
                    {
                        Output(node.Name);
                    }
                }

                if (node.ParameterDeclarations != null)
                {
                    Output('(');
                    for (var ndx = 0; ndx < node.ParameterDeclarations.Count; ++ndx)
                    {
                        if (ndx > 0)
                        {
                            Output(',');
                            if (m_settings.OperatorSpaces)
                            {
                                Output(' ');
                            }
                        }

                        Output(node.ParameterDeclarations[ndx].Binding != null
                            ? node.ParameterDeclarations[ndx].Binding.ToString()
                            : node.ParameterDeclarations[ndx].Name);
                    }
                    Output(')');
                }

                if (node.Body == null || node.Body.Count == 0)
                {
                    Output("{}");
                }
                else if (node.Body.Count == 1)
                {
                    NewLine();
                    Output('{');
                    Indent();
                    NewLine();
                    node.Body.Accept(this);
                    Unindent();
                    NewLine();
                    Output('}');
                }
                else
                {
                    NewLine();
                    node.Body.Accept(this);
                }

                if (needsParens)
                {
                    Output(')');
                }
            }
        }

        public override void Visit(GetterSetter node)
        {
            if (node != null)
            {
                base.Visit(node);
            }
        }

        public override void Visit(IfStatement node)
        {
            if (node != null)
            {
                Output("if(");

                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;
                if (node.Condition != null)
                {
                    node.Condition.Accept(this);
                }
                Output(')');

                if (node.TrueBlock == null || node.TrueBlock.Count == 0)
                {
                    Output(";");
                }
                else if (node.TrueBlock.Count == 1)
                {
                    // we only have a single statement in the true-branch; normally
                    // we wouldn't wrap that statement in braces. However, if there 
                    // is an else-branch, we need to make sure that single statement 
                    // doesn't end with an if-statement that doesn't have an else-branch
                    // because otherwise OUR else-branch will get associated with that
                    // other if-statement
                    var wrapInBraces = false;
                    if (node.FalseBlock != null)
                    {
                        // check to see if the true-block ends with an if-statement that
                        // has no else-block.
                        wrapInBraces = EndsWithIfNoElseVisitor.Check(node.TrueBlock);
                    }

                    if (!wrapInBraces)
                    {
                        // now, older versions of Safari used to throw a script error if
                        // an true-block of an ifstatment contained a single function declaration
                        // without wrapping it in braces. It doesn't seem like current safari has
                        // that problem anymore. But let's keep doing it for now.
                        var funcObj = node.TrueBlock[0] as FunctionObject;
                        if (funcObj != null && funcObj.FunctionType == FunctionType.Declaration)
                        {
                            wrapInBraces = true;
                        }
                    }

                    if (wrapInBraces)
                    {
                        // output the opening brace and indent for the statement
                        NewLine();
                        Output('{');
                        Indent();
                        NewLine();

                        // output the statement
                        node.TrueBlock.Accept(this);

                        // unindent and output the closing brace
                        Unindent();
                        NewLine();
                        Output('}');
                    }
                    else
                    {
                        // just indent, newline, statement, unindent
                        Indent();
                        NewLine();
                        node.TrueBlock.Accept(this);

                        if (node.FalseBlock != null && node.FalseBlock.Count > 0
                            && node.TrueBlock[0].RequiresSeparator)
                        {
                            // we have only one statement, we did not wrap it in braces,
                            // and we have an else-block, and the one true-statement needs
                            // a semicolon; add it now
                            Output(';');
                        }

                        Unindent();
                    }
                }
                else
                {
                    NewLine();
                    node.TrueBlock.Accept(this);
                }

                if (node.FalseBlock != null && node.FalseBlock.Count > 0)
                {
                    NewLine();
                    Output("else");
                    if (node.FalseBlock.Count == 1)
                    {
                        if (node.FalseBlock[0] is IfStatement)
                        {
                            // this is an else-if construct. Don't newline or indent, just
                            // handle the if-statment directly. 
                            node.FalseBlock.Accept(this);
                        }
                        else
                        {
                            Indent();
                            NewLine();
                            node.FalseBlock.Accept(this);
                            Unindent();
                        }
                    }
                    else
                    {
                        NewLine();
                        node.FalseBlock.Accept(this);
                    }
                }
            }
        }

        public override void Visit(ImportantComment node)
        {
            if (node != null)
            {
                // regular text by default
                string text = node.Text;

                // in single-line format, we want the comment to start on a new line
                if (m_settings.OutputFormat == OutputFormat.SingleLine)
                {
                    // single-line -- replace all CRLF with just LF
                    text = text.Replace("\r\n", "\n") + '\n';

                    // we want the comment to start on a new line, so if we're not
                    // already on a newline, add one to the front now
                    if (!m_onNewLine)
                    {
                        text = '\n' + text;
                    }
                }

                Output(text);

                // in single-line mode we append the \n character to the end, so that
                // means that we are now on a new line
                if (m_settings.OutputFormat == OutputFormat.SingleLine)
                {
                    m_onNewLine = true;
                }
            }
        }

        public override void Visit(LabeledStatement node)
        {
            if (node != null)
            {
                Output(string.IsNullOrEmpty(node.AlternateLabel)
                    ? node.Label
                    : node.AlternateLabel);
                Output(':');

                if (node.Statement != null)
                {
                    node.Statement.Accept(this);
                }
            }
        }

        public override void Visit(Lookup node)
        {
            if (node != null)
            {
                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;

                if (node.Binding != null)
                {
                    Output(node.Binding.ToString());
                }
                else
                {
                    Output(node.Name);
                }
            }
        }

        public override void Visit(Member node)
        {
            if (node != null)
            {
                var ourPrecedence = node.OperatorPrecedence;

                if (node.Root != null)
                {
                    var needsParens = GetPrecedence(node.Root) < ourPrecedence;
                    if (!needsParens)
                    {
                        // new operators will keep grouping until they hit their parentheses. But we don't
                        // show the parens when the aruments are empty. So if our collection is a new operator
                        // with no arguments, then we need to wrap it in parens so that it doesn't start
                        // thinking our dot operator is part of the new constructor.
                        var callRoot = node.Root as CallNode;
                        needsParens = (callRoot != null && callRoot.IsConstructor
                            && (callRoot.Arguments == null || callRoot.Arguments.Count == 0));
                    }
                    if (!needsParens)
                    {
                        // if the right-hand operand is a numeric constant wrapper with no decimal point, 
                        // then we need to wrap in parens to keep the dot-operator from being part of the number
                        var constantRoot = node.Root as ConstantWrapper;
                        if (constantRoot != null && constantRoot.PrimitiveType == PrimitiveType.Number)
                        {
                            // don't need the parens if the number has a decimal point, or if it's hex,
                            // or in exponential format. Only if the number is a decimal integer.
                            needsParens = constantRoot.IsIntegerLiteral;
                        }
                    }

                    AcceptNodeWithParens(node.Root, needsParens);
                }

                Output('.');
                Output(node.Name);
                
                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;
            }
        }

        public override void Visit(NumericUnary node)
        {
            if (node != null)
            {
                Output(JSScanner.GetOperatorString(node.OperatorToken));
                AcceptNodeWithParens(node.Operand, node.Operand is BinaryOperator || node.Operand is Conditional);
            }
        }

        private void OutputObjectLiteralProperty(AstNode key, AstNode value)
        {
            // if the key is a getter/setter, then the
            // value will be the function object and take care of outputting the
            // proper markup; won't have to output the key at all
            if (!(key is GetterSetter))
            {
                // the property name
                if (key != null)
                {
                    key.Accept(this);
                }

                // the colon
                Output(':');
                if (m_settings.OperatorSpaces)
                {
                    Output(' ');
                }
            }

            // the value
            if (value != null)
            {
                value.Accept(this);
            }
        }

        public override void Visit(ObjectLiteral node)
        {
            if (node != null)
            {
                Output('{');

                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;
                
                if (node.Count > 0)
                {
                    // if there's only one property defined in this object literal and
                    // the value is not a function, then don't put the property on its
                    // own line -- output inline
                    if (node.Count == 1
                        && !(node.Values[0] is FunctionObject))
                    {
                        OutputObjectLiteralProperty(node.Keys[0], node.Values[0]);
                    }
                    else
                    {
                        // either more than one property, or the one property is a method.
                        // output all properties on separate lines
                        Indent();
                        NewLine();
                        for (var ndx = 0; ndx < node.Count; ++ndx)
                        {
                            // output the separator if we need it
                            if (ndx > 0)
                            {
                                Output(',');
                                NewLine();
                            }

                            OutputObjectLiteralProperty(node.Keys[ndx], node.Values[ndx]);
                        }
                        Unindent();
                        NewLine();
                    }
                }
                Output('}');
            }
        }

        public override void Visit(ObjectLiteralField node)
        {
            if (node != null)
            {
                var rawValue = node.Value.ToString();

                // if the raw value is safe to be an identifier, then go ahead and ditch the quotes and just output
                // the raw value. Otherwise call ToCode to wrap the string in quotes.
                if(JSScanner.IsSafeIdentifier(rawValue) && !JSScanner.IsKeyword(rawValue, node.EnclosingLexicalEnvironment.UseStrict))
                {
                    Output(rawValue);
                }
                else
                {
                    this.Visit((ConstantWrapper)node);
                }
            }
        }

        public override void Visit(PostOrPrefixOperator node)
        {
            if (node != null)
            {
                var needsParens = node.Operand is BinaryOperator || node.Operand is Conditional;
                switch (node.Operator)
                {
                    case PostOrPrefix.PostfixDecrement:
                        AcceptNodeWithParens(node.Operand, needsParens);
                        Output("--");
                        // no longer the start of an expression statement
                        m_startOfExpressionStatement = false;
                        break;

                    case PostOrPrefix.PostfixIncrement:
                        AcceptNodeWithParens(node.Operand, needsParens);
                        Output("++");
                        // no longer the start of an expression statement
                        m_startOfExpressionStatement = false;
                        break;

                    case PostOrPrefix.PrefixDecrement:
                        Output("--");
                        // no longer the start of an expression statement
                        m_startOfExpressionStatement = false;
                        AcceptNodeWithParens(node.Operand, needsParens);
                        break;

                    case PostOrPrefix.PrefixIncrement:
                        Output("++");
                        // no longer the start of an expression statement
                        m_startOfExpressionStatement = false;
                        AcceptNodeWithParens(node.Operand, needsParens);
                        break;
                }
            }
        }

        public override void Visit(RegExpLiteral node)
        {
            if (node != null)
            {
                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;

                Output('/');
                Output(node.Pattern);
                Output('/');

                if (!string.IsNullOrEmpty(node.PatternSwitches))
                {
                    Output(node.PatternSwitches);
                }
            }
        }

        public override void Visit(ReturnStatement node)
        {
            if (node != null)
            {
                Output("return");
                
                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;

                if (node.Operand != null)
                {
                    node.Operand.Accept(this);
                }
            }
        }

        public override void Visit(SwitchStatement node)
        {
            if (node != null)
            {
                Output("switch(");
                
                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;

                if (node.Expression != null)
                {
                    node.Expression.Accept(this);
                }

                Output(')');
                NewLine();

                Output('{');
                Indent();
                NewLine();

                node.Cases.Accept(this);

                Unindent();
                NewLine();
                Output('}');
            }
        }

        public override void Visit(SwitchCase node)
        {
            if (node != null)
            {
                if (node.CaseValue != null)
                {
                    Output("case");
                    node.CaseValue.Accept(this);
                    Output(':');
                }
                else
                {
                    Output("default:");
                }

                if (node.Statements == null || node.Statements.Count == 0)
                {
                    // no statements, this case will be right above the next case
                    NewLine();
                }
                else
                {
                    // we're going to output our statements with no braces or 
                    // extra indent -- we'll add our own indent
                    Indent();
                    NewLine();

                    m_blockMode = BlockMode.NoBraces;
                    node.Statements.Accept(this);

                    Unindent();
                }
            }
        }

        public override void Visit(ThisLiteral node)
        {
            if (node != null)
            {
                Output("this");

                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;
            }
        }

        public override void Visit(ThrowStatement node)
        {
            if (node != null)
            {
                Output("throw");

                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;
                
                if (node.Operand != null)
                {
                    node.Operand.Accept(this);
                }

                // throw should always end in a semicolon because there are
                // browsers out there that will throw a script error if they
                // don't
                Output(';');
            }
        }

        public override void Visit(TryStatement node)
        {
            if (node != null)
            {
                Output("try");

                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;
                
                if (node.TryBlock == null || node.TryBlock.Count == 0)
                {
                    Output("{}");
                }
                else if (node.TryBlock.Count == 1)
                {
                    NewLine();
                    Output('{');
                    Indent();
                    NewLine();
                    node.TryBlock.Accept(this);
                    Unindent();
                    NewLine();
                    Output('}');
                }
                else
                {
                    NewLine();
                    node.TryBlock.Accept(this);
                }

                if (!string.IsNullOrEmpty(node.CatchVarName))
                {
                    NewLine();
                    Output("catch(");
                    Output(node.CatchBinding != null ? node.CatchBinding.ToString() : node.CatchVarName);
                    Output(')');
                    if (node.CatchBlock == null || node.CatchBlock.Count == 0)
                    {
                        Output("{}");
                    }
                    else if (node.CatchBlock.Count == 1)
                    {
                        NewLine();
                        Output('{');
                        Indent();
                        NewLine();
                        node.CatchBlock.Accept(this);
                        Unindent();
                        NewLine();
                        Output('}');
                    }
                    else
                    {
                        NewLine();
                        node.CatchBlock.Accept(this);
                    }
                }

                if (node.FinallyBlock != null && node.FinallyBlock.Count > 0
                    || string.IsNullOrEmpty(node.CatchVarName))
                {
                    NewLine();
                    Output("finally");
                    if (node.FinallyBlock == null || node.FinallyBlock.Count == 0)
                    {
                        Output("{}");
                    }
                    else if (node.FinallyBlock.Count == 1)
                    {
                        NewLine();
                        Output('{');
                        Indent();
                        NewLine();
                        node.FinallyBlock.Accept(this);
                        Unindent();
                        NewLine();
                        Output('}');
                    }
                    else
                    {
                        NewLine();
                        node.FinallyBlock.Accept(this);
                    }
                }
            }
        }

        public override void Visit(TypeOfNode node)
        {
            if (node != null)
            {
                Output(JSScanner.GetOperatorString(JSToken.TypeOf));

                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;
                
                AcceptNodeWithParens(node.Operand, node.Operand is BinaryOperator || node.Operand is Conditional);
            }
        }

        public override void Visit(VarStatement node)
        {
            if (node != null)
            {
                // always output a space with the "var" keyword because there are no valid
                // reasons why a space wouldn't be there (variables should always start with
                // a character or escape sequence that would cause a space to be inserted)
                Output("var ");
                
                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;

                Indent();

                for (var ndx = 0; ndx < node.Count; ++ndx)
                {
                    if (ndx > 0)
                    {
                        Output(',');
                        NewLine();
                    }
                    node[ndx].Accept(this);
                }

                Unindent();
            }
        }

        public override void Visit(VariableDeclaration node)
        {
            if (node != null)
            {
                Output(node.Binding != null ? node.Binding.ToString() : node.Identifier);

                if (node.Initializer != null)
                {
                    if (node.IsCCSpecialCase)
                    {
                        Output(node.UseCCOn
                            ? "/*@cc_on="
                            : "/*@=");
                    }
                    else if (m_settings.OperatorSpaces)
                    {
                        Output(" = ");
                    }
                    else
                    {
                        Output('=');
                    }

                    AcceptNodeWithParens(node.Initializer, GetPrecedence(node.Initializer) == OperatorPrecedence.Comma);

                    if (node.IsCCSpecialCase)
                    {
                        Output("@*/");
                    }
                }
            }
        }

        public override void Visit(VoidNode node)
        {
            if (node != null)
            {
                Output(JSScanner.GetOperatorString(JSToken.Void));
                
                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;

                AcceptNodeWithParens(node.Operand, node.Operand is BinaryOperator || node.Operand is Conditional);
            }
        }

        public override void Visit(WhileStatement node)
        {
            if (node != null)
            {
                Output("while(");
                
                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;

                if (node.Condition != null)
                {
                    node.Condition.Accept(this);
                }
                Output(')');

                if (node.Body == null || node.Body.Count == 0)
                {
                    Output(';');
                }
                else if (node.Body.Count == 1)
                {
                    Indent();
                    NewLine();
                    node.Body.Accept(this);
                    Unindent();
                }
                else
                {
                    NewLine();
                    node.Body.Accept(this);
                }
            }
        }

        public override void Visit(WithStatement node)
        {
            if (node != null)
            {
                Output("with(");

                // no longer the start of an expression statement
                m_startOfExpressionStatement = false;
                
                if (node.WithObject != null)
                {
                    node.WithObject.Accept(this);
                }
                Output(')');

                if (node.Body == null || node.Body.Count == 0)
                {
                    Output(';');
                }
                else if (node.Body.Count == 1)
                {
                    Indent();
                    NewLine();
                    node.Body.Accept(this);
                    Unindent();
                }
                else
                {
                    NewLine();
                    node.Body.Accept(this);
                }
            }
        }

        #endregion

        #region output methods

        private void Output(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                // if we want to force a newline, do it now
                if (m_forceNewLine)
                {
                    NewLine();
                }
                else if (m_prependSemicolon)
                {
                    m_outputStream.Write(';');
                    m_prependSemicolon = false;
                }

                // insert a space if needed, then the character
                InsertSpaceIfNeeded(text);
                m_outputStream.Write(text);

                // just assume this wasn't itself a newline
                m_onNewLine = false;

                // now set the "last character" state
                SetLastCharState(text);
            }
        }

        private void Output(char ch)
        {
            // if we want to force a newline, do it now
            if (m_forceNewLine)
            {
                NewLine();
            }
            else if (m_prependSemicolon)
            {
                m_outputStream.Write(';');
                m_prependSemicolon = false;
            }

            // insert a space if needed, then the character
            InsertSpaceIfNeeded(ch);
            m_outputStream.Write(ch);

            // determine if this was a newline character
            m_onNewLine = (ch == '\n' || ch == '\r');

            // now set the "last character" state
            SetLastCharState(ch);
        }

        private void InsertSpaceIfNeeded(char ch)
        {
            // if the current character is a + or - and the last character was the same....
            if ((ch == '+' || ch == '-') && m_lastCharacter == ch)
            {
                // if we want to put a + or a - in the stream, and the previous character was
                // an odd number of the same, then we need to add a space so it doesn't
                // get read as ++ (or --)
                if (m_lastCountOdd)
                {
                    m_outputStream.Write(' ');
                }
            }
            else if (JSScanner.IsValidIdentifierPart(m_lastCharacter) && JSScanner.IsValidIdentifierPart(ch))
            {
                // either the last character is a valid part of an identifier and the current character is, too;
                // OR the last part was numeric and the current character is a .
                // we need to separate those with spaces as well
                m_outputStream.Write(' ');
            }
        }

        private void InsertSpaceIfNeeded(string text)
        {
            // if the current character is a + or - and the last character was the same....
            var ch = text[0];
            if ((ch == '+' || ch == '-') && m_lastCharacter == ch)
            {
                // if we want to put a + or a - in the stream, and the previous character was
                // an odd number of the same, then we need to add a space so it doesn't
                // get read as ++ (or --)
                if (m_lastCountOdd)
                {
                    m_outputStream.Write(' ');
                }
            }
            else if (JSScanner.IsValidIdentifierPart(m_lastCharacter) && JSScanner.StartsWithIdentifierPart(text))
            {
                // either the last character is a valid part of an identifier and the current character is, too;
                // OR the last part was numeric and the current character is a .
                // we need to separate those with spaces as well
                m_outputStream.Write(' ');
            }
        }

        private void SetLastCharState(char ch)
        {
            // if it's a + or a -, we need to adjust the odd state
            if (ch == '+' || ch == '-')
            {
                if (ch == m_lastCharacter)
                {
                    // same as the last string -- so we're adding one to it.
                    // if it was odd before, it's now even; if it was even before,
                    // it's now odd
                    m_lastCountOdd = !m_lastCountOdd;
                }
                else
                {
                    // not the same as last time, so this is a string of 1
                    // characters, which is odd
                    m_lastCountOdd = true;
                }
            }
            else
            {
                // neither + nor -; reset the odd state
                m_lastCountOdd = false;
            }

            m_lastCharacter = ch;
        }

        private void SetLastCharState(string text)
        {
            // ignore empty strings
            if (!string.IsNullOrEmpty(text))
            {
                // get the last character
                char lastChar = text[text.Length - 1];

                // if it's not a plus or a minus, we don't care
                if (lastChar == '+' || lastChar == '-')
                {
                    // see HOW MANY of those characters were at the end of the string
                    var ndxDifferent = text.Length - 1;
                    while (--ndxDifferent >= 0)
                    {
                        if (text[ndxDifferent] != lastChar)
                        {
                            break;
                        }
                    }

                    // if the first diff index is less than zero, then the whole string is one of
                    // these two special characters
                    if (ndxDifferent < 0 && m_lastCharacter == lastChar)
                    {
                        // the whole string is the same character, AND it's the same character 
                        // at the end of the last time we output stuff. We need to take into 
                        // account the previous state when we set the current state.
                        // it's a logical XOR -- if the two values are the same, m_lastCountOdd is false;
                        // it they are different, m_lastCountOdd is true.
                        m_lastCountOdd = (text.Length % 2 == 1) ^ m_lastCountOdd;
                    }
                    else
                    {
                        // either the whole string wasn't the same character, OR the previous ending
                        // wasn't the same character. Either way, the current state is determined 
                        // exclusively by the number of characters we found at the end of this string
                        // get the number of same characters ending this string, mod by 2, and if the
                        // result is 1, it's an odd number of characters.
                        m_lastCountOdd = (text.Length - 1 - ndxDifferent) % 2 == 1;
                    }
                }
                else
                {
                    // say we weren't odd
                    m_lastCountOdd = false;
                }

                // save the last character for next time
                m_lastCharacter = lastChar;
            }
        }

        private void Indent()
        {
            ++m_indentLevel;
        }

        private void Unindent()
        {
            --m_indentLevel;
        }

        private void NewLine()
        {
            if (m_settings.OutputFormat == OutputFormat.MultipleLines && !m_onNewLine)
            {
                // since we're about to output a newline, we can reset this flag
                m_forceNewLine = false;

                // output the newline character
                m_outputStream.WriteLine();

                // if the indent level is greqater than zero, output the indent spaces
                if (m_indentLevel > 0)
                {
                    var numSpaces = m_indentLevel * m_settings.IndentSpaces;
                    while (numSpaces-- > 0)
                    {
                        m_outputStream.Write(' ');
                    }
                }

                // say our last character was a space
                m_lastCharacter = ' ';

                // we just output a newline
                m_onNewLine = true;
            }
        }

        #endregion

        #region Helper methods

        private static OperatorPrecedence GetPrecedence(AstNode node)
        {
            var expression = node as Expression;
            return expression == null ? OperatorPrecedence.Primary : expression.OperatorPrecedence;
        }

        private void AcceptNodeWithParens(AstNode node, bool needsParens)
        {
            // if we need parentheses, add the opening
            if (needsParens)
            {
                Output('(');
            }
            
            // now output the node
            node.Accept(this);

            // if we need parentheses, add the closing
            if (needsParens)
            {
                Output(')');
            }
        }

        #endregion

        #region numeric formatting methods

        private static string NumericToString(ConstantWrapper constant)
        {
            // numerics are doubles in JavaScript, so force it now as a shortcut
            var doubleValue = constant.ToNumber();
            if (double.IsNaN(doubleValue) || double.IsInfinity(doubleValue))
            {
                // weird number -- just return the uncrunched source code as-is. 
                // we've should have already thrown an error alerting the developer 
                // to the overflow mistake, and it might alter the code to change the value
                if (!Object.ReferenceEquals(constant.Context, null) && !string.IsNullOrEmpty(constant.Context.Code)
                    && string.CompareOrdinal(constant.Context.Code, "[generated code]") != 0)
                {
                    return constant.Context.Code;
                }

                // Hmmm... don't have a context source. 
                // Must be generated. Just generate the proper JS literal.
                //
                // DANGER! If we just output NaN and Infinity and -Infinity blindly, that assumes
                // that there aren't any local variables in this scope chain with that
                // name, and we're pulling the GLOBAL properties. Might want to use properties
                // on the Number object -- which, of course, assumes that Number doesn't
                // resolve to a local variable...
                string objectName = double.IsNaN(doubleValue) ? "NaN" : "Infinity";

                // get the enclosing lexical environment
                var enclosingScope = constant.EnclosingLexicalEnvironment;
                if (enclosingScope != null)
                {
                    var reference = enclosingScope.GetIdentifierReference(objectName, null);
                    if (reference.Category != BindingCategory.Predefined)
                    {
                        // NaN/Infinity didn't resolve to the global predefined values!
                        // see if Number does
                        reference = enclosingScope.GetIdentifierReference("Number", null);
                        if (reference.Category == BindingCategory.Predefined)
                        {
                            // use the properties off this object. Not very compact, but accurate.
                            // I don't think there will be any precedence problems with these constructs --
                            // the member-dot operator is pretty high on the precedence scale.
                            if (double.IsPositiveInfinity(doubleValue))
                            {
                                return "Number.POSITIVE_INFINITY";
                            }
                            if (double.IsNegativeInfinity(doubleValue))
                            {
                                return "Number.NEGATIVE_INFINITY";
                            }
                            return "Number.NaN";
                        }
                        else
                        {
                            // that doesn't resolve to the global Number object, either!
                            // well, extreme circumstances. Let's use literals to generate those values.
                            if (double.IsPositiveInfinity(doubleValue))
                            {
                                // 1 divided by zero is +Infinity
                                return "(1/0)";
                            }
                            if (double.IsNegativeInfinity(doubleValue))
                            {
                                // 1 divided by negative zero is -Infinity
                                return "(1/-0)";
                            }
                            // the unary plus converts to a number, and "x" will generate NaN
                            return "(+'x')";
                        }
                    }
                }

                // we're good to go -- just return the name because it will resolve to the
                // global properties (make a special case for negative infinity)
                return double.IsNegativeInfinity(doubleValue) ? "-Infinity" : objectName;
            }
            else if (doubleValue == 0)
            {
                // special case zero because we don't need to go through all those
                // gyrations to get a "0" -- and because negative zero is different
                // than a positive zero
                return constant.IsNegativeZero ? "-0" : "0";
            }
            else
            {
                // normal string representations
                string normal = GetSmallestRep(doubleValue.ToString("R", CultureInfo.InvariantCulture));

                // if this is an integer (no decimal portion)....
                if (Math.Floor(doubleValue) == doubleValue)
                {
                    // then convert to hex and see if it's smaller.
                    // only really big numbers might be smaller in hex.
                    string hex = NormalOrHexIfSmaller(doubleValue, normal);
                    if (hex.Length < normal.Length)
                    {
                        normal = hex;
                    }
                }
                return normal;
            }
        }

        private static string GetSmallestRep(string number)
        {
            Match match = s_decimalFormat.Match(number);
            if (match.Success)
            {
                string mantissa = match.Result("${man}");
                if (string.IsNullOrEmpty(match.Result("${exp}")))
                {
                    if (string.IsNullOrEmpty(mantissa))
                    {
                        // no decimal portion
                        if (string.IsNullOrEmpty(match.Result("${sig}")))
                        {
                            // no non-zero digits in the magnitude either -- must be a zero
                            number = match.Result("${neg}") + "0";
                        }
                        else
                        {
                            // see if there are trailing zeros
                            // that we can use e-notation to make smaller
                            int numZeros = match.Result("${zer}").Length;
                            if (numZeros > 2)
                            {
                                number = match.Result("${neg}") + match.Result("${sig}")
                                    + 'e' + numZeros.ToString(CultureInfo.InvariantCulture);
                            }
                        }
                    }
                    else
                    {
                        // there is a decimal portion. Put it back together
                        // with the bare-minimum stuff -- no plus-sign, no leading magnitude zeros,
                        // no trailing mantissa zeros. A zero magnitude won't show up, either.
                        number = match.Result("${neg}") + match.Result("${mag}") + '.' + mantissa;
                    }
                }
                else if (string.IsNullOrEmpty(mantissa))
                {
                    // there is an exponent, but no significant mantissa
                    number = match.Result("${neg}") + match.Result("${mag}")
                        + "e" + match.Result("${eng}") + match.Result("${pow}");
                }
                else
                {
                    // there is an exponent and a significant mantissa
                    // we want to see if we can eliminate it and save some bytes

                    // get the integer value of the exponent
                    int exponent;
                    if (int.TryParse(match.Result("${eng}") + match.Result("${pow}"), NumberStyles.Integer, CultureInfo.InvariantCulture, out exponent))
                    {
                        // slap the mantissa directly to the magnitude without a decimal point.
                        // we'll subtract the number of characters we just added to the magnitude from
                        // the exponent
                        number = match.Result("${neg}") + match.Result("${mag}") + mantissa
                            + 'e' + (exponent - mantissa.Length).ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        // should n't get here, but it we do, go with what we have
                        number = match.Result("${neg}") + match.Result("${mag}") + '.' + mantissa
                            + 'e' + match.Result("${eng}") + match.Result("${pow}");
                    }
                }
            }
            return number;
        }

        private static string NormalOrHexIfSmaller(double doubleValue, string normal)
        {
            // keep track of the maximum number of characters we can have in our
            // hexadecimal number before it'd be longer than the normal version.
            // subtract two characters for the 0x
            int maxValue = normal.Length - 2;

            int sign = Math.Sign(doubleValue);
            if (sign < 0)
            {
                // negate the value so it's positive
                doubleValue = -doubleValue;
                // subtract another character for the minus sign
                --maxValue;
            }

            // we don't want to get larger -- or even the same size, so we know
            // the maximum length is the length of the normal string less one
            char[] charArray = new char[normal.Length - 1];
            // point PAST the last character in the array because we will decrement
            // the position before we add a character. that way position will always
            // point to the first valid character in the array.
            int position = charArray.Length;

            while (maxValue > 0 && doubleValue > 0)
            {
                // get the right-most hex character
                int digit = (int)(doubleValue % 16);

                // if the digit is less than ten, then we want to add it to '0' to get the decimal character.
                // otherwise we want to add (digit - 10) to 'a' to get the alphabetic hex digit
                charArray[--position] = (char)((digit < 10 ? '0' : 'a' - 10) + digit);

                // next character
                doubleValue = Math.Floor(doubleValue / 16);
                --maxValue;
            }

            // if the max value is still greater than zero, then the hex value
            // will be shorter than the normal value and we want to go with it
            if (maxValue > 0)
            {
                // add the 0x prefix
                charArray[--position] = 'x';
                charArray[--position] = '0';

                // add the sign if negative
                if (sign < 0)
                {
                    charArray[--position] = '-';
                }

                // create a new string starting at the current position
                normal = new string(charArray, position, charArray.Length - position);
            }
            return normal;
        }
        
        #endregion
    }
}
